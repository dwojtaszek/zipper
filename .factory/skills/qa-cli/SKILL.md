---
name: qa-cli
description: >
  QA tests for the Zipper CLI. Exercises Standard, Loadfile-Only, Chaos Engine,
  Production Set, Source-Driven Generation, Load File Formats, Archive Test
  Fixtures, Production Manifest comparison, version output, and CLI boundaries.
---

# QA CLI

## Target and interaction

Zipper is a one-shot .NET CLI, not a persistent TUI. Use the `droid-control` skill for all terminal interactions. Route a shell through its tuistory backend and `tctl` wrapper; type each real Zipper invocation into the terminal, press Enter, and inspect the actual output and files. Do not use raw tuistory commands or run test flows in a plain shell outside Droid Control.

In CI, use a unique run-scoped session name and `--cols 110 --rows 36`. Run the build and each Zipper command through that routed terminal. Prefix each Zipper launch with `env -u CI FACTORY_DISABLE_KEYRING=true` to avoid Ink CI detection. Capture terminal text after the command returns; use another run-scoped session for each distinct flow. Droid Control owns driver mechanics, Capture, and Verify.

Read `.factory/skills/qa/config.yaml` on every run. Invoke Compose only when both `video_evidence` and `droid_control.compose` are true; never invoke it otherwise.

## Build and workspace

Set `RUN_ID` before building. Build only when the CLI is affected:

```bash
dotnet publish src/Zipper.csproj -c Release \
  -o "./qa-results/$RUN_ID/publish" -p:Version=qa-test -p:InformationalVersion=qa-test
```

The binary is `./qa-results/$RUN_ID/publish/Zipper` (Linux/macOS) or `Zipper.exe` (Windows). Create each flow's output directory below `./qa-results/$RUN_ID/data/`. Do not use the existing `publish-bin/` or `results/` directories. Preserve evidence under `./qa-results/$RUN_ID/evidence/`; clean only this run's `data/` and `publish/` directories after testing.

The orchestrator chooses relevant flows from this menu based on the diff. These are not a full-suite checklist. Use one `default` persona, synthetic inputs, and real generated output. For any CLI-path diff, report at least one successful behavior flow and at least one relevant negative or boundary flow. Prefix each Test Case with `[positive]`, `[negative]`, or `[boundary]` followed by the flow name, and put concise observed evidence in Notes.

## Test flows menu

### 1. Standard Mode: Archive and DAT Load File

Run Zipper with `--type pdf --count 20 --output-path ./qa-results/$RUN_ID/data/standard`.

Verify successful exit, a non-empty Archive and DAT Load File, exactly 20 PDF Native Files, one header plus 20 records, and ASCII 20 as the DAT column delimiter. Do not assume timestamped output names.

### 2. Loadfile-Only Mode

Run `--loadfile-only --count 20 --output-path ./qa-results/$RUN_ID/data/loadfile-only`.

Verify the DAT Load File contains 20 records and the companion `_properties.json` Audit File exists. No Archive or Native Files should be generated.

### 3. Loadfile-Only Mode with Chaos Engine

Run `--loadfile-only --count 200 --chaos-mode --chaos-amount 10 --output-path ./qa-results/$RUN_ID/data/chaos`.

Verify the command succeeds, the Audit File records `chaosMode.totalAnomalies` as 10, and its injected anomaly list contains 10 records. Deliberately anomalous Load File rows are not expected to parse cleanly.

### 4. Production Set structure and volumes

Run `--production-set --count 10 --bates-prefix QA --volume-size 3 --seed 42 --output-path ./qa-results/$RUN_ID/data/production-set`.

Find the generated Production Set by its `_manifest.json`. Verify `DATA`, `NATIVES`, `IMAGES`, and `TEXT`, both DAT and OPT Load Files, the manifest, 10 generated Native Files, and four `VOL001`–`VOL004` volumes under `NATIVES`.

### 5. Bates Number sequence

Run `--type pdf --count 3 --bates-prefix CASE --seed 42 --output-path ./qa-results/$RUN_ID/data/bates`.

Verify the Load File contains `CASE00000001` through `CASE00000003` as consecutive Bates Numbers with eight padded digits.

### 6. Target Archive size

Run `--type pdf --count 100 --target-zip-size 10MB --output-path ./qa-results/$RUN_ID/data/target-size`.

Verify the Archive is within the `10MB_100_pdf` tolerance in `tests/fixtures/target-zip-size-tolerances.json` (0.0031 relative deviation) and no generated file exceeds the test's `target/count*10` padding-spread bound. Confirm success without an impossible-size error. Use the real Archive size, not the generated-file count, as evidence.

### 7. Source-Driven Generation from CSV and Directory Template

Create a small Source CSV with `ControlNumber`, `FilePath`, and `FileType` columns, covering PDF, EML, and TIFF. Run with `--input-csv <source.csv> --seed 42 --output-path ./qa-results/$RUN_ID/data/source-csv`.

Verify each Source Record produces one matching Native File and Load File row, preserving its relative File Path and File Type. For a Directory Template, create nested files with supported extensions, run with `--directory-template <path>`, and verify the nested structure is represented in the Archive. Include a sentinel in a source file and confirm source bytes were not copied into the generated Archive.

### 8. Load File Formats

Run a small Standard Mode generation for each `dat`, `opt`, `csv`, `xml`, and `concordance` value of `--load-file-format`, each with its own output directory.

Verify the expected `.dat`, `.opt`, `.csv`, `.xml`, or Concordance `.dat` file exists and contains five records. Confirm OPT has no header, CSV uses valid quoting, XML parses as EDRM-XML, and Concordance uses its expected header and delimiters.

### 9. Archive Test Fixture smoke suite

Run `--archive-test-suite smoke --seed 42 --output-path ./qa-results/$RUN_ID/data/archive-fixtures` into a new directory.

Verify the frozen smoke suite produces five flat `.zip` / `.json` Archive Test Fixture pairs, each basename matches its Fixture ID in the Expectation File, every Expectation File parses as JSON, and the `missing-eocd` Archive is unreadable by a reference ZIP reader while its sidecar remains readable.

### 10. Production Manifest comparison

Generate a five-record prior Production Set with `--production-set --count 5 --bates-prefix SUPP --output-path <prior-dir>`. Generate a second five-record supplemental Production Set with `--production-set --supplemental-production --prior-manifest <prior-manifest> --count 5 --bates-prefix SUPP --bates-start 6 --output-path <supplemental-dir>`.

Run `--compare-production-manifests <prior-manifest>,<supplemental-manifest> --comparison-mode replacement --comparison-output <report.json>`.

Verify the report parses as JSON and its summary reports five prior records, five new records, five Added records, five Removed records, and zero unchanged records.

### 11. Version output

Run `--version`. Verify exit code 0, the `Zipper vqa-test` prefix, and the repository URL `https://github.com/dwojtaszek/zipper/`.

### 12. Invalid argument rejection

Run `--type invalid --count 1 --output-path ./qa-results/$RUN_ID/data/invalid-type`.

Verify nonzero exit, a clear validation error (not an unhandled exception), and no Archive output.

## Known Failure Modes

1. **No `--help` flag.** Zipper prints usage when invoked without arguments; `--version` is the supported informational flag.
2. **Production Set requires a Bates Prefix.** Always pass `--bates-prefix`; `--production-set` conflicts with `--loadfile-only`.
3. **Chaos Engine constraints.** `--chaos-mode` is parameterless and requires `--loadfile-only`; `--chaos-amount` is a separate value.
4. **DAT delimiters are control characters.** The default column delimiter is ASCII 20 and the quote delimiter is ASCII 254. Inspect bytes or parse with a delimiter-aware script rather than relying on visible text.
5. **Target Archive size needs a count without source input.** `--target-zip-size` requires `--count` only when neither `--input-csv` nor `--directory-template` supplies Source Records. An impossible target is rejected. Compare achieved size with the scenario's committed tolerance.
6. **Source inputs define paths and types, not content.** `FilePath` and `FileType` are required in the Source CSV. Source bytes must not be copied into generated Archives.
7. **Archive Test workflow is exclusive.** Only `--archive-test-suite`, optional `--archive-test-cases`, `--seed`, and required `--output-path` are accepted. The output path must be new.
8. **Manifest comparison bypasses file generation.** Supply two or more valid manifest paths, `--comparison-mode`, and `--comparison-output`. Its three flags remain strictly validated.
9. **Keep all output paths inside the checkout.** Use the run-scoped `qa-results/$RUN_ID/data/` tree; do not write to absolute paths or shared `results/` and `publish-bin/` directories.
