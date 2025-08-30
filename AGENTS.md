# AI Agent Instructions for Zipper

## Project Overview
Zipper is a .NET command-line tool for generating large test datasets. It creates zip archives containing placeholder documents (PDF, JPG, TIFF, EML) with corresponding load files for performance testing scenarios. It can also target a specific final zip archive size by padding files with non-compressible data.

## Key Architecture Components

### Core Components
- `Program.cs`: Main entry point, handles CLI argument parsing and orchestrates file generation
- `FileDistributionHelper.cs`: Implements file distribution algorithms (proportional, gaussian, exponential)
- `PlaceholderFiles.cs`: Contains template data for generating minimal valid files
- `EmlFile.cs`: Email file generation with optional attachments

### Data Flow
1. CLI arguments → Validation → Configuration object
2. Distribution pattern calculation (`FileDistributionHelper.cs`)
3. File generation and streaming to ZIP archive
4. Load file generation with metadata/text (if requested)

## Development Workflows

### Building and Running
```bash
# Build release version
dotnet publish -c Release
```
Output in `Zipper/bin/Release/net8.0/<platform>/publish/`

For development, use:
```bash
dotnet run --project Zipper/Zipper.csproj -- [arguments]
```

### Testing
Use the test scripts in `/tests`:
- Windows: `run-tests.bat`
- Linux/macOS: `run-tests.sh`

Test requirements:
- Run tests before committing changes
- Tests must verify output correctness, not just execution
- All tests must pass before commit

## Project Conventions

### Distribution Implementations
- Distribution algorithms must be O(1) per file
- Pre-calculate parameters where possible
- Handle edge cases (single folder, small counts)
- Validate inputs within `FileDistributionHelper`

### Error Handling
- CLI validation errors: Exit code 1 with usage help
- Runtime errors: Descriptive exceptions with context
- Progress indication for long-running operations

## Key Integration Points

### File Type Support
- Supported types defined in `PlaceholderFiles.cs`
- Each type requires minimal valid binary template
- Load file format matches industry-standard import tools

### External Dependencies
- .NET 8.0 SDK
- SixLabors.ImageSharp for image manipulation
- System.Text.Encoding.CodePages (for ANSI encoding support)
- System.IO.Compression for ZIP handling

## Development Standards
### Documentation
- **Update `README.md` and `Requirements.md` for new features.** Keep documentation in sync with implementation changes.

### Version Control
- **Use conventional commit messages:**
  - `feat(scope):` for new features
  - `fix(scope):` for bug fixes
  - `docs(scope):` for documentation changes
- **Always run the test suite before committing.**

## CI/CD & Automation
- The repository uses GitHub Actions to automate tasks.
- **`gemini-cli.yml`**: Handles general requests, code changes, and issue resolution. You are invoked via `@gemini-cli` comments.
- **`gemini-pr-review.yml`**: Performs automated pull request reviews.
- **`gemini-issue-automated-triage.yml`**: Automatically labels new issues.

## Agent-Specific Behavioral Instructions
- When asked to commit, draft the commit message using the conventional commit format and commit directly without asking for confirmation.
- When writing tests, ensure they include verification steps to confirm the correctness of the output (e.g., file counts, header content), not just the successful execution of the command.