#!/usr/bin/env python3
"""YAML-aware validator for TypeSafe audit GitHub Actions workflow (#1046).

Validates:
1. Advisory mode: runner.py invocations in run: blocks must not use --strict,
   even across line continuations. (Allows --strict for other tools like
   validate-req-traceability.sh).
2. Effective permissions: top-level contents: read must not be overridden by
   job-level write permissions (e.g. contents: write or write-all).
3. Secret-availability and fork guards: audit job must gate execution on secret
   availability, and pr-spec-review must guard against fork PRs.
"""

import os
import shlex
import sys


class WorkflowValidationError(Exception):
    """Raised when a workflow fails structural or security validation."""
    pass


def parse_workflow_yaml(text: str) -> dict:
    """Parse workflow YAML into dict, using PyYAML if available or fallback parser."""
    try:
        import yaml
        return yaml.safe_load(text)
    except ImportError:
        pass

    lines = text.splitlines()

    def get_indent(line: str) -> int:
        return len(line) - len(line.lstrip(" "))

    def parse_block(idx: int, current_indent: int):
        result = {}
        is_list = False
        list_result = []

        while idx < len(lines):
            line = lines[idx]
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                idx += 1
                continue

            indent = get_indent(line)
            if indent < current_indent:
                break

            if stripped.startswith("- "):
                is_list = True
                item_text = stripped[2:].strip()
                if ":" in item_text and not item_text.startswith("{"):
                    sub_indent = indent + 2
                    first_line = " " * sub_indent + item_text
                    sub_lines = [first_line]
                    idx += 1
                    while idx < len(lines):
                        next_line = lines[idx]
                        if not next_line.strip() or next_line.strip().startswith("#"):
                            idx += 1
                            continue
                        if get_indent(next_line) <= indent:
                            break
                        sub_lines.append(next_line)
                        idx += 1
                    list_result.append(parse_workflow_yaml("\n".join(sub_lines)))
                    continue
                else:
                    list_result.append(item_text)
                    idx += 1
                    continue

            if ":" in stripped:
                colon_idx = stripped.index(":")
                key = stripped[:colon_idx].strip()
                val = stripped[colon_idx + 1:].strip()
                if val in ("|", "|-", ">", ">-"):
                    idx += 1
                    scalar_lines = []
                    scalar_indent = None
                    while idx < len(lines):
                        s_line = lines[idx]
                        if not s_line.strip():
                            scalar_lines.append("")
                            idx += 1
                            continue
                        s_indent = get_indent(s_line)
                        if scalar_indent is None:
                            if s_indent <= indent:
                                break
                            scalar_indent = s_indent
                        elif s_indent < scalar_indent:
                            break
                        scalar_lines.append(s_line[scalar_indent:])
                        idx += 1
                    result[key] = "\n".join(scalar_lines)
                    continue
                elif val == "" or val.startswith("#"):
                    idx += 1
                    sub_lines = []
                    while idx < len(lines):
                        n_line = lines[idx]
                        if not n_line.strip() or n_line.strip().startswith("#"):
                            idx += 1
                            continue
                        if get_indent(n_line) <= indent:
                            break
                        sub_lines.append(n_line)
                        idx += 1
                    result[key] = parse_workflow_yaml("\n".join(sub_lines))
                    continue
                else:
                    if not (val.startswith("\"") or val.startswith("\'")):
                        val = val.split(" #")[0].strip()
                    else:
                        if val.startswith("\"") and val.endswith("\""):
                            val = val[1:-1]
                        elif val.startswith("\'") and val.endswith("\'"):
                            val = val[1:-1]
                    result[key] = val
                    idx += 1
                    continue
            idx += 1

        return list_result if is_list else result

    return parse_block(0, 0)


def _require_mapping(obj: object, name: str) -> dict:
    if not isinstance(obj, dict):
        raise WorkflowValidationError(f"{name} must be a YAML mapping/object, got {type(obj).__name__}")
    return obj


def validate_permissions(workflow: dict) -> None:
    """Validate that workflow has least-privilege permissions without write overrides."""
    _require_mapping(workflow, "Workflow root")
    top_permissions = workflow.get("permissions")
    if not top_permissions:
        raise WorkflowValidationError("Missing top-level permissions block")

    if isinstance(top_permissions, str):
        if "write" in top_permissions.lower():
            raise WorkflowValidationError(f"Top-level permissions must not be write: {top_permissions}")
    elif isinstance(top_permissions, dict):
        for scope, level in top_permissions.items():
            if str(level).lower() == "write" or "write" in str(level).lower():
                raise WorkflowValidationError(f"Top-level permission for '{scope}' must not be write: {level}")
        if top_permissions.get("contents") != "read":
            raise WorkflowValidationError(f"Top-level contents permission must be 'read', got: {top_permissions.get('contents')}")

    jobs = _require_mapping(workflow.get("jobs", {}), "Jobs section")
    for job_name, job_def in jobs.items():
        if not isinstance(job_def, dict):
            continue
        job_perms = job_def.get("permissions")
        if not job_perms:
            continue
        if isinstance(job_perms, str):
            if "write" in job_perms.lower():
                raise WorkflowValidationError(f"Job '{job_name}' overrides permissions with write: {job_perms}")
        elif isinstance(job_perms, dict):
            for scope, level in job_perms.items():
                if str(level).lower() == "write" or "write" in str(level).lower():
                    raise WorkflowValidationError(f"Job '{job_name}' overrides '{scope}' permission with write: {level}")


def _extract_commands(run_script: str) -> list[str]:
    raw_lines = run_script.splitlines()
    reconstructed_lines = []
    current: list[str] = []
    for r_line in raw_lines:
        stripped = r_line.strip()
        if stripped.endswith("\\"):
            current.append(stripped[:-1].rstrip())
        else:
            current.append(stripped)
            reconstructed_lines.append(" ".join(current))
            current = []
    if current:
        reconstructed_lines.append(" ".join(current))
    return reconstructed_lines


def _executes_runner(tokens: list[str]) -> bool:
    if not tokens:
        return False
    # If the first token itself is runner.py
    if tokens[0].endswith("runner.py"):
        return True
    # If standard shell utilities (echo, printf, cat, etc.) have runner.py as an argument, ignore
    first_cmd = os.path.basename(tokens[0])
    if first_cmd in ("echo", "printf", "cat", "grep", "sed", "awk"):
        return False
    # Check if any token targets runner.py
    return any(t.endswith("runner.py") for t in tokens)


def _step_executes_runner(run_script: str) -> bool:
    for cmd in _extract_commands(run_script):
        try:
            tokens = shlex.split(cmd)
        except ValueError:
            tokens = cmd.split()
        if _executes_runner(tokens):
            return True
    return False


def _is_effective_secret_guard(condition: str) -> bool:
    cond = condition.strip().strip("${{").strip("}}").strip()
    if not cond or "||" in cond:
        return False
    clauses = [c.strip() for c in cond.split("&&")]
    for clause in clauses:
        clean = clause.replace('"', "'")
        if clean in ("steps.secret.outputs.available == 'true'", "'true' == steps.secret.outputs.available"):
            return True
    return False


def _is_effective_fork_guard(condition: str) -> bool:
    cond = condition.strip().strip("${{").strip("}}").strip()
    if not cond or "||" in cond:
        return False
    clauses = [c.strip() for c in cond.split("&&")]
    for clause in clauses:
        if clause == "github.event.pull_request.head.repo.fork == false":
            return True
    return False


def validate_advisory_mode(workflow: dict) -> None:
    """Validate that runner.py is never invoked with --strict, even across line continuations."""
    _require_mapping(workflow, "Workflow root")
    jobs = _require_mapping(workflow.get("jobs", {}), "Jobs section")
    for job_name, job_def in jobs.items():
        if not isinstance(job_def, dict):
            continue
        steps = job_def.get("steps", [])
        if not isinstance(steps, list):
            continue
        for step in steps:
            if not isinstance(step, dict):
                continue
            run_cmd = step.get("run")
            if not run_cmd or not isinstance(run_cmd, str):
                continue

            for line in _extract_commands(run_cmd):
                try:
                    tokens = shlex.split(line)
                except ValueError:
                    tokens = line.split()

                if _executes_runner(tokens):
                    lower_tokens = [t.lower() for t in tokens]
                    if "--strict" in lower_tokens:
                        raise WorkflowValidationError(
                            f"In job '{job_name}', step '{step.get('name', 'unnamed')}': "
                            "runner.py must stay advisory (no --strict)"
                        )


def validate_secret_guards(workflow: dict) -> None:
    """Validate that secret availability and fork conditions are enforced."""
    _require_mapping(workflow, "Workflow root")
    jobs = _require_mapping(workflow.get("jobs", {}), "Jobs section")

    audit_job = jobs.get("audit")
    if not audit_job or not isinstance(audit_job, dict):
        raise WorkflowValidationError("Workflow is missing 'audit' job")

    audit_steps = audit_job.get("steps", [])
    if not isinstance(audit_steps, list):
        raise WorkflowValidationError("Job 'audit' steps must be a list")

    has_secret_step = False
    runner_step_count = 0

    for step in audit_steps:
        if not isinstance(step, dict):
            continue
        step_id = step.get("id")
        if step_id == "secret":
            has_secret_step = True

        run_cmd = step.get("run")
        if run_cmd and isinstance(run_cmd, str) and _step_executes_runner(run_cmd):
            runner_step_count += 1
            step_if = str(step.get("if", ""))
            if not _is_effective_secret_guard(step_if):
                raise WorkflowValidationError(
                    f"In job 'audit', step '{step.get('name', 'unnamed')}' invokes runner.py without "
                    "effective secret availability guard (steps.secret.outputs.available == 'true')"
                )

    if not has_secret_step:
        raise WorkflowValidationError("Job 'audit' must contain a step with id: secret")
    if runner_step_count == 0:
        raise WorkflowValidationError("Job 'audit' must contain at least one step executing runner.py")

    pr_spec_job = jobs.get("pr-spec-review")
    if pr_spec_job and isinstance(pr_spec_job, dict):
        job_if = str(pr_spec_job.get("if", ""))
        if not _is_effective_fork_guard(job_if):
            raise WorkflowValidationError(
                "Job 'pr-spec-review' must effectively restrict to non-fork PRs with "
                "'github.event.pull_request.head.repo.fork == false'"
            )


def validate_workflow(workflow_path: str) -> list[str]:
    """Validate the workflow at path and return list of error messages (empty if valid)."""
    if not os.path.isfile(workflow_path):
        return [f"Workflow file not found: {workflow_path}"]

    with open(workflow_path, "r", encoding="utf-8") as f:
        content = f.read()

    try:
        workflow = parse_workflow_yaml(content)
    except Exception as e:
        return [f"Failed to parse workflow YAML: {e}"]

    errors = []
    for validator in (validate_permissions, validate_advisory_mode, validate_secret_guards):
        try:
            validator(workflow)
        except WorkflowValidationError as e:
            errors.append(str(e))

    return errors


def main() -> int:
    if len(sys.argv) > 1:
        workflow_path = sys.argv[1]
    else:
        repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
        workflow_path = os.path.join(repo_root, ".github", "workflows", "typesafe-audit.yml")

    errors = validate_workflow(workflow_path)
    if errors:
        for err in errors:
            print(f"[ ERROR ] {err}", file=sys.stderr)
        return 1

    print(f"[ SUCCESS ] Workflow {os.path.basename(workflow_path)} passed all YAML-aware assertions.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
