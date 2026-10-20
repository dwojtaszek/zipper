# CI & External Checks

This file supplements [AGENTS.md](AGENTS.md). The Principles hierarchy in AGENTS.md applies — Critical Rules here may be overridden by Principles when flagged.

## SonarCloud

SonarCloud issues are NOT surfaced as GitHub check failures — fetch manually after CI completes on your PR (see [AGENTS.md workflow step 10](AGENTS.md)):

```bash
curl -s "https://sonarcloud.io/api/issues/search?componentKeys=dwojtaszek_zipper&pullRequest=NNN&statuses=OPEN,CONFIRMED&ps=50" | python3 -c "
import json,sys
data = json.load(sys.stdin)
for i in data['issues']:
    f = i['component'].split(':')[1]
    print(f'{i[\"severity\"]:10s} {i[\"rule\"]:25s} L{i[\"line\"]:4d}  {f}')
    print(f'  {i[\"message\"]}')
    print()
"
```

Issues ordered: `BLOCKER` → `MAJOR` → `MINOR` → `INFO`. MINOR and INFO are optional. Fix all BLOCKER and MAJOR issues before merge. *When this conflicts with Surgical Changes (e.g., a SonarCloud MAJOR in adjacent untouched code), Surgical Changes wins — add a `/* TODO: SONAR */` comment and file a follow-up issue.*

### Quality gate vs. code issues

The SonarCloud check can fail even with zero code issues. Common non-issue causes:

- **Security hotspots** (e.g., unpinned GitHub Actions) — fix by pinning to full commit SHA: `uses: owner/action@<full-sha> # vN`
- **Coverage gate** — may not be actionable for test-only or infra PRs; not a merge blocker if no code issues exist

Check quality gate details if the check fails but no issues are found:

```bash
curl -s "https://sonarcloud.io/api/qualitygates/project_status?projectKey=dwojtaszek_zipper&pullRequest=NNN" | python3 -m json.tool
```

### Fix cycle

1. Fix locally
2. Amend the last commit and force-push **your PR branch** (not `main`): `git commit --amend --no-edit && git push --force-with-lease`
3. Re-check after CI re-runs

## Zipper Runner Tests

The `.zipper-runner/tests` suites (runner CLI, subprocess, and Git-boundary regressions) run in their own `runner-tests` job in [ci.yml](.github/workflows/ci.yml), on an isolated checkout — **not** inside the .NET E2E suite, where a Git-environment failure used to look like a product failure (#1173). They carry their own fixture Git identity via `.zipper-runner/tests/gitenv.py`, so they pass on a machine with no global `user.name`/`user.email`; the job additionally fills a missing identity as a second line of defence for any Git work outside the tests.

```bash
# Empty HOME = no global Git identity, the exact CI failure mode.
HOME=/nonexistent python3 -m unittest discover -s .zipper-runner/tests
```

Adding a new test file to that directory? Do not run it through `tests/test-typesafe-audit.sh` — it will not be picked up there anymore, and the suite is deliberately scoped to the TypeSafe audit tool.

## Release recovery and exact-artifact acceptance

Release allocation/publication is serialized across main, manual stable tags, and recovery runs without cancelling in-progress runs. GitHub may replace a pending run before it starts; it has not reserved an intent or entered publication. Re-dispatch if that source still needs a release. `.github/scripts/release.py` stores its SHA-bound intent in a validated terminal record in the draft release body; release notes are not state. See [docs/cicd.md](docs/cicd.md#4-build-and-test-main--build-and-testyml) for the state transitions.

Re-run an interrupted Build and Test workflow, or dispatch it on main with `source_sha` set to the existing intent's source SHA. Set `release_tag` to the existing intent tag when that source has multiple intents (for example, both main and a manual tag). Recovery requires an existing, unambiguous intent. Recovery matches drafts by the terminal intent's tag, not the mutable draft name, and both release jobs run the controller from the triggering ref (a second checkout at `.controller`), never the controller baked into the historical source SHA — a fixed pipeline can recover a draft its own source commit's controller broke. Every draft update re-sends the intent's `tag_name` and `target_commitish`; if an older pipeline ever detached them (an auto-generated `untagged-…` draft name), a sealed matching draft is re-bound to its intent tag during publication before any upload. Named-tag, public, unsealed, or source-mismatched drift fails closed instead. A sealed intent downloads the original accepted artifacts using immutable artifact IDs and their original `artifact_run`, not mutable names that reruns may reuse. It checks the tag SHA, preserves already uploaded assets, verifies every remote asset by downloading and hashing it, then completes the draft. Only an empty `starter` failed-upload placeholder on that draft may be deleted and re-uploaded. A completed intent does not rebuild or republish. An old main intent can finish but cannot become newest. Artifact retention is 30 days; after expiry, a sealed intent fails closed if the original accepted bytes are unavailable. Do not delete/reallocate the intent or overwrite its tag/assets to bypass that failure.

Local offline regressions: `python3 -m unittest discover -s tests/ci -v` and `bash tests/test-build-version.sh`. Native acceptance uses an absolute `ZIPPER_EXECUTABLE`, `EXPECTED_VERSION`, and `EXPECTED_SHA` with `tests/test-published-artifact.sh` or `.bat`. Explicit executable overrides never build or fall back to `dotnet run`. The independent metadata reader inspects `Zipper.dll` extracted from the actual single-file bundle; the target CLI runs as the native executable. Linux also runs the checked-in golden scenarios against that same candidate. Windows/macOS acceptance must pass on native CI runners; a Linux cross-publish alone is not evidence of execution there.

## Robot Reviews

After creating a PR — and after every subsequent push — run the review-wait gate:

```bash
bash tests/wait-for-reviews.sh <PR-number> [timeout-minutes]   # default timeout: 20
```

The script blocks until each expected bot (CodeRabbit, Codex) has posted a review or declared a rate-limit skip, then lists every unresolved review thread and exits non-zero while any remain. Fix or reply-with-reason on each thread, resolve it, and re-run until exit 0. A bot that stays silent past the timeout produces a warning, not a failure.

Caveats:
- A "pass" check status from a review bot can mean "review skipped" (rate limit) — never treat check status as approval.
- Resolving a thread without a reply is prohibited; the thread must carry a fix reference or a skip reason.

Server-side enforcement: `main` has branch protection with **required conversation resolution** and a **required status check** — GitHub refuses the merge while any review thread is unresolved or the `ci-gate` check is red. `ci-gate` (aggregate job in `pr.yml`) always runs, even on docs-only PRs where the other jobs are skipped, and fails if any needed job (`docs-only`, `ci`, `goldens`) failed or was cancelled. `ci` is the reusable workflow [ci.yml](.github/workflows/ci.yml) — `lint`, `runner-tests`, `version-metadata`, and the three `build-and-test` matrix legs running the full E2E suite — so a red leg inside it still fails the caller job and therefore `ci-gate`. The required check context is always posted. `enforce_admins` is on — no one merges red CI. One-time setup (admin):

```bash
gh api -X PUT "repos/dwojtaszek/zipper/branches/main/protection" \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": false,
    "checks": [
      { "context": "ci-gate" }
    ]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": null,
  "restrictions": null,
  "required_conversation_resolution": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false,
  "lock_branch": false,
  "allow_fork_syncing": false,
  "required_signatures": false,
  "required_linear_history": false
}
JSON
```

Note: `gh pr checks --watch` exits 0 even when checks fail — always pass `--exit-status` (or verify every check's conclusion explicitly) before merging. PR #968–#972 merged with red ubuntu/macos E2E jobs because no checks were required and the watch exit code was trusted without it.

## CodeRabbit

Address blocking issues (required). Nitpicks are optional.

**Repository Star Threshold (< 10 stars):** On public repositories with fewer than 10 stars, the CodeRabbit GitHub App skips automatic reviews on PR creation/update (posting an informational skip comment and marking the check skipped). The local CLI gate (`coderabbit review --committed --base main --agent`) is therefore the primary and required review gate before pushing. To trigger a remote GitHub App review on a specific PR, comment `@coderabbitai review` on the PR.

**Stale reviews after force-push:** CodeRabbit and other bots review the commit at push time. After amending and force-pushing, their comments may reference code that no longer exists. Verify comments still apply to current code before acting on them. Skip already-addressed comments — but reply acknowledging them.

## CodeQL

Failures block merge — must fix.

## TypeSafe Audit

Advisory side check (issue #955): [`typesafe-audit.yml`](.github/workflows/typesafe-audit.yml) + `tools/typesafe-audit/`. **Never blocks merge** — findings, remote outages, rate limits, and low confidence are reported only.

- **Secret setup:** repository Actions secret `TYPESAFE_API_KEY`. The runner reads it from the process environment only — never `.env`, files, or workflow `vars`. Fork PRs cannot access it and skip with a neutral summary (`pull_request_target` is forbidden).
- **Model pinning:** `jev-1.13.0` is pinned in `tools/typesafe-audit/config.json`. Model upgrades require a reviewed PR with an evaluation comparison.
- **Local run (no network):**
  ```bash
  mkdir -p /tmp/tsa-fixtures
  python3 tools/typesafe-audit/record_sample_fixture.py /tmp/tsa-fixtures
  python3 tools/typesafe-audit/runner.py --mode fixture --fixture-dir /tmp/tsa-fixtures \
    --files tools/typesafe-audit/questions/files-sample.list \
    --questions tools/typesafe-audit/questions/example.json \
    --json-out report.json --md-out report.md
  ```
  Exit codes: `0` success/no gated finding, `1` policy finding (only with `--strict`/`blocking`), `2` config/input error, `3` remote service failure.
- **Failure modes:** remote failure (HTTP 429/529/5xx, network) → exit 3, advisory, retryable; input/config error (bad paths, size limits, missing secret) → exit 2; findings → reported in JSON/Markdown/job summary.
- **Rollout:** advisory until ≥30 labeled PR/full-audit outcomes meet documented precision/recall and confidence thresholds; blocking switches only in a later reviewed change. Deterministic gates are unaffected.
- **Requirements semantic audit (#956):** `tools/typesafe-audit/checks/requirements/run_check.py` audits changed requirement/doc sections (docs-only PRs included) with five advisory judgments plus deterministic prechecks (immutable REQ IDs, orphan references, missing traceability rows). PR mode: changed sections vs `github.event.pull_request.base.sha`. Manual dispatch `mode: full`: every active requirement, bounded by `full_mode_max_requests`. Findings cite supplied evidence spans only — TypeSafe cannot invent paths or REQ IDs; low-confidence/mid-range signals are `needs-human-review`. Evaluation corpus: `tests/typesafe-audit-fixtures/` with expected labels (recorded-response accuracy target 1.0). Local reproduction:
  ```bash
  python3 tools/typesafe-audit/record_corpus_fixtures.py /tmp/tsa-fx
  python3 tools/typesafe-audit/checks/requirements/run_check.py \
    --corpus tests/typesafe-audit-fixtures --mode fixture --fixture-dir /tmp/tsa-fx \
    --json-out req.json --md-out req.md
  ```
- **Semantic traceability audit (#957):** `tools/typesafe-audit/checks/traceability/run_check.py` runs AFTER `tests/validate-req-traceability.sh --strict` (the blocking deterministic reference-integrity gate, which validates offline that every active `REQ-NNN` and legacy `REQ_E-NNN` contract is mapped or reasoned-exempt, that all test references resolve to existing executable test cases, and that no orphaned or malformed entries exist) and judges whether each mapped test actually verifies its requirement: `full` / `partial` / `none` / `ambiguous`, plus separate `mocked_only` and `execution_only` signals (line coverage and method invocation are not behavioral coverage). Deterministic preparation parses Requirements.md + the TSV, resolves `Class.Method` unit refs and `script.sh [Scenario]` e2e refs to exact source, and fails (exit 2) on malformed rows, unresolved, ambiguous, or oversized evidence BEFORE any model call — an API outage never weakens the strict gate. A script-only reference supplies the whole script; a scenario reference supplies its existing scenario block. Scripts are resolved beneath the repository's `tests/` directory as well as fixture roots. C# process tests use `unit` mappings because their source is a `Class.Method`, without changing the behavior they test. PR mode: changed requirements, changed mappings, and mappings whose referenced tests changed. Full mode (schedule/dispatch): every active non-exempt requirement in bounded batches. Every result carries raw probabilities, confidence, model, and stable source hashes. Advisory only; blocking may come later only for high-confidence `none` on newly changed requirements. Corpus: `tests/typesafe-audit-fixtures/traceability/`.
- **Architecture seam lint:** `tools/typesafe-audit/checks/architecture/run_check.py`, a step in the `audit` job of `typesafe-audit.yml`. Enforces the Critical Rule 5 seam contract semantically on diffs that touch `src/LoadFiles/**` or `docs/architecture.md`: one bounded Jev request judges `seam_bypass` (does the diff make a load-file format write output outside the `composer -> serializer -> emitter` seam or move format dispatch out of `LoadFileOrchestrator`; the EDRM-XML carve-out is the only sanctioned exception) and `diagram_stale` (does the diff make the architecture diagrams inaccurate without updating `docs/architecture.md` in the same diff). Deterministic prechecks run before any model call: no seam file touched -> neutral exit 0 with no API call; `src/LoadFiles/XmlLoadFileWriter.cs` changed without `docs/architecture.md` in the same diff -> a `needs-human-review` row with no model call. Advisory; findings never gate — Critical Rule 5's explicit human approval still governs. Schedule/dispatch scope: `HEAD~1` (the last merged commit on main); PRs are the primary scope.
- **PR specification compliance side check (#958):** independent `pr-spec-review` job in `typesafe-audit.yml` for same-repository PRs (forks skip; `permissions` are `contents: read`, `pull-requests: read`, `issues: read` only). `tools/typesafe-audit/checks/pr-spec/collect_pr.py` collects the PR context via `gh`: changed files with patches (binaries, generated artifacts, and secrets excluded; strict size budgets), issue references resolved ONLY from explicit `Fixes/Closes/Resolves #N` body references (never branch names), and referenced issue bodies + comments in chronological order — the newest item is flagged `supersedes_older` and deterministically wins when spec texts conflict. Affected REQ IDs come from changed lines, traceability rows for changed test paths, and explicit references. Supplied policy evidence (`AGENTS.md`, `UBIQUITOUS_LANGUAGE.md`, `docs/code-review-guidelines.md`) is included so documented deliberate patterns cannot become findings. Per scoped obligation the model answers four independent questions: implementation (`implemented`/`partial`/`contradicted`/`not_addressed`/`ambiguous`), real-outcome-test and unrelated-behavior Nouls, and docs sync (`docs_synchronized`/`docs_not_required`/`readme_missing`/`requirements_missing`/`glossary_missing`/`architecture_approval_missing`). A PR with no explicit spec yields a neutral `no-explicit-spec` result, never a failure; low confidence routes to `needs-human-review`. Findings are advisory and trace every judgment to issue/requirement/diff evidence. Offline corpus (recorded responses): `tests/typesafe-audit-fixtures/pr-spec/`.
- **Quality prioritization audit (#959):** scheduled/manual workflow `typesafe-quality-audit.yml`, never on PRs. Phase 1 runs the unit suite with coverlet (`XPlat Code Coverage` → Cobertura); `tools/typesafe-audit/quality/coverage_gaps.py` deterministically extracts uncovered lines/branches per method, excluding generated code, trivial accessors, and configured noise, and attaches REQ IDs + owning test names from the traceability TSV. Phase 2 runs one pinned `dotnet-stryker` shard (`.config/dotnet-tools.json` v5.0.0, per-shard timeout, scope via dispatch input); `quality/mutation.py` parses survivors, compile errors, timeouts, and no-coverage mutants as separate deterministic categories. TypeSafe only scores supplied candidates on four independent Score questions (correctness, data-loss/output-corruption, security, regression); priorities are a checked-in weight composition (`quality/weights.json`) that always retains raw component scores, confidences, and source hashes. High-risk output/path/validation/arithmetic candidates must rank above trivial accessors in the evaluation corpus (`tests/typesafe-audit-fixtures/quality/`). API failure is advisory and preserves raw deterministic artifacts; runtime/cost budgets widen only after measured shard runtimes.
- **Issue triage (dry-run, #960):** `typesafe-issue-triage.yml` on `issues: [opened, edited, reopened]` + manual dispatch, `permissions: contents: read, issues: read` — dry run cannot modify GitHub state (no labels/comments/closures/assignments/new issues until a separate write-mode PR lands after 50+ maintainer-reviewed issues). `tools/typesafe-audit/triage/collect_issue.py` reads issue text and open-issue candidates via `gh` as JSON data only (issue numbers, never issue text, cross the shell boundary). Deterministic preparation strips markup with size caps, extracts keywords (REQ IDs, file paths, domain terms), retrieves bounded duplicate candidates by keyword overlap, and supplies closed sets (issue types, subsystems, priorities, active REQ IDs). TypeSafe Choices (type, subsystem, priority, duplicate-vs-related-vs-unrelated-vs-insufficient-evidence per candidate) and Nouls (missing reproduction/spec info, REQ relevance) select among supplied values only; any selection outside the closed sets is demoted to `needs-human-review`. Duplicate suggestions require both deterministic retrieval and high-confidence judgment with candidate links. API failure/no candidates/low confidence are neutral results; raw distributions retained. Corpus: `tests/typesafe-audit-fixtures/triage/`.
- **Ownership:** repository maintainers; the workflow is path-filtered and runs `contents: read` with all third-party actions pinned to full commit SHAs.

Traceability uses the same configured batch size and maximum-request budget in
both PR and full modes. A mapping-file change can select every requirement; it
must not collapse that scope into one unbounded API request.

Preparation builds a fresh run-local class lookup and reuses source text for
unit and E2E references. Every mapping is still validated before scope selection
and before any API call; ambiguity checks and evidence hashes are unchanged.
No source cache persists between runs. Local three-run preparation benchmark
(257 mappings, Linux, Python 3.14) improved from 24.55 s median to 0.387 s,
with identical complete evidence hashes. This is preparation time, not total
hosted workflow time or API latency.

## factory-droid

Bot infra errors — retry, don't block merge.

## GitHub Advanced Security (Copilot code-scanning agent)

`dynamic/agents/github-advanced-security` ("Code scanning AI findings on PR #N") is GitHub-owned infrastructure, not a repo workflow — it cannot be edited from this repo, so no `concurrency:` group and no retry policy can be added to it, and it re-runs on every `pull_request` event regardless of repo-level cancellation. It is **not** in `ci-gate`'s needs, not in branch protection (required: `ci-gate` only), and not even surfaced in `gh pr checks`. Its failures are Copilot agent runtime flakes (proxy/runtime download, sandbox setup) and, when the monthly Copilot quota is exhausted, `SessionModelError: You have exceeded your monthly quota` — both outside this repository's control. Treat exactly like factory-droid: advisory noise, never a merge blocker, never worth chasing. Do not investigate its logs during PR closeout; if curious, re-run the workflow instead. The only in-repo lever is a repo setting (disable the dynamic agent), which is an admin action, not a PR.

## Goldens

Regenerate with:

```bash
# publish/ is created by the publish step below; it is gitignored
dotnet publish src/Zipper.csproj -c Release -r linux-x64 --self-contained true -o ./publish/linux-x64
ZIPPER_CLI=$(pwd)/publish/linux-x64/Zipper bash tests/goldens/run-goldens.sh --capture
```

## Dependency Update Policy

All dependency version bumps must observe a **minimum 3-day waiting period** after the upstream release before merging. This applies to both Dependabot and manual dependency updates.

Rationale: new package releases occasionally introduce breaking changes or regressions that are caught within the first few days. Delaying adoption by 3 days reduces the risk of integrating unstable dependencies.

Implementation:
- Dependabot PRs may be opened immediately (schedule-controlled) but must not be merged until 3 days after the release date of the target version.
- Reviewers should verify the NuGet/GitHub release date before approving a dependency PR.

