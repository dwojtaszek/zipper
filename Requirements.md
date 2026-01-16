# Zipper Application Requirements

## 1. Core Purpose

The `zipper` application is a .NET Core command-line tool designed to generate large, highly compressed `.zip` files containing a specified number of placeholder files. It also generates a corresponding load file compatible with import specifications.

## 2. Features

### FR_E-002: Core File Generation
- **REQ_E-008**: The application must generate a user-specified number of files.
- **REQ_E-009**: The application must support generating up to 100 million files.
- **REQ_E-010**: The application must support generating files of type `pdf`, `jpg`, `tiff`, `eml`, `docx`, or `xlsx`.
- **REQ_E-011**: The content for the generated files must be a minimal, valid, and identical placeholder to ensure maximum compression.
- **REQ_E-012**: The application will provide placeholder content internally, without requiring user-supplied template files.

### FR_E-003: Archive Creation
- **REQ_E-013**: The application must output a single `.zip` archive.
- **REQ_E-014**: The compression level must be equivalent to the standard "best compression" (DEFLATE).
- **REQ_E-015**: The system must support archives exceeding 4GB and 65,535 files (Zip64).

### FR_E-004: Load File Generation
- **REQ_E-016**: The application must generate a single `.dat` load file corresponding to the zip archive.
- **REQ_E-017**: The load file must use ASCII character 20 as the column delimiter.
- **REQ_E-018**: The load file must use ASCII character 254 as the quote/text qualifier.
- **REQ_E-019**: The load file must be saved with a user-specifiable encoding (`UTF-8`, `UTF-16`, `ANSI`), defaulting to `UTF-8`.
- **REQ_E-020**: The load file must contain a unique `Control Number` column for each document.
- **REQ_E-021**: The load file must contain a `File Path` column pointing to the file's relative location within the zip archive.

### FR-000: Automatic Metadata Generation
- **REQ-000**: A new optional command-line argument `--with-metadata` shall be introduced.
- **REQ-001**: When `--with-metadata` is specified, the output `.dat` file must include the additional columns: `Custodian`, `Date Sent`, `Author`, and `File Size`. Note: For EML file types, email-specific metadata columns are always included regardless of this flag, as these are intrinsic to email files.
- **REQ-002**: The `Custodian` field shall be linked to the folder structure. The tool will generate a unique custodian name for each folder (e.g., "Custodian 1"). All files within a given folder will be assigned that folder's custodian. If no folders are specified, a single default custodian name will be assigned to all files.
- **REQ-003**: The `Author`, `Date Sent`, and `File Size` fields will be auto-generated with plausible random data without requiring user input.

### FR-001: Integrated Extracted Text
- **REQ-004**: A new optional command-line argument `--with-text` shall be introduced.
- **REQ-005**: When specified, the tool will generate a matching `.txt` file for every document within the zip archive.
- **REQ-006**: The content for each `.txt` file will be a fixed, non-configurable, standard block of internal placeholder text to ensure maximum compressibility.
- **REQ-007**: The `.dat` load file must be updated to include a column `Extracted Text` pointing to the relative path of the corresponding `.txt` file.

### FR-002: Basic Email Generation
- **REQ-008**: The `--type` argument shall be expanded to accept `eml` as a valid file type.
- **REQ-009**: When `--type eml` is specified, the tool will generate `.eml` files with basic, valid headers (To, From, Subject, Sent-Date) and a simple, repetitive text body.
- **REQ-010**: The associated `.dat` load file will contain columns corresponding to the email headers, populated with auto-generated data. For EML files, metadata columns (To, From, Subject, Sent Date, Attachment) are always included regardless of the `--with-metadata` flag, as these are intrinsic to email files.
- **REQ-011**: A new optional argument `--attachment-rate <percentage>` will control what percentage of generated emails have one of the placeholder documents included as a random attachment. Defaults to 0.

### FR_E-005: File Distribution Patterns
- **REQ_E-022**: A new optional command-line argument `--distribution` shall be introduced.
- **REQ_E-023**: The tool shall support three distribution patterns: `proportional`, `gaussian`, and `exponential`.
- **REQ_E-024**: `proportional` shall be the default distribution pattern.
- **REQ_E-025**: `proportional` distribution shall assign files to folders in a round-robin fashion.
- **REQ_E-026**: `gaussian` distribution shall assign files in a bell-curve pattern, with most files concentrated in the middle folders.
- **REQ_E-027**: `exponential` distribution shall assign files in an exponential decay pattern, with most files concentrated in the first few folders.
- **REQ_E-028**: The application must support distributing files into a user-specified number of folders (from 1 to 100), defaulting to 1.

### FR-005: Target Zip Size via In-File Padding
- **REQ-021**: A new optional command-line argument `--target-zip-size <size>` shall be introduced.
- **REQ-022**: The application must exit with a validation error if `--target-zip-size` is specified without the `--count` argument.
- **REQ-023**: When both `--target-zip-size` and `--count` are used, the tool must calculate the amount of uncompressible padding needed for each file to meet the final compressed --target-zip-size.
- **REQ-024**: The tool must append the calculated amount of random (non-compressible) data to each of the count placeholder files' content before they are added to the archive for compression.
- **REQ-025**: The final generated zip file size must fall within a +/- 10% tolerance of the --target-zip-size. This is the final success criterion for the operation.
- **REQ-026**: The application must perform a pre-check to estimate the minimum possible compressed size of the requested files. If this estimated minimum size already exceeds the --target-zip-size, the tool must exit immediately with a clear error message to prevent an impossible task from running.

### FR-006: Inclusive Load File
- **REQ-027**: A new optional command-line argument `--include-load-file` shall be introduced.
- **REQ-028**: When this flag is specified, the generated `.dat` load file must be included in the root of the output `.zip` archive.
- **REQ-029**: When this flag is specified, the `.dat` load file must not be created as a separate file in the output directory.

### FR-007: Multiple Load File Formats
- **REQ-036**: A new optional command-line argument `--load-file-format <format>` shall be introduced.
- **REQ-037**: The tool shall support multiple load file formats as specified in Section 8: `dat` (default), `opt`, `csv`, and `edrm-xml`.
- **REQ-038**: Each format shall conform to industry-standard specifications defined in Section 8 (Load File Format Standards).

### FR-008: Bates Numbering System
- **REQ-039**: New optional command-line arguments for Bates numbering shall be introduced:
  - `--bates-prefix <prefix>`: Prefix for Bates numbering (e.g., "CLIENT001")
  - `--bates-start <number>`: Starting number for Bates numbering. Defaults to 1
  - `--bates-digits <number>`: Number of digits for Bates numbering. Defaults to 8
- **REQ-040**: When Bates numbering is enabled, the load file must include a `Bates Number` column.
- **REQ-041**: Bates numbers shall be formatted as `{PREFIX}{PADDED_NUMBER}` where the number is zero-padded to the specified digit count.
- **REQ-042**: Bates numbers must increment sequentially for each file generated.

### FR-009: Multipage TIFF Support
- **REQ-043**: A new optional command-line argument `--tiff-pages <min-max>` shall be introduced.
- **REQ-044**: The argument accepts a range specification (e.g., "1-20") to define the minimum and maximum page count.
- **REQ-045**: When `--type tiff` is specified with `--tiff-pages`, each TIFF file shall have a random page count within the specified range.
- **REQ-046**: The load file must include a `Page Count` column indicating the number of pages in each TIFF file.
- **REQ-047**: The default page range shall be "1-1" (single page) for backward compatibility.

## 3. Technical Requirements

- **Framework**: .NET 8.
- **Interface**: Command-line application.
- **Dependencies**:
  - `System.Text.Encoding.CodePages` - For ANSI encoding support
  - `SixLabors.ImageSharp` - For TIFF image generation
  - `ClosedXML` - For XLSX spreadsheet generation
  - `DocumentFormat.OpenXml` - For DOCX document generation
  - `System.Drawing.Common` - For image processing
- **Performance**: The application must be designed to stream data directly to the archive without storing intermediate files on disk, minimizing memory and disk space usage.
- **Parallel Processing**: The application must support parallel file generation with configurable worker pools to optimize performance on multi-core systems.
- **Memory Management**: The application must implement object pooling and buffered I/O to reduce garbage collection pressure and improve throughput.
- **Performance Monitoring**: The application must provide real-time progress tracking, performance metrics, and ETA calculations during file generation.

## 4. Command-Line Arguments

> [!NOTE]
> Additional arguments for load file formats and column profiles are defined in Section 9.

- `--type <pdf|jpg|tiff|eml|docx|xlsx>`: (Required) The type of file to generate.
- `--count <number>`: (Required) The total number of files to generate.
- `--output-path <directory>`: (Required) The directory where the output `.zip` and load file will be saved.
- `--folders <number>`: (Optional) The number of folders to distribute files into. Defaults to 1. Must be between 1 and 100.
- `--encoding <UTF-8|UTF-16|ANSI>`: (Optional) The text encoding for the load file. Defaults to UTF-8. `ANSI` corresponds to the Windows-1252 code page.
- `--distribution <proportional|gaussian|exponential>`: (Optional) The distribution pattern for files across folders. Defaults to `proportional`.
- `--with-metadata`: (Optional) Generates a load file with additional metadata columns (Custodian, Date Sent, Author, File Size).
- `--with-text`: (Optional) Generates a corresponding extracted text file for each document and adds the path to the load file.
- `--attachment-rate <number>`: (Optional) When type is `eml`, specifies the percentage of emails (0-100) that will receive a random document as an attachment. Defaults to 0.
- `--target-zip-size <size>`: (Optional, Requires --count) Specifies a target size for the final zip file (e.g., 500MB, 10GB).
- `--include-load-file`: (Optional) Includes the generated load file in the root of the output `.zip` archive.
- `--load-file-format <dat|opt|csv|xml|concordance>`: (Optional) The format of the load file. Defaults to `dat`.
- `--bates-prefix <prefix>`: (Optional) Prefix for Bates numbering.
- `--bates-start <number>`: (Optional) Starting number for Bates numbering. Defaults to 1.
- `--bates-digits <number>`: (Optional) Number of digits for Bates numbering. Defaults to 8.
- `--tiff-pages <min-max>`: (Optional) Page count range for TIFF files (e.g., "1-20"). Defaults to "1-1".

## 5. Testing

A test suite is provided to ensure that all command-line options function correctly. The following requirements apply to the test suite:

-   **Cross-Platform Compatibility**: The test suite must be runnable on Windows, macOS, and Linux.
-   **Test Coverage**: The test suite must cover all "sunny day" scenarios for each command-line switch.
-   **Output Verification**: Tests must not only execute the command but also verify the integrity and correctness of the output files (e.g., checking file counts, load file headers, and content structure).
-   **Pre-Commit Check**: The test suite must be run and pass before any code is committed to the repository.
-   **CI/CD Integration**: The test suite will be automatically run by a GitHub Actions workflow on every push and pull request to the `main` branch.

## 6. Pre-Commit Hook

To enforce the pre-commit testing requirement, the repository will include a script to set up a pre-commit hook.

-   **Setup Script**: The repository must include a script (`setup-hook.sh` for Linux/macOS, `setup-hook.bat` for Windows) that installs the pre-commit hook.
-   **Hook Logic**: The pre-commit hook will execute the test suite and abort the commit if any tests fail.

## 7. Versioning

### FR-007: Semantic Versioning and Release Automation
- **REQ-030**: The application's version will follow a scheme of the format `MAJOR.MINOR.BUILD`.
- **REQ-031**: The `MAJOR.MINOR` version numbers are to be managed manually in a `.version` file located in the root of the repository.
- **REQ-032**: The `BUILD` number will be automatically generated by the CI/CD pipeline, using the GitHub Actions `run_number`. The full version string will be in the format `<major>.<minor>.<run_number>`.
- **REQ-033**: On every push to the `main` branch, the CI/CD pipeline will build the application, embedding the full version string into the assembly as the `InformationalVersion`.
- **REQ-034**: The application must display its full version number on startup.
- **REQ-035**: The CI/CD pipeline will automatically create a new GitHub Release for each successful `main` branch build. The release tag and title will be named according to the full version string (e.g., `v0.17.123`).

---

## 8. Load File Format Standards (E-Discovery Industry Specifications)

This section documents industry-standard load file formats used by major e-discovery platforms. These specifications inform the implementation of the `--load-file-format` feature and ensure broad platform compatibility.

### 8.1 Overview of Load File Types

| Format | Extension | Primary Purpose |
|--------|-----------|----------------|
| Concordance DAT | `.dat` | Metadata and document data |
| Opticon | `.opt` | Image cross-references (page-level) |
| EDRM XML | `.xml` | Vendor-neutral data interchange |

### 8.2 Concordance DAT Format Specification

The Concordance DAT format is the most widely used load file format in e-discovery. It is a delimited text file containing metadata and document information.

#### Structure
- **Encoding**: UTF-8 (preferred), UTF-8 without BOM, ASCII (Windows-1252), or UTF-16
- **Header Row**: First line contains field names (strongly recommended)
- **Records**: Each subsequent line represents a single document
- **Text Qualifier**: Used to enclose field values containing delimiters

#### Standard Delimiters (ASCII Characters)

| Purpose | ASCII Code (Decimal) | Character | Symbol |
|---------|---------------------|-----------|--------|
| Field Separator | 20 | DC4 | ¶ |
| Quote/Text Qualifier | 254 | Latin Small Letter Thorn | þ |
| Newline within field | 174 | Registered Trademark | ® |
| Multi-Value Separator | 59 | Semicolon | ; |
| Nested Value Separator | 92 | Backslash | \ |

> [!NOTE]
> While these are the standard defaults, some platforms allow custom delimiters. When generating DAT files, use the standard delimiters for maximum compatibility.

#### Required Fields
- **Unique Identifier**: Every load file must have a unique document identifier (e.g., `Control Number`, `DOCID`, `Bates Number`)
- **File Path**: Relative path to the file within the archive/production

#### Common Metadata Fields

| Field Name | Purpose | Example |
|------------|---------|---------|
| `DOCID` | Unique document identifier | `ABC001_00001` |
| `BEGBATES` or `BegDoc` | Beginning Bates number | `ABC001_00001` |
| `ENDBATES` or `EndDoc` | Ending Bates number | `ABC001_00005` |
| `BEGATTACH` | Beginning of attachment range | `ABC001_00001` |
| `ENDATTACH` | End of attachment range | `ABC001_00010` |
| `PARENT_DOCID` | Parent document ID (for families) | `ABC001_00001` |
| `ITEMPATH` or `File Path` | Relative path to native file | `NATIVES\DOC001.pdf` |
| `TEXTPATH` | Relative path to extracted text | `TEXT\DOC001.txt` |
| `Custodian` | Document custodian | `John Smith` |
| `DateSent` | Email sent date | `2024-01-15` |
| `Author` | Document author | `Jane Doe` |
| `From` | Email sender | `sender@example.com` |
| `To` | Email recipients | `recipient@example.com` |
| `CC` | Email CC recipients | `cc@example.com` |
| `BCC` | Email BCC recipients | `bcc@example.com` |
| `Subject` | Email subject | `Re: Project Update` |

#### Platform Compatibility Notes

- Header rows strongly recommended but not always mandatory
- Field order is generally flexible
- An identifier field is required for each load
- Column headers in ALL CAPITAL LETTERS recommended for maximum compatibility
- `DOCID` column required with unique values
- Native file path column should be named `ITEMPATH`
- `PARENT_DOCID` required for family relationships
- Date formats may need configuration during import
- Prefer ASCII or UTF-8 encoding
- All paths should be relative to production root

### 8.3 Opticon (OPT) Format Specification

The Opticon format is a page-level load file that links Bates numbers to image file locations, defining document boundaries.

#### Structure
- **Encoding**: ANSI/Western European (Windows-1252) — Unicode NOT supported in most platforms
- **Header Row**: None (no header row)
- **Delimiter**: Comma (`,`) or Tab
- **Records**: One line per image page

#### Column Structure (7 Columns)

| Column | Name | Description | Example |
|--------|------|-------------|---------|
| 1 | Image Key | Page identifier/Bates number | `ABC001_00001` |
| 2 | Volume | Volume identifier (often blank) | `VOL001` or empty |
| 3 | Image Path | Full or relative path to image | `IMAGES\001\ABC001_00001.tif` |
| 4 | Document Break | "Y" if first page of document | `Y` or empty |
| 5 | Folder Break | Usually blank | empty |
| 6 | Box Break | Usually blank | empty |
| 7 | Page Count | Pages in document (first page only) | `5` or empty |

#### Example OPT File
```
ABC001_00001,VOL001,IMAGES\001\ABC001_00001.tif,Y,,,5
ABC001_00002,VOL001,IMAGES\001\ABC001_00002.tif,,,,
ABC001_00003,VOL001,IMAGES\001\ABC001_00003.tif,,,,
ABC001_00004,VOL001,IMAGES\001\ABC001_00004.tif,,,,
ABC001_00005,VOL001,IMAGES\001\ABC001_00005.tif,,,,
ABC001_00006,VOL001,IMAGES\001\ABC001_00006.tif,Y,,,1
```

> [!IMPORTANT]
> The Document Break marker ("Y" in column 4) is critical for defining document boundaries. Incorrect marking will cause document grouping errors during import.

#### Image Format Requirements

- Single-page Group IV TIFF (most widely supported)
- Single-page JPG
- Multi-page PDF (supported by most modern platforms)
- Single-page TIFF preferred for maximum compatibility

### 8.4 EDRM XML Format Specification

The EDRM (Electronic Discovery Reference Model) XML format is a vendor-neutral standard for e-discovery data interchange.

#### Structure
- **Encoding**: UTF-8 (Unicode support)
- **Format**: XML with defined schema (XSD)
- **Current Version**: 1.2 (with v2.0 in development)

#### Key Elements

| Element | Purpose |
|---------|---------|
| `Root` | Document root element |
| `Batch` | Container for a batch of documents |
| `Document` | Individual document record |
| `File` | File reference with type (Native, Image, Text, Redacted) |
| `ExternalFile` | Reference to external file with path, size, hash |
| `InlineContent` | Embedded content within XML |
| `Fields` | Metadata fields container |
| `Tag` | Tagging/coding information |

#### Example EDRM XML
```xml
<?xml version="1.0" encoding="UTF-8"?>
<Root>
  <Batch>
    <Document DocID="ABC001_00001">
      <Files>
        <File FileType="Native">
          <ExternalFile FilePath="NATIVES\DOC001.pdf" FileSize="125678" Hash="abc123..."/>
        </File>
        <File FileType="Text">
          <ExternalFile FilePath="TEXT\DOC001.txt"/>
        </File>
      </Files>
      <Fields>
        <Field Name="Custodian">John Smith</Field>
        <Field Name="Author">Jane Doe</Field>
        <Field Name="DateCreated">2024-01-15T10:30:00Z</Field>
      </Fields>
    </Document>
  </Batch>
</Root>
```

> [!TIP]
> EDRM XML is self-describing, flexible, and extensible, making it ideal for complex data transfers where maximum interoperability is required.

### 8.5 Format Support Summary

| Feature | Support Level |
|---------|---------------|
| DAT Import | Universal |
| OPT Import | Universal |
| EDRM XML | Widely supported |
| UTF-8 DAT | Universal |
| UTF-8 OPT | Limited (ANSI preferred) |
| Multi-page TIFF | Limited |
| Multi-page PDF | Widely supported |

### 8.6 Implementation Requirements for Zipper

Based on the above research, the following requirements apply to the Zipper load file generation feature:

#### FR-010: Extended Load File Format Support

- **REQ-048**: The `--load-file-format` argument shall support the following formats: `dat`, `opt`, `csv`, `edrm-xml`.
- **REQ-049**: DAT format shall use standard Concordance delimiters (ASCII 20, 254, 174) by default.
- **REQ-050**: A new argument `--dat-delimiters <standard|csv>` shall allow switching between standard Concordance delimiters and standard CSV format.
- **REQ-051**: OPT format shall use comma delimiters and ANSI encoding by default.
- **REQ-052**: EDRM-XML format shall generate well-formed XML conforming to EDRM schema version 1.2.

#### FR-011: Multi-Format Output

- **REQ-055**: A new argument `--load-file-formats <format1,format2,...>` (plural) shall allow generating multiple load file formats simultaneously.
- **REQ-056**: When `--load-file-formats` is specified, all requested formats shall be generated to the output directory.

#### FR-012: OPT File Generation

- **REQ-057**: When `--type tiff` or `--type jpg` is used, an OPT file shall be generated automatically alongside the DAT file. Note: PDF files do NOT trigger automatic OPT generation as they are treated as native files, not page-level images.
- **REQ-058**: The OPT file shall correctly mark document breaks for multi-page documents (when `--tiff-pages` is used). For multi-page TIFFs, page-level Bates numbers shall use suffixes (e.g., `ABC001_00001_001`, `ABC001_00001_002`).
- **REQ-059**: OPT files shall use ANSI encoding for maximum platform compatibility.

#### FR-013: Family Relationship Support

- **REQ-060**: A new argument `--with-families` shall generate parent-child document relationships.
- **REQ-061**: When `--with-families` is specified, the load file shall include `BEGATTACH`, `ENDATTACH`, and `PARENT_DOCID` columns.
- **REQ-062**: Email attachments (when using `--attachment-rate`) shall be properly linked as children of their parent email documents.

#### FR-014: Column Profile System

- **REQ-063**: A new argument `--column-profile <name|path>` shall be introduced to specify metadata columns.
- **REQ-064**: The argument shall accept either a built-in profile name or a path to a custom JSON profile file.
- **REQ-065**: Column profiles shall be embedded in the application binary as resources.
- **REQ-066**: If `--column-profile` is not specified, only base columns (Control Number, File Path) shall be included in the load file. The `--with-metadata` flag adds its columns independently.
- **REQ-076**: When both `--column-profile` and `--with-metadata` are specified, the profile columns take precedence, and `--with-metadata` is ignored with a warning.
- **REQ-077**: When `--type eml` is specified, email-intrinsic columns (From, To, CC, Subject, Sent Date) are always included regardless of the column profile or `--with-metadata` flag.
- **REQ-078**: Custom profiles shall have a maximum of 200 columns. Profiles exceeding this limit shall produce a validation error.
- **REQ-079**: Custom profile JSON shall be validated on load. Invalid JSON or missing required fields shall produce a descriptive error message.
- **REQ-080**: The following built-in profiles shall be provided:

| Profile Name | Column Count | Description |
|-------------|--------------|-------------|
| `minimal` | 5 | Basic fields: DOCID, FILEPATH, CUSTODIAN, DATECREATED, FILESIZE |
| `standard` | 25 | Common e-discovery fields |
| `litigation` | 50 | Full litigation support |
| `full` | 150 | Maximum field coverage |

- **REQ-067**: Custom profile files shall follow a JSON schema with the following structure:

```json
{
  "name": "profile-name",
  "description": "Profile description",
  "version": "1.0",
  "fieldNamingConvention": "UPPERCASE|PascalCase|lowercase",
  "settings": {
    "emptyValuePercentage": 15,
    "multiValueDelimiter": ";",
    "dateFormat": "yyyy-MM-dd"
  },
  "dataSources": {
    "custodians": { "count": 25, "distribution": "pareto" },
    "departments": ["Legal", "Finance", "HR"]
  },
  "columns": [
    {
      "name": "FIELDNAME",
      "type": "text|longtext|date|datetime|number|boolean|coded|email|identifier",
      "required": false,
      "emptyPercentage": 10,
      "multiValue": false,
      "dataSource": "custodians",
      "range": { "min": 0, "max": 100 },
      "distribution": "uniform|gaussian|exponential|pareto|weighted"
    }
  ]
}
```

#### FR-015: Data Generation with Distribution Patterns

- **REQ-068**: The application shall support the following distribution patterns for data generation:
  - `uniform`: Equal probability for all values
  - `gaussian`: Bell curve distribution (most values in middle)
  - `exponential`: Most values at low end, few at high end
  - `pareto`: 80/20 rule (few values dominate)
  - `weighted`: Custom weights per value

- **REQ-069**: Data sources (e.g., custodians) shall be pre-generated and reused across documents following the specified distribution pattern.
- **REQ-070**: A new argument `--seed <number>` shall allow reproducible random data generation.
- **REQ-071**: Per-column empty value percentages shall control the frequency of null/empty values.
- **REQ-072**: Multi-value fields shall support configurable value counts with `multiValueCount` range.

#### FR-016: Supported Column Types

- **REQ-073**: The following column types shall be supported:

| Type | Description | Generator |
|------|-------------|-----------|
| `identifier` | Unique document ID | Sequential with prefix |
| `text` | Short text (< 255 chars) | From data source or random |
| `longtext` | Long text (paragraphs) | Lorem-style generation |
| `date` | Date only | Random within range |
| `datetime` | Date and time | Random within range |
| `number` | Numeric value | Random within range |
| `boolean` | True/False value | Based on true percentage |
| `coded` | Value from list | From data source with weights |
| `email` | Email address | Generated pattern |

- **REQ-074**: Boolean fields shall support format options: `YN`, `TrueFalse`, `10`.
- **REQ-075**: Date fields shall support configurable format strings (e.g., `yyyy-MM-dd`, `MM/dd/yyyy`).

---

## 9. Updated Command-Line Arguments

The following arguments are added or modified by the load file and column profile features:

### Load File Arguments

- `--load-file-format <dat|opt|csv|edrm-xml>`: (Optional) Output format for the load file. Defaults to `dat`.
- `--load-file-formats <format1,format2,...>`: (Optional) Generate multiple load file formats simultaneously.
- `--dat-delimiters <standard|csv>`: (Optional) Delimiter style for DAT files. Defaults to `standard` (ASCII 20/254/174).

> [!NOTE]
> When `--load-file-formats` is used with `--include-load-file`, only the primary DAT file is included in the archive. Other formats are written to the output directory.

### Column Profile Arguments

- `--column-profile <name|path>`: (Optional) Built-in profile name or path to custom JSON profile.
- `--seed <number>`: (Optional) Random seed for reproducible data generation.
- `--date-format <format>`: (Optional) Override the date format from the profile.
- `--empty-percentage <0-100>`: (Optional) Override the global empty value percentage.
- `--custodian-count <number>`: (Optional) Override the number of custodians to generate. Maximum: 1000.

### Family Support Arguments

- `--with-families`: (Optional) Generate parent-child document relationships with appropriate columns.

---

## 10. Argument Interaction Rules

This section clarifies behavior when multiple arguments interact:

| Combination | Behavior |
|-------------|----------|
| `--with-metadata` + `--column-profile` | Profile takes precedence; `--with-metadata` ignored with warning |
| `--type eml` + any profile | Email columns (From, To, CC, Subject, Sent Date) always added |
| `--load-file-formats` + `--include-load-file` | Only DAT included in archive; other formats written externally |
| `--distribution` (folder) + profile distribution | These are independent: `--distribution` controls folders, profile controls data values |
| `--encoding` + profile `dateFormat` | Independent: `--encoding` is file encoding, `dateFormat` is value formatting |

