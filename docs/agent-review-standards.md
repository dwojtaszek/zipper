# Agent review standards

Reviewers own cross-file consistency and deliberate-pattern judgments. Run existing format, analyzer, test, and workflow checks for mechanical contracts; do not replace a deterministic check with another prose rule. Consult [code-review-guidelines.md](code-review-guidelines.md) before reporting project-specific patterns as defects.

## Code Style

- C# 14 (net10.0), file-scoped namespaces, nullable reference types, switch expressions, pattern matching
- Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths
- ZIP entry paths always use `/`; normalize to backslashes with `.Replace('/', '\\')`, never `Replace(Path.DirectorySeparatorChar, '\\')` (a no-op on Windows, where the separator is already `\`)
- **No copyright headers** — do not add `// <copyright ...>` to any files
- **Review guardrails:** Before filing a finding, check `docs/code-review-guidelines.md` for project-specific patterns that look like issues but are deliberate. Applies to human review, robot reviewers (CodeRabbit, Codex), and the autoreview skill.

### Test Naming Convention

Test classes and methods follow the pattern:
- **Class:** `{Subject}Tests` (e.g., `BatesSequenceTests`, `ChaosEngineTests`) or `{Domain}PropertyTests` in `src/Zipper.Tests/Properties/`
- **Method:** `{Method}_{Scenario}_{Expected}` (e.g., `Generate_WithCustomPrefix_ShouldIncludePrefix`)

This convention is enforced by code review and the existing test corpus. All new unit and `[Property]` tests must follow it (see [docs/property-based-testing.md](../docs/property-based-testing.md)).
