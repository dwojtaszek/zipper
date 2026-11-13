# Zipper agent instructions

Read shared host rules at @/home/dom/.agents/SHARED-AGENT-RULES.md.

## Principles

1. **Think before coding:** State assumptions and verifiable success criteria. Resolve conflicting specifications before editing. Fix root causes.
2. **Simplicity first:** Build only what the task needs. Reuse existing code and tools.
3. **Surgical changes:** Preserve unrelated work, behavior, validation, and error handling. Match surrounding code.
4. **Goal-driven execution:** Run the affected checks and finish the authorized work. Report failed or unavailable checks accurately.

Lower-numbered principles take precedence. Critical rules below remain ordered by priority.

## Delivery and Git scope

- Follow the shared Git policy: create branches or worktrees as needed for authorized work and use a PR when branch protection requires it.
- A runner dispatch explicitly supplies an issue branch/worktree; use that checkout throughout the mission.
- At task start, state the delivery scope: local commit, prepared PR, or merge/deploy. A PR-only request ends with a reviewed PR; merge/deploy requests include rollout verification. Ask once only if the scope remains unclear, and retain approvals within their stated scope.
- Return implementation missions after local validation and publication. The parent runner owns remote CI waits and subsequent repair missions.
- Keep one automation owner per issue/PR. Check active branches, PRs, and runner ownership before starting interactive work; use a separate worktree for independent work. Amp schedules and event subscriptions may report progress, but must not launch competing intake, repair, publication, or merge work for runner-owned PRs without an explicit handoff.
- Interactive edits: work in a non-`issue-*` worktree (`.worktrees/<slug>`) so active runner timers cannot reset uncommitted changes; if editing the shared `main` checkout directly, pause timers first via [runner operations](docs/runner-operations.md).

## Read when relevant

- Task workflow: [workflow selection](docs/agent-workflow.md#workflow-selection). Choose one primary workflow; retain required domain checks and review gates.
- Product behavior: [Requirements.md](Requirements.md). Domain language: [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md). Usage: [README.md](README.md).
- Any implementation or test change: [testing and debugging](docs/agent-workflow.md#testing--debugging). Use real outcomes; preserve behavioral coverage. Parser/planner/sanitizer/comparison changes also require [property-based tests](docs/property-based-testing.md).
- Working an issue or preparing a PR: [per-issue workflow](docs/issue-workflow.md), [release notes](docs/issue-workflow.md#release-notes-mandate), and [review gates](docs/issue-workflow.md#adversarial-review).
- Autonomous issue selection or triage: [queue ranking, roadmap, and blockers](docs/issue-selection.md).
- Reviewing a diff: [review standards](docs/agent-review-standards.md) and [deliberate project patterns](docs/code-review-guidelines.md).
- CI/hook changes: [pipeline map](docs/cicd.md). Operating external checks: [CI.md](CI.md).
- Runner operations, preflight, and controlled trials: [runner operations](docs/runner-operations.md). Runner design: [.zipper-runner/README.md](.zipper-runner/README.md).
- Structural changes: [architecture](docs/architecture.md). Output-producing refactors: capture seeded golden parity before editing.
- Stuck diagnosis: [stuck report](docs/agent-workflow.md#when-stuck).

## Check output

Use existing affected gates via `./dev.sh`; follow [gate selection and hook reuse](docs/agent-workflow.md#commands) rather than duplicating full-suite runs. For long checks, retain the full log and bound displayed output:

```bash
python3 .github/scripts/run-check.py --log /tmp/zipper-check.log -- <command> <args>
```

The wrapper returns the child's status. Read a targeted failure excerpt from that log before widening the investigation. Report state changes and remaining gates; avoid repeating unchanged check tables.

## Critical Rules

**When rules conflict, Principles override Critical Rules.** Among Critical Rules, the order below determines priority — higher wins.

1. **Domain Language:** Follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) in code, comments, docs, and reviews. Canonical terminology wins over Simplicity First.

2. **Requirement IDs are IMMUTABLE.** Never renumber or rename `REQ-XXX` or `FR-XXX` IDs.

3. **Test coverage must not decrease in substance.** Replace any removed or weakened test with equal or stricter behavioral coverage; retarget existing output-asserting tests when replacing implementations behind stable interfaces (never delete test files to pass). *If fixing a pre-existing failure requires touching adjacent code, Surgical Changes wins — flag the conflict.*

4. **Documentation Sync:** CLI, Load File, Audit File, Production Set, or Email domain changes must update `README.md`, `Requirements.md`, `UBIQUITOUS_LANGUAGE.md` (if terms change), and both `.sh`/`.bat` E2E scripts, verifying affected `REQ-XXX` lines before commit. *If a trivial change triggers a 4-doc cascade, Simplicity First wins — flag which docs are out of sync.*

5. **Architecture invariants:** [docs/architecture.md](docs/architecture.md) diagrams (`composer → serializer → emitter` seam and EDRM-XML carve-out; ADR-0006, ADR-0007) are the source of truth. Any deviation requires explicit human approval before merge and a same-PR diagram update.

6. **Output parity for refactors:** Before refactoring Load File, Audit File, or Production Set output paths, capture a local gitignored seeded golden content-hash manifest and verify byte-for-byte parity after each step. Preserve historical output quirks inline and file follow-ups instead.

---
