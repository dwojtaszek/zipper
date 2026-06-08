# ADR-0007: Load File Composition / Serialization / Emit Seam

## Status: Accepted

## Context

The delimited load file formats (DAT, OPT, CSV, Concordance) were each produced by a fat writer (`DatWriter` was ~900 lines) that owned column composition, field escaping, streaming, and a duplicated chaos branch. A record-based seam (`LoadFileRecord` / `ILoadFileSerializer`) existed but had tests and zero production wiring — a dead seam, making ADR-0006's extensibility claim false. `ProfileDrivenDatWriter` duplicated the DAT path for the column-profile case.

## Decision

Split delimited load file generation into three deep modules with a single, realised seam:

```text
(request, processedFiles)
  → ILoadFileComposer   // column authority: header columns + lazy raw records
  → ILoadFileSerializer // render authority: record/header → one escaped line (pure)
  → LoadFileEmitter     // I/O + chaos authority: preamble, EOL, batching, chaos
```

- **Composer** decides *what columns, in what order, with what raw values*. `DatComposer` absorbs all three writer modes plus the column-profile path (retiring `ProfileDrivenDatWriter`). Values are emitted **raw**; the serializer escapes once.
- **Serializer** is **pure**: record/header → line. No stream, no EOL, no chaos.
- **Emitter** is the single I/O and chaos authority. Both paths stream lazily (O(1) auxiliary memory, no row materialization): non-chaos output goes through a buffered `StreamWriter` (BOM once); the chaos path renders each line, applies line-targeted interception, and writes cross-line encoding-anomaly bytes straight to the stream after it, preserving byte order. Chaos now runs in exactly one place.

`ILoadFileWriter` is **retained** as the format-selection seam the factory returns. It now has five real adapters (four thin composing writers + the XML carve-out), so it earns its keep rather than being a pass-through.

## Deviation: EDRM-XML keeps its own writer

`ILoadFileSerializer` is row/record-shaped; EDRM-XML is a hierarchical document tree (`Document > Files > Tags` with native/text file refs and hashes). Modelling it as a flat `LoadFileRecord` would bloat the record for one format and fail the deletion test. `XmlLoadFileWriter` therefore stays a dedicated `ILoadFileWriter` (it is already deep, format-aware, and uses no chaos).

## Consequences

- One place to add a column (composer), one place to render a format (serializer), one place chaos runs (emitter). `DatWriter`, `OptWriter`, `CsvWriter`, `ConcordanceWriter`, `ProfileDrivenDatWriter`, and `LoadFileWriterBase` are deleted.
- **Known quirk preserved for byte parity:** standard (in-archive) generation uses the platform newline, while loadfile-only / production / all chaos use the configured EOL. The emitter takes `eol` as a caller-supplied parameter rather than deriving it, so this historical inconsistency is reproduced exactly. Normalising it is a future follow-up that would change golden output.
- OPT standard mode keeps its existing behaviour of ignoring chaos and using ANSI default encoding.
- Adding a new delimited format = one composer + one serializer + one thin composing writer; no orchestrator or call-site changes.

## Closes

Closes #446, #447, #448, #450. Drops #435 (tested class removed). Implements #351 (Concordance), #350/#356 (header naming, OPT page rows) through the composers.
