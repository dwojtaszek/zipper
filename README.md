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

#### Email Threading
Generate deterministic Email reply and forward chains with RFC 5322 threading headers and thread metadata Columns:
```bash
zipper --type eml --count 10000 --output-path ./threaded_emails --with-threading --seed 42
```

#### MIME Realism
Generate Emails with encoded subjects, alternative body parts, multiple Attachments, and bounded nested Emails:
```bash
zipper --type eml --count 1000 --output-path ./mime_realism --mime-realism full --attachment-rate 50 --seed 42
```
Use `--mime-realism standard` for RFC-compliant MIME diversity without nested Emails or deliberately non-RFC headers.
When `--with-families` is enabled, every emitted MIME Attachment becomes a child record and, in Production Set mode, a separate child Native File. The singular Email Metadata `Attachment` column continues to show the first Attachment's filename. `--with-text` emits a unique Extracted Text sidecar for each Attachment. With custom `nested-rfc822` tokens, a rate-selected placeholder Attachment is preserved; enable `multi-attachment` to emit it alongside a nested Email.

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

#### 6. Test documented platform import failures
Generate a DAT Load File with deterministic path, field-length, date, control-byte, and trailing-empty anomalies. These new anomaly types remain opt-in, so the existing default Chaos output is unchanged:
```bash
zipper --loadfile-only --count 100000 --output-path ./import-failures \
    --chaos-mode --chaos-scenario realistic-import-failures --seed 42
```
Each `path-pointer` Anomaly is checked against its original File Path by `PathReconciliationValidator` and recorded in the Audit File as a `PathReconciliation` mismatch. If colliding Source Record paths prevent a safe missing-path candidate, the Anomaly uses an empty File Path instead.

Format-specific chaos scenarios cover the other Load File formats: `csv-escaping-failures` for CSV, `concordance-import-failures` for Concordance, and `edrm-xml-structural-failures` for EDRM-XML. For example:

```bash
zipper --loadfile-only --loadfile-format edrm-xml --count 10000 --output-path ./xml-failures \
    --chaos-mode --chaos-scenario edrm-xml-structural-failures --seed 42
```

The `cross-format-desync` and `broken-families` scenarios coordinate DAT↔OPT anomalies and write reciprocal references into both Audit Files. Use either Loadfile-Only Mode with both formats or a Production Set, which always includes both Load Files. Any other explicitly selected Load File Formats are generated normally without cross-format anomalies:
```bash
zipper --loadfile-only --load-file-formats dat,opt --count 1000 --output-path ./cross-format-failures \
    --chaos-mode --chaos-scenario cross-format-desync --seed 42
zipper --production-set --type eml --count 1000 --output-path ./family-failures --bates-prefix TEST \
    --with-families --attachment-rate 100 --chaos-mode --chaos-scenario broken-families --seed 42
```

For email/MIME parser resilience testing, inject MIME boundary mismatches, malformed RFC 2047 encoded-words, illegal header folding, and deep RFC 822 nesting into generated `.eml` files. This works in Standard mode and in Production Set mode; `--chaos-amount` (default `1%`) selects the Emails to corrupt, and the audit lists each anomaly:
```bash
zipper --type eml --count 500 --output-path ./email-mime-failures \
    --chaos-mode --chaos-types mime-boundary,encoded-word,header-fold,rf822-depth --seed 42
```

#### 7. Generate parser-valid adversarial DAT values
Exercise DAT parsers with correctly escaped delimiters, the literal `Ã¾` lookalike, a 32,768-character value, and two trailing empty rows:
```bash
zipper --loadfile-only --count 100 --output-path ./adversarial-values \
    --chaos-mode --chaos-scenario adversarial-values --seed 42
```
The first selected record contains all three value classes in a generated 32,768-character value. `--chaos-amount` controls which records receive the adversarial values. The scenario defaults to `100%`, so omitting `--chaos-amount` selects every record. Use `--count 0` with this scenario to write a header-only Load File followed by the same two empty rows. This scenario requires DAT-only output; for TIFF or JPG generation, specify `--loadfile-format dat` to avoid the usual implicit OPT Load File. With Source-Driven Generation (`--input-csv` or `--directory-template`), the selected Column Profile must contain an eligible text or long-text column not mapped to Source Metadata; validation fails if no such column exists. Without an explicit Column Profile, use `--with-metadata` or generate Email File Types, which include metadata columns automatically. Source Metadata remains intact, so a supplied value may independently have the same length. Custom delimiters and encodings must round-trip the generated values and avoid fallback characters that corrupt DAT quoting or record boundaries.

#### 8. Publish Archive Test Fixture pairs
Publish the frozen smoke suite (five cases: three valid controls plus a CRC lie and a missing EOCD) as flat `<fixtureId>.zip` / `<fixtureId>.json` pairs for extractor testing — see [Archive Test Suites](docs/archive-test-suites.md):
```bash
zipper --archive-test-suite smoke --seed 42 --output-path ./archive-cases
```
For Archive Test workflows, `--seed` accepts only non-negative integers and defaults to 42; negative values fail before output generation.
Expectation Files can declare Adapter Checks for named Reader Adapter Profiles. The E2E matrix requires Python `zipfile` and the .NET reference reader, compares Info-ZIP `unzip` when available, runs declared optional checks, and prints per-fixture agreement or splits; missing optional readers and platform-inapplicable Archive Test Fixtures are reported as skips. See [Archive Test Suites](docs/archive-test-suites.md).
To generate the malformed `stored-payload-bitflip` Archive Test Fixture on its own:
```bash
zipper --archive-test-suite malformed --archive-test-cases stored-payload-bitflip --seed 42 --output-path ./payload-corruption
```
To generate the malformed `deflate-payload-bitflip` Archive Test Fixture, which flips a stored-block literal while keeping the method-8 stream decodable:
```bash
zipper --archive-test-suite malformed --archive-test-cases deflate-payload-bitflip --seed 42 --output-path ./deflate-payload-corruption
```
To generate the malformed `crc-both-mismatch-deflate` Archive Test Fixture, which flips both CRC-32 header values on a Deflate Archive without changing its payload or sizes:
```bash
zipper --archive-test-suite malformed --archive-test-cases crc-both-mismatch-deflate --seed 42 --output-path ./deflate-crc-mismatch
```
To generate the malformed `declared-size-undersized` Archive Test Fixture, which declares both sizes 16 bytes below the intact DEFLATE payload in both headers:
```bash
zipper --archive-test-suite malformed --archive-test-cases declared-size-undersized --seed 42 --output-path ./declared-size-undersized
```
To generate the malformed `version-local-central-mismatch` Archive Test Fixture, which keeps the local header's version-needed at 2.0 while the central header claims the Zip64 4.5 version the Archive does not use:
```bash
zipper --archive-test-suite malformed --archive-test-cases version-local-central-mismatch --seed 42 --output-path ./version-local-central-mismatch
```
To generate the malformed `extra-field-truncated` Archive Test Fixture, whose first central header declares 1,024 extra-field bytes past the Archive's physical end:
```bash
zipper --archive-test-suite malformed --archive-test-cases extra-field-truncated --seed 42 --output-path ./extra-field-truncated
```
To generate the policy-sensitive `zip64-extra-without-sentinels` Archive Test Fixture, which tests conflicting 32-bit header and Zip64 extended-information sizes:
```bash
zipper --archive-test-suite malformed --archive-test-cases zip64-extra-without-sentinels --seed 42 --output-path ./zip64-extra-conflict
```
To generate the policy-sensitive `trailing-data-undeclared` Archive Test Fixture, which appends 16 bytes after a zero-comment EOCD:
```bash
zipper --archive-test-suite malformed --archive-test-cases trailing-data-undeclared --seed 42 --output-path ./trailing-data
```
To generate the policy-sensitive `zip64-eocd-offset-disagreement` Archive Test Fixture, which keeps the regular EOCD offset true while its Zip64 counterpart points 16 bytes into the central directory:
```bash
zipper --archive-test-suite malformed --archive-test-cases zip64-eocd-offset-disagreement --seed 42 --output-path ./zip64-eocd-offset-disagreement
```
To generate the policy-sensitive `encoding-local-central-codec-divergence` Archive Test Fixture, where the same logical name uses CP932 in the local header and UTF-8 in the central header:
```bash
zipper --archive-test-suite encoding --archive-test-cases encoding-local-central-codec-divergence --seed 42 --output-path ./encoding-divergence
```
To generate the policy-sensitive `name-sibling-prefix-traversal` Archive Test Fixture from the security suite:
```bash
zipper --archive-test-suite security --archive-test-cases name-sibling-prefix-traversal --seed 42 --output-path ./sibling-prefix-traversal
```

#### 9. Choose the Archive compression method
`--compression` sets the ZIP compression method for every Archive the run produces (Standard mode, `--include-load-file`, and `--production-zip`) and defaults to `deflate`:
```bash
zipper --type pdf --count 1000 --output-path ./stored_archive --compression store
```
`store` writes entries uncompressed and, with `--target-zip-size`, switches size pre-checks and padding to an uncompressed ratio of 1.0. `deflate64` and `bzip2` are recognized but not yet supported and are rejected. See [Archive Compression](docs/advanced-guide.md#archive-compression).

---

## Features & Supported Formats

- **Native File Types**: PDF, JPG, TIFF, EML, DOCX, XLSX
- **Load File Formats**: DAT (Concordance), OPT (Opticon), CSV (RFC 4180), EDRM-XML (v1.2), Concordance (quote-wrapped)
- **Generation Modes**:
  - **Standard Mode**: Zip Archive + Load File
  - **Production Set Mode**: Volume-structured output (`DATA/`, `IMAGES/`, `NATIVES/`, `TEXT/`, `_manifest.json`). The **Production Manifest** carries an Azure-safe **Production Metadata** block (`metadata`, with `production_id`, `bates_number_start`, `bates_number_end`, and `volume_count`) whose values are normalized (lossy) derivatives of the manifest's own fields and are mapped onto `x-ms-meta-<key>` headers by upload clients. Every value character is tab (0x09) or printable ASCII (0x20–0x7E); CR (0x0D) and LF (0x0A) become a space, tab is preserved, and every other disallowed character (including other C0 controls, DEL 0x7F, and non-ASCII characters) becomes `_`. The combined key/value budget of 8,192 bytes is guaranteed by construction because the CLI rejects any parsed `--bates-prefix` element longer than 200 UTF-8 bytes and any `--production-id` element longer than 250 UTF-8 bytes after rolling Production ID derivation, and the Production Manifest writer refuses to serialize a **Production Metadata** block that exceeds the budget; the comma-joined flag value itself may be longer when every element is within its cap (the worst-case derived Production Metadata block is 759 bytes); post-generation validation rejects values outside the allowed set. The root `_validation_report.json` also cross-checks DAT/OPT document references, order, page counts, and volume-relative image paths (REQ-233).
  - **Loadfile-Only Mode**: Standalone Load File + `_properties.json` audit file
- **Advanced Capabilities**: Email attachment simulation (`--attachment-rate`), family relationships (`--with-families`), Email Threading (`--with-threading`), MIME realism emission modes (`--mime-realism`), redacted production sets (`--redacted-production`), and Chaos Engine anomaly injection or parser-valid `adversarial-values` generation (`--chaos-mode`).
- **Output Safety & Preservation**: Collision-resistant base naming with incremental suffixes (`_1`, `_2`, …) and atomic creation (`FileMode.CreateNew`) prevent overwriting existing Archives or Load Files. In Standard mode, entry path collisions across Native Files, generated Attachments, Extracted Text, and included Load Files/Audit Files fail descriptively rather than silently dropping or renaming records. Production Set mode rejects existing output directories and zip archives upfront before generation, and cleanup routines delete only artifacts created during the failed run.

---

## Usage & Arguments Quick Reference

All command-line flags recognized by Zipper:

| Argument | Default | Accepted Values / Range | Description |
|----------|---------|-------------------------|-------------|
| `--type` | `pdf` | `pdf`, `jpg`, `tiff`, `eml`, `docx`, `xlsx` | File type to generate |
| `--types` | none | comma-separated `type:weight` (e.g. `pdf:70,eml:30`) | File type mix weights |
| `--input-csv` | none | file path | Source-driven generation from CSV |
| `--directory-template` | none | directory path | Source-driven generation mirroring directory structure |
| `--count` | **required** | non-negative integer for `adversarial-values`; otherwise positive | Total number of files/records to generate |
| `--output-path` | **required** | directory path | Target output directory |
| `--folders` | `1` | `1` to `100` | Number of subfolders for file distribution |
| `--encoding` | `UTF-8` | `UTF-8`, `UTF-16`, `ANSI` | Text encoding for Load Files |
| `--distribution` | `proportional` | `proportional`, `gaussian`, `exponential` | Folder distribution algorithm |
| `--with-metadata` | `false` | flag | Include standard metadata columns (incl. Compression Method reflecting `--compression`) |
| `--with-collection-metadata` | `false` | flag | Include e-discovery collection metadata columns |
| `--with-text` | `false` | flag | Generate companion extracted text files |
| `--attachment-rate` | `0` | `0` to `100` | Percentage of Emails containing attachments |
| `--target-zip-size` | none | e.g. `500MB`, `10GB` | Target padded Archive size |
| `--include-load-file` | `false` | flag | Include Load File inside ZIP Archive |
| `--compression` | `deflate` | `store`, `deflate` (`deflate64`, `bzip2` planned — currently rejected) | ZIP compression method for output Archives (Standard mode, `--include-load-file`, `--production-zip`) |
| `--load-file-format` | `dat` | `dat`, `opt`, `csv`, `edrm-xml`, `xml`, `concordance` | Single Load File format |
| `--load-file-formats` | none | comma-separated list (e.g. `dat,opt,csv`) | Multiple simultaneous Load File formats |
| `--loadfile-format` | `dat` | `dat`, `opt`, `csv`, `edrm-xml`, `xml`, `concordance` | Alias for `--load-file-format` in Loadfile-Only mode |
| `--dat-delimiters` | `standard` | `standard`, `csv` | Preset delimiter style for DAT format |
| `--delimiter-column` | ASCII 20 | ASCII code or single character | Custom column delimiter |
| `--delimiter-quote` | ASCII 254 | ASCII code or single character | Custom quote delimiter |
| `--delimiter-newline` | ASCII 174 | ASCII code or single character | Custom newline replacement |
| `--bates-prefix` | none | string (or comma-separated list), maximum 200 UTF-8 bytes per element | Bates Number prefix |
| `--bates-start` | `1` | non-negative integer | Starting Bates number |
| `--bates-digits` | `8` | `1` to `20` | Bates number digit padding count |
| `--tiff-pages` | `1-1` | min-max range (e.g. `1-20`) | Page count range for TIFF files (controls OPT page count and profile-driven DAT `PAGECOUNT` in Loadfile-Only mode for TIFF output) |
| `--column-profile` | none | `minimal`, `standard`, `litigation`, `full`, or file path | Metadata column profile |
| `--seed` | none (42 for Archive Test) | integer (non-negative for Archive Test) | Random seed for reproducible runs |
| `--date-format` | `yyyy-MM-dd` | format string | Override date format string |
| `--empty-percentage` | `15` | `0` to `100` | Percentage of empty values for optional fields |
| `--custodian-count` | `10` | `1` to `1000` | Number of custodians in data pool |
| `--with-families` | `false` | flag | Generate one child record for each emitted Email Attachment |
| `--with-threading` | `false` | flag | Generate RFC 5322 Message-ID, In-Reply-To, and References Email thread chains with thread metadata Columns (alias `--with-email-threading`) |
| `--mime-realism` | none | `standard`, `full`, or comma-separated tokens (`encoded-words`, `header-folding`, `quoted-printable`, `base64-body`, `multipart-alternative`, `multi-attachment`, `nested-rfc822`, `non-rfc`) | Configure MIME realism emission modes on generated EML emails (aliases `--email-mime-realism`, `--with-mime-realism`) |
| `--loadfile-only` | `false` | flag | Standalone Load File generation (no Archive) |
| `--eol` | `CRLF` | `CRLF`, `LF`, `CR` | Line ending format for Load Files |
| `--col-delim` | ASCII 20 | `ascii:N` or `char:C` | Column delimiter (strict-prefix format) |
| `--quote-delim` | ASCII 254 | `ascii:N`, `char:C`, or `none` | Quote delimiter (strict-prefix format) |
| `--newline-delim` | ASCII 174 | `ascii:N` or `char:C` | Newline replacement (strict-prefix format) |
| `--multi-delim` | `;` | `ascii:N` or `char:C` | Multi-value separator |
| `--nested-delim` | `\` | `ascii:N` or `char:C` | Nested value separator |
| `--chaos-mode` | `false` | flag | Enable Chaos Engine anomaly injection or `adversarial-values` generation (requires `--loadfile-only`, `--production-set`, or `--type eml` in Standard mode) |
| `--chaos-amount` | `1%` | exact count `N` or percentage `N%` | Chaos target count/percentage |
| `--chaos-types` | format defaults | comma-separated list | Filter anomaly types for the selected format/artifact: DAT (`mixed-delimiters`, `quotes`, `columns`, `eol`, `encoding`, `path-pointer`, `field-length`, `date-format`, `null-byte`, `trailing-empties`), OPT (`opt-boundary`, `opt-columns`, `opt-pagecount`, `opt-path`, `opt-batesnumber`, `opt-encoding`), CSV (`csv-quote-escape`, `csv-embedded-newline`, `csv-bom`), Concordance (`concordance-delimiter`, `concordance-quote-escape`), EDRM-XML (`xml-unclosed-tag`, `xml-bad-entity`, `xml-encoding-lie`), or Email (`mime-boundary`, `encoded-word`, `header-fold`, `rf822-depth`); the new DAT types are opt-in and `full-chaos` enables all types for the selected format |
| `--chaos-scenario` | none | scenario name | Predefined chaos scenario (DAT `adversarial-values` emits valid edge-case values, not anomalies; `cross-format-desync` and `broken-families` coordinate DAT↔OPT consistency anomalies) |
| `--chaos-list` | `false` | flag | Print available chaos scenarios and exit |
| `--production-set` | `false` | flag | Structured Production Set output |
| `--volume-size` | `5000` | positive integer | Maximum documents per volume subfolder |
| `--production-id` | auto | string, requires `--production-set`, non-empty, maximum 250 UTF-8 bytes per generated element (after rolling derivation), single safe path segment per element | Production ID for the Production Set |
| `--rolling-count` | `1` | positive integer | Generate multiple Rolling Production Sets |
| `--rolling-bates-mode` | `continuous` | `continuous`, `restart` | Bates sequence behavior across Rolling Production Sets |
| `--source-path-mode` | `bates` | `bates`, `preserve`, `originals` | Source path placement policy |
| `--production-zip` | `false` | flag | Wrap Production Set output in ZIP Archive |
| `--redacted-production` | `false` | flag | Redacted production placeholders |
| `--withheld-native-policy` | `keep-native` | `keep-native`, `omit-native-path`, `replace-with-placeholder` | Redacted Production native path handling policy |
| `--supplemental-production` | `false` | flag | Supplemental Production Set generation |
| `--prior-manifest` | none | comma-separated paths | Paths to prior production `_manifest.json` files |
| `--supplemental-gap-policy` | `reject` | `reject`, `allow` | Gap validation policy for Supplemental Production Sets |
| `--compare-production-manifests` | none | comma-separated paths | Compare Production Set manifests; the last path is the new Production Manifest, earlier paths are prior input |
| `--comparison-mode` | none | `replacement`, `supplemental`, `reproduction` | Production Manifest comparison ruleset; `replacement` and `reproduction` share the same matching/classification rules and differ only in reported mode |
| `--comparison-output` | none | file path | Report JSON output path |
| `--hash-mode` | none | `actual`, `simulated`, `none` | Document hash computation mode |
| `--hash-algorithms` | `md5` | `md5`, `sha1`, `sha256` | Comma-separated hash algorithms |
| `--benchmark` | `false` | flag | Run performance benchmark suite and exit |
| `--archive-test-suite` | none | `smoke`, `compatibility`, `malformed`, `security`, `encoding`, `all` | Publish an Archive Test Fixture suite (the `security` suite includes `name-sibling-prefix-traversal`; the `encoding` suite includes local/central codec divergence; closed flag set: requires `--output-path` pointing to a new directory; accepts only `--archive-test-suite`, `--archive-test-cases`, `--seed`, `--output-path`; see [Archive Test Suites](docs/archive-test-suites.md)) |
| `--archive-test-cases` | none | comma-separated Case Keys | Filter the suite to the named cases (closed flag set; requires `--archive-test-suite`) |
| `--version` | `false` | flag | Print full embedded version string and exit |

Chaos requires `--loadfile-only`, `--production-set` (for cross-format scenarios `cross-format-desync` and `broken-families`, or when generating `--type eml`), or Standard mode generating `--type eml`. On an email run, `--chaos-types` selects only the email types (`mime-boundary`, `encoded-word`, `header-fold`, `rf822-depth`), and `--chaos-scenario` is rejected unless it is a cross-format scenario in Production Set mode. An email type on a `--loadfile-only` run is rejected because that run generates no Emails. `--chaos-scenario` conflicts with `--chaos-types`. Only the DAT `adversarial-values` scenario accepts `--count 0`, which writes a header-only Load File plus two trailing empty rows. It requires DAT-only output, bypasses anomaly injection, and remains parser-valid. For TIFF or JPG generation, specify `--loadfile-format dat` because those File Types normally add OPT output implicitly. For positive counts, `--chaos-amount` controls which records receive the adversarial values; the first selected record contains the escaped delimiters, `Ã¾` lookalike, and a 32,768-character value. The five documented platform import-failure DAT types are opt-in, and `realistic-import-failures` requires DAT. Each format has its own anomaly catalog: CSV, Concordance, and EDRM-XML no longer inherit the DAT arms; cross-format scenarios (`cross-format-desync`, `broken-families`) span DAT and OPT with reciprocal audit tracking. Additional explicitly selected Load File Formats are generated normally without cross-format anomalies. `full-chaos` enables every anomaly type for the selected format. To run a cross-format scenario outside a Production Set, pass both formats explicitly with `--load-file-formats dat,opt`.

`--with-threading` is meaningful when EML participates in the File Type selection; otherwise Zipper warns and continues. It adds RFC 5322 `Message-ID`, `In-Reply-To`, and `References` headers to generated Emails, plus Thread ID, Inclusive, and Thread Parent Columns in DAT, CSV, and Concordance Load Files. The default-off mode preserves legacy EML bytes.

`--mime-realism` is meaningful when EML participates in the File Type selection; otherwise Zipper warns and continues. It configures realistic MIME structures on generated EML files including RFC 2047 encoded-word non-ASCII subjects, header folding, Quoted-Printable/Base64 body encodings, `multipart/alternative` plain/HTML representations, multi-attachment emails, and bounded nested `message/rfc822` emails. The default-off mode preserves legacy EML bytes. The singular Email Metadata `Attachment` column continues to show the first Attachment's filename. When `--with-families` is enabled, each emitted MIME Attachment is included as its own family child; `--with-text` creates a unique Extracted Text sidecar for each Attachment. With custom `nested-rfc822` tokens and without `multi-attachment`, a placeholder Attachment selected by `--attachment-rate` is kept and no nested Email is added to that message.

### Standard Load File Columns

In Standard generation mode (no custom `--column-profile`), Load File columns are emitted in a fixed order depending on active flags:

| Column (DAT) | Column (CSV) | Column (Concordance) | Trigger / Condition |
|---|---|---|---|
| — | — | `BEGATTY`, `ENDATTY` | Fixed leading columns in Concordance format |
| `Control Number` | `CONTROL NUMBER` | `CONTROLNUMBER` | Always present (required) |
| `File Path` | `FILE PATH` | `PATH` | Always present (required) |
| `File Type` | `FILE TYPE` | `FILE_TYPE` | `--types` (File Type Mix only) |
| `Custodian` | `CUSTODIAN` | `CUSTODIAN` | `--with-metadata` or `--type eml` / EML in mix |
| `Date Sent` | `DATE SENT` | `DATESENT` | `--with-metadata` or `--type eml` / EML in mix |
| `Author` | `AUTHOR` | `AUTHOR` | `--with-metadata` or `--type eml` / EML in mix |
| `File Size` | `FILE SIZE` | `FILESIZE` | `--with-metadata` or `--type eml` / EML in mix |
| `Compression Method` | `COMPRESSION METHOD` | `COMPRESSION_METHOD` | `--with-metadata` or `--type eml` / EML in mix |
| `To`, `From`, `CC`, `Subject`, `Sent Date`, `Attachment` | `TO`, `FROM`, `CC`, `SUBJECT`, `SENT DATE`, `ATTACHMENT` | `TO`, `FROM`, `SUBJECT`, `SENTDATE`, `ATTACHMENT` | `--type eml` or EML in mix |
| `Data Source`, `Collection Date`, `De-Nisted`, `Dedupe Group ID`, `Processing Status` | `DATA_SOURCE`, `COLLECTION_DATE`, `DENISTED`, `DEDUPE_GROUP_ID`, `PROCESSING_STATUS` | `DATA_SOURCE`, `COLLECTION_DATE`, `DENISTED`, `DEDUPE_GROUP_ID`, `PROCESSING_STATUS` | `--with-collection-metadata` |
| `Bates Number` | `BATES NUMBER` | `BATES` | `--bates-prefix` |
| `Page Count` | `PAGE COUNT` | `PAGECOUNT` | `--type tiff` (or TIFF in mix) + `--tiff-pages` |
| `Extracted Text` | `EXTRACTED TEXT` | `TEXT_PATH` | `--with-text` |
| `BEGATTACH`, `ENDATTACH`, `PARENTDOCID` | `BEGATTACH`, `ENDATTACH`, `PARENTDOCID` | `BEGATTACH`, `ENDATTACH`, `PARENTDOCID` | `--with-families` |
| `Thread ID`, `Inclusive`, `Thread Parent` | `THREAD ID`, `INCLUSIVE`, `THREAD PARENT` | `THREAD_ID`, `INCLUSIVE`, `THREAD_PARENT` | `--with-threading` |

For in-depth explanations, delimiter syntax, argument interaction rules, and audit schemas, see the [Advanced CLI & Reference Guide](docs/advanced-guide.md). Argument interactions for `--compression` and the exclusive `--archive-test-suite` workflow are in [§7 Argument Interactions Reference](docs/advanced-guide.md#7-argument-interactions-reference).

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

The scalability verdict fails when the worst later-vs-prior-peak throughput ratio across increasing file counts falls below 0.25 (tolerates up to 4x machine noise; independent from the CI Perf Guard historical baseline).

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
- [Archive Test Suites](docs/archive-test-suites.md) — Extractor-test fixture pairs with paired unique IDs (`--archive-test-suite`, ADR-0008)
- [Architecture Specifications](docs/architecture.md) — Core system design, seams, and pipeline architecture
- [Requirements & Specifications](Requirements.md) — Immutable functional requirement definitions (`REQ-XXX`)
- [Ubiquitous Language](UBIQUITOUS_LANGUAGE.md) — Domain language definitions
- [CI/CD & Testing Guide](docs/cicd.md) — Pipeline map, local hooks, and CI gates
