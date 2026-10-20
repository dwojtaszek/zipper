"""Contract and behavioral tests for local developer workflow improvements:
1. dev.sh / dev.bat smart diff-aware CLI wrapper
2. Parallel standalone suite runner in tests/run-tests.sh
3. Content-hash mutant result caching in tests/run-mutation-smoke.sh
4. Auto-sync Git hooks via Directory.Build.props and setup-hook.{sh,bat}
"""
from __future__ import annotations

import os
import shutil
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


@unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
class DevCliAndLocalWorkflowTests(unittest.TestCase):
    def _run_dev(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["bash", str(ROOT / "dev.sh"), *args],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_dev_cli_docs_only_runs_only_docs_drift_tests(self):
        res = self._run_dev("check", "--dry-run", "--files", "README.md,docs/cicd.md")
        self.assertEqual(res.returncode, 0, res.stderr)
        self.assertIn("Build solution (Release)", res.stdout)
        self.assertIn("Run DocsDriftTests", res.stdout)
        self.assertNotIn("Run unit + property tests", res.stdout)
        self.assertNotIn("Run incremental mutation smoke", res.stdout)
        self.assertNotIn("Run basic E2E smoke", res.stdout)

    def test_dev_cli_rejects_unknown_command(self):
        res = self._run_dev("tset")
        self.assertNotEqual(res.returncode, 0)
        self.assertIn("Unknown command", res.stderr)

    def test_dev_cli_rejects_unresolved_base_ref(self):
        res = self._run_dev("check", "--dry-run", "--base", "nonexistent-ref-xyz-12345")
        self.assertNotEqual(res.returncode, 0)
        self.assertIn("Cannot resolve base ref", res.stderr)

    def test_dev_cli_analyzer_changes_run_analyzer_tests_without_mutation(self):
        res = self._run_dev(
            "check",
            "--dry-run",
            "--files",
            "src/Zipper.Analyzers/SampleAnalyzer.cs",
        )
        self.assertEqual(res.returncode, 0, res.stderr)
        self.assertIn("Build solution (Release)", res.stdout)
        self.assertIn("Verify code formatting", res.stdout)
        self.assertIn("Run Roslyn analyzer tests", res.stdout)
        self.assertNotIn("Run incremental mutation smoke", res.stdout)
        self.assertNotIn("Run basic E2E smoke", res.stdout)

    def test_dev_cli_production_cs_changes_build_once_and_run_all_code_gates(self):
        res = self._run_dev("check", "--dry-run", "--files", "src/Program.cs")
        self.assertEqual(res.returncode, 0, res.stderr)
        self.assertEqual(
            res.stdout.count("Build solution (Release)"),
            1,
            "dev.sh must build Release only once across all gates",
        )
        self.assertIn("Verify code formatting", res.stdout)
        self.assertIn("Run unit + property tests", res.stdout)
        self.assertIn("Run incremental mutation smoke", res.stdout)
        self.assertIn("Run basic E2E smoke", res.stdout)
        self.assertNotIn("Run DocsDriftTests", res.stdout)

    def test_dev_cli_modified_e2e_script_runs_parity_and_specific_script(self):
        res = self._run_dev(
            "check",
            "--dry-run",
            "--files",
            "tests/test-bates-numbering.sh",
        )
        self.assertEqual(res.returncode, 0, res.stderr)
        self.assertIn("Validate E2E .sh/.bat parity", res.stdout)
        self.assertIn("Run DocsDriftTests", res.stdout)
        self.assertIn("Run modified E2E script (tests/test-bates-numbering.sh)", res.stdout)

    def test_dev_cli_explicit_subcommands_dry_run(self):
        res_all = self._run_dev("all", "--dry-run")
        self.assertEqual(res_all.returncode, 0, res_all.stderr)
        self.assertEqual(res_all.stdout.count("Build solution (Release)"), 1)
        self.assertIn("Run Roslyn analyzer tests", res_all.stdout)
        self.assertIn("Run incremental mutation smoke", res_all.stdout)
        self.assertIn("Run basic E2E smoke", res_all.stdout)

        res_e2e_full = self._run_dev("e2e", "--full", "--dry-run")
        self.assertEqual(res_e2e_full.returncode, 0, res_e2e_full.stderr)
        self.assertIn("Run full E2E suite", res_e2e_full.stdout)

    def test_run_tests_sh_supports_parallel_and_serial_flags(self):
        run_tests_text = (ROOT / "tests/run-tests.sh").read_text(encoding="utf-8")
        self.assertIn("RUN_TESTS_JOBS", run_tests_text)
        self.assertIn("run_standalone_suite", run_tests_text)
        self.assertIn("flush_parallel_suites", run_tests_text)
        self.assertIn("--serial", run_tests_text)

    def test_mutation_smoke_supports_content_hash_cache(self):
        mutation_text = (ROOT / "tests/run-mutation-smoke.sh").read_text(encoding="utf-8")
        self.assertIn("MUTATION_SMOKE_CACHE", mutation_text)
        self.assertIn("MUTATION_CACHE_DIR", mutation_text)
        self.assertIn("--no-cache", mutation_text)
        self.assertIn(r"'s/\|\|/\&\&/'", mutation_text)
        self.assertIn("compile_${compile_key}", mutation_text)
        self.assertIn("killed_targeted_${targeted_key}", mutation_text)
        self.assertIn("killed_full_${full_key}", mutation_text)

    def test_directory_build_props_auto_syncs_git_hooks_locally_only(self):
        props_text = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        self.assertIn('Target Name="SyncGitHooks"', props_text)
        self.assertIn("'$(ContinuousIntegrationBuild)' != 'true'", props_text)
        self.assertIn("'$(MSBuildProjectName)' == 'Zipper'", props_text)
        self.assertIn("setup-hook.sh", props_text)
        self.assertIn("setup-hook.bat", props_text)

        res = subprocess.run(
            ["bash", str(ROOT / "setup-hook.sh"), "--quiet"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(res.returncode, 0, res.stderr)
        self.assertEqual(res.stdout.strip(), "")


if __name__ == "__main__":
    unittest.main()
