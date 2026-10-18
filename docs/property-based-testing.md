# Property-Based Testing Guide for Agents & Contributors

Zipper uses **`FsCheck.Xunit`** (`src/Zipper.Tests/Properties/`) to verify algebraic invariants across randomized, shrinkable domain inputs. Property-based tests complement example-based unit tests (`[Fact]` / `[Theory]`) and run automatically in local hooks, `dotnet test`, and the CI `Property-based test gate & summary` job.

---

## 1. When to Add or Update Property-Based Tests

### Writing New Code

Add or extend a `[Property]` test in `src/Zipper.Tests/Properties/` whenever new code introduces any of the following:

| Code Change Type | Property Pattern to Write |
|------------------|---------------------------|
| **Serializer / Formatter + Parser / Reader** (e.g., `Load File`, `Production Manifest`, `Source Record` CSV, `Bates Number`, ratio specs) | **Round-trip (`parse(serialize(x)) == x`)**: verify that arbitrary valid records/configs survive serialization and parsing without field drift or truncation. |
| **Sanitizer / Normalizer / Naming Converter** (e.g., `PathValidator`, `SourcePathSanitizer`, `NamingConventionHelper`, `ProductionMetadataBudget`) | **Idempotence (`f(f(x)) == f(x)`) & Safety Invariant**: verify that normalizing twice produces the same output as normalizing once, and that unsafe inputs (traversal, control chars, device names) never pass. |
| **Planner / Partitioner / Distribution** (e.g., `Distributions`, `FileTypePlan`, `ProductionSetPlanner`, `ChaosSampler`) | **Conservation & Bounds**: verify that partition sizes sum to total count ($\sum \text{part}_i = N$), indices stay within $[1, \text{Max}]$, and contiguous ranges never overlap or leave unscheduled gaps. |
| **Seeded / Deterministic Pipeline** (e.g., `ChaosEngine`, `DataGenerator`, `FamilyPlan`, `OrderingWindow`) | **Determinism & Replay**: verify that identical seeds and inputs produce byte-for-byte or element-for-element identical outputs across repeated runs. |
| **Validator / Diff / Comparison Engine** (e.g., `ComparisonEngine`, `BatesRangeAnalyzer`, `PostGenerationValidator`) | **Metamorphic & Identity Laws**: verify that comparing a manifest to itself yields zero diffs, controlled mutations change only expected delta buckets, and total record counts are conserved. |

### Updating Existing Code

1. **Modifying a covered domain class**: Run `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Category=Property"` first. If your change alters an invariant or adds a new branch/parameter (e.g., a new `NamingConvention`, `LoadFileFormat`, `ChaosAnomaly` type, or CLI validation rule), update the corresponding generator in `ZipperArbitraries.cs` or the domain `*PropertyTests.cs` suite so the new branch is exercised.
2. **Fixing a bug**: Before fixing the bug, check whether an existing `[Property]` generator missed the failing input shape (for example, an ungenerated delimiter combination or boundary length). Expand the `Arbitrary` generator to include that input domain, plus a deterministic `[Fact]` regression test for the exact minimal counterexample.
3. **When `[Fact]` / `[Theory]` is sufficient instead**: Do not write a `[Property]` test for fixed help-text strings, static constant tables, or single-outcome error messages with no input variation.

---

## 2. Suite Organization & Required Conventions

All property-based test suites live under `src/Zipper.Tests/Properties/`:

| File | Scope |
|------|-------|
| `ZipperArbitraries.cs` | Shared `readonly record struct` inputs and `Arb.From` generators + shrinkers |
| `LoadFileSerializationPropertyTests.cs` | `DatSerializer`, `DatLineParser`, `CsvSerializer`, `OptSerializer`, `ConcordanceSerializer`, `LoadFileRecordBuilder`, `EncodingHelper` |
| `BatesAndDistributionPropertyTests.cs` | `BatesSequence`, `BatesNumber`, `Distributions`, `FileTypePlan`, `FileTypeRatioParser`, `ProductionSetPlanner` |
| `PathAndSourceInputPropertyTests.cs` | `PathValidator`, `NamingConventionHelper`, `SourcePathSanitizer`, `SourceFileTypeMap`, `SourceCsvReader`, `SourceRecordIntake`, `ProductionModule` |
| `ChaosAndComparisonPropertyTests.cs` | `ChaosSampler`, `DatAnomalyApplier`, `OptAnomalyApplier`, `ComparisonEngine`, `BatesRangeAnalyzer`, `ProductionMetadataBudget`, `DataGenerator`, `NumberGenerator`, `TextGenerator` |

### Mandatory Class & Attribute Structure

Every property test class **must** declare:
1. `[Trait("Category", "Property")]` so `--filter "Category=Property"` and CI gates discover it.
2. `[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]` so custom domain arbitraries are registered and each property executes 100 cases per run.
3. Method naming following `{Method}_{Scenario}_Should{Expected}` where `{Method}` matches the production method under test.
4. `[StructLayout(LayoutKind.Auto)]` on every custom `readonly record struct` input type (enforced by Meziantou analyzer `MA0008` with `TreatWarningsAsErrors`).

```csharp
using System.Runtime.InteropServices;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Zipper.Tests.Properties;

[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class ExampleDomainPropertyTests
{
    [Property]
    public Property Parse_SerializedValue_ShouldRoundTrip(BatesPropertyInput input)
    {
        // Arrange & Act against real production types
        var formatted = BatesNumber.Format(input.Prefix, input.StartNumber, input.Digits);
        var parsed = BatesNumber.Parse(formatted);

        // Assert exact behavioral contract
        return (parsed.Prefix == input.Prefix
            && parsed.Number == input.StartNumber
            && parsed.Digits == input.Digits)
            .Label($"Expected round-trip for '{formatted}', got ({parsed.Prefix}, {parsed.Number}, {parsed.Digits})");
    }
}
```

---

## 3. What Looks Good (Recommended Patterns)

### A. Constrained Domain Generators with Shrinkers (`ZipperArbitraries.cs`)

Generate valid-by-construction domain inputs using `Gen.Choose`, `Gen.Elements`, and LINQ comprehensions, and attach a shrinker so counterexamples reduce to minimal failing inputs:

```csharp
[StructLayout(LayoutKind.Auto)]
public readonly record struct BatesPropertyInput(
    string Prefix,
    int Digits,
    long StartNumber,
    int Count);

public static Arbitrary<BatesPropertyInput> BatesInput()
{
    var gen =
        from prefix in PrefixGen
        from digits in Gen.Choose(4, 12)
        from start in Gen.Choose(1, 500_000).Select(i => (long)i)
        from count in Gen.Choose(1, 250)
        select new BatesPropertyInput(prefix, digits, start, count);

    return Arb.From(gen, ShrinkBatesInput);
}
```

### B. Testing Real Production Seams (Never Shadow Helpers)

Always invoke the actual production class. When a production class writes to disk via an interface seam (such as `IFileMaterializer` in `ProductionManifestWriter`), pass a lightweight in-memory implementation of that interface rather than re-implementing the production logic in a private test helper:

```csharp
// GOOD: Calls real ProductionManifestWriter.WriteAsync through IFileMaterializer seam
var materializer = new ManifestCaptureMaterializer();
await ProductionManifestWriter.WriteAsync(
    manifestPath: "/virtual/production_manifest.json",
    request: request,
    result: result,
    materializer: materializer).ConfigureAwait(false);
```

### C. Safe Seed Normalization (`Math.Abs((long)seed)`)

FsCheck's `int` generator and shrinker actively test boundary values including `int.MinValue` (`-2147483648`). Calling `Math.Abs(int.MinValue)` throws `OverflowException`. Always widen to `long` or mask the sign bit before taking the absolute value:

```csharp
// GOOD: Widens to long before Math.Abs so int.MinValue never overflows
int safeSeed = (int)(Math.Abs((long)seed) % 1_000_000L) + 1;
```

### D. Reusing Per-Class Temp Directories for I/O Properties

A `[Property]` method with `MaxTest = 100` runs its body 100 times. If a test requires filesystem paths (such as `PathValidator` symlink checks or `SourceCsvReader` file reads), allocate one canonical temp root in the test class constructor (`IDisposable`) and use unique per-iteration file/sub-directory names under that root:

```csharp
// GOOD: One temp root per test class instance, cleaned up once in Dispose()
public sealed class PathAndSourceInputPropertyTests : IDisposable
{
    private readonly string tempRoot;

    public PathAndSourceInputPropertyTests()
    {
        this.tempRoot = Path.Combine(Path.GetTempPath(), $"zipper-pbt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempRoot))
        {
            Directory.Delete(this.tempRoot, recursive: true);
        }
    }
}
```

### E. Mutation-Killing Assertions

Write assertions that verify exact output values and boundary conditions so Stryker mutants cannot survive:
- Assert both the mutated output **and** the recorded `ChaosAnomaly` metadata (`Type`, `LineNumber`, `Description`).
- Assert exact quote-doubling (`""`) and delimiter display formatting (`ASCII 20` vs literal char) rather than only checking `line.Length > 0`.

---

## 4. What to Avoid (Anti-Patterns & Review Traps)

| Anti-Pattern | Why It Fails | What to Do Instead |
|--------------|--------------|--------------------|
| **Shadow production logic in tests** (copying private normalization/parsing code into a test helper and testing the helper) | Tests pass even if production code is broken or deleted; leaves production lines uncovered. | Call the real public/internal production entry point (using `InternalsVisibleTo` or an existing interface seam like `IFileMaterializer`). |
| **`Math.Abs(seed)` on `int` parameters** | Throws `OverflowException: Negating the minimum value of a twos complement number is invalid` when FsCheck shrinks to `int.MinValue`. | Use `Math.Abs((long)seed)` or `seed & int.MaxValue`. |
| **Creating and deleting temp directories inside the `[Property]` body** | 5 properties × 100 iterations = 500 `Directory.CreateDirectory` + recursive `Directory.Delete` calls per test run, causing I/O contention and slowdowns. | Create a single temp root in the class constructor and clean up once in `Dispose()`. |
| **Unconstrained `string` arbitraries with discard guards (`if (!IsValid(s)) return true;`)** | Most random strings fail strict domain preconditions, turning a 100-case property into 99 no-ops. | Build a constrained `Arbitrary<T>` in `ZipperArbitraries` that generates valid domain structures directly. |
| **Repeating constant checks inside a `[Property]`** (e.g., asserting `PathValidator.IsPathSafe(null)` inside a 100-iteration property) | Wastes 99 iterations re-running a constant expression and spamming `stderr` capture. | Put constant/null guard checks in a standard `[Fact]` or `[Theory]`, and reserve `[Property]` for inputs that vary across generated parameters. |
| **Swallowing unexpected exceptions (`catch (IOException)`) unconditionally** | Masks real filesystem regressions on Linux/macOS where unprivileged symlinks and file operations should succeed. | Only catch platform-specific privilege exceptions when gated on the platform (e.g., `catch (IOException) when (OperatingSystem.IsWindows())` for symlink creation without Developer Mode). |
| **Re-implementing console capture helpers** | Duplicates `ConsoleOutputCapture.RunWithCapturedOutput` and risks leaking `Console.Out`/`Console.Error` state on exceptions. | Use `ConsoleOutputCapture.RunWithCapturedOutput(...)` from `src/Zipper.Tests/TestHelpers/ConsoleOutputCapture.cs`. |
| **Violating Roslyn / Meziantou analyzers in generators** | `TreatWarningsAsErrors` fails the build on `MA0008` (missing `[StructLayout]`), `CA1865`/`CA1870` (`char[]` instead of `SearchValues<char>` or `char` overloads), or `CA5394` (`System.Random`). | Annotate custom structs with `[StructLayout(LayoutKind.Auto)]`, use `SearchValues<char>`, and suppress `CA5394` with a justification when testing seeded deterministic domain APIs (`Distributions`, `ChaosSampler`). |

---

## 5. Verification Checklist Before Committing Property Tests

1. **Run the property suite directly**:
   ```bash
   dotnet test src/Zipper.Tests/Zipper.Tests.csproj -c Release --filter "Category=Property"
   ```
2. **Verify zero warnings and formatting**:
   ```bash
   dotnet build zipper.sln -c Release && dotnet format --verify-no-changes src/
   ```
3. **Check execution time**: Every individual `[Property]` method should complete its 100 cases in **< 500 ms** (`ci.yml` flags slow tests exceeding `0.5s`).
4. **Check CI gate compatibility**: Ensure the class resides in namespace `Zipper.Tests.Properties` and carries `[Trait("Category", "Property")]` so `.github/workflows/ci.yml` counts it in the `Property-based test gate & summary (Linux)` step.
