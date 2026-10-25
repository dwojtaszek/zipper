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
        self.qa_wf_path = ROOT / ".github" / "workflows" / "qa.yml"
        self.typesafe_audit_path = ROOT / ".github" / "workflows" / "typesafe-audit.yml"
        self.typesafe_quality_path = ROOT / ".github" / "workflows" / "typesafe-quality-audit.yml"
        self.typesafe_triage_path = ROOT / ".github" / "workflows" / "typesafe-issue-triage.yml"

        self.qa_wf_text = self.qa_wf_path.read_text(encoding="utf-8")
        self.typesafe_audit_text = self.typesafe_audit_path.read_text(encoding="utf-8")
        self.typesafe_quality_text = self.typesafe_quality_path.read_text(encoding="utf-8")
        self.typesafe_triage_text = self.typesafe_triage_path.read_text(encoding="utf-8")

    def test_qa_no_continue_on_error_on_scheduled_droid_steps(self):
        """Scheduled Droid steps must not carry continue-on-error so failures produce a non-green run."""
        self.assertNotIn(
            "continue-on-error: ${{ github.event_name == 'schedule' }}",
            self.qa_wf_text,
            "Scheduled Droid steps must not carry continue-on-error",
        )
        try:
            import yaml

            parsed = yaml.safe_load(self.qa_wf_text)
            droid_step_ids = {"terminal-tools", "install-droid", "marketplace", "plugin", "qa"}
            for step in parsed.get("jobs", {}).get("qa", {}).get("steps", []):
                step_id = step.get("id")
                if step_id in droid_step_ids:
                    self.assertNotIn(
                        "continue-on-error",
                        step,
                        f"Droid QA step '{step_id}' must not specify continue-on-error",
                    )
        except ImportError:
            for step_id in ("terminal-tools", "install-droid", "marketplace", "plugin", "qa"):
                pattern = r"id:\s*" + re.escape(step_id) + r".*?continue-on-error"
                self.assertFalse(
                    re.search(pattern, self.qa_wf_text, re.DOTALL),
                    f"Step '{step_id}' should not contain continue-on-error",
                )

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

    def test_qa_workflow_emits_structured_assurance_result(self):
        """QA workflow must emit a structured assurance result with executed, scope, sha, status, reason."""
        self.assertIn("assurance-result.json", self.qa_wf_text)
        self.assertIn("assurance_status", self.qa_wf_text)
        self.assertIn("assurance_executed", self.qa_wf_text)
        self.assertIn("assurance_reason", self.qa_wf_text)

    def test_qa_weekly_issue_body_generation(self):
        """The weekly-issue job in qa.yml must distinguish check unavailable from executed and failed."""
        match = re.search(
            r"name:\s*Open or update weekly improvement review.*?run:\s*\|\s*\n(.*?)(\n\s*(?:-\s|\w+:|$))",
            self.qa_wf_text,
            re.DOTALL,
        )
        self.assertIsNotNone(match, "Could not find 'Open or update weekly improvement review' step run block")
        run_script = match.group(1)

        # Extract the heredoc block that generates weekly-issue.md
        body_match = re.search(r"(\{\s*echo \"Weekly QA run:.*?\n\s*\} > weekly-issue\.md)", run_script, re.DOTALL)
        self.assertIsNotNone(body_match, "Could not find weekly-issue.md heredoc block")
        bash_snippet = body_match.group(1)

        scenarios = [
            (
                {
                    "ASSURANCE_STATUS": "unavailable",
                    "ASSURANCE_EXECUTED": "false",
                    "ASSURANCE_REASON": "missing_factory_api_key",
                    "DROID_RESULT": "skipped",
                    "SMOKE_RESULT": "success",
                },
                "Droid QA check was UNAVAILABLE (missing_factory_api_key)",
            ),
            (
                {
                    "ASSURANCE_STATUS": "unavailable",
                    "ASSURANCE_EXECUTED": "false",
                    "ASSURANCE_REASON": "no_cli_changes",
                    "DROID_RESULT": "success",
                    "SMOKE_RESULT": "success",
                },
                "Droid QA check was UNAVAILABLE (no CLI app code changed in the audited window)",
            ),
            (
                {
                    "ASSURANCE_STATUS": "failed",
                    "ASSURANCE_EXECUTED": "false",
                    "ASSURANCE_REASON": "terminal_tools_install_failed",
                    "DROID_RESULT": "failure",
                    "SMOKE_RESULT": "success",
                },
                "Droid QA check failed during setup/prerequisites (terminal_tools_install_failed; executed: false)",
            ),
            (
                {
                    "ASSURANCE_STATUS": "failed",
                    "ASSURANCE_EXECUTED": "true",
                    "ASSURANCE_REASON": "droid_qa_execution_failed",
                    "DROID_RESULT": "failure",
                    "SMOKE_RESULT": "success",
                },
                "Droid QA check EXECUTED AND FAILED (droid_qa_execution_failed)",
            ),
            (
                {
                    "ASSURANCE_STATUS": "executed",
                    "ASSURANCE_EXECUTED": "true",
                    "ASSURANCE_REASON": "completed",
                    "DROID_RESULT": "success",
                    "SMOKE_RESULT": "success",
                },
                "If all checks passed, inspect recent changes",
            ),
            (
                {
                    "ASSURANCE_STATUS": "",
                    "ASSURANCE_EXECUTED": "",
                    "ASSURANCE_REASON": "",
                    "DROID_RESULT": "skipped",
                    "SMOKE_RESULT": "failure",
                },
                "Deterministic CLI smoke check FAILED (failure)",
            ),
            (
                {
                    "ASSURANCE_STATUS": "",
                    "ASSURANCE_EXECUTED": "",
                    "ASSURANCE_REASON": "",
                    "DROID_RESULT": "skipped",
                    "SMOKE_RESULT": "success",
                },
                "Droid QA check was SKIPPED",
            ),
            (
                {
                    "ASSURANCE_STATUS": "",
                    "ASSURANCE_EXECUTED": "",
                    "ASSURANCE_REASON": "",
                    "DROID_RESULT": "cancelled",
                    "SMOKE_RESULT": "success",
                },
                "Droid QA check was CANCELLED/ABORTED",
            ),
        ]

        for env_vars, expected_text in scenarios:
            with self.subTest(scenario=env_vars):
                with tempfile.TemporaryDirectory() as td:
                    full_env = {
                        "PATH": os.environ.get("PATH", ""),
                        "GITHUB_SHA": "abcdef0123456789abcdef0123456789abcdef01",
                        "RUN_URL": "https://github.com/dwojtaszek/zipper/actions/runs/123",
                        "DROID_AVAILABLE": "true",
                        "TOOLS_RESULT": "success",
                        "CLI_RESULT": "success",
                        "MARKETPLACE_RESULT": "success",
                        "PLUGIN_RESULT": "success",
                        "DROID_OUTCOME": "success",
                        **env_vars,
                    }
                    proc = subprocess.run(
                        ["bash", "-euo", "pipefail", "-c", bash_snippet],
                        cwd=td,
                        env=full_env,
                        capture_output=True,
                        text=True,
                    )
                    self.assertEqual(proc.returncode, 0, f"Bash snippet failed: {proc.stderr}")
                    issue_body_path = Path(td) / "weekly-issue.md"
                    self.assertTrue(issue_body_path.exists())
                    body_text = issue_body_path.read_text(encoding="utf-8")
                    self.assertIn(expected_text, body_text)

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

    def test_qa_assurance_script_logic(self):
        """Verify the QA assurance result emission logic across outcomes."""
        script = self._extract_python_inline_script(self.qa_wf_text, "Emit QA assurance result")

        test_cases = [
            (
                {"CONFIG_OUTCOME": "skipped", "SCOPE_OUTCOME": "skipped"},
                {"executed": False, "status": "failed", "reason": "scope_determination_failed", "scope": "cli"},
            ),
            (
                {"CONFIG_OUTCOME": "success", "SCOPE_OUTCOME": "failure"},
                {"executed": False, "status": "failed", "reason": "scope_determination_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "false", "VALIDATE_OUTCOME": "failure"},
                {"executed": False, "status": "failed", "reason": "qa_report_validation_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "false", "DROID_AVAILABLE": "true", "QA_OUTCOME": "success", "VALIDATE_OUTCOME": "success"},
                {"executed": False, "status": "unavailable", "reason": "no_cli_changes", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "false", "QA_OUTCOME": "skipped", "VALIDATE_OUTCOME": "skipped"},
                {"executed": False, "status": "unavailable", "reason": "missing_factory_api_key", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "QA_OUTCOME": "failure", "VALIDATE_OUTCOME": "skipped"},
                {"executed": True, "status": "failed", "reason": "droid_qa_execution_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "QA_OUTCOME": "success", "VALIDATE_OUTCOME": "failure"},
                {"executed": True, "status": "failed", "reason": "qa_report_validation_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "QA_OUTCOME": "success", "VALIDATE_OUTCOME": "success"},
                {"executed": True, "status": "executed", "reason": "completed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "TOOLS_OUTCOME": "failure", "QA_OUTCOME": "skipped"},
                {"executed": False, "status": "failed", "reason": "terminal_tools_install_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "CLI_OUTCOME": "failure", "QA_OUTCOME": "skipped"},
                {"executed": False, "status": "failed", "reason": "droid_cli_install_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "MARKETPLACE_OUTCOME": "failure", "QA_OUTCOME": "skipped"},
                {"executed": False, "status": "failed", "reason": "marketplace_registration_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "PLUGIN_OUTCOME": "failure", "QA_OUTCOME": "skipped"},
                {"executed": False, "status": "failed", "reason": "plugin_install_failed", "scope": "cli"},
            ),
            (
                {"CLI_AFFECTED": "true", "DROID_AVAILABLE": "true", "QA_OUTCOME": "cancelled"},
                {"executed": False, "status": "failed", "reason": "incomplete_or_aborted", "scope": "cli"},
            ),
        ]

        for env_overrides, expected in test_cases:
            with self.subTest(env=env_overrides):
                with tempfile.TemporaryDirectory() as td:
                    output_file = Path(td) / "output.txt"
                    env = {
                        "PATH": os.environ.get("PATH", ""),
                        "COMMIT_SHA": "0123456789abcdef0123456789abcdef01234567",
                        "CONFIG_OUTCOME": "success",
                        "SCOPE_OUTCOME": "success",
                        "GITHUB_STEP_SUMMARY": str(Path(td) / "summary.md"),
                        "GITHUB_OUTPUT": str(output_file),
                        **env_overrides,
                    }
                    proc = subprocess.run(
                        ["python3", "-c", script],
                        cwd=td,
                        env=env,
                        capture_output=True,
                        text=True,
                    )
                    self.assertEqual(proc.returncode, 0, f"Script failed: {proc.stderr}")
                    result_file = Path(td) / "qa-results" / "assurance-result.json"
                    self.assertTrue(result_file.exists(), "assurance-result.json was not created")
                    data = json.loads(result_file.read_text(encoding="utf-8"))
                    self.assertEqual(data["executed"], expected["executed"])
                    self.assertEqual(data["status"], expected["status"])
                    self.assertEqual(data["reason"], expected["reason"])
                    self.assertEqual(data["scope"], expected["scope"])
                    self.assertEqual(data["sha"], "0123456789abcdef0123456789abcdef01234567")

                    outputs = dict(
                        line.split("=", 1)
                        for line in output_file.read_text(encoding="utf-8").splitlines()
                        if "=" in line
                    )
                    self.assertEqual(outputs.get("status"), expected["status"])
                    self.assertEqual(outputs.get("executed"), "true" if expected["executed"] else "false")
                    self.assertEqual(outputs.get("reason"), expected["reason"])

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
