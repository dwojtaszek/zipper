# Zipper agent instructions

Read shared host rules at @/home/dom/.agents/SHARED-AGENT-RULES.md and Tessl rules at @/home/dom/.tessl/RULES.md.

## Principles

1. **Think before coding:** State assumptions and verifiable success criteria. Resolve conflicting specifications before editing. Fix root causes.
2. **Simplicity first:** Build only what the task needs. Reuse existing code and tools.
3. **Surgical changes:** Preserve unrelated work, behavior, validation, and error handling. Match surrounding code.
4. **Goal-driven execution:** Run the affected checks and finish the authorized work. Report failed or unavailable checks accurately.

Lower-numbered principles take precedence. Critical rules below remain ordered by priority.

## Delivery and Git scope

- Interactive work follows shared host policy: commit to main unless the user requests a PR, branch, or worktree.
- A runner dispatch explicitly supplies an issue branch/worktree; use that checkout throughout the mission.
- At task start, state the delivery scope: local commit, prepared PR, or merge/deploy. A PR-only request ends with a reviewed PR; merge/deploy requests include rollout verification. Ask once only if the scope remains unclear, and retain approvals within their stated scope.
- Return implementation missions after local validation and publication. The parent runner owns remote CI waits and subsequent repair missions.
- Keep checkouts clean. Before changing the shared main checkout, prevent concurrent runner/deploy mutations without discarding active work.

## Read when relevant

- Product behavior: [Requirements.md](Requirements.md). Domain language: [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md). Usage: [README.md](README.md).
- Any implementation or test change: [testing and debugging](docs/agent-workflow.md#testing--debugging). Use real outcomes; preserve behavioral coverage. Parser/planner/sanitizer/comparison changes also require [property-based tests](docs/property-based-testing.md).
- Autonomous issue selection or triage: [queue ranking, roadmap, and blockers](docs/agent-workflow.md#issue-selection-mandatory-for-autonomous--next-issue-work).
- Working an issue or preparing a PR: [per-issue workflow](docs/agent-workflow.md#workflow-for-github-issues), [release notes](docs/agent-workflow.md#release-notes-mandate), and [review gates](docs/agent-workflow.md#adversarial-review).
- Reviewing a diff: [review standards](docs/agent-review-standards.md) and [deliberate project patterns](docs/code-review-guidelines.md).
- CI/hook changes: [pipeline map](docs/cicd.md). Operating external checks: [CI.md](CI.md).
- Runner operations, preflight, and controlled trials: [runner operations](docs/runner-operations.md). Runner design: [.zipper-runner/README.md](.zipper-runner/README.md).
- Structural changes: [architecture](docs/architecture.md). Output-producing refactors: capture seeded golden parity before editing.
- Stuck diagnosis: [stuck report](docs/agent-workflow.md#when-stuck).

## Check output

Use existing affected gates via `./dev.sh`. For long checks, retain the full log and bound displayed output:

```bash
python3 .github/scripts/run-check.py --log /tmp/zipper-check.log -- <command> <args>
```

The wrapper returns the child's status. Read a targeted failure excerpt from that log before widening the investigation. Report state changes and remaining gates; avoid repeating unchanged check tables.

## Critical Rules

**When rules conflict, Principles override Critical Rules.** Among Critical Rules, the order below determines priority — higher wins.

1. **Domain Language:** Read and follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) for all code, comments, documentation, and reviews. Grep the file before writing docs/PRs; flag non-canonical terms in review. *Canonical terminology is non-negotiable — even when a non-canonical term seems simpler, Domain Language wins over Simplicity First.*

2. **Requirement IDs are IMMUTABLE.** REQ-XXX and FR-XXX numbers must NEVER be changed or renumbered.

3. **Test coverage must not decrease in substance.** Every removed or modified test must be replaced with a test that covers the same or a stricter behavioral contract. If no behavioral contract was being tested (e.g., a test that only asserted line execution), a behavior-coverage replacement is required. Never remove test files to make a test run green. When replacing an implementation behind a stable interface, **retarget** the existing output-asserting tests onto the replacement (swap the construction, e.g. `new OldWriter()` → `new NewWriter()`) so they keep guarding the same contract instead of being deleted. *When this conflicts with Surgical Changes (e.g., fixing a pre-existing failing test requires touching adjacent code), Surgical Changes wins — but flag the conflict.*

4. **Documentation Sync:** Any change to CLI behavior, Load File/Audit File/Production Set formats, or Email domain names must update **all** of:
   1. `README.md` — Arguments Quick Reference, Argument Interactions, examples
   2. `Requirements.md` — add or revise requirements (never renumber)
   3. `UBIQUITOUS_LANGUAGE.md` — if domain terms change
   4. E2E scripts — both `.sh` and `.bat` for new coverage

Verify behavior changes against Requirements.md before committing. Run `grep -n "REQ-XXX" Requirements.md` for each affected requirement. *When this conflicts with Simplicity First (e.g., a trivial code change triggers a 4-doc update cascade), Simplicity First wins — but flag the conflict and note which docs are out of sync.*

5. **Architecture invariants:** [docs/architecture.md](docs/architecture.md) diagrams are the source of truth for system structure — notably the load-file `composer → serializer → emitter` seam and the EDRM-XML carve-out. Any change that deviates from them, or makes a diagram inaccurate, requires **explicit human approval** and a same-PR diagram update. AI agents must stop and ask the maintainer (e.g. via AskUserQuestion) before merging such a change. Rationale: ADR-0006, ADR-0007.

6. **Output parity for refactors:** Before refactoring code that produces Load File / Audit File / Production Set output, capture a **seeded golden baseline** of representative scenarios as a content-hash manifest (timestamp/filename-independent), then diff it after **every** step. Byte-for-byte parity is what makes it safe to delete or restructure writers. Keep the harness local and gitignored. *Preserve historical output quirks (Line Ending, Encoding, path separators) exactly — file a follow-up issue rather than "fixing" them inline, since that changes bytes and breaks parity.*

---
