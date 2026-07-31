# Zipper Advanced CLI & Reference Guide

This reference guide covers output directory structure visualizers, format comparison matrices, common use case recipes, custom column profile authoring, advanced flags, delimiter configurations, Chaos Engine usage, Production Set comparisons, and audit schema details.

---

## 1. Output Directory Structure Visualizers

Zipper operates in three distinct generation modes, each producing a specific output layout on disk.

### Standard Mode (`Archive + Load File`)
Generates a `.zip` file containing native files distributed across subfolders, accompanied by a root Load File (`.dat`, `.opt`, `.csv`, `.xml`, or `concordance`).

```text
<output-path>/
├── archive_20260731_120000.zip
│   ├── folder_001/
│   │   ├── 00000001.pdf
│   │   └── 00000003.pdf
│   └── folder_002/
│       ├── 00000002.pdf
│       └── 00000004.pdf
└── archive_20260731_120000.dat  (Load File)
```

### Production Set Mode (`Volume Layout`)
Generates a structured e-discovery Production Set with volume folders, single-page image renders (`.tif`), extracted text (`.txt`), native files, DAT/OPT load files, and a JSON manifest.

```text
<output-path>/
└── PRODUCTION_20260731_120000/
    ├── DATA/
    │   ├── loadfile.dat                 (Standard Production DAT)
    │   ├── loadfile.opt                 (Opticon Page-Level Image Load File)
    │   ├── loadfile_properties.json     (DAT Audit File)
    │   └── loadfile.opt_properties.json (OPT Audit File)
    ├── IMAGES/
    │   └── VOL001/
    │       ├── CLIENT00100000001.tif
    │       └── CLIENT00100000002.tif
    ├── NATIVES/
    │   └── VOL001/
    │       ├── CLIENT00100000001.pdf
    │       └── CLIENT00100000002.pdf
    ├── TEXT/                           (Present when --with-text is enabled)
    │   └── VOL001/
    │       ├── CLIENT00100000001.txt
    │       └── CLIENT00100000002.txt
    ├── _manifest.json                  (Production Set Manifest)
    └── _validation_report.json          (Self-Validation Summary)
```

### Loadfile-Only Mode (`Standalone Metadata`)
Generates metadata or image-referencing load files directly to disk without creating native files or zip archives.

```text
<output-path>/
├── loadfile_20260731_120000.dat
└── loadfile_20260731_120000_properties.json  (Audit Metadata File)
```

---

## 2. Load File Format Comparison Matrix

| Format | File Extension | Column Delimiter | Quote Delimiter | Multi-Value Separator | Image Referencing | E-Discovery Standards |
|--------|---------------|------------------|-----------------|----------------------|-------------------|----------------------|
| `dat` | `.dat` | ASCII 20 (`\x14`) | ASCII 254 (`þ`) | `;` | Document-level | Standard Metadata Export |
| `opt` | `.opt` | Comma (`,`) | None | N/A | Single/Multi-page TIFF/JPG | Page-Level Image Import |
| `csv` | `.csv` | Comma (`,`) | Double-quote (`"`) | `;` | Document-level | RFC 4180 Escaped CSV |
| `edrm-xml` | `.xml` | XML Tags | N/A | `<Value>` Elements | Full Tag & File Map | EDRM Schema v1.2 XML |
| `concordance` | `.dat` | ASCII 20 (`\x14`) | ASCII 254 (`þ`) | `;` | Document-level | Quote-wrapped Database Import |

---

## 3. Common Use Case Recipes

### Recipe A: Synthetic Production Export for Review Ingestion Testing
Generate a 50,000-document legal production with Bates prefixes `CASE2026`, extracted text, and volume directories:

```bash
zipper --production-set --bates-prefix "CASE2026" --count 50000 --output-path ./prod_export --with-text --volume-size 5000
```

### Recipe B: Ingestion Anomaly & Error Resiliency Testing
Inject 5% deliberate structural anomalies into a 100,000-record DAT Load File to test ingestion parser error handling:

```bash
zipper --loadfile-only --count 100000 --output-path ./chaos_ingest --chaos-mode --chaos-amount "5%" --seed 42
```

### Recipe C: E-Mail & Attachment Family Simulation
Generate 10,000 E-Mails with a 30% attachment rate and parent-child attachment relationship columns (`BEGATTACH`, `ENDATTACH`, `PARENTDOCID`):

```bash
zipper --type eml --count 10000 --output-path ./email_families --attachment-rate 30 --with-families --with-metadata
```

### Recipe D: Realistic Document Mix Export
Generate a multi-file-type archive (60% PDF, 20% E-Mail, 10% TIFF, 10% XLSX) with custom litigation metadata profile:

```bash
zipper --types "pdf:60,eml:20,tiff:10,xlsx:10" --count 20000 --output-path ./mixed_archive --column-profile litigation
```

---

## 4. Custom Column Profile Authoring Guide

You can define custom JSON column profiles to generate domain-specific metadata schemas with up to 200 columns. Custom profiles must be saved within your working directory.

### Example Custom Profile (`custom-profile.json`)

```json
{
  "profileName": "custom-ediscovery",
  "description": "Custom review metadata profile with tailored date ranges and custodians",
  "columns": [
    {
      "name": "CONTROL_NUMBER",
      "generatorType": "identifier",
      "generatorParams": { "prefix": "DOC", "digits": 8 }
    },
    {
      "name": "CUSTODIAN",
      "generatorType": "text",
      "generatorParams": { "source": "custodians" }
    },
    {
      "name": "DOCUMENT_DATE",
      "generatorType": "date",
      "generatorParams": { "startDate": "2020-01-01", "endDate": "2026-12-31" }
    },
    {
      "name": "CONFIDENTIALITY",
      "generatorType": "coded",
      "generatorParams": { "values": ["Public", "Confidential", "Highly Confidential", "Restricted"] }
    },
    {
      "name": "FILE_SIZE_BYTES",
      "generatorType": "number",
      "generatorParams": { "min": 1024, "max": 10485760 }
    },
    {
      "name": "REVIEW_NOTES",
      "generatorType": "longtext",
      "generatorParams": { "loremParagraphs": 2 }
    }
  ]
}
```

### Usage:
```bash
zipper --type pdf --count 1000 --output-path ./custom_output --column-profile ./custom-profile.json
```

---

## 5. Advanced CLI Options

### Loadfile-Only Mode

Generate standalone Load Files directly to disk without creating Archives or Native Files.

```bash
# Generate standalone DAT Load File
zipper --loadfile-only --count 100000 --output-path ./dat_only

# Generate standalone OPT Load File in Opticon format
zipper --loadfile-only --loadfile-format opt --count 50000 --output-path ./opt_only
```

- `--loadfile-only`: Enables standalone Load File generation. Conflicts with `--target-zip-size` and `--include-load-file`.
- `--eol <CRLF|LF|CR>`: Set line endings for Loadfile-Only and Production Set modes (defaults to `CRLF`).

---

### Delimiter Configuration & Modes

Zipper supports presets, old-style delimiter flags, and strict-prefix delimiter flags.

| Delimiter Type | Flag Example | Notes |
|----------------|--------------|-------|
| Delimiter Preset | `--dat-delimiters standard` (or `csv`) | DAT-only |
| Old-Style Flags | `--delimiter-column 20` | DAT-only |
| Strict-Prefix Flags | `--col-delim ascii:20`, `--quote-delim char:"`, `--quote-delim none` | Works in all modes; overrides presets/old-style flags |

Example custom delimiters:
```bash
zipper --loadfile-only --count 10000 --output-path ./custom_delims \
    --col-delim "char:|" --quote-delim "char:\"" --eol LF
```

---

### Column Profiles

Generate rich, configurable metadata schemas with up to 200 columns using built-in profiles or custom JSON configurations.

| Profile | Column Count | Use Case |
|---------|--------------|----------|
| `minimal` | 5 | Basic document tracking (DOCID, FILEPATH, CUSTODIAN, DATECREATED, FILESIZE) |
| `standard` | 24 | Standard e-discovery review fields |
| `litigation` | 48 | Litigation support with privilege, responsiveness, and hashes |
| `full` | 138 | Maximum metadata coverage |

Example usage:
```bash
zipper --type pdf --count 5000 --output-path ./litigation_data --column-profile litigation --seed 12345
```

---

### Chaos Engine

Inject deliberate structural anomalies into Load Files for ingestion testing. Requires `--loadfile-only` and `dat` or `opt` format.

```bash
# Inject anomalies into 5% of records
zipper --loadfile-only --count 100000 --output-path ./chaos_test \
    --chaos-mode --chaos-amount "5%" --seed 42

# Predefined chaos scenario
zipper --loadfile-only --count 100000 --output-path ./platform_test \
    --chaos-mode --chaos-scenario structured-import-failures
```

List available scenarios: `zipper --chaos-list`

---

### Production Set Comparison

Compare Production Set manifests (`_manifest.json`) across runs to audit replacements, supplementals, or reproductions.

```bash
zipper --compare-production-manifests "/path/to/prior/_manifest.json,/path/to/new/_manifest.json" \
    --comparison-mode replacement \
    --comparison-output "/path/to/report.json"
```

---

## 6. Audit & Manifest File Schemas

### `_properties.json` (Loadfile-Only Audit File)

Written alongside standalone Load Files in Loadfile-Only Mode using `camelCase` schema:

```json
{
  "fileName": "load.dat",
  "format": "DAT (Metadata)",
  "totalRecords": 200,
  "properties": {
    "encoding": "UTF-8",
    "lineEnding": "LF",
    "delimiters": {
      "column": "ascii:20",
      "quote": "ascii:254",
      "newline": "ascii:174",
      "multiValue": "none",
      "nestedValue": "none"
    }
  },
  "chaosMode": {
    "enabled": true,
    "targetAmount": "5%",
    "totalAnomalies": 10,
    "injectedAnomalies": [
      {
        "lineNumber": "14",
        "recordID": "DOC00000014",
        "column": "Column 3",
        "errorType": "quotes",
        "description": "Omitted closing character."
      }
    ]
  }
}
```

### `_manifest.json` (Production Set Manifest)

Written at the root of Production Sets. Records Bates ranges, volume layout, load file paths, document counts (`nativeFileCount`, `parentNativeFileCount`, `attachmentNativeFileCount`), and configuration parameters.

---

## 7. Argument Interactions Reference

> [!IMPORTANT]
> Some arguments have dependencies or conflicts. Review these rules when combining options.

| Interaction | Behavior |
|-------------|----------|
| `--column-profile` + `--with-metadata` | Column profile takes precedence; `--with-metadata` is ignored with a warning |
| `--column-profile` + `--production-set` | **Conflict**: `--column-profile` is not supported with `--production-set` |
| `--column-profile` + `--with-collection-metadata` | Collection metadata columns are merged into the profile; profile values take precedence with synthetic fallback |
| `--with-collection-metadata` + `--with-metadata` | Both add their own disjoint column sets; no conflict |
| `--with-collection-metadata` + Production Set | Silently ignored (no columns added) |
| `--with-collection-metadata` + `--loadfile-only` (no `--column-profile`) | Silently ignored (no columns added) |
| `--with-collection-metadata` + OPT or EDRM-XML | Silently ignored (not applicable to these formats) |
| `--target-zip-size` | Requires `--count` or source input (`--input-csv` / `--directory-template`) |
| `--types` | Mutually exclusive with `--type`, `--loadfile-only`, and `--column-profile`. Weights must be positive integers (max 1,000,000 each). When `jpg` or `tiff` participate, DAT+OPT are the default formats. Standard-mode Load Files gain a File Type column; Production Set DAT writes FILE_TYPE per record. Email Metadata columns apply only to Email records; Page Count applies only to TIFF records |
| `--input-csv` / `--directory-template` | Mutually exclusive with each other and with `--type` and `--types`. Satisfies the `--type` requirement; `--count` becomes optional but must match the Source Record count when given. Multiple source File Types behave like a File Type Mix (File Type column, per-record Email Metadata and Page Count gating, DAT+OPT default when `jpg`/`tiff` participate). `ControlNumber`/`BatesNumber` columns override per-record identity (BatesNumber requires `--bates-prefix`); extra columns map through `--column-profile` (DAT, Standard and Loadfile-Only modes). With `--production-set`, the `BatesNumber` column is rejected, identity stays Bates-based, and `--source-path-mode` controls Native File placement. In Standard mode `--folders` and `--distribution` are ignored (source paths define the structure) |
| `--source-path-mode` | Requires `--production-set` and source input (`--input-csv` / `--directory-template`) |
| `--attachment-rate` | Only meaningful when `--type eml` (Email File Type) or when `eml` participates in `--types` |
| `--with-families` | Only meaningful when `--type eml` (or `eml` participates in `--types`) and `--attachment-rate > 0` (emits a soft warning to stderr otherwise) |
| `--tiff-pages` | Only meaningful when `--type tiff` or when `tiff` participates in `--types` |
| `--bates-start`, `--bates-digits` | Only meaningful when `--bates-prefix` is specified |
| `--date-format`, `--empty-percentage`, `--custodian-count` | Only meaningful when `--column-profile` is specified |
| `--load-file-formats` vs `--load-file-format` | Multi-format list takes precedence over single format |
| `--include-load-file` + `--load-file-formats` | All specified formats are included in the ZIP |
| `--delimiter-*` + `--dat-delimiters` | Specific delimiter flags override the preset for that delimiter only |
| Strict-prefix `--col-delim`/`--quote-delim`/etc. + old-style flags or preset | Strict-prefix arguments win per delimiter; full chain: defaults → `--dat-delimiters` preset → `--delimiter-*` → strict-prefix. Preset and old-style flags are DAT-only; strict-prefix work in all generation modes but affect only DAT output |
| `--load-file-format csv` vs `--dat-delimiters csv` | Distinct: former selects a true `.csv` (RFC 4180) writer; latter only swaps a `.dat` file's delimiters to comma/quote |
| `--hash-algorithms` | Requires `--hash-mode` to be `actual` or `simulated` |
| `--hash-mode actual` + `--loadfile-only` | **Conflict**: cannot compute actual hashes without generated Native Files |
| `--loadfile-only` + `--target-zip-size` | **Conflict**: cannot use both |
| `--loadfile-only` + `--include-load-file` | **Conflict**: cannot use both |
| `--col-delim`, `--quote-delim`, etc. | Use `ascii:N` or `char:C` prefix (works in all modes) |
| `--chaos-mode` | Requires `--loadfile-only` |
| `--chaos-mode` + `--production-set` | **Conflict**: chaos requires `--loadfile-only` |
| `--chaos-amount`, `--chaos-types` | Require `--chaos-mode` |
| `--chaos-scenario` | Requires `--chaos-mode`; conflicts with `--chaos-types` |
| `--chaos-scenario` + format | Some scenarios require specific `--loadfile-format` (e.g., `broken-boundaries` requires `opt`) |
| `--production-set` | Requires `--bates-prefix`; conflicts with `--loadfile-only` |
| `--redacted-production` | Requires `--production-set`; conflicts with `--loadfile-only`. Redacted text files are only written when `--with-text` is enabled; without it, `REDACTED_TEXT_PATH` is empty in the Load File. |
| `--withheld-native-policy` | Requires `--redacted-production` |
| `--production-set` + `--load-file-format / --load-file-formats` | Ignored. Production Set always generates DAT+OPT regardless. |
| `--production-zip`, `--volume-size` | Require `--production-set` |
| `--supplemental-production` | Requires `--production-set` and `--prior-manifest` |
| `--prior-manifest`, `--supplemental-gap-policy` | Require `--supplemental-production` |
| `--rolling-count`, `--rolling-bates-mode`, `--production-id` | Require `--production-set` |
| `--compare-production-manifests` | Requires `--comparison-mode` and `--comparison-output`. Bypasses normal file generation and validation. |
| `--comparison-mode`, `--comparison-output` | Require `--compare-production-manifests` |
| `--with-families` + non-dat format | Supported. Generates parent-child columns/relationships in CSV, Concordance, and EDRM-XML. |

