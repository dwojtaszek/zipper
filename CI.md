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

## CodeRabbit

Address blocking issues (required). Nitpicks are optional.

**Stale reviews after force-push:** CodeRabbit and other bots review the commit at push time. After amending and force-pushing, their comments may reference code that no longer exists. Verify comments still apply to current code before acting on them. Skip already-addressed comments — but reply acknowledging them.

## CodeQL

Failures block merge — must fix.

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

