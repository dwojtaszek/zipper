#!/usr/bin/env python3
"""Query SonarCloud quality gate and open PR issues for a pull request."""
import argparse
import http.client
import json
import sys
import urllib.request

DEFAULT_PROJECT = "dwojtaszek_zipper"
BLOCKING_SEVERITIES = frozenset({"BLOCKER", "CRITICAL", "MAJOR"})
NON_CODE_METRICS = frozenset({"new_coverage", "coverage"})


def _fetch_json(url: str, timeout: int = 15) -> dict:
    req = urllib.request.Request(url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"Expected JSON object from {url}")
    return data


def _summarize_gate(gate_payload: dict) -> tuple[str, list[dict], list[dict]]:
    project_status = gate_payload.get("projectStatus")
    if not isinstance(project_status, dict):
        raise ValueError("Missing projectStatus object in SonarCloud response")
    gate_status = str(project_status.get("status", "UNKNOWN"))
    failed_conditions = [
        c for c in (project_status.get("conditions") or [])
        if isinstance(c, dict) and c.get("status") == "ERROR"
    ]
    non_coverage = [c for c in failed_conditions if c.get("metricKey") not in NON_CODE_METRICS]
    return gate_status, failed_conditions, non_coverage


def _fetch_all_issues(project: str, pr_number: int) -> list[dict]:
    issues: list[dict] = []
    page = 1
    page_size = 100
    while True:
        payload = _fetch_json(
            f"https://sonarcloud.io/api/issues/search"
            f"?componentKeys={project}&pullRequest={pr_number}&statuses=OPEN,CONFIRMED,REOPENED&ps={page_size}&p={page}"
        )
        batch = payload.get("issues")
        if not isinstance(batch, list):
            raise ValueError("Missing issues list in SonarCloud response")
        issues.extend(item for item in batch if isinstance(item, dict))
        total = payload.get("total")
        if isinstance(total, int) and len(issues) >= total:
            break
        if len(batch) < page_size:
            break
        page += 1
    return issues


def inspect_pr(pr_number: int, project: str = DEFAULT_PROJECT) -> tuple[int, dict]:
    gate_payload = _fetch_json(
        f"https://sonarcloud.io/api/qualitygates/project_status?projectKey={project}&pullRequest={pr_number}"
    )
    gate_status, failed_conditions, non_coverage_failures = _summarize_gate(gate_payload)

    blocking: list[dict] = []
    advisory: list[dict] = []
    for issue in _fetch_all_issues(project, pr_number):
        severity = issue.get("severity") or "MAJOR"
        target = blocking if severity in BLOCKING_SEVERITIES else advisory
        target.append(issue)

    ok = not blocking and (gate_status == "OK" or (gate_status == "ERROR" and bool(failed_conditions) and not non_coverage_failures))
    summary = {
        "pr": pr_number,
        "project": project,
        "gate_status": gate_status,
        "ok": ok,
        "failed_conditions": failed_conditions,
        "blocking_issues": blocking,
        "advisory_issues": advisory,
    }
    return (0 if ok else 1), summary


def _format_issue(issue: dict) -> str:
    comp = str(issue.get("component", "")).split(":", 1)[-1]
    line = issue.get("line") or 0
    sev = str(issue.get("severity", "?"))
    rule = str(issue.get("rule", "?"))
    msg = str(issue.get("message", ""))
    return f"  {sev:9s} {rule:24s} L{line:<4} {comp}\n    {msg}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pr", type=int, help="Pull request number")
    parser.add_argument("--project", default=DEFAULT_PROJECT, help="SonarCloud projectKey")
    parser.add_argument("--json", action="store_true", help="Emit raw JSON summary")
    args = parser.parse_args()
    if args.pr < 1:
        parser.error("PR number must be positive")

    try:
        exit_code, summary = inspect_pr(args.pr, args.project)
    except (OSError, http.client.HTTPException, ValueError) as exc:
        print(f"Error querying SonarCloud for PR #{args.pr}: {exc}", file=sys.stderr)
        return 2

    if args.json:
        print(json.dumps(summary, indent=2))
        return exit_code

    print(f"SonarCloud PR #{args.pr} ({args.project}): gate={summary['gate_status']} ok={summary['ok']}")
    for cond in summary["failed_conditions"]:
        print(f"  condition {cond.get('metricKey')}: {cond.get('status')} (actual={cond.get('actualValue')})")
    if summary["blocking_issues"]:
        print("Blocking issues (BLOCKER/CRITICAL/MAJOR):")
        for issue in summary["blocking_issues"]:
            print(_format_issue(issue))
    if summary["advisory_issues"]:
        print(f"Advisory issues ({len(summary['advisory_issues'])} MINOR/INFO):")
        for issue in summary["advisory_issues"]:
            print(_format_issue(issue))
    if not summary["blocking_issues"] and not summary["advisory_issues"]:
        print("  0 open issues.")
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
