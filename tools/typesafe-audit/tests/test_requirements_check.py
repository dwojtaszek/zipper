#!/usr/bin/env python3
"""Tests for the requirements semantic audit check (#956). No network."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[3]
CHECK_DIR = REPO_ROOT / "tools/typesafe-audit/checks/requirements"
sys.path.insert(0, str(CHECK_DIR.parents[1]))
sys.path.insert(0, str(CHECK_DIR))

import prechecks  # noqa: E402
from run_check import (  # noqa: E402
    EXIT_INPUT_ERROR,
    sections_from_corpus,
    status_for,
    load_json,
    main as run_check_main,
)

POLICY = load_json(CHECK_DIR / "policy.json")
CORPUS_DIR = REPO_ROOT / "tests/typesafe-audit-fixtures"


def git(repo: Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True)


class TempRepoTest(unittest.TestCase):
    """Base class: a temp git repo whose Requirements.md evolves."""

    def setUp(self):
        self.repo = Path(tempfile.mkdtemp())
        git(self.repo, "init", "-q")
        git(self.repo, "config", "user.email", "test@example.com")
        git(self.repo, "config", "user.name", "Test")
        (self.repo / "Requirements.md").write_text("**REQ-100**: Base requirement.\n", encoding="utf-8")
        (self.repo / "tests").mkdir()
        (self.repo / "tests/req-traceability.tsv").write_text("REQ-100\tunit\tSomeTest\ttrace\n", encoding="utf-8")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-qm", "base")
        self.base = subprocess.run(
            ["git", "-C", str(self.repo), "rev-parse", "HEAD"], capture_output=True, text=True, check=True
        ).stdout.strip()

    def evolve(self, content: str):
        (self.repo / "Requirements.md").write_text(content, encoding="utf-8")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-qm", "change")


class ExtractionTests(TempRepoTest):
    def test_extracts_changed_hunks_with_req_ids(self):
        self.evolve("**REQ-100**: Base requirement.\n**REQ-101**: New requirement with behavior X.\n")
        sections = __import__("extract_sections", fromlist=["extract_sections"]).extract_sections(
            self.repo, self.base
        )
        self.assertEqual(1, len(sections))
        self.assertEqual("Requirements.md", sections[0]["path"])
        self.assertIn("REQ-101", sections[0]["req_ids"])
        self.assertIn("New requirement", sections[0]["text"])

    def test_sections_from_corpus_lists_every_example(self):
        sections = sections_from_corpus(CORPUS_DIR)
        self.assertEqual(4, len(sections))
        self.assertEqual({s["path"] for s in sections}, {"clear.md", "ambiguous.md", "contradictory.md", "incomplete.md"})

    def test_deleted_audited_file_is_not_attributed_to_previous_file(self):
        self.evolve("**REQ-100**: Base requirement.\n**REQ-101**: Added.\n")
        (self.repo / "README.md").write_text("Unchanged readme\n", encoding="utf-8")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-qm", "readme")
        mid = subprocess.run(
            ["git", "-C", str(self.repo), "rev-parse", "HEAD"], capture_output=True, text=True, check=True
        ).stdout.strip()
        (self.repo / "README.md").unlink()
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-qm", "delete-readme")
        sections = __import__("extract_sections", fromlist=["extract_sections"]).extract_sections(self.repo, self.base)
        self.assertTrue(all(s["path"] != "README.md" for s in sections))
        self.assertTrue(all(s["path"] == "Requirements.md" for s in sections))


class PrecheckTests(TempRepoTest):
    def test_immutable_id_violation_is_a_defect(self):
        self.evolve("**REQ-102**: Replacement definition; the old requirement is gone.\n")
        sections = __import__("extract_sections", fromlist=["extract_sections"]).extract_sections(self.repo, self.base)
        findings = prechecks.check_immutable_ids(self.repo, self.base)
        self.assertEqual("immutable-req-id", findings[0]["check"])
        self.assertIn("REQ-100", findings[0]["message"])

    def test_orphan_reference_is_a_defect(self):
        # Orphan = another doc cites a REQ id that Requirements.md never defines.
        (self.repo / "README.md").write_text("See REQ-404 for details.\n", encoding="utf-8")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-qm", "readme-cite")
        sections = __import__("extract_sections", fromlist=["extract_sections"]).extract_sections(self.repo, self.base)
        findings = prechecks.check_orphan_references(self.repo, sections)
        self.assertTrue(any(f["req_id"] == "REQ-404" for f in findings))

    def test_missing_traceability_row_is_a_defect(self):
        self.evolve("**REQ-100**: Base requirement.\n**REQ-101**: New requirement without trace row.\n")
        sections = __import__("extract_sections", fromlist=["extract_sections"]).extract_sections(self.repo, self.base)
        findings = prechecks.check_traceability(self.repo, sections)
        self.assertTrue(any(f["req_id"] == "REQ-101" for f in findings))


class StatusPolicyTests(unittest.TestCase):
    def test_choice_bad_answer_is_finding(self):
        row = {"type": "choice", "answer": "ambiguous", "confidence": 0.95}
        self.assertEqual("finding", status_for("testability", row, POLICY))

    def test_choice_good_answer_is_ok(self):
        row = {"type": "choice", "answer": "testable", "confidence": 0.95}
        self.assertEqual("ok", status_for("testability", row, POLICY))

    def test_choice_low_confidence_is_needs_human_review(self):
        row = {"type": "choice", "answer": "ambiguous", "confidence": 0.3}
        self.assertEqual("needs-human-review", status_for("testability", row, POLICY))

    def test_noul_true_is_finding_false_is_ok(self):
        self.assertEqual("finding", status_for("contradiction", {"type": "noul", "answer": 0.99}, POLICY))
        self.assertEqual("ok", status_for("contradiction", {"type": "noul", "answer": 0.01}, POLICY))

    def test_noul_mid_range_is_needs_human_review(self):
        self.assertEqual("needs-human-review", status_for("contradiction", {"type": "noul", "answer": 0.5}, POLICY))

    def test_null_answer_is_needs_human_review(self):
        self.assertEqual("needs-human-review", status_for("completeness", {"type": "choice", "answer": None, "confidence": None}, POLICY))


class CorpusEvaluationTests(unittest.TestCase):
    """Issue #956: corpus fixtures must reproduce every expected label exactly."""

    def test_corpus_labels_reproduced_with_recorded_responses(self):
        fixtures = Path(tempfile.mkdtemp())
        recorder = subprocess.run(
            [sys.executable, str(CHECK_DIR / "record_corpus_fixtures.py"), str(fixtures)],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(0, recorder.returncode, recorder.stderr)

        json_out = fixtures / "report.json"
        md_out = fixtures / "report.md"
        summary_out = fixtures / "summary.md"
        code = run_check_main([
            "--corpus", str(CORPUS_DIR),
            "--mode", "fixture",
            "--fixture-dir", str(fixtures),
            "--json-out", str(json_out),
            "--md-out", str(md_out),
            "--summary-out", str(summary_out),
        ])
        self.assertEqual(0, code)
        # --summary-out receives the Markdown report for the CI job summary.
        self.assertTrue(summary_out.is_file())
        self.assertIn("Requirements Audit Report", summary_out.read_text(encoding="utf-8"))

        report = json.loads(json_out.read_text(encoding="utf-8"))
        expected = {
            e["file"]: e["expected"]
            for e in load_json(CORPUS_DIR / "expected_labels.json")["examples"]
        }
        by_source = {}
        for result in report["results"]:
            by_source.setdefault(result["source"], {})[result["question"]] = result

        for file_name, labels in expected.items():
            end_line = len((CORPUS_DIR / file_name).read_text(encoding="utf-8").splitlines())
            results = by_source[f"{file_name}:1-{end_line}"]
            for question, expected_value in labels.items():
                actual = results[question]
                if question == "contradiction" or question == "non_canonical_term":
                    self.assertEqual(
                        expected_value, actual["answer"] >= POLICY["noul_finding_range"][1],
                        f"{file_name}/{question}",
                    )
                else:
                    self.assertEqual(expected_value, actual["answer"], f"{file_name}/{question}")

        # No needs-human-review on the recorded corpus: responses are decisive.
        self.assertFalse(report["needs_human_review"])

    def test_full_mode_is_request_budgeted(self):
        policy = load_json(CHECK_DIR / "policy.json")
        self.assertLess(policy["full_mode_max_requests"] * policy["full_mode_batch_size"], 10000)

    def test_extraction_failure_is_clean_input_error(self):
        import runner  # noqa: F401  (module already on path via run_check import)
        import extract_sections
        import tempfile as tf
        from pathlib import Path as P

        repo = P(tf.mkdtemp())
        with mock.patch.object(extract_sections, "_run_git", side_effect=RuntimeError("shallow clone has no base commit")):
            code = run_check_main([
                "--base", "deadbeef",
                "--json-out", str(repo / "o.json"),
                "--md-out", str(repo / "o.md"),
            ])
        self.assertEqual(EXIT_INPUT_ERROR, code)


class WorkflowWiringTests(unittest.TestCase):
    def test_workflow_runs_requirements_check(self):
        workflow = (REPO_ROOT / ".github/workflows/typesafe-audit.yml").read_text(encoding="utf-8")
        self.assertIn("run_check.py", workflow)
        self.assertIn("--full", workflow)

    def test_missing_base_outside_corpus_is_input_error(self):
        code = run_check_main([
            "--json-out", str(Path(tempfile.mkdtemp()) / "o.json"),
            "--md-out", str(Path(tempfile.mkdtemp()) / "o.md"),
        ])
        self.assertEqual(EXIT_INPUT_ERROR, code)


class EmptySectionsTests(unittest.TestCase):
    def test_empty_sections_diff_returns_ok_without_crash(self):
        out_dir = Path(tempfile.mkdtemp())
        json_out = out_dir / "report.json"
        md_out = out_dir / "report.md"
        summary_out = out_dir / "summary.md"
        import run_check
        with mock.patch.object(run_check, "extract_sections", return_value=[]):
            code = run_check_main([
                "--base", "HEAD",
                "--mode", "fixture",
                "--json-out", str(json_out),
                "--md-out", str(md_out),
                "--summary-out", str(summary_out),
            ])
        self.assertEqual(0, code)
        report = json.loads(json_out.read_text(encoding="utf-8"))
        self.assertEqual([], report["results"])
        self.assertEqual([], report["precheck_findings"])
        self.assertFalse(report["needs_human_review"])


if __name__ == "__main__":
    unittest.main()

