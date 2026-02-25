# AI Agent Instructions for Zipper

**Behavior:** Think step-by-step before coding. Output COMPLETE files (no placeholders/ellipses). Fix root causes, not symptoms. Match existing style. If unsure, ask for clarification – do not assume. Include error handling. Self-review for bugs/security. Suggest incremental changes over massive rewrites.

Use 'bd' for task tracking.

## Commands

```bash
# Restore (run after clone or adding packages)
dotnet restore zipper.sln

# Build
dotnet publish -c Release

# Run
dotnet run --project src/Zipper.csproj -- [args]

# Unit Tests (must pass before commit)
dotnet test src/Zipper.Tests/Zipper.Tests.csproj

# Lint (CI runs this - fix with: dotnet format)
dotnet format --verify-no-changes

# E2E Tests (must pass before push - enforced by git hooks)
./tests/run-tests.sh   # Linux/macOS
tests/run-tests.bat    # Windows
```

---

## Critical Rules

> [!CAUTION]
> **Requirement numbers in Requirements.md are IMMUTABLE.** REQ-XXX and FR-XXX numbers must NEVER be changed or renumbered.

> [!IMPORTANT]
> **CLI Documentation Sync**: When modifying CLI args in `CommandLineValidator.cs`, update:
> 1. `README.md` - Arguments Quick Reference table
> 2. `Requirements.md` - Add new requirements
> 3. This file - if adding major features

**Test Location:** All unit tests go in `src/Zipper.Tests/` (NOT the root `Zipper.Tests/` which is obsolete).

**E2E Tests:** Must verify actual output (file counts, headers, content). All new E2E tests need both `.sh` and `.bat` implementations.

---

## Project Structure

**Solution:** `zipper.sln` (build with `dotnet build zipper.sln`)  
**Warnings as Errors:** Enabled - fix all warnings before commit

| Path | Purpose |
|------|---------|
| `src/Zipper.csproj` | Main application |
| `src/Program.cs` | Entry point, CLI orchestration |
| `src/CommandLineValidator.cs` | CLI parsing and validation |
| `src/ParallelFileGenerator.cs` | Core file generation pipeline |
| `src/LoadfileOnlyGenerator.cs` | Standalone load file generation (DAT/OPT) |
| `src/ChaosEngine.cs` | Chaos anomaly injection engine |
| `src/ChaosAnomaly.cs` | Anomaly record for audit tracking |
| `src/LoadfileAuditWriter.cs` | `_properties.json` audit file writer |
| `src/LoadFiles/` | Load file writers (DAT, OPT, CSV, XML) |
| `src/Profiles/` | Column profile system |
| `src/Zipper.Tests/` | Unit tests |
| `tests/` | E2E test scripts |
| `tests/run-e2e-loadfile.sh` | E2E tests for loadfile-only and chaos |

**For full CLI arguments and options:** See [README.md](README.md)  
**For requirements and specifications:** See [Requirements.md](Requirements.md)

---

## Code Patterns

```csharp
// EML forces sequential processing with attachments
if (request.FileType == "eml" && (request.WithText || request.AttachmentRate > 0))
    request.Concurrency = 1;

// Cross-platform ANSI encoding
System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Caller-owned streams: flush but don't dispose
await writer.FlushAsync();  // NOT: await writer.DisposeAsync();
```

**Performance:** Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths.

**Style:** C# 8.0+, file-scoped namespaces, nullable reference types, switch expressions, pattern matching.

**No Copyright Headers:** Do NOT add copyright, license, or file header comments (e.g. `// <copyright ...>`) to any files. This project does not use file-level copyright headers.
