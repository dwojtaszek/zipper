---
name: qa
description: >
  Run functional QA for Zipper. Maps the git diff to CLI flows, tests the
  built application through Droid Control, and writes a concise QA report.
  Use when testing PRs, releases, or local changes.
---

# QA Orchestrator

**SCOPE: Functional QA only.** Verify real CLI behavior by interacting with Zipper and inspecting generated Archives, Load Files, Production Sets, and reports. Do not run or report unit tests, E2E scripts, formatting, builds as test cases, or static analysis.

## 1. Load configuration and scope the run

Read `.factory/skills/qa/config.yaml`. Use its target, path patterns, persona, cleanup rule, `video_evidence`, and `droid_control.compose`.

Before writing output, refuse a symlinked `qa-results` root, create the directory if absent, and verify its resolved path is exactly `qa-results` under the checkout. If this check fails, report BLOCKED and stop. Set `RUN_ID="${QA_RUN_ID:-$(date +%s)-$$}"` and export it. Require `RUN_ID` to match `^[A-Za-z0-9_-]+$`; otherwise report BLOCKED and stop. Create `qa-results/$RUN_ID` only if it does not already exist; if it exists, report BLOCKED rather than reusing or deleting it. Store generated data and build output under that directory; preserve `qa-results/report.md` and evidence.

For an affected interactive app, invoke `droid-control` before interaction. Use its terminal route, Capture, and Verify stages. Read `droid_control.compose` on every run. Invoke Compose only when both `video_evidence` and `droid_control.compose` are true; otherwise, do not load Compose. Keep these decisions conditional on the config values.

## 2. Select the target

Use `default_target` unless the user names another configured target. Zipper runs locally and has no hosted application URL, authentication, roles, or external app services. Use synthetic data only.

## 3. Analyze the diff

Use `QA_DIFF_BASE` when provided (the PR base in CI). Otherwise compare against `origin/main`; if that is unavailable, compare the latest commit with its parent. Include committed changes since that base, `git diff --name-only HEAD`, and paths from `git ls-files --others --exclude-standard` before matching app patterns. This includes staged, unstaged, and untracked app files.

Map changed paths to `apps.*.path_patterns`. Files outside every app's patterns, including this QA setup, docs, and unrelated CI files, do not trigger app flows.

- Run only flows relevant to the affected app and the diff.
- Add a direct, change-specific check when the menu has no matching flow.
- Include adjacent flows only when they verify integration with the change.
- Ensure at least half of reported cases directly test the changed behavior.
- For any CLI-path diff, include at least one successful behavior flow and at least one relevant negative or boundary flow. Prefix the Test Case with `[positive]`, `[negative]`, or `[boundary]`, followed by a descriptive flow name. Put concise observed evidence in the Notes column.
- If no app path changed, report one INCONCLUSIVE row with Test Case exactly `No app code changed` and Notes `No app code changed -- QA not applicable for this diff.` Do not build or run app flows.

## 4. App-specific pre-flight

For the affected CLI app only:

1. Build with `apps.cli.build_command`, after setting `RUN_ID`.
2. Confirm `qa-results/$RUN_ID/publish/Zipper` exists (or `Zipper.exe` on Windows).
3. Confirm Droid Control is active and its terminal route prerequisites are available. If not, report the app flows as BLOCKED with the missing prerequisite and remediation.

Run the build through Droid Control's terminal route. The build is a prerequisite, not a report row. Do not run pre-flight steps for an unaffected app. When Compose is selected by config, let Droid Control resolve its own plugin root; if its `remotion/node_modules` is missing, install from the lockfile in that `remotion` directory before rendering. Never install Remotion dependencies otherwise.

## 5. Choose and run flows

Read `.factory/skills/qa-cli/SKILL.md`. Treat its test menu as options, not a checklist:

- Select only flows that exercise the diff, plus relevant integration checks.
- Execute Zipper through Droid Control's terminal route. The CLI is one-shot, so send each invocation through a run-scoped terminal session; use real arguments and inspect real generated files.
- Do not launch raw `tuistory` or bypass Droid Control for terminal interactions.
- Do not run the repository's E2E scripts or any unit-test/static-analysis suite.
- If an existing flow does not cover the diff, add an ad-hoc interaction that directly proves the changed behavior.

## 6. Capture evidence

Use Droid Control Capture and Verify for the selected route. For each numbered report case, save the terminal's unedited `$TCTL -s "$session" snapshot --trim` output after the Zipper invocation to `qa-results/$RUN_ID/evidence/case-<number>.snapshot.txt`, before closing that case's session. Include the actual CLI output and a shell `APP_EXIT:<number>` marker in the snapshot; keep verifier output separately when needed. Each snapshot must show distinct evidence. In `qa-results/report.md`'s evidence block, link each snapshot with a report-relative Markdown link, `[$label]($RUN_ID/evidence/case-<number>.snapshot.txt)`, so the links also work in the downloaded artifact. Do not replace snapshots with verification summaries.

When `imagemagick` is true and the change has meaningful before/after screenshots, use ImageMagick to create an animated GIF diff under the evidence directory. Do not fabricate a baseline; skip the GIF when no real comparison exists.

When `video_evidence` is true, capture one recording per flow. Keep raw terminal casts as downloadable artifacts, never upload `.cast` files as video. When both runtime settings permit Compose, render one verified MP4 per flow using a `single` layout and the literal `"preset": "factory"`. Do not derive or configure the preset. For GitHub uploads, use the returned URL exactly: bare URL for videos, image Markdown for screenshots. Upload failures fall back to text and artifacts.

When `video_evidence` is false, use those raw text snapshots as primary evidence. Do not embed screenshot or repository URLs in GitHub comments; name any files retained in the workflow artifact.

## 7. Handle failures and clean up

**Never silently skip a flow. If a flow cannot complete, report it as BLOCKED with what was tried and how the user can fix it.** Continue with other relevant flows. Distinguish FAIL (behavior incorrect) from BLOCKED (environment or prerequisite prevented a valid test).

Delete only the run's `data/` and `publish/` subdirectories after testing, after verifying `qa-results`, the run directory, and both targets are not symlinks. Preserve the report and evidence. Never clean or overwrite other output paths.

## 8. Report and failure learning

Write `qa-results/report.md` using `.factory/skills/qa/REPORT-TEMPLATE.md`.

- Start with `## QA Report` and the result table.
- Use `:white_check_mark:` PASS, `:x:` FAIL, `:no_entry:` BLOCKED, `:warning:` FLAKY, or `:grey_question:` INCONCLUSIVE in the Result column.
- Keep the report concise: table, short Action Required section if needed, and one collapsed evidence block.
- Do not include a behavioral-change summary, setup rows, or unrelated-flow commentary.

Read `failure_learning` from config. For `suggest_in_report`, add a “Suggested Skill Updates (N issues found)” section only when a BLOCKED or FAIL result exposed new testing-environment knowledge not already in the sub-skill's Known Failure Modes. Use a table with columns `#`, `Severity`, `File`, `Issue`, and `Fix Prompt`. Classify severity as Breaking, Degraded, or Info. Each fix prompt must be self-contained; put it in a collapsed `<details><summary>Copy</summary>` block. Include the exact target file, heading, and insertion point in the issue or prompt. Do not suggest selector fixes or expected behavior changes. Do not write `skill-updates.json`.
