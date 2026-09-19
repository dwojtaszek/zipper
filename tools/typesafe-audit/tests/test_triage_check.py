"""Tests for the TypeSafe issue triage check (#960, dry-run)."""

from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOL_DIR = REPO_ROOT / "tools" / "typesafe-audit"
TRIAGE_DIR = TOOL_DIR / "triage"
CORPUS = REPO_ROOT / "tests" / "typesafe-audit-fixtures" / "triage"


def load_module(name: str, path: Path):
    # Explicit-path loading avoids module-name collisions between checks.
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class CollectorTests(unittest.TestCase):
    """Deterministic preparation: markup stripping, keywords, candidate search."""

    @classmethod
    def setUpClass(cls):
        cls.collect = load_module("triage_collect", TRIAGE_DIR / "collect.py")

    def test_markup_stripped_and_size_capped(self):
        text = "# Heading\n\n**bold** `code` [link](https://x)\n" + "x" * 5000
        stripped = self.collect.strip_markup(text, max_chars=200)
        self.assertNotIn("**", stripped)
        self.assertNotIn("[link]", stripped)
        self.assertLessEqual(len(stripped), 200)

    def test_keywords_include_req_ids_and_paths(self):
        keywords = self.collect.extract_keywords("Fix REQ-171 handling of src/LoadFiles/DatComposer.cs paths")
        self.assertIn("REQ-171", keywords)
        self.assertIn("src/LoadFiles/DatComposer.cs", keywords)

    def test_duplicate_candidate_search_ranks_keyword_overlap(self):
        corpus = [
            {"number": 1, "title": "Archive extraction ignores directory entries", "body": "zip directory paths"},
            {"number": 2, "title": "Docs drift on compression table", "body": "readme compression"},
        ]
        issue = {"number": 9, "title": "Archive extraction ignores directory entries", "body": "zip directory paths lost"}
        candidates = self.collect.search_candidates(issue, corpus, max_candidates=2)
        self.assertEqual(candidates[0]["number"], 1)

    def test_allowed_sets_come_from_policy_and_requirements(self):
        allowed = self.collect.allowed_sets(
            {"subsystems": ["archive", "cli"], "labels": ["bug", "P1"]},
            ["REQ-171", "REQ-178"],
        )
        self.assertIn("archive", allowed["subsystems"])
        self.assertIn("REQ-171", allowed["req_ids"])


class RunCheckTests(unittest.TestCase):
    """Dry-run judgments over recorded fixtures; closed-set enforcement."""

    @classmethod
    def setUpClass(cls):
        cls.run_check = load_module("triage_run_check", TRIAGE_DIR / "run_check.py")

    def _record(self, fixture_dir: Path):
        subprocess.run(
            [sys.executable, str(TRIAGE_DIR / "record_corpus_fixtures.py"), str(fixture_dir)],
            check=True,
        )

    def test_corpus_labels_reproduced(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            self.run_check.main([
                "--corpus", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            report = json.loads((tmp / "r.json").read_text())
            expected = {
                e["issue"]: e["expected"] for e in json.loads((CORPUS / "expected_labels.json").read_text())["examples"]
            }
            by_issue = {r["issue"]: r for r in report["results"]}
            for issue, want in expected.items():
                for field, value in want.items():
                    self.assertEqual(by_issue[issue]["judgments"][field]["value"], value, f"issue {issue} {field}")

    def test_selections_constrained_to_allowed_sets(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            self.run_check.main([
                "--corpus", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            report = json.loads((tmp / "r.json").read_text())
            policy = json.loads((TRIAGE_DIR / "policy.json").read_text())
            for result in report["results"]:
                self.assertIn(result["judgments"]["type"]["value"], policy["types"] + ["unclear"])
                self.assertIn(result["judgments"]["subsystem"]["value"], policy["subsystems"])
                self.assertIn(result["judgments"]["priority"]["value"], policy["priorities"] + ["insufficient-evidence"])

    def test_duplicate_links_only_to_retrieved_candidates(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            self.run_check.main([
                "--corpus", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            report = json.loads((tmp / "r.json").read_text())
            for result in report["results"]:
                for dup in result["judgments"].get("duplicates", []):
                    if dup["value"] == "duplicate":
                        self.assertTrue(dup["candidate_url"])

    def test_report_is_byte_stable(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            args = [
                "--corpus", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ]
            self.run_check.main(args)
            first = (tmp / "r.json").read_bytes()
            (tmp / "r.json").unlink()
            self.run_check.main(args)
            self.assertEqual(first, (tmp / "r.json").read_bytes())

    def test_out_of_set_selections_and_low_confidence_demoted(self):
        # Regression (review MAJOR 1/2): out-of-set type/subsystem/priority and
        # below-threshold confidence must be demoted to needs-human-review
        # with a None value, never reported as judged.
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            case = collect_module = json.loads((CORPUS / "issues" / "bug-report.json").read_text())
            corpus = json.loads((CORPUS / "open-issues.json").read_text())["issues"]
            with mock.patch.object(
                self.run_check.runner, "run_fixture",
                return_value={"answers": {
                    self.run_check.question_key("type"): {"type": "choice", "choice": "banana", "confidence": 0.9},
                    self.run_check.question_key("subsystem"): {"type": "choice", "choice": "does-not-exist", "confidence": 0.9},
                    self.run_check.question_key("priority"): {"type": "choice", "choice": "P0", "confidence": 0.5},
                    self.run_check.question_key("missing_info"): {"type": "noul", "noul": 0.02, "confidence": 0.9},
                }},
            ):
                args = mock.Mock(fixture_dir="unused", mode="fixture")
                prepared = self.run_check.prepare(case, corpus, json.loads((TRIAGE_DIR / "policy.json").read_text()), [])
                judgments = self.run_check.judge(
                    prepared,
                    json.loads((TRIAGE_DIR / "questions.json").read_text()),
                    json.loads((TRIAGE_DIR / "policy.json").read_text()),
                    {}, args, "", [],
                )
            self.assertEqual(judgments["type"]["value"], None)
            self.assertEqual(judgments["type"]["status"], "needs-human-review")
            self.assertEqual(judgments["subsystem"]["value"], None)
            self.assertEqual(judgments["subsystem"]["status"], "needs-human-review")
            self.assertEqual(judgments["priority"]["value"], "P0")  # in-set: value kept
            self.assertEqual(judgments["priority"]["status"], "needs-human-review")  # low confidence demotes

    def test_missing_model_answer_is_neutral_not_fatal(self):
        # Regression (review MAJOR 3): an absent answer must yield a neutral
        # needs-human-review row, never exit 2 on an advisory dry run.
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            with mock.patch.object(self.run_check.runner, "run_fixture", return_value={"answers": {}}):
                args = mock.Mock(fixture_dir="unused", mode="fixture")
                prepared = self.run_check.prepare(
                    json.loads((CORPUS / "issues" / "bug-report.json").read_text()),
                    json.loads((CORPUS / "open-issues.json").read_text())["issues"],
                    json.loads((TRIAGE_DIR / "policy.json").read_text()),
                    [],
                )
                judgments = self.run_check.judge(
                    prepared,
                    json.loads((TRIAGE_DIR / "questions.json").read_text()),
                    json.loads((TRIAGE_DIR / "policy.json").read_text()),
                    {}, args, "", [],
                )
            self.assertIsNone(judgments["type"]["value"])
            self.assertEqual(judgments["type"]["status"], "needs-human-review")


class WorkflowSecurityTests(unittest.TestCase):
    """Dry-run workflow cannot write and never interpolates untrusted text."""

    @classmethod
    def setUpClass(cls):
        cls.wf = (REPO_ROOT / ".github" / "workflows" / "typesafe-issue-triage.yml").read_text()

    def test_triggers_and_permissions(self):
        self.assertIn("issues:", self.wf)
        self.assertIn("opened", self.wf)
        self.assertIn("workflow_dispatch:", self.wf)
        self.assertIn("issues: read", self.wf)
        self.assertNotIn("issues: write", self.wf)

    def test_no_untrusted_interpolation_into_shell(self):
        # Issue content must reach the collector as data (env/args), never as
        # unquoted shell interpolation of body/title fields.
        self.assertNotIn("github.event.issue.body", self.wf)
        self.assertNotIn("github.event.issue.title", self.wf)

    def test_dry_run_marker(self):
        self.assertIn("dry-run", self.wf.lower())


if __name__ == "__main__":
    unittest.main()
