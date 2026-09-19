"""Tests for the TypeSafe quality prioritization check (#959)."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOL_DIR = REPO_ROOT / "tools" / "typesafe-audit"
QUALITY_DIR = TOOL_DIR / "quality"
CORPUS = REPO_ROOT / "tests" / "typesafe-audit-fixtures" / "quality"


def load_module(name: str, path: Path):
    # Explicit-path loading avoids module-name collisions between checks.
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class CoverageGapTests(unittest.TestCase):
    """Deterministic Cobertura parsing, exclusions, and REQ mapping."""

    @classmethod
    def setUpClass(cls):
        cls.gaps = load_module("quality_coverage_gaps", QUALITY_DIR / "coverage_gaps.py")

    def test_uncovered_lines_mapped_to_methods(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        by_method = {g["method"]: g for g in gaps}
        self.assertIn("ValidateDestination", by_method)
        self.assertTrue(by_method["ValidateDestination"]["uncovered_lines"])
        self.assertIn("ValidateDestination", by_method["ValidateDestination"]["method"])

    def test_branch_coverage_is_parsed(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        by_method = {g["method"]: g for g in gaps}
        self.assertEqual(by_method["ValidateDestination"]["uncovered_branches"], 1)

    def test_generated_and_trivial_accessors_excluded(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        methods = [g["method"] for g in gaps]
        self.assertNotIn("get_Priority", methods)  # trivial accessor
        self.assertFalse(any("Designer" in m for m in methods))

    def test_req_ids_from_method_text_and_traceability_rows(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        by_method = {g["method"]: g for g in gaps}
        # REQ-171 comes from both the method source text and the TSV rows; the
        # traceability rows also provide owning test names.
        self.assertIn("REQ-171", by_method["ValidateDestination"]["req_ids"])
        self.assertIn("ProductionSetPostValidatorTests.ValidateDestination_MissingDirectory_ShouldThrow", by_method["ValidateDestination"]["tests"])

    def test_gap_budget_enforced(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS, max_gaps=1)
        self.assertEqual(len(gaps), 1)


class MutationTests(unittest.TestCase):
    """Deterministic Stryker report parsing and category separation."""

    @classmethod
    def setUpClass(cls):
        cls.mutation = load_module("quality_mutation", QUALITY_DIR / "mutation.py")

    def test_categories_are_separate(self):
        categories = self.mutation.parse_report(CORPUS / "stryker.json", CORPUS)
        self.assertEqual(
            sorted(categories),
            ["compile_error", "no_coverage", "survived", "timeout"],
        )

    def test_survivors_carry_location_and_mutator(self):
        categories = self.mutation.parse_report(CORPUS / "stryker.json", CORPUS)
        survivor = categories["survived"][0]
        self.assertEqual(survivor["mutator"], "Arithmetic")
        self.assertIn("Path.Combine", survivor["description"])
        self.assertEqual(survivor["method"], "ValidateDestination")
        self.assertIn("REQ-171", survivor["req_ids"])


class RunCheckTests(unittest.TestCase):
    """Fixture-mode ranking run, composition, byte stability."""

    @classmethod
    def setUpClass(cls):
        cls.run_check = load_module("quality_run_check", QUALITY_DIR / "run_check.py")

    def _record(self, fixture_dir: Path):
        subprocess.run(
            [sys.executable, str(QUALITY_DIR / "record_corpus_fixtures.py"), str(fixture_dir)],
            check=True,
        )

    def test_ranking_reproduces_expected_order(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            self.run_check.main([
                "--coverage", str(CORPUS / "cobertura.xml"),
                "--mutation", str(CORPUS / "stryker.json"),
                "--repo-root", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            report = json.loads((tmp / "r.json").read_text())
            candidates = report["ranked"]
            self.assertGreater(len(candidates), 0)
            scores = [c["priority"] for c in candidates]
            self.assertEqual(scores, sorted(scores, reverse=True))
            # High-risk output-validation candidates rank above trivial accessors.
            self.assertNotIn("get_Priority", [c["method"] for c in candidates])
            # Components and weights are never collapsed away.
            top = candidates[0]
            self.assertIn("components", top)
            self.assertIn("weights", top)
            self.assertIn("source_sha256", top)

    def test_report_is_byte_stable(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            args = [
                "--coverage", str(CORPUS / "cobertura.xml"),
                "--mutation", str(CORPUS / "stryker.json"),
                "--repo-root", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ]
            self.run_check.main(args)
            first = (tmp / "r.json").read_bytes()
            (tmp / "r.json").unlink()
            self.run_check.main(args)
            self.assertEqual(first, (tmp / "r.json").read_bytes())

    def test_missing_inputs_is_input_error(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            code = self.run_check.main([
                "--repo-root", str(CORPUS),
                "--mode", "fixture",
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            self.assertEqual(code, self.run_check.EXIT_INPUT_ERROR)

    def test_live_mode_reaches_runner_with_wellformed_request(self):
        # Regression (review B1): live mode must actually invoke run_live with
        # a well-formed request, not crash on a missing config local.
        import os
        from unittest import mock

        captured = {}

        def fake_run_live(request, config, api_key, secrets):
            captured["request"] = request
            captured["api_key"] = api_key
            return {"answers": {key: {"score": 0.5, "confidence": 0.9} for key in request["questions"]}}

        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}):
                with mock.patch.object(self.run_check.runner, "run_live", side_effect=fake_run_live):
                    code = self.run_check.main([
                        "--coverage", str(CORPUS / "cobertura.xml"),
                        "--repo-root", str(CORPUS),
                        "--mode", "live",
                        "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
                    ])
            self.assertEqual(code, self.run_check.EXIT_OK)
            request = captured["request"]
            self.assertIn("model", request)
            self.assertIn("questions", request)
            self.assertTrue(all("#" in key for key in request["questions"]))
            self.assertEqual(captured["api_key"], "test-key")

    def test_ranking_sorted_across_batch_boundaries(self):
        # Regression (review M1): with more candidates than batch_size, the
        # final ranking must still be globally priority-ordered.
        from unittest import mock

        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            synthetic = [
                {"origin": "coverage", "file": f"src/Synthetic{i}.cs", "method": f"M{i}", "first_line": 1,
                 "last_line": 2, "uncovered_lines": 1, "uncovered_branches": 0, "req_ids": [], "tests": [],
                 "source_sha256": f"{i:064x}"}
                for i in range(25)
            ]
            with mock.patch.object(self.run_check.coverage_gaps, "extract_gaps", return_value=synthetic):
                with mock.patch.object(
                    self.run_check.runner, "run_fixture",
                    side_effect=lambda request, fixture_dir: {
                        "answers": {
                            key: {"score": int(key.split("#")[-2]) / 30.0, "confidence": 0.9}
                            for key in request["questions"]
                        }
                    },
                ):
                    self.run_check.main([
                        "--coverage", str(CORPUS / "cobertura.xml"),
                        "--repo-root", str(CORPUS),
                        "--mode", "fixture", "--fixture-dir", str(fixtures),
                        "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
                    ])
            report = json.loads((tmp / "r.json").read_text())
            priorities = [c["priority"] for c in report["ranked"]]
            self.assertEqual(priorities, sorted(priorities, reverse=True))
            self.assertEqual(len(priorities), 25)


class WorkflowWiringTests(unittest.TestCase):
    """Scheduled/manual quality workflow with least privilege and budgets."""

    @classmethod
    def setUpClass(cls):
        cls.wf = (REPO_ROOT / ".github" / "workflows" / "typesafe-quality-audit.yml").read_text()

    def test_weekly_schedule_and_dispatch(self):
        self.assertIn("cron:", self.wf)
        self.assertIn("workflow_dispatch:", self.wf)
        self.assertIn("scope:", self.wf)
        self.assertIn("shard:", self.wf)

    def test_read_only_permissions(self):
        self.assertIn("contents: read", self.wf)
        self.assertNotIn("contents: write", self.wf)

    def test_coverage_run_uses_cobertura(self):
        self.assertIn("XPlat Code Coverage", self.wf)

    def test_mutation_tool_is_pinned_in_manifest(self):
        manifest = json.loads((REPO_ROOT / ".config" / "dotnet-tools.json").read_text())
        self.assertIn("dotnet-stryker", manifest["tools"])
        self.assertTrue(manifest["tools"]["dotnet-stryker"]["version"])


if __name__ == "__main__":
    unittest.main()
