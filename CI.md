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

## Robot Reviews

After creating a PR — and after every subsequent push — run the review-wait gate:

```bash
bash tests/wait-for-reviews.sh <PR-number> [timeout-minutes]   # default timeout: 20
```

The script blocks until each expected bot (CodeRabbit, Codex) has posted a review or declared a rate-limit skip, then lists every unresolved review thread and exits non-zero while any remain. Fix or reply-with-reason on each thread, resolve it, and re-run until exit 0. A bot that stays silent past the timeout produces a warning, not a failure.

Caveats:
- A "pass" check status from a review bot can mean "review skipped" (rate limit) — never treat check status as approval.
- Resolving a thread without a reply is prohibited; the thread must carry a fix reference or a skip reason.

Server-side enforcement: `main` has branch protection with **required conversation resolution** — GitHub refuses the merge while any review thread is unresolved. One-time setup (admin):

```bash
gh api -X PUT "repos/dwojtaszek/zipper/branches/main/protection" \
  --input - <<'JSON'
{
  "required_status_checks": null,
  "enforce_admins": false,
  "required_pull_request_reviews": null,
  "restrictions": null,
  "required_conversation_resolution": true
}
JSON
```

## CodeRabbit

Address blocking issues (required). Nitpicks are optional.

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
- **Semantic traceability audit (#957):** `tools/typesafe-audit/checks/traceability/run_check.py` runs AFTER `tests/validate-req-traceability.sh --strict` (which remains the blocking row-presence gate) and judges whether each mapped test actually verifies its requirement: `full` / `partial` / `none` / `ambiguous`, plus separate `mocked_only` and `execution_only` signals (line coverage and method invocation are not behavioral coverage). Deterministic preparation parses Requirements.md + the TSV, resolves `Class.Method` unit refs and `script.sh Scenario` e2e refs to exact source, and fails (exit 2) on malformed rows, unresolved, ambiguous, or oversized evidence BEFORE any model call — an API outage never weakens the strict gate. PR mode: changed requirements, changed mappings, and mappings whose referenced tests changed. Full mode (schedule/dispatch): every active non-exempt requirement in bounded batches. Every result carries raw probabilities, confidence, model, and stable source hashes. Advisory only; blocking may come later only for high-confidence `none` on newly changed requirements. Corpus: `tests/typesafe-audit-fixtures/traceability/`.
- **PR specification compliance side check (#958):** independent `pr-spec-review` job in `typesafe-audit.yml` for same-repository PRs (forks skip; `permissions` are `contents: read`, `pull-requests: read`, `issues: read` only). `tools/typesafe-audit/checks/pr-spec/collect_pr.py` collects the PR context via `gh`: changed files with patches (binaries, generated artifacts, and secrets excluded; strict size budgets), issue references resolved ONLY from explicit `Fixes/Closes/Resolves #N` body references (never branch names), and referenced issue bodies + comments in chronological order — the newest item is flagged `supersedes_older` and deterministically wins when spec texts conflict. Affected REQ IDs come from changed lines, traceability rows for changed test paths, and explicit references. Supplied policy evidence (`AGENTS.md`, `UBIQUITOUS_LANGUAGE.md`, `docs/code-review-guidelines.md`) is included so documented deliberate patterns cannot become findings. Per scoped obligation the model answers four independent questions: implementation (`implemented`/`partial`/`contradicted`/`not_addressed`/`ambiguous`), real-outcome-test and unrelated-behavior Nouls, and docs sync (`docs_synchronized`/`docs_not_required`/`readme_missing`/`requirements_missing`/`glossary_missing`/`architecture_approval_missing`). A PR with no explicit spec yields a neutral `no-explicit-spec` result, never a failure; low confidence routes to `needs-human-review`. Findings are advisory and trace every judgment to issue/requirement/diff evidence. Offline corpus (recorded responses): `tests/typesafe-audit-fixtures/pr-spec/`.
- **Quality prioritization audit (#959):** scheduled/manual workflow `typesafe-quality-audit.yml`, never on PRs. Phase 1 runs the unit suite with coverlet (`XPlat Code Coverage` → Cobertura); `tools/typesafe-audit/quality/coverage_gaps.py` deterministically extracts uncovered lines/branches per method, excluding generated code, trivial accessors, and configured noise, and attaches REQ IDs + owning test names from the traceability TSV. Phase 2 runs one pinned `dotnet-stryker` shard (`.config/dotnet-tools.json` v5.0.0, per-shard timeout, scope via dispatch input); `quality/mutation.py` parses survivors, compile errors, timeouts, and no-coverage mutants as separate deterministic categories. TypeSafe only scores supplied candidates on four independent Score questions (correctness, data-loss/output-corruption, security, regression); priorities are a checked-in weight composition (`quality/weights.json`) that always retains raw component scores, confidences, and source hashes. High-risk output/path/validation/arithmetic candidates must rank above trivial accessors in the evaluation corpus (`tests/typesafe-audit-fixtures/quality/`). API failure is advisory and preserves raw deterministic artifacts; runtime/cost budgets widen only after measured shard runtimes.
- **Ownership:** repository maintainers; the workflow is path-filtered and runs `contents: read` with all third-party actions pinned to full commit SHAs.

## factory-droid

Bot infra errors — retry, don't block merge.

## Goldens

Regenerate with:

```bash
# publish-bin/ is created by the publish step below; it is gitignored
dotnet publish src/Zipper.csproj -c Release -o ./publish-bin
ZIPPER_CLI=$(pwd)/publish-bin/Zipper bash tests/goldens/run-goldens.sh --capture
```

## Dependency Update Policy

All dependency version bumps must observe a **minimum 3-day waiting period** after the upstream release before merging. This applies to both Dependabot and manual dependency updates.

Rationale: new package releases occasionally introduce breaking changes or regressions that are caught within the first few days. Delaying adoption by 3 days reduces the risk of integrating unstable dependencies.

Implementation:
- Dependabot PRs may be opened immediately (schedule-controlled) but must not be merged until 3 days after the release date of the target version.
- Reviewers should verify the NuGet/GitHub release date before approving a dependency PR.

