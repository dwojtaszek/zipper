# CLI Domain Modules — Phase 4 (CrossCuttingRules + delete waterfall) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the `CliParser → CliValidator → RequestBuilder` waterfall (`CliParser.cs`, `CliValidator.cs`, `ParsedArguments.cs`, `RequestBuilder.cs`, and the three `src/Cli/Validation/*` mode validators) and replace its two remaining responsibilities — cross-domain validation and `FileGenerationRequest` assembly — with `CrossCuttingRules` + a folded-in `Pipeline.AssembleRequest`, with zero behavior or byte-level output change. Issue #750 Phase 4 (final phase).

**Architecture:** Successor of the Phase 1–3 module seam. Four moves, no new behavior:

1. **Comparison trio → `ComparisonModule`.** `--compare-production-manifests` / `--comparison-mode` / `--comparison-output` are the last flags `CliParser` handled itself (`CliParser.cs:40–55`). They become a real module (`ComparisonModule`) owning parse + trio validation (REQ-176–179), exactly like every other domain. `Program.cs` parses once and asks the module for a validated `ComparisonRequest`. On a hit it runs the comparer; on a miss Task 2 still calls `Pipeline.Build(args)` (the existing `string[]` overload) so the solution compiles. Task 3 switches that call to `Pipeline.Build(modules)` and deletes the second parse. That is when the Phase 3 "discarded set" wart actually dies.
2. **Token dispatch loop → `CliModuleSet.Parse`.** `CliParser`'s loop (own-flag lookup, value fetch, `TryApply`, unknown-arg error) moves onto `CliModuleSet` as an instance method. No separate tokenizer class — the set is always the dispatch context. After `Comparison` is in `All`, `CliParser.Parse`'s comparison `switch` is dead (module `Owns` wins); delete the switch with `CliParser` in Task 3.
3. **Cross-domain validation → `CrossCuttingRules`.** The generation-mode checks left in `CliValidator` (`--type`/`--count` required gates, `CliValidator.cs:48–63`) plus `StandardModeValidator` + `ProductionSetValidator` + `CrossCuttingValidator` merge into one `CrossCuttingRules.Validate(CliModuleSet)`. Messages and ordering are byte-identical, and it still runs **before** the `TryBuild` chain, so the documented multi-invalid error-precedence divergence from Phases 1–3 does not change for the generation path. Do **not** move `HashModule`/`DelimiterModule` sibling-parameter checks (`--hash-mode actual` × `--loadfile-only`, `--eol` requires loadfile-only/production-set) — those comments that say "moves to CrossCuttingRules in Phase 4" are stale; rewrite the comments only.
4. **Assembly + image-type override → `Pipeline.AssembleRequest`.** `RequestBuilder.Build`'s null-guards are dropped (internal callers always pass non-null configs from `TryBuild`); the image-type → DAT+OPT override block and the `FileGenerationRequest` construction move verbatim into a private `Pipeline.AssembleRequest`. `RequestBuilder`'s four statics (`ParseSize`, `GetDistributionFromName`, `GetEncodingFromName`, `GetLoadFileFormat`) are already `internal static` (`RequestBuilder.cs:68–109`) — copy them as `internal static` on `ArgumentHelpers`, not `public`.

**Maintainer decisions requested** (see also Global Constraints → Architecture invariants):

- **D1:** Comparison trio lives in `ComparisonModule`, not `CrossCuttingRules`. Rationale: `Program` must validate the trio *before* deciding to short-circuit to the comparer; `CrossCuttingRules` runs inside `Pipeline` after that decision and must never see comparison mode. This matches "every domain owns parse+validate+build."
- **D2:** The token loop folds into `CliModuleSet.Parse(args)` instead of a separate `ArgTokenizer` class — one fewer file, dispatch context is always the set.
- **D3:** `RequestBuilder.Build`'s `ArgumentNullException` guards are dropped (dead defensive code on an internal-only path). The two null-guard tests are deleted under the Critical Rule 3 exception (tests whose only contract was the removed null guard).

**Tech Stack:** C# 14 / .NET 10, xUnit, Mermaid (architecture.md), bash E2E + goldens harness.

## Grounding notes (2026-08-14, against `42708f5`)

Reviewed against current `src/Cli/*`, module comments, test inventories, `docs/architecture.md`, goldens (20 scenarios in `tests/goldens/scenarios.tsv`), and `tests/test-production-sets.sh`. Corrections baked into the tasks below:

- **Task 2 cannot call `Pipeline.Build(modules)`.** That overload does not exist until Task 3. Task 2 keeps `Pipeline.Build(args)` (temporary double-parse on the generation path). Task 3 is the real discarded-set delete.
- **`ArgumentHelpers` methods are `internal static`**, matching today's `RequestBuilder` statics (`RequestBuilder.cs:68–109`). Not `public`.
- **Do not move `HashModule`/`DelimiterModule` sibling-parameter checks** into `CrossCuttingRules`. The "moves to CrossCuttingRules in Phase 4" comments are stale; rewrite comments only.
- **Test names / line numbers retargeted to the current corpus.** `Parse_WithNullArgs_ThrowsArgumentNullException` (not `Parse_NullArgs_…`). Comparison tests live at `CliValidatorTests.cs:328–405`. `OutputModuleTests.TryBuild_SingleTypeDefaults_MatchRequestBuilder` does not call the helper.
- **`ComparisonTests` is not a `Program.Main` guard.** It calls `ProductionManifestComparer` directly. The Program comparison-path guard is `tests/test-production-sets.sh` Test 10.
- **`ChaosAnomalyTypes` is consumed by `ChaosModule` + `ChaosEngine`**, not `CliValidator`. `ProductionModule` transitional comment starts at line 118, not 120.
- **`Pipeline.Build` TryBuild chain stays ten modules.** Comparison is parsed/validated by `Program` and is not in that chain.

## Global Constraints

- `FileGenerationRequest` and all 9 sub-config records are the **stable output contract — do not change them** (issue #750). The Phase 4 delete removes only CLI plumbing; config assembly output is byte-identical.
- Preserve `composer → serializer → emitter` Load File seam (ADR-0007) and the three-mode pipeline (ADR-0006).
- **Byte-exact output parity** (Critical Rule 6): this phase is a pure logic move. The existing goldens harness (`tests/goldens/run-goldens.sh`, 20 scenarios) + `tests/run-tests.sh` / `tests/run-tests.bat` E2E are the parity gate. **No new harness.**
- Error/warning messages move **byte-for-byte** on single-invalid invocations (E2E + unit tests assert exact strings). Accepted divergence (unchanged from Phases 1–3): multi-invalid error precedence — do not claim parity, do not "fix" it.
- **New accepted micro-divergence (flag in PR):** `Pipeline.Build(string[])` with an *invalid* comparison trio used to fail via `CliValidator`; after Phase 4 it ignores the trio and proceeds to generation. No production caller routes comparison flags through `Pipeline.Build` (`Program` handles them first), and no test exercises this — document it, do not add a comparison check to `CrossCuttingRules`.
- **Comparison trio error ordering must stay byte-identical** to today's `CliValidator` (see `ComparisonModule.TryBuild` in Task 2).
- `Warnings as Errors` is enabled (`zipper.sln`); run `dotnet format --verify-no-changes src/` after every task.
- Docs sync (Critical Rule 4): Phase 4 changes no CLI behavior or formats → no README/Requirements/UBIQUITOUS_LANGUAGE changes. Flag the Rule 4 no-op in the PR if any single-invalid message byte drifts.
- Architecture invariants (Critical Rule 5): the Component Map in `docs/architecture.md` names `CliParser`/`CliValidator`/`RequestBuilder` — **same-PR diagram update required** (Task 4). **This plan is not architecture approval.** Re-review the mermaid after the diagram edit before treating Rule 5 as approved.
- Test coverage must not decrease (Critical Rule 3): every removed test in `CliParserTests`/`CliValidatorTests`/`RequestBuilderTests` is **retargeted** to `CliModuleSetTests` / `CrossCuttingRulesTests` / `PipelineTests` / `ComparisonModuleTests` / `ArgumentHelpersTests` with construction swapped (`new ParsedArguments { ComparisonMode = "replacement" }` → `modules.Comparison.TryApply("--comparison-mode", "replacement")`). Pure duplicates (the module-direct tests already re-homed in `LoadFileModuleTests`/`MetadataModuleTests`/`BatesModuleTests` during Phases 2–3) are deleted, not re-moved. Exception: the two `RequestBuilder.Build` null-guard tests (`Build_NullArg_ThrowsArgumentNullException`, `Build_NullConfigArg_ThrowsArgumentNullException`) and `CliValidatorTests.Validate_NullArg_ThrowsArgumentNullException` are deleted per D3 / D3-adjacent.
- No copyright headers. File-scoped namespaces. Naming: test class `{Subject}Tests`, method `{Method}_{Scenario}_{Expected}`.
- `InternalsVisibleTo("Zipper.Tests")` already exists (`src/Zipper.csproj`) — `internal` `CrossCuttingRules`/`ArgumentHelpers`/`HelpTextGenerator` and `internal` `Pipeline.Build(CliModuleSet)` (Task 3) are test-visible.
- PR closer is `Refs #750` / `Towards #750`, **never** `Fixes #750`. This closes out the roadmap.

## File Structure

**Create:**
- `src/Cli/ArgumentHelpers.cs` — internal static: `ParseSize`, `GetDistributionFromName`, `GetEncodingFromName`, `GetLoadFileFormat` (moved verbatim from `RequestBuilder`).
- `src/Cli/Modules/ComparisonModule.cs` — `ComparisonModule : CliModule` + `ComparisonRequest` record.
- `src/Cli/CrossCuttingRules.cs` — internal static: generation-mode cross-domain validation (CliValidator remnant + 3 mode validators merged).
- `src/Zipper.Tests/Cli/ArgumentHelpersTests.cs`
- `src/Zipper.Tests/Modules/ComparisonModuleTests.cs`
- `src/Zipper.Tests/Cli/CrossCuttingRulesTests.cs`
- `src/Zipper.Tests/Cli/CliModuleSetTests.cs` (parse/dispatch — successor of `CliParserTests`)
- `src/Zipper.Tests/Cli/PipelineTests.cs` (request assembly + REQ-106/164 path roundtrips — successor of `RequestBuilderTests` Build_* + `CliParserTests` path tests)
- `src/Zipper.Tests/Cli/PipelineTestHelper.cs` (renamed from `RequestBuilderTestHelper.cs`)

**Delete:**
- `src/Cli/CliParser.cs`
- `src/Cli/CliValidator.cs`
- `src/Cli/ParsedArguments.cs`
- `src/Cli/RequestBuilder.cs`
- `src/Cli/Validation/StandardModeValidator.cs`
- `src/Cli/Validation/ProductionSetValidator.cs`
- `src/Cli/Validation/CrossCuttingValidator.cs`
- `src/Zipper.Tests/Cli/CliParserTests.cs`
- `src/Zipper.Tests/Cli/CliValidatorTests.cs`
- `src/Zipper.Tests/Cli/RequestBuilderTests.cs`
- `src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs` (→ renamed)

**Modify:**
- `src/Cli/Modules/CliModules.cs` — add `Comparison` property + `CliModuleSet.Parse` + add module to `All`/`Create`.
- `src/Cli/Modules/OutputModule.cs` — `RequestBuilder.*` → `ArgumentHelpers.*` (lines 183, 213, 220); update stale comments (transitional getters at 120–121; check-order comment at 134–136; `Byte-identical to RequestBuilder.Build` at 227).
- `src/Cli/Modules/LoadFileModule.cs` — nine `RequestBuilder.*` call sites (lines 54, 64, 75, 100, 112, 127, 134, 137, 146) → `ArgumentHelpers.*`.
- `src/Cli/Pipeline.cs` — Task 3 only: `Build(string[])` + `Build(CliModuleSet)` overloads + `AssembleRequest`. Do not touch in Task 2.
- `src/Program.cs` — Task 2: single parse + comparison short-circuit + empty-args help + still `Pipeline.Build(args)`. Task 3: switch that last call to `Pipeline.Build(modules)`. Also rewrite the `SelectMode` comment at line 114 (`The CLI validator ensures…`).
- `src/ChaosAnomalyTypes.cs:5` — `consumed by both CliValidator and ChaosEngine` → `consumed by ChaosModule and ChaosEngine`.
- `src/Cli/Modules/{MetadataModule,SourceInputModule,BatesModule,ProductionModule,HashModule,DelimiterModule}.cs` — stale comments only. Transitional `ParsedArguments` getters: `MetadataModule.cs:66–67`, `SourceInputModule.cs:40–41`, `BatesModule.cs:88–89`, `ProductionModule.cs:118–119`. False "moves to CrossCuttingRules in Phase 4" comments stay as comments: `HashModule.cs:70–73`, `DelimiterModule.cs:49`.
- `src/Zipper.Tests/MixedFileTypeCliTests.cs` / `SourceDrivenCliTests.cs` / `src/Zipper.Tests/Modules/OutputModuleTests.cs` — update `RequestBuilderTestHelper` → `PipelineTestHelper` call sites. `TryBuild_SingleTypeDefaults_MatchRequestBuilder` (OutputModuleTests.cs:374) does **not** call the helper — leave the body; rename only if you want, it is cosmetic.
- `docs/architecture.md` — Component Map nodes (lines 72–85), `Program --> Pipeline --> RequestBuilder --> FGR` (line 147), phase note (line 161). Rule 5; human approval.

## Test Retarget Matrix (Critical Rule 3)

| Source file (deleted) | Test goes to | Notes |
|---|---|---|
| `CliParserTests.cs` — `Parse_RequiredArgs_ParsesCorrectly`, `Parse_MissingTypeValue_ReturnsNull` (rename → `ReturnsFalse`), `Parse_MissingValueForValueTakingFlags_ReturnsNull` (rename → `ReturnsFalse`; **add the three comparison flags to the flag array**), `Parse_UnknownFlag_ReturnsNull`, `Parse_UnknownPositionalValue_ReturnsNull`, `Parse_UnknownFlagInValuePosition_ReturnsNull`, `Parse_WithNullArgs_ThrowsArgumentNullException` (keep this exact name), `Parse_ChaosListNotConsumedAsValueForPrecedingArg`, `Parse_BenchmarkNotConsumedAsValueForPrecedingArg`, all `Parse_Invalid*` (count/folders/attachment-rate/bates-start/bates-digits/seed/empty-percentage/custodian-count/volume-size), ownership tests (`AllBooleanFlags`, `LoadfileOnlyArgs`, `ProductionSetArgs`, `ColumnProfileArgs`) | `CliModuleSetTests` | `CliParser.Parse(new[]{...})` / `RequestBuilderTestHelper.Parse(...)` → `CliModules.Create().Parse(...)`; `Assert.Null(result)` → `Assert.False(ok)`; ownership asserts unchanged against module getters. `Parse_LoadfileOnlyArgs` needs `using Zipper;` (or `Zipper.LoadFileFormat`) — `LoadFileFormat` lives in `namespace Zipper`, and `CliParserTests.cs` currently only `using Zipper.Cli` |
| `CliParserTests.cs` — `Parse_OutputPathWithParentTraversal_RejectsPathOutsideCwd`, `Parse_OutputPathWithinCwd_IsAccepted`, `Parse_ColumnProfileWithParentTraversal_RejectsPathOutsideCwd`, `Parse_ColumnProfileWithinCwd_IsAccepted` | `PipelineTests` | Drop the `CliValidator.Validate` middle line; assert `PipelineTestHelper.Build(modules: modules)` null/non-null. These currently parse **then** `CliValidator.Validate` **then** `RequestBuilderTestHelper.Build` (`CliParserTests.cs:210–304`) |
| `CliValidatorTests.cs` — validation tests (`ValidArgs`, `MissingType`, `LoadfileOnly_WithoutType`, `ProductionSet_WithoutType`, `MissingCount`, `TargetZipSizeWithoutCount`, `ProductionSet_ConflictsWithLoadfileOnly`, `RedactedProduction_ConflictsWithLoadfileOnly`, `ProductionSet_WithoutBatesPrefix`, `Types_WithLoadfileOnly`, `Types_WithColumnProfile`, `ColumnProfile_WithProductionSet`) | `CrossCuttingRulesTests` | `CliValidator.Validate(parsed!, modules)` → `CrossCuttingRules.Validate(modules)`; `CreateValid()` → apply `--type pdf --count 10 --output-path cwd` via `TryApply` |
| `CliValidatorTests.cs` — `Validate_NullArg_ThrowsArgumentNullException` | delete | no null contract on internal `CrossCuttingRules` (D3-adjacent). Today's method only nulls `parsed`; `modules` also has `ThrowIfNull` (`CliValidator.cs:10–11`) — do not port either |
| `CliValidatorTests.cs` — comparison trio tests (`CliValidatorTests.cs:328–405`: `Validate_ComparisonMode_WithoutCompareManifests_ReturnsFalse`, `Validate_ComparisonOutput_WithoutCompareManifests_ReturnsFalse`, `Validate_CompareManifests_WithoutComparisonMode_ReturnsFalse`, `Validate_CompareManifests_WithoutComparisonOutput_ReturnsFalse`, `Validate_CompareManifests_ValidMode_ReturnsTrue` theory, `Validate_CompareManifests_InvalidMode_ReturnsFalse`, `Validate_CompareManifests_BypassesTypeCountOutputPathValidation`) | `ComparisonModuleTests` | `new ParsedArguments { X = ... }` → `modules.Comparison.TryApply(...)`; `CliValidator.Validate(args, ...)` → `modules.Comparison.TryBuild(out var request)`; the bypass test asserts `TryBuild` returns a non-null `request` without any other module state (trio only) |
| `CliValidatorTests.cs` — already-moved module-direct wrappers | delete | already covered 1:1 by module tests (do **not** re-home): `Validate_AttachmentRateOutOfRange` → `MetadataModuleTests.TryBuild_AttachmentRateOutOfRange_ReturnsFalse`; `Validate_InvalidLoadFileFormat` → `LoadFileModuleTests.TryBuild_UnknownFormat_ReturnsInvalidFormatNotDatOptRestriction`; `Validate_LoadfileOnlyWithTargetZipSize` → `LoadFileModuleTests.TryBuild_LoadfileOnly_WithTargetZipSize_ReturnsFalse`; `Validate_LoadfileOnlyWithIncludeLoadFile` → `LoadFileModuleTests.TryBuild_LoadfileOnly_WithIncludeLoadFile_ReturnsFalse`; `Validate_BatesPrefix_WithPathSeparator`/`WithDotDot`/`WithSpecialChars` → `BatesModuleTests.TryBuild_PrefixWithPathSeparator`/`PrefixWithDotDot`/`PrefixWithSpecialChars`; `Validate_WithFamiliesWithoutEml`/`WithEmlAndAttachmentRateZero`/`WithEmlAndAttachmentRatePositive` → matching `MetadataModuleTests.TryBuild_WithFamilies*`; `Validate_LoadfileOnly_WithCsvFormat`/`WithEdrmXmlFormat`/`WithCsvFormatsPlural`/`WithCsvAndXmlFormatsPlural`/`WithDatFormatsPlural`/`WithDatFormat`/`WithOptFormat` → matching `LoadFileModuleTests.TryBuild_LoadfileOnly_*`. **Gap:** `Validate_WithFamiliesAndLoadfileOnly_EmitsWarning` (`CliValidatorTests.cs:237`) has **no** `MetadataModuleTests` twin — it is the same warning as `TryBuild_WithFamiliesWithoutEml` (`includesEml: false`). Delete it; do not invent a new test |
| `RequestBuilderTests.cs` — `Build_StandardMode_SetsAllDefaults`, `Build_WithValidPath_ResolvesDirectory`, `Build_LoadfileOnly_SetsProperties`, `Build_ProductionSet_SetsVolumeSize`, `Build_BatesConfig_SetsCorrectly`, `Build_ColumnProfile_LoadsProfile`, `Build_MultiFormat_CreatesFormatList`, `Build_LoadfileOnlyEncoding_UsesExtendedSet`, `Build_Encoding_PreservesNormalizedInputName` | `PipelineTests` | `RequestBuilderTestHelper.Build(modules: modules)` → `PipelineTestHelper.Build(modules: modules)`; asserts unchanged |
| `RequestBuilderTests.cs` — `Build_NullArg_ThrowsArgumentNullException`, `Build_NullConfigArg_ThrowsArgumentNullException` | delete | per D3 |
| `RequestBuilderTests.cs` — `ParseSize_*`, `GetDistributionFromName_*`, `GetLoadFileFormat_*` | `ArgumentHelpersTests` | `RequestBuilder.X` → `ArgumentHelpers.X` |

---

### Task 1: `ArgumentHelpers` (move `RequestBuilder` statics)

**Files:**
- Create: `src/Cli/ArgumentHelpers.cs`
- Modify: `src/Cli/Modules/OutputModule.cs`, `src/Cli/Modules/LoadFileModule.cs`
- Test: `src/Zipper.Tests/Cli/ArgumentHelpersTests.cs`

**Interfaces:**
- Produces: `internal static class ArgumentHelpers` with `internal static long? ParseSize(string size)`, `internal static DistributionType? GetDistributionFromName(string name)`, `internal static Encoding? GetEncodingFromName(string name)`, `internal static LoadFileFormat? GetLoadFileFormat(string name)` — visibility + signatures byte-identical to today's `RequestBuilder` statics (`RequestBuilder.cs:68–109`).
- Consumes: `EncodingHelper.GetEncoding` (`src/EncodingHelper.cs`), `DistributionType` (`Zipper.Config`), `LoadFileFormat` (`Zipper`). All four move verbatim, byte-for-byte. `RequestBuilder.cs` currently has `using Zipper.Config;` only — `System.Text` / `System.Globalization` come from implicit usings.

- [ ] **Step 1: Create `ArgumentHelpers.cs`** (verbatim move of the statics; `RequestBuilder.cs:68–109`)

```csharp
using System.Text;
using Zipper.Config;

namespace Zipper.Cli;

internal static class ArgumentHelpers
{
    private static readonly Dictionary<string, long> SizeMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KB"] = 1024,
        ["MB"] = 1024 * 1024,
        ["GB"] = 1024 * 1024 * 1024,
    };

    internal static long? ParseSize(string size)
    {
        size = size.Trim();

        foreach (var (suffix, multiplier) in SizeMultipliers)
        {
            if (size.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var numberPart = size.Substring(0, size.Length - suffix.Length);
                return long.TryParse(numberPart, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value * multiplier : null;
            }
        }

        return null;
    }

    internal static DistributionType? GetDistributionFromName(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "PROPORTIONAL" => DistributionType.Proportional,
            "GAUSSIAN" => DistributionType.Gaussian,
            "EXPONENTIAL" => DistributionType.Exponential,
            _ => null,
        };
    }

    internal static Encoding? GetEncodingFromName(string name) => EncodingHelper.GetEncoding(name);

    internal static LoadFileFormat? GetLoadFileFormat(string name)
    {
        return name.ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal) switch
        {
            "DAT" => LoadFileFormat.Dat,
            "OPT" => LoadFileFormat.Opt,
            "CSV" => LoadFileFormat.Csv,
            "XML" => LoadFileFormat.EdrmXml,
            "EDRMXML" => LoadFileFormat.EdrmXml,
            "CONCORDANCE" => LoadFileFormat.Concordance,
            _ => null,
        };
    }
}
```

- [ ] **Step 2: Re-point the two module call sites**

In `OutputModule.cs`:
- `src/Cli/Modules/OutputModule.cs:183` — `RequestBuilder.ParseSize(_targetZipSize)` → `ArgumentHelpers.ParseSize(_targetZipSize)`
- `src/Cli/Modules/OutputModule.cs:213` — `RequestBuilder.GetEncodingFromName(_encoding)` → `ArgumentHelpers.GetEncodingFromName(_encoding)`
- `src/Cli/Modules/OutputModule.cs:220` — `RequestBuilder.GetDistributionFromName(_distribution)` → `ArgumentHelpers.GetDistributionFromName(_distribution)`
- `src/Cli/Modules/OutputModule.cs:227` — comment `Byte-identical to RequestBuilder.Build` → `Byte-identical to Pipeline.AssembleRequest` (name lands in Task 3; safe to write now)

In `LoadFileModule.cs` (9 occurrences: lines 54, 64, 75, 100, 112, 127, 134, 137, 146 — `CurrentFormat` getter + eight `TryBuild` calls of `GetLoadFileFormat` / `GetEncodingFromName` / `GetDistributionFromName`) — same rename. Leave the `TryBuild` comment at lines 58–59 (`were bag fields pre-Phase-3`); it does not mention `RequestBuilder`.

- [ ] **Step 3: Create `ArgumentHelpersTests.cs`** (port of `RequestBuilderTests.cs:151–195`, construction swapped)

```csharp
using Xunit;
using Zipper.Cli;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ArgumentHelpersTests
{
    [Fact]
    public void ParseSize_ValidSizes_ReturnsBytes()
    {
        Assert.Equal(1024, ArgumentHelpers.ParseSize("1KB"));
        Assert.Equal(1024 * 1024, ArgumentHelpers.ParseSize("1MB"));
        Assert.Equal(1024L * 1024 * 1024, ArgumentHelpers.ParseSize("1GB"));
        Assert.Equal(500L * 1024 * 1024, ArgumentHelpers.ParseSize("500MB"));
    }

    [Fact]
    public void ParseSize_InvalidSize_ReturnsNull()
    {
        Assert.Null(ArgumentHelpers.ParseSize("invalid"));
        Assert.Null(ArgumentHelpers.ParseSize("10XB"));
    }

    [Fact]
    public void GetDistributionFromName_ValidNames_ReturnsCorrectType()
    {
        Assert.Equal(DistributionType.Proportional, ArgumentHelpers.GetDistributionFromName("proportional"));
        Assert.Equal(DistributionType.Gaussian, ArgumentHelpers.GetDistributionFromName("gaussian"));
        Assert.Equal(DistributionType.Exponential, ArgumentHelpers.GetDistributionFromName("exponential"));
    }

    [Fact]
    public void GetDistributionFromName_InvalidName_ReturnsNull()
    {
        Assert.Null(ArgumentHelpers.GetDistributionFromName("invalid"));
    }

    [Fact]
    public void GetLoadFileFormat_ValidNames_ReturnsCorrectFormat()
    {
        Assert.Equal(LoadFileFormat.Dat, ArgumentHelpers.GetLoadFileFormat("dat"));
        Assert.Equal(LoadFileFormat.Opt, ArgumentHelpers.GetLoadFileFormat("opt"));
        Assert.Equal(LoadFileFormat.Csv, ArgumentHelpers.GetLoadFileFormat("csv"));
        Assert.Equal(LoadFileFormat.EdrmXml, ArgumentHelpers.GetLoadFileFormat("xml"));
        Assert.Equal(LoadFileFormat.EdrmXml, ArgumentHelpers.GetLoadFileFormat("edrm-xml"));
        Assert.Equal(LoadFileFormat.Concordance, ArgumentHelpers.GetLoadFileFormat("concordance"));
    }

    [Fact]
    public void GetLoadFileFormat_InvalidName_ReturnsNull()
    {
        Assert.Null(ArgumentHelpers.GetLoadFileFormat("invalid"));
    }
}
```

- [ ] **Step 4: Delete the six now-duplicated static tests from `RequestBuilderTests.cs`** (lines 150–195: `ParseSize_ValidSizes_ReturnsBytes`, `ParseSize_InvalidSize_ReturnsNull`, `GetDistributionFromName_ValidNames_ReturnsCorrectType`, `GetDistributionFromName_InvalidName_ReturnsNull`, `GetLoadFileFormat_ValidNames_ReturnsCorrectFormat`, `GetLoadFileFormat_InvalidName_ReturnsNull`). Keep `using Zipper.Cli;` — the remaining `Build_*` / null-guard tests still call `RequestBuilder`. Leave those tests in place (retargeted or deleted in Task 3).

- [ ] **Step 5: Verify**

```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj
```
Expected: build clean (0 warnings — Warnings as Errors), format clean, all tests pass (the 6 static tests now run under `ArgumentHelpersTests`, `RequestBuilderTests` has 11 remaining).

- [ ] **Step 6: Commit**

```bash
git add src/Cli/ArgumentHelpers.cs src/Cli/Modules/OutputModule.cs src/Cli/Modules/LoadFileModule.cs src/Zipper.Tests/Cli/ArgumentHelpersTests.cs src/Zipper.Tests/Cli/RequestBuilderTests.cs
git commit -m "refactor(cli): extract RequestBuilder statics into ArgumentHelpers (Phase 4 of #750)"
```

---

### Task 2: `ComparisonModule` + `CliModuleSet.Parse` + `Program` cut-over

**Files:**
- Create: `src/Cli/Modules/ComparisonModule.cs`, `src/Zipper.Tests/Modules/ComparisonModuleTests.cs`
- Modify: `src/Cli/Modules/CliModules.cs`, `src/Program.cs`
- Test: `src/Zipper.Tests/Cli/CliModuleSetTests.cs` (parse/dispatch tests — the only new tests this task; full `CliParserTests` retarget lands in Task 3)

**Interfaces:**
- Consumes: `CliModule` base (`OwnedFlags`, `TakesValue`, `TryApply`).
- Produces:
  - `public sealed class ComparisonModule : CliModule` with `public bool HasComparisonRequest` and `public bool TryBuild(out ComparisonRequest? request)`. No extra flag getters — tests read `HasComparisonRequest` / `ComparisonRequest` only.
  - `public sealed record ComparisonRequest(string ManifestPaths, string Mode, string OutputPath)`.
  - `CliModuleSet.Comparison` property; `CliModuleSet.Parse(string[] args) : bool` (token dispatch).

- [ ] **Step 1: Write the failing `ComparisonModuleTests.cs`**

```csharp
using Xunit;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ComparisonModuleTests
{
    private static CliModuleSet CreateModules(Action<CliModuleSet>? configure = null)
    {
        var modules = CliModules.Create();
        configure?.Invoke(modules);
        return modules;
    }

    [Fact]
    public void TryBuild_NoComparisonFlags_ReturnsNullRequest()
    {
        Assert.True(CreateModules().Comparison.TryBuild(out var request));
        Assert.Null(request);
    }

    [Fact]
    public void Parse_CompareManifests_SetsModuleState()
    {
        var modules = CreateModules(m => m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json"));
        Assert.True(modules.Comparison.HasComparisonRequest);
    }

    [Fact]
    public void TryApply_MissingValue_ReturnsFalse()
    {
        Assert.False(CreateModules().Comparison.TryApply("--comparison-mode", null));
    }

    // REQ-177/REQ-178: companion flags without the main flag must fail.
    [Fact]
    public void TryBuild_ComparisonMode_WithoutCompareManifests_ReturnsFalse()
    {
        var modules = CreateModules(m => m.Comparison.TryApply("--comparison-mode", "replacement"));
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    [Fact]
    public void TryBuild_ComparisonOutput_WithoutCompareManifests_ReturnsFalse()
    {
        var modules = CreateModules(m => m.Comparison.TryApply("--comparison-output", "/tmp/report.json"));
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    // REQ-176/REQ-178: compare requires both companions.
    [Fact]
    public void TryBuild_CompareManifests_WithoutComparisonMode_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    [Fact]
    public void TryBuild_CompareManifests_WithoutComparisonOutput_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", "replacement");
        });
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("supplemental")]
    [InlineData("reproduction")]
    [InlineData("REPRODUCTION")]
    public void TryBuild_ValidMode_ReturnsRequest(string mode)
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", mode);
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.True(modules.Comparison.TryBuild(out var request));
        Assert.NotNull(request);
        Assert.Equal("/tmp/a.json,/tmp/b.json", request!.ManifestPaths);
        Assert.Equal("/tmp/report.json", request.OutputPath);
    }

    [Fact]
    public void TryBuild_InvalidMode_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", "swap");
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    // REQ-179: valid trio short-circuits without any generation-flag validation.
    [Fact]
    public void TryBuild_ValidTrio_DoesNotTouchOtherModules()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", "replacement");
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.True(modules.Comparison.TryBuild(out var request));
        Assert.NotNull(request);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ComparisonModuleTests"`
Expected: FAIL to compile — `ComparisonModule` / `CliModuleSet.Comparison` undefined.

- [ ] **Step 3: Create `ComparisonModule.cs`**

```csharp
namespace Zipper.Cli.Modules;

/// <summary>Owns the Production Manifest comparison flags (--compare-production-manifests / --comparison-mode / --comparison-output): parse, validate, and build ComparisonRequest (REQ-176–179).</summary>
public sealed class ComparisonModule : CliModule
{
    private string? _compareManifests;
    private string? _comparisonMode;
    private string? _comparisonOutput;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--compare-production-manifests", "--comparison-mode", "--comparison-output",
    };

    public bool HasComparisonRequest => !string.IsNullOrEmpty(_compareManifests);

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--compare-production-manifests":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --compare-production-manifests requires a value.");
                    return false;
                }
                _compareManifests = value;
                return true;
            case "--comparison-mode":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --comparison-mode requires a value.");
                    return false;
                }
                _comparisonMode = value;
                return true;
            case "--comparison-output":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --comparison-output requires a value.");
                    return false;
                }
                _comparisonOutput = value;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    /// <summary>
    /// Validates the comparison trio per REQ-176/177/178. Message order mirrors the old
    /// CliValidator comparison branch byte-for-byte. Returns true with a null request when
    /// no comparison flags were given. REQ-179: this short-circuits generation validation.
    /// </summary>
    public bool TryBuild(out ComparisonRequest? request)
    {
        if (!HasComparisonRequest)
        {
            if (!string.IsNullOrEmpty(_comparisonMode) || !string.IsNullOrEmpty(_comparisonOutput))
            {
                Console.Error.WriteLine("Error: --comparison-mode and --comparison-output require --compare-production-manifests to be specified.");
                request = null;
                return false;
            }
            request = null;
            return true;
        }

        if (string.IsNullOrEmpty(_comparisonMode))
        {
            Console.Error.WriteLine("Error: --comparison-mode is required when using --compare-production-manifests.");
            request = null;
            return false;
        }

        if (!string.Equals(_comparisonMode, "replacement", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_comparisonMode, "supplemental", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_comparisonMode, "reproduction", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --comparison-mode must be 'replacement', 'supplemental', or 'reproduction'.");
            request = null;
            return false;
        }

        if (string.IsNullOrEmpty(_comparisonOutput))
        {
            Console.Error.WriteLine("Error: --comparison-output is required when using --compare-production-manifests.");
            request = null;
            return false;
        }

        request = new ComparisonRequest(_compareManifests!, _comparisonMode, _comparisonOutput);
        return true;
    }
}

/// <summary>Validated Production Manifest comparison request (REQ-176/177/178).</summary>
public sealed record ComparisonRequest(string ManifestPaths, string Mode, string OutputPath);
```

- [ ] **Step 4: Wire `Comparison` into `CliModuleSet` and add `CliModuleSet.Parse`** (`src/Cli/Modules/CliModules.cs`)

Replace the whole file body after the `namespace` line:

```csharp
namespace Zipper.Cli.Modules;

/// <summary>
/// One constructed set of CLI modules. Pipeline must parse and TryBuild against
/// the same instances (TryApply mutates fields). Do not new the modules twice.
/// </summary>
public sealed class CliModuleSet
{
    public required ProductionModule Production { get; init; }
    public required SourceInputModule SourceInput { get; init; }
    public required OutputModule Output { get; init; }
    public required BatesModule Bates { get; init; }
    public required MetadataModule Metadata { get; init; }
    public required LoadFileModule LoadFile { get; init; }
    public required DelimiterModule Delimiter { get; init; }
    public required TiffModule Tiff { get; init; }
    public required ChaosModule Chaos { get; init; }
    public required HashModule Hash { get; init; }
    public required ComparisonModule Comparison { get; init; }
    public IReadOnlyList<CliModule> All => new CliModule[] { Production, SourceInput, Output, Bates, Metadata, LoadFile, Delimiter, Tiff, Chaos, Hash, Comparison };

    /// <summary>
    /// Token reader + module dispatcher: for each token finds the owning module, pulls a
    /// value when the flag takes one, and delegates to the module's TryApply. Successor of
    /// the old CliParser loop (which also handled the comparison trio — now a module too).
    /// </summary>
    public bool Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var modules = All;
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
                        return false;
                    }
                    i++;
                }

                if (!module.TryApply(arg, value))
                {
                    return false;
                }
                continue;
            }

            Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{args[i]}'");
            return false;
        }

        return true;
    }

    private static bool TryGetValue(string[] args, int currentIndex, out string value)
    {
        if (currentIndex + 1 < args.Length && !args[currentIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[currentIndex + 1];
            return true;
        }

        value = string.Empty;
        return false;
    }
}

/// <summary>
/// Registry of all CLI modules. Extended one module per phase until every
/// sub-domain of FileGenerationRequest is owned by a module.
/// </summary>
public static class CliModules
{
    public static CliModuleSet Create()
    {
        return new CliModuleSet
        {
            Production = new ProductionModule(),
            SourceInput = new SourceInputModule(),
            Output = new OutputModule(),
            Bates = new BatesModule(),
            Metadata = new MetadataModule(),
            LoadFile = new LoadFileModule(),
            Delimiter = new DelimiterModule(),
            Tiff = new TiffModule(),
            Chaos = new ChaosModule(),
            Hash = new HashModule(),
            Comparison = new ComparisonModule(),
        };
    }
}
```

**Atomicity note (Phase 3 plan lesson):** `ComparisonModule` must be registered in `Create()` *and* `Program` rewired in the same cut-over. After `Comparison` is in `All`, `CliParser.Parse(args)` (the one-arg overload still used by leftover tests) also routes the trio into a module — but those leftover tests never pass comparison flags, so they stay green. `Program` must not keep calling `CliParser.Parse(args)` after registration: the module would swallow the tokens and `parsedArgs.CompareProductionManifests` would stay null. Do Steps 4 and 5 together. Do **not** add `Pipeline.Build(CliModuleSet)` in this task.

- [ ] **Step 5: Rewire `Program.cs`** (comparison short-circuit + single parse + empty-args help)

Replace the block from `if (args is not null && args.Length > 0)` through `var request = Cli.Pipeline.Build(args!);` (`src/Program.cs:44–79`) with:

```csharp
        var modules = Cli.Modules.CliModules.Create();
        if (args.Length == 0)
        {
            Cli.HelpTextGenerator.Show();
            return 1;
        }

        if (!modules.Parse(args))
        {
            return 1;
        }

        if (!modules.Comparison.TryBuild(out var comparison))
        {
            return 1;
        }

        if (comparison is not null)
        {
            try
            {
                var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                    comparison.ManifestPaths, comparison.Mode, comparison.OutputPath).ConfigureAwait(false);
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        var request = Cli.Pipeline.Build(args);
        if (request is null)
        {
            return 1;
        }
```

Notes:
- `HelpTextGenerator.Show()` is `internal` in `Zipper.Cli`; `Program` is in `Zipper` (same assembly) — accessible.
- The previous `parsedArgs.ComparisonMode ?? "replacement"` / `ComparisonOutput ?? string.Empty` fallbacks are dropped — `comparison.Mode`/`OutputPath` are non-null after `TryBuild`.
- `--version` / `--benchmark` / `--chaos-list` early returns above this block are unchanged.
- Keep `Pipeline.Build(args)` here. `Pipeline.Build(CliModuleSet)` does not exist until Task 3. The generation path therefore reparses `args` until Task 3 switches the call. That is deliberate and temporary.

- [ ] **Step 6: Create `CliModuleSetTests.cs`** (full parse/dispatch port of `CliParserTests` — this is the whole file, not a stub)

```csharp
using Xunit;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class CliModuleSetTests
{
    [Fact]
    public void Parse_RequiredArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Path.Combine(Directory.GetCurrentDirectory(), "test") });
        Assert.True(ok);
        Assert.Equal("pdf", modules.Output.FileType);
        Assert.Equal(100, modules.Output.Count);
    }

    [Fact]
    public void Parse_MissingTypeValue_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type" }));
    }

    [Fact]
    public void Parse_MissingValueForValueTakingFlags_ReturnsFalse()
    {
        var flags = new[]
        {
            "--type", "--count", "--output-path", "--delimiter-column", "--delimiter-quote", "--delimiter-newline",
            "--folders", "--encoding", "--distribution", "--attachment-rate", "--target-zip-size",
            "--load-file-format", "--bates-prefix", "--bates-start", "--bates-digits", "--tiff-pages",
            "--column-profile", "--seed", "--date-format", "--empty-percentage", "--custodian-count",
                "--load-file-formats", "--dat-delimiters", "--loadfile-format", "--eol", "--col-delim",
            "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim", "--chaos-amount",
            "--chaos-types", "--chaos-scenario", "--volume-size", "--hash-mode", "--hash-algorithms",
            "--compare-production-manifests", "--comparison-mode", "--comparison-output"
        };

        foreach (var flag in flags)
        {
            Assert.False(CliModules.Create().Parse(new[] { flag }), $"Expected false when {flag} is missing a value");
        }
    }

    [Fact]
    public void Parse_UnknownFlag_ReturnsFalse()
    {
        var ok = CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--unknown-flag" });
        Assert.False(ok);
    }

    [Fact]
    public void Parse_UnknownPositionalValue_ReturnsFalse()
    {
        var ok = CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "extra_value" });
        Assert.False(ok);
    }

    [Fact]
    public void Parse_UnknownFlagInValuePosition_ReturnsFalse()
    {
        var ok = CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "--unknown-flag" });
        Assert.False(ok);
    }

    [Fact]
    public void Parse_WithNullArgs_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CliModules.Create().Parse(null!));
    }

    [Fact]
    public void Parse_ChaosListNotConsumedAsValueForPrecedingArg_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "--chaos-list" }));
    }

    [Fact]
    public void Parse_BenchmarkNotConsumedAsValueForPrecedingArg_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "--benchmark" }));
    }

    [Fact]
    public void Parse_InvalidCount_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "not-a-number", "--output-path", Directory.GetCurrentDirectory() }));
    }

    [Fact]
    public void Parse_InvalidFolders_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--folders", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidAttachmentRate_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--attachment-rate", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidBatesStart_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-start", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidBatesDigits_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-digits", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidSeed_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--seed", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidEmptyPercentage_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--empty-percentage", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidCustodianCount_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--custodian-count", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidVolumeSize_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--volume-size", "notanumber" }));
    }

    [Fact]
    public void Parse_AllBooleanFlags_SetCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--type", "pdf", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--with-metadata", "--with-text", "--include-load-file", "--with-families", "--loadfile-only", "--production-set", "--production-zip" });
        Assert.True(ok);
        Assert.True(modules.Metadata.WithMetadata);
        Assert.True(modules.Output.WithText);
        Assert.True(modules.Output.IncludeLoadFile);
        Assert.True(modules.Metadata.WithFamilies);
        Assert.True(modules.LoadFile.LoadfileOnly);
        Assert.True(modules.Production.ProductionSet);
        Assert.True(modules.Production.ProductionZip);
    }

    [Fact]
    public void Parse_LoadfileOnlyArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--loadfile-only", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-format", "opt" });
        Assert.True(ok);
        Assert.True(modules.LoadFile.LoadfileOnly);
        Assert.True(modules.LoadFile.IsLoadFileFormatExplicit);
        Assert.Equal(LoadFileFormat.Opt, modules.LoadFile.CurrentFormat);
    }

    [Fact]
    public void Parse_ProductionSetArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6", "--volume-size", "1000", "--count", "20", "--output-path", Directory.GetCurrentDirectory() });
        Assert.True(ok);
        Assert.True(modules.Production.ProductionSet);
        Assert.Equal("CL001", modules.Bates.BatesPrefix);
        Assert.Equal(100, modules.Bates.BatesStart);
        Assert.Equal(6, modules.Bates.BatesDigits);
        Assert.Equal(1000, modules.Production.VolumeSize);
    }

    [Fact]
    public void Parse_ColumnProfileArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "standard", "--seed", "42", "--date-format", "yyyy-MM-dd", "--empty-percentage", "15", "--custodian-count", "50" });
        Assert.True(ok);
        Assert.Equal("standard", modules.Metadata.ColumnProfile);
        Assert.Equal(42, modules.Metadata.Seed);
        Assert.Equal("yyyy-MM-dd", modules.Metadata.DateFormat);
        Assert.Equal(15, modules.Metadata.EmptyPercentage);
        Assert.Equal(50, modules.Metadata.CustodianCount);
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ComparisonModuleTests|FullyQualifiedName~CliModuleSetTests"`
Expected: PASS.

- [ ] **Step 8: Full suite check**

```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj
```
Expected: green. `CliParserTests`/`CliValidatorTests` still exist and pass (`CliParser.Parse(args)` still behaves identically — those tests never pass comparison flags, so the now-dead comparison `switch` is unused). `ComparisonTests` is **not** a `Program.Main` guard — it calls `ProductionManifestComparer.CompareAndReportAsync` directly. The real Program cut-over guard is `tests/test-production-sets.sh` Test 10 (valid trio + invalid `--comparison-mode swap`). Run that script in Task 5, not here. After this task `Program` parses once for comparison, then `Pipeline.Build(args)` parses a **second** set for generation. That double-parse is temporary and dies in Task 3.

- [ ] **Step 9: Commit**

```bash
git add src/Cli/Modules/ComparisonModule.cs src/Cli/Modules/CliModules.cs src/Program.cs src/Zipper.Tests/Modules/ComparisonModuleTests.cs src/Zipper.Tests/Cli/CliModuleSetTests.cs
git commit -m "refactor(cli): add ComparisonModule and move token dispatch to CliModuleSet.Parse (Phase 4 of #750)"
```

---

### Task 3: `CrossCuttingRules` + `Pipeline` rewrite + delete the waterfall (cut-over)

**Files:**
- Create: `src/Cli/CrossCuttingRules.cs`, `src/Zipper.Tests/Cli/CrossCuttingRulesTests.cs`, `src/Zipper.Tests/Cli/PipelineTests.cs`, `src/Zipper.Tests/Cli/PipelineTestHelper.cs`
- Modify: `src/Cli/Pipeline.cs`, `src/Program.cs` (switch the Task 2 `Pipeline.Build(args)` to `Pipeline.Build(modules)` + rewrite the `SelectMode` comment at line 114), `src/Zipper.Tests/MixedFileTypeCliTests.cs`, `src/Zipper.Tests/SourceDrivenCliTests.cs`, `src/Zipper.Tests/Modules/OutputModuleTests.cs`
- Delete: `src/Cli/CliParser.cs`, `src/Cli/CliValidator.cs`, `src/Cli/ParsedArguments.cs`, `src/Cli/RequestBuilder.cs`, `src/Cli/Validation/StandardModeValidator.cs`, `src/Cli/Validation/ProductionSetValidator.cs`, `src/Cli/Validation/CrossCuttingValidator.cs`, `src/Zipper.Tests/Cli/CliParserTests.cs`, `src/Zipper.Tests/Cli/CliValidatorTests.cs`, `src/Zipper.Tests/Cli/RequestBuilderTests.cs`, `src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs`

**Interfaces:**
- Consumes: module getters from Task 1/2 (unchanged): `Output.FileType/FileTypes/Count/TargetZipSize`, `LoadFile.LoadfileOnly`, `Production.ProductionSet/RedactedProduction/SourcePathMode`, `SourceInput.HasSourceInput`, `Metadata.HasColumnProfile`, `Bates.HasBatesPrefix`.
- Produces:
  - `internal static class CrossCuttingRules` with `public static bool Validate(CliModuleSet modules)`.
  - `Pipeline.Build(string[] args)` (public, unchanged signature — empty args → help), `internal static FileGenerationRequest? Build(CliModuleSet modules)` (CrossCuttingRules + TryBuild chain + AssembleRequest), `private static FileGenerationRequest AssembleRequest(...)`.
  - `internal static class PipelineTestHelper` with `public static (bool Ok, CliModuleSet Modules) Parse(string[] args)`, `public static FileGenerationRequest? Build(CliModuleSet? modules = null, Action<CliModuleSet>? configureModules = null)`, `public static FileGenerationRequest? Build(string[] args)`.

- [ ] **Step 1: Write the failing `CrossCuttingRulesTests.cs`** (port of `CliValidatorTests` validation tests; construction swapped to module `TryApply`)

```csharp
using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class CrossCuttingRulesTests
{
    private static CliModuleSet CreateModules(Action<CliModuleSet>? configure = null)
    {
        var modules = CliModules.Create();
        configure?.Invoke(modules);
        return modules;
    }

    private static void ApplyValidBase(CliModuleSet modules)
    {
        modules.Output.TryApply("--type", "pdf");
        modules.Output.TryApply("--count", "10");
        modules.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
    }

    [Fact]
    public void Validate_ValidArgs_ReturnsTrue()
    {
        var modules = CreateModules(ApplyValidBase);
        Assert.True(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_MissingType_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--count", "10");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithoutType_ReturnsTrue()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--count", "10");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.True(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutType_ReturnsTrue()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--count", "10");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
            m.Production.TryApply("--production-set", null);
            m.Bates.TryApply("--bates-prefix", "PREFIX");
        });
        Assert.True(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_MissingCount_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--type", "pdf");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_TargetZipSizeWithoutCount_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--type", "pdf");
            m.Output.TryApply("--target-zip-size", "10MB");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ProductionSet_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_RedactedProduction_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--redacted-production", null);
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutBatesPrefix_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_Types_WithLoadfileOnly_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Output.TryApply("--types", "pdf:70,xls:30");
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_Types_WithColumnProfile_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Output.TryApply("--types", "pdf:70,xls:30");
            m.Metadata.TryApply("--column-profile", "standard");
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ColumnProfile_WithProductionSet_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
            m.Metadata.TryApply("--column-profile", "edrm-standard");
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~CrossCuttingRulesTests"`
Expected: FAIL to compile — `CrossCuttingRules` undefined.

- [ ] **Step 3: Create `CrossCuttingRules.cs`** (merge of `CliValidator` generation checks + `StandardModeValidator` + `ProductionSetValidator` + `CrossCuttingValidator`, byte-identical messages and order)

```csharp
using Zipper.Cli.Modules;

namespace Zipper.Cli;

/// <summary>
/// Cross-domain CLI validation that no single domain module owns: required-flag gates
/// (--type/--count) plus the Standard / Production Set / cross-domain conflict checks.
/// Runs after parse, before any TryBuild. Successor of CliValidator + the three mode
/// validators (StandardModeValidator, ProductionSetValidator, CrossCuttingValidator).
/// Comparison-mode validation lives in ComparisonModule — it short-circuits before this
/// ever runs (REQ-179).
/// </summary>
internal static class CrossCuttingRules
{
    public static bool Validate(CliModuleSet modules)
    {
        // Source-Driven Generation (--input-csv/--directory-template) supplies File Types and
        // the File Count from Source Records, so --type and --count are not required with it.
        bool hasSourceInput = modules.SourceInput.HasSourceInput;

        if (string.IsNullOrEmpty(modules.Output.FileType) && modules.Output.FileTypes is null && !modules.LoadFile.LoadfileOnly && !modules.Production.ProductionSet && !hasSourceInput)
        {
            Console.Error.WriteLine("Error: --type is required.");
            return false;
        }

        if (!modules.Output.Count.HasValue && !hasSourceInput)
        {
            Console.Error.WriteLine("Error: --count is required.");
            return false;
        }

        if (!ValidateStandardMode(modules) ||
            !ValidateProductionSet(modules) ||
            !ValidateCrossCutting(modules))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateStandardMode(CliModuleSet modules)
    {
        if (string.IsNullOrEmpty(modules.Output.TargetZipSize))
        {
            return true;
        }

        if (!modules.Output.Count.HasValue && !modules.SourceInput.HasSourceInput)
        {
            Console.Error.WriteLine("Error: --target-zip-size requires --count to be specified.");
            return false;
        }

        return true;
    }

    private static bool ValidateProductionSet(CliModuleSet modules)
    {
        if (modules.Production.ProductionSet && modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --production-set conflicts with --loadfile-only.");
            return false;
        }

        if (modules.Production.ProductionSet && !modules.Bates.HasBatesPrefix)
        {
            Console.Error.WriteLine("Error: --production-set requires --bates-prefix.");
            return false;
        }

        if (modules.Production.RedactedProduction && modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --redacted-production conflicts with --loadfile-only.");
            return false;
        }

        return true;
    }

    private static bool ValidateCrossCutting(CliModuleSet modules)
    {
        var fileTypes = modules.Output.FileTypes;
        if (fileTypes is not null && modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --types is not supported with --loadfile-only.");
            return false;
        }

        if (fileTypes is not null && modules.Metadata.HasColumnProfile)
        {
            Console.Error.WriteLine("Error: --types is not supported with --column-profile. Use --type for profile-driven generation.");
            return false;
        }

        if (modules.Production.SourcePathMode is not null)
        {
            if (!modules.Production.ProductionSet)
            {
                Console.Error.WriteLine("Error: --source-path-mode requires --production-set.");
                return false;
            }

            if (!modules.SourceInput.HasSourceInput)
            {
                Console.Error.WriteLine("Error: --source-path-mode requires --input-csv or --directory-template.");
                return false;
            }
        }

        if (modules.SourceInput.HasSourceInput)
        {
            if (!string.IsNullOrEmpty(modules.Output.FileType))
            {
                Console.Error.WriteLine("Error: --type cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
                return false;
            }

            if (modules.Output.FileTypes is not null)
            {
                Console.Error.WriteLine("Error: --types cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
                return false;
            }
        }

        if (modules.Metadata.HasColumnProfile && modules.Production.ProductionSet)
        {
            Console.Error.WriteLine("Error: --column-profile is not supported with --production-set.");
            return false;
        }

        return true;
    }
}
```

- [ ] **Step 4: Rewrite `Pipeline.cs`**

```csharp
using Zipper.Cli.Modules;
using Zipper.Config;

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
        if (!modules.Parse(args))
            return null;

        return Build(modules);
    }

    internal static FileGenerationRequest? Build(CliModuleSet modules)
    {
        if (!CrossCuttingRules.Validate(modules))
            return null;

        if (!modules.Production.TryBuild(out var production) ||
            !modules.Bates.TryBuild(production.ProductionSet, production.RollingCount, production.RollingBatesMode.ToString(), modules.Output.Count, out var bates) ||
            !modules.SourceInput.TryBuild(modules.Output.Count, production.ProductionSet, bates, out var sourceRecords) ||
            !modules.Output.TryBuild(sourceRecords, out var output) ||
            !modules.Metadata.TryBuild(output.HasFileType("eml"), modules.SourceInput.HasSourceInput, out var metadata) ||
            !modules.LoadFile.TryBuild(modules.Metadata.AttachmentRate, modules.Output.Encoding, modules.Output.IsEncodingExplicit, modules.Output.Distribution, modules.Output.TargetZipSize, modules.Output.IncludeLoadFile, out var loadFile) ||
            !modules.Delimiter.TryBuild(modules.LoadFile.LoadfileOnly, production.ProductionSet, out var delimiters) ||
            !modules.Tiff.TryBuild(out var tiff) ||
            !modules.Chaos.TryBuild(modules.LoadFile.LoadfileOnly, modules.LoadFile.CurrentFormat, out var chaos) ||
            !modules.Hash.TryBuild(modules.LoadFile.LoadfileOnly, out var hash))
        {
            return null;
        }

        return AssembleRequest(
            output, metadata, loadFile, delimiters, bates, tiff, chaos, hash, production, sourceRecords,
            modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
    }

    private static FileGenerationRequest AssembleRequest(
        OutputConfig output,
        MetadataConfig metadata,
        LoadFileConfig loadFile,
        DelimiterConfig delimiters,
        BatesNumberConfig? bates,
        TiffConfig tiff,
        ChaosConfig chaos,
        HashConfig hash,
        ProductionConfig production,
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords,
        bool loadfileOnly,
        bool isLoadFileFormatExplicit)
    {
        // The image-type override (image-only runs get both DAT and OPT load files) keys off
        // whether the user explicitly chose formats. hasImageType reads output.FileType /
        // output.FileTypeRatios / sourceRecords — it cannot move to LoadFileModule.
        if (!isLoadFileFormatExplicit)
        {
            var hasImageType = output.FileType is "tiff" or "jpg"
                || (output.FileTypeRatios?.Any(r => r.Type is "tiff" or "jpg") ?? false)
                || (sourceRecords?.Any(r => r.FileType is "tiff" or "jpg") ?? false);
            if (hasImageType)
            {
                loadFile = loadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };
            }
        }

        return new FileGenerationRequest
        {
            Output = output,
            Metadata = metadata,
            LoadFile = loadFile,
            Delimiters = delimiters,
            Bates = bates,
            Tiff = tiff,
            Chaos = chaos,
            Production = production,
            LoadfileOnly = loadfileOnly,
            Hash = hash,
            SourceRecords = sourceRecords,
        };
    }
}
```

This absorbs `RequestBuilder.Build` verbatim (image-type override + construction); the `ArgumentNullException` guards are dropped per D3. `AssembleRequest`'s `IReadOnlyList<SourceInput.SourceRecord>` type resolves the same way `RequestBuilder.Build` does today (parent namespace `Zipper.SourceInput` — no extra using).

- [ ] **Step 4b: Switch `Program.cs` off the second parse**

Replace the Task 2 line `var request = Cli.Pipeline.Build(args);` with `var request = Cli.Pipeline.Build(modules);`. This is the cut-over that actually deletes the discarded-set wart. Also rewrite the `SelectMode` doc comment (`src/Program.cs:114`): `The CLI validator ensures LoadfileOnly and ProductionSet are mutually exclusive.` → `CrossCuttingRules ensures LoadfileOnly and ProductionSet are mutually exclusive.`

- [ ] **Step 5: Replace `RequestBuilderTestHelper.cs` with `PipelineTestHelper.cs`** (delete the old file, create the new)

```csharp
using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

internal static class PipelineTestHelper
{
    public static (bool Ok, CliModuleSet Modules) Parse(string[] args)
    {
        var modules = CliModules.Create();
        return (modules.Parse(args), modules);
    }

    public static FileGenerationRequest? Build(
        CliModuleSet? modules = null,
        Action<CliModuleSet>? configureModules = null)
    {
        modules ??= CliModules.Create();
        configureModules?.Invoke(modules);
        return Cli.Pipeline.Build(modules);
    }

    public static FileGenerationRequest? Build(string[] args)
        => Cli.Pipeline.Build(args);
}
```

**Note:** `Build(modules:)` now runs `CrossCuttingRules.Validate` (the old helper skipped validation and ran only the `TryBuild` chain). Every existing call site's flag set is cross-cutting-valid (verified: the `Build_*` sets in `RequestBuilderTests`, the REQ-106/164 path sets, and `OutputModuleTests` `TryBuild_SingleType_*` sets all satisfy `--type`/`--count`/conflict rules), so no test needs flag changes.

- [ ] **Step 6: Delete the waterfall files**

```bash
git rm src/Cli/CliParser.cs src/Cli/CliValidator.cs src/Cli/ParsedArguments.cs src/Cli/RequestBuilder.cs
git rm src/Cli/Validation/StandardModeValidator.cs src/Cli/Validation/ProductionSetValidator.cs src/Cli/Validation/CrossCuttingValidator.cs
```

- [ ] **Step 7: Create `PipelineTests.cs`** (retarget of `RequestBuilderTests` `Build_*` + `CliParserTests` REQ-106/164 path tests)

```csharp
using Xunit;
using Zipper.Cli;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class PipelineTests
{
    [Fact]
    public void Build_StandardMode_SetsAllDefaults()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory() });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);

        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
        Assert.Equal(100, result!.Output.FileCount);
        Assert.Equal("pdf", result!.Output.FileType);
        Assert.Equal(1, result!.Output.Folders);
        Assert.Equal(DistributionType.Proportional, result!.LoadFile.Distribution);
        Assert.False(result!.Metadata.WithMetadata);
        Assert.False(result!.Output.WithText);
        Assert.False(result!.Output.IncludeLoadFile);
        Assert.Equal(0, result!.LoadFile.AttachmentRate);
        Assert.Null(result!.Bates);
    }

    [Fact]
    public void Build_WithValidPath_ResolvesDirectory()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory() });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
    }

    [Fact]
    public void Build_LoadfileOnly_SetsProperties()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only", "--load-file-format", "opt" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.True(result!.LoadfileOnly);
        Assert.Single(result!.LoadFile.Formats);
        Assert.Equal(LoadFileFormat.Opt, result!.LoadFile.Formats[0]);
    }

    [Fact]
    public void Build_ProductionSet_SetsVolumeSize()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--production-set", "--bates-prefix", "PREFIX", "--volume-size", "1000" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.True(result!.Production.ProductionSet);
        Assert.Equal(1000, result!.Production.VolumeSize);
    }

    [Fact]
    public void Build_BatesConfig_SetsCorrectly()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result!.Bates);
        Assert.Equal("CL001", result!.Bates.Prefix);
        Assert.Equal(100, result!.Bates.Start);
        Assert.Equal(6, result!.Bates.Digits);
    }

    [Fact]
    public void Build_ColumnProfile_LoadsProfile()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "standard" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result!.Metadata.ColumnProfile);
    }

    [Fact]
    public void Build_MultiFormat_CreatesFormatList()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--load-file-formats", "dat,opt,csv" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.Equal(3, result!.LoadFile.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Opt, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Csv, result!.LoadFile.Formats);
    }

    [Fact]
    public void Build_LoadfileOnlyEncoding_UsesExtendedSet()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only", "--encoding", "WINDOWS-1252" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.Equal("WINDOWS-1252", result!.LoadFile.Encoding);
    }

    [Fact]
    public void Build_Encoding_PreservesNormalizedInputName()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--encoding", "UTF-16" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.Equal("UTF-16", result!.LoadFile.Encoding);
    }

    // --- REQ-106: relative output paths resolve against CWD, traversal outside CWD rejected ---

    [Fact]
    public void Build_OutputPathWithParentTraversal_RejectsPathOutsideCwd()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "../escape" });
        Assert.True(ok);
        Assert.Null(PipelineTestHelper.Build(modules: modules));
    }

    [Fact]
    public void Build_OutputPathWithinCwd_IsAccepted()
    {
        var uniqueDirName = "output_" + Guid.NewGuid().ToString("N");
        try
        {
            var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", uniqueDirName });
            Assert.True(ok);
            Assert.NotNull(PipelineTestHelper.Build(modules: modules));
        }
        finally
        {
            if (Directory.Exists(uniqueDirName))
            {
                Directory.Delete(uniqueDirName, recursive: true);
            }
        }
    }

    // --- REQ-164: custom column profile paths resolve against CWD, traversal rejected ---

    [Fact]
    public void Build_ColumnProfileWithParentTraversal_RejectsPathOutsideCwd()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "../outside-profile.json" });
        Assert.True(ok);
        Assert.Null(PipelineTestHelper.Build(modules: modules));
    }

    [Fact]
    public void Build_ColumnProfileWithinCwd_IsAccepted()
    {
        var tempProfilePath = Path.Combine(Directory.GetCurrentDirectory(), "temp_profile_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var validProfileJson = @"{
                ""name"": ""TempProfile"",
                ""columns"": [{ ""name"": ""DocID"", ""type"": ""identifier"" }],
                ""dataSources"": {}
            }";
            File.WriteAllText(tempProfilePath, validProfileJson);

            var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", tempProfilePath });
            Assert.True(ok);
            Assert.NotNull(PipelineTestHelper.Build(modules: modules));
        }
        finally
        {
            if (File.Exists(tempProfilePath))
            {
                File.Delete(tempProfilePath);
            }
        }
    }
}
```

- [ ] **Step 8: Update remaining `RequestBuilderTestHelper` call sites**

Mechanical swaps (`var (result, modules) = RequestBuilderTestHelper.Parse(...)` → `var (ok, modules) = PipelineTestHelper.Parse(...)`; `Assert.NotNull(result)` → `Assert.True(ok)`):

- `src/Zipper.Tests/MixedFileTypeCliTests.cs:27` — `Parse_TypesArgument_StoresRawValue`
- `src/Zipper.Tests/SourceDrivenCliTests.cs:58–66` — `Parse_InputCsvAndDirectoryTemplate_StoreRawValues` (two parses: `result`/`modules` and `resultDir`/`modulesDir`)
- `src/Zipper.Tests/Modules/OutputModuleTests.cs:345–358` — `TryBuild_SingleType_MatchesRequestBuilderAssembly` (`var parsed = RequestBuilderTestHelper.Parse(...)` → `var (ok, parsedModules) = PipelineTestHelper.Parse(...)`; `Assert.NotNull(parsed.Parsed)` → `Assert.True(ok)`; `RequestBuilderTestHelper.Build(modules: parsed.Modules)` → `PipelineTestHelper.Build(modules: parsedModules)`). Method name can stay; it is cosmetic.
- `src/Zipper.Tests/Modules/OutputModuleTests.cs:374` — `TryBuild_SingleTypeDefaults_MatchRequestBuilder` does **not** call the helper. Leave the body alone.

- [ ] **Step 9: Delete the three old test files** (content already redistributed)

```bash
git rm src/Zipper.Tests/Cli/CliParserTests.cs src/Zipper.Tests/Cli/CliValidatorTests.cs src/Zipper.Tests/Cli/RequestBuilderTests.cs
```

- [ ] **Step 10: Verify**

```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj
```
Expected: green. Watch for:
- Any `using Zipper.Cli;` that becomes unused in the deleted/edited test files — `dotnet format` + build (Warnings as Errors) will catch it.
- `ChaosAnomalyTypes.cs` still compiles (comment-only reference to `CliValidator` — fixed in Task 4).
- Grep confirms nothing else references the deleted types: `rg -n "CliParser|CliValidator|ParsedArguments|RequestBuilder" src/` should return **zero** hits in `src/Cli/` after Task 4 rewrites the leftover comments. Before Task 4 the only remaining hits are comments (`OutputModule.cs:120–121/134–136/227`, `BatesModule.cs:88–89`, `MetadataModule.cs:66–67`, `ProductionModule.cs:118–119`, `SourceInputModule.cs:40–41`, `HashModule.cs:70–73`, `DelimiterModule.cs:49`, `ChaosAnomalyTypes.cs:5`, `Program.cs:114`). Test method names like `TryBuild_SingleType_MatchesRequestBuilderAssembly` are fine — they're just names.

- [ ] **Step 11: Commit**

```bash
git add -A src/Cli src/Zipper.Tests
git commit -m "refactor(cli): replace CliParser/CliValidator/RequestBuilder waterfall with CrossCuttingRules + Pipeline assembly (Phase 4 of #750)"
```

---

### Task 4: Stale comments + `docs/architecture.md`

**Files:**
- Modify: `src/ChaosAnomalyTypes.cs`, `src/Cli/Modules/{OutputModule,LoadFileModule,MetadataModule,SourceInputModule,BatesModule,ProductionModule,HashModule,DelimiterModule}.cs` (comments only), `src/Program.cs` (only if the `SelectMode` comment was not already rewritten in Task 3), `docs/architecture.md`

- [ ] **Step 1: Fix the stale comment in `src/ChaosAnomalyTypes.cs:5`**

Current: `/// Single source of truth consumed by both CliValidator and ChaosEngine.`
New: `/// Single source of truth consumed by ChaosModule and ChaosEngine.`
(`ChaosModule` is the actual CLI consumer — `CliValidator` never read this catalog.)

- [ ] **Step 2: Fix the transitional "ParsedArguments deletes its X fields" comments in the modules**

These comments said the getters were temporary until `ParsedArguments` deleted the fields. `ParsedArguments` is gone; the getters are now the permanent sibling channel. Rewrite each to state that:

- `OutputModule.cs:120–121` — `// Transitional (Phase 3): test-facing raw state so CliParserTests/RequestBuilderTests can assert module ownership; ParsedArguments deletes its output fields and these move too.` → `// Sibling-channel + test-facing raw state. CrossCuttingRules and other modules read these getters.`
- `OutputModule.cs:134–136` — rewrite the check-order comment that still names `CliValidator` / `StandardModeValidator` / `CrossCuttingValidator` to name `CrossCuttingRules` + this module's own TryBuild checks.
- `LoadFileModule.cs:58–59` — already accurate (`were bag fields pre-Phase-3; OutputModule now owns them`). Leave it.
- `MetadataModule.cs:66–67` — same sibling-channel rewrite as OutputModule.
- `SourceInputModule.cs:40–41` — same.
- `BatesModule.cs:88–89` — same.
- `ProductionModule.cs:118–119` — same (the comment is two lines starting at 118, not a single line 120).
- `HashModule.cs:70–73` — drop "moves to CrossCuttingRules in Phase 4". The `--hash-mode actual` × `--loadfile-only` check stays in `HashModule.TryBuild(loadfileOnly, …)` as a sibling-parameter check. Rewrite to: `// Sibling-parameter check: loadfileOnly is owned by LoadFileModule.`
- `DelimiterModule.cs:49` — same: `--eol` stays in `DelimiterModule.TryBuild`. Rewrite to: `// Sibling-parameter check: --eol is only valid with loadfile-only or production-set.`

Read each comment and rewrite to remove the `ParsedArguments` forward-reference; do not touch any logic.

- [ ] **Step 3: Update `docs/architecture.md` Component Map** (Critical Rule 5 — diagram change, same-PR)

Replace the CLI Layer subgraph (`docs/architecture.md:72–85`):

```mermaid
    subgraph CLI Layer
        Program["Program.cs<br/>(parse + comparison short-circuit + SelectMode dispatch)"]
        Pipeline["Pipeline.Build<br/>(CrossCuttingRules + TryBuild chain + AssembleRequest)"]
        Modules["Domain Modules<br/>Hash / Delimiter / Tiff / Chaos / Bates / Metadata / LoadFile / Production / SourceInput / Output / Comparison<br/>(incl. column profile)"]
        CrossCutting["CrossCuttingRules<br/>(required-flag gates + cross-domain checks)"]
        Program --> Pipeline
        Program --> Modules
        Pipeline --> Modules
        Modules --> Pipeline
        Pipeline --> CrossCutting
    end
```

Replace `docs/architecture.md:147` (`Program --> Pipeline --> RequestBuilder --> FGR`) with:

```
    Program --> Pipeline --> FGR
```

Replace the phase note (`docs/architecture.md:161`) with:

```
*Phase 1–4 of #750 complete: all eleven parse/validate/build domains moved into `CliModule`s (Hash/Delimiter/Tiff/Chaos/Bates/Metadata/LoadFile/Production/SourceInput/Output/Comparison). `CliModuleSet.Parse` is the token reader + module dispatcher; `CrossCuttingRules.Validate` runs the generation-path cross-domain checks after parse. `Pipeline.Build` still runs the **ten**-module `TryBuild` chain in order Production → Bates → SourceInput → Output → Metadata → LoadFile → Delimiter → Tiff → Chaos → Hash (Comparison is not in that chain), then `Pipeline.AssembleRequest` applies the image-type load-file override and constructs `FileGenerationRequest`. `Program` parses once, short-circuits to the Production Manifest comparer when `ComparisonModule.TryBuild` yields a validated `ComparisonRequest` (REQ-179), and otherwise hands the same parsed set to `Pipeline.Build`. `CliParser`/`CliValidator`/`ParsedArguments`/`RequestBuilder` and the three mode validators are deleted.*
```

**This diagram edit is an architecture change under Critical Rule 5 — it requires explicit maintainer approval before merge. This plan document is not approval.** If the maintainer objects, stop and do not merge the PR.

- [ ] **Step 4: Verify**

```bash
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj
```
Expected: green (docs + comments only; no logic change).

- [ ] **Step 5: Commit**

```bash
git add docs/architecture.md src/ChaosAnomalyTypes.cs src/Cli/Modules src/Program.cs
git commit -m "docs(cli): update architecture component map for Phase 4 waterfall removal (Phase 4 of #750)"
```

---

### Task 5: Full verification (parity + E2E + gates)

**Files:** none (verification only).

- [ ] **Step 1: Build Release + format + full unit/analyzer suites**

```bash
dotnet build -c Release zipper.sln
dotnet format --verify-no-changes src/
dotnet test src/Zipper.Tests/Zipper.Tests.csproj
dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj
```
Expected: all green, 0 warnings, format clean.

- [ ] **Step 2: Golden parity (Critical Rule 6)**

```bash
./tests/goldens/run-goldens.sh
```
Expected: exit 0, all 20 scenarios byte-match their fixtures. Phase 4 changes no output-producing code path, so goldens must be untouched. If any scenario diverges, stop and root-cause before proceeding — do not `--capture`.

- [ ] **Step 3: E2E**

```bash
./tests/run-tests.sh
```
Expected: all suites pass, including `tests/test-production-sets.sh` Test 10 (valid comparison trio + invalid `--comparison-mode swap`). That script is the real `Program` comparison-path guard — `ComparisonTests` never calls `Program.Main`. If Test 10 fails, the Task 2/3 `Program` cut-over is the prime suspect.

- [ ] **Step 4: Docs sync check (Critical Rule 4)**

`grep -n "compare-production-manifests\|comparison-mode\|comparison-output" README.md Requirements.md UBIQUITOUS_LANGUAGE.md tests/*.sh tests/*.bat` — no change expected (no CLI behavior or format changed). Flag the Rule 4 no-op in the PR. Run `grep -n "REQ-176\|REQ-177\|REQ-178\|REQ-179" Requirements.md` and verify each is still implemented (they are — `ComparisonModule`).

- [ ] **Step 5: Adversarial review + PR**

Per AGENTS.md: run the autoreview skill (`.agents/skills/autoreview/SKILL.md`) before creating the PR — mandatory (this change touches logic, error handling, and public-ish contracts). PR body must include:
- `Refs #750` (never `Fixes`).
- `## Release Notes` per the Release Notes Mandate (this is a multi-file refactor — 3–5 sentences; plain prose, canonical domain terms).
- The **Architecture** checklist item acknowledging the `docs/architecture.md` diagram update and requesting the maintainer's Rule 5 approval (D1–D3 + the accepted micro-divergence also listed here).
- List the documented accepted divergences (multi-invalid precedence from Phases 1–3, unchanged; the new `Pipeline.Build` comparison-trio micro-divergence).

---

## Self-Review

**Spec coverage (#750 Phase 4):**
- "CrossCuttingRules" → Task 3 Step 3. ✔
- "delete ParsedArguments/RequestBuilder/CliValidator/validators" → Task 3 Steps 6/9. ✔
- Comparison trio leaves the deleted `CliParser`/`CliValidator` (REQ-176–179 still enforced) → Task 2. ✔
- Architecture diagram no longer names deleted classes → Task 4. ✔
- No behavior/byte change → Task 5 goldens/E2E gate. ✔

**Placeholder scan:** all new files contain complete code; no "TBD"/"similar to Task N". `OutputModuleTests.TryBuild_SingleTypeDefaults_MatchRequestBuilder` (line 374) is quoted as a no-op — it does not call the helper.

**Type consistency:** `CliModuleSet.Parse` (bool) used identically in `Program`/`Pipeline`/`PipelineTestHelper`; `CrossCuttingRules.Validate(CliModuleSet)` signature matches all tests; `ComparisonModule.TryBuild(out ComparisonRequest?)` matches `Program` usage; `ArgumentHelpers` signatures are `internal static` (same as today's `RequestBuilder` statics) and match `OutputModule`/`LoadFileModule` call sites and `ArgumentHelpersTests`. `Pipeline.AssembleRequest` parameter list matches the `TryBuild` chain outputs exactly as `RequestBuilder.Build` did. Task 2 keeps `Pipeline.Build(args)` so the solution compiles; Task 3 adds `Pipeline.Build(CliModuleSet)` and switches `Program`.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-14-cli-domain-modules-phase4.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
