# Zipper: Test Data Generation Tool

Zipper is a high-performance .NET tool for generating synthetic e-discovery Archives (`.zip`), Production Sets, and Load Files (`.dat`, `.opt`, `.csv`, `.xml`). Designed for e-discovery platform testing, benchmark suites, and ingestion validation, Zipper can scale to 100+ million records.

---

## Quick Start

### Installation & Prerequisites

- **Prerequisites**: [.NET 10.0 SDK](https://dotnet.microsoft.com/) (or newer)
- **Build from Source**:
  ```bash
  git clone https://github.com/dwojtaszek/zipper.git
  cd zipper
  dotnet publish -c Release
  ```
  The binary is built to `src/bin/Release/net10.0/<platform>/publish/zipper`.

### Core Use Cases

#### 1. Generate an Archive with Native Files & DAT Load File
Generate 10,000 PDF files distributed across 5 folders with standard metadata:
```bash
zipper --type pdf --count 10000 --output-path ./output --folders 5 --with-metadata
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

## Usage & Arguments Reference

### Key Arguments

| Argument | Description | Default | Values / Examples |
|----------|-------------|---------|-------------------|
| `--count <N>` | Total records/files to generate | **Required** | Integer (e.g. `10000`) |
| `--output-path <dir>` | Target output directory | **Required** | Directory path |
| `--type <type>` | Single native file type | `pdf` | `pdf`, `jpg`, `tiff`, `eml`, `docx`, `xlsx` |
| `--types <mix>` | File type mix weights | none | `pdf:70,eml:20,tiff:10` |
| `--load-file-format <fmt>` | Load file format | `dat` | `dat`, `opt`, `csv`, `edrm-xml`, `concordance` |
| `--load-file-formats <list>` | Generate multiple formats | none | `dat,opt,csv` |
| `--folders <N>` | Directory folder count | `1` | `1` to `100` |
| `--distribution <pattern>` | Folder distribution | `proportional` | `proportional`, `gaussian`, `exponential` |
| `--with-metadata` | Standard metadata columns | `false` | Flag |
| `--with-text` | Extracted text files | `false` | Flag |
| `--bates-prefix <prefix>` | Bates prefix | none | e.g. `CLIENT001` |
| `--bates-start <N>` | Starting Bates number | `1` | Integer |
| `--bates-digits <N>` | Bates digit padding | `8` | e.g. `8` (`CLIENT00000001`) |
| `--column-profile <name>` | Metadata column profile | none | `minimal`, `standard`, `litigation`, `full` |
| `--production-set` | Enable Production Set output | `false` | Flag (requires `--bates-prefix`) |
| `--loadfile-only` | Standalone Load File output | `false` | Flag |

For complete argument interactions, delimiter options, Chaos Engine flags, and audit schemas, see the [Advanced CLI & Reference Guide](docs/advanced-guide.md).

---

## Performance & Benchmarks

Zipper utilizes multi-threaded parallel generation, object memory pooling, and streaming buffered I/O to achieve high throughput:

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
- Setting up the development environment
- Running unit, analyzer, and E2E smoke tests
- Architectural invariants and critical principles (`AGENTS.md`)

---

## Documentation Index

- [Contributing Guide](docs/contributing.md) — Build, test, and developer workflow
- [Advanced CLI & Reference Guide](docs/advanced-guide.md) — Complete flag reference, Chaos Engine, delimiter tuning, and schemas
- [Architecture Specifications](docs/architecture.md) — Core system design, seams, and pipeline architecture
- [Requirements & Specifications](Requirements.md) — Immutable functional requirement definitions (`REQ-XXX`)
- [Ubiquitous Language](UBIQUITOUS_LANGUAGE.md) — Domain language definitions
- [CI/CD & Testing Guide](docs/cicd.md) — Pipeline map, local hooks, and CI gates
