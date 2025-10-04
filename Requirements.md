# Zipper Application Requirements

## 1. Core Purpose

The `zipper` application is a .NET Core command-line tool designed to generate large, highly compressed `.zip` files containing a specified number of placeholder files. It also generates a corresponding load file compatible with import specifications.

## 2. Features

### FR_E-002: Core File Generation
- **REQ_E-008**: The application must generate a user-specified number of files.
- **REQ_E-009**: The application must support generating up to 100 million files.
- **REQ_E-010**: The application must support generating files of type `pdf`, `jpg`, or `tiff`.
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
- **REQ-001**: When `--with-metadata` is specified, the output `.dat` file must include the additional columns: `Custodian`, `Date Sent`, `Author`, and `File Size`.
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
- **REQ-010**: The associated `.dat` load file will contain columns corresponding to the email headers, populated with auto-generated data.
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

## 3. Technical Requirements

- **Framework**: .NET 8.
- **Interface**: Command-line application.
- **Dependencies**: `System.Text.Encoding.CodePages`.
- **Performance**: The application must be designed to stream data directly to the archive without storing intermediate files on disk, minimizing memory and disk space usage.

## 4. Command-Line Arguments

- `--type <pdf|jpg|tiff|eml>`: (Required) The type of file to generate.
- `--count <number>`: (Required) The total number of files to generate.
- `--output-path <directory>`: (Required) The directory where the output `.zip` and `.dat` files will be saved.
- `--folders <number>`: (Optional) The number of folders to distribute files into. Defaults to 1. Must be between 1 and 100.
- `--encoding <UTF-8|UTF-16|ANSI>`: (Optional) The text encoding for the load file. Defaults to UTF-8. `ANSI` corresponds to the Windows-1252 code page.
- `--distribution <proportional|gaussian|exponential>`: (Optional) The distribution pattern for files across folders. Defaults to `proportional`.
- `--with-metadata`: (Optional) Generates a load file with additional metadata columns (Custodian, Date Sent, Author, File Size).
- `--with-text`: (Optional) Generates a corresponding extracted text file for each document and adds the path to the load file.
- `--attachment-rate <number>`: (Optional) When type is `eml`, specifies the percentage of emails (0-100) that will receive a random document as an attachment. Defaults to 0.
- `--target-zip-size <size>`: (Optional, Requires --count) Specifies a target size for the final zip file (e.g., 500MB, 10GB).
- `--include-load-file`: (Optional) Includes the generated `.dat` load file in the root of the output `.zip` archive.

## 5. Testing

A test suite is provided to ensure that all command-line options function correctly. The following requirements apply to the test suite:

-   **Cross-Platform Compatibility**: The test suite must be runnable on Windows, macOS, and Linux.
-   **Test Coverage**: The test suite must cover all "sunny day" scenarios for each command-line switch.
-   **Output Verification**: Tests must not only execute the command but also verify the integrity and correctness of the output files (e.g., checking file counts, load file headers, and content structure).
-   **Pre-Commit Check**: The test suite must be run and pass before any code is committed to the repository.
-   **CI/CD Integration**: The test suite will be automatically run by a GitHub Actions workflow on every push and pull request to the `master` branch.

## 6. Pre-Commit Hook

To enforce the pre-commit testing requirement, the repository will include a script to set up a pre-commit hook.

-   **Setup Script**: The repository must include a script (`setup-hook.sh` for Linux/macOS, `setup-hook.bat` for Windows) that installs the pre-commit hook.
-   **Hook Logic**: The pre-commit hook will execute the test suite and abort the commit if any tests fail.
