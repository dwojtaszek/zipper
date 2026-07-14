# Design: Consolidate Post-Generation Validator (#568)

## Problem

Post-generation validation works but is architecturally scattered:
- Each mode adapter (`StandardMode`, `LoadFileOnlyMode`, `ProductionSetMode`) duplicates `ValidateGeneratedLoadFile()` orchestration logic
- `ProductionSetPostValidator` is standalone, not wired through `ValidatorRunner`
- `FileGenerationResult.LoadFilePath` is singular — can't represent multiple load files from `--load-file-formats`
- No single "validate this output" entry point exists

## Approach

**Consolidate + extend:** Extract duplicated orchestration into `PostGenerationValidator`, add `ValidationContext`, extend `FileGenerationResult` with multi-load-file support, wire `ProductionSetPostValidator` through the orchestrator, and add missing test scenarios.

## Components

### 1. `ValidationContext` (new record)

```csharp
public sealed record ValidationContext
{
    public string? ArchiveFilePath { get; init; }
    public string? ProductionSetPath { get; init; }
    public IReadOnlyDictionary<string, string> LoadFiles { get; init; } // format → file path
    public IReadOnlyList<string>? ArchiveEntryPaths { get; init; }
    public FileGenerationRequest Request { get; init; }
    public bool SkipEolValidation { get; init; }
    public bool IsChaosMode { get; init; }
}
```

Mode adapters construct this from their result + request. `PostGenerationValidator` consumes it.

### 2. `PostGenerationValidator` (new class)

Location: `src/Validation/PostGenerationValidator.cs`

Single public method:

```csharp
public sealed class PostGenerationValidator
{
    public ValidationResult Validate(ValidationContext context)
}
```

**Orchestration flow:**
1. If `context.IsChaosMode` → return empty result (skip validation, matching existing LoadFileOnly behavior)
2. If `context.ArchiveFilePath` is set (Standard Mode):
   - Open ZIP, get entry paths
   - For each format in `context.LoadFiles`: read from ZIP entry or disk, call `ValidatorRunner`
   - Skip EDRM-XML format
3. If `context.ProductionSetPath` is set (Production Set):
   - Call `ProductionSetPostValidator.Validate()`, convert findings to `ValidationFinding`s
4. For non-production-set modes: validate EOL unless `context.SkipEolValidation`
5. Merge all findings into single `ValidationResult`

### 3. `FileGenerationResult` changes

Add `LoadFilePaths` alongside existing `LoadFilePath`:

```csharp
public IReadOnlyDictionary<string, string> LoadFilePaths { get; set; }
    = new Dictionary<string, string>();
```

- `LoadFilePath` kept for backward compat (primary DAT path)
- `LoadFilePaths` maps format name → file path for all generated formats
- Mode adapters populate both

### 4. Mode adapter changes

Each mode adapter replaces inline `ValidateGeneratedLoadFile()` with:

```csharp
var context = new ValidationContext { /* ... */ };
var validator = new PostGenerationValidator();
var vr = validator.Validate(context);
if (vr.HasErrors)
{
    Console.Error.WriteLine(vr.GetSummary());
    throw new InvalidOperationException("Post-generation validation failed.");
}
```

**StandardMode:** Constructs context with `ArchiveFilePath`, `LoadFilePaths` from result, `ArchiveEntryPaths` from ZIP entries.

**LoadFileOnlyMode:** Constructs context with `LoadFilePaths` from result, `IsChaosMode` from request.

**ProductionSetMode:** Constructs context with `ProductionSetPath`, `LoadFilePaths` (DAT + OPT paths).

### 5. Missing test scenarios

| Test | File | What it verifies |
|------|------|-----------------|
| Valid Standard Mode Archive passes | `PostGenerationValidatorTests` | ZIP with valid DAT + natives validates clean |
| Concordance quote-wrapped records | `PostGenerationValidatorTests` | Quote-delimited fields with column delimiter don't false-positive |
| Large-file streaming | `PostGenerationValidatorTests` | 10k+ row DAT via TextReader overload, no OOM, correct findings |
| Multi-format validation | `PostGenerationValidatorTests` | `--load-file-formats dat opt csv` validates all three |
| ProductionSet orchestrator integration | `PostGenerationValidatorTests` | `PostGenerationValidator` delegates to `ProductionSetPostValidator`, merges findings |

## Files Changed

| File | Change |
|------|--------|
| `src/Validation/ValidationContext.cs` | New — context record |
| `src/Validation/PostGenerationValidator.cs` | New — orchestrator |
| `src/FileGenerationRequest.cs` | Add `LoadFilePaths` to `FileGenerationResult` |
| `src/StandardMode.cs` | Replace inline validation with `PostGenerationValidator` |
| `src/LoadFileOnlyMode.cs` | Replace inline validation with `PostGenerationValidator` |
| `src/ProductionSetMode.cs` | Replace inline validation with `PostGenerationValidator` |
| `src/Zipper.Tests/PostGenerationValidatorTests.cs` | Add 5 new test scenarios |

## Architecture Invariants

- `composer → serializer → emitter` seam unchanged
- EDRM-XML carve-out unchanged (still skipped by validator)
- `ValidatorRunner` remains the per-file validation engine; `PostGenerationValidator` orchestrates across files/modes
- `ProductionSetPostValidator` remains the production-set-specific engine; `PostGenerationValidator` delegates to it

## Risks

- `ProductionSetPostValidator` returns `ProductionSetValidationReport` (different type from `ValidationResult`) — conversion layer needed
- ZIP entry reading in Standard Mode must handle encoding correctly
- Backward compat: `LoadFilePath` must remain populated for any code that reads it
