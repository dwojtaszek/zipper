## QA Report

| #   | Test Case | App | Persona | Result | Notes |
| --- | --------- | --- | ------- | ------ | ----- |
| 1   | Standard: Basic PDF generation (10 files) | cli | operator | :white_check_mark: PASS | ZIP + DAT created, 10 PDFs, correct header |
| 2   | Loadfile-Only: Chaos mode (5%, seed 42) | cli | operator | :white_check_mark: PASS | 5 anomalies injected across 5 types, _properties.json correct |
| 3   | Production Set: Basic (50 PDFs, CASE001) | cli | operator | :white_check_mark: PASS | DATA/IMAGES/NATIVES/TEXT dirs, _manifest.json with Bates range |
| 4   | Arg Validation: Missing required args | cli | operator | :white_check_mark: PASS | Exit 1, error message with usage |
| 5   | Arg Validation: loadfile-only + target-zip-size conflict | cli | operator | :white_check_mark: PASS | Exit 1, conflict error |
| 6   | Arg Validation: chaos-mode without loadfile-only | cli | operator | :white_check_mark: PASS | Exit 1, requires --loadfile-only |
| 7   | Arg Validation: production-set without bates-prefix | cli | operator | :white_check_mark: PASS | Exit 1, requires --bates-prefix |
| 8   | Arg Validation: Path traversal (`../escape`) | cli | operator | :x: FAIL | Path accepted, files created outside CWD. PathValidator called without baseDirectory. Pre-existing gap, not a regression. |
| 9   | Standard: EML with attachments + metadata (20 files) | cli | operator | :white_check_mark: PASS | Email columns present, 20 EMLs, attachment column in header |
| 10   | Standard: Multi-format load files (dat+opt+csv) | cli | operator | :white_check_mark: PASS | All 3 formats generated, CSV comma-delimited, OPT 7-column |
| 11   | Loadfile-Only: Custom delimiters (pipe, no quotes, LF) | cli | operator | :white_check_mark: PASS | Pipe-delimited DAT, no quote chars, LF line endings in properties |
| 12   | Arg Validation: chaos-scenario + chaos-types conflict | cli | operator | :white_check_mark: PASS | Exit 1, conflict error |
| 13   | Standard: EDRM-XML format (5 PDFs) | cli | operator | :white_check_mark: PASS | Valid XML with controlNumber/filePath elements |

Result values: :white_check_mark: PASS, :x: FAIL, :no_entry: BLOCKED, :warning: FLAKY, :grey_question: INCONCLUSIVE

### Action Required

- **Test 8 (Path traversal):** `--output-path "../escape"` was accepted and files were created outside the CWD. `CliParser.cs:55` calls `PathValidator.ValidateAndCreateDirectory(pathArg)` without a `baseDirectory` argument, so traversal detection is bypassed. This is a **pre-existing gap** (not caused by the current diff which only touches test infra). Consider passing `Directory.GetCurrentDirectory()` as the base directory to enforce REQ-106.

<details>
<summary>Screenshots & Evidence</summary>

### Test 1: Basic PDF generation
```
Generation complete in 0.3 seconds.
  Archive created: .../archive_20260524_144110.zip
  Load file created: .../archive_20260524_144110.dat

DAT header: ﻿þControl NumberþþFile Pathþ
First record: þDOC00000001þþfolder_001/00000001.pdfþ
ZIP PDF count: 10
```

### Test 2: Loadfile-Only with Chaos
```
format=DAT (Metadata), records=100, chaos.enabled=True, anomalies=5
anomaly types: {'encoding', 'columns', 'mixed-delimiters', 'eol', 'quotes'}
_properties.json contains 5 injectedAnomalies with lineNumber, recordID, errorType, description
```

### Test 3: Production Set
```json
{
  "batesRange": { "start": "CASE00100000001", "end": "CASE00100000050" },
  "documentCount": 50,
  "volumeCount": 1,
  "directories": { "data": "DATA", "natives": "NATIVES", "text": "TEXT", "images": "IMAGES" },
  "loadFiles": { "dat": "DATA/loadfile.dat", "opt": "DATA/loadfile.opt" }
}
```

### Test 8: Path traversal (FAIL)
```
$ ./publish-bin/Zipper --type pdf --count 10 --output-path "../escape"
Output Path: /home/dom/Downloads/repos/escape   <-- accepted, created outside CWD
exit=0
```

### Test 9: EML with attachments + metadata
```
DAT header includes: Control Number, File Path, Custodian, Date Sent, Author, File Size, To, From, Subject, Sent Date, Attachment
ZIP EML count: 20
```

### Test 10: Multi-format load files
```
Files: .dat, .opt, .csv, .zip (+ _properties.json for each format)
CSV: ﻿Control Number,File Path / DOC00000001,folder_001/00000001.pdf
OPT: ﻿DOC00000001,VOL001,IMAGES\DOC00000001.tif,Y,,,1
```

### Test 11: Custom delimiters
```
DAT: ﻿Control Number|File Path|Custodian|Date Sent|Author|File Size|EmailSubject|...
Properties: column=char:|, quote=none, lineEnding=LF
```

### Test 13: EDRM-XML
```xml
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<documents>
  <document>
    <controlNumber>DOC00000001</controlNumber>
    <filePath>folder_001/00000001.pdf</filePath>
  </document>
</documents>
```

</details>
