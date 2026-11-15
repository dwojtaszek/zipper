"""Tests for scheduled assurance structured results and failure gating (#1225)."""
from __future__ import annotations

import json
import os
import re
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class ScheduledAssuranceWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.typesafe_audit_path = ROOT / ".github" / "workflows" / "typesafe-audit.yml"
        self.typesafe_quality_path = ROOT / ".github" / "workflows" / "typesafe-quality-audit.yml"
        self.typesafe_triage_path = ROOT / ".github" / "workflows" / "typesafe-issue-triage.yml"

        self.typesafe_audit_text = self.typesafe_audit_path.read_text(encoding="utf-8")
        self.typesafe_quality_text = self.typesafe_quality_path.read_text(encoding="utf-8")
        self.typesafe_triage_text = self.typesafe_triage_path.read_text(encoding="utf-8")

    def test_no_if_no_files_found_ignore_in_typesafe_workflows(self):
        """TypeSafe workflows must not silently ignore missing artifacts with if-no-files-found: ignore."""
        for name, text in [
            ("typesafe-audit.yml", self.typesafe_audit_text),
            ("typesafe-quality-audit.yml", self.typesafe_quality_text),
            ("typesafe-issue-triage.yml", self.typesafe_triage_text),
        ]:
            with self.subTest(workflow=name):
                self.assertNotIn(
                    "if-no-files-found: ignore",
                    text,
                    f"{name} must not contain 'if-no-files-found: ignore'",
                )
                self.assertIn(
                    "if-no-files-found: warn",
                    text,
                    f"{name} must use 'if-no-files-found: warn' instead of ignore",
                )

    def test_neutral_skip_summary_schema_and_outputs(self):
        """Neutral skip summary in TypeSafe workflows must produce valid assurance-result.json and step outputs."""
        for name, wf_text, scope in [
            ("typesafe-audit.yml", self.typesafe_audit_text, "requirements"),
            ("typesafe-quality-audit.yml", self.typesafe_quality_text, "quality"),
            ("typesafe-issue-triage.yml", self.typesafe_triage_text, "triage"),
        ]:
            with self.subTest(workflow=name):
                match = re.search(
                    r"name:\s*Neutral skip summary.*?run:\s*\|\s*\n(.*?)(?=\n\s*-\s+name:|\Z)",
                    wf_text,
                    re.DOTALL,
                )
                self.assertIsNotNone(match, f"Could not find Neutral skip summary in {name}")
                script = textwrap.dedent(match.group(1))

                # Replace matrix.check and github.sha template placeholders
                script_expanded = (
                    script.replace("${{ matrix.check }}", scope)
                    .replace("${{ github.sha }}", "0123456789abcdef0123456789abcdef01234567")
                )

                with tempfile.TemporaryDirectory() as td:
                    output_file = Path(td) / "output.txt"
                    summary_file = Path(td) / "summary.md"
                    env = {
                        "PATH": os.environ.get("PATH", ""),
                        "GITHUB_OUTPUT": str(output_file),
                        "GITHUB_STEP_SUMMARY": str(summary_file),
                    }
                    proc = subprocess.run(
                        ["bash", "-euo", "pipefail", "-c", script_expanded],
                        cwd=td,
                        env=env,
                        capture_output=True,
                        text=True,
                    )
                    self.assertEqual(proc.returncode, 0, f"Script failed: {proc.stderr}")

                    # Verify assurance-result.json
                    json_paths = list(Path(td).glob("results/**/assurance-result.json"))
                    self.assertTrue(json_paths, f"assurance-result.json not found in {td}")
                    data = json.loads(json_paths[0].read_text(encoding="utf-8"))
                    self.assertEqual(data["executed"], False)
                    self.assertEqual(data["status"], "unavailable")
                    self.assertEqual(data["reason"], "missing_typesafe_api_key")
                    self.assertEqual(data["sha"], "0123456789abcdef0123456789abcdef01234567")
                    self.assertEqual(data["scope"], scope)

                    # Verify GITHUB_OUTPUT
                    outputs = dict(
                        line.split("=", 1)
                        for line in output_file.read_text(encoding="utf-8").splitlines()
                        if "=" in line
                    )
                    self.assertEqual(outputs.get("status"), "unavailable")
                    self.assertEqual(outputs.get("executed"), "false")
                    self.assertEqual(outputs.get("reason"), "missing_typesafe_api_key")

    def _extract_python_inline_script(self, workflow_text: str, step_name: str) -> str:
        """Extract the python inline script (python3 - <<'PY' ... PY) from a workflow step."""
        step_match = re.search(
            r"name:\s*" + re.escape(step_name) + r".*?run:\s*\|\s*\n(.*?)(?=\n\s*-\s+name:|\Z)",
            workflow_text,
            re.DOTALL,
        )
        self.assertIsNotNone(step_match, f"Could not find step named '{step_name}'")
        run_block = step_match.group(1)
        py_match = re.search(r"python3\s+-\s*<<['\"]?PY['\"]?\s*\n(.*?\n)\s*PY", run_block, re.DOTALL)
        self.assertIsNotNone(py_match, f"Could not find inline python block in step '{step_name}'")
        return textwrap.dedent(py_match.group(1))

    def _run_assurance_script(
        self, script: str, target_dir_rel: str, setup_files: dict, extra_env: dict
    ) -> dict:
        """Run an inline assurance script in an isolated tempdir and return the parsed JSON result."""
        with tempfile.TemporaryDirectory() as td:
            results_dir = Path(td) / target_dir_rel
            results_dir.mkdir(parents=True, exist_ok=True)
            for filename, content in setup_files.items():
                (results_dir / filename).write_text(content, encoding="utf-8")
            env = {
                "PATH": os.environ.get("PATH", ""),
                "GITHUB_STEP_SUMMARY": str(Path(td) / "summary.md"),
                "GITHUB_OUTPUT": str(Path(td) / "output.txt"),
                **extra_env,
            }
            proc = subprocess.run(["python3", "-c", script], cwd=td, env=env, capture_output=True, text=True)
            self.assertEqual(proc.returncode, 0, proc.stderr)
            res_file = results_dir / "assurance-result.json"
            self.assertTrue(res_file.exists(), f"Expected {res_file} to exist")
            return json.loads(res_file.read_text(encoding="utf-8"))

    def test_typesafe_quality_assurance_script_logic(self):
        """Verify the quality assurance result emission logic across outcomes."""
        script = self._extract_python_inline_script(self.typesafe_quality_text, "Emit quality assurance result")

        cases = [
            ({"ranking.json": "{}"}, {"COMMIT_SHA": "sha123", "RANKING_OUTCOME": "success"}, "executed", True, "completed"),
            ({}, {"COMMIT_SHA": "sha123", "RANKING_OUTCOME": "success"}, "unavailable", False, "remote_failure"),
            ({}, {"COMMIT_SHA": "sha123", "RANKING_OUTCOME": "failure"}, "failed", False, "check_failed"),
        ]
        for setup, env_vars, exp_status, exp_exec, exp_reason in cases:
            data = self._run_assurance_script(script, "results/typesafe-quality", setup, env_vars)
            self.assertEqual((data["status"], data["executed"], data["reason"]), (exp_status, exp_exec, exp_reason))

    def test_typesafe_audit_assurance_script_logic(self):
        """Verify the audit assurance result emission logic across outcomes."""
        script = self._extract_python_inline_script(self.typesafe_audit_text, "Emit audit assurance result")

        cases = [
            ({"traceability.json": "{}"}, {"COMMIT_SHA": "sha456", "CHECK": "traceability"}, "executed", True, "completed", "traceability"),
            ({"report.json": "{}"}, {"COMMIT_SHA": "sha456", "CHECK": "foundation"}, "executed", True, "completed", "foundation"),
            ({}, {"COMMIT_SHA": "sha456", "CHECK": "requirements"}, "unavailable", False, "remote_failure_or_unmet_preconditions", "requirements"),
        ]
        for setup, env_vars, exp_status, exp_exec, exp_reason, exp_scope in cases:
            data = self._run_assurance_script(script, "results/typesafe-audit", setup, env_vars)
            self.assertEqual((data["status"], data["executed"], data["reason"], data["scope"]), (exp_status, exp_exec, exp_reason, exp_scope))

    def test_typesafe_triage_assurance_script_logic(self):
        """Verify the triage assurance result emission logic across outcomes."""
        script = self._extract_python_inline_script(self.typesafe_triage_text, "Emit triage assurance result")

        cases = [
            ({"triage.json": "{}"}, {"COMMIT_SHA": "sha789", "TRIAGE_OUTCOME": "success"}, "executed", True, "completed"),
            ({}, {"COMMIT_SHA": "sha789", "TRIAGE_OUTCOME": "success"}, "unavailable", False, "remote_failure"),
            ({}, {"COMMIT_SHA": "sha789", "TRIAGE_OUTCOME": "failure"}, "failed", False, "check_failed"),
        ]
        for setup, env_vars, exp_status, exp_exec, exp_reason in cases:
            data = self._run_assurance_script(script, "results/typesafe-triage", setup, env_vars)
            self.assertEqual((data["status"], data["executed"], data["reason"]), (exp_status, exp_exec, exp_reason))

    def test_typesafe_pr_spec_assurance_script_logic(self):
        """Verify the pr-spec assurance result emission logic across outcomes."""
        script = self._extract_python_inline_script(self.typesafe_audit_text, "Emit PR spec assurance result")

        cases = [
            ({"pr-spec.json": "{}"}, {"COMMIT_SHA": "sha999"}, "executed", True, "completed"),
            ({}, {"COMMIT_SHA": "sha999"}, "unavailable", False, "remote_failure_or_missing_spec"),
        ]
        for setup, env_vars, exp_status, exp_exec, exp_reason in cases:
            data = self._run_assurance_script(script, "results/typesafe-audit", setup, env_vars)
            self.assertEqual((data["status"], data["executed"], data["reason"]), (exp_status, exp_exec, exp_reason))


if __name__ == "__main__":
    unittest.main()
