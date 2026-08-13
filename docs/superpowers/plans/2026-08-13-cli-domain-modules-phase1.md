# CLI Domain Modules — Phase 1 (Leaf Modules) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the four self-contained leaf domains (`HashModule`, `TiffModule`, `DelimiterModule`, `ChaosModule`) out of the `CliParser → CliValidator → RequestBuilder` waterfall so each owns its arg registration, parsing, validation, and sub-config construction, without any behavior or byte-level output change.

**Architecture:** Each domain module holds its own raw flag state, applies tokens during parse, and produces a typed `Zipper.Config` sub-config in `TryBuild`. `CliParser` stays the token reader but dispatches module-owned flags to the owning module instead of populating the flat `ParsedArguments` bag. `Pipeline` assembles the module-produced configs plus the remaining `RequestBuilder` output into `FileGenerationRequest`. Cross-domain checks (chaos↔loadfile, hash↔loadfile, eol↔mode) temporarily read sibling state from the still-present `ParsedArguments`; they move to `CrossCuttingRules` in Phase 4.

Phase 1 **collocates** validate+parse in the module; it does not yet eliminate double interpretation (e.g. `HashModule.TryBuild` still validates the mode string, then `Parse` maps it again). Do not claim "one pass / no double interpretation" in the PR. That is a Phase 4 cleanup.

**Tech Stack:** C# 14 / .NET 10, xUnit, Mermaid (architecture.md), bash E2E + goldens harness.

## Global Constraints

- `FileGenerationRequest` and all 9 sub-config records are the **stable output contract — do not change them** (issue #750).
- Preserve `composer → serializer → emitter` Load File seam (ADR-0007) and the three-mode pipeline (ADR-0006).
- Every intermediate commit must leave the full test suite green — no broken states.
- **Byte-exact output parity** (Critical Rule 6): Phase 1 is a pure logic move; the existing goldens harness (`tests/goldens/run-goldens.sh`, 20 scenarios incl. `custom-delim`, `chaos-dat`, `tiff-multipage`) is the parity gate for delim/tiff/chaos/loadfile-only output. **No golden passes `--hash-mode` / `--hash-algorithms`** — hash regressions are guarded only by `HashModuleTests`. No new harness.
- Error/warning messages move **byte-for-byte** (E2E scripts assert exact strings). Full message inventory in Tasks 2–5.
- `Warnings as Errors` is enabled (`zipper.sln`); run `dotnet format --verify-no-changes src/` after every task.
- Docs sync (Critical Rule 4): Phase 1 changes no CLI behavior or formats → no README/Requirements/UBIQUITOUS_LANGUAGE changes.
- Architecture invariants (Critical Rule 5): the Component Map in `docs/architecture.md` depicts `Program → CliParser → CliValidator → RequestBuilder → FGR`. Phase 1 makes it stale → **same-PR diagram update required** (Task 7). **This plan review is not architecture approval.** The first draft mermaid erased `CliValidator`/`RequestBuilder`; Task 7 now keeps them. Re-review the mermaid after the diagram edit before treating Rule 5 as approved.
- Test coverage must not decrease (Critical Rule 3): removed `CliValidatorTests`/`RequestBuilderTests`/`CliParserTests` tests are **retargeted** (ported to module test files with construction swapped), never deleted without a strict-or-stricter replacement.
- No copyright headers. File-scoped namespaces. Naming: test class `{Subject}Tests`, method `{Method}_{Scenario}_{Expected}`.

## Phase Roadmap (issue #750)

| Phase | Modules | This plan |
|---|---|---|
| 1 (leaf) | Hash, Delimiter, Tiff, Chaos | ✅ full detail |
| 2 (medium) | Bates, Metadata, LoadFile | sketch in §"Phase 2–4" |
| 3 (complex) | Production, Output, SourceInput | sketch |
| 4 (cleanup) | CrossCuttingRules, delete `ParsedArguments`/`RequestBuilder`/`CliValidator`/4 validators | sketch |

Each phase is independently mergeable/revertable; human review at each phase boundary.

---

## File Structure

**Create:**
- `src/Cli/Modules/CliModule.cs` — abstract base: `OwnedFlags`, `TakesValue`, `TryApply`
- `src/Cli/Modules/CliModules.cs` — `CliModuleSet` factory (`All` + typed properties after Task 6) consumed by `CliParser.Parse` and `Pipeline`
- `src/Cli/Modules/HashModule.cs`
- `src/Cli/Modules/TiffModule.cs`
- `src/Cli/Modules/DelimiterModule.cs`
- `src/Cli/Modules/ChaosModule.cs`
- `src/Zipper.Tests/Modules/HashModuleTests.cs` — folder `Modules/`, namespace `Zipper.Tests` (match the rest of the tree)
- `src/Zipper.Tests/Modules/TiffModuleTests.cs`
- `src/Zipper.Tests/Modules/DelimiterModuleTests.cs`
- `src/Zipper.Tests/Modules/ChaosModuleTests.cs`
- `src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs` — shared `RequestBuilderTestHelper.Build` (Task 6; one helper, not copy-pasted)

**Modify:**
- `src/Cli/CliParser.cs` — two-arg `Parse(args, modules)` overload; module dispatch in loop; remove 17 module-owned switch cases + `--chaos-mode` from `ParameterlessFlags`
- `src/Cli/ParsedArguments.cs` — delete 17 properties
- `src/Cli/Pipeline.cs` — create modules, dispatch, `TryBuild` after `CliValidator`, pass configs to `RequestBuilder.Build`
- `src/Cli/RequestBuilder.cs` — new `Build(parsed, delimiters, tiff, chaos, hash)` signature; delete 4 sections + `ParseHashConfig`/`ParseDelimiterArgument`/`ParseStrictDelimiter`
- `src/Cli/CliValidator.cs` — delete `IsValidStrictDelimiter`/`IsValidChaosAmount` delegates
- `src/Cli/Validation/CrossCuttingValidator.cs` — delete `ValidateChaos`/`ValidateDelimiters`/`ValidateHashes`/`ValidateTiffPagesRange`/`ValidateDatDelimiters` + delimiter/chaos helpers; trim `Validate`/`ValidateFormattingAndProfiles` chains
- `src/Cli/Validation/LoadfileOnlyValidator.cs` — delete eol + hash-actual checks
- `docs/architecture.md` — update Component Map CLI Layer (same PR, Rule 5)
- `src/Zipper.Tests/Cli/CliParserTests.cs` — delete 5 module-flag tests; trim `Parse_AllBooleanFlags_SetCorrectly`; route 2 `RequestBuilder.Build` sites through `RequestBuilderTestHelper`
- `src/Zipper.Tests/Cli/CliValidatorTests.cs` — delete ~26 tests whose contracts move to module tests
- `src/Zipper.Tests/Cli/RequestBuilderTests.cs` — delete/port 16 tests; route remaining `Build` calls through `RequestBuilderTestHelper`

---

## Task 1: Module Seam

**Files:**
- Create: `src/Cli/Modules/CliModule.cs`, `src/Cli/Modules/CliModules.cs`
- Modify: `src/Cli/CliParser.cs`

**Interfaces:**
- Produces: `CliModule` (abstract base), `CliModuleSet` + `CliModules.Create()` (empty `All` in this task), `CliParser.Parse(string[] args)` (unchanged signature) and `CliParser.Parse(string[] args, IReadOnlyList<CliModule> modules)` (new).

- [ ] **Step 1: Create the base class and factory**

`src/Cli/Modules/CliModule.cs`:

```csharp
namespace Zipper.Cli.Modules;

/// <summary>
/// A domain-scoped CLI module: owns the flags, argument parsing, validation, and
/// config construction for one sub-domain of FileGenerationRequest.
/// </summary>
public abstract class CliModule
{
    /// <summary>Flag names this module consumes (lowercase, with "--" prefix).</summary>
    public abstract IReadOnlyCollection<string> OwnedFlags { get; }

    /// <summary>Whether the flag consumes a following value token. Parameterless flags override to false.</summary>
    public virtual bool TakesValue(string flag) => true;

    /// <summary>
    /// Applies one flag token. <paramref name="value"/> is null for parameterless flags.
    /// Returns false (after writing to Console.Error) on a hard parse failure.
    /// OwnedFlags and the TryApply switch must stay identical: a silent
    /// <c>default: return false</c> would drop the current
    /// "Error: Unknown argument..." line.
    /// </summary>
    public abstract bool TryApply(string flag, string? value);

    public bool Owns(string flag) => OwnedFlags.Contains(flag);
}
```

`src/Cli/Modules/CliModules.cs`:

```csharp
namespace Zipper.Cli.Modules;

/// <summary>
/// One constructed set of CLI modules. Pipeline must parse and TryBuild against
/// the same instances (TryApply mutates fields). Do not new the modules twice.
/// </summary>
public sealed class CliModuleSet
{
    public required IReadOnlyList<CliModule> All { get; init; }
}

/// <summary>
/// Registry of all CLI modules. Extended one module per phase until every
/// sub-domain of FileGenerationRequest is owned by a module.
/// </summary>
public static class CliModules
{
    public static CliModuleSet Create()
    {
        // Phase 1 task 6 fills this set; empty All here means zero behavior change.
        return new CliModuleSet { All = Array.Empty<CliModule>() };
    }
}
```

- [ ] **Step 2: Add the two-arg `CliParser.Parse` overload with module dispatch**

`src/Cli/CliParser.cs` — replace the single `Parse` method with:

```csharp
public static ParsedArguments? Parse(string[] args) => Parse(args, CliModules.Create().All);

public static ParsedArguments? Parse(string[] args, IReadOnlyList<CliModule> modules)
{
    ArgumentNullException.ThrowIfNull(args);

    var parsed = new ParsedArguments();

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i].ToLowerInvariant();

        var module = modules.FirstOrDefault(m => m.Owns(arg));
        if (module is not null)
        {
            string? value = null;
            if (module.TakesValue(arg))
            {
                if (!TryGetValue(args, i, out value))
                {
                    Console.Error.WriteLine($"Error: {arg} requires a value.");
                    return null;
                }
                i++;
            }

            if (!module.TryApply(arg, value))
            {
                return null;
            }
            continue;
        }

        if (ParameterlessFlags.TryGetValue(arg, out var flagAction))
        {
            flagAction(parsed);
            continue;
        }

        switch (arg)
        {
            // ... existing cases, unchanged for now ...
        }
    }

    return parsed;
}
```

Add `using Zipper.Cli.Modules;` at the top. The switch and `ParameterlessFlags` keep **all** current cases in this task — the empty module list means dispatch is a no-op and behavior is byte-identical.

- [ ] **Step 3: Verify**

Run:
```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj
```
Expected: build green, format clean, all unit tests pass (no test file touched).

- [ ] **Step 4: Commit**

```bash
git add src/Cli/Modules/CliModule.cs src/Cli/Modules/CliModules.cs src/Cli/CliParser.cs
git commit -m "refactor: add domain CLI module seam (CliModule + dispatch overload)"
```

---

## Task 2: HashModule

**Files:**
- Create: `src/Cli/Modules/HashModule.cs`, `src/Zipper.Tests/Modules/HashModuleTests.cs`

**Interfaces:**
- Consumes: `CliModule` base (Task 1).
- Produces: `HashModule` with `TryBuild(ParsedArguments parsed, out HashConfig config) : bool` and `public static HashConfig Parse(string? mode, string? algorithms)`. Writes errors to `Console.Error`.
- Not yet registered in `CliModules.Create()` (registration happens in Task 6).

- [ ] **Step 1: Write the failing test file**

Port these existing tests **verbatim** (swap construction from `CliValidator.Validate(parsed)` / `RequestBuilder.ParseHashConfig(parsed)` / `RequestBuilder.Build(parsed)` to `module.TryApply(...)` + `module.TryBuild(parsed, out var config)`), preserving every assertion. `[Collection("ConsoleTests")]` since failure paths write `Console.Error`.

`src/Zipper.Tests/Modules/HashModuleTests.cs` — outline (full bodies are the existing tests, only construction swapped). **16 tests**, not 17. Folder is `Modules/`; namespace stays `Zipper.Tests` like every other test class. `using Zipper.Config` is required (`HashConfig` lives there; implicit usings do not cover it).

```csharp
using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class HashModuleTests
{
    private static HashModule CreateModule(string? mode = null, string? algorithms = null)
    {
        var module = new HashModule();
        if (mode is not null)
        {
            Assert.True(module.TryApply("--hash-mode", mode));
        }
        if (algorithms is not null)
        {
            Assert.True(module.TryApply("--hash-algorithms", algorithms));
        }
        return module;
    }

    // TryBuild_ValidHashMode_ReturnsTrue          <- CliValidatorTests.Validate_ValidHashMode_ReturnsTrue
    //                                              (Theory: actual/simulated/none/ACTUAL)
    // TryBuild_InvalidHashMode_ReturnsFalse        <- CliValidatorTests.Validate_InvalidHashMode_ReturnsFalse
    // TryBuild_EmptyHashMode_ReturnsFalse          <- CliValidatorTests.Validate_EmptyHashMode_ReturnsFalse
    // TryBuild_ValidHashAlgorithms_ReturnsTrue     <- CliValidatorTests.Validate_ValidHashAlgorithms_ReturnsTrue
    //                                              (Theory: md5 / sha1,sha256 / MD5,SHA256)
    // TryBuild_InvalidHashAlgorithm_ReturnsFalse   <- CliValidatorTests.Validate_InvalidHashAlgorithm_ReturnsFalse
    // TryBuild_EmptyHashAlgorithms_ReturnsFalse    <- CliValidatorTests.Validate_EmptyHashAlgorithms_ReturnsFalse
    // TryBuild_MalformedHashAlgorithms_ReturnsFalse<- CliValidatorTests.Validate_MalformedHashAlgorithms_ReturnsFalse
    // TryBuild_HashAlgorithmsWithoutHashMode_ReturnsFalse <- CliValidatorTests.Validate_HashAlgorithmsWithoutHashMode_ReturnsFalse
    // TryBuild_ActualWithLoadfileOnly_ReturnsFalse <- CliValidatorTests.Validate_HashModeActualWithLoadfileOnly_ReturnsFalse
    //                                              (parsed = new ParsedArguments { FileType = null, LoadfileOnly = true })
    // TryBuild_ActualModeWithAlgorithms_SetsHashConfig  <- RequestBuilderTests.Build_HashModeActualAndAlgorithms_SetsHashConfig
    // TryBuild_SimulatedMode_SetsSimulatedMode     <- RequestBuilderTests.Build_HashModeSimulated_SetsSimulatedMode
    // TryBuild_Default_NoneModeEmptyAlgorithms     <- RequestBuilderTests.Build_HashModeNone_DefaultsToDisabled
    // Parse_ActualMode_ReturnsCorrectConfig        <- RequestBuilderTests.ParseHashConfig_ActualMode_ReturnsCorrectConfig
    // Parse_SimulatedMode_ReturnsDefaultMD5        <- RequestBuilderTests.ParseHashConfig_SimulatedMode_ReturnsSimulatedModeWithDefaultMD5
    // Parse_InvalidMode_DefaultsToNone             <- RequestBuilderTests.ParseHashConfig_InvalidMode_DefaultsToNone
    // Parse_Default_NoneModeEmptyAlgorithms        <- RequestBuilderTests.ParseHashConfig_Default_NoneModeEmptyAlgorithms
}
```

Note the retarget contracts that must be preserved exactly:
- `Validate_EmptyHashMode_ReturnsFalse` uses `HashMode = ""` → module must reject `""` (see Step 2 first check).
- `TryBuild_ActualWithLoadfileOnly_ReturnsFalse` passes `parsed.LoadfileOnly = true`, `FileType = null` — asserts `false`.
- `Parse_SimulatedMode_ReturnsDefaultMD5` asserts `MD5` is auto-added when mode enabled with no algorithms.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~HashModuleTests"`
Expected: FAIL (compilation error — `HashModule` not defined). The task is red.

- [ ] **Step 3: Write HashModule**

`src/Cli/Modules/HashModule.cs` — logic is a byte-for-byte move of `CrossCuttingValidator.ValidateHashMode` + `ValidateHashAlgorithms` + the loadfile-only cross-check + `RequestBuilder.ParseHashConfig`:

```csharp
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns --hash-mode and --hash-algorithms: parse, validate, and build HashConfig.</summary>
public sealed class HashModule : CliModule
{
    private string? _mode;
    private string? _algorithms;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--hash-mode", "--hash-algorithms" };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--hash-mode": _mode = value; return true;
            case "--hash-algorithms": _algorithms = value; return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(ParsedArguments parsed, out HashConfig config)
    {
        if (_mode is not null)
        {
            var mode = _mode.ToLowerInvariant();
            if (mode != "actual" && mode != "simulated" && mode != "none")
            {
                Console.Error.WriteLine($"Error: Invalid --hash-mode '{_mode}'. Supported values: actual, simulated, none.");
                config = default!;
                return false;
            }
        }

        if (_algorithms is not null)
        {
            bool isHashEnabled = _mode is not null &&
                (string.Equals(_mode, "actual", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(_mode, "simulated", StringComparison.OrdinalIgnoreCase));

            if (!isHashEnabled)
            {
                Console.Error.WriteLine("Error: --hash-algorithms requires --hash-mode to be 'actual' or 'simulated'.");
                config = default!;
                return false;
            }

            var algs = _algorithms.Split(',', StringSplitOptions.TrimEntries);
            if (algs.Length == 0 || algs.Any(string.IsNullOrEmpty))
            {
                Console.Error.WriteLine("Error: --hash-algorithms requires at least one valid algorithm (md5, sha1, sha256).");
                config = default!;
                return false;
            }

            foreach (var alg in algs)
            {
                var lowerAlg = alg.ToLowerInvariant();
                if (lowerAlg != "md5" && lowerAlg != "sha1" && lowerAlg != "sha256")
                {
                    Console.Error.WriteLine($"Error: Invalid hash algorithm '{alg}'. Supported values: md5, sha1, sha256.");
                    config = default!;
                    return false;
                }
            }
        }

        // Cross-domain (moves to CrossCuttingRules in Phase 4): reads LoadfileOnly from the
        // still-present bag because LoadFileModule (Phase 2) owns that flag.
        // Keep the LoadfileOnlyValidator bytes (capital E + period). Do not "fix" to the
        // RequestBuilder variant ("error: ... hash)" — no E2E asserts that string).
        if (parsed.LoadfileOnly && string.Equals(_mode, "actual", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --hash-mode actual is not supported with --loadfile-only (no file bytes to hash).");
            config = default!;
            return false;
        }

        config = Parse(_mode, _algorithms);
        return true;
    }

    public static HashConfig Parse(string? mode, string? algorithms)
    {
        var parsedMode = HashMode.None;
        if (!string.IsNullOrEmpty(mode))
        {
            parsedMode = mode.ToLowerInvariant() switch
            {
                "actual" => HashMode.Actual,
                "simulated" => HashMode.Simulated,
                "none" => HashMode.None,
                _ => HashMode.None,
            };
        }

        var parsedAlgorithms = new HashSet<HashAlgorithm>();
        if (!string.IsNullOrEmpty(algorithms))
        {
            foreach (var alg in algorithms.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var parsedAlg = alg.ToLowerInvariant() switch
                {
                    "md5" => HashAlgorithm.MD5,
                    "sha1" => HashAlgorithm.SHA1,
                    "sha256" => HashAlgorithm.SHA256,
                    _ => (HashAlgorithm?)null,
                };
                if (parsedAlg.HasValue)
                {
                    parsedAlgorithms.Add(parsedAlg.Value);
                }
            }
        }

        if (parsedMode != HashMode.None && parsedAlgorithms.Count == 0)
        {
            parsedAlgorithms.Add(HashAlgorithm.MD5);
        }

        return new HashConfig { Mode = parsedMode, Algorithms = parsedAlgorithms };
    }
}
```

`config = default!` is safe on the failure paths: the caller (`Pipeline`) returns null immediately when `TryBuild` is false, and tests only read `config` on success. (`default!` silences the "out param unassigned" definite-assignment error while the record's default property initializers are irrelevant on error paths.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~HashModuleTests"`
Expected: PASS (16 tests). `HashModule` is not registered yet, so the rest of the suite is untouched and green.

- [ ] **Step 5: Commit**

```bash
git add src/Cli/Modules/HashModule.cs src/Zipper.Tests/Modules/HashModuleTests.cs
git commit -m "refactor: extract HashModule (parse+validate+build HashConfig in one pass)"
```

---

## Task 3: TiffModule

**Files:**
- Create: `src/Cli/Modules/TiffModule.cs`, `src/Zipper.Tests/Modules/TiffModuleTests.cs`

**Interfaces:**
- Consumes: `CliModule` base.
- Produces: `TiffModule.TryBuild(ParsedArguments parsed, out TiffConfig config) : bool`.
- Not yet registered.

- [ ] **Step 1: Write the failing test file**

`src/Zipper.Tests/Modules/TiffModuleTests.cs` — namespace `Zipper.Tests`, `using Zipper.Config`:

```csharp
using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class TiffModuleTests
{
    private static TiffConfig Build(string? pageRange)
    {
        var module = new TiffModule();
        if (pageRange is not null)
        {
            Assert.True(module.TryApply("--tiff-pages", pageRange));
        }
        Assert.True(module.TryBuild(new ParsedArguments(), out var config));
        return config;
    }

    [Fact]
    public void TryBuild_ValidRange_SetsPageRange()
    {
        var config = Build("1-20");
        Assert.Equal((1, 20), config.PageRange);
    }

    [Fact]
    public void TryBuild_Default_NullPageRange()
    {
        var config = Build(null);
        Assert.Null(config.PageRange);
    }

    [Fact]
    public void TryBuild_InvalidRange_ReturnsFalse()
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--tiff-pages", "not-a-range"));
        Assert.False(module.TryBuild(new ParsedArguments(), out _));
    }
}
```

Coverage contract: `CrossCuttingValidator.ValidateTiffPagesRange` (invalid range → error, validated pre-build) and `RequestBuilder` `PageRange` assignment (valid range → set, absent → null). Guards the golden scenario `tiff-multipage`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~TiffModuleTests"`
Expected: FAIL (compilation error — `TiffModule` not defined).

- [ ] **Step 3: Write TiffModule**

`src/Cli/Modules/TiffModule.cs` — move of `ValidateTiffPagesRange` + RequestBuilder `Tiff` section:

```csharp
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns --tiff-pages: parse, validate, and build TiffConfig.</summary>
public sealed class TiffModule : CliModule
{
    private string? _pageRange;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--tiff-pages" };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--tiff-pages": _pageRange = value; return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(ParsedArguments parsed, out TiffConfig config)
    {
        _ = parsed; // uniform TryBuild signature; Tiff has no cross-domain reads yet
        if (!string.IsNullOrEmpty(_pageRange) && TiffMultiPageGenerator.ParsePageRange(_pageRange!) is null)
        {
            Console.Error.WriteLine("Error: Invalid TIFF pages range. Use format: <min>-<max> (e.g., 1-20).");
            config = default!;
            return false;
        }

        config = new TiffConfig
        {
            PageRange = !string.IsNullOrEmpty(_pageRange) ? TiffMultiPageGenerator.ParsePageRange(_pageRange!) : null,
        };
        return true;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~TiffModuleTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Cli/Modules/TiffModule.cs src/Zipper.Tests/Modules/TiffModuleTests.cs
git commit -m "refactor: extract TiffModule (parse+validate+build TiffConfig in one pass)"
```

---

## Task 4: DelimiterModule

**Files:**
- Create: `src/Cli/Modules/DelimiterModule.cs`, `src/Zipper.Tests/Modules/DelimiterModuleTests.cs`

**Interfaces:**
- Consumes: `CliModule` base.
- Produces: `DelimiterModule.TryBuild(ParsedArguments parsed, out DelimiterConfig config) : bool`, static `IsValidStrictDelimiter(string)`, static `ParseDelimiterArgument(string)` (throws `ArgumentException`), static `ParseStrictDelimiter(string)` (throws `ArgumentException`).
- Not yet registered.

- [ ] **Step 1: Write the failing test file**

Port these existing tests **verbatim** (construction swapped), `[Collection("ConsoleTests")]`. File lives at `src/Zipper.Tests/Modules/DelimiterModuleTests.cs` with `namespace Zipper.Tests;` and `using Zipper.Config;`.

`src/Zipper.Tests/Modules/DelimiterModuleTests.cs` — mapping (full bodies are the existing tests; module built via `TryApply`):

| New test | Source test | Construction swap |
|---|---|---|
| `TryBuild_DatDelimitersCsv_SetsCommaDelimiters` | `RequestBuilderTests.Build_DelimiterPreset_Csv_SetsCommaDelimiters` | `TryApply("--dat-delimiters", "csv")` then `TryBuild(parsed, out config)`; assert `ColumnDelimiter == ","`, `QuoteDelimiter == "\""` |
| `TryBuild_DelimiterOverride_OverridesPreset` | `RequestBuilderTests.Build_DelimiterOverride_OverridesPreset` | `TryApply("--dat-delimiters", "csv")` + `TryApply("--delimiter-column", "\|")`; drop the `CliValidator.Validate` pre-check (TryBuild is the validator now); assert column `"\|"`, quote `"\""` |
| `TryBuild_StrictDelimiters_OverrideOldDelimiters` | `RequestBuilderTests.Build_StrictDelimiters_OverrideOldDelimiters` | `parsed.LoadfileOnly = true`; `TryApply("--delimiter-column", ",")` + `TryApply("--col-delim", "ascii:20")`; assert `ColumnDelimiter == "\u0014"` |
| `TryBuild_LoadfileOnlyEol_SetsEndOfLine` | `RequestBuilderTests.Build_LoadfileOnly_SetsProperties` (Eol half) | `parsed.LoadfileOnly = true`; `TryApply("--eol", "LF")`; assert `EndOfLine == "LF"` |
| `TryBuild_EolWithProductionSet_ReturnsTrue` | `CliValidatorTests.Validate_EolWithProductionSet_ReturnsTrue` | `parsed.ProductionSet = true`; `TryApply("--eol", "LF")`; assert `TryBuild` true |
| `TryBuild_EolWithoutLoadfileOnlyOrProductionSet_ReturnsFalse` | `CliValidatorTests.Validate_LoadfileOnlyArgs_WithoutLoadfileOnly_ReturnsFalse` | `TryApply("--eol", "LF")`; assert false |
| `TryBuild_InvalidEol_ReturnsFalse` | `CliValidatorTests.Validate_InvalidEol_ReturnsFalse` | `parsed.LoadfileOnly = true`; `TryApply("--eol", "INVALID")`; assert false |
| `TryBuild_ValidEol_ReturnsTrue` | `CliValidatorTests.Validate_ValidEol_ReturnsTrue` | `parsed.LoadfileOnly = true`; loop `CRLF/LF/CR`; assert true each |
| `TryBuild_InvalidStrictDelimiter_ReturnsFalse` | `CliValidatorTests.Validate_InvalidStrictDelimiter_ReturnsFalse` | `parsed.LoadfileOnly = true`; `TryApply("--col-delim", "20")`; assert false |
| `TryBuild_InvalidDatDelimiters_ReturnsFalse` | (new — guards moved `ValidateDatDelimiters`, previously only E2E-covered) | `TryApply("--dat-delimiters", "bogus")`; assert false |
| `ParseDelimiterArgument_ValidInputs_ReturnsCorrectValue` | `RequestBuilderTests.ParseDelimiterArgument_ValidInputs_ReturnsCorrectValue` | `DelimiterModule.ParseDelimiterArgument(input)` (Theory: `\t→tab`, `\n→LF`, `\r→CR`, `20→\u0014`, `254→\u00fe`, `\|→\|`) |
| `ParseDelimiterArgument_Empty_Throws` | `RequestBuilderTests.ParseDelimiterArgument_Empty_Throws` | assert `ArgumentException` |
| `ParseStrictDelimiter_ValidInputs_ReturnsCorrectValue` | `RequestBuilderTests.ParseStrictDelimiter_ValidInputs_ReturnsCorrectValue` | (Theory: `ascii:20→\u0014`, `ascii:254→\u00fe`, `char:;→;`, `char:\|→\|`) |
| `ParseStrictDelimiter_InvalidPrefix_Throws` | `RequestBuilderTests.ParseStrictDelimiter_InvalidPrefix_Throws` | assert `ArgumentException` |
| `IsValidStrictDelimiter_ValidAscii_ReturnsTrue` | `CliValidatorTests.IsValidStrictDelimiter_ValidAscii_ReturnsTrue` | `DelimiterModule.IsValidStrictDelimiter("ascii:20/0/255")` |
| `IsValidStrictDelimiter_InvalidAscii_ReturnsFalse` | `CliValidatorTests.IsValidStrictDelimiter_InvalidAscii_ReturnsFalse` | `ascii:256 / ascii:-1 / ascii:abc` |
| `IsValidStrictDelimiter_ValidChar_ReturnsTrue` | `CliValidatorTests.IsValidStrictDelimiter_ValidChar_ReturnsTrue` | `char:; / char:\|` |
| `IsValidStrictDelimiter_InvalidFormat_ReturnsFalse` | `CliValidatorTests.IsValidStrictDelimiter_InvalidFormat_ReturnsFalse` | `"20" / "" / "ascii:"` |
| `TryApply_DelimiterArgs_ParsesCorrectly` | `CliParserTests.Parse_DelimiterArgs_ParsesCorrectly` + delimiter half of `Parse_LoadfileOnlyArgs_ParsesCorrectly` | apply `--dat-delimiters csv`, `--delimiter-column \|`, `--delimiter-quote ~`, `--delimiter-newline ' '`, then assert raw values round-trip through `TryBuild` config |

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~DelimiterModuleTests"`
Expected: FAIL (compilation error).

- [ ] **Step 3: Write DelimiterModule**

`src/Cli/Modules/DelimiterModule.cs` — move of `CrossCuttingValidator.ValidateDelimiters` + `ValidateDatDelimiters` + `LoadfileOnlyValidator` eol check + `RequestBuilder` delimiter section + `ParseDelimiterArgument`/`ParseStrictDelimiter`/`IsValidStrictDelimiter`:

```csharp
using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the ten delimiter flags: parse, validate, and build DelimiterConfig.</summary>
public sealed class DelimiterModule : CliModule
{
    private string? _datDelimiters;
    private string? _delimiterColumn;
    private string? _delimiterQuote;
    private string? _delimiterNewline;
    private string? _eol;
    private string? _colDelim;
    private string? _quoteDelim;
    private string? _newlineDelim;
    private string? _multiDelim;
    private string? _nestedDelim;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--dat-delimiters", "--delimiter-column", "--delimiter-quote", "--delimiter-newline", "--eol",
        "--col-delim", "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim",
    };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--dat-delimiters": _datDelimiters = value; return true;
            case "--delimiter-column": _delimiterColumn = value; return true;
            case "--delimiter-quote": _delimiterQuote = value; return true;
            case "--delimiter-newline": _delimiterNewline = value; return true;
            case "--eol": _eol = value; return true;
            case "--col-delim": _colDelim = value; return true;
            case "--quote-delim": _quoteDelim = value; return true;
            case "--newline-delim": _newlineDelim = value; return true;
            case "--multi-delim": _multiDelim = value; return true;
            case "--nested-delim": _nestedDelim = value; return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(ParsedArguments parsed, out DelimiterConfig config)
    {
        // Cross-domain (moves to CrossCuttingRules in Phase 4): --eol only with loadfile-only or production-set.
        if (!string.IsNullOrEmpty(_eol) && !parsed.LoadfileOnly && !parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --eol requires --loadfile-only or --production-set.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_eol))
        {
            var isValid = _eol!.ToUpperInvariant() switch
            {
                "CRLF" or "LF" or "CR" => true,
                _ => false,
            };
            if (!isValid)
            {
                Console.Error.WriteLine("Error: --eol must be CRLF, LF, or CR.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_datDelimiters))
        {
            var delim = _datDelimiters.ToLowerInvariant();
            if (delim != "standard" && delim != "csv")
            {
                Console.Error.WriteLine("Error: DAT delimiters must be 'standard' or 'csv'.");
                config = default!;
                return false;
            }
        }

        var sArgs = new[] { _colDelim, _newlineDelim, _multiDelim, _nestedDelim };
        var sNames = new[] { "--col-delim", "--newline-delim", "--multi-delim", "--nested-delim" };
        for (int idx = 0; idx < sArgs.Length; idx++)
        {
            if (!string.IsNullOrEmpty(sArgs[idx]) && !IsValidStrictDelimiter(sArgs[idx]!))
            {
                Console.Error.WriteLine($"Error: {sNames[idx]} must use 'ascii:<N>' or 'char:<c>' prefix.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_quoteDelim) && !_quoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) && !IsValidStrictDelimiter(_quoteDelim))
        {
            Console.Error.WriteLine("Error: --quote-delim must use 'ascii:<N>', 'char:<c>', or 'none'.");
            config = default!;
            return false;
        }

        try
        {
            if (!string.IsNullOrEmpty(_delimiterColumn)) ParseDelimiterArgument(_delimiterColumn!);
            if (!string.IsNullOrEmpty(_delimiterQuote)) ParseDelimiterArgument(_delimiterQuote!);
            if (!string.IsNullOrEmpty(_delimiterNewline)) ParseDelimiterArgument(_delimiterNewline!);
            if (!string.IsNullOrEmpty(_colDelim)) ParseStrictDelimiter(_colDelim!);
            if (!string.IsNullOrEmpty(_quoteDelim)) { if (!_quoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase)) ParseStrictDelimiter(_quoteDelim); }
            if (!string.IsNullOrEmpty(_newlineDelim)) ParseStrictDelimiter(_newlineDelim!);
            if (!string.IsNullOrEmpty(_multiDelim)) ParseStrictDelimiter(_multiDelim!);
            if (!string.IsNullOrEmpty(_nestedDelim)) ParseStrictDelimiter(_nestedDelim!);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            config = default!;
            return false;
        }

        string columnDelim = "\u0014";
        string quoteDelim = "\u00fe";
        string newlineDelim = "\u00ae";

        if (!string.IsNullOrEmpty(_datDelimiters) && _datDelimiters.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            columnDelim = ",";
            quoteDelim = "\"";
            newlineDelim = " ";
        }

        if (!string.IsNullOrEmpty(_delimiterColumn)) columnDelim = ParseDelimiterArgument(_delimiterColumn!);
        if (!string.IsNullOrEmpty(_delimiterQuote)) quoteDelim = ParseDelimiterArgument(_delimiterQuote!);
        if (!string.IsNullOrEmpty(_delimiterNewline)) newlineDelim = ParseDelimiterArgument(_delimiterNewline!);
        if (!string.IsNullOrEmpty(_colDelim)) columnDelim = ParseStrictDelimiter(_colDelim!);
        if (!string.IsNullOrEmpty(_quoteDelim)) quoteDelim = _quoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : ParseStrictDelimiter(_quoteDelim);
        if (!string.IsNullOrEmpty(_newlineDelim)) newlineDelim = ParseStrictDelimiter(_newlineDelim!);

        string multiDelim = ";";
        if (!string.IsNullOrEmpty(_multiDelim)) multiDelim = ParseStrictDelimiter(_multiDelim!);

        string nestedDelim = "\\";
        if (!string.IsNullOrEmpty(_nestedDelim)) nestedDelim = ParseStrictDelimiter(_nestedDelim!);

        config = new DelimiterConfig
        {
            ColumnDelimiter = columnDelim,
            QuoteDelimiter = quoteDelim,
            NewlineDelimiter = newlineDelim,
            MultiValueDelimiter = multiDelim,
            NestedValueDelimiter = nestedDelim,
            EndOfLine = _eol ?? "CRLF",
        };
        return true;
    }

    public static bool IsValidStrictDelimiter(string value) => value switch
    {
        _ when value.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase) =>
            int.TryParse(value.Substring(6), CultureInfo.InvariantCulture, out var code) && code is >= 0 and <= 255,
        _ when value.StartsWith("char:", StringComparison.OrdinalIgnoreCase) => value.Length >= 6,
        _ => false,
    };

    public static string ParseDelimiterArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg)) throw new ArgumentException("Delimiter argument cannot be empty.");
        if (string.Equals(arg, "\\t", StringComparison.Ordinal)) return "\t";
        if (string.Equals(arg, "\\n", StringComparison.Ordinal)) return "\n";
        if (string.Equals(arg, "\\r", StringComparison.Ordinal)) return "\r";
        if (string.Equals(arg, "\\r\\n", StringComparison.Ordinal)) return "\r\n";
        if (int.TryParse(arg, CultureInfo.InvariantCulture, out var asciiCode) && asciiCode >= 0 && asciiCode <= 255) return ((char)asciiCode).ToString();
        if (arg.Length > 1) Console.Error.WriteLine($"Warning: Delimiter argument '{arg}' is longer than 1 character. Using first character: '{arg[0]}'");
        return arg[0].ToString();
    }

    public static string ParseStrictDelimiter(string arg)
    {
        if (arg.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = arg.Substring(6);
            if (int.TryParse(numPart, CultureInfo.InvariantCulture, out var code) && code >= 0 && code <= 255) return ((char)code).ToString();
            throw new ArgumentException($"Invalid ASCII code in delimiter: '{arg}'");
        }
        if (arg.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
        {
            var charPart = arg.Substring(5);
            if (charPart.Length >= 1) return charPart[0].ToString();
            throw new ArgumentException($"Missing character in delimiter: '{arg}'");
        }
        throw new ArgumentException($"Delimiter must use 'ascii:<N>' or 'char:<c>' prefix: '{arg}'");
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~DelimiterModuleTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Cli/Modules/DelimiterModule.cs src/Zipper.Tests/Modules/DelimiterModuleTests.cs
git commit -m "refactor: extract DelimiterModule (parse+validate+build DelimiterConfig in one pass)"
```

---

## Task 5: ChaosModule

**Files:**
- Create: `src/Cli/Modules/ChaosModule.cs`, `src/Zipper.Tests/Modules/ChaosModuleTests.cs`

**Interfaces:**
- Consumes: `CliModule` base; transitively `RequestBuilder.GetLoadFileFormat` (still lives in `RequestBuilder` until LoadFileModule, Phase 2 — documented transitional coupling).
- Produces: `ChaosModule.TryBuild(ParsedArguments parsed, out ChaosConfig config) : bool`, static `IsValidChaosAmount(string) : bool`.
- Not yet registered.

- [ ] **Step 1: Write the failing test file**

Port these existing tests verbatim (construction swapped), `[Collection("ConsoleTests")]`. File lives at `src/Zipper.Tests/Modules/ChaosModuleTests.cs` with `namespace Zipper.Tests;` and `using Zipper.Config;`.

| New test | Source test | Notes |
|---|---|---|
| `TryBuild_ChaosMode_SetsChaosProperties` | `RequestBuilderTests.Build_ChaosMode_SetsChaosProperties` | `parsed.LoadfileOnly = true`; apply `--chaos-mode`, `--chaos-amount 5%`, `--chaos-types quotes,columns`; assert config |
| `TryBuild_ChaosModeWithoutLoadfileOnly_ReturnsFalse` | `CliValidatorTests.Validate_ChaosMode_WithoutLoadfileOnly_ReturnsFalse` | apply `--chaos-mode`; assert false |
| `TryBuild_ChaosAmountWithoutChaosMode_ReturnsFalse` | `CliValidatorTests.Validate_ChaosAmount_WithoutChaosMode_ReturnsFalse` | `parsed.LoadfileOnly = true`; apply `--chaos-amount 5%`; assert false |
| `TryBuild_ChaosTypesWithoutChaosMode_ReturnsFalse` | `CliValidatorTests.Validate_ChaosTypes_WithoutChaosMode_ReturnsFalse` | apply `--chaos-types quotes`; assert false |
| `TryBuild_ChaosScenarioWithoutChaosMode_ReturnsFalse` | `CliValidatorTests.Validate_ChaosScenario_WithoutChaosMode_ReturnsFalse` | apply `--chaos-scenario basic`; assert false |
| `TryBuild_ChaosScenarioWithTypes_ReturnsFalse` | `CliValidatorTests.Validate_ChaosScenarioWithTypes_ReturnsFalse` | `parsed.LoadfileOnly = true`; apply `--chaos-mode`, `--chaos-scenario basic`, `--chaos-types quotes`; assert false |
| `TryBuild_InvalidChaosAmount_ReturnsFalse` | `CliValidatorTests.Validate_InvalidChaosAmount_ReturnsFalse` | `parsed.LoadfileOnly = true`; apply `--chaos-mode`, `--chaos-amount abc` (and `10.5x%`); assert false |
| `IsValidChaosAmount_ValidPercentage_ReturnsTrue` | `CliValidatorTests.IsValidChaosAmount_ValidPercentage_ReturnsTrue` | `1% / 100% / 0.5%` |
| `IsValidChaosAmount_ValidExact_ReturnsTrue` | `CliValidatorTests.IsValidChaosAmount_ValidExact_ReturnsTrue` | `500 / 1` |
| `IsValidChaosAmount_Invalid_ReturnsFalse` | `CliValidatorTests.IsValidChaosAmount_Invalid_ReturnsFalse` | `abc / 0% / -5` |
| `TryBuild_ValidScenario_BuildsConfig` | (new — guards scenario lookup + format match) | `parsed.LoadfileOnly = true`, `parsed.LoadFileFormat = "dat"`; apply `--chaos-mode`, `--chaos-scenario structured-import-failures`; assert true |
| `TryApply_ChaosArgs_ParsesCorrectly` | `CliParserTests.Parse_ChaosArgs_ParsesCorrectly` | apply `--chaos-mode`, `--chaos-amount 5%`, `--chaos-types quotes,columns`, `--chaos-scenario test`. Assert `TryApply` succeeded. Then `TryBuild` with `parsed.LoadfileOnly = true` — rejects unknown scenario `test`. Also keep a success-path assert that raw strings round-trip: apply `--chaos-mode` + `--chaos-amount 5%` + `--chaos-types quotes,columns` (no scenario) and assert `config.ChaosAmount == "5%"` / `config.ChaosTypes == "quotes,columns"` (same contract as `TryBuild_ChaosMode_SetsChaosProperties`; do not drop the storage assert just because `test` is not a real scenario) |

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ChaosModuleTests"`
Expected: FAIL (compilation error).

- [ ] **Step 3: Write ChaosModule**

`src/Cli/Modules/ChaosModule.cs` — move of `CrossCuttingValidator.ValidateChaos` + `IsValidChaosAmount` + RequestBuilder `Chaos` section:

```csharp
using System.Globalization;
using Zipper.Cli;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the four chaos flags: parse, validate, and build ChaosConfig.</summary>
public sealed class ChaosModule : CliModule
{
    private bool _mode;
    private string? _amount;
    private string? _types;
    private string? _scenario;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--chaos-mode", "--chaos-amount", "--chaos-types", "--chaos-scenario" };

    public override bool TakesValue(string flag) => flag != "--chaos-mode";

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--chaos-mode": _mode = true; return true;
            case "--chaos-amount": _amount = value; return true;
            case "--chaos-types": _types = value; return true;
            case "--chaos-scenario": _scenario = value; return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(ParsedArguments parsed, out ChaosConfig config)
    {
        // Transitional: currentFormat comes from RequestBuilder.GetLoadFileFormat until
        // LoadFileModule (Phase 2) owns the format strings; the LoadfileOnly/format reads
        // move to CrossCuttingRules (Phase 4).
        var currentFormat = RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat;

        if (_mode)
        {
            if (!parsed.LoadfileOnly)
            {
                Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only.");
                config = default!;
                return false;
            }

            if (currentFormat != LoadFileFormat.Dat && currentFormat != LoadFileFormat.Opt)
            {
                Console.Error.WriteLine("Error: --chaos-mode is only supported for dat and opt load file formats.");
                config = default!;
                return false;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(_amount))
            {
                Console.Error.WriteLine("Error: --chaos-amount requires --chaos-mode.");
                config = default!;
                return false;
            }
            if (!string.IsNullOrEmpty(_types))
            {
                Console.Error.WriteLine("Error: --chaos-types requires --chaos-mode.");
                config = default!;
                return false;
            }
            if (!string.IsNullOrEmpty(_scenario))
            {
                Console.Error.WriteLine("Error: --chaos-scenario requires --chaos-mode.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_scenario) && !string.IsNullOrEmpty(_types))
        {
            Console.Error.WriteLine("Error: --chaos-scenario conflicts with --chaos-types. Use one or the other.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_scenario))
        {
            var scenario = ChaosScenarios.GetByName(_scenario);
            if (scenario == null)
            {
                Console.Error.WriteLine($"Error: Unknown chaos scenario '{_scenario}'.\n       Available scenarios: {string.Join(", ", ChaosScenarios.ScenarioNames)}");
                config = default!;
                return false;
            }

            if (scenario.RequiredFormat.HasValue && scenario.RequiredFormat.Value != currentFormat)
            {
                Console.Error.WriteLine($"Error: Chaos scenario '{_scenario}' requires --loadfile-format {scenario.RequiredFormat.Value.ToString().ToLowerInvariant()} but got {currentFormat.ToString().ToLowerInvariant()}.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_amount) && !IsValidChaosAmount(_amount))
        {
            Console.Error.WriteLine("Error: --chaos-amount must be a percentage (e.g., '1%') or an exact count (e.g., '500').");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_types))
        {
            var validTypes = new HashSet<string>(ChaosAnomalyTypes.ForFormat(currentFormat), StringComparer.OrdinalIgnoreCase);
            foreach (var t in _types.Split(','))
            {
                if (!validTypes.Contains(t.Trim()))
                {
                    Console.Error.WriteLine($"Error: Invalid chaos type '{t.Trim()}'. Valid types for {currentFormat}: {string.Join(", ", validTypes)}");
                    config = default!;
                    return false;
                }
            }
        }

        config = new ChaosConfig
        {
            ChaosMode = _mode,
            ChaosAmount = _amount,
            ChaosTypes = _types,
            ChaosScenario = _scenario,
        };
        return true;
    }

    public static bool IsValidChaosAmount(string value) => value switch
    {
        _ when value.EndsWith("%", StringComparison.Ordinal) => double.TryParse(value.TrimEnd('%'), CultureInfo.InvariantCulture, out var pct) && pct > 0,
        _ => int.TryParse(value, CultureInfo.InvariantCulture, out var count) && count > 0,
    };
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ChaosModuleTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Cli/Modules/ChaosModule.cs src/Zipper.Tests/Modules/ChaosModuleTests.cs
git commit -m "refactor: extract ChaosModule (parse+validate+build ChaosConfig in one pass)"
```

---

## Task 6: Wire the Modules Into the Pipeline

The seam (Task 1) is live but empty; the four modules exist with full test coverage but are unregistered. This task flips the switch: register modules, delete the flat-bag props + validator blocks + RequestBuilder sections, retarget the old tests, and green the whole suite. All behavioral logic was already moved and verified in Tasks 2–5, so this task is mechanical deletion + wiring.

**Files:**
- Modify: `src/Cli/Modules/CliModules.cs`, `src/Cli/CliParser.cs`, `src/Cli/ParsedArguments.cs`, `src/Cli/Pipeline.cs`, `src/Cli/RequestBuilder.cs`, `src/Cli/CliValidator.cs`, `src/Cli/Validation/CrossCuttingValidator.cs`, `src/Cli/Validation/LoadfileOnlyValidator.cs`
- Create: `src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs`
- Test: `src/Zipper.Tests/Cli/CliParserTests.cs`, `src/Zipper.Tests/Cli/CliValidatorTests.cs`, `src/Zipper.Tests/Cli/RequestBuilderTests.cs`

- [ ] **Step 1: Register the modules (one list, typed bag)**

`src/Cli/Modules/CliModules.cs` — replace the empty Task 1 stub. Pipeline and `Parse(args)` must share this factory so adding a module later cannot leak a flag:

```csharp
public sealed class CliModuleSet
{
    public required DelimiterModule Delimiter { get; init; }
    public required TiffModule Tiff { get; init; }
    public required ChaosModule Chaos { get; init; }
    public required HashModule Hash { get; init; }
    public IReadOnlyList<CliModule> All => new CliModule[] { Delimiter, Tiff, Chaos, Hash };
}

public static class CliModules
{
    public static CliModuleSet Create()
    {
        return new CliModuleSet
        {
            Delimiter = new DelimiterModule(),
            Tiff = new TiffModule(),
            Chaos = new ChaosModule(),
            Hash = new HashModule(),
        };
    }
}
```

`Parse(string[] args)` already uses `CliModules.Create().All` (Task 1). Do **not** also `new` the four modules in `Pipeline`.

- [ ] **Step 2: Delete the 17 `ParsedArguments` properties**

`src/Cli/ParsedArguments.cs` — remove exactly:

```
DatDelimiters, DelimiterColumn, DelimiterQuote, DelimiterNewline,
Eol, ColDelim, QuoteDelim, NewlineDelim, MultiDelim, NestedDelim,
TiffPagesRange, ChaosMode, ChaosAmount, ChaosTypes, ChaosScenario,
HashMode, HashAlgorithms
```

(Keep `LoadFileFormat`/`LoadFileFormats`/`IsLoadFileFormatExplicit` — LoadFileModule is Phase 2.)

- [ ] **Step 3: Remove module-owned cases from `CliParser`**

`src/Cli/CliParser.cs`:
- Remove `["--chaos-mode"] = p => p.ChaosMode = true,` from `ParameterlessFlags`.
- Remove these switch cases: `--dat-delimiters`, `--delimiter-column`, `--delimiter-quote`, `--delimiter-newline`, `--eol`, `--col-delim`, `--quote-delim`, `--newline-delim`, `--multi-delim`, `--nested-delim`, `--tiff-pages`, `--chaos-amount`, `--chaos-types`, `--chaos-scenario`, `--hash-mode`, `--hash-algorithms`.
- `using System.Globalization;` becomes unused — remove it (the only remaining numeric readers are in switch cases that were already int-based? verify: `--count`/`--folders`/`--volume-size` etc. still use `ReadIntArg`/`ReadLongArg`/`CultureInfo`, so **keep the using** — check at edit time and only remove if `CultureInfo` no longer appears).

- [ ] **Step 4: Delete the absorbed validator logic**

`src/Cli/Validation/CrossCuttingValidator.cs`:
- Delete private methods: `ValidateChaos`, `ValidateDelimiters`, `ValidateHashes`, `ValidateHashMode`, `ValidateHashAlgorithms`, `ValidateTiffPagesRange`, `ValidateDatDelimiters`.
- Delete public/internal helpers: `IsValidStrictDelimiter`, `IsValidChaosAmount`, `ParseDelimiterArgument`, `ParseStrictDelimiter`.
- `Validate` chain becomes:
  ```csharp
  return ValidateFileTypeMix(parsed) &&
         ValidateSourceInput(parsed) &&
         ValidateFormattingAndProfiles(parsed) &&
         ValidateBates(parsed);
  ```
- `ValidateFormattingAndProfiles` chain becomes:
  ```csharp
  return ValidateEncodingAndDistribution(parsed) &&
         ValidateLoadFileFormats(parsed) &&
         ValidateColumnProfile(parsed);
  ```
- Keep `ValidateEncodingAndDistribution` (uses `parsed.Encoding`/`parsed.Distribution`, Phase 2/3 domains), `ValidateLoadFileFormats`, `ValidateColumnProfile`, `ValidateFileTypeMix`, `ValidateSourceInput`, `ValidateBates`.

`src/Cli/Validation/LoadfileOnlyValidator.cs`:
- Delete the `--eol` block (moved to `DelimiterModule.TryBuild`).
- Delete the `--hash-mode actual` block (moved to `HashModule.TryBuild`).

`src/Cli/CliValidator.cs`:
- Delete `IsValidStrictDelimiter` and `IsValidChaosAmount` (their only consumers were the retargeted tests).

- [ ] **Step 5: Update `RequestBuilder.Build` to consume module configs**

`src/Cli/RequestBuilder.cs`:
- New signature:
  ```csharp
  public static FileGenerationRequest? Build(
      ParsedArguments parsed,
      DelimiterConfig delimiters,
      TiffConfig tiff,
      ChaosConfig chaos,
      HashConfig hash)
  ```
- Delete the delimiter parsing block (old lines ~140–194: `columnDelim`/`quoteDelim`/`newlineDelim`/`multiDelim`/`nestedDelim` locals).
- Delete the hash block (old lines ~204–209: `var hashConfig = ParseHashConfig(parsed); ...`).
- In the `FileGenerationRequest` initializer, replace:
  ```csharp
  Delimiters = new DelimiterConfig { ... },
  ...
  Tiff = new TiffConfig { PageRange = ... },
  Chaos = new ChaosConfig { ... },
  ...
  Hash = hashConfig,
  ```
  with the passed parameters:
  ```csharp
  Delimiters = delimiters,
  Tiff = tiff,
  Chaos = chaos,
  Hash = hash,
  ```
- Delete methods: `ParseHashConfig`, `ParseDelimiterArgument`, `ParseStrictDelimiter`.
- **Keep** `ParseSize`, `GetDistributionFromName`, `GetEncodingFromName`, `GetLoadFileFormat` (still used by validators and, transitionally, by `ChaosModule`).

- [ ] **Step 6: Update `Pipeline`**

`src/Cli/Pipeline.cs`:

```csharp
using Zipper.Cli.Modules;

namespace Zipper.Cli;

public static class Pipeline
{
    public static FileGenerationRequest? Build(string[] args)
    {
        if (args is null || args.Length is 0)
        {
            HelpTextGenerator.Show();
            return null;
        }

        var modules = CliModules.Create();
        var parsedArgs = CliParser.Parse(args, modules.All);
        if (parsedArgs is null)
        {
            return null;
        }

        if (!CliValidator.Validate(parsedArgs))
        {
            return null;
        }

        if (!modules.Delimiter.TryBuild(parsedArgs, out var delimiters) ||
            !modules.Tiff.TryBuild(parsedArgs, out var tiff) ||
            !modules.Chaos.TryBuild(parsedArgs, out var chaos) ||
            !modules.Hash.TryBuild(parsedArgs, out var hash))
        {
            return null;
        }

        return RequestBuilder.Build(parsedArgs, delimiters, tiff, chaos, hash);
    }
}
```

Same instances that absorbed `TryApply` during parse must run `TryBuild`. `Program` comparison still calls `CliParser.Parse(args)` (throwaway `Create().All`); that path never `TryBuild`s and already skipped hash/chaos/eol validation.

- [ ] **Step 7: Retarget the old tests**

`src/Zipper.Tests/Cli/CliParserTests.cs`:
- `Parse_AllBooleanFlags_SetCorrectly`: remove `--chaos-mode` from the arg array and `Assert.True(result.ChaosMode);` (contract now covered by `ChaosModuleTests`).
- `Parse_LoadfileOnlyArgs_ParsesCorrectly`: strip `--eol`, `--col-delim`, `--quote-delim`, `--multi-delim`, `--nested-delim` args + their assertions; keep `--loadfile-only`/`--count`/`--output-path`/`--loadfile-format opt` + `LoadfileOnly`/`LoadFileFormat` assertions (delimiter contracts now in `DelimiterModuleTests`).
- Delete: `Parse_ChaosArgs_ParsesCorrectly`, `Parse_HashModeArgs_ParsesCorrectly`, `Parse_HashModeOnly_ParsesCorrectly`, `Parse_InvalidHashMode_IsParsedAsString`, `Parse_DelimiterArgs_ParsesCorrectly` (contracts covered by `ChaosModuleTests`/`HashModuleTests`/`DelimiterModuleTests`).
- `Parse_OutputPathWithParentTraversal_RejectsPathOutsideCwd` and `Parse_OutputPathWithinCwd_IsAccepted`: `RequestBuilder.Build(result!)` → `RequestBuilderTestHelper.Build(result!)`. Add `using Zipper.Cli;` if needed.

`src/Zipper.Tests/Cli/CliValidatorTests.cs` — delete these tests (all ported to module test files in Tasks 2–5, same-or-stricter contract):
- `Validate_LoadfileOnlyArgs_WithoutLoadfileOnly_ReturnsFalse`, `Validate_EolWithProductionSet_ReturnsTrue` → DelimiterModuleTests
- `Validate_InvalidEol_ReturnsFalse`, `Validate_ValidEol_ReturnsTrue` → DelimiterModuleTests
- `Validate_InvalidStrictDelimiter_ReturnsFalse` → DelimiterModuleTests
- `IsValidStrictDelimiter_ValidAscii_ReturnsTrue`, `IsValidStrictDelimiter_InvalidAscii_ReturnsFalse`, `IsValidStrictDelimiter_ValidChar_ReturnsTrue`, `IsValidStrictDelimiter_InvalidFormat_ReturnsFalse` → DelimiterModuleTests
- `Validate_ChaosMode_WithoutLoadfileOnly_ReturnsFalse`, `Validate_ChaosAmount_WithoutChaosMode_ReturnsFalse`, `Validate_ChaosTypes_WithoutChaosMode_ReturnsFalse`, `Validate_ChaosScenario_WithoutChaosMode_ReturnsFalse`, `Validate_ChaosScenarioWithTypes_ReturnsFalse`, `Validate_InvalidChaosAmount_ReturnsFalse` → ChaosModuleTests
- `IsValidChaosAmount_ValidPercentage_ReturnsTrue`, `IsValidChaosAmount_ValidExact_ReturnsTrue`, `IsValidChaosAmount_Invalid_ReturnsFalse` → ChaosModuleTests
- `Validate_ValidHashMode_ReturnsTrue`, `Validate_InvalidHashMode_ReturnsFalse`, `Validate_EmptyHashMode_ReturnsFalse`, `Validate_ValidHashAlgorithms_ReturnsTrue`, `Validate_InvalidHashAlgorithm_ReturnsFalse`, `Validate_EmptyHashAlgorithms_ReturnsFalse`, `Validate_MalformedHashAlgorithms_ReturnsFalse`, `Validate_HashAlgorithmsWithoutHashMode_ReturnsFalse`, `Validate_HashModeActualWithLoadfileOnly_ReturnsFalse` → HashModuleTests

Create `src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs` — **one** helper, not copy-pasted into two test classes:

```csharp
using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

internal static class RequestBuilderTestHelper
{
    public static FileGenerationRequest? Build(ParsedArguments parsed)
    {
        var modules = CliModules.Create();
        if (!modules.Delimiter.TryBuild(parsed, out var delimiters) ||
            !modules.Tiff.TryBuild(parsed, out var tiff) ||
            !modules.Chaos.TryBuild(parsed, out var chaos) ||
            !modules.Hash.TryBuild(parsed, out var hash))
        {
            return null;
        }

        return RequestBuilder.Build(parsed, delimiters, tiff, chaos, hash);
    }
}
```

`src/Zipper.Tests/Cli/RequestBuilderTests.cs`:
- Replace every surviving `RequestBuilder.Build(parsed)` with `RequestBuilderTestHelper.Build(parsed)`. Miss one → compile red (`Warnings as Errors`).
- Surviving `Build` sites (keep these tests, swap construction only): `Build_StandardMode_SetsAllDefaults`, `Build_WithValidPath_ResolvesDirectory`, `Build_WithInvalidPath_ReturnsNull`, `Build_LoadfileOnly_SetsProperties`, `Build_ProductionSet_SetsVolumeSize`, `Build_BatesConfig_SetsCorrectly`, `Build_ColumnProfile_LoadsProfile`, `Build_MultiFormat_CreatesFormatList`, `Build_LoadfileOnlyEncoding_UsesExtendedSet`, `Build_Encoding_PreservesNormalizedInputName`.
- `Build_NullArg_ThrowsArgumentNullException` → `RequestBuilder.Build(null!, new DelimiterConfig(), new TiffConfig(), new ChaosConfig(), new HashConfig())` (add `using Zipper.Config;`).
- Delete (ported to module tests): `Build_ChaosMode_SetsChaosProperties`, `Build_DelimiterPreset_Csv_SetsCommaDelimiters`, `Build_DelimiterOverride_OverridesPreset`, `Build_StrictDelimiters_OverrideOldDelimiters`, `ParseDelimiterArgument_ValidInputs_ReturnsCorrectValue`, `ParseDelimiterArgument_Empty_Throws`, `ParseStrictDelimiter_ValidInputs_ReturnsCorrectValue`, `ParseStrictDelimiter_InvalidPrefix_Throws`, `Build_HashModeActualAndAlgorithms_SetsHashConfig`, `Build_HashModeSimulated_SetsSimulatedMode`, `Build_HashModeNone_DefaultsToDisabled`, `ParseHashConfig_ActualMode_ReturnsCorrectConfig`, `ParseHashConfig_SimulatedMode_ReturnsSimulatedModeWithDefaultMD5`, `ParseHashConfig_InvalidMode_DefaultsToNone`, `ParseHashConfig_Default_NoneModeEmptyAlgorithms`, `Build_LoadfileOnlyWithActualHashMode_ReturnsNull`.
- `Build_LoadfileOnly_SetsProperties`: drop `parsed.Eol = "LF"` and the `Assert.Equal("LF", result!.Delimiters.EndOfLine)` assertion (ported to `DelimiterModuleTests`); keep the `LoadfileOnly`/`LoadFileFormat`/`Formats` assertions.
- `CliPipelineTests` / `MixedFileTypeCliTests` / `SourceDrivenCliTests` go through `Pipeline.Build` — no signature change. They stay green if this wiring is correct.

- [ ] **Step 8: Run the full gate**

```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj
```
Expected: build green, format clean, all unit + analyzer tests pass. If any test was missed in the retarget map, restore the assertion as a module test (do not resurrect the flat-bag property).

- [ ] **Step 9: Byte-parity check (Critical Rule 6)**

```bash
dotnet build -c Release
tests/goldens/run-goldens.sh
```
Expected: exit 0, all 20 scenarios match their committed fixtures byte-for-byte (covers `custom-delim`, `chaos-dat`, `tiff-multipage`, `loadfile-only-*`). **Hash is not in the goldens** — rely on `HashModuleTests`. If a scenario diverges, the module logic was not a faithful move; diff via `tests/goldens/lib/diff-loadfile.sh` output and fix, do not regenerate fixtures.

- [ ] **Step 10: Commit**

```bash
git add src/Cli/Modules/CliModules.cs src/Cli/CliParser.cs src/Cli/ParsedArguments.cs src/Cli/Pipeline.cs src/Cli/RequestBuilder.cs src/Cli/CliValidator.cs src/Cli/Validation/CrossCuttingValidator.cs src/Cli/Validation/LoadfileOnlyValidator.cs src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs src/Zipper.Tests/Cli/CliParserTests.cs src/Zipper.Tests/Cli/CliValidatorTests.cs src/Zipper.Tests/Cli/RequestBuilderTests.cs
git commit -m "refactor: wire domain modules into CLI pipeline, retire flat-bag props for leaf domains"
```

---

## Task 7: Architecture Diagram + Full E2E

**Files:**
- Modify: `docs/architecture.md` (Critical Rule 5 — same-PR diagram update). **Do not erase `CliValidator` or `RequestBuilder`.** This review is not architecture approval; re-review the mermaid after the edit.

- [ ] **Step 1: Update the Component Map**

`docs/architecture.md`, Component Map CLI Layer subgraph (lines ~72–77) and the bottom `Program → … → FGR` link. Phase 1 state — modules exist; `ParsedArguments` / `CliValidator` / `RequestBuilder` still present (deleted in Phase 4). Comparison still short-circuits in `Program` via `CliParser.Parse(args)` and never hits `Pipeline`.

```mermaid
subgraph CLI Layer
    Program["Program.cs<br/>(SelectMode dispatch)"]
    Pipeline["Pipeline.Build"]
    CliParser["CliParser<br/>(token reader + module dispatch)"]
    Modules["Domain Modules<br/>Hash / Delimiter / Tiff / Chaos"]
    CliValidator["CliValidator<br/>(remaining domains)"]
    RequestBuilder["RequestBuilder<br/>(remaining configs + source/profile)"]
    Program --> Pipeline
    Pipeline --> CliParser
    CliParser --> Modules
    Pipeline --> CliValidator
    Modules --> Pipeline
    Pipeline --> RequestBuilder
end
```

Bottom link stays `Program --> Pipeline --> RequestBuilder --> FGR` (not `Program --> CliParser --> Pipeline --> FGR`). Footnote: *"Phase 1 of #750: Hash/Delimiter/Tiff/Chaos parse+validate+build moved into modules. CliValidator and RequestBuilder still own the remaining domains until Phase 4. Comparison short-circuit in Program still calls CliParser.Parse(args) directly."*

- [ ] **Step 2: Run the full verification gate**

```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/
dotnet test src/Zipper.Tests/Zipper.Tests.csproj
dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj
dotnet build -c Release
./tests/run-tests.sh          # full E2E (Linux)
```
Expected: all green. Note the pre-commit hook runs format + unit tests automatically.

- [ ] **Step 3: Commit + PR per AGENTS.md workflow**

```bash
git add docs/architecture.md
git commit -m "docs: reflect domain module seam in architecture component map"
```

Then create the PR referencing **`Refs #750`** (or `Towards #750`) — **not** `Fixes #750`. Phase 1 is a slice; Phases 2–4 are still open and GitHub ignores the parenthetical. Include `## Release Notes` per the mandate, run `tests/wait-for-reviews.sh`, and check SonarCloud for BLOCKER/MAJOR before merge. Four near-copy `TryApply`/`TryBuild` tails may trip new-code duplication — extract a helper only if the gate fails.

---

## Self-Review

**Spec coverage (issue #750 Phase 1):**
- HashModule (3 args) ✅ Task 2 — absorbs `ValidateHashMode`/`ValidateHashAlgorithms`/`ParseHashConfig` + the loadfile-only cross-check.
- DelimiterModule (10 args) ✅ Task 4 — absorbs `ValidateDelimiters`/`ValidateDatDelimiters`/`ParseDelimiterArgument`/`ParseStrictDelimiter` + `RequestBuilder` delimiter section + LoadfileOnlyValidator eol check.
- TiffModule (1 arg) ✅ Task 3 — absorbs `ValidateTiffPagesRange` + `Tiff` section.
- ChaosModule (4 args) ✅ Task 5 — absorbs `ValidateChaos`/`IsValidChaosAmount` + `Chaos` section.
- Each phase green, no broken intermediate states ✅ (module tasks are additive; wiring task is atomic).
- Security validation (`IsPathSafe` in `ValidateSourceInput`/`ValidateColumnProfile`) untouched ✅ (stays in CrossCuttingValidator; Phase 3/4 concern).
- `composer → serializer → emitter` seam and three-mode pipeline untouched ✅.

**Placeholder scan:** All module production code is complete in this document. Test bodies are the existing corpus moved with construction swapped; the mapping tables name the exact source test and swap, which is a precise retarget (Critical Rule 3) rather than a placeholder.

**Type consistency:** `TryBuild(ParsedArguments, out TConfig)` shape is uniform across all four modules; `CliModule.Owns/TakesValue/TryApply` used identically in `CliParser` and `Pipeline`. `HashConfig.Algorithms` is `IReadOnlySet<HashAlgorithm>` (matches `HashConfig.cs`). `DelimiterConfig`/`TiffConfig`/`ChaosConfig`/`HashConfig` are `Zipper.Config` records — unchanged contract.

**Known gaps / judgment calls (review applied 2026-08-13):**
1. Cross-domain checks (chaos↔loadfile, hash↔loadfile, eol↔mode) live in the leaf modules reading `ParsedArguments` temporarily; they relocate to `CrossCuttingRules` in Phase 4. This is the issue's own plan.
2. `ChaosModule` transitively calls `RequestBuilder.GetLoadFileFormat` — transitional until LoadFileModule (Phase 2). Do not duplicate the 6-line parser.
3. `docs/architecture.md` diagram update is included (Rule 5). **This review is not architecture approval** — first mermaid erased validator/builder; Task 7 now keeps them. Re-review after the diagram edit.
4. The issue says "Not recommended for autonomous agent execution"; this plan scopes execution to Phase 1 (the low-risk leaf phase) with review checkpoints at Tasks 2, 6, and 7.
5. Phase 1 collocates validate+parse; it does not remove double interpretation. Do not claim that in the PR.
6. Goldens do not cover hash flags. `HashModuleTests` is the hash contract.
7. PR closer is `Refs #750`, never `Fixes #750`.

---

## Phase 2–4 (sketch, detailed plans follow each phase's approval)

**Phase 2 (medium):** BatesModule (absorbs `ProductionSetValidator` Bates-range math + `ValidateBates` + RequestBuilder `Bates` section + the `--bates-prefix/--bates-start/--bates-digits` parse incl. comma lists and `--bates-prefixes`/`--bates-starts`), MetadataModule (absorbs `--column-profile` loading + `ValidateColumnProfile` + RequestBuilder metadata section + StandardModeValidator bounds), LoadFileModule (absorbs format strings + `GetLoadFileFormat` + `ValidateLoadFileFormats` + LoadfileOnlyValidator format rules + the `RequestBuilder` load-file section; `ChaosModule`'s transitional `GetLoadFileFormat` call relocates). `--eol`'s mode check may also migrate here.

**Phase 3 (complex):** ProductionModule (most of `ProductionSetValidator` + `GenerateProductionIds`, retargeting `ProductionSetGenerator.cs:44`), OutputModule (`StandardModeValidator` + `ParseSize` + output section), SourceInputModule (source reading + identity-collision logic + `ValidateSourceInput`).

**Phase 4 (cleanup):** Typed `CrossCuttingRules` phase validating the assembled request (absorbs the module-level cross-domain checks), delete `ParsedArguments`/`RequestBuilder`/`CliValidator`/the 4 validator classes, `CliParser.Parse(args)` returns modules directly, final architecture diagram update, Requirements.md/README sync if any behavior surfaced.
