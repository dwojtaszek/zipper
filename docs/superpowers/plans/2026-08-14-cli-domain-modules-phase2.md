# CLI Domain Modules — Phase 2 (Bates, Metadata, LoadFile) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the three medium-complexity domains (`BatesModule`, `MetadataModule`, `LoadFileModule`) out of the `CliParser → CliValidator → RequestBuilder` waterfall so each owns its arg registration, parsing, validation, and sub-config construction — completing Phase 1's module pattern for all non-cross-cutting flags — without any behavior or byte-level output change.

**Architecture:** Follows the Phase 1 seam exactly (`CliModule` base, `CliModuleSet`, `TryApply` during parse, `TryBuild` after `CliValidator.Validate`, typed configs into `RequestBuilder.Build`). Phase 2 differs from Phase 1 in one deliberate way: the leaf modules (Hash/Chaos/Delimiter) previously read sibling state from the still-present `ParsedArguments` bag. Phase 2 **deletes** the bag properties that sibling readers depend on, so sibling state is threaded explicitly instead:
- `HashModule.TryBuild(parsed, bool loadfileOnly, out HashConfig)` — replaces its `parsed.LoadfileOnly` read (`HashModule.cs:77`).
- `ChaosModule.TryBuild(parsed, bool loadfileOnly, LoadFileFormat currentFormat, out ChaosConfig)` — replaces its `parsed.LoadfileOnly` + `RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat ?? "dat")` reads (`ChaosModule.cs:36–45`). `currentFormat` is the **single-format** value (`_loadFileFormat ?? "dat"`), **not** `--load-file-formats` and **not** the image-type override. Do not "fix" that.
- `DelimiterModule.TryBuild(parsed, bool loadfileOnly, out DelimiterConfig)` — replaces its `parsed.LoadfileOnly` read (`DelimiterModule.cs:51`). It still reads `parsed.ProductionSet` from the bag (Phase 3). (Three Phase-1 modules read `LoadfileOnly` — Delimiter, Hash, Chaos — not just Hash/Chaos.)
- `LoadFileModule.TryBuild(parsed, int attachmentRate, out LoadFileConfig)` — reads `modules.Metadata.AttachmentRate` as a value param (consistency with the leaf modules; no module ever takes the whole set in `TryBuild`).
- The three remaining validators take `CliModuleSet modules` for the state they legitimately need: `CliValidator`, `ProductionSetValidator`, `CrossCuttingValidator`. `StandardModeValidator.Validate(ParsedArguments)` keeps its signature (it no longer reads any moved flag after trim).
- A few non-owned reads remain on the bag (`parsed.ProductionSet`/`RollingCount`/`RollingBatesMode`/`Count` in `BatesModule`; `parsed.FileType`/`FileTypes`/`InputCsv`/`DirectoryTemplate` in `MetadataModule`; `parsed.Encoding`/`IsEncodingExplicit`/`Distribution`/`TargetZipSize`/`IncludeLoadFile` in `LoadFileModule`) — all annotated `// Transitional: X lives in SourceInputModule/OutputModule/ProductionModule (Phase 3)`, exactly as Phase 1 annotated its own bag reads.

**Shared `CliModuleSet` rule (non-negotiable):** one `CliModuleSet` instance must travel parse → validate → TryBuild. `TryApply` mutates fields on that instance. `CliParser.Parse(args)` (`Parse(args, CliModules.Create().All)`) **throws the set away**. After Task 4 that is fatal for any site that Parses then Validates/Builds using moved flags — the second `Create()` is empty. `Pipeline.Build` already keeps one set; every test helper and every `Parse`→`Validate`/`Build` site must do the same. The one-arg `Parse` stays only for parse-null / remaining-bag assertions.

Like Phase 1, Phase 2 **collocates** validate+parse in the module; it does not eliminate double interpretation (e.g. a value still validated once in `TryBuild` and re-derived in `RequestBuilder` helpers). Do not claim "one pass / no double interpretation" in the PR. That is a Phase 4 cleanup.

**Tech Stack:** C# 14 / .NET 10, xUnit, Mermaid (architecture.md), bash E2E + goldens harness.

## Global Constraints

- `FileGenerationRequest` and all 9 sub-config records are the **stable output contract — do not change them** (issue #750). That means:
  - `LoadFileConfig` stays `Formats` / `Encoding` / `IsEncodingExplicit` / `Distribution` / `AttachmentRate`. It has **no** `IsLoadFileFormatExplicit`. Keep that flag on `LoadFileModule` and pass it into `RequestBuilder.Build` as a `bool` (same pattern as `loadfileOnly`).
  - `MetadataConfig` has **no** `AttachmentRate`. Attachment rate lives on `LoadFileConfig`. `MetadataModule` exposes `int AttachmentRate` only as the sibling channel into `LoadFileModule.TryBuild`.
  - `FileGenerationRequest.LoadfileOnly` is a top-level field; its value must keep flowing from `LoadFileModule.LoadfileOnly`.
- Preserve `composer → serializer → emitter` Load File seam (ADR-0007) and the three-mode pipeline (ADR-0006).
- Every intermediate commit must leave the full test suite green — no broken states.
- **Byte-exact output parity** (Critical Rule 6): Phase 2 is a pure logic move. The existing goldens harness (`tests/goldens/run-goldens.sh`, 20 scenarios) and `tests/run-tests.sh` E2E are the parity gate. **No new harness.** Goldens **do** exercise Phase 2 flags — do not claim otherwise:
  - `--bates-prefix` / `--bates-digits`: `pdf-full`, `production-set`, `redacted-prod`
  - `--attachment-rate` / `--with-families` / `--with-metadata`: `eml-attachments`, `eml-full`, `families-eml`, `pdf-metadata`, `pdf-full`
  - `--loadfile-only` / `--loadfile-format`: `loadfile-only-dat`, `loadfile-only-opt`, `chaos-dat`, `custom-delim`
  - Uncovered by goldens (module unit tests only): `--column-profile`, `--seed`, `--date-format`, `--empty-percentage`, `--custodian-count`, `--with-collection-metadata`, `--bates-start`, comma-list bates, `--load-file-formats`
- Error/warning messages move **byte-for-byte** on single-invalid invocations (E2E asserts exact strings). Full message inventory in Tasks 1–3. **Accepted divergence (same as Phase 1):** moving checks from `CliValidator` into post-Validate `TryBuild` flips which error wins on **multi-invalid** argv (e.g. bad `--attachment-rate` + bad `--encoding` today prints attachment first; after, encoding still in CrossCutting wins). Do not claim multi-invalid precedence parity. Do not "fix" it.
- `Warnings as Errors` is enabled (`zipper.sln`); run `dotnet format --verify-no-changes src/` after every task.
- Docs sync (Critical Rule 4): Phase 2 changes no CLI behavior or formats → no README/Requirements/UBIQUITOUS_LANGUAGE changes. Flag the Rule 4 conflict if any single-invalid message byte drifts.
- Architecture invariants (Critical Rule 5): the Component Map in `docs/architecture.md` shows only 4 modules (Phase 1). Phase 2 adds 3 → **same-PR diagram update required** (Task 5). **This plan review is not architecture approval.** Re-review the mermaid after the diagram edit before treating Rule 5 as approved.
- Test coverage must not decrease (Critical Rule 3): removed `CliValidatorTests`/`RequestBuilderTests`/`CliParserTests` tests are **retargeted** (ported to module test files with construction swapped — `new ParsedArguments { LoadfileOnly = true }` → `modules.LoadFile.TryApply("--loadfile-only")`), never deleted without a strict-or-stricter replacement.
- No copyright headers. File-scoped namespaces. Naming: test class `{Subject}Tests`, method `{Method}_{Scenario}_{Expected}`.
- Correction to the issue's Phase 2 table (flag in PR): it lists `--bates-prefixes`/`--bates-starts` as BatesModule flags. **They are not real CLI flags** — they are comma-separated lists derived from `--bates-prefix`/`--bates-start` (split on `,`), currently split in `CliParser` and consumed in `ProductionSetValidator`. The module owns 3 flags (`--bates-prefix`, `--bates-start`, `--bates-digits`); the comma-split moves into `BatesModule.TryApply`.
- PR closer is `Refs #750` / `Towards #750`, **never** `Fixes #750`. Phase 2 is a slice; Phases 3–4 remain open.

## File Structure

**Create:**
- `src/Cli/Modules/BatesModule.cs`
- `src/Cli/Modules/MetadataModule.cs`
- `src/Cli/Modules/LoadFileModule.cs`
- `src/Zipper.Tests/Modules/BatesModuleTests.cs`
- `src/Zipper.Tests/Modules/MetadataModuleTests.cs`
- `src/Zipper.Tests/Modules/LoadFileModuleTests.cs`

**Modify:**
- `src/Cli/Modules/CliModules.cs` — add `required BatesModule Bates`, `required MetadataModule Metadata`, `required LoadFileModule LoadFile` to `CliModuleSet`; register all three in `CliModules.Create()` (Task 4). `All` must include the new three (parse dispatch) **and** keep Delimiter/Tiff/Chaos/Hash.
- `src/Cli/Pipeline.cs` — insert the three new `TryBuild` calls **before** the existing four; pass `modules.LoadFile.LoadfileOnly` to Delimiter/Hash/Chaos, `modules.LoadFile.CurrentFormat` to Chaos, and all configs + `loadfileOnly` + `isLoadFileFormatExplicit` to `RequestBuilder.Build`.
- `src/Cli/CliParser.cs` — remove 12 switch cases (`--bates-prefix`/`--bates-start`/`--bates-digits`, `--column-profile`/`--seed`/`--date-format`/`--empty-percentage`/`--custodian-count`/`--attachment-rate`, `--load-file-format`/`--load-file-formats`/`--loadfile-format`) + 4 `ParameterlessFlags` entries (`--with-metadata`, `--with-collection-metadata`, `--with-families`, `--loadfile-only`) + bates comma-split block (lines 148–187). `ReadIntArg`/`ReadLongArg`/`TryGetValue` stay (still used by remaining bag flags). One-arg `Parse` stays `Parse(args, CliModules.Create().All)` — it cannot carry Phase-2 flags into a later Validate/Build.
- `src/Cli/ParsedArguments.cs` — delete 18 properties: `BatesPrefix`, `BatesStart`, `BatesDigits`, `BatesPrefixes`, `BatesStarts`, `WithMetadata`, `WithCollectionMetadata`, `ColumnProfile`, `Seed`, `DateFormat`, `EmptyPercentage`, `CustodianCount`, `WithFamilies`, `AttachmentRate`, `LoadfileOnly`, `LoadFileFormat`, `LoadFileFormats`, `IsLoadFileFormatExplicit`.
- `src/Cli/RequestBuilder.cs` — new `Build(parsed, delimiters, tiff, chaos, hash, bates, metadata, loadFile, loadfileOnly, isLoadFileFormatExplicit)` signature; delete profile-load section (lines 38–57), bates section, metadata section (lines 175–185 → single `Metadata = metadata`), load-file section (lines 128–155 / 186–193 → single `LoadFile = loadFile` + image-type override keyed off the **bool param**, not a config field); retarget **both** `parsed.BatesPrefix` reads (`FindGeneratedIdentityCollision` **and** the source `BatesNumber` column check at line 104) onto `bates`. Keep `GetLoadFileFormat`/`GetDistributionFromName`/`GetEncodingFromName`/`ParseSize` (validators + ChaosModule still use them).
- `src/Cli/CliValidator.cs` — `Validate(ParsedArguments, CliModuleSet)`; route `LoadfileOnly` through `modules.LoadFile.LoadfileOnly`.
- `src/Cli/Validation/StandardModeValidator.cs` — delete attachment/empty/custodian bounds + with-families warning + `IncludesEml` helper; signature unchanged.
- `src/Cli/Validation/ProductionSetValidator.cs` — `Validate(ParsedArguments, CliModuleSet)`; delete **only** the bates list-length + ranges/overlap block (the `BatesPrefixes`/`BatesStarts`/`BatesStart`/`BatesPrefix` math, currently ~164–240). Keep rolling-count / rolling-bates-mode / source-path-mode / production-ID checks. Replace `string.IsNullOrEmpty(parsed.BatesPrefix)` with `!modules.Bates.HasBatesPrefix`. Route `LoadfileOnly` through `modules.LoadFile.LoadfileOnly`.
- `src/Cli/Validation/CrossCuttingValidator.cs` — `Validate(ParsedArguments, CliModuleSet)`; keep only the two column-profile cross-domain conflicts (`--column-profile` × `--production-set` via `modules.Metadata.HasColumnProfile`, × `--types` via the same); delete path-safety/existence/precedence block, `ValidateLoadFileFormats`, bates dry-run, and the now-unused `PathValidator`/`File` imports. Route `LoadfileOnly` through `modules.LoadFile.LoadfileOnly`.
- `src/Cli/Validation/LoadfileOnlyValidator.cs` — **delete file** (all four checks move to `LoadFileModule`).
- `src/Cli/Modules/DelimiterModule.cs` / `HashModule.cs` / `ChaosModule.cs` — new `TryBuild` signatures (Task 4).
- `src/Program.cs` (comparison path, lines 46/54) — `Cli.CliValidator.Validate(parsedArgs, Cli.CliModules.Create())`. This `Create()` is a **discarded** set: comparison short-circuits before any module-owned check, so an empty set is correct. Do not pretend it sees parse-time modules. (One-arg `Parse` on this path also discards its set — same reason, safe.)
- `src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs` — rewrite so Parse/Validate/Build share one `CliModuleSet` (see Task 4 Step 6).
- `src/Zipper.Tests/Cli/CliParserTests.cs` — retarget bag-property assertions + the two Parse-then-Validate column-profile path tests (they **cannot** stay as-is).
- `src/Zipper.Tests/Cli/CliValidatorTests.cs` — retarget/port moved contracts; remaining tests that currently poke `LoadfileOnly`/`BatesPrefix`/`ColumnProfile`/`AttachmentRate` must configure the shared set.
- `src/Zipper.Tests/Cli/RequestBuilderTests.cs` — retarget/port the 7 construction sites that set moved bag props.
- `src/Zipper.Tests/Modules/HashModuleTests.cs` / `ChaosModuleTests.cs` / `DelimiterModuleTests.cs` — update **every** `TryBuild` call (not just two files).
- `src/Zipper.Tests/LoadFiles/FieldNamingTests.cs` — retarget `CliValidator_ShouldRejectInvalidFormatInLoadFileOnlyMode` (lines 194–208).
- `docs/architecture.md` — update Component Map CLI Layer (same PR, Rule 5).

There is **no** `ProductionSetValidatorTests.cs` and **no** `LoadfileOnlyValidatorTests.cs`. Do not go looking for them. Bates rolling math has no unit test today — write new ones from `ProductionSetValidator.cs:164–240`. Loadfile-only contracts live in `CliValidatorTests`.

---

## Task 1: BatesModule

**Files:** Create `src/Cli/Modules/BatesModule.cs`, `src/Zipper.Tests/Modules/BatesModuleTests.cs`. **Additive** — module is NOT registered in `CliModules.Create().All` yet, so no behavior change.

**Owned flags:** `--bates-prefix`, `--bates-start`, `--bates-digits` (all `TakesValue`).
**Exposes:** `bool HasBatesPrefix` (`!string.IsNullOrEmpty(_batesPrefix)` — empty string is false, matching `ProductionSetValidator`), `IReadOnlyList<string>? BatesPrefixes`, `IReadOnlyList<long>? BatesStarts` (comma-derived during `TryApply`, mirroring `CliParser`), plus the private raw `_batesPrefix`/`_batesStart`(`long?`)/`_batesDigits` and `BatesNumberConfig? TryBuild(...)` output. `BatesNumberConfig` is an init-property record (`Prefix`, `long Start`, `Digits`, `Prefixes`, `Starts`) — **no positional ctor**; build it with the object initializer.

- [ ] **Step 1: Implement `BatesModule`**

```csharp
public sealed class BatesModule : CliModule
{
    public override IReadOnlyCollection<string> OwnedFlags { get; } =
        new[] { "--bates-prefix", "--bates-start", "--bates-digits" };

    private string? _batesPrefix;
    private IReadOnlyList<string>? _batesPrefixes;
    private long? _batesStart;
    private IReadOnlyList<long>? _batesStarts;
    private int? _batesDigits;

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--bates-prefix":
                _batesPrefix = value;
                _batesPrefixes = value?.Contains(',', StringComparison.Ordinal) == true
                    ? value.Split(',').Select(p => p.Trim()).ToList()
                    : new List<string> { value! };
                return true;
            case "--bates-start":
                // Dispatcher already rejected a missing token (`Error: --bates-start requires a value.`).
                // Direct TryApply(null) / empty: match ReadLongArg invalid-value bytes.
                if (value?.Contains(',', StringComparison.Ordinal) == true)
                {
                    var starts = new List<long>();
                    foreach (var part in value.Split(','))
                    {
                        if (long.TryParse(part.Trim(), CultureInfo.InvariantCulture, out var sVal)) starts.Add(sVal);
                        else { Console.Error.WriteLine($"Error: Invalid value for --bates-start: '{value}'"); return false; }
                    }
                    _batesStarts = starts;
                    _batesStart = starts[0];
                }
                else if (long.TryParse(value, CultureInfo.InvariantCulture, out var batesStart))
                {
                    _batesStart = batesStart;
                    _batesStarts = new List<long> { batesStart };
                }
                else { Console.Error.WriteLine($"Error: Invalid value for --bates-start: '{value}'"); return false; }
                return true;
            case "--bates-digits":
                if (!int.TryParse(value, CultureInfo.InvariantCulture, out var digits))
                {
                    Console.Error.WriteLine($"Error: Invalid value for --bates-digits: '{value}'");
                    return false;
                }
                _batesDigits = digits;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool HasBatesPrefix => !string.IsNullOrEmpty(_batesPrefix);
}
```

`CliParser.Parse` already prints `Error: {flag} requires a value.` and returns null **before** `TryApply` when the value token is missing (`TakesValue` + `TryGetValue` fail). `CliParserTests.Parse_MissingValueForValueTakingFlags` stays green via that dispatcher — do not also print "requires a value" inside `TryApply` (double message). Parse-invalid-numeric (`Parse_InvalidBatesStart` / `Parse_InvalidBatesDigits`) hits `TryApply` with the bad token and must keep the `Invalid value for …` line.

- [ ] **Step 2: `TryBuild` — absorb bates validation**

Read `CrossCuttingValidator.ValidateBates` (non-production-set dry-run of `BatesSequence.TryCreate`) and the `ProductionSetValidator.ValidateRollingConfig` bates list-length + ranges/overlap block (the `BatesPrefixes`/`BatesStarts` math, currently lines 164–240) first, then replicate. **Do not** move rolling-count / rolling-bates-mode / source-path-mode / production-ID checks — those stay in `ProductionSetValidator`.

```csharp
public bool TryBuild(ParsedArguments parsed, out BatesNumberConfig? config)
{
    config = null;
    // Transitional: ProductionSet/RollingCount/RollingBatesMode/Count live in ProductionModule (Phase 3).
    if (_batesPrefix is not null || _batesStart is not null || _batesDigits is not null)
    {
        // Dry-run — exact copy of CrossCuttingValidator.ValidateBates (any bates flag set triggers it):
        var probe = new BatesNumberConfig { Prefix = _batesPrefix ?? "DOC", Start = _batesStart ?? 1, Digits = _batesDigits ?? 8 };
        if (!BatesSequence.TryCreate(probe, out _, out var error))
        {
            Console.Error.WriteLine($"Error: {error}");
            return false;
        }
    }
    if (parsed.ProductionSet)
    {
        // Exact copy of ProductionSetValidator bates list-length + ranges/overlap ONLY
        // (ProductionSetValidator.cs:164–240). Gate is ProductionSet, NOT
        // `RollingCount || RollingBatesMode` — RollingCount defaults to 1 and
        // RollingBatesMode defaults to "continuous", so that condition is always true
        // and would also imply the wrong predicate. Today's math runs for every
        // --production-set invocation.
        // Strings: "Error: Number of bates prefixes must match rolling count." /
        // "Error: Bates prefix cannot be empty or whitespace." /
        // "Error: Number of bates starts must match rolling count." /
        // "Error: Bates ranges overlap for prefix '{prefix}': Set {i} ({start}-{end}) and Set {j} ({start}-{end})."
    }
    if (!string.IsNullOrEmpty(_batesPrefix))
    {
        config = new BatesNumberConfig
        {
            Prefix = _batesPrefix,
            Start = _batesStart ?? 1,
            Digits = _batesDigits ?? 8,
            Prefixes = _batesPrefixes,
            Starts = _batesStarts,
        };
    }
    return true;
}
```

Exact shapes and error strings come from the three pinned sources — reproduce byte-for-byte:
- comma-split + parse + error strings: `CliParser.cs:148–187`
- dry-run: `CrossCuttingValidator.ValidateBates` (`CrossCuttingValidator.cs:193–210`)
- rolling list-length + ranges/overlap: `ProductionSetValidator.cs:164–240`
- final config shape: `RequestBuilder.cs:195–203`

When `parsed.ProductionSet && !HasBatesPrefix`, leave `config = null` and let `ProductionSetValidator` keep its `--production-set requires --bates-prefix` check (gated on `modules.Bates.HasBatesPrefix`).

- [ ] **Step 3: `BatesModuleTests`**

`[Collection("ConsoleTests")]`. There is **no** `ProductionSetValidatorTests` to port. Write the contracts from the pinned sources:

- `CliParserTests.Parse_ProductionSetArgs_ParsesCorrectly` (bates parts — prefix/start/digits + derived lists)
- `CliParserTests.Parse_InvalidBatesStart` / `Parse_InvalidBatesDigits` (via `TryApply` → false + exact error string)
- `CliValidatorTests.Validate_BatesPrefix_WithPathSeparator` / `WithDotDot` / `WithSpecialChars` (dry-run: `TryApply("--bates-prefix", …)` + `TryBuild` → false + exact `Error:` line from `BatesSequence.TryCreate`)
- **New** (no existing unit test covers this math — copy the validator loop, do not invent a file to port):
  - `TryBuild_ProductionSet_PrefixCountMismatch_ReturnsFalse`
  - `TryBuild_ProductionSet_EmptyPrefixInList_ReturnsFalse`
  - `TryBuild_ProductionSet_StartCountMismatch_ReturnsFalse`
  - `TryBuild_ProductionSet_ContinuousSamePrefixOverlap_ReturnsFalse`
  - `TryBuild_ProductionSet_RestartSamePrefix_ReturnsTrue`
- New: `TryBuild_CommaPrefix_DerivesPrefixesAndStarts`, `TryBuild_NoFlags_ReturnsNullConfig`, `HasBatesPrefix_EmptyString_False`

Run the combo gate after this task (additive — suite must stay green).

---

## Task 2: MetadataModule

**Files:** Create `src/Cli/Modules/MetadataModule.cs`, `src/Zipper.Tests/Modules/MetadataModuleTests.cs`. **Additive** — not registered yet.

**Owned flags:** parameterless `--with-metadata`, `--with-collection-metadata`, `--with-families`; value-taking `--column-profile`, `--seed`, `--date-format`, `--empty-percentage`, `--custodian-count`, `--attachment-rate`.
**Exposes:** `int AttachmentRate` (sibling channel for LoadFile — **not** a `MetadataConfig` field), `bool HasColumnProfile`, `bool WithMetadata`, `bool WithCollectionMetadata`, `bool WithFamilies` (module test getters), raw `_seed`/`_dateFormat`/`_emptyPercentage`/`_custodianCount`/`_columnProfile`/`_withFamilies`/`_attachmentRate`.

- [ ] **Step 1: Implement `MetadataModule` parse surface**

`TryApply` replicates current `CliParser` behavior byte-for-byte:
- parameterless flags set the bool and return true (moved out of `ParameterlessFlags`; `TakesValue` override returns false for them)
- value flags parse via the same pattern as today (`CliParser.cs:103–145`): `--seed`, `--empty-percentage`, `--custodian-count`, `--attachment-rate` use `ReadIntArg` semantics (`int.TryParse` with `CultureInfo.InvariantCulture`), `--date-format` / `--column-profile` use `ReadStringArg`; on failure print `Error: Invalid value for {flag}: '{value}'` and return false
- `default:` writes `Error: Unknown argument or unconsumed value '{flag}'` (Phase 1 convention)

- [ ] **Step 2: `TryBuild` — absorb StandardModeValidator + CrossCuttingValidator + RequestBuilder metadata logic**

Emission order matters (preserves today's ordering where StandardModeValidator runs before CrossCuttingValidator, and RequestBuilder's load/merge runs last):

1. **Bounds** (from `StandardModeValidator` lines 33/54/60): `--attachment-rate` must be 0–100, `--empty-percentage` 0–100, `--custodian-count` 1–1000. On violation: exact `Error: Attachment rate must be between 0 and 100.` / `Error: Empty percentage must be between 0 and 100.` / `Error: Custodian count must be between 1 and 1000.`, return false.
2. **With-families warning** (from `StandardModeValidator` line 68): if `_withFamilies && !hasSourceInput && (!IncludesEml(parsed) || _attachmentRate <= 0)` → exact `Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.` Needs `hasSourceInput` (`parsed.InputCsv`/`parsed.DirectoryTemplate`) and the eml ratio check; port `IncludesEml` (`FileTypeRatioParser` on `parsed.FileTypes`) into the module. `// Transitional: FileType/FileTypes/InputCsv/DirectoryTemplate live in SourceInputModule (Phase 3).` Warning writes and **continues** (does not return false).
3. **Column profile** (from `CrossCuttingValidator.ValidateColumnProfile` lines 162–188 + `RequestBuilder` lines 38–57):
   - if `_columnProfile` set and `parsed.ProductionSet` → the `--production-set` conflict check **stays in `CrossCuttingValidator`** (Task 4) — not here.
   - path safety: `ColumnProfileLoader.IsBuiltInProfile` else `PathValidator.IsPathSafe(_columnProfile, Directory.GetCurrentDirectory())`; on failure exact `Error: Path traversal detected in column profile path '{...}'. Profile file must reside within working directory.`, return false.
   - existence: `File.Exists` else exact `Error: Column profile '{...}' is not a valid built-in profile or file path.\n       Built-in profiles: {string.Join(", ", BuiltInProfiles.ProfileNames)}`, return false.
   - precedence: if `_withMetadata && _columnProfile` set → exact `Warning: --column-profile takes precedence over --with-metadata. --with-metadata will be ignored.` and set `_withMetadata = false` (this replaces the current `parsed.WithMetadata = false` mutation — today the mutation leaks into `ParsedArguments`, now it is module-local, same observable output). Warning continues.
   - **load — copy `RequestBuilder.cs:38–57` bytes, do not invert Error/Warning:**
     ```
     try { profile = ColumnProfileLoader.Load(_columnProfile); }
     catch (InvalidOperationException ex) {
         Console.Error.WriteLine($"Error: {ex.Message}");
         return false;   // HARD fail — not a warning
     }
     if (profile is null) {
         Console.Error.WriteLine($"Warning: Failed to load column profile '{_columnProfile}'.");
         // continue with profile == null
     } else if (_withCollectionMetadata) {
         profile = BuiltInProfiles.MergeWithCollectionMetadata(profile);
     }
     ```
     Turning the `InvalidOperationException` path into a warning is a behavior change. Do not do it.
4. **Build config** — `MetadataConfig` has no `AttachmentRate` field:

```csharp
config = new MetadataConfig
{
    WithMetadata = _withMetadata,
    ColumnProfile = profile,
    Seed = _seed,
    DateFormatOverride = _dateFormat,
    EmptyPercentageOverride = _emptyPercentage,
    CustodianCountOverride = _custodianCount,
    WithFamilies = _withFamilies,
    WithCollectionMetadata = _withCollectionMetadata,
};
```

This single config **replaces** the RequestBuilder metadata section (lines 177–185). `AttachmentRate` stays on the module for `LoadFileModule.TryBuild`.

- [ ] **Step 3: `MetadataModuleTests`**

Port (construction swapped to `new MetadataModule()` / `modules.Metadata.TryApply(...)` + `TryBuild`). **Do not cite `CliValidatorTests` lines 189/213–235/436/446/456/469/481** — those are production-set / supplemental, not profile path tests.

Real sources:
- `CliValidatorTests.Validate_AttachmentRateOutOfRange_ReturnsFalse` (the only existing bounds test). **Write new** empty-percentage / custodian-count bounds tests — they have no validator coverage today, only `CliParserTests.Parse_InvalidEmptyPercentage` / `Parse_InvalidCustodianCount` (parse-int failures, different message).
- `CliValidatorTests.Validate_WithFamiliesWithoutEml_EmitsWarning` / `WithEmlAndAttachmentRateZero` / `WithEmlAndAttachmentRatePositive` / `WithFamiliesAndLoadfileOnly`
- `CliParserTests.Parse_ColumnProfileWithParentTraversal_RejectsPathOutsideCwd` (path-safety) and `Parse_ColumnProfileWithinCwd_IsAccepted` (existence). These stay in `CliParserTests` too (Task 4 retargets them onto a shared set) — port the contract here as `TryBuild` tests.
- **New** (no existing test asserts this): `TryBuild_ColumnProfileWithWithMetadata_EmitsPrecedenceWarningAndClearsWithMetadata`
- `RequestBuilderTests.Build_ColumnProfile_LoadsProfile` (incl. a new merge-with-collection-metadata case if you add `--with-collection-metadata`; the current test only loads `"standard"`)
- `CliParserTests.Parse_ColumnProfileArgs_ParsesCorrectly`, `Parse_AllBooleanFlags_SetCorrectly` (metadata parts)
- New: `TryBuild_NoFlags_ReturnsDefaultMetadataConfig`, `TryBuild_LoadThrows_ReturnsFalseWithError` (hard-fail path), `TryBuild_MissingFile_ReturnsFalseWithBuiltInList`, `TryBuild_WithFamiliesWarning_EmittedWhenNoSourceInput`

`RequestBuilderTestHelper` is not changed this task (module not registered) — call the module directly.

---

## Task 3: LoadFileModule

**Files:** Create `src/Cli/Modules/LoadFileModule.cs`, `src/Zipper.Tests/Modules/LoadFileModuleTests.cs`. **Additive** — not registered yet.

**Owned flags:** parameterless `--loadfile-only`; value-taking `--load-file-format`, `--load-file-formats`, legacy alias `--loadfile-format`.
**Exposes:** `bool LoadfileOnly`, `bool IsLoadFileFormatExplicit` (**module property, not a `LoadFileConfig` field**), `LoadFileFormat CurrentFormat` (= `RequestBuilder.GetLoadFileFormat(_loadFileFormat ?? "dat") ?? LoadFileFormat.Dat` — single-format only, matching today's `ChaosModule` read), plus internal `_loadfileOnly`/`_loadFileFormat`/`_loadFileFormats` state.

`LoadFileConfig` shape is unchanged: `Formats` / `Encoding` / `IsEncodingExplicit` / `Distribution` / `AttachmentRate`. `FileGenerationRequest.LoadFile` is the only consumer of that record.

Default: today's bag initializes `LoadFileFormat = "dat"`, so `!string.IsNullOrEmpty(parsed.LoadFileFormat)` is **always true** and `ValidateLoadFileFormats` always runs the single-format check. The module must keep that — initialize `_loadFileFormat = "dat"` (or always run the single-format map). Do not treat "flag absent" as skip.

- [ ] **Step 1: Implement `LoadFileModule` parse surface**

`TakesValue` returns false for `--loadfile-only`.

`TryApply`:
- `--loadfile-only` → `_loadfileOnly = true`, true.
- `--load-file-format` / legacy `--loadfile-format` → store raw value, set `IsLoadFileFormatExplicit = true`, true (both flags share one field today — `CliParser` lines 113–124; keep the same precedence/overwrite semantics: last flag wins, same field).
- `--load-file-formats` → store raw comma string, set `IsLoadFileFormatExplicit = true`, true.
- `default:` writes the Phase 1 unknown-argument line.

- [ ] **Step 2: `TryBuild` — absorb LoadfileOnlyValidator + CrossCuttingValidator + RequestBuilder load-file logic**

**Message-order invariant (do not invert):** today `LoadfileOnlyValidator` runs first but treats unknown formats as `Dat` (`GetLoadFileFormat(x) ?? Dat`), so the dat/opt restriction does **not** fire on garbage; `ValidateLoadFileFormats` then prints `Invalid load file format…`. `--load-file-formats` unknown parts are skipped by the restriction (`currentFormat.HasValue && …`) and also fall through to the invalid-format line.

Preserve that:

1. **Format validation first** (from `CrossCuttingValidator.ValidateLoadFileFormats`, exact strings):
   - single-format (`_loadFileFormat`, including the `"dat"` default): `GetLoadFileFormat` null → `Error: Invalid load file format. Supported values are dat, opt, csv, edrm-xml, xml, concordance.`, false.
   - `--load-file-formats` each comma part: null → `Error: Invalid load file format '{fmt}'. Supported: dat, opt, csv, edrm-xml, xml, concordance.`, false. (`fmt` is the raw split token, matching today — confirm against `CrossCuttingValidator.cs:137–157`.)
2. **Loadfile-only conflicts** (from `LoadfileOnlyValidator`, exact strings) — only after formats mapped:
   - if `_loadfileOnly && !string.IsNullOrEmpty(parsed.TargetZipSize)` → `Error: --loadfile-only conflicts with --target-zip-size.`, false. (`IsNullOrEmpty`, not `is not null` — empty string must not trip.)
   - if `_loadfileOnly && parsed.IncludeLoadFile` → `Error: --loadfile-only conflicts with --include-load-file.`, false.
   - if `_loadfileOnly`: restriction only on **successfully mapped** formats (`HasValue && != Dat && != Opt`). Unknown already died in step 1. Known csv/xml/concordance → `Error: --loadfile-only mode is only supported for 'dat' and 'opt' load file formats.`, false.
   - `// Transitional: TargetZipSize/IncludeLoadFile live in OutputModule (Phase 3).`
3. **Build config** — do **not** put `IsLoadFileFormatExplicit` on the record:

```csharp
public bool TryBuild(ParsedArguments parsed, int attachmentRate, out LoadFileConfig config)
{
    // …validate as above…
    IReadOnlyList<LoadFileFormat> formats;
    if (!string.IsNullOrEmpty(_loadFileFormats))
    {
        formats = _loadFileFormats.Split(',')
            .Select(f => RequestBuilder.GetLoadFileFormat(f.Trim()))
            .Where(f => f.HasValue)
            .Select(f => f!.Value)
            .ToList();
    }
    else
    {
        formats = new List<LoadFileFormat> { RequestBuilder.GetLoadFileFormat(_loadFileFormat ?? "dat") ?? LoadFileFormat.Dat };
    }

    // Transitional: Encoding/IsEncodingExplicit/Distribution live in OutputModule (Phase 3).
    var encoding = RequestBuilder.GetEncodingFromName(parsed.Encoding ?? "UTF-8");
    var encodingName = (encoding is not null && !string.IsNullOrEmpty(parsed.Encoding))
        ? parsed.Encoding.ToUpperInvariant()
        : "UTF-8";

    config = new LoadFileConfig
    {
        Formats = formats,
        Encoding = encodingName,
        IsEncodingExplicit = parsed.IsEncodingExplicit,
        Distribution = RequestBuilder.GetDistributionFromName(parsed.Distribution ?? "proportional") ?? DistributionType.Proportional,
        AttachmentRate = attachmentRate,
    };
    return true;
}
```

Copy the encoding-name shape from `RequestBuilder.Build` lines 156–193. `AttachmentRate = attachmentRate` is **confirmed needed** (`RequestBuilder.cs:192`).

- [ ] **Step 3: `LoadFileModuleTests`**

There is **no** `LoadfileOnlyValidatorTests`. Port from `CliValidatorTests`:

- `Validate_LoadfileOnlyWithTargetZipSize_ReturnsFalse`
- `Validate_LoadfileOnlyWithIncludeLoadFile_ReturnsFalse`
- `Validate_LoadfileOnly_WithCsvFormat_ReturnsFalse` / `WithEdrmXmlFormat` / `WithCsvFormatsPlural` / `WithCsvAndXmlFormatsPlural` / `WithDatFormatsPlural` / `WithDatFormat` / `WithOptFormat`
- `Validate_InvalidLoadFileFormat_ReturnsFalse` (unknown → **invalid-format** string, not the dat/opt restriction)
- `RequestBuilderTests.Build_MultiFormat_CreatesFormatList`, `Build_LoadfileOnlyEncoding_UsesExtendedSet`
- `CliParserTests.Parse_LoadfileOnlyArgs_ParsesCorrectly`
- `FieldNamingTests.CliValidator_ShouldRejectInvalidFormatInLoadFileOnlyMode` contract (csv + loadfile-only → false; original retargeted in Task 4)
- New: `TryBuild_UnknownFormat_ReturnsInvalidFormatNotDatOptRestriction`, `CurrentFormat_Default_Dat`, `TryBuild_LastFlagWins_ForSingleFormatFlag`, `TryBuild_LoadfileOnlyFormat_Csv_ReturnsFalse` (legacy `--loadfile-format`)

---

## Task 4: Wire into the Pipeline (atomic)

**Files:** `CliModules.cs`, `Pipeline.cs`, `CliParser.cs`, `ParsedArguments.cs`, `RequestBuilder.cs`, `CliValidator.cs`, `Validation/StandardModeValidator.cs`, `Validation/ProductionSetValidator.cs`, `Validation/CrossCuttingValidator.cs`, `Validation/LoadfileOnlyValidator.cs` (delete), `Modules/DelimiterModule.cs`, `Modules/HashModule.cs`, `Modules/ChaosModule.cs`, `Program.cs`, `RequestBuilderTestHelper.cs`, `CliParserTests.cs`, `CliValidatorTests.cs`, `RequestBuilderTests.cs`, `HashModuleTests.cs`, `ChaosModuleTests.cs`, `DelimiterModuleTests.cs`, `FieldNamingTests.cs`.

This is the single risky, atomic commit. Do it in one pass and run the full gate; do not stop mid-way (the bag deletions break compilation until all readers are updated).

- [ ] **Step 1: Register modules in `CliModuleSet`**

`CliModuleSet` gains `required BatesModule Bates`, `required MetadataModule Metadata`, `required LoadFileModule LoadFile`; `CliModules.Create()` constructs all three. `All` includes Bates, Metadata, LoadFile, Delimiter, Tiff, Chaos, Hash (order does not matter for parse dispatch). `CliParser.Parse(args)` stays `Parse(args, CliModules.Create().All)` — callers that need the set must use the two-arg overload.

- [ ] **Step 2: Thread sibling state through signatures**

- `DelimiterModule.TryBuild(ParsedArguments parsed, bool loadfileOnly, out DelimiterConfig config)` — replace `parsed.LoadfileOnly` read at `DelimiterModule.cs:51`. Leave `parsed.ProductionSet`.
- `HashModule.TryBuild(ParsedArguments parsed, bool loadfileOnly, out HashConfig config)` — replace `parsed.LoadfileOnly` read at `HashModule.cs:77`.
- `ChaosModule.TryBuild(ParsedArguments parsed, bool loadfileOnly, LoadFileFormat currentFormat, out ChaosConfig config)` — delete the transitional reads at `ChaosModule.cs:36–45`; keep its dat/opt guard (`currentFormat != Dat && != Opt` → false) unchanged. Do not recompute format from `--load-file-formats`.
- `CliValidator.Validate(ParsedArguments parsed, CliModuleSet modules)`, `ProductionSetValidator.Validate(ParsedArguments parsed, CliModuleSet modules)`, `CrossCuttingValidator.Validate(ParsedArguments parsed, CliModuleSet modules)` — thread `modules` down through the validator chains; replace every `parsed.LoadfileOnly` with `modules.LoadFile.LoadfileOnly`, `string.IsNullOrEmpty(parsed.BatesPrefix)` with `!modules.Bates.HasBatesPrefix`, `parsed.ColumnProfile` with `modules.Metadata.HasColumnProfile`. `StandardModeValidator.Validate(ParsedArguments)` unchanged.
- `Program.cs` comparison path: `CliValidator.Validate(parsedArgs, CliModules.Create())` — discarded empty set, safe (see File Structure).

- [ ] **Step 3: Delete moved validator logic**

- `StandardModeValidator`: delete attachment/empty/custodian bounds + with-families warning + `IncludesEml`.
- `ProductionSetValidator`: delete bates list-length + ranges/overlap only; keep `--production-set requires --bates-prefix` gated on `!modules.Bates.HasBatesPrefix`; keep rolling-count / mode / source-path / production-ID.
- `CrossCuttingValidator`: keep only the two `--column-profile` conflicts (`× --production-set`, `× --types`); delete path-safety/existence/precedence block, `ValidateLoadFileFormats`, bates dry-run, and the now-unused `PathValidator`/`File` imports.
- Delete `LoadfileOnlyValidator.cs` + its call in `CliValidator`.
- Delete `ParsedArguments` 18 properties (see File Structure).

- [ ] **Step 4: Rewire `RequestBuilder`**

New signature:

```csharp
public static FileGenerationRequest? Build(
    ParsedArguments parsed, DelimiterConfig delimiters, TiffConfig tiff,
    ChaosConfig chaos, HashConfig hash, BatesNumberConfig? bates,
    MetadataConfig metadata, LoadFileConfig loadFile,
    bool loadfileOnly, bool isLoadFileFormatExplicit)
```

- Delete profile-load section (lines 38–57), bates section, metadata section → `Metadata = metadata`.
- Load-file section → `LoadFile = loadFile` (the module builds the whole `LoadFileConfig`). **Image-type override stays here** and keys off the bool param, **not** a config field:
  ```
  if (!isLoadFileFormatExplicit && hasImageType)
      loadFile = loadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };
  ```
  `hasImageType` still uses `fileType` / `fileTypeRatios` / `sourceRecords` computed in this method — cannot move. `MixedFileTypeCliTests.Build_MixWithTiffOrJpg_DefaultsToDatAndOpt` (line 114) + `SourceDrivenCliTests` guard it.
- Retarget **both** Bates bag reads:
  - line 104: `rows.Any(r => r.BatesNumber is not null) && string.IsNullOrEmpty(parsed.BatesPrefix)` → `… && bates is null` (`config` is only built when prefix is non-empty, so `bates is null` ≡ `!HasBatesPrefix`).
  - `FindGeneratedIdentityCollision(rows, parsed)` → `(rows, bates)`, reading `bates?.Prefix` / `bates?.Start ?? 1` / `bates?.Digits ?? 8`.
- `LoadfileOnly = loadfileOnly`.
- Keep `GetLoadFileFormat`/`GetDistributionFromName`/`GetEncodingFromName`/`ParseSize` (transitional, Phase 4 deletes with the remaining validators).

- [ ] **Step 5: Rewire `Pipeline`**

```csharp
var modules = CliModules.Create();
parsedArgs = CliParser.Parse(args, modules.All);
if (parsedArgs is null) return null;
if (!CliValidator.Validate(parsedArgs, modules)) return null;
if (!modules.Bates.TryBuild(parsedArgs, out var bates) ||
    !modules.Metadata.TryBuild(parsedArgs, out var metadata) ||
    !modules.LoadFile.TryBuild(parsedArgs, modules.Metadata.AttachmentRate, out var loadFile) ||
    !modules.Delimiter.TryBuild(parsedArgs, modules.LoadFile.LoadfileOnly, out var delimiters) ||
    !modules.Tiff.TryBuild(parsedArgs, out var tiff) ||
    !modules.Chaos.TryBuild(parsedArgs, modules.LoadFile.LoadfileOnly, modules.LoadFile.CurrentFormat, out var chaos) ||
    !modules.Hash.TryBuild(parsedArgs, modules.LoadFile.LoadfileOnly, out var hash))
{
    return null;
}
var request = RequestBuilder.Build(
    parsedArgs, delimiters, tiff, chaos, hash, bates, metadata, loadFile,
    modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
```

Preserves the existing Phase 1 order exactly (`Delimiter, Tiff, Chaos, Hash`), with the three new modules inserted before it — they must run first because Delimiter/Hash/Chaos now depend on `loadFile.LoadfileOnly`/`CurrentFormat`. Preserve the return-null paths exactly.

- [ ] **Step 6: Update tests (retarget, never delete)**

Rewrite `RequestBuilderTestHelper` so one set is shared:

```csharp
internal static class RequestBuilderTestHelper
{
    public static (ParsedArguments? Parsed, CliModuleSet Modules) Parse(string[] args)
    {
        var modules = CliModules.Create();
        return (CliParser.Parse(args, modules.All), modules);
    }

    public static FileGenerationRequest? Build(
        ParsedArguments parsed,
        Action<CliModuleSet>? configureModules = null,
        CliModuleSet? modules = null)
    {
        modules ??= CliModules.Create();
        configureModules?.Invoke(modules);
        if (!modules.Bates.TryBuild(parsed, out var bates) ||
            !modules.Metadata.TryBuild(parsed, out var metadata) ||
            !modules.LoadFile.TryBuild(parsed, modules.Metadata.AttachmentRate, out var loadFile) ||
            !modules.Delimiter.TryBuild(parsed, modules.LoadFile.LoadfileOnly, out var delimiters) ||
            !modules.Tiff.TryBuild(parsed, out var tiff) ||
            !modules.Chaos.TryBuild(parsed, modules.LoadFile.LoadfileOnly, modules.LoadFile.CurrentFormat, out var chaos) ||
            !modules.Hash.TryBuild(parsed, modules.LoadFile.LoadfileOnly, out var hash))
        {
            return null;
        }

        return RequestBuilder.Build(
            parsed, delimiters, tiff, chaos, hash, bates, metadata, loadFile,
            modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
    }
}
```

**Parse-then-Validate/Build sites that must share a set** (empty `Create()` is a false green after bag delete):

| Site | Why |
|---|---|
| `CliParserTests.Parse_ColumnProfileWithParentTraversal_RejectsPathOutsideCwd` | Validate reads `HasColumnProfile` / path from the module, not the bag |
| `CliParserTests.Parse_ColumnProfileWithinCwd_IsAccepted` | same |
| `CliParserTests.Parse_OutputPathWithinCwd_IsAccepted` | Validate after Parse; no moved flags, default set is OK but use the helper anyway |
| `CliValidatorTests.CreateValidArgs` + every test that sets `LoadfileOnly` / `BatesPrefix` / `ColumnProfile` / `AttachmentRate` / `LoadFileFormat(s)` | bag props gone |
| `CliValidatorTests` supplemental block (`BatesPrefix = "SUPP"`) | otherwise `--production-set requires --bates-prefix` fires first |
| `CliValidatorTests.Validate_ColumnProfile_WithProductionSet_ReturnsFalse` | stays in CrossCutting; needs `modules.Metadata.TryApply("--column-profile", …)` |
| `CliValidatorTests.Validate_ProductionSet_ConflictsWithLoadfileOnly_ReturnsFalse` | stays in ProductionSetValidator; needs `modules.LoadFile.TryApply("--loadfile-only")` |
| `FieldNamingTests.CliValidator_ShouldRejectInvalidFormatInLoadFileOnlyMode` | `TryApply("--loadfile-only")` + `TryApply("--load-file-format", "csv")` on the same set passed to `Validate` |
| `RequestBuilderTests.Build_LoadfileOnly_SetsProperties` / `Build_BatesConfig_SetsCorrectly` / `Build_ColumnProfile_LoadsProfile` / `Build_MultiFormat_CreatesFormatList` / `Build_LoadfileOnlyEncoding_UsesExtendedSet` | configure lambda, not bag setters |
| `RequestBuilderTests.Build_NullArg` / `Build_NullConfigArg` | update to the new 10-arg signature |

`CliParserTests` bag assertions (`Parse_AllBooleanFlags_SetCorrectly`, `Parse_LoadfileOnlyArgs_ParsesCorrectly`, `Parse_ProductionSetArgs_ParsesCorrectly`, `Parse_ColumnProfileArgs_ParsesCorrectly`): switch to `RequestBuilderTestHelper.Parse` and assert **module** getters (`modules.Metadata.WithMetadata`, `modules.LoadFile.LoadfileOnly`, `modules.Bates` prefix/start/digits, `modules.Metadata` profile/seed/date/empty/custodian). Remaining bag flags (`WithText`, `IncludeLoadFile`, `ProductionSet`, `ProductionZip`, `VolumeSize`) stay on `parsed`.

`Parse_MissingValueForValueTakingFlags` and invalid-numeric tests stay on one-arg `Parse` (dispatcher / `TryApply` return false → `Parse` null).

`CliValidatorTests` remaining (type/count/folders/encoding/distribution/target-zip/comparison/supplemental-non-bates): pass a default `CliModules.Create()` **only when they do not set a moved flag**. If they do, configure the same set. Do not write "remaining tests pass `Create()` (default state)" as a blanket — that is how the supplemental `BatesPrefix = "SUPP"` cases go red.

`DelimiterModuleTests` / `HashModuleTests` / `ChaosModuleTests`: every `TryBuild(parsed, out _)` site changes. Delimiter gets `loadfileOnly` (the `--eol` cases at ~63/70 pass `parsed.LoadfileOnly` through the new param instead of the bag). Hash gets `loadfileOnly`. Chaos gets `loadfileOnly` + `currentFormat` (tests that set `parsed.LoadFileFormat` must pass `GetLoadFileFormat(...) ?? Dat` as the new arg).

Run `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj` — full green required.

---

## Task 5: Architecture, E2E, Self-Review

- [ ] **Step 1: Update `docs/architecture.md`**

Component Map CLI Layer: add `BatesModule`, `MetadataModule`, `LoadFileModule` beside the Phase 1 modules; keep `CliParser`/`CliValidator`/`RequestBuilder` depicted (still present). Suggested subgraph text: `Domain Modules<br/>Hash / Delimiter / Tiff / Chaos / Bates / Metadata / LoadFile`. Footnote: keep the Phase-1 note and extend it — comparison short-circuit in `Program` still calls `CliParser.Parse(args)` + `CliValidator.Validate(parsed, CliModules.Create())` on a discarded set. Re-review the mermaid per Critical Rule 5 before treating as approved. No README/Requirements/UBIQUITOUS_LANGUAGE changes (no CLI/format behavior change — Critical Rule 4).

- [ ] **Step 2: E2E + goldens**

`dotnet build -c Release && ./tests/run-tests.sh` and `tests/goldens/run-goldens.sh` — must pass byte-identical. Phase 2 flags **are** on the golden path (see Global Constraints). Fix any parity break as a correctness bug, not by regenerating fixtures.

- [ ] **Step 3: Self-review + autoreview**

Run the autoreview skill (mandatory — this touches parsing, validation, and public contracts). Verify: no double-interpretation claim, no multi-invalid precedence claim, exact single-invalid error-string inventory intact, no deleted test without a strict replacement, `LoadFileConfig` / `MetadataConfig` unchanged, no `ParsedArguments` Phase-2 property references remain (`rg "parsed\\.(BatesPrefix|BatesStart|BatesDigits|BatesPrefixes|BatesStarts|WithMetadata|WithCollectionMetadata|ColumnProfile|Seed|DateFormat|EmptyPercentage|CustodianCount|WithFamilies|AttachmentRate|LoadfileOnly|LoadFileFormat|LoadFileFormats|IsLoadFileFormatExplicit)" src/` must return nothing except comments). Confirm every Parse-then-Validate site shares a set.

- [ ] **Step 4: Commit + PR**

Conventional commit (`refactor(cli): extract Bates/Metadata/LoadFile domain modules (Phase 2 of #750)`), reference `#750` as `Refs #750` (never `Fixes`), flag the `--bates-prefixes/--bates-starts` issue-table correction, `## Release Notes` (3–5 sentences), and the Architecture checklist noting the diagram update.

---

## Grounding notes (review 2026-08-13)

Pinned against `853aa8b` (Phase 1 merged) + current `src/Cli/**`. Do not re-introduce these:

1. `LoadFileConfig` has no `IsLoadFileFormatExplicit`. Adding it mutates the #750 stable contract. Keep the flag on `LoadFileModule`; pass a `bool` into `RequestBuilder.Build`.
2. `MetadataConfig` has no `AttachmentRate`. Attachment rate is `LoadFileConfig.AttachmentRate`. Module exposes `int AttachmentRate` only as the sibling channel.
3. `RequestBuilder` profile-load: `InvalidOperationException` → `Error: {ex.Message}` + hard fail; `profile is null` → Warning + continue. Inverting that is a behavior change.
4. `RequestBuilder.cs:104` (`source BatesNumber column requires --bates-prefix`) still reads `parsed.BatesPrefix` after the bag delete. Retarget onto `bates is null` alongside `FindGeneratedIdentityCollision`.
5. One-arg `CliParser.Parse` discards the module set. Shared-set rule is in Architecture above. `CliParserTests` column-profile path tests and every `CliValidatorTests` setter of a moved flag are the trap.
6. Invented files: there is no `ProductionSetValidatorTests` (do not confuse with `Validation/ProductionSetValidationTests.cs`, which is post-generation) and no `LoadfileOnlyValidatorTests`. Bates overlap/list-length has no unit test — write new. Profile path tests live in `CliParserTests`, not `CliValidatorTests` line 189+.
7. Goldens cover `--bates-*`, `--attachment-rate`, `--with-families`, `--with-metadata`, `--loadfile-only`. Only `--column-profile` / `--seed` / `--date-format` (and friends listed above) are unit-test-only.
8. `ChaosModule.CurrentFormat` is the single `--load-file-format` / `--loadfile-format` value, default Dat. Not the multi-format list. Not the image-type override.
9. Unknown load-file format must keep the **invalid-format** message, not the loadfile-only dat/opt restriction. Validate formats first.
10. Bates rolling math gate is `parsed.ProductionSet`, not `RollingCount || RollingBatesMode` (both have always-on defaults).
11. `Program` comparison `Create()` is a discarded empty set on purpose. Safe. Not a shared-set exception to "fix."
12. This review is not Rule 5 architecture approval. Re-review the mermaid after Task 5.

---

## Phase Roadmap (issue #750)

| Phase | Modules | This plan |
|---|---|---|
| 1 (leaf) | Hash, Delimiter, Tiff, Chaos | ✅ done (merged, commit `853aa8b`) |
| 2 (medium) | Bates, Metadata, LoadFile | ✅ full detail (grounded 2026-08-13) |
| 3 (complex) | Production, Output, SourceInput | sketch in Phase 1 plan |
| 4 (cleanup) | CrossCuttingRules, delete `ParsedArguments`/`RequestBuilder`/`CliValidator`/validators | sketch |

Each phase is independently mergeable/revertable; human review at each phase boundary.
