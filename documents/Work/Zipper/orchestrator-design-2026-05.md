# Orchestrator Design — May 2026

## Context

All prerequisite issues are resolved:
- #259: Chaos wiring correctness ✅
- #260: Load File escaping correctness ✅
- #261: LoadFileRecord + ILoadFileSerializer seam ✅
- #262: Unified IColumnValueGenerator ✅
- #263: RequiresSequentialProcessing removed, EmlGenerationService retired ✅
- #274: ProductionSetGenerator path planning split ✅

## Current Architecture (post-refactor)

Three generation modes dispatched by `Program.cs → SelectMode() → IGenerationMode → GenerationRunner.RunAsync()`:

| Mode | Adapter | Generator | Output |
|------|---------|-----------|--------|
| Standard | `StandardMode` | `ParallelFileGenerator` | ZIP + Load File(s) |
| Loadfile-Only | `LoadfileOnlyMode` | `LoadfileOnlyGenerator` | Load File + `_properties.json` |
| Production Set | `ProductionSetMode` | `ProductionSetGenerator` | Directory tree + Load Files + Manifest |

### Standard Pipeline (ParallelFileGenerator)

```
WorkChannel → N Producers (IFileGenerator.Generate) → ResultChannel → Consumer (ZipArchiveService)
```

- Bounded channels with backpressure
- Consumer writes ZIP entries + Load Files
- Deadlock protection: Task.WhenAny races consumer with producers

### Loadfile-Only Pipeline (LoadfileOnlyGenerator)

- No producers, no channels
- Direct sequential row generation via DataGenerator
- ChaosEngine applied inline

### Production Set Pipeline (ProductionSetGenerator)

- Sequential file writing (no channel pipeline)
- ProductionSetPlanner.Plan() computes paths upfront
- DAT + OPT Load Files written after all files
- Cleanup on failure

## Design Decision: No Further Unification Needed

The three pipelines serve fundamentally different I/O patterns:

1. **Standard**: streaming to a ZIP archive (requires bounded channel for backpressure)
2. **Loadfile-Only**: no file generation, just row emission (no pipeline needed)
3. **Production Set**: sequential disk writes to a directory tree (no ZIP, no streaming)

Forcing these into a single orchestrator would add abstraction without reducing complexity. The current shape is correct.

## Chaos Engine Wiring

Chaos is now correctly scoped:
- CLI validator enforces `--chaos-mode` requires `--loadfile-only` (REQ-094)
- `ChaosAnomalyTypes` catalog is the single source of truth
- Only `LoadfileOnlyGenerator` wires the Chaos Engine (correct per spec)
- Standard and Production Set modes pass `ChaosEngineBuilder.Build()` for audit purposes only

## Fate of #274

**Resolved.** PR #329 split `ProductionSetGenerator` path planning into `ProductionSetPlanner`. No further split needed.

## Follow-up Implementation Issues

None required. The orchestrator shape is stable. Future work should focus on:
- Migrating existing writers to use `ILoadFileSerializer` (incremental, per-writer)
- Adding new Load File formats via the serializer seam (one class + one factory entry)

## Conclusion

The orchestrator unification design question is answered: **the current three-mode architecture is the right shape.** No unification refactor is needed. The seam (`LoadFileRecord` + `ILoadFileSerializer`) enables format extensibility without orchestrator changes.
