# ADR-0004: Unified Column Value Generation

Date: 2026-05-10
Status: Accepted
Decides: How column values are generated in load files

## Context

Zipper had two parallel code paths for generating metadata column values:

1. **Legacy path** (`MetadataRowBuilder`, 142 lines, 27 public methods) — used when `--with-metadata` or EML files are in play. Hard-coded column logic: folder-number-based custodians, random date/author generation, email field extraction from `FileData.Email`.

2. **Profile path** (`DataGenerator`, 411 lines) — used when `--column-profile` is active. Evaluates `ColumnProfile` JSON: column types (identifier, text, date, number, boolean, coded, email), distribution patterns, data sources, multi-value fields.

These two paths produced different values for the same domain concept (e.g., `CUSTODIAN`) depending on an implicit path choice hidden inside the load file writer. Adding a new column type or changing generation behavior required updating both independently.

## Decision

Retire `MetadataRowBuilder` in favour of a unified registry of `IColumnValueGenerator` implementations, consumed by a slimmed-down `DataGenerator` orchestrator.

### Interface

```csharp
internal interface IColumnValueGenerator
{
    string Generate(ColumnGenerationContext context);
}

internal sealed record ColumnGenerationContext
{
    public required long NativeFileIndex { get; init; }
    public required int FolderNumber { get; init; }
    public required int DocumentIndex { get; init; }
    public required Random Seeded { get; init; }
    public required DateTime Now { get; init; }
    public FileData? FileData { get; init; }
    public ColumnDefinition? ProfileColumn { get; init; }
}
```

### Generator taxonomy

| Category | Column types | Examples |
|---|---|---|
| Profile-driven kinds | identifier, text, longtext, date, datetime, number, boolean, coded, email | `IdentifierGenerator`, `DateGenerator` |
| Legacy metadata kinds | folderCustodian, indexCustodian, legacyDateSent, legacyAuthor, fileDataSize, randomFileSize | `LegacyFolderCustodianGenerator`, `LegacyDateSentGenerator` |
| EML column kinds | emailTo, emailFrom, emailSubject, emailSentDate, emailAttachment | `LegacyEmailToGenerator` (reads `fileData.Email`) |
| Synthetic email kinds | syntheticEmailTo/From/Subject/SentDate | index-based fallbacks for loadfile-only mode |

### Built-in pseudo-profiles

Two built-in `ColumnProfile` instances activate the legacy generators via the same `DataGenerator` path:

- **`BuiltInProfiles.LegacyWithMetadata`**: 4 columns (CUSTODIAN, DATESENT, AUTHOR, FILESIZE) for `--with-metadata` on non-EML files.
- **`BuiltInProfiles.LegacyEml`**: 9 columns (4 + 5 email columns) for EML files.

`DatWriter` determines the effective profile internally: if `ShouldIncludeMetadataColumns` is true and no user-specified profile exists, it activates the appropriate built-in profile.

## Consequences

- `src/MetadataRowBuilder.cs` is deleted. All its methods are replaced by registered generators.
- `DataGenerator` becomes a thin orchestrator (≤ 150 logical LOC) that creates per-column generators during initialization and routes each row through them.
- Every Column Kind has exactly one registered generator in `ColumnValueGeneratorRegistry.KnownTypes`.
- `LoadFileWriterBase.GenerateMetadataValues` and `GenerateEmlValues` are simplified to inline logic (no `MetadataRowBuilder` dependency).
- `AppendField` and `SanitizeField` move from `MetadataRowBuilder` to `LoadFileWriterBase` as `internal static` utilities.

## What this ADR forbids

- Re-introducing `MetadataRowBuilder`-shaped per-mode value generators that bypass `IColumnValueGenerator`.
- Adding a new column semantic without registering a corresponding generator in `ColumnValueGeneratorRegistry.KnownTypes`.
- Calling column value generation directly from writers without going through `DataGenerator` or an `IColumnValueGenerator`.
