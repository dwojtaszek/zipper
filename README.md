# Zipper: Test Data Generation Tool

Zipper is a high-performance .NET command-line tool for generating synthetic e-discovery Archives (`.zip`), Production Sets, and Load Files (`.dat`, `.opt`, `.csv`, `.xml`). Designed for e-discovery platform testing, benchmark suites, and ingestion validation, Zipper scales to 100+ million records and runs cross-platform on **Windows**, **macOS**, and **Linux**.

---

## Quick Start

### Installation & Prerequisites

- **Prerequisites**: [.NET 10.0 SDK](https://dotnet.microsoft.com/) (or newer)
- **Cross-Platform Compatibility**: Windows (x64/arm64), macOS (x64/arm64), and Linux (x64/arm64)
- **Build from Source**:
  ```bash
  git clone https://github.com/dwojtaszek/zipper.git
  cd zipper
  dotnet publish -c Release
  ```
  The executable will be placed in `src/bin/Release/net10.0/<platform>/publish/` (`zipper.exe` on Windows, `zipper` on Linux/macOS).

### Core Use Cases

#### 1. Generate an Archive with Native Files & DAT Load File
Generate 10,000 PDF files distributed across 5 folders with standard metadata:

**Linux / macOS (Bash / zsh)**:
```bash
zipper --type pdf --count 10000 --output-path ./output --folders 5 --with-metadata
```

**Windows (Command Prompt / PowerShell)**:
```cmd
zipper.exe --type pdf --count 10000 --output-path .\output --folders 5 --with-metadata
```

#### 2. Generate a File Type Mix
Generate 10,000 documents with mixed file types (70% PDF, 20% EML, 10% TIFF):
```bash
zipper --types "pdf:70,eml:20,tiff:10" --count 10000 --output-path ./mixed_output
```

#### 3. Generate a Legal Production Set
Generate a structured e-discovery Production Set with Bates numbering and volume output:
```bash
zipper --production-set --bates-prefix "CLIENT001" --count 5000 --output-path ./prod_set --with-text
```

#### 4. Standalone Load File Generation (No Native Files)
Generate a 100,000-record DAT Load File directly to disk:
```bash
zipper --loadfile-only --count 100000 --output-path ./loadfile_data
```

#### 5. Source-Driven Generation
Mirror an input CSV or existing directory structure using synthetic placeholder content:
```bash
zipper --input-csv ./source.csv --output-path ./source_output --bates-prefix ABC
```

---

## Features & Supported Formats

- **Native File Types**: PDF, JPG, TIFF, EML, DOCX, XLSX
- **Load File Formats**: DAT (Concordance), OPT (Opticon), CSV (RFC 4180), EDRM-XML (v1.2), Concordance (quote-wrapped)
- **Generation Modes**:
  - **Standard Mode**: Zip Archive + Load File
  - **Production Set Mode**: Volume-structured output (`DATA/`, `IMAGES/`, `NATIVES/`, `TEXT/`, `_manifest.json`)
  - **Loadfile-Only Mode**: Standalone Load File + `_properties.json` audit file
- **Advanced Capabilities**: Email attachment simulation (`--attachment-rate`), family relationships (`--with-families`), redacted production sets (`--redacted-production`), and chaos anomaly injection (`--chaos-mode`).

---

## Usage & Arguments Quick Reference

All command-line flags recognized by Zipper:

| Argument | Default | Accepted Values / Range | Description |
|----------|---------|-------------------------|-------------|
| `--type` | `pdf` | `pdf`, `jpg`, `tiff`, `eml`, `docx`, `xlsx` | File type to generate |
| `--types` | none | comma-separated `type:weight` (e.g. `pdf:70,eml:30`) | File type mix weights |
| `--input-csv` | none | file path | Source-driven generation from CSV |
| `--directory-template` | none | directory path | Source-driven generation mirroring directory structure |
| `--count` | **required** | positive integer | Total number of files/records to generate |
| `--output-path` | **required** | directory path | Target output directory |
| `--folders` | `1` | `1` to `100` | Number of subfolders for file distribution |
| `--encoding` | `UTF-8` | `UTF-8`, `UTF-16`, `ANSI` | Text encoding for Load Files |
| `--distribution` | `proportional` | `proportional`, `gaussian`, `exponential` | Folder distribution algorithm |
| `--with-metadata` | `false` | flag | Include standard metadata columns |
| `--with-collection-metadata` | `false` | flag | Include e-discovery collection metadata columns |
| `--with-text` | `false` | flag | Generate companion extracted text files |
| `--attachment-rate` | `0` | `0` to `100` | Percentage of Emails containing attachments |
| `--target-zip-size` | none | e.g. `500MB`, `10GB` | Target padded Archive size |
| `--include-load-file` | `false` | flag | Include Load File inside ZIP Archive |
| `--load-file-format` | `dat` | `dat`, `opt`, `csv`, `edrm-xml`, `concordance` | Single Load File format |
| `--load-file-formats` | none | comma-separated list (e.g. `dat,opt,csv`) | Multiple simultaneous Load File formats |
| `--loadfile-format` | `dat` | `dat`, `opt` | Alias for `--load-file-format` in Loadfile-Only mode |
| `--dat-delimiters` | `standard` | `standard`, `csv` | Preset delimiter style for DAT format |
| `--delimiter-column` | ASCII 20 | ASCII code or single character | Custom column delimiter |
| `--delimiter-quote` | ASCII 254 | ASCII code or single character | Custom quote delimiter |
| `--delimiter-newline` | ASCII 174 | ASCII code or single character | Custom newline replacement |
| `--bates-prefix` | none | string (or comma-separated list) | Bates numbering prefix |
| `--bates-start` | `1` | non-negative integer | Starting Bates number |
| `--bates-digits` | `8` | `1` to `20` | Bates number digit padding count |
| `--tiff-pages` | `1-1` | min-max range (e.g. `1-20`) | Page count range for TIFF files |
| `--column-profile` | none | `minimal`, `standard`, `litigation`, `full`, or file path | Metadata column profile |
| `--seed` | none | integer | Random seed for reproducible runs |
| `--date-format` | `yyyy-MM-dd` | format string | Override date format string |
| `--empty-percentage` | `15` | `0` to `100` | Percentage of empty values for optional fields |
| `--custodian-count` | `10` | `1` to `1000` | Number of custodians in data pool |
| `--with-families` | `false` | flag | Generate parent-child attachment relationships |
| `--loadfile-only` | `false` | flag | Standalone Load File generation (no Archive) |
| `--eol` | `CRLF` | `CRLF`, `LF`, `CR` | Line ending format for Load Files |
| `--col-delim` | ASCII 20 | `ascii:N` or `char:C` | Column delimiter (strict-prefix format) |
| `--quote-delim` | ASCII 254 | `ascii:N`, `char:C`, or `none` | Quote delimiter (strict-prefix format) |
| `--newline-delim` | ASCII 174 | `ascii:N` or `char:C` | Newline replacement (strict-prefix format) |
| `--multi-delim` | `;` | `ascii:N` or `char:C` | Multi-value separator |
| `--nested-delim` | `\` | `ascii:N` or `char:C` | Nested value separator |
| `--chaos-mode` | `false` | flag | Enable Chaos Engine anomaly injection |
| `--chaos-amount` | `1%` | exact count `N` or percentage `N%` | Anomaly target count/percentage |
| `--chaos-types` | all | comma-separated list | Filter specific chaos anomaly types |
| `--chaos-scenario` | none | scenario name | Predefined chaos scenario |
| `--chaos-list` | `false` | flag | Print available chaos scenarios and exit |
| `--production-set` | `false` | flag | Structured Production Set output |
| `--volume-size` | `5000` | positive integer | Maximum documents per volume subfolder |
| `--production-id` | auto | string | Production volume set identifier |
| `--rolling-count` | `1` | positive integer | Generate multiple rolling production sets |
| `--rolling-bates-mode` | `continuous` | `continuous`, `restart` | Bates sequence behavior across rolling sets |
| `--source-path-mode` | `bates` | `bates`, `preserve`, `originals` | Source path placement policy |
| `--production-zip` | `false` | flag | Wrap Production Set output in ZIP Archive |
| `--redacted-production` | `false` | flag | Redacted production placeholders |
| `--withheld-native-policy` | `keep-native` | `keep-native`, `omit-native-path`, `replace-with-placeholder` | Redacted mode native path handling policy |
| `--supplemental-production` | `false` | flag | Supplemental Production Set generation |
| `--prior-manifest` | none | comma-separated paths | Paths to prior production `_manifest.json` files |
| `--supplemental-gap-policy` | `reject` | `reject`, `allow` | Gap validation policy for supplemental sets |
| `--compare-production-manifests` | none | comma-separated paths | Compare Production Set manifests |
| `--comparison-mode` | none | `replacement`, `supplemental`, `reproduction` | Production Manifest comparison ruleset |
| `--comparison-output` | none | file path | Report JSON output path |
| `--hash-mode` | none | `actual`, `simulated`, `none` | Document hash computation mode |
| `--hash-algorithms` | `md5` | `md5`, `sha1`, `sha256` | Comma-separated hash algorithms |
| `--benchmark` | `false` | flag | Run performance benchmark suite and exit |
| `--version` | `false` | flag | Print version string and exit |

For in-depth explanations, delimiter syntax, argument interaction rules, and audit schemas, see the [Advanced CLI & Reference Guide](docs/advanced-guide.md).

---

## Performance & Benchmarks

Zipper utilizes multi-threaded parallel generation, object memory pooling, and streaming buffered I/O to achieve high throughput across Windows, macOS, and Linux:

| File Count | Typical Time | Files / Second |
|------------|--------------|----------------|
| 1,000      | 1–2 sec      | 500 – 1,500    |
| 10,000     | 5–10 sec     | 1,000 – 3,000  |
| 100,000    | 30–60 sec    | 1,500 – 4,000  |

Run the built-in micro-benchmark suite:
```bash
zipper --benchmark
```

---

## Contributing & Developer Setup

We welcome contributions! Please see our [Contributing Guide](docs/contributing.md) for instructions on:
- Setting up the development environment on Windows, macOS, or Linux
- Running unit, analyzer, and E2E smoke tests across platforms
- Architectural invariants and critical principles (`AGENTS.md`)

---

## Documentation Index

- [Contributing Guide](docs/contributing.md) — Build, test, and developer workflow (Windows, macOS, Linux)
- [Advanced CLI & Reference Guide](docs/advanced-guide.md) — Complete flag reference, Chaos Engine, delimiter tuning, and schemas
- [Architecture Specifications](docs/architecture.md) — Core system design, seams, and pipeline architecture
- [Requirements & Specifications](Requirements.md) — Immutable functional requirement definitions (`REQ-XXX`)
- [Ubiquitous Language](UBIQUITOUS_LANGUAGE.md) — Domain language definitions
- [CI/CD & Testing Guide](docs/cicd.md) — Pipeline map, local hooks, and CI gates
