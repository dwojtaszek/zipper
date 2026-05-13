# ADR-0006: Three-Mode Pipeline Architecture

## Status: Accepted

## Context

The tool has three generation modes with fundamentally different I/O patterns. A proposal to unify them into a single orchestrator was evaluated (#266).

## Decision

The three-mode architecture is the correct shape. No unification needed.

| Mode | Pipeline | Rationale |
|------|----------|-----------|
| Standard | Bounded channel producer/consumer | Streaming to ZIP requires backpressure |
| Loadfile-Only | Direct sequential emission | No file generation, just rows |
| Production Set | Sequential disk writes with upfront planning | Directory tree, no ZIP |

`IFileGenerator.RequiresSequentialProcessing` is removed and stays removed. All file types run in parallel in Standard mode.

## Consequences

- Do not add a unified orchestrator abstraction
- Adding a new generation mode = one `IGenerationMode` adapter + one generator class
- `LoadFileRecord` + `ILoadFileSerializer` enables format extensibility without orchestrator changes
- Chaos Engine is correctly scoped to Loadfile-Only mode only (REQ-094)
