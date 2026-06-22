---
name: qa-cli
description: >
  QA tests for the Zipper CLI. Tests the binary by running it with real arguments
  and verifying output files (ZIP archives, load files, production set directories).
  Covers Standard, Loadfile-Only, Production Set modes, target ZIP size, and version.
---

# QA CLI Tests

## Build

```bash
# Bake a version string into the binary (CI does this via -p:Version from git tags).
# Without this, --version defaults to "v1.0.0" which is not meaningful for QA.
dotnet publish src/Zipper.csproj -c Release -o ./publish-bin -p:Version=qa-test
```

The binary is at `./publish-bin/Zipper` (Linux/macOS) or `./publish-bin/Zipper.exe` (Windows). All test commands use `./publish-bin/Zipper` as the path; on Windows substitute `Zipper.exe`. The version string "qa-test" will appear in `--version` output, confirming the build is fresh.

## Test Flows Menu

The orchestrator picks only the flows relevant to the current diff. Each flow describes what to run and what to verify.

All commands run from the repository root. Output paths are relative directories under `./qa-work/`.

### Flow 1: Standard mode (ZIP + load file)

**What it tests:** Default generation mode produces a ZIP archive with native files and a DAT load file.

**Command:**
```bash
mkdir -p ./qa-work/standard
./publish-bin/Zipper --count 50 --type pdf --output-path ./qa-work/standard/
```

**Verify:**
- Exit code is 0
- A subdirectory is created under `./qa-work/standard/` containing the .zip and .dat files
- ZIP file is non-empty
- DAT file has a header row and 50 data rows
- DAT file uses ASCII 20 (DC4) as column delimiter

### Flow 2: Loadfile-Only mode (no archive)

**What it tests:** Loadfile-only mode generates load files directly to disk without creating native files.

**Command:**
```bash
mkdir -p ./qa-work/loadfile-only
./publish-bin/Zipper --loadfile-only --count 100 --output-path ./qa-work/loadfile-only/
```

**Verify:**
- Exit code is 0
- .dat file exists in output directory
- .opt file exists if --loadfile-format includes opt
- _properties.json audit file exists
- DAT file has 100 data rows

### Flow 3: Loadfile-Only with chaos

**What it tests:** Chaos engine injects anomalies into load file output.

**Command:**
```bash
mkdir -p ./qa-work/chaos
./publish-bin/Zipper --loadfile-only --count 200 --chaos-mode --chaos-amount 10 --output-path ./qa-work/chaos/
```

**Verify:**
- Exit code is 0
- _properties.json contains chaos anomaly records
- Number of anomalies matches the --chaos-amount value (10)
- Load file is still parseable despite anomalies

### Flow 4: Production Set mode

**What it tests:** Production set creates directory tree with NATIVES/IMAGES/DATA/TEXT subdirectories and per-volume load files.

**Command:**
```bash
mkdir -p ./qa-work/prodset
./publish-bin/Zipper --production-set --count 100 --bates-prefix BATES001 --output-path ./qa-work/prodset/
```

**Verify:**
- Exit code is 0
- Directory tree exists with NATIVES/, IMAGES/, DATA/, TEXT/ subdirectories
- Load files in DATA/ directory
- Manifest file exists
- Volume directories named VOL001, VOL002, etc. (if count > volume size)

### Flow 5: Bates numbering

**What it tests:** Bates numbers are generated with the specified prefix and appear in load files.

**Command:**
```bash
mkdir -p ./qa-work/bates
./publish-bin/Zipper --production-set --count 20 --bates-prefix CLIENT001 --output-path ./qa-work/bates/
```

**Verify:**
- Exit code is 0
- Load file contains Bates Number column
- Bates numbers start at CLIENT00100000001
- Numbers are sequential and zero-padded to 8 digits

### Flow 6: Target ZIP size

**What it tests:** In-file padding achieves target ZIP size within tolerance.

**Command:**
```bash
mkdir -p ./qa-work/target-size
./publish-bin/Zipper --count 10 --type pdf --target-zip-size 1MB --output-path ./qa-work/target-size/
```

**Verify:**
- Exit code is 0
- ZIP file size is within +/- 10% of 1MB
- No impossible-size error (since 1MB is achievable with 10 files)

### Flow 7: Version output

**What it tests:** Version output works correctly and reflects the baked-in build version. Note: Zipper does not have a `--help` flag.

**Command:**
```bash
./publish-bin/Zipper --version
```

**Verify:**
- --version exits with code 0
- Output contains "Zipper v" prefix
- Version string matches the `-p:Version` value passed during build (e.g., "qa-test" if built with `-p:Version=qa-test`)
- Output includes the GitHub URL "https://github.com/dwojtaszek/zipper/"

### Flow 8: Email generation

**What it tests:** EML file type generates valid email files with metadata.

**Command:**
```bash
mkdir -p ./qa-work/email
./publish-bin/Zipper --count 10 --type eml --output-path ./qa-work/email/
```

**Verify:**
- Exit code is 0
- ZIP contains .eml files
- Load file includes email-specific columns (To, From, Subject, Sent Date)

### Flow 9: Multiple load file formats

**What it tests:** CSV and XML load file formats are generated correctly.

**Command:**
```bash
mkdir -p ./qa-work/csv ./qa-work/xml
./publish-bin/Zipper --count 20 --type pdf --loadfile-format csv --output-path ./qa-work/csv/
./publish-bin/Zipper --count 20 --type pdf --loadfile-format xml --output-path ./qa-work/xml/
```

**Verify:**
- CSV load file uses comma delimiter with proper quoting
- XML load file is valid XML with EDRM structure

## Negative Tests

### N1: Invalid file type

```bash
mkdir -p ./qa-work/neg-invalid
./publish-bin/Zipper --count 10 --type invalid --output-path ./qa-work/neg-invalid/
```

**Verify:** Non-zero exit code, error message about invalid file type.

### N2: Impossible target ZIP size

```bash
mkdir -p ./qa-work/neg-impossible
./publish-bin/Zipper --count 10000 --type pdf --target-zip-size 1KB --output-path ./qa-work/neg-impossible/
```

**Verify:** Non-zero exit code, error message about minimum compressed size exceeding target.

### N3: Missing required count argument

```bash
mkdir -p ./qa-work/neg-no-count
./publish-bin/Zipper --type pdf --output-path ./qa-work/neg-no-count/
```

**Verify:** Non-zero exit code, error message about missing --count.

### N4: Target ZIP size without count

```bash
mkdir -p ./qa-work/neg-size-no-count
./publish-bin/Zipper --target-zip-size 10MB --output-path ./qa-work/neg-size-no-count/
```

**Verify:** Non-zero exit code, error message about --target-zip-size requiring --count.

## Cleanup

After all tests, remove generated files:

```bash
rm -rf ./qa-work
```

## Known Failure Modes

1. **Build fails due to warnings-as-errors.** The project has `TreatWarningsAsErrors=true`. If the diff introduces a warning, the build will fail. Report as BLOCKED with the specific compiler warning.
2. **Binary not found after publish.** If `dotnet publish` succeeds but the binary is not at `./publish-bin/Zipper`, check the output path. On Windows the binary is `Zipper.exe`.
3. **Large file counts are slow.** Tests with --count > 10000 may take significant time. Use smaller counts (50-200) for QA tests unless specifically testing scale.
4. **Load file delimiter is invisible.** The DAT format uses ASCII 20 (DC4) as delimiter, which is non-printable. Use `cat -v` or `hexdump` to inspect the file if visual inspection is needed.
5. **Production set volume size.** Default volume size is 5000. To test volume splitting, use a count larger than 5000 or override with `--volume-size`.
6. **Local builds show v1.0.0.** The .csproj has no `<Version>` property. CI bakes the version via `-p:Version` from git tags (e.g., v1.8.231). Local builds without `-p:Version` default to "1.0.0". Always pass `-p:Version=qa-test` (or similar) during the build step so `--version` output is meaningful.
7. **Path traversal protection.** The CLI rejects output paths outside the current working directory. All output paths must be relative (e.g., `./qa-work/standard/`), not absolute paths like `/tmp/qa-test.zip`.
8. **Production set requires Bates prefix.** The `--production-set` flag requires `--bates-prefix`. Without it, the CLI exits with code 1.
9. **No --help flag.** Zipper does not have a `--help` argument. It treats `--help` as an unknown value and then errors on missing required arguments. Only `--version` is supported for info output.
10. **--chaos-mode is parameterless.** The `--chaos-mode` flag takes no value. It simply enables chaos mode. Use `--chaos-amount N` to control the number of anomalies. Do not pass a value after `--chaos-mode` (e.g., `--chaos-mode random` is wrong; `random` becomes an unconsumed argument).
11. **--output-path creates a directory, not a file.** For Standard mode, the CLI creates a subdirectory under the output path and places the .zip and .dat files inside it. Always pass a directory path, not a `.zip` filename.
