# Zipper Architecture

> **For AI agents:** this file is auto-discovered via [AGENTS.md](../AGENTS.md). The diagrams below are the **source of truth** for Zipper's structure — see [Architecture Invariants](#architecture-invariants-human-approval-required) before making structural changes.

## Architecture Invariants (human approval required)

The diagrams in this file are a **contract**, not just documentation:

- **The load-file pipeline is `composer → serializer → emitter`.** Column decisions live in a **Composer**, line rendering in a **Serializer** (pure — no I/O), and all I/O + chaos in the **Emitter**. Do not reintroduce a "fat writer" that owns more than one of these responsibilities.
- **EDRM-XML is the only carve-out.** It keeps its own `ILoadFileWriter` because it is a hierarchical document tree, not a flat record. Do not force other formats out of the seam, and do not fold XML into it.
- **`ILoadFileWriter` is the format-selection seam** the factory returns; the **Chaos Engine** runs in exactly one place (the emitter), scoped to Loadfile-Only mode (REQ-094).

**Any deviation from these invariants — or any change that makes a diagram inaccurate — requires explicit human approval, plus a same-PR update to the affected diagram.** AI agents: stop and ask the maintainer (e.g. via the AskUserQuestion tool) before merging such a change. See the **Architecture** checklist in the PR template. Decision rationale is recorded in [ADR-0006](adr/ADR-0006-three-mode-pipeline.md) and [ADR-0007](adr/ADR-0007-loadfile-composition-seam.md). See also [ADR-0004](adr/ADR-0004-unified-column-generation.md) (unified column value generation via `IColumnValueGenerator`) and [ADR-0005](adr/ADR-0005-email-aggregate.md) (Email value object + `EmailFactory` as sole constructor).

## Three Generation Modes

`Program.cs` uses `SelectMode(request)` → `IGenerationMode` → `GenerationRunner.RunAsync()` to dispatch to one of three strategies:

| Mode | Trigger | Adapter | Generator |
|------|---------|---------|-----------|
| **Standard** | default | `StandardMode` | `ParallelFileGenerator.GenerateFilesAsync()` → Archive (.zip) + Load File |
| **Loadfile-Only** | `--loadfile-only` | `LoadFileOnlyMode` | `LoadFileOnlyGenerator.GenerateAsync()` → Load File + `_properties.json` audit |
| **Production Set** | `--production-set` | `ProductionSetMode` | `ProductionSetGenerator.GenerateAsync()` → Directory tree (NATIVES/IMAGES/DATA/TEXT) + Load Files |

## Standard Pipeline

`ParallelFileGenerator` uses `System.Threading.Channels` for a producer-consumer pipeline:

1. **Work channel**: Produces `FileWorkItem` objects using the configured distribution algorithm. In Source-Driven Generation (`--input-csv` / `--directory-template`), the work items instead come one-to-one from the Source Records, carrying the source-relative path, File Type, and identity overrides. Bounded channel provides backpressure.
2. **Generation**: N concurrent producers generate file data and write to result channel. All file types run in parallel. In a File Type Mix run (`--types`), each `FileWorkItem` carries its own File Type from the `FileTypePlan` and producers route to the matching per-type generator; single-type runs resolve to one generator as before.
3. **Archive writing**: Single consumer (`ZipArchiveSink`, implementing `IArchiveSink`) writes ZIP entries, then delegates Load File emission to `LoadFileOrchestrator`, which drives the composer → serializer → emitter seam (selected via `ILoadFileWriter`; see [Load File Composition Seam](#load-file-composition-seam)).
4. **Deadlock protection**: `Task.WhenAny` races consumer with producers; if consumer faults, result channel is completed with its exception to unblock producers.

## Chaos Engine (Loadfile-Only Mode only)

`ChaosEngine` uses Floyd's algorithm for O(k) exact random sampling of lines to corrupt. DAT and OPT anomaly types are cataloged in `ChaosAnomalyTypes.cs` — see source for current list. Tracked in `_properties.json` via `LoadFileAuditWriter`.

## Three-Mode Pipeline

```mermaid
graph TD
    CLI["CLI (Program.cs)"]
    CLI --> SelectMode["SelectMode(request)"]
    SelectMode -->|"default"| StandardMode["StandardMode"]
    SelectMode -->|"--loadfile-only"| LoadFileOnlyMode["LoadFileOnlyMode"]
    SelectMode -->|"--production-set"| ProductionSetMode["ProductionSetMode"]

    StandardMode --> PFG["ParallelFileGenerator"]
    PFG -->|"Work Channel"| Producers["N Concurrent Producers"]
    Producers -->|"Result Channel"| ZAS["ZipArchiveSink (Consumer)"]
    ZAS --> ZIP["ZIP Archive"]
    ZAS --> LFO["LoadFileOrchestrator<br/>(format dispatch)"]
    LFO --> LF1["Load Files + Audit Files"]

    LoadFileOnlyMode --> LOG["LoadFileOnlyGenerator"]
    LOG --> LF2["Load Files (DAT/OPT)"]
    LOG -->|"optional"| Chaos["ChaosEngine (Floyd's algorithm)"]
    Chaos --> Audit["_properties.json Audit"]

    ProductionSetMode --> PSG["ProductionSetGenerator"]
    PSG --> PSP["ProductionSetPlanner (no I/O)"]
    PSP --> Tree["Directory Tree (NATIVES/IMAGES/DATA/TEXT; ORIGINALS in source-path-mode originals)"]
    PSG --> LFO
    LFO --> LF3["Load Files + Audit Files"]
    PSG --> Manifest["Production Manifest"]
```

## Component Map

```mermaid
graph LR
    subgraph CLI Layer
        Program["Program.cs<br/>(SelectMode dispatch)"]
        CliParser["CliParser"]
        CliValidator["CliValidator"]
        RequestBuilder["RequestBuilder"]
    end

    subgraph Config
        FGR["FileGenerationRequest"]
        FGR --> Output["OutputConfig"]
        FGR --> Metadata["MetadataConfig"]
        FGR --> LoadFile["LoadFileConfig"]
        FGR --> Delimiters["DelimiterConfig"]
        FGR --> Bates["BatesNumberConfig"]
        FGR --> Tiff["TiffConfig"]
        FGR --> Chaos["ChaosConfig"]
        FGR --> Production["ProductionConfig"]
        FGR --> Hash["HashConfig"]
        FGR --> LoadfileOnly["LoadfileOnly flag"]
    end

    subgraph Mode Adapters
        StdMode["StandardMode"]
        LFMode["LoadFileOnlyMode"]
        PSMode["ProductionSetMode"]
        PSG["ProductionSetGenerator"]
        PSMode --> PSG
    end

    subgraph File Generators
        EML["EmlFileGenerator"]
        TIFF["TiffFileGenerator"]
        Office["OfficeFileGenerator"]
        Placeholder["PlaceholderFileGenerator"]
    end

    subgraph Load File Seam
        LFO["LoadFileOrchestrator<br/>(format dispatch)"]
        Factory["LoadFileWriterFactory"]
        Composer["Composer<br/>(Dat/Opt/Csv/Concordance)"]
        Serializer["Serializer<br/>(Dat/Opt/Csv/Concordance)"]
        Emitter["LoadFileEmitter<br/>(preamble/EOL/chaos)"]
        XMLW["XmlLoadFileWriter<br/>(carve-out)"]
        LFO --> Factory
        Factory --> Composer --> Serializer --> Emitter
        Factory --> XMLW
    end

    subgraph Profiles
        Loader["ColumnProfileLoader"]
        DataGen["DataGenerator"]
        BuiltIns["BuiltInProfiles"]
    end

    subgraph Validation
        PGV["PostGenerationValidator<br/>(Standard / Loadfile-Only / Production Set)"]
        Runner["ValidatorRunner"]
        PSPV["ProductionSetPostValidator"]
        SuppV["SupplementalValidator<br/>(pre-output, supplemental mode)"]
        PGV --> Runner
        PGV --> PSPV
    end

    subgraph Manifest Comparison
        PMC["ProductionManifestComparer<br/>(--compare-production-manifests)"]
    end

    Program --> CliParser --> CliValidator --> RequestBuilder --> FGR
    Program -->|"--compare-production-manifests"| PMC
    Program -->|"SelectMode(request)"| StdMode
    Program -->|"SelectMode(request)"| LFMode
    Program -->|"SelectMode(request)"| PSMode
    StdMode --> PGV
    LFMode --> PGV
    PSMode --> PGV
    PSG --> SuppV
    FGR --> File Generators
    FGR --> Load File Seam
    Profiles --> DataGen
```

## Post-Generation Validation

Each mode adapter runs `PostGenerationValidator.Validate(ValidationContext)` after its generator completes:

- **Standard / Loadfile-Only / Production Set**: `StandardMode`, `LoadFileOnlyMode`, and `ProductionSetMode` each construct `PostGenerationValidator`, which drives `ValidatorRunner` over the emitted Load File(s). For Production Set mode it additionally calls `ProductionSetPostValidator.Validate`.
- **Supplemental**: `SupplementalValidator.ValidateAsync` runs during supplemental Production Set generation (before output is written) to validate Bates Number ranges against prior Production Manifests.
- **Manifest Comparison**: `--compare-production-manifests` short-circuits normal generation in `Program.cs` and dispatches directly to `ProductionManifestComparer.CompareAndReportAsync`, which writes the comparison JSON report. This path bypasses `PostGenerationValidator` entirely.

## Load File Composition Seam

The four delimited formats (DAT, OPT, CSV, Concordance) are produced by three deep modules; EDRM-XML is the carve-out. `LoadFileOrchestrator` is the single owner of format dispatch for Standard and Production Set modes — for each requested format it creates the writer, opens the output stream (ZIP entry or disk file), writes, and emits the Audit File. ZipArchiveSink (Standard mode) and ProductionSetGenerator (Production Set mode) both delegate to it; Loadfile-Only Mode keeps its own loop because it applies chaos per format. See the [Architecture Invariants](#architecture-invariants-human-approval-required) — the composer/serializer/emitter shape must not be collapsed back into fat writers without human approval.

- **Composer** (`ILoadFileComposer`) — column authority: header columns + lazy `LoadFileRecord`s with raw values held as parallel arrays aligned by index (handles modes + column profiles internally).
- **Serializer** (`ILoadFileSerializer`) — render authority: record/header → one escaped line. Pure (no stream, EOL, or chaos).
- **Emitter** (`LoadFileEmitter`) — I/O + chaos authority: encoding preamble (BOM), end-of-line, batching, and the single Chaos Engine pipeline. Both paths stream lazily (O(1) auxiliary memory); the chaos path additionally intercepts each line and writes inter-line encoding-anomaly bytes straight after it.

```mermaid
graph TD
    Req["FileGenerationRequest + processedFiles"]
    Req --> LFO["LoadFileOrchestrator<br/>(format dispatch; stream target via caller)"]
    LFO --> Factory["LoadFileWriterFactory.CreateWriter(format, mode)"]
    Factory -->|"DAT / OPT / CSV / Concordance"| CW["Composing Writer<br/>(thin ILoadFileWriter)"]
    Factory -->|"EDRM-XML"| XML["XmlLoadFileWriter<br/>(carve-out: hierarchical tree)"]

    CW --> Comp["Composer — column authority<br/>header columns + lazy raw records"]
    Comp --> Rec["LoadFileRecord<br/>(columns + raw values,<br/>parallel arrays aligned by index)"]
    Rec --> Ser["Serializer — render authority<br/>record/header → one escaped line (pure)"]
    Ser --> Emit["LoadFileEmitter — I/O + chaos authority<br/>preamble (BOM), EOL, batching"]

    Emit -->|"no chaos"| Stream["stream lines<br/>(buffered StreamWriter)"]
    Emit -->|"chaos (Loadfile-Only)"| ChaosP["stream line → intercept<br/>+ inter-line anomaly bytes"]

    Stream --> Out["Load File (on disk / in ZIP)"]
    ChaosP --> Out
    XML --> Out
```
