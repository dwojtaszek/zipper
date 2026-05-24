---
name: qa
description: >
  Run QA tests for Zipper. Analyzes git diff to determine affected areas,
  runs configured test flows with the operator persona, and generates
  diff-targeted tests. Uses CLI command execution and output file verification.
  Use when testing PRs, releases, or smoke testing the CLI.
---

# QA Orchestrator

**SCOPE: This skill performs manual/functional QA only -- verifying that the application actually works by running the CLI and checking output files. Do NOT run or report on CI checks, linting, dotnet format, unit tests, or any static analysis. Those are handled by separate workflows.**

## Step 1: Load Configuration

Read `.factory/skills/qa/config.yaml` for environment URLs, credentials, personas, and app definitions.

## Step 2: Determine Target Environment

Zipper is a standalone CLI tool with no environments. Build the binary locally and test against it.

**Pre-flight: Build the CLI binary**

```bash
dotnet publish src/Zipper.csproj -c Release -o publish-bin
```

The binary is at `publish-bin/Zipper` (Linux/macOS) or `publish-bin/Zipper.exe` (Windows).

## Step 3: Analyze Git Diff

Run `git diff` to determine what changed. Map changed files to apps using the path_patterns in config.yaml.

Files that don't match ANY app's path_patterns (e.g., `.factory/skills/**`, `docs/**`, `.github/**`, config files) are NOT associated with any app. Do NOT run app test flows for them.

For the `cli` app (path_patterns: `src/**`, `tests/**`):

- Run ONLY flows relevant to the changed code
- Generate ADDITIONAL targeted tests based on the specific changes in the diff

If NO app is affected by the diff (e.g., docs-only, CI-only, or config-only changes), report as INCONCLUSIVE: "No app code changed -- QA not applicable for this diff." Do NOT run any app flows.

## Step 4: Pre-flight Checks

1. **Build the CLI binary** -- `dotnet publish src/Zipper.csproj -c Release -o publish-bin`
2. **Verify the binary runs** -- `./publish-bin/Zipper --benchmark` (quick exit mode) or `./publish-bin/Zipper --chaos-list`
3. **Create a temporary output directory** -- `mkdir -p ./qa-results/$RUN_ID`

If the build fails, report as BLOCKED with the build error. Do NOT proceed.

## Step 5: Execute Diff-Relevant Flows Only

Read the sub-skill from `.factory/skills/qa-cli/SKILL.md`.

The sub-skill contains a MENU of available test flows. You must:

1. Read the diff carefully and identify which flows are relevant to the change
2. Run those flows PLUS any adjacent flows that verify the change integrates correctly
3. Do NOT run completely unrelated flows (e.g., if the diff only adds a chaos type, do NOT test production sets)
4. If no existing flow covers the change, write a NEW ad-hoc test that directly verifies the changed behavior
5. Do NOT run unit tests, lint, typecheck, or any automated test suite. This is manual/functional QA -- run the CLI as a real user would.

## Step 6: Evidence Capture

After each significant test step, capture evidence:

- **CLI output**: Capture stdout/stderr as text (use `2>&1 | tee`)
- **File listings**: `ls -la` of the output directory
- **Load File content**: `head -n 5` of .dat/.opt/.csv files to show header and first records
- **_properties.json**: `cat` the audit file for loadfile-only/chaos runs
- **_manifest.json**: `cat` the manifest for production set runs
- **ZIP contents**: `unzip -l archive.zip | head -n 20` to show structure
- **File counts**: Verify expected file counts in ZIP and load file

Evidence quality rules:

- Focus on the RELEVANT content. Trim output to the meaningful part.
- Label each capture clearly: what it shows and why it matters for the test.
- Use fenced code blocks with descriptive labels.

## Step 7: Test Quality Gate

TEST QUALITY REQUIREMENTS:

1. CHANGE-SPECIFIC FIRST. Prioritize tests that directly verify the behavioral change in the diff. At least half your tests should be testing the new/changed feature itself.
2. INTEGRATION TESTS ARE VALID. Tests that verify the change integrates correctly with existing features are good (e.g., new argument appears in help, conflict detection works). These are NOT smoke tests.
3. NO UNRELATED FLOWS. Do NOT test features completely unrelated to the diff.
4. NO AUTOMATED TEST SUITES. Do NOT run dotnet test, dotnet format, or any CI-style checks. This is manual/functional QA only.
5. NEGATIVE TESTS. Include at least 1 test verifying error handling or boundary conditions related to the change.
6. INTERACTIVE TESTING. Test by actually running the CLI as a real user would.
7. INCONCLUSIVE IF UNSURE. If you cannot articulate what the PR changes, mark as INCONCLUSIVE rather than PASS.

## Step 8: Handle Failures

**Never silently skip a flow.** If a flow cannot complete, report it as BLOCKED with what was tried and how the user can fix it. Then continue to the next flow -- never abort the entire run for a single failure.

## Step 9: Generate Report

Generate the report at `./qa-results/report.md` using `.factory/skills/qa/REPORT-TEMPLATE.md`.

The report MUST follow the template. Key rules:

- Start with `## QA Report` heading followed by the test results table
- Result column MUST use emojis: :white_check_mark: PASS, :x: FAIL, :no_entry: BLOCKED, :warning: FLAKY, :grey_question: INCONCLUSIVE
- Keep it CONCISE. The table + a short "Action Required" section (if any) + collapsed evidence = the entire report.
- Do NOT include: "Behavioral Change Summary", "Blocked Flows" prose, "Info" metadata table, or verbose explanations of what the diff does.
- Do NOT report setup/prerequisite steps (building, startup) as test rows. Those are means to an end, not test cases.
- Put ALL evidence in a single collapsed `<details>` block
- For CLI evidence: embed command output as labeled fenced code blocks.

## Step 10: Suggest Skill Updates (Failure Learning)

After generating the report, check if any BLOCKED or FAIL results revealed a **testing environment insight** that would help future QA runs succeed.

**Good suggestions** (environment/workflow knowledge):

- "Binary must be published with -o publish-bin, not just built, for E2E testing"
- "Chaos Engine requires --loadfile-only flag; without it, --chaos-mode is silently rejected"
- "Production set requires --bates-prefix; exit code 1 without it"

**Bad suggestions** (skill bugs, not environment insights -- do NOT suggest these):

- "The --type argument changed" -- that's expected from the PR diff

Format as a table with severity, collapsible fix prompts, and a count in the heading:

## Suggested Skill Updates (N issues found)

| #   | Severity        | File     | Issue               | Fix Prompt                                                                           |
| --- | --------------- | -------- | ------------------- | ------------------------------------------------------------------------------------ |
| 1   | <emoji> <level> | `<file>` | <short description> | <details><summary>Copy</summary><br>`<full droid prompt to fix the issue>`</details> |

**Severity levels:**

- `🔴 Breaking` -- Causes test failures every run
- `🟡 Degraded` -- Causes intermittent failures or suboptimal behavior
- `🔵 Info` -- New knowledge that improves future runs

Read the `failure_learning` field from config.yaml to determine the strategy:

- `auto_commit`: include the table in the report AND write a `qa-results/skill-updates.json` file so the workflow can apply the edits. Also commit the changes directly to the skill files.

**`skill-updates.json` format** (for `auto_commit`):

```json
[
  {
    "file": ".factory/skills/qa-cli/SKILL.md",
    "section": "Known Failure Modes",
    "action": "append",
    "content": "7. **Binary publish required.** Must use `dotnet publish`, not `dotnet build`, for E2E testing."
  }
]
```

After writing skill-updates.json, apply the edits to the actual skill files and commit with message `chore(qa): update failure catalog from QA run`.
