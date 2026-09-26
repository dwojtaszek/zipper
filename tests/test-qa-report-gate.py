#!/usr/bin/env python3
"""Exercise the workflow's real QA report validator before any model call."""

import os
from pathlib import Path
import subprocess
import tempfile

import yaml


workflow = yaml.safe_load(Path(".github/workflows/qa.yml").read_text())
validate = next(
    step["run"]
    for step in workflow["jobs"]["qa"]["steps"]
    if step.get("name") == "Validate QA report"
)
report = """## QA Report

| # | Test Case | App | Persona | Result | Notes |
| --- | --- | --- | --- | --- | --- |
| 1 | [positive] Archive generation | CLI | default | :white_check_mark: PASS | Archive created. |
| 2 | [negative] Invalid File Type | CLI | default | :white_check_mark: PASS | Exit 1, no Archive. |

<details>
<summary>Screenshots & Evidence</summary>

- [Case 1 snapshot](test-1/evidence/case-1.snapshot.txt)
- [Case 2 snapshot](test-1/evidence/case-2.snapshot.txt)

</details>
"""

with tempfile.TemporaryDirectory(prefix="zipper-qa-report-gate-") as workspace:
    root = Path(workspace)
    evidence = root / "qa-results/test-1/evidence"
    evidence.mkdir(parents=True)
    (evidence / "case-1.snapshot.txt").write_text(
        "$ Zipper --version\nZipper vqa-test\nAPP_EXIT:0\n"
    )
    negative = evidence / "case-2.snapshot.txt"
    negative.write_text("$ Zipper --type invalid\nError: invalid File Type\nAPP_EXIT:1\n")
    report_file = root / "qa-results/report.md"

    def check(content, expected, cli_affected=True):
        report_file.write_text(content)
        result = subprocess.run(
            ["bash", "-e", "-o", "pipefail"],
            input=validate,
            text=True,
            cwd=root,
            env={
                **os.environ,
                "CLI_AFFECTED": "true" if cli_affected else "false",
                "QA_RUN_ID": "test-1",
            },
            capture_output=True,
        )
        assert (result.returncode == 0) == expected, result.stdout + result.stderr

    check(report, True)
    check(report.replace(
        "](test-1/evidence/case-1.snapshot.txt)",
        "](qa-results/test-1/evidence/case-1.snapshot.txt)",
    ), False)
    check(report.replace(
        "[Case 1 snapshot](test-1/evidence/case-1.snapshot.txt)",
        "`test-1/evidence/case-1.snapshot.txt`",
    ), False)
    check(report.replace(
        "[Case 1 snapshot](test-1/evidence/case-1.snapshot.txt)",
        "Case 1 snapshot](test-1/evidence/case-1.snapshot.txt)",
    ), False)
    negative.write_text("APP_RC=1 VERIFY_RC=0\n")
    check(report, False)
    negative.write_text("Summary: PASS\nAPP_EXIT:1\n")
    check(report, False)
    negative.write_text("$ Zipper --type invalid\nError: invalid File Type\nAPP_EXIT:1\n")
    check(report.replace("case-2.snapshot.txt", "summary.txt"), False)
    notes_only = report.replace(
        "Exit 1, no Archive.",
        "Exit 1, no Archive; test-1/evidence/case-2.snapshot.txt.",
    ).replace("- [Case 2 snapshot](test-1/evidence/case-2.snapshot.txt)", "")
    check(notes_only, False)
    check(report.replace("| 2 |", "| 1 |"), False)
    negative.unlink()
    check(report, False)
    negative.symlink_to(evidence / "case-1.snapshot.txt")
    check(report, False)
    no_app = """## QA Report

| #   | Test Case | App | Persona | Result | Notes |
| --- | --------- | --- | ------- | ------ | ----- |
| 1 | No app code changed | CLI | default | :grey_question: INCONCLUSIVE | No app code changed -- QA not applicable for this diff. |
"""
    check(no_app, True, cli_affected=False)

print("QA report gate accepts linked snapshots and rejects missing or summary evidence.")
