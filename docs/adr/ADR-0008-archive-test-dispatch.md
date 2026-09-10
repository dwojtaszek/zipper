# ADR-0008: Archive Test Fixture CLI Short-Circuit

## Status: Proposed

Approval is tracked in [#834](https://github.com/dwojtaszek/zipper/issues/834): the maintainer gate required by AGENTS.md (architecture invariants) is satisfied by the recorded directive to implement the Archive Test Fixture slices. This ADR is proposed in slice 01 (#835); the runtime dispatch and the `docs/architecture.md` mode-dispatch diagram update land together in slice 10 (#844), at which point the status becomes Accepted.

## Context

Archive Test Fixtures (see [docs/archive-test-suites.md](../archive-test-suites.md)) need CLI exposure, but they are not a generation mode: they produce no Load File dataset, no Native Files, and no user-facing Archive. Their output is folder-published fixture pairs with paired unique IDs. Adding a fourth `IGenerationMode` or extending `FileGenerationRequest` would force every existing mode and request consumer to carry state that is irrelevant to them.

## Decision

Expose the Archive Test workflow as a dedicated Program short-circuit, dispatched before `Pipeline.Build`, analogous to the comparison module short-circuit (`src/Program.cs`: `modules.Comparison.TryBuild` runs before request building). The typed request lives under `src/ArchiveTests/`.

- It is **not** a fourth `IGenerationMode`; `StandardMode`, `LoadFileOnlyMode`, and `ProductionSetMode` are untouched.
- `FileGenerationRequest` is not extended; the Archive Test workflow carries its own typed request.
- The mode-dispatch diagram in `docs/architecture.md` gains one pre-pipeline short-circuit, updated in the same PR as the runtime code (slice 10, #844).

## Consequences

- `Pipeline.Build` never runs for Archive Test invocations, so generation cross-cutting rules do not apply to them; instead the Archive Test module rejects any flag combination outside `--archive-test-suite`, `--archive-test-cases`, `--output-path`, and `--seed`.
- The comparison short-circuit remains the precedent: Program-level dispatch is reserved for workflows whose output is not an Archive/Load File dataset.
- Adding a future non-generation workflow should follow the same short-circuit shape rather than growing the mode enum.
