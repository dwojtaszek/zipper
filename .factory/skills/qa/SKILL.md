---
name: qa
description: >
  Run QA tests for Zipper. Analyzes git diff to determine affected areas,
  runs configured test flows against the CLI binary, and generates diff-targeted tests.
  Uses shell commands to run the binary and verify output files.
  Use when testing PRs, releases, or smoke testing the CLI.
---

# QA Orchestrator

**SCOPE: This skill performs functional QA only -- verifying that the Zipper CLI actually works by running it with real arguments and checking output files. Do NOT run or report on CI checks, linting, dotnet format, unit tests, or any static analysis. Those are handled by separate workflows.**

## Step 1: Load Configuration

Read `.factory/skills/qa/config.yaml` for build commands, test tool config, and app definitions.

## Step 2: Determine Target Environment

The default target is `local`. Zipper is a CLI tool with no deployed environments -- it runs locally against the built binary.

## Step 3: Analyze Git Diff

Run `git diff` to determine what changed. Map changed files to apps using the path_patterns in config.yaml.

Files that don't match ANY app's path_patterns (e.g., `.factory/skills/**`, `docs/**`, `.github/**`, `droid-wiki/**`, `*.md`) are NOT associated with any app. Do NOT run app test flows for them.

For each affected app:

- Run ONLY that app's flows from its sub-skill
- Generate ADDITIONAL targeted tests based on the specific changes in the diff

For apps NOT affected by the diff:

- Do NOT load or run their flows. They are completely out of scope.

If NO app is affected by the diff (e.g., docs-only, CI-only, or config-only changes), report as INCONCLUSIVE: "No app code changed -- QA not applicable for this diff." Do NOT run any app flows.

## Step 4: Pre-flight Checks

For the CLI app (if affected):

1. Build the binary: `dotnet publish src/Zipper.csproj -c Release -o ./publish-bin`
2. Verify the binary exists: `test -f ./publish-bin/Zipper`
3. If build fails, report BLOCKED with the build error

Do NOT run pre-flight checks if the CLI app is not affected by the diff.

## Step 5: Execute Diff-Relevant Flows Only

Read the sub-skill from `.factory/skills/qa-cli/SKILL.md`.

The sub-skill contains a MENU of available test flows. You must:

1. Read the diff carefully and identify which flows are relevant to the change
2. Run those flows PLUS adjacent flows that verify the change integrates correctly
3. Do NOT run completely unrelated flows
4. If no existing flow covers the change, write a NEW ad-hoc test that directly verifies the changed behavior
5. Do NOT run unit tests, lint, or any automated test suite. This is functional QA -- run the binary as a real user would.

## Step 6: Evidence Capture

After each test, capture evidence:

- Terminal output (stdout/stderr) from the CLI run
- Output file verification: list generated files, check file sizes, verify load file format
- For load files: show first few lines to verify headers and delimiters
- For ZIP archives: list contents
- For production sets: show directory tree structure

Embed evidence as fenced code blocks in the report with descriptive labels.

## Step 7: Test Quality Gate

1. CHANGE-SPECIFIC FIRST. Prioritize tests that directly verify the behavioral change in the diff.
2. INTEGRATION TESTS ARE VALID. Tests verifying the change integrates with existing features are good.
3. NO UNRELATED FLOWS. Do NOT test features completely unrelated to the diff.
4. NO AUTOMATED TEST SUITES. Do NOT run dotnet test or any CI-style checks.
5. NEGATIVE TESTS. Include at least 1 test verifying error handling or boundary conditions.
6. FUNCTIONAL TESTING. Test by running the binary with real arguments and checking outputs.
7. INCONCLUSIVE IF UNSURE. If you cannot articulate what the PR changes, mark INCONCLUSIVE.

## Step 8: Handle Failures

**Never silently skip a flow.** If a flow cannot complete, report it as BLOCKED with what was tried and how the user can fix it. Then continue to the next flow.

## Step 9: Generate Report

Generate the report at `./qa-results/report.md` using `.factory/skills/qa/REPORT-TEMPLATE.md`.

The report MUST follow the template. Key rules:

- Start with `## QA Report` heading followed by the test results table
- Result column MUST use emojis: ✅ PASS, ❌ FAIL, ⛔ BLOCKED, ⚠️ FLAKY, ❓ INCONCLUSIVE
- Keep it CONCISE. The table + short "Action Required" section + collapsed evidence = the entire report.
- Do NOT report build steps as test rows. Only report rows that verify actual user-facing behavior.
- Put ALL evidence in a single collapsed `<details>` block.

## Step 10: Suggest Skill Updates (Failure Learning)

After generating the report, check if any BLOCKED or FAIL results revealed a testing environment insight that would help future QA runs.

Format as a table with severity, collapsible fix prompts, and a count in the heading:

## Suggested Skill Updates (N issues found)

| # | Severity | File | Issue | Fix Prompt |
| --- | -------- | ---- | ----- | ---------- |
| 1 | emoji level | `file` | description | details with fix prompt |

Severity levels: Breaking (causes failures every run), Degraded (intermittent failures), Info (improves future runs).

Read the `failure_learning` field from config.yaml (`open_pr`): include the table in the report AND write a `qa-results/skill-updates.json` file so the workflow can open a PR with the updates.

**`skill-updates.json` format:**

```json
[
  {
    "file": ".factory/skills/qa-cli/SKILL.md",
    "section": "Known Failure Modes",
    "action": "append",
    "content": "N. **Description.** Fix instructions."
  }
]
```
