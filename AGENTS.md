# Zipper agent instructions

Read shared host rules at @/home/dom/.agents/SHARED-AGENT-RULES.md.

## Principles

1. **Think before coding:** State assumptions and verifiable success criteria. Resolve conflicting specifications before editing. Fix root causes.
2. **Simplicity first:** Build only what the task needs. Reuse existing code and tools.
3. **Surgical changes:** Preserve unrelated work, behavior, validation, and error handling. Match surrounding code.
4. **Goal-driven execution:** Run the affected checks and finish the authorized work. Report failed or unavailable checks accurately.

Lower-numbered principles take precedence. Critical rules below remain ordered by priority.

## Delivery and Git scope

- Create branches/worktrees as needed for authorized work and use PRs when branch protection requires them. Retain approvals within their stated scope (a PR-only request ends with a reviewed PR; merge/deploy requests include rollout verification). State delivery scope once when unclear.
- Keep one automation owner per issue/PR (scheduled or event-driven agents must not launch competing intake, repair, publication, or merge work for runner-owned PRs without an explicit handoff).
- **Dispatched runner mission:** use the supplied issue branch/worktree; return after local validation and publication so the parent runner handles CI waits and repairs.
- **Interactive session:** use a non-`issue-*` worktree (`.worktrees/<slug>`) so active runner timers cannot reset uncommitted edits; check active branches, PRs, and runner locks first via [runner operations](docs/runner-operations.md).

## Read when relevant

- Task workflow & checks (`./dev.sh` / `run-check.py`): [workflow selection](docs/agent-workflow.md#workflow-selection).
- Product behavior, domain terms, CLI usage: [Requirements.md](Requirements.md), [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md), [README.md](README.md).
- Implementation or test changes: [testing and debugging](docs/agent-workflow.md#testing--debugging), [property-based tests](docs/property-based-testing.md).
- Issues, PRs, release notes, review gates: [per-issue workflow](docs/issue-workflow.md), [issue selection](docs/issue-selection.md).
- Diff reviews: [review standards](docs/agent-review-standards.md), [deliberate project patterns](docs/code-review-guidelines.md).
- CI/hooks & external checks: [pipeline map](docs/cicd.md), [CI.md](CI.md).
- Architecture & output-refactor parity: [architecture](docs/architecture.md), [runner design](.zipper-runner/README.md).

## Critical Rules

**When rules conflict, Principles override Critical Rules.** Among Critical Rules, higher priority wins.

1. **Domain Language:** Follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) in code, comments, docs, and reviews. Canonical terminology wins over Simplicity First.
2. **Requirement IDs are immutable:** Keep every `REQ-XXX` and `FR-XXX` ID unchanged.
3. **Test coverage must not decrease in substance:** Replace any removed or weakened test with equal or stricter behavioral coverage; retarget existing output-asserting tests behind stable interfaces (never delete test files to pass). *If fixing a pre-existing failure requires touching adjacent code, Surgical Changes wins — flag the conflict.*
4. **Documentation Sync:** CLI, Load File, Audit File, Production Set, or Email domain changes must update `README.md`, `Requirements.md`, `UBIQUITOUS_LANGUAGE.md` (if terms change), and both `.sh`/`.bat` E2E scripts, verifying affected `REQ-XXX` lines before commit. *If a trivial change triggers a 4-doc cascade, Simplicity First wins — flag which docs are out of sync.*
5. **Architecture invariants:** [docs/architecture.md](docs/architecture.md) diagrams (`composer → serializer → emitter` seam and EDRM-XML carve-out; ADR-0006, ADR-0007) are the source of truth. Any deviation requires explicit human approval before merge and a same-PR diagram update.
6. **Output parity for refactors:** Before refactoring Load File, Audit File, or Production Set output paths, capture a local gitignored seeded golden content-hash manifest and verify byte-for-byte parity after each step. Preserve historical output quirks inline and file follow-ups instead.
