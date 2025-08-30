# AI Agent Instructions for Zipper

## Project Overview
Zipper is a .NET command-line tool for generating large test datasets. It creates zip archives containing placeholder documents (PDF, JPG, TIFF, EML) with corresponding load files for performance testing scenarios.

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
- System.IO.Compression for ZIP handling

## Development Standards
### Documentation
- Update both `README.md` and `Requirements.md` for new features
- Keep documentation in sync with implementation changes

### Version Control
- Use conventional commit messages:
  - `feat(scope):` for new features
  - `fix(scope):` for bug fixes
  - `docs(scope):` for documentation changes
- Always run test suite before committing

## Common Patterns
- File generation uses streaming to handle large datasets
- Distribution helpers are stateless and thread-safe
- Load files use configurable encoding (UTF-8, UTF-16, ANSI)

## Performance Considerations
- Use streaming operations for large file counts
- Avoid memory allocation in tight loops
- Buffer writes to optimize compression
