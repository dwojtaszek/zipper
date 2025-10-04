# AI Agent Instructions for Zipper

## Project Overview
Zipper is a .NET command-line tool for generating large, structured test datasets for performance testing scenarios. It creates zip archives containing placeholder documents (PDF, JPG, TIFF, EML) and corresponding load files (`.dat`). The tool can distribute files across a specified number of folders using various algorithms and can target a specific final zip archive size by padding files with non-compressible random data.

## Key Architecture Components

- **`Program.cs`**: The main entry point of the application. It contains all CLI argument parsing logic, validation, and orchestrates the generation process by calling the appropriate helper methods. It has two primary generation flows: `GenerateFiles` for document types and `GenerateEmlFiles` for emails.
- **`FileDistributionHelper.cs`**: Implements the logic for distributing files across folders. It supports three algorithms: `Proportional` (round-robin), `Gaussian` (normal distribution), and `Exponential`. The choice of algorithm is controlled by the `--distribution` CLI argument.
- **`PlaceholderFiles.cs`**: Contains the binary templates for minimal, valid placeholder files (JPG, PDF, TIFF). It also provides a static byte array for extracted text content.
- **`EmlFile.cs`**: Contains the logic for generating `.eml` (email) files, including support for headers, body, and adding random file attachments.

## Core Functionality & Logic

### Command-Line Interface (CLI) Arguments
The application is configured via the following command-line arguments:

- **`--type <string>`**: **(Required)** Specifies the file type to generate. Supported values: `pdf`, `jpg`, `tiff`, `eml`.
- **`--count <long>`**: **(Required)** The total number of files to generate.
- **`--output-path <path>`**: **(Required)** The directory where the output zip and `.dat` files will be saved.
- **`--folders <int>`**: The number of folders to distribute files into within the zip archive. (Default: `1`, Max: `100`).
- **`--distribution <string>`**: The algorithm for file distribution. Supported values: `proportional`, `gaussian`, `exponential`. (Default: `proportional`).
- **`--encoding <string>`**: The text encoding for the output `.dat` load file. Supported values: `UTF-8`, `UTF-16`, `ANSI`. (Default: `UTF-8`).
- **`--with-metadata`**: If present, adds metadata columns (Custodian, Date Sent, etc.) to the `.dat` file for `pdf`, `jpg`, and `tiff` types. **Note: This option is currently not supported for `--type eml`**.
- **`--with-text`**: If present, generates a corresponding `.txt` file for each document and links it in the `Extracted Text` field of the `.dat` file. **Note: This option is currently not supported for `--type eml`**.
- **`--attachment-rate <int>`**: For `--type eml` only. An integer percentage (0-100) representing the chance an email will have an attachment. (Default: `0`).
- **`--target-zip-size <size>`**: If specified, pads each file with random, non-compressible data to make the final zip archive approach the target size (e.g., `500MB`, `10GB`). Requires `--count`.
- **`--include-load-file`**: If present, includes the generated `.dat` load file in the root of the output `.zip` archive.

### Data Flow & Generation Logic
1.  **Parsing & Validation**: `Program.cs` parses CLI arguments into a configuration. It performs validation on required arguments, folder counts, and attachment rates.
2.  **Distribution Calculation**: For each file generated (in a loop from 1 to `count`), `FileDistributionHelper.GetFolderNumber()` is called to determine which subfolder (`folder_001`, `folder_002`, etc.) it should be placed in.
3.  **File Generation**:
    - For `pdf`, `jpg`, `tiff`: The binary template from `PlaceholderFiles.cs` is used. If `--target-zip-size` is set, random padding is added.
    - For `eml`: `EmlFile.CreateEmlContent()` is called to construct the email. If the attachment check passes, a random placeholder file is attached.
4.  **ZIP Streaming**: Files are written directly into the `.zip` archive stream without being stored on disk first. This is efficient for large datasets.
5.  **Load File Generation**: A corresponding entry is written to the `.dat` file for each generated document, including its control number, path within the zip, and any requested metadata.

## Development Workflows

### Building and Running
```bash
# Build a self-contained release executable
dotnet publish -c Release
```
The output will be in `Zipper/bin/Release/net8.0/<platform>/publish/`.

For development, run directly using:
```bash
dotnet run --project Zipper/Zipper.csproj -- [arguments]
```
**Example:**
```bash
dotnet run --project Zipper/Zipper.csproj -- --type pdf --count 1000 --output-path ./output --folders 10 --with-metadata
```

### Testing
Use the provided test scripts in the `/tests` directory:
- Windows: `run-tests.bat`
- Linux/macOS: `run-tests.sh`

Tests have to be run before any commit. They must verify the correctness of the output (e.g., file counts, header content in the `.dat` file), not just the successful execution of the command.

## Versioning

The application's version will follow a scheme of the format `MAJOR.MINOR.BUILD`.
- The `MAJOR.MINOR` version numbers are to be managed manually in a `.version` file located in the root of the repository.
- The `BUILD` number will be automatically generated by the CI/CD pipeline, using the GitHub Actions `run_number`. The full version string will be in the format `<major>.<minor>.<run_number>`.
- The CI/CD pipeline will automatically create a new GitHub Release for each successful `master` branch build.

## Project Conventions

### Distribution Implementations
- Distribution algorithms must be O(1) per file.
- Pre-calculate parameters where possible.
- Handle edge cases (e.g., a single folder).
- Validate inputs within `FileDistributionHelper`.

### Error Handling
- CLI validation errors should result in exit code 1 with helpful usage text.
- Runtime errors should throw descriptive exceptions.
- A progress indicator is displayed in the console for long-running operations.

## Dependencies
- .NET 8.0 SDK
- `SixLabors.ImageSharp`: For creating the TIFF placeholder file.
- `System.Text.Encoding.CodePages`: For ANSI encoding support in the load file.
- `System.IO.Compression`: For ZIP archive handling (part of the .NET SDK).

## CI/CD & Automation
- The repository uses GitHub Actions to automate tasks.
- **`build.yml`**: Builds the application.
- **`code-review.yml`**: Performs automated code reviews.
- **`gemini-cli.yml`**: Handles general requests, code changes, and issue resolution. You are invoked via `@gemini-cli` comments.
- **`gemini-dispatch.yml`**: Dispatches Gemini commands.
- **`gemini-invoke.yml`**: Invokes Gemini commands.
- **`gemini-issue-automated-triage.yml`**: Automatically labels new issues.
- **`gemini-issue-scheduled-triage.yml`**: Schedules triage for issues.
- **`gemini-review.yml`**: Performs automated pull request reviews.
- **`gemini-scheduled-triage.yml`**: Schedules triage.
- **`gemini-triage.yml`**: Triage for issues.
- **`test.yml`**: Runs the test suite.

## Agent-Specific Behavioral Instructions
- When asked to commit, draft the commit message using the conventional commit format and commit directly without asking for confirmation.
- When writing tests, ensure they include verification steps to confirm the correctness of the output, not just successful execution.
- Update README.md and Requirements.md for new features. Keep the documentation in sync with implementation changes.
