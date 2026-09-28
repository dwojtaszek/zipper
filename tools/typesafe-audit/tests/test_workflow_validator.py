import os
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import workflow_validator  # noqa: E402
from workflow_validator import (
    parse_workflow_yaml,
    validate_workflow,
    validate_permissions,
    validate_advisory_mode,
    validate_secret_guards,
    WorkflowValidationError,
)


WORKFLOW_PATH = os.path.join(
    os.path.dirname(__file__), "..", "..", "..", ".github", "workflows", "typesafe-audit.yml"
)


class TestWorkflowValidator(unittest.TestCase):
    def setUp(self):
        with open(WORKFLOW_PATH, "r", encoding="utf-8") as f:
            self.workflow_text = f.read()
        self.workflow = parse_workflow_yaml(self.workflow_text)

    def test_actual_workflow_passes_all_validations(self):
        errors = validate_workflow(WORKFLOW_PATH)
        self.assertEqual(errors, [])

    def test_all_three_actual_workflows_pass_validations(self):
        workflows_dir = os.path.join(os.path.dirname(__file__), "..", "..", "..", ".github", "workflows")
        for fname in ("typesafe-audit.yml", "typesafe-issue-triage.yml", "typesafe-quality-audit.yml"):
            wpath = os.path.join(workflows_dir, fname)
            errors = validate_workflow(wpath)
            self.assertEqual(errors, [], f"Workflow {fname} failed validation: {errors}")

    def test_triage_job_missing_secret_condition_fails(self):
        workflows_dir = os.path.join(os.path.dirname(__file__), "..", "..", "..", ".github", "workflows")
        triage_path = os.path.join(workflows_dir, "typesafe-issue-triage.yml")
        with open(triage_path, "r", encoding="utf-8") as f:
            triage_text = f.read()
        mutated = triage_text.replace(
            "if: steps.secret.outputs.available == 'true'",
            "if: success()",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("secret availability guard", str(ctx.exception).lower())

    def test_quality_job_missing_secret_condition_fails(self):
        workflows_dir = os.path.join(os.path.dirname(__file__), "..", "..", "..", ".github", "workflows")
        quality_path = os.path.join(workflows_dir, "typesafe-quality-audit.yml")
        with open(quality_path, "r", encoding="utf-8") as f:
            quality_text = f.read()
        mutated = quality_text.replace(
            "if: steps.secret.outputs.available == 'true'",
            "if: success()",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("secret availability guard", str(ctx.exception).lower())

    # ---- Regression 1: Advisory mode & line continuations ----

    def test_runner_with_strict_on_continuation_line_fails(self):
        mutated = self.workflow_text.replace(
            "--mode live \\",
            "--mode live \\\n            --strict \\",
        )
        self.assertNotEqual(mutated, self.workflow_text)
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_advisory_mode(parsed)
        self.assertIn("runner.py must stay advisory (no --strict)", str(ctx.exception))

    def test_runner_with_strict_same_line_fails(self):
        mutated = self.workflow_text.replace(
            "python3 tools/typesafe-audit/runner.py \\",
            "python3 tools/typesafe-audit/runner.py --strict \\",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_advisory_mode(parsed)
        self.assertIn("runner.py must stay advisory (no --strict)", str(ctx.exception))

    def test_other_tools_using_strict_do_not_trigger_runner_check(self):
        # validate-req-traceability.sh --strict is legitimately used in another job/step
        validate_advisory_mode(self.workflow)

    # ---- Regression 2: Effective permissions scope ----

    def test_job_level_write_override_fails(self):
        mutated = self.workflow_text.replace(
            "  audit:\n    runs-on: ubuntu-latest",
            "  audit:\n    runs-on: ubuntu-latest\n    permissions:\n      contents: write",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_permissions(parsed)
        self.assertIn("permission", str(ctx.exception).lower())

    def test_job_level_write_all_fails(self):
        mutated = self.workflow_text.replace(
            "  audit:\n    runs-on: ubuntu-latest",
            "  audit:\n    runs-on: ubuntu-latest\n    permissions: write-all",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_permissions(parsed)
        self.assertIn("permission", str(ctx.exception).lower())

    def test_commented_permission_with_job_write_fails(self):
        mutated = self.workflow_text.replace(
            "permissions:\n  contents: read",
            "# permissions:\n#   contents: read",
        ).replace(
            "  audit:\n    runs-on: ubuntu-latest",
            "  audit:\n    runs-on: ubuntu-latest\n    permissions:\n      contents: write",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError):
            validate_permissions(parsed)

    # ---- Regression 3: Secret availability guard ----

    def test_audit_job_missing_secret_condition_fails(self):
        mutated = self.workflow_text.replace(
            "if: steps.secret.outputs.available == 'true'",
            "# guard removed",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("secret", str(ctx.exception).lower())

    def test_runner_step_missing_secret_guard_fails(self):
        mutated = self.workflow_text.replace(
            "if: steps.secret.outputs.available == 'true'",
            "if: success()",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("secret availability guard", str(ctx.exception).lower())

    def test_pr_spec_missing_fork_guard_fails(self):
        mutated = self.workflow_text.replace(
            "if: github.event_name == 'pull_request' && github.event.pull_request.head.repo.fork == false",
            "if: github.event_name == 'pull_request'",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("fork", str(ctx.exception).lower())

    def test_pr_spec_bare_fork_token_fails(self):
        # Bare mention of head.repo.fork without == false should be rejected
        mutated = self.workflow_text.replace(
            "github.event.pull_request.head.repo.fork == false",
            "github.event.pull_request.head.repo.fork",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("fork", str(ctx.exception).lower())

    def test_fork_guard_with_disjunction_bypass_fails(self):
        mutated = self.workflow_text.replace(
            "github.event.pull_request.head.repo.fork == false",
            "true || github.event.pull_request.head.repo.fork == false",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("fork", str(ctx.exception).lower())

    def test_secret_guard_with_disjunction_bypass_fails(self):
        mutated = self.workflow_text.replace(
            "steps.secret.outputs.available == 'true'",
            "true || steps.secret.outputs.available == 'true'",
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_secret_guards(parsed)
        self.assertIn("secret", str(ctx.exception).lower())

    def test_quoted_strict_flag_fails(self):
        mutated = self.workflow_text.replace(
            "python3 tools/typesafe-audit/runner.py \\",
            'python3 tools/typesafe-audit/runner.py "--strict" \\',
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_advisory_mode(parsed)
        self.assertIn("runner.py must stay advisory (no --strict)", str(ctx.exception))

    # ---- Regression 4: Malformed or non-mapping YAML ----

    def test_non_mapping_root_fails(self):
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_permissions(["not", "a", "dict"])
        self.assertIn("YAML mapping", str(ctx.exception))

    def test_missing_jobs_mapping_fails(self):
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_advisory_mode({"permissions": {"contents": "read"}, "jobs": "not_a_dict"})
        self.assertIn("YAML mapping", str(ctx.exception))


if __name__ == "__main__":
    unittest.main()
