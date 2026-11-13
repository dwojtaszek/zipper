#!/usr/bin/env python3
"""Tests for the SonarCloud PR gate in runner.py."""
import http.client
import json
import sys
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import patch

RUNNER_BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNNER_BASE))

import runner  # noqa: E402


class SonarCloudGateTests(unittest.TestCase):
    def test_clean_issues_and_ok_gate_pass(self):
        with patch.object(
            runner,
            "_sonarcloud_get_json",
            side_effect=[
                {"issues": []},
                {"projectStatus": {"status": "OK", "conditions": []}},
            ],
        ):
            result = runner._check_sonarcloud_pr_gate(1438)

        self.assertTrue(result.ok)
        self.assertFalse(result.environmental)

    def test_open_blocking_issue_fails_non_environmentally(self):
        with patch.object(
            runner,
            "_sonarcloud_get_json",
            side_effect=[
                {
                    "issues": [
                        {
                            "severity": "CRITICAL",
                            "rule": "csharpsquid:S3776",
                            "component": "dwojtaszek_zipper:src/Validation/ProductionSetPostValidator.cs",
                            "line": 670,
                            "message": "Refactor this method to reduce its Cognitive Complexity.",
                        }
                    ]
                },
                {"projectStatus": {"status": "OK", "conditions": []}},
            ],
        ):
            result = runner._check_sonarcloud_pr_gate(1438)

        self.assertFalse(result.ok)
        self.assertFalse(result.environmental)
        self.assertIn("CRITICAL csharpsquid:S3776", result.detail)
        self.assertIn("ProductionSetPostValidator.cs:670", result.detail)

    def test_quality_gate_error_without_issues_still_fails(self):
        with patch.object(
            runner,
            "_sonarcloud_get_json",
            side_effect=[
                {"issues": []},
                {
                    "projectStatus": {
                        "status": "ERROR",
                        "conditions": [
                            {
                                "status": "ERROR",
                                "metricKey": "new_duplicated_lines_density",
                                "actualValue": "4.5",
                                "errorThreshold": "3",
                            }
                        ],
                    }
                },
            ],
        ):
            result = runner._check_sonarcloud_pr_gate(1438)

        self.assertFalse(result.ok)
        self.assertFalse(result.environmental)
        self.assertIn("new_duplicated_lines_density=4.5", result.detail)

    def test_network_or_payload_error_is_environmental(self):
        with patch.object(
            runner,
            "_sonarcloud_get_json",
            side_effect=urllib.error.URLError("timeout"),
        ):
            result = runner._check_sonarcloud_pr_gate(1438)

        self.assertFalse(result.ok)
        self.assertTrue(result.environmental)

        for bad_payloads in (
            [None, {"projectStatus": {"status": "OK"}}],
            [[], {"projectStatus": {"status": "OK"}}],
            [{"issues": []}, None],
            [{"issues": [None]}, {"projectStatus": {"status": "OK"}}],
            [{"issues": []}, {"projectStatus": []}],
            [{"unexpected": []}, {"projectStatus": {"status": "OK"}}],
        ):
            with self.subTest(bad_payloads=bad_payloads), patch.object(
                runner,
                "_sonarcloud_get_json",
                side_effect=bad_payloads,
            ):
                malformed = runner._check_sonarcloud_pr_gate(1438)
            self.assertFalse(malformed.ok)
            self.assertTrue(malformed.environmental)

    def test_absent_or_skipped_sonarcloud_rollup_skips_api_query(self):
        with patch.object(runner, "_sonarcloud_get_json", side_effect=AssertionError("should not query")):
            absent = runner._check_sonarcloud_pr_gate(1438, rollup=[
                {"__typename": "CheckRun", "name": "ci-gate", "status": "COMPLETED", "conclusion": "SUCCESS"},
            ])
            skipped = runner._check_sonarcloud_pr_gate(1438, rollup=[
                {"__typename": "CheckRun", "name": "SonarCloud Code Analysis", "status": "COMPLETED", "conclusion": "SKIPPED"},
            ])

        self.assertTrue(absent.ok)
        self.assertTrue(skipped.ok)

    def test_completed_sonarcloud_rollup_queries_api_and_blocks_major_issue(self):
        checkrun_rollup = [
            {"__typename": "CheckRun", "name": "SonarCloud Code Analysis", "status": "COMPLETED", "conclusion": "SUCCESS"},
        ]
        status_context_rollup = [
            {"__typename": "StatusContext", "context": "SonarCloud Code Analysis", "state": "SUCCESS"},
        ]
        for rollup in (checkrun_rollup, status_context_rollup):
            with self.subTest(rollup=rollup), patch.object(
                runner,
                "_sonarcloud_get_json",
                side_effect=[
                    {"issues": [{"severity": "MAJOR", "rule": "csharpsquid:S100", "component": "x:src/A.cs", "line": 12, "message": "Fix"}]},
                    {"projectStatus": {"status": "OK", "conditions": []}},
                ],
            ):
                result = runner._check_sonarcloud_pr_gate(1438, rollup=rollup)

            self.assertFalse(result.ok)
            self.assertFalse(result.environmental)
            self.assertIn("MAJOR csharpsquid:S100", result.detail)

    def test_merge_authorized_pr_rechecks_sonarcloud_against_fresh_rollup(self):
        fresh_pr = json.dumps({
            "headRefOid": "a" * 40,
            "statusCheckRollup": [
                {"__typename": "CheckRun", "name": "ci-gate", "status": "COMPLETED", "conclusion": "SUCCESS"},
                {"__typename": "CheckRun", "name": "SonarCloud Code Analysis", "status": "COMPLETED", "conclusion": "SUCCESS"},
            ],
        })
        with patch.object(runner, "_is_coderabbit_gate_failed", return_value=False), \
             patch.object(runner, "_authorized_pr", return_value={"head": {"sha": "a" * 40}}), \
             patch.object(runner, "run_cmd", return_value=(0, fresh_pr, "")) as mock_cmd, \
             patch.object(
                 runner,
                 "_sonarcloud_get_json",
                 side_effect=[
                     {"issues": [{"severity": "BLOCKER", "rule": "csharpsquid:S999", "component": "x:src/B.cs", "line": 3, "message": "Bug"}]},
                     {"projectStatus": {"status": "OK", "conditions": []}},
                 ],
             ):
            code, _, err, rollup = runner._merge_authorized_pr(1438, "1432", "a" * 40)

        self.assertEqual(code, 1)
        self.assertIn("BLOCKER csharpsquid:S999", err)
        self.assertIsNotNone(rollup)
        self.assertEqual(mock_cmd.call_count, 1)
        self.assertEqual(runner._merge_refusal_blocker(1438, err, rollup), err)

    def test_merge_authorized_pr_treats_sonarcloud_http_exception_as_environmental(self):
        fresh_pr = json.dumps({
            "headRefOid": "a" * 40,
            "statusCheckRollup": [
                {"__typename": "CheckRun", "name": "SonarCloud Code Analysis", "status": "COMPLETED", "conclusion": "SUCCESS"},
            ],
        })
        with patch.object(runner, "_is_coderabbit_gate_failed", return_value=False), \
             patch.object(runner, "_authorized_pr", return_value={"head": {"sha": "a" * 40}}), \
             patch.object(runner, "run_cmd", return_value=(0, fresh_pr, "")), \
             patch.object(runner, "_sonarcloud_get_json", side_effect=http.client.IncompleteRead(b"partial")):
            code, _, err, rollup = runner._merge_authorized_pr(1438, "1432", "a" * 40)

        self.assertEqual(code, 1)
        self.assertIn("SonarCloud", err)
        self.assertIsNone(rollup)
        self.assertEqual(runner._merge_refusal_blocker(1438, err, rollup), "")

    def test_coverage_only_quality_gate_error_without_issues_passes(self):
        with patch.object(
            runner,
            "_sonarcloud_get_json",
            side_effect=[
                {"issues": []},
                {
                    "projectStatus": {
                        "status": "ERROR",
                        "conditions": [
                            {
                                "status": "ERROR",
                                "metricKey": "new_coverage",
                                "actualValue": "60.0",
                                "errorThreshold": "80",
                            }
                        ],
                    }
                },
            ],
        ):
            result = runner._check_sonarcloud_pr_gate(1438)

        self.assertTrue(result.ok)
        self.assertFalse(result.environmental)


if __name__ == "__main__":
    unittest.main()
