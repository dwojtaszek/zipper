#!/usr/bin/env python3
"""YAML-aware validator for GitHub Actions workflows (#1046, #1112).

Validates:
1. Advisory mode (#1046): runner.py invocations in run: blocks must not use --strict.
2. Effective permissions (#1046): contents: read must not be overridden by job-level writes.
3. Secret-availability and fork guards (#1046).
4. Artifact producer/consumer contracts (#1112): matrix platforms, self-contained publish,
   PDB cleanup, binary rename, upload/download artifact steps, release files, and cache scoping.
"""

import os
import shlex
import sys


class WorkflowValidationError(Exception):
    """Raised when a workflow fails structural or security validation."""
    pass


DOT_GITHUB_DIR = ".github"


def parse_workflow_yaml(text: str) -> dict:
    """Parse workflow YAML into dict, using PyYAML if available or fallback parser."""
    try:
        import yaml
        return yaml.safe_load(text) or {}
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
    if tokens[0].endswith("runner.py") or tokens[0].endswith("run_check.py"):
        return True
    first_cmd = os.path.basename(tokens[0])
    if first_cmd in ("echo", "printf", "cat", "grep", "sed", "awk"):
        return False
    return any(t.endswith("runner.py") or t.endswith("run_check.py") for t in tokens)


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

    # If this workflow contains the 'audit' job or neither triage nor quality-audit, validate audit workflow requirements
    if "audit" in jobs or ("triage" not in jobs and "quality-audit" not in jobs):
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

    # General secret guard checks for other TypeSafe jobs (e.g. triage, quality-audit)
    for job_name in ("triage", "quality-audit"):
        if job_name in jobs:
            job_def = jobs[job_name]
            if not isinstance(job_def, dict):
                raise WorkflowValidationError(f"Job '{job_name}' must be a mapping")
            steps = job_def.get("steps", [])
            if not isinstance(steps, list):
                raise WorkflowValidationError(f"Job '{job_name}' steps must be a list")

            has_secret_step = False
            check_step_count = 0
            for step in steps:
                if not isinstance(step, dict):
                    continue
                if step.get("id") == "secret":
                    has_secret_step = True
                run_cmd = step.get("run")
                if run_cmd and isinstance(run_cmd, str) and _step_executes_runner(run_cmd):
                    check_step_count += 1
                    step_if = str(step.get("if", ""))
                    if not _is_effective_secret_guard(step_if):
                        raise WorkflowValidationError(
                            f"In job '{job_name}', step '{step.get('name', 'unnamed')}' invokes TypeSafe check without "
                            "effective secret availability guard (steps.secret.outputs.available == 'true')"
                        )
            if not has_secret_step:
                raise WorkflowValidationError(f"Job '{job_name}' must contain a step with id: secret")
            if check_step_count == 0:
                raise WorkflowValidationError(f"Job '{job_name}' must contain at least one step executing TypeSafe")


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


REQUIRED_PLATFORMS = {"win-x64", "linux-x64", "osx-arm64"}


def _extract_matrix_platforms(matrix: dict) -> set[str]:
    platforms = set()
    if "platform" in matrix:
        p = matrix["platform"]
        if isinstance(p, list):
            platforms.update(str(x) for x in p)
        elif isinstance(p, str):
            p_clean = p.strip("[] \t\r\n")
            platforms.update(x.strip() for x in p_clean.split(",") if x.strip())
    if "include" in matrix and isinstance(matrix["include"], list):
        for item in matrix["include"]:
            if isinstance(item, dict) and "platform" in item:
                platforms.add(str(item["platform"]))
    return platforms


def _extract_uncommented_run_lines(steps: list) -> list[str]:
    lines = []
    for step in steps:
        if isinstance(step, dict) and isinstance(step.get("run"), str):
            for line in step["run"].splitlines():
                line_s = line.strip()
                if line_s and not line_s.startswith("#"):
                    lines.append(line_s)
    return lines


def _validate_producer_renames(uncommented_lines: list[str]) -> None:
    for plat in sorted(REQUIRED_PLATFORMS):
        expected_renamed = f"zipper-{plat}.exe" if plat.startswith("win") else f"zipper-{plat}"
        if not any("mv " in line and expected_renamed in line for line in uncommented_lines):
            raise WorkflowValidationError(
                f"Producer job 'build-and-test' missing executable rename command for '{plat}' ('{expected_renamed}')"
            )


def _validate_producer_steps(steps: list) -> None:
    uncommented_lines = _extract_uncommented_run_lines(steps)
    uncommented_run = "\n".join(uncommented_lines)

    if "dotnet publish" not in uncommented_run or "--self-contained" not in uncommented_run:
        raise WorkflowValidationError(
            "Producer job 'build-and-test' missing executable 'dotnet publish' step with --self-contained"
        )
    if "publish/${{ matrix.platform }}" not in uncommented_run:
        raise WorkflowValidationError(
            "Producer job 'build-and-test' missing publish output directory 'publish/${{ matrix.platform }}'"
        )

    if not any("rm " in line and ".pdb" in line for line in uncommented_lines):
        raise WorkflowValidationError(
            "Producer job 'build-and-test' missing executable PDB cleanup command ('rm -f publish/${{ matrix.platform }}/*.pdb')"
        )

    _validate_producer_renames(uncommented_lines)


def _validate_producer_upload(steps: list) -> None:
    upload_steps = [
        step for step in steps
        if isinstance(step, dict) and str(step.get("uses", "")).startswith("actions/upload-artifact@")
    ]
    if not upload_steps:
        raise WorkflowValidationError(
            "Producer job 'build-and-test' missing executable 'actions/upload-artifact' step"
        )

    self_contained_upload = next(
        (
            ustep for ustep in upload_steps
            if isinstance(ustep.get("with"), dict) and "matrix.platform" in str(ustep["with"].get("name", ""))
        ),
        None
    )
    if not self_contained_upload:
        raise WorkflowValidationError(
            "Producer job 'build-and-test' missing executable upload-artifact step for self-contained binaries (expected name: 'zipper-${{ matrix.platform }}')"
        )

    u_with = self_contained_upload.get("with", {})
    if u_with.get("name") != "zipper-${{ matrix.platform }}":
        raise WorkflowValidationError(
            f"Upload-artifact step in 'build-and-test' has unexpected name '{u_with.get('name')}', expected 'zipper-${{{{ matrix.platform }}}}'"
        )
    if u_with.get("path") != "publish/${{ matrix.platform }}":
        raise WorkflowValidationError(
            f"Upload-artifact step in 'build-and-test' has unexpected path '{u_with.get('path')}', expected 'publish/${{{{ matrix.platform }}}}'"
        )

    retention = u_with.get("retention-days")
    is_valid_retention = (
        isinstance(retention, int) and not isinstance(retention, bool) and retention > 0
    ) or (
        isinstance(retention, str) and retention.isdigit() and int(retention) > 0
    )
    if not is_valid_retention:
        raise WorkflowValidationError(
            f"Upload-artifact step in 'build-and-test' missing or invalid 'retention-days' (got {retention})"
        )


def validate_artifact_producer(workflow: dict) -> None:
    """Validate artifact producer jobs and steps (in ci.yml)."""
    _require_mapping(workflow, "Workflow root")
    jobs = _require_mapping(workflow.get("jobs", {}), "Jobs section")
    if not jobs:
        raise WorkflowValidationError("Workflow root must contain 'jobs' mapping with executable jobs")

    producer_job = jobs.get("build-and-test")
    if not isinstance(producer_job, dict):
        raise WorkflowValidationError("Workflow is missing required producer job 'build-and-test'")

    strategy = producer_job.get("strategy")
    if not isinstance(strategy, dict):
        raise WorkflowValidationError("Producer job 'build-and-test' missing 'strategy' mapping")
    matrix = strategy.get("matrix")
    if not isinstance(matrix, dict):
        raise WorkflowValidationError("Producer job 'build-and-test' missing 'strategy.matrix' mapping")

    platforms = _extract_matrix_platforms(matrix)
    missing_platforms = REQUIRED_PLATFORMS - platforms
    if missing_platforms:
        raise WorkflowValidationError(
            f"Producer job 'build-and-test' matrix missing required platform(s): {sorted(missing_platforms)}"
        )

    steps = producer_job.get("steps")
    if not isinstance(steps, list):
        raise WorkflowValidationError("Producer job 'build-and-test' steps must be a list")

    _validate_producer_steps(steps)
    _validate_producer_upload(steps)


def _validate_consumer_download(steps: list) -> str:
    download_steps = [
        step for step in steps
        if isinstance(step, dict) and str(step.get("uses", "")).startswith("actions/download-artifact@")
    ]
    if not download_steps:
        raise WorkflowValidationError("Consumer job 'tag-and-release' missing executable 'actions/download-artifact' step")

    download_step = download_steps[0]
    d_with = download_step.get("with", {})
    if not isinstance(d_with, dict):
        raise WorkflowValidationError("Download-artifact step missing 'with' mapping")
    if d_with.get("name"):
        raise WorkflowValidationError(
            "Download-artifact step should omit 'name' to download all platform artifacts into subdirectories"
        )
    download_path = str(d_with.get("path", "")).strip().rstrip("/")
    if not download_path:
        raise WorkflowValidationError("Download-artifact step missing 'path' property")
    return download_path


def _validate_consumer_release(steps: list, download_path: str, expected_release_files: list[str] | None = None) -> None:
    release_steps = [
        step for step in steps
        if isinstance(step, dict) and str(step.get("uses", "")).startswith("softprops/action-gh-release@")
    ]
    if not release_steps:
        raise WorkflowValidationError("Consumer job 'tag-and-release' missing executable 'softprops/action-gh-release' step")

    release_step = release_steps[0]
    r_with = release_step.get("with", {})
    if not isinstance(r_with, dict):
        raise WorkflowValidationError("Release step missing 'with' mapping")

    files_val = r_with.get("files")
    if not files_val:
        raise WorkflowValidationError("Release step missing 'files' property")

    if isinstance(files_val, list):
        release_files = [str(f).strip() for f in files_val if str(f).strip()]
    elif isinstance(files_val, str):
        release_files = [line.strip() for line in files_val.splitlines() if line.strip()]
    else:
        release_files = []

    if expected_release_files is None:
        expected_release_files = [
            f"{download_path}/zipper-{plat}/zipper-{plat}.exe" if plat.startswith("win") else f"{download_path}/zipper-{plat}/zipper-{plat}"
            for plat in sorted(REQUIRED_PLATFORMS)
        ]

    missing_files = [f for f in expected_release_files if f not in release_files]
    if missing_files:
        raise WorkflowValidationError(
            f"Release step in 'tag-and-release' missing expected release file(s): {missing_files} (found: {release_files})"
        )


def validate_artifact_consumer(
    workflow: dict,
    expected_release_files: list[str] | None = None,
) -> None:
    """Validate artifact consumer and release jobs (in build-and-test.yml)."""
    _require_mapping(workflow, "Release workflow root")
    jobs = _require_mapping(workflow.get("jobs", {}), "Jobs section")
    if not jobs:
        raise WorkflowValidationError("Release workflow root must contain 'jobs' mapping with executable jobs")

    consumer_job = jobs.get("tag-and-release")
    if not isinstance(consumer_job, dict):
        raise WorkflowValidationError("Release workflow is missing required consumer job 'tag-and-release'")

    needs = consumer_job.get("needs", [])
    if isinstance(needs, str):
        needs_str = needs.strip("[] \t\r\n")
        needs = [n.strip() for n in needs_str.split(",") if n.strip()]
    elif not isinstance(needs, list):
        needs = [needs]
    if "ci" not in needs:
        raise WorkflowValidationError("Consumer job 'tag-and-release' must depend on 'ci' via 'needs'")

    steps = consumer_job.get("steps")
    if not isinstance(steps, list):
        raise WorkflowValidationError("Job 'tag-and-release' steps must be a list")

    download_path = _validate_consumer_download(steps)
    _validate_consumer_release(steps, download_path, expected_release_files)


def _validate_job_setup_dotnet_cache(job_name: str, j_def: dict) -> None:
    if not isinstance(j_def, dict):
        raise WorkflowValidationError(f"Job '{job_name}' must be a mapping")
    steps = j_def.get("steps", [])
    if not isinstance(steps, list):
        raise WorkflowValidationError(f"Job '{job_name}' steps must be a list")
    setup_steps = [
        step for step in steps
        if isinstance(step, dict) and str(step.get("uses", "")).startswith("actions/setup-dotnet@")
    ]
    if not setup_steps:
        raise WorkflowValidationError(f"Job '{job_name}' missing executable 'actions/setup-dotnet' step")

    s_with = setup_steps[0].get("with", {})
    if not isinstance(s_with, dict):
        raise WorkflowValidationError(f"Job '{job_name}' setup-dotnet step missing 'with' configuration")

    cache_val = s_with.get("cache")
    if cache_val is not True and str(cache_val).lower() != "true":
        raise WorkflowValidationError(
            f"Job '{job_name}' setup-dotnet step missing 'cache: true' (got: {cache_val})"
        )

    dep_path = s_with.get("cache-dependency-path")
    if not dep_path or not isinstance(dep_path, str) or not dep_path.strip():
        raise WorkflowValidationError(
            f"Job '{job_name}' setup-dotnet step missing 'cache-dependency-path'"
        )


def _validate_coverage_action_cache(coverage_action: dict) -> None:
    _require_mapping(coverage_action, "Coverage action root")
    runs = coverage_action.get("runs", {})
    if not isinstance(runs, dict):
        raise WorkflowValidationError("Coverage gate action missing 'runs' mapping")
    c_steps = runs.get("steps", [])
    if not isinstance(c_steps, list):
        raise WorkflowValidationError("Coverage gate action 'runs.steps' must be a list")

    cache_steps = [
        step for step in c_steps
        if isinstance(step, dict) and str(step.get("uses", "")).startswith("actions/cache@")
    ]
    if not cache_steps:
        raise WorkflowValidationError("Coverage gate action missing executable 'actions/cache' step")

    c_with = cache_steps[0].get("with", {})
    if not isinstance(c_with, dict):
        raise WorkflowValidationError("Coverage gate action actions/cache step missing 'with' configuration")

    c_path = c_with.get("path")
    if not c_path or not isinstance(c_path, str) or not c_path.strip():
        raise WorkflowValidationError("Coverage gate action actions/cache step missing 'path' property")

    c_key = c_with.get("key")
    if not c_key or not isinstance(c_key, str) or not c_key.strip():
        raise WorkflowValidationError("Coverage gate action actions/cache step missing 'key' property")


def validate_cache_configuration(ci_workflow: dict, coverage_action: dict | None = None) -> None:
    """Validate scoped caching assertions on setup-dotnet and coverage-gate action."""
    _require_mapping(ci_workflow, "CI workflow root")
    jobs = _require_mapping(ci_workflow.get("jobs", {}), "Jobs section")

    for job_name in ("lint", "build-and-test"):
        if job_name not in jobs:
            raise WorkflowValidationError(f"CI workflow missing required job '{job_name}' for cache validation")
        _validate_job_setup_dotnet_cache(job_name, jobs[job_name])

    if coverage_action is not None:
        _validate_coverage_action_cache(coverage_action)


def validate_artifact_pipeline(
    ci_workflow_path: str,
    release_workflow_path: str,
    coverage_action_path: str | None = None,
) -> list[str]:
    """Validate complete artifact pipeline across producer, consumer, and cache configs."""
    errors = []

    if not os.path.isfile(ci_workflow_path):
        return [f"Workflow file not found: {ci_workflow_path}"]
    try:
        with open(ci_workflow_path, "r", encoding="utf-8") as f:
            ci_workflow = parse_workflow_yaml(f.read())
    except Exception as e:
        return [f"Failed to parse CI workflow YAML: {e}"]

    if not os.path.isfile(release_workflow_path):
        return [f"Workflow file not found: {release_workflow_path}"]
    try:
        with open(release_workflow_path, "r", encoding="utf-8") as f:
            release_workflow = parse_workflow_yaml(f.read())
    except Exception as e:
        return [f"Failed to parse release workflow YAML: {e}"]

    coverage_action = None
    if coverage_action_path:
        if not os.path.isfile(coverage_action_path):
            return [f"Coverage action file not found: {coverage_action_path}"]
        try:
            with open(coverage_action_path, "r", encoding="utf-8") as f:
                coverage_action = parse_workflow_yaml(f.read())
        except Exception as e:
            return [f"Failed to parse coverage action YAML: {e}"]

    for validator, args in (
        (validate_artifact_producer, (ci_workflow,)),
        (validate_artifact_consumer, (release_workflow,)),
        (validate_cache_configuration, (ci_workflow, coverage_action)),
    ):
        try:
            validator(*args)
        except WorkflowValidationError as e:
            errors.append(str(e))

    return errors


def _validate_single_mutation_case(case_path: str, case_name: str) -> list[str]:
    ci_path = os.path.join(case_path, "ci.yml")
    rel_path = os.path.join(case_path, "build-and-test.yml")
    cov_path = os.path.join(case_path, "coverage-gate-action.yml")
    if not os.path.isfile(cov_path):
        cov_path = None
    exp_path = os.path.join(case_path, "expected_error.txt")

    case_errors = validate_artifact_pipeline(ci_path, rel_path, cov_path)
    if not os.path.isfile(exp_path):
        if case_errors:
            return [f"Fixture case '{case_name}' failed validation unexpectedly: {case_errors}"]
        return []

    with open(exp_path, "r", encoding="utf-8") as f:
        expected_sub = f.read().strip().lower()

    if not case_errors:
        return [
            f"Mutation case '{case_name}' unexpectedly passed validation (expected failure containing '{expected_sub}')"
        ]

    combined_err = " ".join(case_errors).lower()
    if expected_sub not in combined_err:
        return [
            f"Mutation case '{case_name}' failed but error did not contain expected diagnostic '{expected_sub}': {case_errors}"
        ]
    return []


def validate_artifact_mutations(fixtures_dir: str) -> list[str]:
    """Validate that mutation fixtures fail with targeted diagnostics, and valid fixture passes."""
    if not os.path.isdir(fixtures_dir):
        return [f"Mutation fixtures directory not found: {fixtures_dir}"]

    case_dirs = sorted([
        d for d in os.listdir(fixtures_dir)
        if os.path.isdir(os.path.join(fixtures_dir, d))
    ])

    errors = []
    for case_name in case_dirs:
        case_path = os.path.join(fixtures_dir, case_name)
        errors.extend(_validate_single_mutation_case(case_path, case_name))

    return errors


def _parse_artifact_cli_args(args: list[str], repo_root: str) -> tuple[dict[str, str], list[str]]:
    paths = {
        "ci": os.path.join(repo_root, DOT_GITHUB_DIR, "workflows", "ci.yml"),
        "release": os.path.join(repo_root, DOT_GITHUB_DIR, "workflows", "build-and-test.yml"),
        "coverage": os.path.join(repo_root, DOT_GITHUB_DIR, "actions", "coverage-gate", "action.yml"),
        "fixtures": os.path.join(repo_root, "tests", "typesafe-audit-fixtures", "artifacts", "cases"),
    }
    opt_map = {
        "--ci-workflow": "ci",
        "--release-workflow": "release",
        "--coverage-action": "coverage",
        "--fixtures-dir": "fixtures",
    }
    idx = 0
    errors = []
    while idx < len(args):
        arg = args[idx]
        if arg in opt_map:
            if idx + 1 < len(args):
                paths[opt_map[arg]] = args[idx + 1]
                idx += 2
            else:
                errors.append(f"Missing argument value for {arg}")
                idx += 1
        elif arg.startswith("--"):
            errors.append(f"Unrecognized option: {arg}")
            idx += 1
        else:
            errors.append(f"Unexpected positional argument: {arg}")
            idx += 1
    return paths, errors


def run_artifact_validation_cli(args: list[str]) -> int:
    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    paths, parse_errors = _parse_artifact_cli_args(args, repo_root)
    if parse_errors:
        for err in parse_errors:
            print(f"[ ERROR ] {err}", file=sys.stderr)
        return 1

    ci_path = paths["ci"]
    release_path = paths["release"]
    coverage_path = paths["coverage"]
    fixtures_dir = paths["fixtures"]

    print(f"1. Analyzing {os.path.basename(ci_path)} artifact patterns...")
    pipeline_errors = validate_artifact_pipeline(ci_path, release_path, coverage_path)
    if pipeline_errors:
        for err in pipeline_errors:
            print(f"[ ERROR ] {err}", file=sys.stderr)
        return 1

    print("✓ Uses actions/upload-artifact in producer job")
    print("✓ Found expected artifact names and matrix platforms")
    print("✓ Found publish/ directory structure with platform variable")
    print("✓ Removes PDB files from artifacts")
    print("✓ Found executable renaming patterns")

    print(f"2. Checking {os.path.basename(release_path)} artifact handling...")
    print("✓ Uses actions/download-artifact in release job")
    print("✓ Found release file patterns")

    print("3. Analyzing caching strategy...")
    print("✓ Uses actions/setup-dotnet with cache: true and cache-dependency-path")
    if coverage_path and os.path.isfile(coverage_path):
        print("✓ Uses actions/cache in coverage gate composite action")

    print("4. Running mutation fixtures regression suite...")
    if not os.path.isdir(fixtures_dir):
        print(f"[ ERROR ] Mutation fixtures directory not found: {fixtures_dir}", file=sys.stderr)
        return 1
    mutation_errors = validate_artifact_mutations(fixtures_dir)
    if mutation_errors:
        for err in mutation_errors:
            print(f"[ ERROR ] {err}", file=sys.stderr)
        return 1
    print("✓ All mutation fixtures passed (comment-only, missing upload, wrong job, name mismatch, omitted platform, changed release paths, decoy comments)")

    print("")
    print("=== All Artifact Handling Tests Passed ===")
    print("Current artifact patterns are compatible with unified workflow design")
    return 0


def main() -> int:
    if "--artifacts" in sys.argv:
        args = [a for a in sys.argv[1:] if a != "--artifacts"]
        return run_artifact_validation_cli(args)

    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    workflows_dir = os.path.join(repo_root, DOT_GITHUB_DIR, "workflows")

    if len(sys.argv) > 1:
        workflow_paths = sys.argv[1:]
    else:
        # Default: discover all typesafe-*.yml workflows if present
        if os.path.isdir(workflows_dir):
            workflow_paths = sorted([
                os.path.join(workflows_dir, f)
                for f in os.listdir(workflows_dir)
                if f.startswith("typesafe-") and f.endswith(".yml")
            ])
        else:
            workflow_paths = [os.path.join(workflows_dir, "typesafe-audit.yml")]

    all_errors = []
    for workflow_path in workflow_paths:
        errors = validate_workflow(workflow_path)
        if errors:
            for err in errors:
                print(f"[ ERROR ] {os.path.basename(workflow_path)}: {err}", file=sys.stderr)
            all_errors.extend(errors)
        else:
            print(f"[ SUCCESS ] Workflow {os.path.basename(workflow_path)} passed all YAML-aware assertions.")

    return 1 if all_errors else 0


if __name__ == "__main__":
    sys.exit(main())

