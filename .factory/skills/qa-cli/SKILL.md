---
name: qa-cli
description: >
  QA tests for the Zipper CLI app. Runs the published binary with various
  argument combinations and verifies output files (ZIP archives, Load Files,
  audit files, manifests). Covers all three generation modes, argument
  validation, and error handling.
---

# QA Tests: Zipper CLI

**Test tool:** CLI command execution + output file verification (not tuistory -- Zipper is a batch tool, not interactive TUI).

**Testing Target:** Always test against the branch's published binary.

1. Build from the checked-out branch code: `dotnet publish src/Zipper.csproj -c Release -o publish-bin`
2. The binary is at `publish-bin/Zipper` (Linux/macOS) or `publish-bin/Zipper.exe` (Windows)
3. If the build fails, report ALL tests as BLOCKED: "Binary build failed -- cannot test."

**CRITICAL:** Do NOT fall back to a globally installed Zipper or a binary from a different branch. The QA must test the PR's actual code.

## Pre-flight Setup

```bash
# Build the binary
dotnet publish src/Zipper.csproj -c Release -o publish-bin

# Verify it runs
./publish-bin/Zipper --chaos-list 2>&1 | head -n 5

# Create output directory
RUN_ID="qa-$(date +%Y%m%d-%H%M%S)"
mkdir -p "./qa-results/$RUN_ID"
OUT="./qa-results/$RUN_ID"
```

Set `ZIPPER_CLI=./publish-bin/Zipper` for all commands below.

## Available Test Flows

### Flow 1: Standard Archive Generation

**When to run:** Changes to `ParallelFileGenerator`, `ZipArchiveService`, `StandardMode`, `Program.cs`, file generators (`EmlFileGenerator`, `TiffFileGenerator`, `OfficeFileGenerator`, `PlaceholderFileGenerator`), `LoadFiles/` writers, `Distributions.cs`, `PerformanceMonitor`.

**Test cases:**

1. **Basic PDF generation** -- `$CLI --type pdf --count 10 --output-path $OUT/basic-pdf`
   - Verify: .zip exists, .dat exists, .dat has 11 lines (1 header + 10 records), .zip contains 10 .pdf files
   - Check header: `Control Number` and `File Path` columns present

2. **JPG with encoding** -- `$CLI --type jpg --count 10 --output-path $OUT/jpg-utf16 --encoding UTF-16`
   - Verify: .dat is UTF-16LE encoded, records decode correctly

3. **TIFF with folders** -- `$CLI --type tiff --count 20 --output-path $OUT/tiff-folders --folders 5 --distribution proportional`
   - Verify: ZIP contains files in 5 folder paths, .dat has 21 lines

4. **Gaussian distribution** -- `$CLI --type pdf --count 20 --output-path $OUT/pdf-gauss --folders 5 --distribution gaussian`
   - Verify: middle folders have more files than edge folders

5. **With metadata** -- `$CLI --type pdf --count 10 --output-path $OUT/pdf-meta --with-metadata`
   - Verify: .dat header includes `Custodian`, `Date Sent`, `Author`, `File Size`

6. **With text** -- `$CLI --type pdf --count 10 --output-path $OUT/pdf-text --with-text`
   - Verify: .dat header includes `Extracted Text`, .zip contains .txt files

7. **With metadata + text** -- `$CLI --type pdf --count 10 --output-path $OUT/pdf-both --with-metadata --with-text`
   - Verify: both metadata and text columns present

8. **Include load file in ZIP** -- `$CLI --type pdf --count 10 --output-path $OUT/pdf-incl --include-load-file`
   - Verify: no separate .dat file, .dat inside .zip

9. **Multiple load file formats** -- `$CLI --type pdf --count 10 --output-path $OUT/pdf-multi --load-file-formats dat,opt,csv`
   - Verify: .dat, .opt, and .csv all exist

10. **Target zip size** -- `$CLI --type pdf --count 10 --output-path $OUT/pdf-target --target-zip-size 1MB`
    - Verify: .zip size is within +/-10% of 1MB

11. **EML with attachments** -- `$CLI --type eml --count 20 --output-path $OUT/eml-attach --attachment-rate 50`
    - Verify: .dat has email columns (To, From, Subject, Sent Date, Attachment), some attachments present

12. **Bates numbering** -- `$CLI --type docx --count 10 --output-path $OUT/docx-bates --bates-prefix CLIENT001 --bates-start 1 --bates-digits 8`
    - Verify: .dat has `Bates Number` column, values like `CLIENT00100000001`

13. **Multipage TIFF** -- `$CLI --type tiff --count 10 --output-path $OUT/tiff-mp --tiff-pages 1-5`
    - Verify: .dat has `Page Count` column, values between 1-5

14. **Column profile** -- `$CLI --type pdf --count 10 --output-path $OUT/pdf-profile --column-profile standard`
    - Verify: .dat has 24 columns from standard profile

15. **With families** -- `$CLI --type eml --count 20 --output-path $OUT/eml-fam --attachment-rate 30 --with-families`
    - Verify: .dat has `BEGATTACH`, `ENDATTACH`, `PARENT_DOCID` columns

### Flow 2: Loadfile-Only Generation

**When to run:** Changes to `LoadfileOnlyGenerator`, `LoadfileOnlyMode`, `ChaosEngine`, `ChaosEngineBuilder`, `ChaosScenario`, `ChaosAnomalyTypes`, `LoadfileAuditWriter`, `CliValidator` (loadfile-only paths), delimiter handling.

**Test cases:**

1. **Basic DAT loadfile-only** -- `$CLI --loadfile-only --count 100 --output-path $OUT/lo-basic`
   - Verify: .dat exists with 101 lines, `_properties.json` exists, no .zip file
   - Check `_properties.json`: `format` is "DAT", `totalRecords` is 100

2. **OPT loadfile-only** -- `$CLI --loadfile-only --loadfile-format opt --count 50 --output-path $OUT/lo-opt`
   - Verify: .opt exists, comma-delimited 7-column format, no header row
   - Check `_properties.json`: `format` mentions OPT

3. **Custom delimiters** -- `$CLI --loadfile-only --count 50 --output-path $OUT/lo-delim --col-delim "char:|" --quote-delim none --eol LF`
   - Verify: .dat uses `|` as column delimiter, no quote chars, LF line endings

4. **Chaos mode basic** -- `$CLI --loadfile-only --count 1000 --output-path $OUT/lo-chaos --chaos-mode --chaos-amount "5%" --seed 42`
   - Verify: `_properties.json` has `chaosMode.enabled: true`, `totalAnomalies > 0`, `injectedAnomalies` array non-empty
   - Check anomaly fields: `lineNumber`, `recordID`, `errorType`, `description` present

5. **Chaos with type filter** -- `$CLI --loadfile-only --count 500 --output-path $OUT/lo-chaos-filter --chaos-mode --chaos-amount 10 --chaos-types "quotes,columns"`
   - Verify: `injectedAnomalies` only contains `quotes` and `columns` error types

6. **Chaos scenario** -- `$CLI --loadfile-only --count 1000 --output-path $OUT/lo-scenario --chaos-mode --chaos-scenario structured-import-failures --seed 42`
   - Verify: `_properties.json` shows correct anomaly types for scenario

7. **OPT chaos** -- `$CLI --loadfile-only --loadfile-format opt --count 500 --output-path $OUT/lo-opt-chaos --chaos-mode --chaos-types "opt-boundary,opt-pagecount"`
   - Verify: OPT anomalies in `_properties.json`

8. **ANSI encoding** -- `$CLI --loadfile-only --count 50 --output-path $OUT/lo-ansi --encoding ANSI`
   - Verify: `_properties.json` shows `encoding: ANSI`

9. **Reproducibility with seed** -- Run twice with same seed, verify identical output:
   - `$CLI --loadfile-only --count 100 --output-path $OUT/lo-seed1 --seed 999`
   - `$CLI --loadfile-only --count 100 --output-path $OUT/lo-seed2 --seed 999`
   - Verify: .dat files are byte-identical (use `diff` or `md5sum`)

### Flow 3: Production Set Generation

**When to run:** Changes to `ProductionSetGenerator`, `ProductionSetMode`, `ProductionSetPlanner`, `ProductionManifestWriter`, `BatesNumberGenerator`, `LoadFiles/OptWriter`.

**Test cases:**

1. **Basic production set** -- `$CLI --production-set --type pdf --count 100 --output-path $OUT/ps-basic --bates-prefix CASE001`
   - Verify: directory structure `DATA/`, `IMAGES/`, `NATIVES/`, `TEXT/` exists
   - Verify: `DATA/` contains .dat and .opt files
   - Verify: `_manifest.json` exists with volume info and Bates range

2. **Production with volume size** -- `$CLI --production-set --type pdf --count 100 --output-path $OUT/ps-vol --bates-prefix PROD --volume-size 50`
   - Verify: multiple volumes (VOL001, VOL002), each with <=50 files
   - Verify: `_manifest.json` lists volumes with correct Bates ranges

3. **Production zip** -- `$CLI --production-set --type pdf --count 50 --output-path $OUT/ps-zip --bates-prefix ZIP --production-zip`
   - Verify: .zip file contains the full directory structure

4. **Production set with metadata** -- `$CLI --production-set --type tiff --count 50 --output-path $OUT/ps-meta --bates-prefix IMG --tiff-pages 1-3 --with-metadata`
   - Verify: OPT file has correct document breaks and page counts
   - Verify: .dat has metadata columns

### Flow 4: Benchmark Mode

**When to run:** Changes to `PerformanceBenchmarkRunner`, `PerformanceConstants`, `PerformanceMonitor`.

**Test cases:**

1. **Benchmark runs successfully** -- `$CLI --benchmark`
   - Verify: exits with code 0, outputs performance metrics to stdout
   - Verify: no errors in output

### Flow 5: Argument Validation & Conflicts

**When to run:** Changes to `CliValidator`, `CliParser`, `RequestBuilder`, `CliOptions`, `Pipeline`, argument interaction rules.

**Test cases:**

1. **Missing required args** -- `$CLI` (no args)
   - Verify: exits with code 1, error message about missing args

2. **loadfile-only + target-zip-size conflict** -- `$CLI --loadfile-only --count 10 --output-path $OUT/bad1 --target-zip-size 1MB`
   - Verify: exits with code 1, error about conflict

3. **loadfile-only + include-load-file conflict** -- `$CLI --loadfile-only --count 10 --output-path $OUT/bad2 --include-load-file`
   - Verify: exits with code 1, error about conflict

4. **chaos-mode without loadfile-only** -- `$CLI --type pdf --count 10 --output-path $OUT/bad3 --chaos-mode`
   - Verify: exits with code 1, error about --loadfile-only required

5. **production-set without bates-prefix** -- `$CLI --production-set --count 10 --output-path $OUT/bad4`
   - Verify: exits with code 1, error about --bates-prefix required

6. **chaos-scenario + chaos-types conflict** -- `$CLI --loadfile-only --count 10 --output-path $OUT/bad5 --chaos-mode --chaos-scenario full-chaos --chaos-types quotes`
   - Verify: exits with code 1, error about conflict

7. **Path traversal rejection** -- `$CLI --type pdf --count 10 --output-path "../escape"`
   - Verify: exits with code 1, error about path traversal

8. **Invalid file type** -- `$CLI --type xyz --count 10 --output-path $OUT/bad7`
   - Verify: exits with code 1, error about invalid type

9. **Folders out of range** -- `$CLI --type pdf --count 10 --output-path $OUT/bad8 --folders 0`
   - Verify: exits with code 1, error about range

10. **target-zip-size impossible** -- `$CLI --type pdf --count 1 --output-path $OUT/bad9 --target-zip-size 1KB`
    - Verify: exits with code 1, error about minimum size

11. **chaos-list flag** -- `$CLI --chaos-list`
    - Verify: exits with code 0, prints scenario list

### Flow 6: Output File Integrity

**When to run:** Changes to `LoadFiles/` writers (`DatWriter`, `OptWriter`, `CsvWriter`, `XmlLoadFileWriter`, `ConcordanceWriter`), `EncodingHelper`, `ContentTypeHelper`, `FieldNamingConvention` handling.

**Test cases:**

1. **DAT delimiter correctness** -- `$CLI --type pdf --count 5 --output-path $OUT/int-d --load-file-format dat`
   - Verify: ASCII 20 (DC4) as column delimiter, ASCII 254 (thorn) as quote char

2. **CSV RFC 4180 compliance** -- `$CLI --type pdf --count 5 --output-path $OUT/int-csv --load-file-format csv`
   - Verify: comma delimiter, double-quote escaping

3. **EDRM-XML well-formedness** -- `$CLI --type pdf --count 5 --output-path $OUT/int-xml --load-file-format edrm-xml`
   - Verify: valid XML with Root/Batch/Document structure

4. **OPT 7-column format** -- `$CLI --type tiff --count 5 --output-path $OUT/int-opt --load-file-format opt --tiff-pages 1-3`
   - Verify: exactly 7 comma-separated columns, document break markers

5. **ANSI encoding output** -- `$CLI --type pdf --count 5 --output-path $OUT/int-ansi --encoding ANSI`
   - Verify: .dat is Windows-1252 encoded

6. **Field naming convention** -- `$CLI --type pdf --count 5 --output-path $OUT/int-fnc --column-profile standard --column-profile-field-naming UPPERCASE`
   - Verify: header columns are ALL CAPS

## Persona Variations

**Operator** (the only persona):** Runs all flows. No special persona variations needed since there is no auth/role system.

## Error Handling

- If the binary fails to build: BLOCKED, report build error
- If a command exits with non-zero code unexpectedly: FAIL, include full stderr
- If an output file is missing: FAIL, list what was expected vs. what was found
- If file content doesn't match expectations: FAIL, show actual vs. expected

## Known Failure Modes

1. **Build fails with NuGet lock mismatch.** Run `dotnet restore zipper.sln --locked-mode` first. If lock file is stale, try `dotnet restore` without `--locked-mode`.
2. **Large --count values slow down tests.** Use small counts (5-50) for QA tests. The E2E scripts handle larger counts.
3. **Windows path separators.** .dat File Path column uses backslashes on Windows. When testing on Linux/macOS, expect forward slashes.
4. **Target zip size tolerance.** The +/-10% tolerance may fail for very small targets. Use at least 1MB for target-zip-size tests.
5. **Chaos Engine randomness.** Without `--seed`, chaos output is non-deterministic. Always use `--seed` for reproducibility in QA.
6. **ANSI encoding on non-Windows.** The ANSI (Windows-1252) code page may produce different byte sequences on different platforms. Verify with `iconv -f WINDOWS-1252 -t UTF-8`.
7. **_properties.json filename is prefixed.** The audit file is named `<prefix>_properties.json` (e.g., `loadfile_20260524_144110_properties.json` or `archive_20260524_144110_properties.json`), not just `_properties.json`. Use `find $OUT -name '*_properties.json'` to discover it rather than a hardcoded path.
