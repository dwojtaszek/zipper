---
name: mine-agent-transcripts
description: Use when the user asks what keeps going wrong, after a session where the agent needed repeated correction, or before hardening the environment, to mine past OpenCode sessions in this repository for recurring corrections.
---

# Mine agent transcripts

Past corrections are a materialized process. They are the spec for what the
environment should have prevented.

## Read the history

From any worktree of this repository:

```bash
python3 .agents/skills/mine-agent-transcripts/scan.py --days 30
python3 .agents/skills/mine-agent-transcripts/scan.py --days 90 --corrections
```

`--corrections` narrows to user turns that look like a correction. `--full` keeps
long messages intact. The script is read-only, resolves the project root through
`git rev-parse --git-common-dir`, and matches every session and worktree of the
project.

Set `OPENCODE_DB` to read another OpenCode database. The script covers OpenCode
sessions only, not Claude Code or Cursor history.

## Cluster, do not fix

Group the turns by what the agent kept getting wrong. A cluster needs at least two
turns and the same underlying shape:

- the same wrong file or layer touched
- the same missing verification step
- the same wording correction, repeated
- the same question the agent should have answered from the repository

Write one line per cluster with links to the turns. Ignore preferences that appear
once and never again; they are noise, not process.

## Hand off the clusters

Pass the cluster list to `harden-environment`. Each cluster becomes one constraint,
at the cheapest layer that makes the mistake impossible. If no constraint is
possible, the cluster is a judgment call: write the rule once, with the why, and
stop repeating it.

## Rules

- Never commit the transcript dump. It contains prompts, paths, and user content.
- Never quote a transcript as evidence in a PR without the user's approval.
- Never turn a single annoyed message into a repository rule. Two occurrences
  minimum, or the rule is just noise with extra maintenance.
- The output is evidence, not the fix. The fix lives in the environment.
