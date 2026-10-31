# Ubiquitous Language: Zipper Domain Model

## Core Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Archive Test Fixture** | A deliberately constructed ZIP **Archive** — valid, malformed, or **policy-sensitive** — published as a flat folder pair: `atc-<64-hex>.zip` plus its **Expectation File**, for testing unzip implementations. Produced by the Archive Test workflow (#844; not a generation mode, ADR-0008); distinct from user-facing Standard/Production Set **Archives**. | Test zip, fixture archive, test case, test vector |
| **Archive Test Suite** | A predefined grouping of **Archive Test Fixtures** (`smoke`, `compatibility`, `malformed`, `security`, `encoding`, or `all`) selected via `--archive-test-suite`, optionally filtered to named **Case Keys** via `--archive-test-cases` (REQ-208). | Test suite, fixture suite, zip test suite |
| **Fixture ID** | The unique instance identifier of an **Archive Test Fixture**: `atc-` plus 64 lowercase SHA-256 hex characters over the canonical descriptor (**Case Key**, revisions, **Seed**, final Archive SHA-256). Names both the `.zip` and the `.json` of a pair. | Fixture name, case ID |
| **Case Key** | The human-readable scenario name of an **Archive Test Fixture** (e.g. `valid-empty`). Identifies the scenario, not the instance; one Case Key plus revisions, **Seed**, and final bytes determine the **Fixture ID**. | Scenario name, test id |
| **Expectation File** | The JSON sidecar (`atc-<64-hex>.json`) paired with an **Archive Test Fixture**, recording identity, entries, mutations, expectations, and budgets per `tests/fixtures/archive-test-case.schema.json`. Distinct from the **Audit File** (`_properties.json`). | Expected file, JSON manifest |
| **Adapter Check** | A per-case directive in an **Expectation File** telling a named **Reader Adapter Profile** which operation to perform and which normalized outcome to require, independently of whether a platform-scoped Reader Profile expectation applies. The .NET reference reader and Python `zipfile` support all four operations; Info-ZIP supports list, read-entry, and integrity-check; 7-Zip retains `extract`, `test`, `extract-entries`, and `presence`. Reference-reader `extract` Adapter Checks are valid only for `valid` fixtures, because ordinary verification does not extract malformed or policy-sensitive fixtures. | Reader expectation, adapter policy |
| **Mutation** | A deliberate byte-level or structural modification applied to a ZIP **Archive** entry, header, or payload to test reader resilience, recorded with `before-mutation` offsets, lengths, and SHA-256 hashes in the **Expectation File**. Distinct from Load File **Anomaly**. | Anomaly, corruption, defect |
| **Reader Profile** | The named target extractor behavior model declared in `expectations[].profile` of an **Expectation File** (`strict`, `strict-integrity-v1`, `full-codec`, `unsupported-reader`; `common` in schema examples) that determines which outcomes and failure stages are valid for each reader operation (`list`, `read-entry`, `integrity-check`, `extract`). | Decompressor profile, parser mode |
| **Reader Adapter Profile** | A concrete reader implementation in the Archive Test differential matrix (`dotnet-reference`, `python-zipfile`, `infozip-unzip`, `7zip`). It identifies which reader runs; optional profiles are skipped with a reason when their executable is absent, and an Archive Test Fixture with no host-applicable expectation is reported as skipped. Distinct from the behavior model in an Expectation File's **Reader Profile**. | Reader tool profile, adapter profile |
| **Capability Profile** | The codec-gated **Reader Profile** pairing (`full-codec` for extractors supporting **Deflate64**/**BZip2** vs `unsupported-reader` for extractors that must reject unsupported codecs cleanly) together with the specific feature tag in `expectations[].capability` (`crc32`, `method-9-deflate64`, `method-98-ppmd`, `method-98-mixed`, `deflate-bitstream`, `coded-bitstream`). | Codec profile, support level, feature set |
| **Frozen Test Vector** | A deterministic, version-pinned test fixture reference (e.g. `valid-empty` at **Seed** 42, Case Revision 1, Expectation Revision 2 → `atc-4d275f1fe266174e43c71d2cbe42084da23ce42369a17af7b84a9d165b6c489f`, or `valid-stored` at **Seed** 42) whose binary output and **Expectation File** are preserved as exact baseline fixtures. | Golden fixture, reference vector |
| **Archive** | A compressed `.zip` file containing zero or more generated **Native Files** organized into **Folders**. Each **Archive** corresponds to one **Load File dataset** (which may be exported in multiple formats: DAT, OPT, CSV, XML). | ZIP file, package |
| **Native File** | A single placeholder document (PDF, JPG, TIFF, DOCX, XLSX, or EML) added to the **Archive**. Each **Native File** has a unique identity tracked in the **Load File**. | File, document, item |
| **Load File** | A data file that maps **Native Files** to metadata. Formats are delimited text (DAT, OPT, CSV, or Concordance) or hierarchical EDRM-XML. One record per **Native File**, with two exceptions: OPT emits one record per page (a multipage TIFF expands to `_NNN`-suffixed page records), and with `--with-families` each **Attachment** adds one family child record (keyed `{parent}_A001`, `{parent}_A002`, and so on). | Manifest, index, metadata file |
| **Metadata** | **Column** values in the **Load File** describing a **Native File**. In the narrow CLI sense (`--with-metadata`), adds `Custodian`, `Date Sent`, `Author`, `File Size`, and `Compression Method` (also automatically enabled in Standard mode whenever `eml` is in the **File Type Mix**); distinct from **Email Metadata**, **Collection Metadata**, and **Source Metadata**. | Attributes, properties, fields |
| **Collection Metadata** | Five optional eDiscovery processing **Columns** (`Data Source`, `Collection Date`, `De-Nisted`, `Dedupe Group ID`, `Processing Status` in Standard **Concordance DAT**/EDRM-XML; `DATA_SOURCE`, `COLLECTION_DATE`, `DENISTED`, `DEDUPE_GROUP_ID`, `PROCESSING_STATUS` in CSV/Concordance/**Production Set** **Concordance DAT**) added via `--with-collection-metadata` (FR-025, REQ-181–REQ-189). | Processing metadata, eDiscovery metadata, NIST columns |
| **Folder** | A logical directory within the **Archive** (used only during regular **Archive** generation via `--folders`) to distribute **Native Files** across a directory structure. The number of **Folders** is configurable; defaults to 1. | Directory, bucket, container |
| **Distribution** | The pattern by which **Native Files** are assigned to **Folders**. Supported patterns: `proportional`, `gaussian`, `exponential`. | Assignment strategy, allocation |
| **File Type** | The format of generated **Native Files**: `pdf`, `jpg`, `tiff`, `eml`, `docx`, `xlsx`. | Format, extension, kind |
| **File Type Mix** | A weighted set of **File Types** generated together in one **Archive** or **Production Set** via `--types type:weight,...`. Counts are allocated exactly (largest-remainder) in contiguous ranges in declared order. **Load Files** identify each record's own type (File Type column / `FILE_TYPE`); the **Production Manifest** `fileType` is a top-level comma-separated string of participating **File Types** (e.g. `"pdf,eml"`), not a per-record value. | Mixed types, multi-type, type blend |
| **Source-Driven Generation** | A generation mode (Standard, Loadfile-Only, or Production Set) where records come from a user-supplied **Source CSV** (`--input-csv`) or **Directory Template** (`--directory-template`) instead of a synthetic plan. Source files define **File Paths**, **File Types**, optional identity overrides, and (Source CSV only) **Source Metadata**; their bytes are never copied. Multiple source **File Types** behave like a **File Type Mix**. With **Production Set**, per-record `BatesNumber` overrides are rejected and **Source Path Mode** controls Native File placement. | CSV-driven generation, template-driven generation |
| **Source Record** | One row of source input: a **File Path**, a **File Type**, and optional `ControlNumber`, `BatesNumber`, and **Source Metadata** values. Maps to exactly one **Native File** (Standard or Production Set) or one **Load File** record (Loadfile-Only). | Input row, source row |
| **Source CSV** | A CSV file driving **Source-Driven Generation**. Required columns `FilePath` and `FileType`; optional `ControlNumber`/`DocId`, `BatesNumber`/`Bates`/`BegBates`; extra columns become **Source Metadata**. | Input CSV |
| **Directory Template** | A directory recursively walked to derive **Source Records**: each file's relative path and extension-derived **File Type** define one record. Files with unsupported extensions are rejected. | Template directory, folder template |
| **Source Metadata** | Extra **Source CSV** columns beyond path, type, and identity. Surfaced in the **Load File** only through **Column Profile** mapping (**Concordance DAT**); unmatched keys are ignored. | Custom source columns |
| **Source Path Mode** | Native File placement when **Source-Driven Generation** feeds a **Production Set** (`--source-path-mode`): `bates` (standard Volume layout, default), `preserve` (source subdirectories under the Volume, Bates-named file), or `originals` (full source path with original filename under ORIGINALS). | Path placement mode |
| **ORIGINALS** | The Production Set directory tree that mirrors **Source Record** relative paths verbatim in `originals` **Source Path Mode**. Derived artifacts (TEXT, IMAGES, REDACTED) and Attachment children never live there; they stay Volume-rooted and Bates-named. | Originals folder |
| **Control Number** | A unique identifier for each **Native File** in the **Load File**. Required **Column**. | DOCID, document ID |
| **File Path** | The relative path to a **Native File** within the **Archive** or production structure. | Item path, location, reference |

---

## Email Specific Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Email** | A **Native File** with File Type `eml`. Contains **Email Metadata** (`To`, `From`, `CC`, `Subject`, `Sent Date`) and may have zero or more **Attachments** when MIME Realism is enabled. Represented by `Zipper.Emails.Email`. | EML file, message |
| **Email Metadata** | Intrinsic **Columns** in the **Load File** for **Emails**: `To`, `From`, `CC`, `Subject`, `Sent Date`, and `Attachment` in Standard/CSV/Concordance/EDRM-XML, or `Attachment`, `EmailSubject`, `EmailFrom`, `EmailTo`, `EmailCC`, and `EmailSentDate` in the **Production Set** **Concordance DAT** (`EmailSubject`, `EmailFrom`, `EmailTo`, `EmailCC`, `EmailSentDate` in **Loadfile-Only Mode**). When an **Email** has multiple **Attachments**, the singular `Attachment` **Column** identifies the first **Attachment**; `--with-families` exposes every **Attachment** as its own child record. Included whenever `eml` is in the **File Type Mix** regardless of `--with-metadata`; in Standard mode, `eml` also enables the five `--with-metadata` **Columns** (`Custodian`, `Date Sent`, `Author`, `File Size`, `Compression Method`). Absent from the OPT format (which carries no **Metadata** **Columns**), while `Compression Method` is also absent from the **Production Set** **Concordance DAT** per REQ-001. | Email headers, email-specific columns |
| **Attachment** | A MIME part embedded within an **Email**. It may contain placeholder binary content (one of `attachment.jpg`, `attachment.tiff`, or `attachment.pdf` drawn at random from the internal Attachment pool) or a bounded nested **Email** (`message/rfc822`). Placeholder Attachments use internal content, never one of the `--count` generated **Native Files** (REQ-011), preserving the streaming, no-intermediate-storage generation design. The **Attachment Rate** determines what percentage of **Emails** receive at least one placeholder **Attachment**; MIME Realism's `multi-attachment` capability may add more. With `--with-families`, each emitted **Attachment** is surfaced as its own **Document Family** child (keyed `{parent}_A001`, `{parent}_A002`, and so on) and, in **Production Sets**, materializes as separate child files under `NATIVES`/`IMAGES`/`TEXT`, counted separately in the **Production Manifest** (`attachmentNativeFileCount`). Represented by `Zipper.Emails.EmailAttachment`. | Enclosed file, embedded document |
| **Attachment Rate** | The percentage (0–100) of **Emails** that receive at least one random placeholder **Attachment**. Controlled by `--attachment-rate`. | Attachment probability, attachment percentage |
| **Document Family** | A parent-child relationship linking an **Email** parent record to each **Attachment** child record (`{parent}_A001`, `{parent}_A002`, and so on in Standard/**Loadfile-Only Mode**, or sequential child **Bates Numbers** in a **Production Set**) via family **Columns** (`BEGATTACH`, `ENDATTACH`, `PARENTDOCID` in **Concordance DAT**/CSV/**Production Set**; plus `BEGATTY`, `ENDATTY` in Concordance; `<Relationships>` in EDRM-XML). Enabled with `--with-families`. | Email family, attachment group, parent-child group |
| **Email Threading** | An opt-in generation mode (`--with-threading`, alias `--with-email-threading`) that emits RFC 5322 `Message-ID`, `In-Reply-To`, and `References` headers across generated **Email** files to form realistic conversation trees, and exposes **Thread ID**, **Inclusive Email**, and **Thread Parent** in the **Load File**. | Conversation threading, message threading |
| **Thread ID** | The document identifier of the root **Email** of a conversation thread (`DOC00000001` or root Bates number), populated in the `Thread ID` / `THREAD ID` / `THREAD_ID` **Column** for all members of the thread. | Conversation ID, thread group |
| **Inclusive Email** | An **Email** in a thread containing unique content not subsumed by any subsequent reply (a leaf node in the thread tree), indicated by `"Y"` (or `"N"` for subsumed ancestor messages) in the `Inclusive` / `INCLUSIVE` **Column**. | Inclusive message, thread leaf |
| **Thread Parent** | The document identifier of the immediate parent **Email** within a conversation thread, populated in the `Thread Parent` / `THREAD PARENT` / `THREAD_PARENT` **Column** (empty for root messages and attachment family children). | Parent doc, parent thread ID |
| **MIME Realism** | An opt-in generation mode (`--mime-realism`, aliases `--email-mime-realism`, `--with-mime-realism`) that emits realistic MIME structures (RFC 2047 encoded-words, header folding, Quoted-Printable/Base64 body encodings, `multipart/alternative`, multi-attachment parts, and bounded nested `message/rfc822` emails) on generated **Email** files. | MIME diversity, realistic MIME |
| **RFC 2047 Encoded-Word** | A MIME-encoded ASCII token sequence (`=?utf-8?B?...?=` or `=?utf-8?Q?...?=`) used to represent non-ASCII text in email header fields, bounded to ≤ 75 characters per encoded-word. | Encoded word, MIME word |
| **Header Folding** | The RFC 5322 continuation of long header fields across multiple lines separated by CRLF and linear whitespace (space or tab). | MIME folding, line folding |
| **Multipart Diversity** | The emission of structured MIME multipart structures (`multipart/alternative` for text/plain and text/html bodies, `multipart/mixed` for messages with multiple attachments). | Multipart email, alternate body |
| **Nested RFC 822** | A bounded attached **Email** formatted as a `message/rfc822` part within a parent **Email**'s MIME container up to a configured nesting depth. | Attached email, embedded EML, recursive email |

---

## Load File Composition Concepts

The delimited **Load File Formats** (DAT, OPT, CSV, Concordance) are produced by three deep modules in sequence. EDRM-XML is the **carve-out**: its hierarchical document tree does not fit a flat record, so it keeps its own dedicated writer (see ADR-0007).

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Load File Record** | A format-independent row: ordered **Columns** plus raw (unescaped) values aligned with the columns by index, with a **Record Id** for chaos auditing. Cardinality follows the **Load File** definition: one record per **Native File**, except OPT page-level records and `--with-families` **Document Family** child records. `LoadFileRecord`. | Row object, line model |
| **Composer** | The **column authority** for a format: decides the header **Columns** and yields **Load File Records** with raw values (handling modes and **Column Profiles** internally). `ILoadFileComposer`, `DatComposer`, `OptComposer`, `CsvComposer`, `ConcordanceComposer`. | Builder, row generator |
| **Serializer** | The **render authority**: turns one **Load File Record** (or header) into a single escaped text line. Pure — no stream, no **Line Ending**, no chaos. `ILoadFileSerializer`, `DatSerializer`, `OptSerializer`, `CsvSerializer`, `ConcordanceSerializer`. | Formatter, writer |
| **Emitter** | The **I/O and chaos authority**: writes rendered lines to the stream, owning the encoding preamble (BOM), **Line Ending** sequence, output batching, and the per-format **Chaos Engine** pipeline. In coordinated DAT/OPT scenarios, the **Cross-Format Anomaly Applier** processes both rendered line buffers before the **Emitter** writes them. Ordinary paths stream lazily (O(1) auxiliary memory); coordinated DAT/OPT generation buffers both **Load Files** so their records can be reconciled. `LoadFileEmitter`. | Stream writer, output writer |

## Load File Format Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Load File Format** | The structured output format of the **Load File**. Supported formats: DAT, OPT (Opticon), CSV, EDRM-XML, and Concordance (database-import). The `concordance` CLI value is a **distinct** format — not an alias of `dat`. | File format, output format |
| **Concordance DAT** | Industry-standard **Load File Format** using ASCII delimiters (DC4 for columns, Thorn for quotes, ®  for newlines). Default format. | DAT format, concordance format |
| **Delimiter** | A special character marking boundaries in a **Concordance DAT** format. Types: **Column Delimiter**, **Quote Delimiter**, **Newline Delimiter**, **Multi-Value Delimiter**, **Nested Delimiter**. | Separator, boundary marker |
| **Column Delimiter** | The character separating **Columns** in a **Concordance DAT**. Default: ASCII 20 (DC4). | Field separator |
| **Quote Delimiter** | The character used to enclose **Column** values containing **Column Delimiter** characters in a **Concordance DAT**. Default: ASCII 254 (Thorn). When the **Quote Delimiter** appears within a **Column** value, it is escaped (doubled). | Text qualifier, quote character |
| **Newline Delimiter** | The character replacing actual newlines (`\n`, `\r`, `\r\n`) within **Metadata** values in a **Concordance DAT** (CLI `--load-file-format dat`). Default: ASCII 174 (®). Prevents **Load File** corruption. Not used in Concordance database-import (`concordance` format), which preserves embedded newlines within quote qualifiers. | Newline replacement, line-break escape |
| **Extracted Text** | A companion `.txt` file for each **Native File**, containing placeholder text. Optional, controlled by `--with-text`. The **Load File** includes a **Text Path** column. | Text file, OCR text, content file |
| **Text Path** | The relative path to an **Extracted Text** file within the **Archive**. | Text file reference, text location |

---

## Cryptographic Hash Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Hash Mode** | Mode controlling cryptographic hash generation for **Native Files** (`actual`, `simulated`, `none`). | Checksum mode, hash strategy |
| **Hash Algorithm** | Supported cryptographic hash algorithm (`md5`, `sha1`, `sha256`) emitted into **Load File** hash columns. | Hash type, checksum algorithm |
| **Hash Column** | Intrinsic column emitted in **Load Files** when **Hash Mode** is enabled (`MD5HASH`, `SHA1HASH`, `SHA256HASH`). | Checksum column, hash field |

---

## Bates Numbering Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Bates Number** | A unique sequential identifier assigned to each **Native File** in legal workflows. Format: `{PREFIX}{PADDED_NUMBER}`. Optional **Column** in Standard and **Loadfile-Only Mode** via `--bates-prefix` (REQ-039); required in a **Production Set** (REQ-120), where it also names each **Native File**, **Extracted Text**, and image file. | Bates ID, bates code |
| **Bates Prefix** | The text prefix for a **Bates Number** (e.g., "CLIENT001"). Specified by `--bates-prefix`. | Bates code prefix |
| **Bates Start** | The non-negative starting integer (`>= 0`) for **Bates Number** generation. Defaults to 1 (REQ-039). | Start number, initial count |
| **Bates Digits** | The zero-padding width for the numeric part of a **Bates Number** (e.g., 8 digits yields `00000001`). Defaults to 8. | Digit count, padding width |

---

## Pagination & Structure Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Page Count** | The number of pages in a **Native File**. Only relevant for File Type `tiff`. Inclusion is mode- and format-specific: row-based formats (DAT, CSV, Concordance, EDRM-XML) emit the **Column** only when `--tiff-pages` is specified (REQ-046), while OPT carries `PageCount` as a fixed positional **Column** (total on the first page record, `1` for single-page **Native Files**) and **Loadfile-Only Mode** OPT assigns random 1–10 pages per **Native File** when `--tiff-pages` is omitted (REQ-047). In **Loadfile-Only Mode**, an explicit **Column Profile** containing a **Page Count** **Column** draws from `--tiff-pages` when specified for TIFF output, defaulting to random 1–10 pages per **Native File** when omitted (REQ-058). In a **File Type Mix**, values appear only on TIFF records. | Number of pages, page range |
| **TIFF Page Range** | A range specification (min-max) defining the variability of **Page Count** across **TIFF** **Native Files**. E.g., "1-20" generates TIFFs with 1–20 pages. Defaults to "1-1". | Page count range, page bounds |
| **Volume** | A logical partition within a **Production Set** (used only when `--production-set` is specified). Each **Volume** can contain up to **Volume Size** **Native Files**. Named `VOL001`, `VOL002`, etc. | Partition, subset, group |
| **Volume Size** | The maximum number of **Native Files** per **Volume**. Defaults to 5,000. | Volume limit, max files per volume |

---

## Target Size & Compression Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Target Zip Size** | A desired final size for the **Archive** (e.g., 500MB, 10GB). When specified, each **Native File** is padded with uncompressible data to meet the target. May require recalculation if achievable. | Target size, goal size |
| **Padding** | Non-compressible random data appended to a **Native File** to achieve a **Target Zip Size**. | Filler, uncompressible data |
| **Compression Ratio** | The ratio of uncompressed to compressed size in the **Archive**. Using **Target Zip Size** significantly reduces the **Compression Ratio**. | Compression factor, shrink factor |
| **Compression Method** | The compression algorithm applied to **Archive** entries: **Stored** (0, CLI token `store`), **Deflate** (8, CLI token `deflate`, default), **Deflate64** (9, CLI token `deflate64`), or **BZip2** (12, CLI token `bzip2`). Controlled by `--compression`, which supports `store` and `deflate` for user-generated **Archives** and rejects `deflate64` and `bzip2` as not yet supported (REQ-223); **Archive Test Fixtures** emit all four plus wire code 98 (PPMd). | Compression type, compression algorithm |
| **Stored** | An uncompressed **Archive** entry (Compression Method 0) where entry payload matches raw uncompressed bytes exactly. | Store, uncompressed |
| **Deflate** | Standard DEFLATE compressed **Archive** entry (Compression Method 8). | Deflated, zlib |
| **Deflate64** | Enhanced DEFLATE compressed **Archive** entry (Compression Method 9) using a 64KB sliding dictionary. | Enhanced deflate |
| **BZip2** | Burrows-Wheeler block-sorting compressed **Archive** entry (Compression Method 12). | Bzip |
| **Local Header Method** | The compression method ID recorded in the ZIP Local File Header (LFH). | LFH method, local method |
| **Central Directory Method** | The compression method ID recorded in the ZIP Central Directory File Header (CDFH). | CDFH method, central method |
| **Payload Codec** | The actual compression stream format of the entry payload bytes in an **Archive Test Fixture** (`stored`, `deflate`, `deflate64`, `bzip2`, or `unknown` for file entries; omitted for directory entries per REQ-217), which may diverge from **Local Header Method** and **Central Directory Method** under **Mutation**. | Wire codec, entry codec |

---

## Column Profile Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Column Profile** | A reusable configuration specifying which **Metadata** **Columns** to include in the **Load File** and how to generate their values. Can be built-in (`minimal`, `standard`, `litigation`, `full`) or custom JSON. | Profile, metadata schema, column schema |
| **Column** | A single **Metadata** column in the **Load File**. Examples: DOCID, FILEPATH, CUSTODIAN, DATECREATED. A **Column Profile** can define up to 200 **Columns**. | Field, attribute, property |
| **Column Type** | The data type of a **Column**: `identifier`, `text`, `longtext`, `date`, `datetime`, `number`, `boolean`, `coded`, `email`. Determines how values are generated. | Field type, data type |
| **Data Generator** | The algorithm that produces values for a **Column** based on its **Column Type** and **Distribution Pattern**. | Generator, value factory |
| **Custodian** | A **Column** identifying the person or entity responsible for a **Native File**. Often distributed across **Folders** or generated from a pool. | Owner, data owner, responsible party |
| **Distribution Pattern** | The statistical distribution used to generate **Metadata** values within a **Column**. Supported patterns: `uniform`, `gaussian` (with `normal` accepted as an alias), `exponential`, `pareto`, `weighted`. | Data distribution, generation strategy |
| **Empty Percentage** | The percentage (0–100) of **Metadata** values that are intentionally left empty in a **Column**. Configured per **Column** or globally. | Empty rate, null percentage |
| **Multi-Value Field** | A **Column** that can contain multiple values separated by a **Multi-Value Delimiter**. E.g., a `To` **Column** with multiple recipients. | Multi-value column, repeating field |
| **Field Naming Convention** | The header casing rule configured on a **Column Profile** (`fieldNamingConvention`): `UPPERCASE`, `PascalCase`, `lowercase`, or `snake_case`. Enforced by `NamingConventionHelper`. | Header case, column casing |
| **Seed** | A fixed random number used to ensure reproducible **Metadata** generation. When the same **Seed** is used with identical other parameters (**Native File** count, **File Type**, **Column Profile**, etc.), the same **Metadata** values are generated in the same order. | Random seed, deterministic seed |

---

## Loadfile-Only & Chaos Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Loadfile-Only Mode** | A generation mode (`--loadfile-only`) that creates a **Load File** without generating an **Archive** or **Native Files**. Produces a companion `_properties.json` **Audit File**. | Standalone load file mode, audit mode |
| **Chaos Engine** | A subsystem that injects deliberate structural **Anomalies** into **Load Files** for ingestion resilience testing. Single-format scenarios require **Loadfile-Only Mode**; `cross-format-desync` and `broken-families` also support **Production Set** generation. The `adversarial-values` **Chaos Scenario** uses Loadfile-Only Mode to generate parser-valid edge-case values without injecting Anomalies. | Anomaly injector, error simulator |
| **Cross-Format Anomaly Applier** | The coordinator that applies `cross-format-desync` and `broken-families` Anomaly Types to rendered DAT and OPT line buffers together. It records each peer **Load File** and **Line Number** so both companion **Audit Files** identify the coordinated Anomaly. | Multi-file corrupter, cross-format injector |
| **Anomaly** | A deliberately injected error in a **Load File** record, document structure, or record boundary. Types include delimiter corruption, quote issues, column misalignment, unescaped newlines, encoding errors, missing **File Path** values, overlong values, malformed dates, unsupported control bytes, trailing empty columns, malformed EDRM-XML structure, and coordinated DAT/OPT consistency failures. Tracked in the companion **Audit File**, with reciprocal peer locations for coordinated cross-format Anomalies. | Error, corruption, defect |
| **Anomaly Type** | The classification of an **Anomaly**. DAT types: `mixed-delimiters`, `quotes`, `columns`, `eol`, `encoding`, `path-pointer`, `field-length`, `date-format`, `null-byte`, `trailing-empties`. The five record-level DAT types are opt-in to preserve existing output. OPT types: `opt-boundary`, `opt-columns`, `opt-pagecount`, `opt-path`, `opt-batesnumber`, `opt-encoding`; OPT `opt-encoding` is opt-in unless `full-chaos` is selected. CSV types: `csv-quote-escape`, `csv-embedded-newline`, `csv-bom`. Concordance types: `concordance-delimiter`, `concordance-quote-escape`, using its ASCII 20 column and ASCII 254 quote conventions. EDRM-XML types: `xml-unclosed-tag`, `xml-bad-entity`, `xml-encoding-lie`, applied inside the `XmlLoadFileWriter` carve-out. Cross-format types: `desync-pagecount`, `desync-order`, `desync-path`, `orphan-attachment`, `missing-child`, and `broken-parent`. Each non-DAT/OPT format has its own catalog; `full-chaos` enables all types for the selected format. | Error type, corruption type |
| **Chaos Amount** | The number or percentage of **Load File** records selected for Chaos processing, specified as a count or percentage (e.g., `5` or `10%`). The Chaos Engine injects **Anomalies** into selected records unless the `adversarial-values` scenario is active, which instead adds parser-valid edge-case values. | Anomaly count, corruption amount |
| **Chaos Scenario** | A predefined set of **Anomaly Types** and **Chaos Amount** for common ingestion failure patterns (e.g., `structured-import-failures`, `encoding-nightmare`, `realistic-import-failures`, `cross-format-desync`, `broken-families`). The last two coordinate DAT/OPT consistency Anomalies. The `adversarial-values` scenario is the exception: it creates parser-valid DAT edge-case values and does not inject Anomalies. | Scenario, preset configuration |
| **Adversarial-Values Chaos Scenario** | A DAT **Chaos Scenario** that creates parser-valid escaped delimiters, a quote-lookalike value, one scenario-generated platform-length value, and trailing empty rows for parser and importer edge-case testing. It writes generated values to an eligible Column Profile column not mapped to **Source Metadata**, preserving supplied values; if none exists, validation fails. A zero record count creates a header-only **Load File** and the trailing empty rows. | Valid-value scenario |
| **Audit File** | The `_properties.json` companion to a **Load File** in **Loadfile-Only Mode** or a **Production Set**, documenting format details, delimiters, and injected **Anomalies** with line numbers and descriptions. Coordinated cross-format Anomalies include the companion **Load File** and line in a peer reference. | Metadata file, manifest, audit log |

---

## Production Set Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Production Set** | A structured directory hierarchy (`--production-set`) containing **Native Files** organized into `DATA`, `IMAGES`, `NATIVES`, `TEXT` subdirectories with accompanying **Load Files**. | Production, deliverable, structured output |
| **Production Manifest** | The `_manifest.json` file at the root of a **Production Set**, documenting **Volume** structure, **Native File** counts, and **Bates Number** ranges. Carries an Azure-safe **Production Metadata** block (`metadata`): derived key/value pairs an upload client maps onto `x-ms-meta-<key>` headers. | Manifest, metadata file |
| **Production Metadata** | The Azure Blob custom-metadata block of the **Production Manifest**: keys follow the Azure C#-identifier rule, while values are printable ASCII (0x20–0x7E) plus tab (0x09). The generator emits it Azure-safe by construction, normalizing CR/LF to spaces, preserving tabs, and replacing other disallowed characters with underscores; post-generation validation enforces the character and 8,192-byte combined key/value budget constraints (REQ-225, REQ-226). | Azure metadata, x-ms-meta |
| **Production Metadata Budget** | The maximum aggregate byte size (8,192 bytes UTF-8) for all key/value pairs in the Azure-safe **Production Metadata** block of a **Production Manifest** (REQ-226). | Metadata budget, Azure budget |
| **Production Zip** | An **Archive** containing the entire **Production Set** directory structure, created when `--production-zip` is specified with `--production-set`. | Production archive, wrapped delivery |
| **Redacted Production** | A Production Set mode (`--redacted-production`) that generates redacted image/text placeholders under `REDACTED/IMAGES/` and `REDACTED/TEXT/`, and optionally withholds Native Files. | Redacted mode, redaction mode |
| **Redacted Image** | A placeholder TIFF file in `REDACTED/IMAGES/` representing a redacted version of a **Native File**'s visual content. | Redacted visual, redacted page |
| **Withheld Native File** | A **Native File** whose path in the **Load File** is omitted, emptied, or replaced with a placeholder path according to `--withheld-native-policy`. | Withheld native, suppressed native |
| **Withheld Native Policy** | The strategy for handling withheld **Native File** paths: `keep-native` (retain original path), `omit-native-path` (empty the path column), or `replace-with-placeholder` (synthetic path). | Native policy, withholding policy |
| **Redaction Reason** | A Metadata value in the `REDACTION_REASON` column indicating why a **Native File** was redacted (e.g., Privileged, Attorney Work Product). Cycled deterministically by **Seed**. | Reason code, redaction justification |

---

## Rolling & Supplemental Production Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Rolling Production Set** | One of N **Production Sets** generated from a single **Generation Request** via `--rolling-count`. Each **Rolling Production Set** carries a `rollingSequenceNumber` in its **Production Manifest**. | Rolling set, rolling production |
| **Production ID** | The configurable identifier for a **Production Set**, used in output directory names, **Production Manifest** Metadata, and report identifiers. Controlled by `--production-id` (single value or comma-separated list); defaults to a `PRODUCTION_<timestamp>` scheme. Each value must be a **Safe Path Segment** because it names a directory. | Production name, production label |
| **Safe Path Segment** | A single path component usable as one directory name, and therefore free of path separators (`/` and `\`), the dot segments `.` and `..`, control characters, a trailing space or dot, the characters Windows reserves in file names (`: * ? " < > |`), and the reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `CONIN$`, `CONOUT$`, `COM1`–`COM9`, `LPT1`–`LPT9`, with or without an extension, where the COM/LPT suffix may also be a superscript one, two, or three). A **Production ID** element must be a Safe Path Segment; the rule is enforced at the CLI input seam and is platform-independent. | Safe filename, sanitized path, path segment |
| **Rolling Bates Mode** | The Bates Number range behavior across **Rolling Production Sets**, controlled by `--rolling-bates-mode`. `continuous` produces one contiguous range across all sets; `restart` resets the range per set. Defaults to `continuous`. | Bates mode, rolling mode |
| **Supplemental Production** | A **Production Set** generation mode (`--supplemental-production`) that validates the new Bates Number range against one or more prior **Production Manifests** (`--prior-manifest`) before writing output. | Supplemental generation, supplemental mode |
| **Prior Production Manifest** | A **Production Manifest** supplied via `--prior-manifest` (or as a non-final argument to `--compare-production-manifests`) whose Bates Number range and records are evaluated against a **Supplemental Production** or **New Production Manifest**. | Prior manifest, previous manifest |
| **Supplemental Gap Policy** | The strategy for handling skipped Bates Numbers between prior and new **Production Manifests**, controlled by `--supplemental-gap-policy`: `reject` (fail validation on gaps) or `allow` (report gaps and proceed). Defaults to `reject`. | Gap policy |

---

## Production Manifest Comparison Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Production Manifest Comparison** | A workflow (`--compare-production-manifests`) that compares two or more **Production Manifests** and writes a machine-readable JSON report with a human-readable summary. Bypasses normal **Native File** generation. Entries are positional: the final resolved manifest is the **New Production Manifest**, every preceding resolved manifest is a **Prior Production Manifest**, and the report preserves the supplied manifest order. | Manifest comparison, comparison |
| **New Production Manifest** | The final positional **Production Manifest** argument supplied to `--compare-production-manifests`, evaluated against one or more preceding **Prior Production Manifests**. | New manifest, target manifest |
| **Comparison Mode** | The ruleset applied by **Production Manifest Comparison**, controlled by `--comparison-mode`: `replacement`, `supplemental`, or `reproduction`. Replacement and reproduction share identical matching and classification rules; supplemental is additive. | Comparison ruleset |
| **Replacement Workflow** | A **Comparison Mode** (`replacement`) comparing a prior **Production Set** against a new one that replaces it. New-side records match prior-side records by **Bates Number**, then **Control Number**; outcomes are `added`, `removed`, `unchanged`, `changed`, or `replaced`. | Replacement mode |
| **Supplemental Workflow** | A **Comparison Mode** (`supplemental`) comparing prior **Production Manifests** against a new **Supplemental Production**. Additive: overlaps are **Duplicates** plus `added`; supplemental records never establish removals. | Supplemental comparison mode |
| **Reproduction Workflow** | A **Comparison Mode** (`reproduction`) auditing corrected **Native File** content/Metadata between an original and a reproduced **Production Set**. Uses the same matching and classification rules as the **Replacement Workflow**; only the reported mode and workflow intent differ. Use `reproduction` (not "re-production") consistently across CLI, requirements, and docs. | Re-production, reproducted, re-production workflow |
| **Volume Membership** | The assignment of a **Native File** to a specific **Volume** within a **Production Set**. A change in Volume assignment triggers a `changed` record outcome during **Replacement Workflow** or **Reproduction Workflow** comparison even if file content matches. | Volume assignment, volume placement |
| **Comparison Report** | The JSON file written to `--comparison-output` containing the **Comparison Mode**, summary counts (added, removed, unchanged, replaced, duplicated, skipped, changed), and Bates Number range analysis. | Comparison output, comparison JSON |

---

## Generation & Processing Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Generation Request** | The complete configuration (all CLI arguments) for a single generation job. Encapsulated in `FileGenerationRequest` and grouped by sub-configs: Output, Metadata, Load File, Delimiters, Bates, TIFF, Chaos, Production, and Hash (plus `LoadfileOnly` and `SourceRecords`). The flat pass-through properties were removed in #213 — access configuration via sub-configs directly (enforced by `FGR_FLAT_ACCESS`). | Request, job specification |
| **File Generation** | The process of creating all **Native Files** for an **Archive** or **Production Set**, distributed across **Folders** or **Volumes** according to the **Generation Request**. | File creation, document generation |
| **Parallel Generation** | Multi-threaded **File Generation** with configurable worker pools optimized for available CPU cores. | Concurrent generation, parallel processing |
| **Worker Pool** | The set of background threads performing **Parallel Generation**. Size is auto-detected based on CPU core count. | Thread pool, worker threads |
| **Batch Size** | The number of **Native Files** processed by each **Worker Pool** thread before synchronization. Optimized for throughput. | Chunk size, processing unit |
| **Performance Monitor** | A subsystem tracking real-time progress, throughput (files/second), memory usage, and ETA during **File Generation**. | Progress tracker, metrics collector |
| **Memory Pool** | An object pool manager that reduces garbage collection pressure during **Parallel Generation** by reusing buffer objects. | Buffer pool, object pool |

---

## Encoding & Output Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Encoding** | The character encoding for the **Load File** output (`--encoding`). Supported: `UTF-8` (default for most modes), `UTF-16` / `UTF-16LE`, `ANSI` (Windows-1252; default for **Loadfile-Only Mode** OPT per REQ-051/REQ-059), and `ASCII`. | Character set, code page |
| **Line Ending** | The newline format for the **Load File**. Controlled by `--eol` in **Loadfile-Only and Production Set modes**. Options: CRLF, LF, CR. Note: in **Standard** (archive) mode, line endings follow the platform default rather than `--eol`, so output bytes may differ between Windows and Linux/macOS. This is a known preserved quirk (see ADR-0007). | EOL, newline format, line separator |
| **Placeholder Content** | The minimal valid content generated for each **Native File** to ensure maximum compression. Identical across all **Native Files** of the same **File Type**. | Template content, default content |
| **Placeholder Text** | The fixed block of text used in **Extracted Text** files to ensure compressibility. | Template text, default text |
| **Validation Report** | The post-generation verification summary (and `_validation_report.json` artifact in a **Production Set**) produced by `ValidationOrchestrator`, `PostGenerationValidator`, `ProductionSetPostValidator`, and `SupplementalValidator` checking **Load File** column alignment, **Line Endings**, OPT page boundaries, path reconciliation, **Redacted Production** paths, and **Production Metadata Budget**. | Validation log, check report |

---

## Relationships

- An **Archive** contains one or more **Native Files** organized into **Folders**
- A **Load File** maps to one **Archive**, one **Production Set**, or stands alone in **Loadfile-Only Mode**. Contains one record per **Native File** (except OPT page records and `--with-families` **Document Family** child records — see the **Load File** definition)
- A **Column Profile** defines zero or more **Columns** for a **Load File**
- An **Email** may have zero or more **Attachments**; **Attachment Rate** selects Emails to receive at least one placeholder **Attachment**, and MIME Realism may add more. `--with-families` links every emitted Attachment as a **Document Family** child
- A **Production Set** contains **Volumes**, each **Volume** contains up to **Volume Size** **Native Files**
- A **Bates Number** is required for each **Native File** in a **Production Set** and optional via `--bates-prefix` in Standard and **Loadfile-Only Mode**
- The **Chaos Engine** injects single-format **Anomalies** into **Load Files** in **Loadfile-Only Mode**; `cross-format-desync` and `broken-families` coordinate DAT/OPT **Anomalies** in **Loadfile-Only Mode** or a **Production Set**. Delimited output uses the **Emitter**, while EDRM-XML stays inside the `XmlLoadFileWriter` carve-out
- An **Audit File** (`_properties.json`) records all **Anomalies** injected by the **Chaos Engine**
- A **Production Manifest** (`_manifest.json`) documents the **Volume** structure and **Bates Number** ranges of a **Production Set**
- A **Validation Report** (`_validation_report.json`) verifies **Load File** and **Production Set** invariants after generation
- **Redacted Production** adds `REDACTED/IMAGES/` and `REDACTED/TEXT/` subdirectories alongside the standard `DATA`, `IMAGES`, `NATIVES`, `TEXT` tree

## Example Dialogue

> **Developer:** "When I run Zipper to create an **Archive**, what ends up in it?"
>
> **Domain Expert:** "Two things: the **Native Files** (your PDFs, **Emails**, whatever **File Type** you specified) and optionally a **Load File**. The **Load File** is the **Metadata**. If you use `--include-load-file`, the **Load File** gets added to the **Archive** too. Otherwise it's written separately."
>
> **Dev:** "So if I generate 1,000 **Native Files** and distribute them into 10 **Folders**, the **Archive** will have a 10-level **Folder** structure?"
>
> **Expert:** "Exactly. The **Distribution** pattern controls *how* those 1,000 **Native Files** are assigned. With `proportional`, each **Folder** gets 100. With `gaussian`, the middle **Folders** get more. The **Load File** has a **File Path** **Column** pointing to each **Native File**'s location."
>
> **Dev:** "What if I use `--with-metadata`?"
>
> **Expert:** "Then the **Load File** gains five additional **Columns**: **Custodian**, Date Sent, Author, File Size, and **Compression Method** — all auto-generated. If `eml` is in your **File Type Mix**, Standard mode automatically includes those five **Columns** plus the six **Email Metadata** **Columns** (`To`, `From`, `CC`, `Subject`, `Sent Date`, and `Attachment`), whether or not you specify `--with-metadata`."
>
> **Dev:** "And the `--chaos-mode` option?"
>
> **Expert:** "That's for testing. Single-format scenarios inject **Anomalies** into a **Load File** in **Loadfile-Only Mode**. The `cross-format-desync` and `broken-families` scenarios coordinate DAT/OPT **Anomalies** in **Loadfile-Only Mode** or a **Production Set**. The **Audit Files** record each **Anomaly** and its peer location."
>
> **Dev:** "How does `--production-set` differ from a regular **Archive**?"
>
> **Expert:** "A **Production Set** is a structured directory tree — `NATIVES/`, `IMAGES/`, `DATA/`, `TEXT/` — with **Native Files** organized into **Volumes**. Each **Volume** holds up to **Volume Size** **Native Files** and gets a mandatory **Bates Number** range that also names every file. A regular **Archive** is a compressed `.zip` organized into **Folders** — no **Volumes**, no `DATA/`/`NATIVES/`/`TEXT/`/`IMAGES/` directory tree, and **Bates Numbers** are optional rather than naming the **Native Files**."
>
> **Dev:** "And the **Production Manifest**?"
>
> **Expert:** "That's the `_manifest.json` at the root of the **Production Set**. It tells you exactly what's in each **Volume** — **Native File** counts, **Bates Number** ranges, directory paths, and Azure-safe **Production Metadata**. Think of it as the manifest for the entire **Production Set**."

---

## Flagged Ambiguities

1. **"File" ambiguity**: 
   - A **Native File** is a placeholder document in the **Archive** (PDF, EML, etc.).
   - A **Load File** is the metadata text file mapping **Native Files**.
   - These are distinct concepts. Always be explicit: "**Native File**" for the archive content, "**Load File**" for metadata.

2. **"Folder" vs "Volume"**:
   - A **Folder** is a logical directory within an **Archive**, controlled by `--folders` and `--distribution`.
   - A **Volume** is a logical partition within a **Production Set**, controlled by `--volume-size`.
   - Use **Folders** when discussing regular **Archive** generation; use **Volumes** when discussing **Production Sets**.

3. **"Metadata" scope**:
   - **Metadata** can mean all **Columns** in the **Load File** (broad definition).
   - `--with-metadata` adds five specific **Columns** (`Custodian`, `Date Sent`, `Author`, `File Size`, `Compression Method`); in Standard mode these five **Columns** are also included automatically whenever `eml` is in the **File Type Mix**.
   - **Email Metadata** adds the six intrinsic **Email** **Columns** (`To`, `From`, `CC`, `Subject`, `Sent Date`, `Attachment` in Standard/CSV/Concordance/EDRM-XML; `Attachment`, `EmailSubject`, `EmailFrom`, `EmailTo`, `EmailCC`, `EmailSentDate` in the **Production Set** **Concordance DAT**) whenever `eml` is generated.
   - **Collection Metadata** adds five eDiscovery processing **Columns** (`Data Source`, `Collection Date`, `De-Nisted`, `Dedupe Group ID`, `Processing Status`) via `--with-collection-metadata`.
   - **Source Metadata** refers to extra columns from a **Source CSV** mapped via a **Column Profile**.
   - Use "**Load File** **Columns**" for breadth; name the specific metadata category (`--with-metadata` **Columns**, **Email Metadata**, **Collection Metadata**, **Source Metadata**, or **Production Metadata**) when precision matters.

4. **"Encoding" vs "Line Ending"**:
   - **Encoding** is the character set (`UTF-8`, `UTF-16`/`UTF-16LE`, `ANSI`, `ASCII`) for the entire **Load File**.
   - **Line Ending** is the newline format (`CRLF`, `LF`, `CR`) within a **Load File**.
   - These are independent settings.
