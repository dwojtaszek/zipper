# Zipper Advanced CLI & Reference Guide

This reference guide covers advanced flags, column profiles, delimiter configurations, Chaos Engine usage, Production Set comparisons, and audit schema details.

---

## 1. Advanced CLI Options

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

## 2. Audit & Manifest File Schemas

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

## 3. Argument Interactions Reference

| Interaction | Behavior |
|-------------|----------|
| `--column-profile` + `--with-metadata` | Column profile takes precedence; `--with-metadata` is ignored with a warning |
| `--column-profile` + `--production-set` | **Conflict**: `--column-profile` is not supported with `--production-set` |
| `--types` | Mutually exclusive with `--type`, `--loadfile-only`, and `--column-profile` |
| `--input-csv` / `--directory-template` | Mutually exclusive with each other and with `--type`/`--types`. Requires no `--count` (or `--count` matching source record count) |
| `--chaos-mode` | Requires `--loadfile-only` and `dat`/`opt` format |
| `--production-set` | Requires `--bates-prefix`; conflicts with `--loadfile-only` |
| `--redacted-production` | Requires `--production-set`; text placeholders require `--with-text` |
