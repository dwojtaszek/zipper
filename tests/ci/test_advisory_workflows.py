"""Regressions for advisory workflow exit code classification under bash -e (#1224)."""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class AdvisoryWorkflowExitClassificationTests(unittest.TestCase):
    def setUp(self):
        self.quality_wf_path = ROOT / ".github" / "workflows" / "typesafe-quality-audit.yml"
        self.triage_wf_path = ROOT / ".github" / "workflows" / "typesafe-issue-triage.yml"
        self.quality_wf_text = self.quality_wf_path.read_text(encoding="utf-8")
        self.triage_wf_text = self.triage_wf_path.read_text(encoding="utf-8")

    def _get_step_run(self, workflow_text: str, step_name: str) -> str:
        try:
            import yaml
            parsed = yaml.safe_load(workflow_text)
            for job in parsed.get("jobs", {}).values():
                for step in job.get("steps", []):
                    if step.get("name") == step_name:
                        return step.get("run", "")
        except ImportError:
            pass

        return self._extract_step_run_fallback(workflow_text, step_name)

    @staticmethod
    def _extract_step_run_fallback(workflow_text: str, step_name: str) -> str:
        """Fallback manual extraction when PyYAML is unavailable."""
        lines = workflow_text.splitlines()
        in_step = False
        in_run = False
        run_indent = None
        run_lines = []
        for line in lines:
            stripped = line.strip()
            if f"name: {step_name}" in line or f'name: "{step_name}"' in line or f"name: '{step_name}'" in line:
                in_step = True
                in_run = False
                continue
            if in_step:
                if stripped.startswith("- name:"):
                    break
                if not in_run:
                    if stripped.startswith("run:"):
                        in_run = True
                    continue
                if not stripped:
                    run_lines.append("")
                    continue
                indent = len(line) - len(line.lstrip())
                if run_indent is None:
                    run_indent = indent
                if indent < run_indent:
                    break
                run_lines.append(line[run_indent:])
        return "\n".join(run_lines)

    def test_step_run_extraction_fallback_matches_pyyaml(self):
        """Fallback extraction must extract the exact run block when PyYAML is unavailable."""
        for wf_text, step_name in [
            (self.quality_wf_text, "Run TypeSafe quality ranking (advisory)"),
            (self.quality_wf_text, "Mutation testing shard (Phase 2)"),
            (self.triage_wf_text, "Run TypeSafe triage (dry run, advisory)"),
        ]:
            with self.subTest(step=step_name):
                fallback = self._extract_step_run_fallback(wf_text, step_name)
                self.assertTrue(len(fallback) > 0, f"Fallback extraction failed for '{step_name}'")
                try:
                    import yaml
                    parsed = yaml.safe_load(wf_text)
                    pyyaml_result = None
                    for job in parsed.get("jobs", {}).values():
                        for step in job.get("steps", []):
                            if step.get("name") == step_name:
                                pyyaml_result = step.get("run", "")
                                break
                    if pyyaml_result is not None:
                        self.assertEqual(fallback.strip(), pyyaml_result.strip())
                except ImportError:
                    pass

    def test_quality_audit_workflow_guards_run_check_and_stryker_syntax(self):
        """Quality audit workflow must not contain unguarded commands immediately followed by rc=$?."""
        # run_check.py must be guarded so bash -e does not kill the step before rc=$?
        self.assertRegex(
            self.quality_wf_text,
            r"(?s)python3 tools/typesafe-audit/quality/run_check\.py.*?\|\|\s*rc=\$\?",
            "run_check.py in typesafe-quality-audit.yml must be guarded with || rc=$?",
        )
        # stryker invocation must be guarded so bash -e does not kill the step before STRYKER_RC=$?
        self.assertRegex(
            self.quality_wf_text,
            r"(?s)dotnet stryker.*?\|\|\s*STRYKER_RC=\$\?",
            "dotnet stryker in typesafe-quality-audit.yml must be guarded with || STRYKER_RC=$?",
        )

    def test_issue_triage_workflow_guards_run_check_syntax(self):
        """Issue triage workflow must not contain unguarded run_check.py immediately followed by rc=$?."""
        self.assertRegex(
            self.triage_wf_text,
            r"(?s)python3 tools/typesafe-audit/triage/run_check\.py.*?\|\|\s*rc=\$\?",
            "run_check.py in typesafe-issue-triage.yml must be guarded with || rc=$?",
        )

    def _assert_advisory_exit_classification(self, step_run: str, summary_phrase: str):
        """Verify advisory step exit code classification (0 success, 3 neutral, 2 failure) under bash -eo pipefail."""
        script = re.sub(r"\$\{\{[^}]+\}\}", "dummy_val", step_run)
        for mock_rc, expected_exit, expect_summary in [
            (0, 0, False),
            (3, 0, True),  # Advisory neutral: remote failure exits 0 with summary
            (2, 2, False), # Config/input error fails the step visibly
        ]:
            with self.subTest(mock_rc=mock_rc):
                with tempfile.TemporaryDirectory() as td:
                    bin_dir = os.path.join(td, "bin")
                    os.makedirs(bin_dir)
                    mock_py = os.path.join(bin_dir, "python3")
                    with open(mock_py, "w", encoding="utf-8") as f:
                        f.write(f"#!/bin/sh\nexit {mock_rc}\n")
                    os.chmod(mock_py, 0o755)

                    summary_file = os.path.join(td, "step_summary.md")
                    env = os.environ.copy()
                    env["PATH"] = f"{bin_dir}:{env.get('PATH', '')}"
                    env["GITHUB_STEP_SUMMARY"] = summary_file

                    proc = subprocess.run(
                        ["bash", "-eo", "pipefail", "-c", script],
                        cwd=td,
                        env=env,
                        capture_output=True,
                        text=True,
                    )

                    self.assertEqual(
                        proc.returncode,
                        expected_exit,
                        f"Expected exit code {expected_exit} for child exit {mock_rc}, got {proc.returncode}. stderr: {proc.stderr}",
                    )

                    summary_content = ""
                    if os.path.exists(summary_file):
                        with open(summary_file, "r", encoding="utf-8") as sf:
                            summary_content = sf.read()

                    if expect_summary:
                        self.assertIn(
                            summary_phrase,
                            summary_content,
                            f"Exit code 3 must append '{summary_phrase}' to GITHUB_STEP_SUMMARY",
                        )
                    else:
                        self.assertNotIn(
                            summary_phrase,
                            summary_content,
                            f"Exit code {mock_rc} must not append '{summary_phrase}'",
                        )

    def _run_stryker_step(self, script: str, td: str, dotnet_rc: int, python_output: str = ""):
        """Execute the Stryker workflow step in a temporary directory with mocked tools."""
        bin_dir = os.path.join(td, "bin")
        os.makedirs(bin_dir)
        os.makedirs(os.path.join(td, "src"))

        mock_timeout = os.path.join(bin_dir, "timeout")
        with open(mock_timeout, "w", encoding="utf-8") as f:
            f.write("#!/bin/sh\nshift\nexec \"$@\"\n")
        os.chmod(mock_timeout, 0o755)

        mock_dotnet = os.path.join(bin_dir, "dotnet")
        with open(mock_dotnet, "w", encoding="utf-8") as f:
            f.write(f"#!/bin/sh\nexit {dotnet_rc}\n")
        os.chmod(mock_dotnet, 0o755)

        mock_py = os.path.join(bin_dir, "python3")
        with open(mock_py, "w", encoding="utf-8") as f:
            f.write(f"#!/bin/sh\n{python_output}\nexit 0\n")
        os.chmod(mock_py, 0o755)

        env = os.environ.copy()
        env["PATH"] = f"{bin_dir}:{env.get('PATH', '')}"
        env["SCOPE"] = "LoadFiles/**"
        env["SHARD_TIMEOUT"] = "45"

        return subprocess.run(
            ["bash", "-eo", "pipefail", "-c", script],
            cwd=td,
            env=env,
            capture_output=True,
            text=True,
        )

    @unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
    def test_quality_audit_step_exit_classification_under_bash_e(self):
        """Quality audit step must handle exit codes 0, 2, and 3 correctly under bash -eo pipefail."""
        step_run = self._get_step_run(self.quality_wf_text, "Run TypeSafe quality ranking (advisory)")
        self.assertTrue(step_run, "Could not locate 'Run TypeSafe quality ranking (advisory)' step script")
        self._assert_advisory_exit_classification(
            step_run, "Quality ranking unavailable (remote failure; advisory)"
        )

    @unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
    def test_quality_audit_stryker_step_error_propagation_under_bash_e(self):
        """Stryker step under bash -eo pipefail must emit ::error:: and exit with failure code on error."""
        step_run = self._get_step_run(self.quality_wf_text, "Mutation testing shard (Phase 2)")
        self.assertTrue(step_run, "Could not locate 'Mutation testing shard (Phase 2)' step script")
        script = re.sub(r"\$\{\{[^}]+\}\}", "dummy_val", step_run)

        # Test failure cases across multiple non-zero exit codes (e.g. 1, 2, 124)
        for exit_code in (1, 2, 124):
            with self.subTest(exit_code=exit_code):
                with tempfile.TemporaryDirectory() as td:
                    proc = self._run_stryker_step(script, td, dotnet_rc=exit_code)
                    self.assertEqual(
                        proc.returncode,
                        exit_code,
                        f"Expected Stryker failure exit code {exit_code}, got {proc.returncode}. stdout: {proc.stdout}, stderr: {proc.stderr}",
                    )
                    self.assertIn(
                        f"::error::dotnet stryker exited with code {exit_code}",
                        proc.stdout + proc.stderr,
                        "Stryker failure must emit GitHub Actions error annotation",
                    )

        # Test success case
        with tempfile.TemporaryDirectory() as td:
            proc = self._run_stryker_step(
                script, td, dotnet_rc=0, python_output='echo "normalized in $(pwd)"'
            )
            self.assertEqual(
                proc.returncode,
                0,
                f"Expected Stryker success exit code 0, got {proc.returncode}. stdout: {proc.stdout}, stderr: {proc.stderr}",
            )
            self.assertNotIn(
                "::error::",
                proc.stdout + proc.stderr,
                "Stryker success must not emit GitHub Actions error annotation",
            )
            self.assertIn(
                f"normalized in {os.path.realpath(td)}",
                proc.stdout + proc.stderr,
                "normalize_report.py must execute in the repo root directory",
            )

    @unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
    def test_issue_triage_step_exit_classification_under_bash_e(self):
        """Issue triage step must handle exit codes 0, 2, and 3 correctly under bash -eo pipefail."""
        step_run = self._get_step_run(self.triage_wf_text, "Run TypeSafe triage (dry run, advisory)")
        self.assertTrue(step_run, "Could not locate 'Run TypeSafe triage (dry run, advisory)' step script")
        self._assert_advisory_exit_classification(
            step_run, "Issue triage unavailable (remote failure; advisory)"
        )


if __name__ == "__main__":
    unittest.main()
