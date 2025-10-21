# AI Agent Instructions for Zipper

## Why AGENTS.md?

This file provides dedicated context for AI coding agents working on the Zipper project. It's separate from README.md to give agents focused, actionable information about code style, build processes, and project-specific conventions without overwhelming end-user documentation.

## Supported Agents

- **Claude Code**: Primary AI coding assistant for this project
- **GitHub Copilot**: Code completion and suggestion support
- **Cursor**: AI-powered IDE support
- **Other AI Agents**: Any AI coding agent that can read markdown files

## How to Use AGENTS.md

1. **Read First**: Always start by reading this file before making changes
2. **Follow Commands**: Use the exact build/test commands listed below
3. **Apply Guidelines**: Follow all code style and project conventions
4. **Test Changes**: Run required tests before committing

## Examples

**Adding a new file type:**
1. Update `PlaceholderFiles.cs` with binary template
2. Add file type to CLI argument validation in `Program.cs`
3. Create unit tests in `Zipper.Tests`
4. Update documentation in both README.md and AGENTS.md
5. Run all tests and ensure they pass

**Performance optimization:**
1. Profile existing code with `BenchmarkDotNet`
2. Apply patterns from the "Performance Guidelines" section
3. Use `Span<T>`/`Memory<T>` for zero-allocation operations
4. Update tests to include performance benchmarks
5. Verify no regression in functionality

## Build/Test Commands
- **Build**: `dotnet publish -c Release` (output: `Zipper/bin/Release/net8.0/<platform>/publish/`)
- **Run**: `dotnet run --project Zipper/Zipper.csproj -- [args]`
- **Unit Tests**: `dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj`
- **Single Test**: `dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~TestName"`
- **E2E Tests**: `tests/run-tests.sh` (Linux/macOS) or `tests/run-tests.bat` (Windows)
- **EML Tests**: `tests/test-eml-comprehensive.sh` or `.bat`
- **Stress Tests**: `tests/stress/run-stress-tests.sh` (manual invocation only)

**Example Usage:**
```bash
dotnet run --project Zipper/Zipper.csproj -- --type pdf --count 1000 --output-path ./output --folders 10 --with-metadata
```

## Architecture Overview
- **Main Project**: `Zipper/` - .NET 8.0 CLI tool with Program.cs entry point
- **Test Project**: `Zipper/Zipper.Tests/` - Unit tests for components
- **Key Components**:
  - `Program.cs`: CLI parsing, validation, orchestration
  - `FileDistributionHelper.cs`: File distribution algorithms (Proportional/Gaussian/Exponential)
  - `PlaceholderFiles.cs`: Binary templates for PDF/JPG/TIFF files
  - `EmlGenerator.cs`: Email file generation with attachments
  - `ParallelFileGenerator.cs`: High-performance parallel file generation
  - `MemoryPoolManager.cs`: Memory management for large datasets
- **Output**: ZIP archives with `.dat` load files containing metadata

## Code Style Guidelines
- **C#**: Implicit usings, nullable reference types, file-scoped namespaces
- **Formatting**: Use `var` when type obvious, expression-bodied members, discard unused vars with `_`
- **Naming**: PascalCase for classes/methods, camelCase for locals, UPPER_CASE for constants
- **Error Handling**: CLI validation → exit code 1 with usage text; runtime → descriptive exceptions
- **Imports**: Implicit usings enabled, no explicit using statements needed
- **Conventions**: No databases, stream-based processing, O(1) distribution algorithms
- **Testing**: Dual platform (.bat/.sh) E2E tests required, verify actual output correctness

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

### Testing Requirements

**CRITICAL CONSTITUTIONAL REQUIREMENT**: All tests (Unit and E2E) have to be run before any commit. They must verify the correctness of the output (e.g., file counts, header content in the `.dat` file), not just the successful execution of the command.

#### Mandatory Dual-Platform Testing
**ALL NEW E2E TESTS MUST BE IMPLEMENTED IN BOTH .BAT (WINDOWS) AND .SH (UNIX) FORMATS AND MUST PASS ON BOTH PLATFORMS BEFORE DEPLOYMENT.** This is a non-negotiable requirement per the project constitution.

- **Create both versions**: Every test script MUST have both a Windows batch (.bat) implementation and a Unix shell (.sh) implementation
- **Identical validation**: Both test versions must produce identical validation results
- **CI/CD integration**: Ensure tests pass on both Windows and Linux CI agents before considering any task complete
- **Path handling**: Use appropriate path separators and handling for each platform in test scripts

#### Unit Tests
Unit tests test individual components and classes in isolation. Located in the `Zipper.Tests` project.

```bash
dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj
```

#### End-to-End (E2E) Tests
E2E tests run the compiled CLI application with various arguments and verify the entire workflow. Located in the `/tests` directory.

- **General tests**: `run-tests.bat` (Windows) or `run-tests.sh` (Linux/macOS)
- **EML-specific tests**: `test-eml-comprehensive.bat` (Windows) or `test-eml-comprehensive.sh` (Linux/macOS)

## Versioning

The application's version will follow a scheme of the format `MAJOR.MINOR.BUILD`.
- The `MAJOR.MINOR` version numbers are to be managed manually in a `.version` file located in the root of the repository.
- The `BUILD` number will be automatically generated by the CI/CD pipeline, using the GitHub Actions `run_number`. The full version string will be in the format `<major>.<minor>.<run_number>`.
- The CI/CD pipeline will automatically create a new GitHub Release for each successful `master` branch build.

## Project Conventions

### C# and .NET 8.0+ Conventions

#### Language Features
- **Implicit Usings and Nullable Reference Types**: Project uses implicit usings and nullable reference types (enabled in `.csproj`)
- **Top-Level Statements**: Use for simple programs and entry points (like Program.cs)
- **File-Scoped Namespaces**: All C# files should use file-scoped namespaces (e.g., `namespace Zipper;`)

#### Modern C# Features (8.0+)
- **Pattern Matching**: Use `switch expressions`, `is patterns`, and property patterns for cleaner code
- **Records**: Use for immutable data structures and DTOs
- **Async/Await**: Use async patterns for I/O operations with proper cancellation tokens
- **Span/Memory**: Use for high-performance memory operations without allocations
- **Ranges and Indices**: Use for cleaner array/collection manipulation
- **Using Declarations**: Prefer `using var resource = new Resource()` over traditional using blocks
- **Null Coalescing Assignment**: Use `??=` for null-coalescing assignment patterns
- **Target-Typed `new`**: Use `var list = new List<T>()` syntax

#### Code Style and Formatting
- Use `var` where the type is obvious from the right-hand side
- Prefer expression-bodied members for simple methods and properties
- Use `_` to discard unused variables and `_` prefix for tuple discard values

#### Nullable Reference Types
- Use `!` (null-forgiving operator) only when absolutely certain
- Use `?.` for null-conditional access
- Use `??` for null-coalescing
- Prefer `string?` for nullable string types

#### Async/Await Best Practices
- Always pass `CancellationToken` to async methods when available
- Use `ConfigureAwait(false)` in library code
- Avoid `async void` except for event handlers
- Use `ValueTask<T>` for high-performance scenarios where results may be synchronous

#### Resource Management
- Prefer `using` declarations over `using` blocks
- Implement `IAsyncDisposable` for async resources
- Use `ArrayPool<T>.Shared` for rented arrays
- Properly dispose `IDisposable` and `IAsyncDisposable` resources

#### Performance Guidelines

- **Memory Efficiency**: Use `Span<T>` and `Memory<T>` for zero-allocation operations
- **String Operations**: Use `StringBuilder` for concatenations, `ReadOnlySpan<char>` for parsing
- **Collections**: Use appropriate collection types:
  - `Span<T>`/`Memory<T>` for stack-based operations
  - `ArrayPool<T>` for temporary large arrays
  - `PooledMemoryStream` or custom pooling for frequent allocations
- **LINQ**: Be mindful of allocations in hot paths; consider foreach loops instead
- **Parallel Processing**: Use `Parallel.ForEachAsync` for I/O-bound operations, `Parallel.For` for CPU-bound
- **Cancellation**: Support cancellation tokens throughout async operations

**High-Performance File Generation**
- **Stream-based processing** is essential for large-scale data generation
- **Zero-allocation patterns** using `Span<T>` and `Memory<T>` for critical paths
- **Memory pooling** with `ArrayPool<T>` for temporary buffers
- **Asynchronous streaming** for I/O operations with proper cancellation
- **Parallel processing** with controlled concurrency to avoid resource exhaustion

**Memory Management Best Practices**
- **Prefer stack allocation** over heap allocation where possible
- **Use `ArrayPool<T>.Shared`** for temporary large arrays
- **Implement custom pooling** for frequently allocated objects
- **Avoid `foreach` on collections** that allocate enumerators in hot paths
- **Use `ref struct`** for stack-only types in performance-critical code
- **Dispose resources properly** to prevent memory leaks

**Performance Testing Guidelines**
- **Test with realistic data sizes** (millions of files when applicable)
- **Measure memory allocations** in hot paths
- **Profile both Windows and Linux** performance characteristics
- **Test edge cases** like single files, maximum file counts, and error scenarios
- **Validate performance regression** with automated benchmarks

**Common Performance Anti-Patterns to Avoid**
- **String concatenation in loops** - use `StringBuilder` instead
- **LINQ in hot paths** - use foreach loops instead
- **Unnecessary allocations** - use spans and memory pools
- **Blocking async operations** - use `await` properly
- **Large object allocations** - pool and reuse objects
- **Improper disposal** - ensure all resources are properly disposed

**Specific Performance Patterns for Zipper**
- **ZIP Streaming**: Write directly to ZIP stream without intermediate files
- **File Distribution**: Use O(1) distribution algorithms with pre-calculated parameters
- **Binary Template Handling**: Reuse templates and apply modifications efficiently
- **Text Generation**: Use `StringBuilder` with pre-allocated capacity
- **Parallel Generation**: Balance between CPU cores and I/O bandwidth

**Note on Linting:** The project enforces these rules through `.editorconfig` and Roslyn analyzers. Ensure all new code passes static analysis.

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

The repository uses GitHub Actions to automate tasks with modern practices and security considerations.

### Core Workflows
- **`build-and-test.yml`**: Unifies the build, test, and release process
  - Multi-platform builds (Windows, Linux, macOS)
  - Parallel matrix strategy for efficiency
  - Artifact caching and dependencies
  - Automated release creation on master
  - **MSBuild Best Practices**: Uses `--no-restore` flags to avoid socket issues
- **`code-review.yml`**: Performs automated code reviews
- **`gemini-review.yml`**: Performs automated pull request reviews with AI analysis

### Gemini AI Assistant Workflows
- **`gemini-dispatch.yml`**: Dispatches Gemini commands based on triggers
  - Pull request events, issue comments, and manual dispatch
  - Command extraction and routing
  - Authentication and security considerations
- **`gemini-invoke.yml`**: Invokes Gemini commands with proper context
- **`gemini-triage.yml`**: Issue triage and labeling automation
- **`gemini-scheduled-triage.yml`**: Scheduled issue triage (every 3 hours)
- **`gemini-issue-automated-triage.yml`**: Automated issue labeling on creation

### Security & Best Practices
- **Authentication**: Uses GitHub App tokens with scoped permissions
- **Secret Management**: Proper handling of API keys and secrets
- **Input Validation**: Security-focused input handling in workflows
- **Concurrency**: Proper concurrency groups to prevent race conditions
- **Caching**: Dependency and build artifact caching for performance
- **Error Handling**: Comprehensive error handling and fallback strategies

### Workflow Development Guidelines
- **Security First**: Never use untrusted inputs directly in run commands
- **Environment Variables**: Use env: blocks for GitHub expressions
- **Action Versions**: Pin to specific action versions for reproducibility
- **Timeouts**: Set appropriate timeouts for long-running operations
- **Resource Limits**: Configure appropriate runner resources
- **Testing**: Test workflows with real-world scenarios

### Common Workflow Issues & Solutions
- **MSBuild Socket Errors**: Use `--no-restore` flags on build/test commands
- **Authentication Failures**: Ensure proper API key environment setup
- **Race Conditions**: Use proper concurrency groups and mutexes
- **Timeout Issues**: Configure appropriate timeouts and retry logic

### GitHub CLI Commands for PR Management
For detailed GitHub CLI commands including PR management, review thread handling, and repository operations, see **[GITHUB_CLI_REFERENCE.md](./GITHUB_CLI_REFERENCE.md)**.

**Quick examples:**
- Get PR comments: `gh api repos/:owner/:repo/pulls/:number/comments`
- Get PR reviews: `gh pr view :number --json comments,reviews`
- Manage review threads: See reference document for complete commands

## Code Review Checklist

### 🔴 Critical Security Issues (Must Fix Before Merge)
- [ ] **Input Validation**: All user inputs are properly validated and sanitized
- [ ] **Path Traversal**: File paths are properly validated and sandboxed
- [ ] **Resource Management**: All `IDisposable` and `IAsyncDisposable` resources are properly disposed
- [ ] **Exception Handling**: No sensitive information leaked in exception messages
- [ ] **Memory Safety**: No buffer overflows, null reference issues, or unsafe operations
- [ ] **Injection Attacks**: No SQL injection, command injection, or code injection vulnerabilities

### 🟠 High Priority (Should Fix Before Merge)
- [ ] **Performance**: No memory leaks, unnecessary allocations, or performance bottlenecks
- [ ] **Async/Await**: Proper async patterns with cancellation tokens
- [ ] **Error Handling**: Comprehensive error handling with meaningful error messages
- [ ] **Thread Safety**: Thread-safe operations where concurrent access is possible
- [ ] **Resource Limits**: Appropriate limits on file sizes, memory usage, and operation counts

### 🟡 Medium Priority (Consider for Improvement)
- [ ] **Code Organization**: Proper separation of concerns and single responsibility principle
- [ ] **API Design**: Clean, intuitive APIs with proper parameter validation
- [ ] **Documentation**: Code is well-documented with XML comments where needed
- [ ] **Testing**: Unit tests cover edge cases and error conditions
- [ ] **Maintainability**: Code is readable and follows established patterns

### 🟢 Low Priority (Nice to Have)
- [ ] **Style Guidelines**: Consistent formatting and naming conventions
- [ ] **Optimization**: Micro-optimizations in non-critical paths
- [ ] **Logging**: Appropriate logging levels and structured logging
- [ ] **Configuration**: Configuration is externalized and documented

### Modern C# Best Practices Review
- [ ] **Nullable Reference Types**: All nullable warnings are properly addressed
- [ ] **Pattern Matching**: Used appropriately instead of if-else chains
- [ ] **Records**: Used for immutable data structures
- [ ] **Span/Memory**: Used for zero-allocation operations in performance-critical code
- [ ] **Async/Await**: Proper usage with cancellation tokens and ConfigureAwait
- [ ] **Resource Management**: Using declarations and proper disposal patterns
- [ ] **LINQ**: No allocations in hot paths, appropriate use of methods

### Performance Review Checklist
- [ ] **Memory Allocations**: No unnecessary allocations in hot paths
- [ ] **Array Operations**: Use of `Span<T>` and `Memory<T>` where appropriate
- [ ] **String Operations**: Efficient string handling with `StringBuilder` and spans
- [ ] **Parallel Processing**: Appropriate use of parallel operations
- [ ] **Caching**: Proper caching of expensive operations
- [ ] **Resource Pooling**: Use of object pools for frequently allocated resources

### Testing Review Checklist
- [ ] **Unit Tests**: All public methods have corresponding unit tests
- [ ] **Edge Cases**: Tests cover boundary conditions and error scenarios
- [ ] **Integration Tests**: Tests cover component interactions
- [ ] **Performance Tests**: Critical paths have performance benchmarks
- [ ] **E2E Tests**: Complete workflows are tested end-to-end
- [ ] **Cross-Platform**: Tests run on both Windows and Linux/macOS

## Agent-Specific Behavioral Instructions

### Commit Practices
- When asked to commit, draft the commit message using simple conventional commit format (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`) and commit directly without asking for confirmation.
- Use descriptive commit messages that clearly indicate what was changed and why.

### Testing Requirements
- **CRITICAL**: All tests MUST include verification steps to confirm the correctness of the output, not just successful execution.
- **Test Coverage**: Tests must verify actual output (file counts, header content, zip integrity, etc.) not just command execution success.
- **No feature is considered complete** until tests pass on both Windows and Linux/macOS environments.
- *See the comprehensive Testing Requirements section above for detailed dual-platform testing guidelines.*

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


#### Performance Monitoring & Profiling
- **Benchmark critical paths** using `BenchmarkDotNet`
- **Monitor GC pressure** and allocation rates
- **Track memory usage** for large-scale operations
- **Profile async operations** for proper resource utilization
- **Measure throughput** (files/second, MB/second) for optimization targets

## Code Style
GitHub Actions YAML v2.0: Follow standard conventions

## FAQ

**Q: Can I modify the CLI argument structure?**
A: Yes, but ensure backward compatibility. Update README.md, AGENTS.md, and all test scripts (.bat/.sh) to reflect changes.

**Q: How do I add a new distribution algorithm?**
A: Add the enum value to `DistributionType`, implement in `FileDistributionHelper.cs`, update CLI parsing, and add comprehensive tests.

**Q: What's the difference between AGENTS.md and README.md?**
A: README.md is for end-users, AGENTS.md is for AI agents with technical details, code patterns, and development guidelines.

**Q: Do I need to run tests before every commit?**
A: Yes, absolutely. The constitutional requirement states: "All tests (Unit and E2E) have to be run before any commit."

**Q: Can I use database persistence?**
A: No. The project follows stream-based processing with no database dependencies as per project conventions.

**Q: How should I handle large file generation (millions of files)?**
A: Use the existing memory pooling and streaming patterns. Profile first, then optimize using Span<T>/Memory<T> and parallel processing.

**Q: What if I find a security vulnerability?**
A: Follow the security review checklist immediately. Fix critical issues before any other work, and ensure proper input validation.

## Recent Changes
- **feat/update-agents-md**: Comprehensive enhancement of AGENTS.md with:
  - Modern C# 8.0+ best practices and patterns
  - Comprehensive code review checklist with priority levels
  - Updated GitHub Actions documentation with security best practices
  - Performance optimization guidelines and anti-patterns
  - Enhanced testing and documentation requirements
  - Added agents.md standard compliance (Why AGENTS.md, Supported Agents, Examples, FAQ)
- **002-task-5-improve**: Added GitHub Actions YAML v2.0 + actions/checkout@v3, actions/setup-dotnet@v3, actions/cache@v3, actions/upload-artifact@v4, actions/download-artifact@v4, softprops/action-gh-release@v2