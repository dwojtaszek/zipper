---
name: github-issue-workflow
description: End-to-end GitHub issue workflow. Finds lowest-number open issue, checks for blockers in description and comments, implements on a branch, creates PR, monitors CI, addresses issues, and merges on green.
---

# GitHub Issue Workflow Skill

Execute the complete issue-to-merge workflow for the lowest-number open issue.

## Workflow Steps

### 1. Find Lowest-Number Open Issue

```bash
gh issue list --state open --limit 100 --json number,title,labels,body --jq 'sort_by(.number) | .[0]'
```

Extract: issue number, title, labels, body.

### 2. Check for Blockers

Review the issue description and all comments for blocker indicators:

```bash
gh issue view <number> --comments
```

**Blocker signals:**
- `blocked by` / `depends on` / `waiting for` in description or comments
- References to other issues that must be resolved first
- Labels: `blocked`, `dependency`, `waiting`
- PR references that are still open

**If blocked:**
- Report the blocker to the user
- Ask whether to proceed anyway or pick the next issue
- Do not proceed without user confirmation

### 3. Create Feature Branch

```bash
git checkout main && git pull
git checkout -b fix/ISSUE-<number>-<short-desc>  # for bugs
git checkout -b feat/ISSUE-<number>-<short-desc>  # for features
```

Branch prefix convention: `fix/`, `feat/`, `refactor/`, `test/`, `docs/`, `chore/`

### 4. Implement the Solution

Follow the project's AGENTS.md or contributing guidelines for:
- TDD approach (write failing test first)
- Code style and formatting
- Required documentation updates

After implementation:
```bash
dotnet format --verify-no-changes src/  # or project-specific lint
dotnet test                              # or project-specific test command
```

### 5. Commit with Conventional Commits

```bash
git add <files>
git commit -m "<type>: <description>

Closes #<issue-number>"
```

Types: `fix:`, `feat:`, `refactor:`, `test:`, `docs:`, `chore:`, `deps:`

### 6. Push and Create PR

```bash
git push -u origin <branch-name>
```

```bash
gh pr create --title "<type>: <description>" --body "
## Summary
<Brief summary of changes>

## Changes
- <Change 1>
- <Change 2>

## Testing
- <How this was tested>

Closes #<issue-number>
"
```

### 7. Monitor CI

```bash
gh pr checks <pr-number> --watch
```

Or check status:
```bash
gh pr view <pr-number> --json statusCheckRollup
```

**If CI fails:**
1. Read the failure logs: `gh run view <run-id>`
2. Fix the issues locally
3. Commit and push: `git commit --amend --no-edit && git push --force-with-lease`
4. Re-monitor until green

### 8. Check SonarCloud Issues

SonarCloud issues are NOT surfaced as GitHub check failures — fetch manually after CI completes:

```bash
curl -s "https://sonarcloud.io/api/issues/search?componentKeys=dwojtaszek_zipper&pullRequest=<pr-number>&statuses=OPEN,CONFIRMED&ps=50" | python3 -c "
import json,sys
data = json.load(sys.stdin)
if data['total'] == 0:
    print('No open SonarCloud issues.')
else:
    for i in data['issues']:
        f = i['component'].split(':')[1]
        print(f'{i[\"severity\"]:10s} {i[\"rule\"]:25s} L{i.get(\"line\",0):4d}  {f}')
        print(f'  {i[\"message\"]}')
        print()
"
```

**Fix all BLOCKER and MAJOR issues before merge.** MINOR and INFO are optional.

**Quality gate vs. code issues:** The SonarCloud check can fail even with zero code issues. Common non-issue causes:
- **Security hotspots** (e.g., unpinned GitHub Actions dependencies) — fix by pinning to full commit SHA: `uses: owner/action@<full-sha> # vN`
- **Coverage gate** — may not be actionable for test-only or infra PRs; not a merge blocker if no code issues exist

Check the quality gate details if the check fails but no issues are found:
```bash
curl -s "https://sonarcloud.io/api/qualitygates/project_status?projectKey=dwojtaszek_zipper&pullRequest=<pr-number>" | python3 -m json.tool
```

If issues are found:
1. Fix locally
2. Amend commit and force push
3. Re-check after CI re-runs

### 9. Address Review Comments

Watch for PR comments:
```bash
gh pr view <pr-number> --comments
```

For each review comment:
- Address the feedback
- Commit changes
- Push to the same branch
- Reply to the comment indicating it's addressed

**Stale reviews after force-push:** CodeRabbit and other bots review the commit at push time. After amending and force-pushing, their comments may reference code that no longer exists. Before acting on a review comment, verify it still applies to the current code. Skip comments that were already addressed by the amend.

### 10. Merge on Green

When all checks pass and reviews are complete:

```bash
gh pr merge <pr-number> --squash --delete-branch
```

Or use merge commit if project prefers:
```bash
gh pr merge <pr-number> --merge --delete-branch
```

## Error Handling

- **Issue not found**: Report to user, ask for guidance
- **Branch exists**: Ask user whether to delete and recreate or use different name
- **Merged PR cannot be reopened**: Create a follow-up branch from main (e.g., `fix/ISSUE-NNN-followup`) and open a new PR instead
- **CI repeatedly fails**: Diagnose root cause, ask user for guidance if stuck
- **Review requests major changes**: Implement changes, do not argue with reviewers
- **Merge conflicts**: Rebase onto main and resolve

## Best Practices

- **Pin third-party GitHub Actions to full commit SHAs** with a version comment. SonarCloud flags `uses: action@vN` as a security hotspot. Use: `uses: owner/action@<full-sha> # vN`. Look up SHAs via: `curl -s "https://api.github.com/repos/OWNER/REPO/git/ref/tags/vN" | jq .object.sha`

## Example Session

```
User: Take the lowest number issue

1. Found issue #42: "Fix null reference in email parser"
2. No blockers detected in description or comments
3. Created branch: fix/ISSUE-42-email-null-ref
4. Implemented fix with test
5. Committed: "fix: handle null reference in email parser"
6. Created PR #43
7. CI passed (all checks green)
8. Review approved with minor suggestion
9. Addressed suggestion, pushed
10. Merged PR #43

Issue #42 is now closed.
```
