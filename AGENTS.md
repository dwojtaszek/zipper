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
- **`--with-metadata`**: If present, adds metadata columns (Custodian, Date Sent, Author, File Size) to the `.dat` file for all file types including `eml`.
- **`--with-text`**: If present, generates a corresponding `.txt` file for each document and links it in the `Extracted Text` field of the `.dat` file for all file types including `eml`.
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

**CRITICAL CONSTITUTIONAL REQUIREMENT**: Tests have to be run before any commit. They must verify the correctness of the output (e.g., file counts, header content in the `.dat` file), not just the successful execution of the command. **ALL NEW TESTS MUST BE IMPLEMENTED IN BOTH .BAT (WINDOWS) AND .SH (UNIX) FORMATS AND MUST PASS ON BOTH PLATFORMS BEFORE DEPLOYMENT.** This is a non-negotiable requirement per the project constitution.

## Versioning

The application's version will follow a scheme of the format `MAJOR.MINOR.BUILD`.
- The `MAJOR.MINOR` version numbers are to be managed manually in a `.version` file located in the root of the repository.
- The `BUILD` number will be automatically generated by the CI/CD pipeline, using the GitHub Actions `run_number`. The full version string will be in the format `<major>.<minor>.<run_number>`.
- The CI/CD pipeline will automatically create a new GitHub Release for each successful `master` branch build.

## Project Conventions

### C# and .NET Conventions

- **Implicit Usings and Nullable Reference Types**: The project uses implicit usings and nullable reference types, which are enabled in the `.csproj` file. All new code should adhere to these conventions.
- **Top-Level Statements**: While not extensively used, the project is open to the use of top-level statements for simple programs.
- **File-Scoped Namespaces**: All C# files should use file-scoped namespaces (e.g., `namespace Zipper;`).

#### Code Style and Formatting

- Use `var` where the type is obvious.
- Prefer expression-bodied members for simple methods and properties.
- Use `_` to discard unused variables.

**Note on Linting:** To automate the enforcement of these rules, consider using a linter like `.editorconfig` or a Roslyn analyzer.

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

### GitHub CLI Commands for PR Management
- **Get PR comments with file locations and line numbers**:
  ```bash
  gh api repos/:owner/:repo/pulls/45/comments --jq '.[] | {path, line, body}'
  ```
  Replace `:owner/:repo` with actual repository (e.g., `dwojtaszek/zipper`) and `45` with the PR number. This command extracts inline comments with their context, making it easier to address specific feedback.

- **Get PR reviews and comments**:
  ```bash
  gh pr view 13 --json comments,reviews --jq '.reviews[].body'
  ```
  Gets all review comments for PR #13.

- **Get inline review comments with file context**:
  ```bash
  gh api repos/dwojtaszek/zipper/pulls/13/comments --jq '.[] | {path, line, body}'
  ```
  Example: Gets inline comments from PR #13 with file paths and line numbers for targeted fixes.

- **Resolve all review threads in a PR (Simple)**:
  ```bash
  # Just change these three values
  owner="dwojtaszek"
  repo="zipper"
  pr=123

  # Get and resolve all review threads
  gh api graphql -f query='query($owner:String!,$repo:String!,$pr:Int!){repository(owner:$owner,name:$repo){pullRequest(number:$pr){reviewThreads(first:100){nodes{id}}}}}' -F owner="$owner" -F repo="$repo" -F pr="$pr" --jq '.data.repository.pullRequest.reviewThreads.nodes[].id' | xargs -I {} gh api graphql -f query='mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{id isResolved}}}' -F id={}
  ```

- **Quick check review threads status**:
  ```bash
  # Replace values and run
  owner="dwojtaszek" repo="zipper" pr=123
  gh api graphql -f query='query($owner:String!,$repo:String!,$pr:Int!){repository(owner:$owner,name:$repo){pullRequest(number:$pr){reviewThreads(first:100){nodes{id isResolved}}}}}' -F owner="$owner" -F repo="$repo" -F pr="$pr" --jq '.data.repository.pullRequest.reviewThreads.nodes[]'
  ```

- **Simple one-liner for current repo**:
  ```bash
  # For dwojtaszek/zipper repo, just change PR number
  pr=123 && gh api graphql -f query='query($pr:Int!){repository(owner:"dwojtaszek",name:"zipper"){pullRequest(number:$pr){reviewThreads(first:100){nodes{id}}}}}' -F pr=$pr --jq '.data.repository.pullRequest.reviewThreads.nodes[].id' | xargs -I {} gh api graphql -f query='mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{id isResolved}}}' -F id={}
  ```

All these commands use the GraphQL API since review threads are only available via GraphQL. Replace the example values with your actual repository owner, repo name, and PR number.

## Agent-Specific Behavioral Instructions

### Commit Practices
- When asked to commit, draft the commit message using simple conventional commit format (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`) and commit directly without asking for confirmation.
- Use descriptive commit messages that clearly indicate what was changed and why.

### Testing Requirements
- **CRITICAL**: All tests MUST include verification steps to confirm the correctness of the output, not just successful execution.
- **MANDATORY Dual Platform Testing**: Create both Windows (.bat) and Unix (.sh) versions of every test script.
- **Test Validation**: Both test versions MUST produce identical validation results and MUST pass on their respective platforms.
- **No feature is considered complete** until tests pass on both Windows and Linux/macOS environments.
- **Test Coverage**: Tests must verify actual output (file counts, header content, zip integrity, etc.) not just command execution success.

### Documentation Synchronization
- **Always update** README.md and Requirements.md after implementing new features or making breaking changes.
- Keep CLI argument documentation in sync with implementation changes.
- Update examples in README.md when adding new functionality or changing behavior.
- Document any new requirements in Requirements.md with proper requirement numbers.
- **Documentation is the source of truth** - maintain accuracy at all times.

### Error Handling Patterns
- **CLI Validation Errors**: Exit with code 1 and display helpful usage text with specific error details.
- **Runtime Errors**: Throw descriptive exceptions with clear error messages and suggested resolutions.
- **Input Validation**: Validate all required arguments before processing, provide clear error messages for missing or invalid values.
- **File System Errors**: Handle file/directory creation failures gracefully with informative messages.
- **Progress Indicators**: Display console progress for long-running operations that exceed 5 seconds.

### Cross-Platform Compliance
- **Mandatory Testing Requirement**: Every test script MUST have both a Windows batch (.bat) implementation and a Unix shell (.sh) implementation.
- **Identical Validation**: Both test versions must produce identical validation results.
- **CI/CD Integration**: Ensure tests pass on both Windows and Linux CI agents before considering any task complete.
- **Path Handling**: Use appropriate path separators and handling for each platform in test scripts.

### Performance Considerations
- Stream-based processing is essential for large-scale data generation.
- Avoid storing intermediate files on disk whenever possible.
- Memory efficiency should be maintained for handling millions of files.

## Code Style
GitHub Actions YAML v2.0: Follow standard conventions

## Recent Changes
- 002-task-5-improve: Added GitHub Actions YAML v2.0 + actions/checkout@v3, actions/setup-dotnet@v3, actions/cache@v3, actions/upload-artifact@v4, actions/download-artifact@v4, softprops/action-gh-release@v2