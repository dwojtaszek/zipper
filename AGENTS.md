"Use 'bd' for task tracking

# AI Agent Instructions for Zipper

## Build/Test Commands
- **Build**: `dotnet publish -c Release` (output: `Zipper/bin/Release/net8.0/<platform>/publish/`)
- **Run**: `dotnet run --project Zipper/Zipper.csproj -- [args]`
- **Unit Tests**: `dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj`
- **Single Test**: `dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~TestName"`
- **E2E Tests**: `tests/run-tests.sh` (Linux/macOS) or `tests/run-tests.bat` (Windows)
- **EML Tests**: `tests/test-eml-comprehensive.sh` or `.bat`
- **Stress Tests**: `tests/stress/run-stress-tests.sh` (manual invocation only)

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

There are two primary types of tests in this project: Unit Tests and End-to-End (E2E) Tests.

#### Unit Tests
Unit tests are designed to test individual components and classes in isolation. They are fast and are located in the `Zipper.Tests` project.

To run all unit tests, execute the following command from the root of the repository:
```bash
dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj
```

#### End-to-End (E2E) Tests
E2E tests run the compiled command-line application with various arguments and verify the entire workflow, from input to the final generated output (`.zip` and `.dat` files). These are located in the `/tests` directory.

- **Windows**: `run-tests.bat`
- **Linux/macOS**: `run-tests.sh`

A more comprehensive EML-specific test suite is also available:
- **Windows**: `tests/test-eml-comprehensive.bat`
- **Linux/macOS**: `tests/test-eml-comprehensive.sh`

**CRITICAL CONSTITUTIONAL REQUIREMENT**: All tests (Unit and E2E) have to be run before any commit. They must verify the correctness of the output (e.g., file counts, header content in the `.dat` file), not just the successful execution of the command. **ALL NEW E2E TESTS MUST BE IMPLEMENTED IN BOTH .BAT (WINDOWS) AND .SH (UNIX) FORMATS AND MUST PASS ON BOTH PLATFORMS BEFORE DEPLOYMENT.** This is a non-negotiable requirement per the project constitution.

## Versioning

The application's version will follow a scheme of the format `MAJOR.MINOR.BUILD`.
- The `MAJOR.MINOR` version numbers are to be managed manually in a `.version` file located in the root of the repository.
- The `BUILD` number will be automatically generated by the CI/CD pipeline, using the GitHub Actions `run_number`. The full version string will be in the format `<major>.<minor>.<run_number>`.
- The CI/CD pipeline will automatically create a new GitHub Release for each successful `master` branch build.

## Project Conventions

### C# and .NET 8.0+ Conventions

- **Implicit Usings and Nullable Reference Types**: The project uses implicit usings and nullable reference types, which are enabled in the `.csproj` file. All new code should adhere to these conventions.
- **Top-Level Statements**: Use top-level statements for simple programs and entry points (like Program.cs).
- **File-Scoped Namespaces**: All C# files should use file-scoped namespaces (e.g., `namespace Zipper;`).
- **Modern C# Features**: Leverage C# 8.0+ features including:
  - **Pattern Matching**: Use `switch expressions`, `is patterns`, and property patterns for cleaner code
  - **Records**: Use for immutable data structures and DTOs
  - **Async/Aawait**: Use async patterns for I/O operations with proper cancellation tokens
  - **Span/Memory**: Use for high-performance memory operations without allocations
  - **Ranges and Indices**: Use for cleaner array/collection manipulation
  - **Using Declarations**: Prefer `using var resource = new Resource()` over traditional using blocks
  - **Null Coalescing Assignment**: Use `??=` for null-coalescing assignment patterns
  - **Target-Typed `new`**: Use `var list = new List<T>()` syntax

#### Code Style and Formatting

- Use `var` where the type is obvious from the right-hand side
- Prefer expression-bodied members for simple methods and properties
- Use `_` to discard unused variables and `_` prefix for tuple discard values
- **Nullable Reference Types**: Always handle nullable warnings appropriately:
  - Use `!` (null-forgiving operator) only when absolutely certain
  - Use `?.` for null-conditional access
  - Use `??` for null-coalescing
  - Prefer `string?` for nullable string types
- **Async/Await Best Practices**:
  - Always pass `CancellationToken` to async methods when available
  - Use `ConfigureAwait(false)` in library code
  - Avoid `async void` except for event handlers
  - Use `ValueTask<T>` for high-performance scenarios where results may be synchronous
- **Resource Management**:
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

### Branch Strategy

#### Feature Development
- **Primary Branch**: `master` - always stable, CI passes
- **Feature Branches**: Create from `master` for each significant feature or refactor
- **Branch Naming**: Use descriptive names like `refactor/security-fixes`, `feature/cli-validation`, `fix/path-traversal`
- **Branch Protection**: `master` branch requires PR review and CI passing

#### Pull Request Process
- **PR Required**: All changes must go through PR except trivial fixes
- **PR Template**: Use clear title and description linking to tasks/issues
- **Code Review**: At least one human review required for non-trivial changes
- **CI Validation**: All tests must pass (unit + E2E on both platforms)
- **Approval Required**: PR must be approved before merge to `master`

#### Code Review Guidelines

##### Review Process
- **Self-Review**: Creator reviews own changes before submission
- **Peer Review**: Another team member reviews using checklist below
- **Automated Review**: AI assistants provide additional review using GitHub Actions
- **Final Check**: Ensure all review comments are addressed or resolved

##### Review Checklist
- **Functionality**: Code works as intended and matches requirements
- **Security**: No security vulnerabilities, proper input validation
- **Performance**: No performance regressions, maintains O(1) algorithms
- **Testing**: Adequate test coverage, tests actually validate functionality
- **Code Style**: Follows project conventions in AGENTS.md
- **Documentation**: Updated README.md/Requirements.md for breaking changes
- **Cross-Platform**: Tests pass on both Windows and Linux/macOS

##### Review Comments Format
- **Critical Issues**: Use `🔴` prefix for must-fix before merge
- **High Priority**: Use `🟠` prefix for should-fix before merge
- **Suggestions**: Use `💡` prefix for optional improvements
- **Questions**: Use `❓` prefix for clarifications needed

#### Merge Strategy
- **Squash Merge**: For feature branches to clean commit history
- **Merge Commit**: Only for hotfixes or when maintaining individual commits is important
- **Post-Merge**: Delete feature branch, update any related tickets/issues

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

#### High-Performance File Generation
- **Stream-based processing** is essential for large-scale data generation
- **Zero-allocation patterns** using `Span<T>` and `Memory<T>` for critical paths
- **Memory pooling** with `ArrayPool<T>` for temporary buffers
- **Asynchronous streaming** for I/O operations with proper cancellation
- **Parallel processing** with controlled concurrency to avoid resource exhaustion

#### Memory Management Best Practices
- **Prefer stack allocation** over heap allocation where possible
- **Use `ArrayPool<T>.Shared`** for temporary large arrays
- **Implement custom pooling** for frequently allocated objects
- **Avoid `foreach` on collections** that allocate enumerators in hot paths
- **Use `ref struct`** for stack-only types in performance-critical code
- **Dispose resources properly** to prevent memory leaks

#### Performance Monitoring & Profiling
- **Benchmark critical paths** using `BenchmarkDotNet`
- **Monitor GC pressure** and allocation rates
- **Track memory usage** for large-scale operations
- **Profile async operations** for proper resource utilization
- **Measure throughput** (files/second, MB/second) for optimization targets

#### Specific Performance Patterns for Zipper
- **ZIP Streaming**: Write directly to ZIP stream without intermediate files
- **File Distribution**: Use O(1) distribution algorithms with pre-calculated parameters
- **Binary Template Handling**: Reuse templates and apply modifications efficiently
- **Text Generation**: Use `StringBuilder` with pre-allocated capacity
- **Parallel Generation**: Balance between CPU cores and I/O bandwidth

#### Performance Testing Guidelines
- **Test with realistic data sizes** (millions of files when applicable)
- **Measure memory allocations** in hot paths
- **Profile both Windows and Linux** performance characteristics
- **Test edge cases** like single files, maximum file counts, and error scenarios
- **Validate performance regression** with automated benchmarks

#### Common Performance Anti-Patterns to Avoid
- **String concatenation in loops** - use `StringBuilder` instead
- **LINQ in hot paths** - use foreach loops instead
- **Unnecessary allocations** - use spans and memory pools
- **Blocking async operations** - use `await` properly
- **Large object allocations** - pool and reuse objects
- **Improper disposal** - ensure all resources are properly disposed

## Code Style
GitHub Actions YAML v2.0: Follow standard conventions


## Landing the Plane (Session Completion)

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd sync
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds

Use 'bd' for task tracking
