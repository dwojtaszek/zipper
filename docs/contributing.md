# Contributing to Zipper

Thank you for your interest in contributing to Zipper! This document outlines how to set up your environment, build, run tests, and follow the project's development workflow.

## Prerequisites

- **.NET 10.0 SDK** (or newer)
- **Git**

## Building the Code

To restore dependencies and build the solution:

```bash
dotnet restore zipper.sln
dotnet build zipper.sln
```

To build a release binary:

```bash
dotnet publish -c Release
```

The executable will be located in `src/bin/Release/net10.0/<platform>/publish/`.

## Running Locally

Run directly with arguments using `dotnet run`:

```bash
dotnet run --project src/Zipper.csproj -- --type pdf --count 100 --output-path ./output
```

## Running Tests

Zipper has unit tests, analyzer tests, and end-to-end (E2E) smoke tests.

### Unit & Analyzer Tests

Run unit tests before every commit:

```bash
dotnet test src/Zipper.Tests/Zipper.Tests.csproj
dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj
```

### Formatting Check

Verify formatting matches repository standards:

```bash
dotnet format --verify-no-changes src/
```

### One-line Build, Format & Test Verification

```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj
```

### End-to-End (E2E) Smoke Tests

Build the Release binary first, then run the OS-specific test script:

```bash
dotnet build -c Release

# Linux / macOS:
./tests/run-tests.sh

# Windows:
tests/run-tests.bat
```

## Pre-Commit Hook Setup

To install pre-commit hooks that format code and run unit tests on every commit:

- **Linux / macOS**: `./setup-hook.sh`
- **Windows**: `setup-hook.bat`

## Local Debugging & Troubleshooting

| Issue / Symptom | Possible Cause | Resolution |
|-----------------|----------------|------------|
| `Path traversal detected` when specifying output path | Target directory is outside the repository base directory | Supply a relative path within the workspace (e.g. `./output`) or use a subfolder inside the working directory |
| `dotnet format` fails on `--verify-no-changes` | Code formatting deviates from `.editorconfig` rules | Run `dotnet format src/` locally to auto-fix formatting before committing |
| E2E test script fails with missing release binary | `./tests/run-tests.sh` checks the Release output path | Build the Release configuration first (`dotnet build -c Release`) |
| Pre-commit hook blocks commit during docs-only edits | Staged non-doc files trigger full unit test run | Use `git commit --no-verify` or verify all staged files match `*.md` or `docs/` patterns |
| Custom column profile fails with `InvalidCastException` | JSON generator parameters contain unparsed numeric types | Ensure custom profile generator params use primitive string/numeric values or pass through `ColumnProfileLoader` |

## Architecture & Code Guidelines

Before contributing code changes, please review key design documents:
- **`AGENTS.md`** — Development principles, workflow, and critical rules
- **`docs/architecture.md`** — System structure, architecture invariants, and load-file pipeline design
- **`UBIQUITOUS_LANGUAGE.md`** — Canonical domain terms (must be followed in code, comments, and PRs)
- **`Requirements.md`** — Immutable requirement specifications (`REQ-XXX`, `FR-XXX`)

## Development Workflow

1. Create a feature branch from `main` (`git checkout -b feat/your-feature` or `fix/your-fix`).
2. Write a failing test first (Test-Driven Development).
3. Implement the minimal code required to pass tests.
4. Run formatting check and unit tests.
5. Create a Pull Request with a clear `## Release Notes` section in the PR description.
