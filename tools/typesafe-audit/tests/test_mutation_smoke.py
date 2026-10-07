"""Contract tests for pinned Stryker CLI execution and mutation report (#1110)."""

from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOL_DIR = REPO_ROOT / "tools" / "typesafe-audit"
QUALITY_DIR = TOOL_DIR / "quality"


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class StrykerContractTests(unittest.TestCase):
    """End-to-end contract between pinned Stryker CLI, normalization, and quality ranking."""

    @classmethod
    def setUpClass(cls):
        cls.mutation = load_module("quality_mutation", QUALITY_DIR / "mutation.py")
        cls.normalize = load_module("quality_normalize_report", QUALITY_DIR / "normalize_report.py")
        cls.run_check = load_module("quality_run_check", QUALITY_DIR / "run_check.py")

    def test_cli_accepts_pinned_options(self):
        result = subprocess.run(["dotnet", "stryker", "--help"], capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 0)
        help_text = result.stdout
        for opt in ["--concurrency", "--mutate", "--project", "--test-project", "--reporter", "--output", "--break-at", "--skip-version-check"]:
            self.assertIn(opt, help_text)

    def test_cli_rejects_unsupported_max_concurrent_test_runs(self):
        result = subprocess.run(
            ["dotnet", "stryker", "--max-concurrent-test-runs", "4"],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotEqual(result.returncode, 0)
        combined = result.stdout + result.stderr
        self.assertIn("Unrecognized option '--max-concurrent-test-runs'", combined)

    def test_end_to_end_contract_with_real_stryker_schema(self):
        # Creates a report mimicking real Stryker 5.0.0 output with absolute paths and mixed statuses
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            reports_dir = tmp / "reports"
            reports_dir.mkdir()
            raw_report = reports_dir / "mutation-report.json"
            abs_target = str((REPO_ROOT / "src" / "ContentTypeHelper.cs").resolve())

            sample_stryker_output = {
                "schemaVersion": "4",
                "thresholds": {"high": 80, "low": 60},
                "projectRoot": str((REPO_ROOT / "src").resolve()),
                "files": {
                    abs_target: {
                        "language": "csharp",
                        "mutants": [
                            {
                                "id": 1,
                                "mutatorName": "String mutation",
                                "description": "empty string changed",
                                "location": {"start": {"line": 15, "column": 10}, "end": {"line": 15, "column": 20}},
                                "status": "Survived",
                            },
                            {
                                "id": 2,
                                "mutatorName": "Equality",
                                "description": "== changed to !=",
                                "location": {"start": {"line": 16, "column": 5}, "end": {"line": 16, "column": 10}},
                                "status": "Killed",
                            },
                        ],
                    },
                    str((REPO_ROOT / "src" / "IgnoredFile.cs").resolve()): {
                        "language": "csharp",
                        "mutants": [
                            {
                                "id": 3,
                                "mutatorName": "Equality",
                                "description": "ignored mutant",
                                "location": {"start": {"line": 1, "column": 1}, "end": {"line": 1, "column": 5}},
                                "status": "Ignored",
                            }
                        ],
                    },
                },
            }
            raw_report.write_text(json.dumps(sample_stryker_output), encoding="utf-8")

            # 1. Locate report beneath directory
            located = self.mutation.locate_report(tmp)
            self.assertEqual(located, raw_report)

            # 2. Positive mutant count
            self.assertEqual(self.mutation.count_selected_mutants(sample_stryker_output), 2)

            # 3. Normalize to downstream path
            downstream = tmp / "results" / "mutation" / "mutation-report.json"
            norm = self.mutation.process_report(tmp, downstream, REPO_ROOT, scope="ContentTypeHelper.cs")
            self.assertTrue(downstream.is_file())
            self.assertIn("ContentTypeHelper.cs", norm["files"])
            self.assertNotIn(abs_target, norm["files"])

            # 4. Parse report via mutation.py
            cats = self.mutation.parse_report(downstream, REPO_ROOT)
            self.assertIn("survived", cats)
            survivor = cats["survived"][0]
            self.assertEqual(survivor["file"], "src/ContentTypeHelper.cs")
            self.assertEqual(survivor["method"], "GetContentTypeForExtension")
            self.assertEqual(survivor["mutator"], "String mutation")

    def test_missing_report_fails_explicitly(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            empty = tmp / "empty"
            empty.mkdir()
            out = tmp / "out.json"
            code = self.normalize.main(["--input", str(empty), "--output", str(out), "--repo-root", str(REPO_ROOT)])
            self.assertEqual(code, 1)

    def test_zero_mutants_fails_explicitly(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            raw = tmp / "raw.json"
            raw.write_text(json.dumps({"files": {"F.cs": {"mutants": [{"status": "Ignored"}]}}}), encoding="utf-8")
            out = tmp / "out.json"
            code = self.normalize.main(["--input", str(raw), "--output", str(out), "--scope", "LoadFiles/**", "--repo-root", str(REPO_ROOT)])
            self.assertEqual(code, 1)

    @unittest.skipUnless(os.environ.get("RUN_STRYKER_SMOKE") == "1", "Live Stryker smoke test requires RUN_STRYKER_SMOKE=1")
    def test_live_bounded_smoke(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            cmd = [
                "dotnet", "stryker",
                "--project", "Zipper.csproj",
                "--test-project", "Zipper.Tests/Zipper.Tests.csproj",
                "--mutate", "ContentTypeHelper.cs",
                "--reporter", "json",
                "--output", str(tmp),
                "--break-at", "0",
                "--concurrency", "4",
                "--skip-version-check",
            ]
            result = subprocess.run(cmd, cwd=REPO_ROOT / "src", capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 0, f"Stryker failed: {result.stdout}\n{result.stderr}")
            downstream = tmp / "results" / "mutation" / "mutation-report.json"
            self.mutation.process_report(tmp, downstream, REPO_ROOT, scope="ContentTypeHelper.cs")
            self.assertTrue(downstream.is_file())
            cats = self.mutation.parse_report(downstream, REPO_ROOT)
            total = sum(len(v) for v in cats.values())
            self.assertGreater(total, 0)


if __name__ == "__main__":
    unittest.main()
