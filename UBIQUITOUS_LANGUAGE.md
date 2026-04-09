# Ubiquitous Language: Zipper Domain Model

## Core Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Archive** | A compressed `.zip` file containing zero or more generated **Native Files** organized into **Folders**. Each **Archive** corresponds to one **Load File dataset** (which may be exported in multiple formats: DAT, OPT, CSV, XML). | ZIP file, package |
| **Native File** | A single placeholder document (PDF, JPG, TIFF, DOCX, XLSX, or EML) added to the **Archive**. Each **Native File** has a unique identity tracked in the **Load File**. | File, document, item |
| **Load File** | A delimited text record (DAT, OPT, CSV, or XML) that maps **Native Files** to metadata. One record per **Native File**. | Manifest, index, metadata file |
| **Metadata** | Column values in the **Load File** describing a **Native File** (e.g., Custodian, Date Sent, Author, File Size). | Attributes, properties, fields |
| **Folder** | A logical directory within the **Archive** (used only during regular **Archive** generation via `--folders`) to distribute **Native Files** across a directory structure. The number of **Folders** is configurable; defaults to 1. | Directory, bucket, container |
| **Distribution** | The pattern by which **Native Files** are assigned to **Folders**. Supported patterns: `proportional`, `gaussian`, `exponential`. | Assignment strategy, allocation |
| **File Type** | The format of generated **Native Files**: `pdf`, `jpg`, `tiff`, `eml`, `docx`, `xlsx`. | Format, extension, kind |
| **Control Number** | A unique identifier for each **Native File** in the **Load File**. Required field. | DOCID, document ID |
| **File Path** | The relative path to a **Native File** within the **Archive** or production structure. | Item path, location, reference |

---

## Email Specific Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Email** | A **Native File** with File Type `eml`. Contains email metadata (To, From, Subject, Sent Date) and may have **Attachments**. | EML file, message |
| **Email Metadata** | Intrinsic columns in the **Load File** for **Emails** (To, From, CC, Subject, Sent Date). Always included regardless of `--with-metadata` flag. | Email headers, email-specific columns |
| **Attachment** | A **Native File** (any type) selected randomly from the generated set and embedded within an **Email** as binary content. The **Attachment Rate** determines what percentage of **Emails** have **Attachments**. | Enclosed file, embedded document |
| **Attachment Rate** | The percentage (0–100) of **Emails** that receive a random **Attachment**. Controlled by `--attachment-rate`. | Attachment probability, attachment percentage |

---

## Load File Format Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Load File Format** | The structured output format of the **Load File**. Supported formats: DAT (Concordance), OPT (Opticon), CSV, EDRM-XML. | File format, output format |
| **Concordance DAT** | Industry-standard **Load File Format** using ASCII delimiters (DC4 for columns, Thorn for quotes, ®  for newlines). Default format. | DAT format, concordance format |
| **Delimiter** | A special character marking boundaries in a **Concordance DAT** format. Types: **Column Delimiter**, **Quote Delimiter**, **Newline Delimiter**, **Multi-Value Delimiter**, **Nested Delimiter**. | Separator, boundary marker |
| **Column Delimiter** | The character separating fields in a **Concordance DAT**. Default: ASCII 20 (DC4). | Field separator |
| **Quote Delimiter** | The character used to enclose field values containing **Column Delimiter** characters in a **Concordance DAT**. Default: ASCII 254 (Thorn). When the **Quote Delimiter** appears within a field value, it is escaped (doubled). | Text qualifier, quote character |
| **Newline Delimiter** | The character replacing actual newlines (`\n`, `\r`, `\r\n`) within **Metadata** values in a **Concordance DAT**. Default: ASCII 174 (®). Prevents **Load File** corruption. | Newline replacement, line-break escape |
| **Extracted Text** | A companion `.txt` file for each **Native File**, containing placeholder text. Optional, controlled by `--with-text`. The **Load File** includes a **Text Path** column. | Text file, OCR text, content file |
| **Text Path** | The relative path to an **Extracted Text** file within the **Archive**. | Text file reference, text location |

---

## Bates Numbering Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Bates Number** | A unique sequential identifier assigned to each **Native File** in legal workflows. Format: `{PREFIX}{PADDED_NUMBER}`. Optional column in the **Load File**. | Bates ID, bates code |
| **Bates Prefix** | The text prefix for a **Bates Number** (e.g., "CLIENT001"). Specified by `--bates-prefix`. | Bates code prefix |
| **Bates Start** | The starting integer for **Bates Number** generation. Defaults to 1. | Start number, initial count |
| **Bates Digits** | The zero-padding width for the numeric part of a **Bates Number** (e.g., 8 digits yields `00000001`). Defaults to 8. | Digit count, padding width |

---

## Pagination & Structure Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Page Count** | The number of pages in a **Native File**. Only relevant for File Type `tiff`. Included in the **Load File** when TIFF files are generated. | Number of pages, page range |
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

---

## Column Profile Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Column Profile** | A reusable configuration specifying which **Metadata** columns to include in the **Load File** and how to generate their values. Can be built-in (`minimal`, `standard`, `litigation`, `full`) or custom JSON. | Profile, metadata schema, column schema |
| **Column** | A single metadata field in the **Load File**. Examples: DOCID, FILEPATH, CUSTODIAN, DATECREATED. A **Column Profile** can define up to 200 **Columns**. | Field, attribute, property |
| **Column Type** | The data type of a **Column**: `identifier`, `text`, `longtext`, `date`, `datetime`, `number`, `boolean`, `coded`, `email`. Determines how values are generated. | Field type, data type |
| **Data Generator** | The algorithm that produces values for a **Column** based on its **Column Type** and distribution pattern. | Generator, value factory |
| **Custodian** | A **Column** identifying the person or entity responsible for a **Native File**. Often distributed across **Folders** or generated from a pool. | Owner, data owner, responsible party |
| **Distribution Pattern** | The statistical distribution used to generate **Metadata** values within a **Column**. Supported patterns: `uniform`, `gaussian`, `exponential`, `pareto`, `weighted`. | Data distribution, generation strategy |
| **Empty Percentage** | The percentage (0–100) of **Metadata** values that are intentionally left empty in a **Column**. Configured per **Column** or globally. | Empty rate, null percentage |
| **Multi-Value Field** | A **Column** that can contain multiple values separated by a **Multi-Value Delimiter**. E.g., a `To` field with multiple recipients. | Multi-value column, repeating field |
| **Seed** | A fixed random number used to ensure reproducible **Metadata** generation. When the same **Seed** is used with identical other parameters (file count, type, profile, etc.), the same **Metadata** values are generated in the same order. | Random seed, deterministic seed |

---

## Loadfile-Only & Chaos Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Loadfile-Only Mode** | A generation mode (`--loadfile-only`) that creates a **Load File** without generating an **Archive** or **Native Files**. Produces a companion `_properties.json` audit file. | Standalone load file mode, audit mode |
| **Chaos Engine** | A subsystem that injects deliberate structural **Anomalies** into a **Load File** for ingestion resilience testing. Requires **Loadfile-Only Mode**. | Anomaly injector, error simulator |
| **Anomaly** | A deliberately injected error in a **Load File** record. Types: delimiter corruption, quote issues, column misalignment, unescaped newlines, encoding errors. Tracked in `_properties.json`. | Error, corruption, defect |
| **Anomaly Type** | The classification of an **Anomaly**. DAT types: `mixed-delimiters`, `quotes`, `columns`, `eol`, `encoding`. OPT types: `opt-boundary`, `opt-columns`, `opt-pagecount`. | Error type, corruption type |
| **Chaos Amount** | The number or percentage of **Load File** records to corrupt with **Anomalies**. Specified as a count or percentage (e.g., `5` or `10%`). | Anomaly count, corruption amount |
| **Chaos Scenario** | A predefined set of **Anomaly Types** and **Chaos Amount** for common ingestion failure patterns (e.g., `relativity-import`, `encoding-nightmare`). | Scenario, preset configuration |
| **Audit File** | The `_properties.json` file generated in **Loadfile-Only Mode**, documenting format details, delimiters, and all injected **Anomalies** with line numbers and descriptions. | Metadata file, manifest |

---

## Production Set Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Production Set** | A structured directory hierarchy (`--production-set`) containing **Native Files** organized into `DATA`, `IMAGES`, `NATIVES`, `TEXT` subdirectories with accompanying **Load Files**. | Production, deliverable, structured output |
| **Production Manifest** | The `_manifest.json` file at the root of a **Production Set**, documenting **Volume** structure, document counts, and **Bates Number** ranges. | Manifest, metadata file |
| **Production Zip** | An **Archive** containing the entire **Production Set** directory structure, created when `--production-zip` is specified with `--production-set`. | Production archive, wrapped delivery |

---

## Generation & Processing Concepts

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Generation Request** | The complete configuration (all CLI arguments) for a single generation job. Encapsulated in `FileGenerationRequest`. | Request, job specification |
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
| **Encoding** | The character encoding for the **Load File** output. Supported: UTF-8 (default), UTF-16, ANSI (Windows-1252). | Character set, code page |
| **Line Ending** | The newline format for the **Load File**. Controlled by `--eol` in **Loadfile-Only Mode**. Options: CRLF, LF, CR. | EOL, newline format, line separator |
| **Placeholder Content** | The minimal valid content generated for each **Native File** to ensure maximum compression. Identical across all files of the same **File Type**. | Template content, default content |
| **Placeholder Text** | The fixed block of text used in **Extracted Text** files to ensure compressibility. | Template text, default text |

---

## Example Dialogue

> **Developer:** "When I run Zipper to create an **Archive**, what files end up in it?"
> 
> **Domain Expert:** "Two things: the **Native Files** (your PDFs, emails, whatever **File Type** you specified) and optionally a **Load File**. The **Load File** is the metadata. If you use `--include-load-file`, the **Load File** gets added to the **Archive** too. Otherwise it's written separately."
>
> **Dev:** "So if I generate 1,000 **Native Files** and distribute them into 10 **Folders**, the **Archive** will have a 10-level **Folder** structure?"
>
> **Expert:** "Exactly. The **Distribution** pattern controls *how* those 1,000 files are assigned. With `proportional`, each **Folder** gets 100. With `gaussian`, the middle **Folders** get more. The **Load File** has a **File Path** column pointing to each file's location."
>
> **Dev:** "What if I use `--with-metadata`?"
>
> **Expert:** "Then the **Load File** gains additional **Columns** like **Custodian**, Author, Date Sent, File Size—all auto-generated. If you're using **Emails**, those always get email **Metadata**: To, From, Subject, Sent Date, whether or not you specify `--with-metadata`."
>
> **Dev:** "And the `--chaos-mode` option?"
>
> **Expert:** "That's for testing. It injects **Anomalies**—deliberate errors in the **Load File**—so you can test whether your ingestion system handles corruption. The **Chaos Engine** tracks every **Anomaly** in `_properties.json` so you know exactly what was broken. Only works in **Loadfile-Only Mode**."

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
   - **Metadata** can mean all columns in the **Load File** (broad definition).
   - But `--with-metadata` adds only specific columns (Custodian, Author, Date Sent, File Size)—not all columns.
   - Use "**Load File** columns" for breadth; "**with-metadata** columns" for the specific flag's columns.

4. **"Encoding" vs "Line Ending"**:
   - **Encoding** is the character set (UTF-8, UTF-16, ANSI) for the entire **Load File**.
   - **Line Ending** is the newline format (CRLF, LF, CR) within a **Load File**.
   - These are independent settings.
