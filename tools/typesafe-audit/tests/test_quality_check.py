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

    def test_parse_report_handles_absolute_paths_from_real_stryker(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            real_stryker_json = tmp / "mutation-report.json"
            abs_file = (CORPUS / "src" / "Validation" / "ProductionSetPostValidator.cs").resolve()
            report_data = {
                "schemaVersion": "4",
                "files": {
                    str(abs_file): {
                        "language": "csharp",
                        "mutants": [
                            {
                                "id": 1,
                                "mutatorName": "Arithmetic",
                                "description": "Path.Combine(baseDir, name) changed to Path.Combine(baseDir, name + 1)",
                                "location": {"start": {"line": 12, "column": 20}, "end": {"line": 12, "column": 44}},
                                "status": "Survived"
                            }
                        ]
                    }
                }
            }
            real_stryker_json.write_text(json.dumps(report_data), encoding="utf-8")
            categories = self.mutation.parse_report(real_stryker_json, CORPUS)
            self.assertIn("survived", categories)
            survivor = categories["survived"][0]
            self.assertEqual(survivor["file"], "src/Validation/ProductionSetPostValidator.cs")
            self.assertFalse(survivor["file"].startswith("src//"))
            self.assertEqual(survivor["method"], "ValidateDestination")



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

    def test_changed_files_filter_restricts_candidates(self):
        from unittest import mock
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fake_scored = [{"origin": "coverage", "category": "coverage_gap", "file": "src/Validation/ProductionSetPostValidator.cs", "method": "Validate", "lines": "1-10", "req_ids": [], "priority": 0.8, "scores": {}}]
            with mock.patch.object(self.run_check, "score_batch", return_value=fake_scored):
                args = [
                    "--coverage", str(CORPUS / "cobertura.xml"),
                    "--repo-root", str(CORPUS),
                    "--mode", "fixture",
                    "--changed-files", "src/Validation/ProductionSetPostValidator.cs",
                    "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
                ]
                code = self.run_check.main(args)
                self.assertEqual(code, self.run_check.EXIT_OK)
                report = json.loads((tmp / "r.json").read_text(encoding="utf-8"))
                self.assertEqual(len(report["ranked"]), 1)

            args[7] = "src/Unrelated/OtherFile.cs"
            code = self.run_check.main(args)
            self.assertEqual(code, self.run_check.EXIT_OK)
            report2 = json.loads((tmp / "r.json").read_text(encoding="utf-8"))
            self.assertEqual(len(report2["ranked"]), 0)

    def test_path_matches_respects_boundary(self):
        pm = self.run_check._path_matches
        self.assertTrue(pm("src/Foo.cs", {"src/Foo.cs"}))
        self.assertTrue(pm("src/Foo.cs", {"Foo.cs"}))
        self.assertTrue(pm("/repo/src/Foo.cs", {"src/Foo.cs"}))
        self.assertFalse(pm("src/BarFoo.cs", {"Foo.cs"}))
        self.assertFalse(pm("src/Foo.cs", {"BarFoo.cs"}))


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

    def test_workflow_uses_pinned_concurrency_option(self):
        self.assertIn("--concurrency 4", self.wf)
        self.assertNotIn("--max-concurrent-test-runs", self.wf)

    def test_workflow_scope_relative_to_project_root(self):
        self.assertIn('default: "LoadFiles/**', self.wf)
        self.assertNotIn('default: "src/LoadFiles/**', self.wf)

    def test_workflow_wires_shard_timeout(self):
        self.assertIn("timeout-minutes: ${{ fromJSON(github.event.inputs.shard || '45') }}", self.wf)

    def test_workflow_upload_runs_on_failure_or_timeout(self):
        self.assertIn("always() && steps.secret.outputs.available == 'true'", self.wf)

    def test_workflow_does_not_silently_drop_mutation_input(self):
        self.assertNotIn("MUTATION_ARG=()", self.wf)
        self.assertIn("--mutation results/mutation/mutation-report.json", self.wf)

    def test_workflow_locates_and_normalizes_mutation_report(self):
        self.assertIn("normalize_report.py", self.wf)
        self.assertIn('--output "../results/mutation"', self.wf)


class MutationNormalizeTests(unittest.TestCase):
    """Report location, mutant counting, normalization, and validation."""

    @classmethod
    def setUpClass(cls):
        cls.mutation = load_module("quality_mutation", QUALITY_DIR / "mutation.py")

    def test_locate_report_finds_nested_file(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            nested = tmp / "reports"
            nested.mkdir()
            report_file = nested / "mutation-report.json"
            report_file.write_text("{}", encoding="utf-8")
            located = self.mutation.locate_report(tmp)
            self.assertEqual(located, report_file)

    def test_locate_report_missing_file_raises(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            with self.assertRaises(FileNotFoundError):
                self.mutation.locate_report(tmp)

    def test_count_selected_mutants_excludes_ignored(self):
        report = {
            "files": {
                "Foo.cs": {
                    "mutants": [
                        {"status": "Ignored"},
                        {"status": "Killed"},
                        {"status": "Survived"},
                    ]
                }
            }
        }
        self.assertEqual(self.mutation.count_selected_mutants(report), 2)

    def test_process_report_zero_mutants_raises(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            raw = tmp / "raw.json"
            raw.write_text(json.dumps({
                "files": {
                    "Foo.cs": {"mutants": [{"status": "Ignored"}]}
                }
            }), encoding="utf-8")
            out = tmp / "out.json"
            with self.assertRaises(ValueError) as ctx:
                self.mutation.process_report(raw, out, REPO_ROOT, scope="Foo/**")
            self.assertIn("0 selected mutants", str(ctx.exception))

    def test_process_report_normalizes_and_writes_output(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            raw = tmp / "raw.json"
            abs_path = str((REPO_ROOT / "src" / "ContentTypeHelper.cs").resolve())
            raw.write_text(json.dumps({
                "files": {
                    abs_path: {"mutants": [{"status": "Killed"}]}
                }
            }), encoding="utf-8")
            out = tmp / "out.json"
            norm = self.mutation.process_report(raw, out, REPO_ROOT, scope="ContentTypeHelper.cs")
            self.assertTrue(out.is_file())
            self.assertIn("files", norm)
            self.assertIn("ContentTypeHelper.cs", norm["files"])
            self.assertNotIn(abs_path, norm["files"])


if __name__ == "__main__":
    unittest.main()
