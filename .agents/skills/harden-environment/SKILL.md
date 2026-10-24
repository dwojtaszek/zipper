---
name: harden-environment
description: Use when the same agent mistake, review finding, mutation survivor, or CI failure appears twice, or when spot-checking recently merged PRs, to turn the pattern into a permanent constraint instead of another prose rule.
---

# Harden the environment

A repeated correction is an environment bug. Fix the kitchen, not the dish.

The agent will take the path of least resistance. The only durable fix is to make
the wrong path fail and the right path the obvious one.

## When this applies

Two or more occurrences with the same shape:

- the same review comment on different PRs
- the same agent shortcut, workaround, or tautological test
- the same mutation survivor
- the same hook or CI failure
- the same wrong wiring point for a new CLI flag or Load File column

One occurrence is an incident. Fix it, close it, do not add a rule for it.

## 1. Collect evidence before changing anything

Read-only commands from the repository root:

```bash
gh pr list --state merged --limit 20 --json number,title,mergedAt
gh run list --limit 20
python3 .agents/skills/mine-agent-transcripts/scan.py --days 30
bash tests/run-mutation-smoke.sh

# Review bots post to three separate endpoints; read all three.
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
for ep in "pulls/$N/comments" "pulls/$N/reviews" "issues/$N/comments"; do
  gh api "repos/$REPO/$ep" --paginate --jq '.[].body'
done
```

Write one line per occurrence: PR, file, what the agent did. Keep the queue in a
`quality` issue or a PR comment, not as TODOs in source.

Do not fix the individual instances yet. The batch view is what reveals the rule.

## 2. Pick the narrowest layer that fails on the bad shape

Cost is what you pay to write it plus what you pay to keep it true. A single
assertion is a test, not an analyzer. A rule that must bind every call site is an
analyzer, not a test.

| Layer | Use for | Where |
|---|---|---|
| Format | whitespace, ordering, naming | `.editorconfig` |
| Banned API | a method or type that must not be called | `BannedSymbols.txt` |
| Analyzer | a structural mistake that must fail at every call site | `src/Zipper.Analyzers/` + `src/Zipper.Analyzers.Tests/` |
| Test | one behavioral contract, including test quality | `src/Zipper.Tests/` (property tests in `Properties/`) |
| Golden | byte output drift | `tests/goldens/` |
| Hook or script | a command an agent must run, or a test-strength gate | `.github/hooks/`, `dev.sh`, `tests/` |
| Prose | judgment that no check can express | `AGENTS.md`, `docs/` |

Existing analyzer IDs: ZIP001 Load File seam, ZIP002 deterministic formattable,
ZIP003 exact stream read, ZIP004 sealed domain types. Extend one only when its
rule is actually about the same invariant. Do not contort an unrelated analyzer,
and do not add a fifth for a rule a single test can hold.

A weak test is its own cluster: a test that asserts a column exists but not its
serialized value leaves the value free to change. The constraint is a mutation
operator or test that observes the value, not another assertion of existence. If
the mutation gate has no operator for that value shape, add one or write the
value-observing test. A missing operator is not an answer.

Prose is the last resort. A rule an agent can skip is not a constraint, and a
duplicated rule in prose plus a check is just drift waiting to happen.

## 3. Prove the constraint fires

The deliverable is a command whose result changes because of the constraint:

- analyzer: a case in `src/Zipper.Analyzers.Tests/` that fails on the bad shape
  and passes on the fixed shape
- test: red first, then implement
- banned symbol or `.editorconfig`: a scratch violation that produces the
  diagnostic, pasted into the PR
- hook or script: make a scratch change that violates the rule, run the gate,
  paste the failure, then revert the scratch change

If you cannot show the failure, you added prose, not a constraint.

Some constraints cannot exercise themselves. A mutation-operator change has no
`src/` candidates in its own diff, so the pre-push gate exits green without
running the new operator. Prove it manually with the scratch change above, paste
the output in the PR, and add a test or follow-up issue that keeps it honest.

## 4. Land one change, not thirty

One cluster gets one PR: the constraint, its proof, and the docs the change
invalidates. Link the occurrences it prevents. Two clusters may share a PR only
when each proof can still be read on its own.

Close the queued instances in the same PR only when the constraint makes them
mechanical to fix. Otherwise file follow-ups labelled `quality` and let the next
batch sweep them. Thirty hand-fixes teach the environment nothing.

When the check lands, cut the prose that restated the same rule down to a pointer,
or delete it. A check plus a restated rule drifts.

## Red flags

- "I'll remember this next time." You will not. Encode it.
- "A test is enough here." Then write the test. The point is a check, not the
  most impressive check.
- "The rule is obvious." Agents take the easy path, not the obvious one.
- "Add it to AGENTS.md." Only after the cheaper layers are genuinely unavailable.
- "Fix all thirty sites now." Batch the constraint first, or you never get to it.
