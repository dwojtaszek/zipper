#!/usr/bin/env python3
"""Tests for the semantic traceability audit check (#957). No network."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[3]
CHECK_DIR = REPO_ROOT / "tools/typesafe-audit/checks/traceability"
CORPUS_DIR = REPO_ROOT / "tests/typesafe-audit-fixtures/traceability"


def _load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


# Explicit-path imports: both audit checks define parse.py/run_check.py, so
# plain `import` would collide within one test process.
trace_parse = _load_module("tsa_trace_parse", CHECK_DIR / "parse.py")
trace_run_check = _load_module("tsa_trace_run_check", CHECK_DIR / "run_check.py")
ParseError = trace_parse.ParseError
parse_tsv = trace_parse.parse_tsv
resolve_e2e_reference = trace_parse.resolve_e2e_reference
resolve_unit_reference = trace_parse.resolve_unit_reference
run_check_main = trace_run_check.main
EXIT_INPUT_ERROR = trace_run_check.EXIT_INPUT_ERROR
load_json = trace_run_check.load_json

POLICY = load_json(CHECK_DIR / "policy.json")


class TsvParserTests(unittest.TestCase):
    def test_parses_valid_rows(self):
        rows = parse_tsv(CORPUS_DIR / "req-traceability.tsv")
        self.assertEqual(8, len(rows))
        self.assertEqual("REQ-910", rows[0]["req_id"])

    def test_rejects_malformed_row(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "bad.tsv"
            bad.write_text("req_id\tcoverage\treference\tnotes\nREQ-1\tunit\n", encoding="utf-8")
            with self.assertRaises(ParseError):
                parse_tsv(bad)

    def test_rejects_exemption_without_rationale(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "bad.tsv"
            bad.write_text("req_id\tcoverage\treference\tnotes\nREQ-1\texemption\t-\t\n", encoding="utf-8")
            with self.assertRaises(ParseError):
                parse_tsv(bad)

    def test_rejects_unknown_coverage(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "bad.tsv"
            bad.write_text("req_id\tcoverage\treference\tnotes\nREQ-1\tmaybe\tX\t-\n", encoding="utf-8")
            with self.assertRaises(ParseError):
                parse_tsv(bad)


class UnitResolverTests(unittest.TestCase):
    def test_resolves_fact_method_body(self):
        resolved = resolve_unit_reference(CORPUS_DIR / "src", "FixtureTests.Full")
        self.assertIn("Assert.Equal(3, result)", resolved["body"])
        self.assertEqual(64, len(resolved["sha256"]))

    def test_resolves_theory_method_body(self):
        resolved = resolve_unit_reference(CORPUS_DIR / "src", "FixtureTests.TheoryFull")
        self.assertIn("Assert.Equal(expected, result)", resolved["body"])

    def test_resolves_method_in_nested_class(self):
        resolved = resolve_unit_reference(CORPUS_DIR / "src", "NestedFixtureTests.InnerCheck")
        self.assertIn("Assert.NotEmpty", resolved["body"])

    def test_duplicate_method_names_are_ambiguous(self):
        with self.assertRaises(ParseError) as ctx:
            resolve_unit_reference(CORPUS_DIR / "src", "DupTests.Dup")
        self.assertIn("ambiguous", str(ctx.exception))

    def test_renamed_test_is_unresolved(self):
        with self.assertRaises(ParseError):
            resolve_unit_reference(CORPUS_DIR / "src", "FixtureTests.WasRenamedAway")

    def test_malformed_reference_is_rejected(self):
        with self.assertRaises(ParseError):
            resolve_unit_reference(CORPUS_DIR / "src", "NoDot")


class E2eResolverTests(unittest.TestCase):
    def test_resolves_scenario_block(self):
        resolved = resolve_e2e_reference(CORPUS_DIR, "sample.sh full-scenario")
        self.assertIn("assert output A", resolved["body"])
        self.assertNotIn("partial-scenario", resolved["body"])

    def test_unknown_scenario_is_unresolved(self):
        with self.assertRaises(ParseError):
            resolve_e2e_reference(CORPUS_DIR, "sample.sh missing-scenario")


class CorpusEvaluationTests(unittest.TestCase):
    def test_corpus_labels_reproduced_with_recorded_responses(self):
        fixtures = Path(tempfile.mkdtemp())
        recorder = subprocess.run(
            [sys.executable, str(CHECK_DIR / "record_corpus_fixtures.py"),
             str(fixtures),
             str(CORPUS_DIR / "req-traceability.tsv"),
             str(CORPUS_DIR / "src"),
             str(CORPUS_DIR),
             str(CHECK_DIR / "questions.json")],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(0, recorder.returncode, recorder.stderr)

        json_out = fixtures / "report.json"
        md_out = fixtures / "report.md"
        code = run_check_main([
            "--full",
            "--tsv", str(CORPUS_DIR / "req-traceability.tsv"),
            "--tests-root", str(CORPUS_DIR / "src"),
            "--requirements-root", str(CORPUS_DIR),
            "--mode", "fixture",
            "--fixture-dir", str(fixtures),
            "--json-out", str(json_out),
            "--md-out", str(md_out),
        ])
        self.assertEqual(0, code)

        report = json.loads(json_out.read_text(encoding="utf-8"))
        expected = {
            e["file"]: e["expected"]
            for e in load_json(CORPUS_DIR / "expected_labels.json")["examples"]
        }
        by_req = {}
        for result in report["results"]:
            by_req.setdefault((result["req_id"], result["question"]), result)

        for req_id, labels in expected.items():
            for question, value in labels.items():
                result = by_req[(req_id, question)]
                if question == "coverage":
                    self.assertEqual(value, result["answer"], f"{req_id}/{question}")
                else:
                    self.assertEqual(value, result["answer"] >= POLICY["noul_finding_range"][1], f"{req_id}/{question}")

        # Exemption rows are outside the audited scope.
        self.assertFalse(any(r["req_id"] == "REQ-914" for r in report["results"]))
        self.assertFalse(report["needs_human_review"])

    def test_report_json_is_stable_byte_for_byte(self):
        fixtures = Path(tempfile.mkdtemp())
        record = subprocess.run(
            [sys.executable, str(CHECK_DIR / "record_corpus_fixtures.py"),
             str(fixtures),
             str(CORPUS_DIR / "req-traceability.tsv"),
             str(CORPUS_DIR / "src"),
             str(CORPUS_DIR),
             str(CHECK_DIR / "questions.json")],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(0, record.returncode, record.stderr)
        base_args = [
            "--full",
            "--tsv", str(CORPUS_DIR / "req-traceability.tsv"),
            "--tests-root", str(CORPUS_DIR / "src"),
            "--requirements-root", str(CORPUS_DIR),
            "--mode", "fixture",
            "--fixture-dir", str(fixtures),
        ]
        first = fixtures / "a.json"
        run_check_main([*base_args, "--json-out", str(first), "--md-out", str(fixtures / "a.md")])
        second = fixtures / "b.json"
        run_check_main([*base_args, "--json-out", str(second), "--md-out", str(fixtures / "b.md")])
        self.assertEqual(first.read_text(encoding="utf-8"), second.read_text(encoding="utf-8"))

    def test_unresolved_reference_fails_deterministically_before_api(self):
        code = run_check_main([
            "--full",
            "--tsv", str(CORPUS_DIR / "req-traceability.tsv"),
            "--tests-root", str(CORPUS_DIR / "src"),
            "--requirements-root", str(CORPUS_DIR),
            "--mode", "live",
            "--json-out", str(Path(tempfile.mkdtemp()) / "o.json"),
            "--md-out", str(Path(tempfile.mkdtemp()) / "o.md"),
        ])
        self.assertEqual(EXIT_INPUT_ERROR, code)


class WorkflowWiringTests(unittest.TestCase):
    def test_workflow_runs_strict_gate_then_semantic_audit(self):
        workflow = (REPO_ROOT / ".github/workflows/typesafe-audit.yml").read_text(encoding="utf-8")
        self.assertIn("validate-req-traceability.sh --strict", workflow)
        self.assertIn("checks/traceability/run_check.py", workflow)


if __name__ == "__main__":
    unittest.main()
