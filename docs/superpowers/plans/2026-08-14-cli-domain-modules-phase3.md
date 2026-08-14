# CLI Domain Modules — Phase 3 (Production, Output, SourceInput) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the three remaining domains (`ProductionModule`, `OutputModule`, `SourceInputModule`) out of the `CliParser → CliValidator → RequestBuilder` waterfall so every sub-domain of `FileGenerationRequest` is owned by a module, with no behavior or byte-level output change. After this phase the `ParsedArguments` bag holds only the three comparison flags and no module reads the bag — the precondition for Phase 4 (`CrossCuttingRules` + delete `ParsedArguments`, `RequestBuilder`, `CliValidator`, the trimmed validators).

**Architecture:** Follows the Phase 1/2 seam exactly (`CliModule` base, `CliModuleSet`, `TryApply` during parse, `TryBuild` after `CliValidator.Validate`, typed configs into `RequestBuilder.Build`). Three **maintainer decisions** shape this phase:

1. **Trim-but-keep validators (Phase 3 does NOT delete validator classes).** Modules absorb **pure-domain** checks; **cross-domain** checks stay in the shrinking validators and move to `CrossCuttingRules` in Phase 4. Concretely:
   - `StandardModeValidator` shrinks to **one** retained cross check (`--target-zip-size` requires `--count`/source input).
   - `ProductionSetValidator` shrinks to **three** retained cross checks (`--production-set` × `--loadfile-only`, `--production-set` requires `--bates-prefix`, `--redacted-production` × `--loadfile-only`).
   - `CrossCuttingValidator` keeps only genuinely cross-domain checks (see Task 4).
   - The three mode validators become `Validate(CliModuleSet modules)` and read module getters, never the bag. **`CliValidator` itself still takes `ParsedArguments`** — the comparison trio lives there until Phase 4.
2. **Drop `parsed` from every `TryBuild` signature**, including the Phase 1/2 modules that still take an unused bag (`HashModule`, `ChaosModule`, `TiffModule`). Sibling state flows module→module as **primitives / getters**, never the bag and never the whole set. `RequestBuilder.Build` also drops its `parsed` parameter.
3. **Comparison flags stay in `CliParser` + `CliValidator` until Phase 4.** `Program.cs` comparison path (which builds a deliberately discarded set) is unchanged.

**New `TryBuild` signatures (all drop `parsed`):**

```csharp
ProductionModule.TryBuild(out ProductionConfig config)
BatesModule.TryBuild(bool productionSet, int rollingCount, string? rollingBatesMode, long? count, out BatesNumberConfig? config)
SourceInputModule.TryBuild(long? declaredCount, bool productionSet, BatesNumberConfig? bates, out IReadOnlyList<SourceInput.SourceRecord>? sourceRecords)
OutputModule.TryBuild(IReadOnlyList<SourceInput.SourceRecord>? sourceRecords, out OutputConfig config)
MetadataModule.TryBuild(bool includesEml, bool hasSourceInput, out MetadataConfig config)
LoadFileModule.TryBuild(int attachmentRate, string? encoding, bool isEncodingExplicit, string? distribution, string? targetZipSize, bool includeLoadFile, out LoadFileConfig config)
DelimiterModule.TryBuild(bool loadfileOnly, bool productionSet, out DelimiterConfig config)
TiffModule.TryBuild(out TiffConfig config)
ChaosModule.TryBuild(bool loadfileOnly, LoadFileFormat currentFormat, out ChaosConfig config)  // drops unused parsed
HashModule.TryBuild(bool loadfileOnly, out HashConfig config)                                  // drops unused parsed
```

`MetadataModule.TryBuild` takes `bool includesEml` (computed in `Pipeline` as `output.HasFileType("eml")`) instead of the raw `FileType`/`FileTypes` strings. This is a **superset** of the old `IncludesEml` helper (`MetadataModule.cs:177–187`): `HasFileType` also sees source-driven types, which `IncludesEml` never did. **Keep the `!hasSourceInput` gate** around the `--with-families` warning so source-driven runs still skip it. Do not drop that gate just because `HasFileType` can see source types.

**Sibling channels are module getters, never `OutputConfig` fields.** `OutputConfig` has no `Encoding`, `IsEncodingExplicit`, `Distribution`, or string `TargetZipSize` (`TargetZipSize` on the config is `long?`). `LoadFileModule.TryBuild` must take `modules.Output.Encoding` / `IsEncodingExplicit` / `Distribution` / `TargetZipSize` (raw string) / `IncludeLoadFile` — not `output.Encoding`.

**Module parse-time getters consumed by siblings/validators (never the bag):**
- `OutputModule`: `FileType`, `FileTypes`, `Count`, `TargetZipSize` (raw string), `Encoding` (default `"UTF-8"`, same as `ParsedArguments`), `IsEncodingExplicit`, `Distribution` (default `"proportional"`), `IncludeLoadFile`, `Folders` (default `1`), `WithText`
- `ProductionModule`: `ProductionSet`, `RedactedProduction`, `RollingCount` (default `1`), `RollingBatesMode` (default `"continuous"`), `SourcePathMode`
- `SourceInputModule`: `HasSourceInput` (+ `InputCsv`/`DirectoryTemplate` test-facing)
- existing: `LoadFileModule.LoadfileOnly`/`IsLoadFileFormatExplicit`/`CurrentFormat`, `MetadataModule.AttachmentRate`/`HasColumnProfile`, `BatesModule.HasBatesPrefix`

`Folders` **must** default to `1`. Default `0` would fail the 1–100 check on every run that omits `--folders`.

**Build ordering (dependency-driven, `Pipeline.Build`):**
`Production → Bates → SourceInput → Output → Metadata → LoadFile → Delimiter → Tiff → Chaos → Hash`. Rationale: `BatesModule`/`SourceInputModule` need parse-time production state; `SourceInputModule` needs the built `bates` config; `OutputModule` needs `sourceRecords`; `MetadataModule`/`LoadFileModule` read `output` (HasFileType) or Output **module** getters. Production builds first so its pure-domain errors fire before downstream build errors.

**Shared `CliModuleSet` rule (non-negotiable):** one `CliModuleSet` instance travels parse → validate → TryBuild. `CliParser.Parse(args)` (`Parse(args, CliModules.Create().All)`) throws the set away; after Task 4 any Parse-then-Validate/Build site using a moved flag is empty. `Pipeline.Build` already keeps one set; every test helper and `Parse`→`Validate`/`Build` site must do the same. The one-arg `Parse` stays only for parse-null / comparison-bag assertions.

Like Phases 1–2, this **collocates** validate+parse in modules; it does not eliminate double interpretation (values still validated in `TryBuild` and re-derived in `RequestBuilder`-adjacent helpers such as `RequestBuilder.ParseSize` / `GetEncodingFromName`). Do not claim "one pass" in the PR. Phase 4 cleanup.

**Tech Stack:** C# 14 / .NET 10, xUnit, Mermaid (architecture.md), bash E2E + goldens harness.

## Global Constraints

- `FileGenerationRequest` and all 9 sub-config records are the **stable output contract — do not change them** (issue #750). That means:
  - `OutputConfig` stays as-is (no `Encoding`/`Distribution`/raw-`TargetZipSize` fields — those flags are owned by `OutputModule` and consumed by `LoadFileModule` via **module getters**).
  - `ProductionConfig` stays as-is; `GenerateProductionIds` moves onto `ProductionModule` as `internal static`. **`ProductionSetGenerator.cs:42` currently calls `ProductionSetValidator.GenerateProductionIds`** — retarget that call in Task 4 or the solution does not compile. Do not invent a new shared helper; the generator already depends on a CLI type.
  - `FileGenerationRequest.LoadfileOnly`, `SourceRecords` keep flowing as today (from `LoadFileModule.LoadfileOnly` getter + `SourceInputModule` build output).
- Preserve `composer → serializer → emitter` Load File seam (ADR-0007) and the three-mode pipeline (ADR-0006).
- Every intermediate commit must leave the full test suite green — no broken states. Task 4 is one cut-over (cannot register modules while parser cases still exist — `FirstOrDefault(Owns)` would swallow the token and the switch would never run).
- **Byte-exact output parity** (Critical Rule 6): Phase 3 is a pure logic move. The existing goldens harness (`tests/goldens/run-goldens.sh`, 20 scenarios) + `tests/run-tests.sh` / `tests/run-tests.bat` E2E are the parity gate. **No new harness.** Golden coverage of Phase-3 flags:
  - `--type`/`--count`/`--output-path`: every scenario
  - `--folders`/`--distribution`: `jpg-folders-gaussian`
  - `--with-text`: `pdf-text`, `pdf-full`, `eml-full`, `redacted-prod`
  - `--production-set`/`--volume-size`: `production-set`, `redacted-prod`
  - `--redacted-production`: `redacted-prod`
  - Uncovered by goldens (module unit tests + E2E listed below): `--types`, `--input-csv`, `--directory-template`, `--encoding`, `--target-zip-size`, `--include-load-file`, `--production-zip`, `--supplemental-production`, `--prior-manifest`, `--supplemental-gap-policy`, `--production-id`, `--rolling-count`, `--rolling-bates-mode`, `--withheld-native-policy`, `--source-path-mode`
- Error/warning messages move **byte-for-byte** on single-invalid invocations (E2E + unit tests assert exact strings). Full message inventory in Tasks 1–4. **Accepted divergence (same as Phases 1–2):** moving checks from `CliValidator` into post-Validate `TryBuild` flips which error wins on **multi-invalid** argv. Do not claim multi-invalid precedence parity. Do not "fix" it. The list is in Task 4 Step 8.
- `Warnings as Errors` is enabled (`zipper.sln`); run `dotnet format --verify-no-changes src/` after every task.
- Docs sync (Critical Rule 4): Phase 3 changes no CLI behavior or formats → no README/Requirements/UBIQUITOUS_LANGUAGE changes. Flag the Rule 4 conflict if any single-invalid message byte drifts.
- Architecture invariants (Critical Rule 5): the Component Map in `docs/architecture.md` shows 7 modules (Phase 2). Phase 3 adds 3 → **same-PR diagram update required** (Task 5). **This plan review is not architecture approval.** Re-review the mermaid after the diagram edit before treating Rule 5 as approved.
- Test coverage must not decrease (Critical Rule 3): removed `CliValidatorTests`/`RequestBuilderTests`/`CliParserTests` tests are **retargeted** (ported to module test files with construction swapped — `new ParsedArguments { FileType = "pdf" }` → `modules.Output.TryApply("--type", "pdf")`), never deleted without a strict-or-stricter replacement. Exception: `TryBuild_NullArg` tests whose only contract was `ArgumentNullException` on a now-removed `parsed` parameter — delete those, do not invent a new null guard.
- No copyright headers. File-scoped namespaces. Naming: test class `{Subject}Tests`, method `{Method}_{Scenario}_{Expected}`.
- Corrections to the issue's Phase 3 table (flag in PR): (1) the issue lists no home for `--compare-production-manifests`/`--comparison-mode`/`--comparison-output` — they stay in `CliParser`+`CliValidator` per maintainer decision (REQ-179 comparison path short-circuits before module-owned checks). (2) `--source-path-mode` is production-domain (its two retained preconditions live in `CrossCuttingValidator`, not `SourceInputModule`). (3) the issue lists `--prior-manifests`; the real flag is `--prior-manifest`.
- PR closer is `Refs #750` / `Towards #750`, **never** `Fixes #750`. Phase 3 is a slice; Phase 4 remains.

## File Structure

**Create:**
- `src/Cli/Modules/ProductionModule.cs`
- `src/Cli/Modules/SourceInputModule.cs`
- `src/Cli/Modules/OutputModule.cs`
- `src/Zipper.Tests/Modules/ProductionModuleTests.cs`
- `src/Zipper.Tests/Modules/SourceInputModuleTests.cs`
- `src/Zipper.Tests/Modules/OutputModuleTests.cs`

**Modify:**
- `src/Cli/Modules/CliModules.cs` — add `required ProductionModule Production`, `required SourceInputModule SourceInput`, `required OutputModule Output` to `CliModuleSet`; register all three in `CliModules.Create()` (Task 4); `All` includes all 10.
- `src/Cli/Pipeline.cs` — new 10-module build chain (Task 4) + new `RequestBuilder.Build` call.
- `src/Cli/CliParser.cs` — remove 18 switch cases + all 6 `ParameterlessFlags` entries (Task 4); `ReadIntArg`/`ReadLongArg` become unused → delete; keep `ReadStringArg`/`TryGetValue` (comparison trio). `ParameterlessFlags` dict becomes empty → delete it and its dispatch block.
- `src/Cli/ParsedArguments.cs` — delete 26 properties; keep only `CompareProductionManifests`, `ComparisonMode`, `ComparisonOutput`.
- `src/Cli/CliValidator.cs` — comparison checks still read the bag; `--type is required`/`--count is required` read module getters; delegate to the three trimmed validators (`Validate(CliModuleSet)`).
- `src/Cli/Validation/StandardModeValidator.cs` — trim to the one retained cross check; `Validate(CliModuleSet modules)`.
- `src/Cli/Validation/ProductionSetValidator.cs` — trim to the three retained cross checks; `Validate(CliModuleSet modules)`; delete `GenerateProductionIds`.
- `src/ProductionSetGenerator.cs` — retarget `ProductionSetValidator.GenerateProductionIds` (`:42`) to `ProductionModule.GenerateProductionIds`.
- `src/Cli/RequestBuilder.cs` — new `Build(output, metadata, loadFile, delimiters, bates, tiff, chaos, hash, production, sourceRecords, loadfileOnly, isLoadFileFormatExplicit)`; delete path resolution, ratio parse, source reading, count-vs-rows, bates-column checks, identity collision, and all `OutputConfig`/`ProductionConfig` assembly; keep image-type override. Keep `ParseSize`/`GetEncodingFromName`/`GetDistributionFromName`/`GetLoadFileFormat` as internal statics (modules call them transitionally, same pattern as `LoadFileModule` calling `RequestBuilder.GetLoadFileFormat` today).
- `src/Cli/Modules/BatesModule.cs` — new `TryBuild` signature; drop `parsed` reads for `ProductionSet`/`RollingCount`/`RollingBatesMode`/`Count`.
- `src/Cli/Modules/MetadataModule.cs` — new `TryBuild(bool includesEml, bool hasSourceInput, out MetadataConfig)`; delete `IncludesEml` helper.
- `src/Cli/Modules/LoadFileModule.cs` — new `TryBuild` signature; replace `parsed.Encoding`/`IsEncodingExplicit`/`Distribution`/`TargetZipSize`/`IncludeLoadFile` reads with params.
- `src/Cli/Modules/DelimiterModule.cs` — `TryBuild(bool loadfileOnly, bool productionSet, out DelimiterConfig)`; replace `parsed.ProductionSet` read (`DelimiterModule.cs:51`).
- `src/Cli/Modules/TiffModule.cs` / `HashModule.cs` / `ChaosModule.cs` — drop unused `parsed` (they only `ThrowIfNull(parsed)` today).
- `src/Program.cs` — comparison path: `CliValidator.Validate(parsedArgs, CliModules.Create())` **unchanged** (comparison short-circuits before module-owned checks; the discarded set is correct).
- `src/Zipper.Tests/Cli/RequestBuilderTestHelper.cs` — rewrite Parse/Validate/Build to share one set and mirror the new 10-module chain + new `Build` signature.
- `src/Zipper.Tests/Cli/CliValidatorTests.cs` — retarget every construction site (`CreateValidArgs`) to parse real argv through the shared set, or `TryApply` on that set.
- `src/Zipper.Tests/Cli/CliParserTests.cs` — retarget moved-flag bag assertions (`FileType`/`Count`/`WithText`/`IncludeLoadFile`/`ProductionSet`/`ProductionZip`/`VolumeSize`) to module getters; keep comparison-bag + parse-null cases.
- `src/Zipper.Tests/Cli/RequestBuilderTests.cs` — retarget remaining `Build` call sites; move path/output/production assembly cases to module tests.
- `src/Zipper.Tests/Modules/BatesModuleTests.cs` / `MetadataModuleTests.cs` / `LoadFileModuleTests.cs` / `DelimiterModuleTests.cs` / `HashModuleTests.cs` / `ChaosModuleTests.cs` / `TiffModuleTests.cs` — update **every** `TryBuild` call for the new signatures.
- `src/Zipper.Tests/LoadFiles/FieldNamingTests.cs` — retarget `LoadFileModule_ShouldRejectInvalidFormatInLoadFileOnlyMode` (lines 194–208) to the new `TryBuild` params.
- `src/Zipper.Tests/SourceDrivenCliTests.cs` — retarget `Parse_InputCsvAndDirectoryTemplate_StoreRawValues` (reads `parsed.InputCsv` / `parsed.DirectoryTemplate`). **Do not touch** the `Pipeline.Build` tests.
- `src/Zipper.Tests/MixedFileTypeCliTests.cs` — retarget `Parse_TypesArgument_StoresRawValue` (reads `parsed.FileTypes`). **Do not touch** the `Pipeline.Build` tests.
- `docs/architecture.md` — update Component Map CLI Layer to 10 modules (same PR, Rule 5).

**Verify unchanged (behavior parity — `Pipeline.Build` tests only):** `src/Zipper.Tests/CliPipelineTests.cs` and the non-parse tests in `MixedFileTypeCliTests` / `SourceDrivenCliTests`. Those drive `Cli.Pipeline.Build` end-to-end. If a `Pipeline.Build` test fails, it is a porting bug, not a test edit.

There is **no** `ProductionSetValidatorTests.cs` and **no** `StandardModeValidatorTests.cs`. Do not go looking for them. Production pure-domain contracts live in `CliValidatorTests` today → port to `ProductionModuleTests`. Source-reading contracts live in `SourceDrivenCliTests` (CLI/`Pipeline.Build`) plus `SourceInput/SourceCsvReaderTests` — **not** `RequestBuilderTests` (that file has no identity-collision cases). Add module-level collision tests in `SourceInputModuleTests`; leave the `Pipeline.Build` collision coverage in `SourceDrivenCliTests` as the parity guard.

---

## Task 1: ProductionModule

**Files:** Create `src/Cli/Modules/ProductionModule.cs`, `src/Zipper.Tests/Modules/ProductionModuleTests.cs`. **Additive** — module NOT registered in `CliModules.Create().All` yet, so no behavior change. Do **not** delete `ProductionSetValidator.GenerateProductionIds` in this task (generator still calls it).

**OwnedFlags:** `--production-set`, `--production-zip`, `--volume-size`, `--supplemental-production`, `--prior-manifest`, `--supplemental-gap-policy`, `--production-id`, `--rolling-count`, `--rolling-bates-mode`, `--redacted-production`, `--withheld-native-policy`, `--source-path-mode`.

**TakesValue:** `false` for `--production-set`, `--production-zip`, `--supplemental-production`, `--redacted-production`; `true` otherwise.

**TryApply:** raw storage; numeric parse for `--volume-size` (int) and `--rolling-count` (int) with the exact `CliParser.ReadIntArg` messages (`"Error: Invalid value for --volume-size: '{value}'"` / `"Error: --volume-size requires a value."`, and the `--rolling-count` equivalents). Missing-value on the **parse** path is already printed by the dispatcher (`Error: {arg} requires a value.`) **before** `TryApply`; the "requires a value" line inside `TryApply` is for direct `TryApply(null)` only (same pattern as `BatesModule`).

**Parse-time getters:** `ProductionSet`, `RedactedProduction`, `RollingCount` (default 1), `RollingBatesMode` (default `"continuous"`), `SourcePathMode`, plus test-facing raw: `VolumeSize`, `ProductionZip`, `SupplementalProduction`, `PriorManifests`, `SupplementalGapPolicy`, `ProductionId`, `WithheldNativePolicy`.

**TryBuild:** `public bool TryBuild(out ProductionConfig config)` — absorbs every pure-production check from `ProductionSetValidator` (`ValidateDependencies` + `ValidateRollingConfig`, currently `:12–167`), **excluding** the three retained cross checks. Message order inside the module (today's remaining order after the three cross checks, which still run earlier in `ProductionSetValidator`):

1. `_productionZip && !_productionSet` → `"Error: --production-zip requires --production-set."`
2. `_volumeSize.HasValue && !_productionSet` → `"Error: --volume-size requires --production-set."`
3. `_productionSet && _volumeSize is < 1` → `"Error: --volume-size must be at least 1."`
4. `_supplementalProduction && !_productionSet` → `"Error: --supplemental-production requires --production-set."`
5. `_supplementalProduction && string.IsNullOrEmpty(_priorManifests)` → `"Error: --supplemental-production requires --prior-manifest."`
6. `!string.IsNullOrEmpty(_priorManifests) && !_supplementalProduction` → `"Error: --prior-manifest requires --supplemental-production."`
7. `_supplementalGapPolicy is not null`:
   - `!_supplementalProduction` → `"Error: --supplemental-gap-policy requires --supplemental-production."`
   - value not `reject`/`allow` (OrdinalIgnoreCase) → `"Error: --supplemental-gap-policy must be 'reject' or 'allow'."`
8. `_redactedProduction && !_productionSet` → `"Error: --redacted-production requires --production-set."`
9. `!string.IsNullOrEmpty(_withheldNativePolicy)`:
   - `!_redactedProduction` → `"Error: --withheld-native-policy requires --redacted-production."`
   - value not `keep-native`/`omit-native-path`/`replace-with-placeholder` → `"Error: --withheld-native-policy must be 'keep-native', 'omit-native-path', or 'replace-with-placeholder'."`
10. If `_productionSet` (rolling config, `ProductionSetValidator.cs:109–167`):
    - `_rollingCount <= 0` → `"Error: --rolling-count must be a positive number."`
    - `_rollingBatesMode` invalid → `"Error: --rolling-bates-mode must be 'continuous' or 'restart'."`
    - `_sourcePathMode` invalid → `"Error: --source-path-mode must be 'bates', 'preserve', or 'originals'."`
    - `GenerateProductionIds(_productionId, _rollingCount)` count mismatch → `"Error: Number of production IDs must match rolling count."`
    - duplicates (OrdinalIgnoreCase) → `"Error: Duplicate production IDs are not allowed."`
    - any blank → `"Error: Production ID cannot be empty."`

Copy `GenerateProductionIds` **verbatim** from `ProductionSetValidator.cs:169–224` as `internal static` on `ProductionModule`. Task 4 deletes the validator copy and retargets `ProductionSetGenerator`. Until then two identical copies exist — do not "improve" either.

Build `ProductionConfig` byte-identical to `RequestBuilder.cs:147–172`: `ProductionSet`, `ProductionZip`, `VolumeSize = _volumeSize ?? 5000`, `SupplementalProduction`, `PriorManifests` (split `,` with `TrimEntries | RemoveEmptyEntries`, or `Array.Empty<string>()`), `SupplementalGapPolicy = _supplementalGapPolicy ?? "reject"`, `ProductionId`, `RollingCount`, `RollingBatesMode` enum mapping (`"restart"` → `Restart`, else `Continuous`), `RedactedProduction`, `WithheldNativePolicy = _withheldNativePolicy?.ToLowerInvariant() ?? "keep-native"`, `SourcePathMode` enum mapping (`"preserve"` → `PreserveSubdirs`, `"originals"` → `Originals`, else `Bates`).

**Do NOT absorb** the three cross-domain checks (they stay in `ProductionSetValidator`): `--production-set` × `--loadfile-only`, `--production-set` requires `--bates-prefix`, `--redacted-production` × `--loadfile-only`. Those currently run *before* the pure-domain list; keeping them in the validator preserves that precedence on mixed invalid argv.

**TDD (failing first):** write `ProductionModuleTests.cs` covering each **pure-production** check + config assembly, porting the production cases from `CliValidatorTests` (which today reach them via `Validate`). The three cross-domain checks (`--production-set` × `--loadfile-only`, `--production-set` requires `--bates-prefix`, `--redacted-production` × `--loadfile-only`) stay in `ProductionSetValidator` and remain covered by `CliValidatorTests`:

- [ ] `TryApply_VolumeSize_StoresValue` / `TryApply_RollingCount_StoresValue` (numeric + invalid-value messages)
- [ ] `TryBuild_ProductionZipWithoutSet_ReturnsFalse` (+ message byte-for-byte)
- [ ] `TryBuild_VolumeSizeWithoutSet_ReturnsFalse`
- [ ] `TryBuild_VolumeSizeZeroWithSet_ReturnsFalse`
- [ ] `TryBuild_SupplementalWithoutSet_ReturnsFalse` / `TryBuild_SupplementalWithoutManifest_ReturnsFalse`
- [ ] `TryBuild_PriorManifestWithoutSupplemental_ReturnsFalse`
- [ ] `TryBuild_GapPolicyWithoutSupplemental_ReturnsFalse` / `TryBuild_GapPolicyInvalid_ReturnsFalse`
- [ ] `TryBuild_RedactedWithoutSet_ReturnsFalse`
- [ ] `TryBuild_WithheldWithoutRedacted_ReturnsFalse` / `TryBuild_WithheldInvalid_ReturnsFalse`
- [ ] `TryBuild_RollingCountZero_ReturnsFalse`
- [ ] `TryBuild_RollingBatesModeInvalid_ReturnsFalse`
- [ ] `TryBuild_SourcePathModeInvalid_ReturnsFalse`
- [ ] `TryBuild_ProductionIdCountMismatch_ReturnsFalse` / `TryBuild_ProductionIdDuplicate_ReturnsFalse` / `TryBuild_ProductionIdEmpty_ReturnsFalse`
- [ ] `TryBuild_NoFlags_BuildsDefaults` (all defaults: `VolumeSize 5000`, `Continuous`, `Bates`, `"reject"`, `"keep-native"`)
- [ ] `TryBuild_ValidFlags_MatchesRequestBuilderAssembly` (compare against the current `RequestBuilder` production block values, e.g. comma-list `--prior-manifest`, `--withheld-native-policy keep-native`, `--rolling-bates-mode restart`)
- [ ] `GenerateProductionIds_*` — new coverage (none exists today): comma list, trailing-digit increment, `_N` suffix, default timestamp **shape** (do not assert the exact `yyyyMMdd_HHmmss` clock value), count-mismatch contract (comma list length ≠ rolling count)

**Verify after each step:** `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ProductionModuleTests" && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`.

---

## Task 2: SourceInputModule

**Files:** Create `src/Cli/Modules/SourceInputModule.cs`, `src/Zipper.Tests/Modules/SourceInputModuleTests.cs`. **Additive.**

**OwnedFlags:** `--input-csv`, `--directory-template` (both take value).

**TryApply:** raw storage; `null` value → `"Error: --input-csv requires a value."` / `"Error: --directory-template requires a value."` (matches `CliParser.ReadStringArg`; dispatcher already handles the parse-path missing token).

**Parse-time getters:** `HasSourceInput` (`!IsNullOrEmpty(InputCsv) || !IsNullOrEmpty(DirectoryTemplate)`), `InputCsv`, `DirectoryTemplate`.

**TryBuild:** `public bool TryBuild(long? declaredCount, bool productionSet, BatesNumberConfig? bates, out IReadOnlyList<SourceInput.SourceRecord>? sourceRecords)` — absorbs the source-reading gate from `RequestBuilder.Build` (`RequestBuilder.cs:65–107`), the **pure-source** checks from `CrossCuttingValidator.ValidateSourceInput` (`CrossCuttingValidator.cs:35–73`: csv+dir, path-safety, existence), and `FindGeneratedIdentityCollision` (`RequestBuilder.cs:183–234`). Message order inside the module:

1. `!HasSourceInput` → `sourceRecords = null; return true;` (no-op path).
2. `HasCsv && HasDirectory` → `"Error: --input-csv and --directory-template cannot be used together."`
3. `!PathValidator.IsPathSafe(sourcePath, Directory.GetCurrentDirectory())` → `"Error: Path traversal detected in source input path '{sourcePath}'. Source input must reside within working directory."`
4. existence: `"Error: Source CSV '{sourcePath}' does not exist."` / `"Error: Directory template '{sourcePath}' does not exist."`
5. `SourceCsvReader.TryRead`/`DirectoryTemplateReader.TryRead` (same branch as `RequestBuilder.cs:69–71`) → on failure `"Error: {readError}"` (REQ-199/REQ-207: the 10M cap error comes from the reader; do not wrap or reword).
6. `declaredCount.HasValue && declaredCount.Value != rows.Count` → `"Error: --count ({declaredCount}) does not match the Source Record count ({rows.Count}). Align --count with the source input or omit it."`
7. `!productionSet && rows.Any(r => r.BatesNumber is not null) && bates is null` → `"Error: the source 'BatesNumber' column requires --bates-prefix so the Bates column is emitted."` The `!productionSet` guard is defensive: production-set still requires `--bates-prefix` in `ProductionSetValidator`, so `bates` is non-null on that path. Do not drop the guard and do not treat it as a new check.
8. `productionSet && rows.Any(r => r.BatesNumber is not null)` → `"Error: the source 'BatesNumber' column cannot be used with --production-set. Production Set Bates Numbers come from the configured Bates sequence so Volume ranges in the Production Manifest stay exact."`
9. `FindGeneratedIdentityCollision(rows, bates)` non-null → `"Error: {identityCollision}"` (moved verbatim, including the Control-Number `DOC{index:D8}` and Bates-sequence collision logic).
10. `sourceRecords = rows; return true;`

**Do NOT absorb** the cross-domain source checks that stay in `CrossCuttingValidator`: `--source-path-mode` requires `--production-set`, `--source-path-mode` requires source input, `--type`/`--types` × source input. Those still run at Validate time, before any `TryBuild`.

**TDD (failing first):** write `SourceInputModuleTests.cs`. Identity-collision / count-mismatch / Bates-column contracts today live in `SourceDrivenCliTests` (`Pipeline.Build`), **not** `RequestBuilderTests`. Add module-level tests; leave the `Pipeline.Build` tests as the parity guard.

- [ ] `TryBuild_NoSourceInput_ReturnsTrueWithNullRecords`
- [ ] `TryApply_MissingValue_ReturnsFalse` (both flags)
- [ ] `TryBuild_CsvAndDirectoryTogether_ReturnsFalse`
- [ ] `TryBuild_PathTraversal_ReturnsFalse`
- [ ] `TryBuild_MissingCsv_ReturnsFalse` / `TryBuild_MissingDirectory_ReturnsFalse`
- [ ] `TryBuild_CountMismatch_ReturnsFalse`
- [ ] `TryBuild_BatesColumnWithoutPrefix_ReturnsFalse`
- [ ] `TryBuild_BatesColumnWithProductionSet_ReturnsFalse`
- [ ] `TryBuild_IdentityCollisionControlNumber_ReturnsFalse` (row `DOC00000001` for index 1; and a non-colliding `DOC99999999` passes)
- [ ] `TryBuild_IdentityCollisionBates_ReturnsFalse` (bates prefix/start/digits matching a generated value; and non-colliding passes)
- [ ] `TryBuild_ValidCsv_ReturnsRecords` (real temp CSV; assert `records.Count` + a `SourceRecord` field; `[Collection("ConsoleTests")]` + temp dir pattern from `MixedFileTypeCliTests`)
- [ ] Use a real CSV fixture written to a temp dir — never mock `SourceCsvReader`.

**Verify after each step:** same combo as Task 1, filter `SourceInputModuleTests`, then full unit suite.

---

## Task 3: OutputModule

**Files:** Create `src/Cli/Modules/OutputModule.cs`, `src/Zipper.Tests/Modules/OutputModuleTests.cs`. **Additive.**

**OwnedFlags:** `--type`, `--types`, `--count`, `--output-path`, `--folders`, `--encoding`, `--distribution`, `--target-zip-size`, `--include-load-file`, `--with-text`.

**TakesValue:** `false` for `--include-load-file`, `--with-text`; `true` otherwise.

**TryApply:** raw storage; numeric parse for `--count` (long) and `--folders` (int) with the exact `CliParser.ReadLongArg`/`ReadIntArg` messages; `--encoding` sets `_isEncodingExplicit = true`. `_folders` defaults to `1`. `_encoding` defaults to `"UTF-8"`. `_distribution` defaults to `"proportional"`. Those three defaults match `ParsedArguments` today so `LoadFileModule`'s `?? "UTF-8"` / `?? "proportional"` stay equivalent.

**Parse-time getters:** `FileType`, `FileTypes`, `Count`, `TargetZipSize` (raw string), `Encoding`, `IsEncodingExplicit`, `Distribution`, `IncludeLoadFile`, `Folders`, `WithText`.

**TryBuild:** `public bool TryBuild(IReadOnlyList<SourceInput.SourceRecord>? sourceRecords, out OutputConfig config)` — absorbs the pure-output checks from `CliValidator` (count bounds), `StandardModeValidator` (path, known type, folders, target-zip-size format), `CrossCuttingValidator.ValidateFileTypeMix` (`--type` × `--types`, ratio syntax) + `ValidateEncodingAndDistribution`, and the output assembly + path resolution + ratio parse from `RequestBuilder` (`RequestBuilder.cs:36–63, 123–140`).

**Check order** — preserve today's validator-relative order (count bounds were in `CliValidator` first; then `StandardModeValidator`; then `CrossCuttingValidator`), not an invented "format then folders" order:

1. Count bounds (`CliValidator.cs:64–74`): `_count is <= 0` → `"Error: --count must be a positive number."`; `_count > int.MaxValue - 1` → `"Error: --count must not exceed {int.MaxValue - 1}."`
2. Path (`StandardModeValidator.cs:7–11` + `PathValidator.ResolveSecurePath`): `string.IsNullOrWhiteSpace(_outputPath)` → `"Error: Output path is required."`; then `ResolveSecurePath(_outputPath, cwd)` returns null → `return false` (its own messages: `"Error: Output path cannot be null or empty."` is **not** reachable after the whitespace check; traversal/format/too-long still are).
3. Known type (`StandardModeValidator.cs:13–17`): `_fileType` set and `!FileGeneratorFactory.IsKnownType(_fileType)` → `"Error: Unsupported file type '{x}'. Supported types: pdf, jpg, tiff, eml, docx, xlsx."`
4. Folders (`StandardModeValidator.cs:27–31`): `_folders < 1 || _folders > 100` → `"Error: Number of folders must be between 1 and 100."`
5. `--target-zip-size` format (`StandardModeValidator.cs:33–46`): `_targetZipSize` set → `RequestBuilder.ParseSize(_targetZipSize)` null → `"Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB)."`; parsed `<= 0` → `"Error: --target-zip-size must be positive."` Parse **once**; reuse the `long?` when assembling `OutputConfig`.
6. `--type` × `--types` (`CrossCuttingValidator.cs:84–88`): both set → `"Error: --type and --types cannot be used together. Use --types for a File Type mix."`
7. Ratio syntax (`CrossCuttingValidator.cs:104–108` + `RequestBuilder.cs:47–51`): `FileTypeRatioParser.TryParse(_fileTypes, out _, out var error)` fails → `"Error: {error}"` (parse once; keep the ratios for config assembly).
8. Encoding (`CrossCuttingValidator.cs:115–119`): `_encoding` set and `RequestBuilder.GetEncodingFromName(_encoding) is null` → `"Error: Invalid encoding '{x}'. Supported values are UTF-8, UTF-16, ANSI."` Default `"UTF-8"` is valid; checking it is equivalent to today's always-non-empty bag default.
9. Distribution (`CrossCuttingValidator.cs:121–125`): `_distribution` set and `RequestBuilder.GetDistributionFromName(_distribution) is null` → `"Error: Invalid distribution '{x}'. Supported values are proportional, gaussian, exponential."`

Then assemble `OutputConfig` byte-identical to `RequestBuilder.cs:123–140`: resolve `fileType = (_fileType ?? "pdf").ToLowerInvariant()`; single-ratio collapse when `parsedRatios.Count == 1`; else `FileTypeRatios = parsedRatios`, `FileTypePlan = new FileTypePlan(parsedRatios, _count!.Value)`, `fileType = parsedRatios[0].Type`; then `OutputPath = resolved.FullName`, `FileCount = sourceRecords is not null ? sourceRecords.Count : _count!.Value`, `FileType = sourceRecords is not null ? sourceRecords[0].FileType : fileType`, `FileTypeRatios`, `FileTypePlan`, `SourceFileTypes` (distinct, `StringComparer.Ordinal`, `OrderBy` Ordinal — copy the RequestBuilder projection exactly), `Folders = _folders`, `Concurrency = PerformanceConstants.DefaultConcurrency`, `WithText = _withText`, `TargetZipSize = parsedSize` (the `long?` from step 5, or `ParseSize` if the flag was valid), `IncludeLoadFile = _includeLoadFile`.

**Do NOT absorb:** `--type is required` / `--count is required` (stay in `CliValidator`, cross-domain waivers), `--target-zip-size requires --count or source` (stays in `StandardModeValidator`), the image-type override (stays in `RequestBuilder`, keys off `output.FileType`/`FileTypeRatios` + `sourceRecords` + `isLoadFileFormatExplicit`), `--types` × `--loadfile-only` / × `--column-profile` (stay in `CrossCuttingValidator`).

**TDD (failing first):** write `OutputModuleTests.cs` porting from `CliValidatorTests` + `MixedFileTypeCliTests` ratio behavior + `RequestBuilderTests` assembly:

- [ ] `TryApply_Count_StoresValue` / `TryApply_CountInvalid_ReturnsFalse` / `TryApply_FoldersInvalid_ReturnsFalse`
- [ ] `TryBuild_CountZero_ReturnsFalse` / `TryBuild_CountExceedsMax_ReturnsFalse` (message byte-for-byte)
- [ ] `TryBuild_NullOutputPath_ReturnsFalse` (the `"Output path is required."` message, not `ResolveSecurePath`'s)
- [ ] `TryBuild_PathTraversal_ReturnsFalse` (from `ResolveSecurePath`)
- [ ] `TryBuild_UnknownType_ReturnsFalse`
- [ ] `TryBuild_TypeAndTypes_ReturnsFalse`
- [ ] `TryBuild_InvalidRatio_ReturnsFalse` (e.g. `--types bogus:1`)
- [ ] `TryBuild_TargetZipSizeInvalidFormat_ReturnsFalse` / `TryBuild_TargetZipSizeZero_ReturnsFalse`
- [ ] `TryBuild_InvalidEncoding_ReturnsFalse` / `TryBuild_InvalidDistribution_ReturnsFalse`
- [ ] `TryBuild_FoldersOutOfRange_ReturnsFalse`
- [ ] `TryBuild_SingleType_MatchesRequestBuilderAssembly` (compare OutputConfig fields vs today's `RequestBuilder.Build` output)
- [ ] `TryBuild_SingleRatioMix_BehavesAsSingleType` (port `MixedFileTypeCliTests.Build_SingleEntryMix_BehavesAsSingleType` semantics)
- [ ] `TryBuild_MultiRatioMix_CreatesFileTypePlan` (plan counts 5/3/2 from `pdf:50,eml:30,tiff:20`, count 10)
- [ ] `TryBuild_SourceDriven_ComputesFromRecords` (records → FileCount/FileType/SourceFileTypes)
- [ ] **Do not** add `TryBuild_NoSourceNoCount_ThrowsNullRef`. `--count is required` stays in `CliValidator`; `TryBuild` without count and without records is only reachable if the caller broke the contract. Keep `_count!.Value` — do not add a new guard.

`RequestBuilderTests.Build_WithInvalidPath_ReturnsNull` currently skips `CliValidator` and hits `ResolveSecurePath` via the helper. After path resolution moves, port that case onto `OutputModule.TryBuild` (empty path → `"Output path is required."`).

**Verify after each step:** same combo as Task 1, filter `OutputModuleTests`, then full unit suite.

---

## Task 4: Wire into the Pipeline (atomic)

**Files:** all modify steps + signature changes + test retargets. One cut-over commit. Registration + parser-case removal cannot be split (module `Owns` would swallow the token and the switch would never run). Update tests in the same change so the tree stays green.

### Step 1 — CliModules registration

- [ ] Add `required ProductionModule Production`, `required SourceInputModule SourceInput`, `required OutputModule Output` to `CliModuleSet`; register in `Create()`; add all three to `All` (order: `Production`, `SourceInput`, `Output`, then existing seven). Flag ownership is disjoint, so `All` order does not change dispatch.

### Step 2 — OutputModule/ProductionModule/SourceInputModule TryBuild signature (already built in Tasks 1–3)

### Step 3 — Retarget sibling `TryBuild` signatures

- [ ] `BatesModule.TryBuild(bool productionSet, int rollingCount, string? rollingBatesMode, long? count, out BatesNumberConfig? config)` — `ValidateRollingBates` reads the four params where it read `parsed.ProductionSet`/`parsed.RollingCount`/`parsed.RollingBatesMode`/`parsed.Count` (`BatesModule.cs:103, 142, 154, 162, 164, 171, 195`). Gating unchanged: rolling validation only runs when `productionSet`.
- [ ] `MetadataModule.TryBuild(bool includesEml, bool hasSourceInput, out MetadataConfig config)` — replace `hasSourceInput` computation (`:106`) with the param; replace `IncludesEml(parsed)` (`:110`) with `includesEml`; delete `IncludesEml` helper (`:177–187`).
- [ ] `LoadFileModule.TryBuild(int attachmentRate, string? encoding, bool isEncodingExplicit, string? distribution, string? targetZipSize, bool includeLoadFile, out LoadFileConfig config)` — replace `parsed.TargetZipSize`/`parsed.IncludeLoadFile` (`:86, 93`), `parsed.Encoding`/`parsed.IsEncodingExplicit`/`parsed.Distribution` (`:138–148`) with the params. `targetZipSize` is the **raw string**, not `OutputConfig.TargetZipSize` (`long?`). Keep internal calls to `RequestBuilder.GetEncodingFromName`/`GetDistributionFromName`/`GetLoadFileFormat` (transitional, Phase 4).
- [ ] `DelimiterModule.TryBuild(bool loadfileOnly, bool productionSet, out DelimiterConfig config)` — replace `parsed.ProductionSet` (`:51`).
- [ ] `TiffModule.TryBuild(out TiffConfig config)` / `HashModule.TryBuild(bool loadfileOnly, out HashConfig config)` / `ChaosModule.TryBuild(bool loadfileOnly, LoadFileFormat currentFormat, out ChaosConfig config)` — drop unused `parsed` (they only null-check it today).

### Step 4 — CliParser

- [ ] Remove 18 switch cases: `--type`, `--types`, `--input-csv`, `--directory-template`, `--count`, `--output-path`, `--folders`, `--encoding`, `--distribution`, `--target-zip-size`, `--volume-size`, `--prior-manifest`, `--supplemental-gap-policy`, `--production-id`, `--rolling-count`, `--rolling-bates-mode`, `--source-path-mode`, `--withheld-native-policy`.
- [ ] Remove all 6 `ParameterlessFlags` entries (`--with-text`, `--include-load-file`, `--production-set`, `--production-zip`, `--supplemental-production`, `--redacted-production`); the dict is now empty → delete it and its dispatch block.
- [ ] Keep: the module-ownership dispatch (`modules.FirstOrDefault(m => m.Owns(arg))`), the comparison switch cases, `ReadStringArg`, `TryGetValue`. Delete `ReadIntArg`/`ReadLongArg` (unused; numeric parse moved to module `TryApply`).
- [ ] `ReadStringArg`-style `--type`/`--count` value-required messages on the parse path stay in the dispatcher (`Error: {arg} requires a value.`). Verify byte equality against today's `CliParserTests.Parse_MissingTypeValue` / `Parse_MissingValueForValueTakingFlags`.

### Step 5 — ParsedArguments

- [ ] Delete 26 properties (everything except the comparison trio): `FileType`, `FileTypes`, `InputCsv`, `DirectoryTemplate`, `SourcePathMode`, `Count`, `OutputDirectory` (dead — only `FieldNamingTests` sets it, nothing reads it), `OutputPathStr`, `Folders`, `Encoding`, `IsEncodingExplicit`, `Distribution`, `WithText`, `TargetZipSize`, `IncludeLoadFile`, `ProductionSet`, `ProductionZip`, `VolumeSize`, `SupplementalProduction`, `PriorManifests`, `SupplementalGapPolicy`, `ProductionId`, `RollingCount`, `RollingBatesMode`, `RedactedProduction`, `WithheldNativePolicy`. Keep `CompareProductionManifests`, `ComparisonMode`, `ComparisonOutput`.

### Step 6 — Validators

- [ ] `CliValidator.Validate(ParsedArguments parsed, CliModuleSet modules)`:
  - comparison branch unchanged (incl. `return true` short-circuit). Still reads the bag.
  - `--type is required` (`:52`): read `modules.Output.FileType`, `modules.Output.FileTypes`, `modules.LoadFile.LoadfileOnly`, `modules.Production.ProductionSet`, `modules.SourceInput.HasSourceInput`.
  - `--count is required` (`:58`): read `modules.Output.Count`, `modules.SourceInput.HasSourceInput`.
  - **Delete** the count-bounds block (`:64–74`) — it moved to `OutputModule.TryBuild`. After this, `CliValidator.Validate` with `--count 0` returns **true** (count is present). `Validate_CountZero` / `Validate_CountNegative` / `Validate_CountExceedsMax` must move to `OutputModuleTests`.
  - Delegate: `StandardModeValidator.Validate(modules)`, `ProductionSetValidator.Validate(modules)`, `CrossCuttingValidator.Validate(modules)`.
- [ ] `StandardModeValidator.Validate(CliModuleSet modules)` — **one** check: `!string.IsNullOrEmpty(modules.Output.TargetZipSize) && !modules.Output.Count.HasValue && !modules.SourceInput.HasSourceInput` → `"Error: --target-zip-size requires --count to be specified."`. Delete everything else. This is the first time this validator takes `modules` (Phase 2 left `Validate(ParsedArguments)`).
- [ ] `ProductionSetValidator.Validate(CliModuleSet modules)` — **three** checks, reading getters: `--production-set` × `modules.LoadFile.LoadfileOnly`; `--production-set` requires `modules.Bates.HasBatesPrefix`; `--redacted-production` (`modules.Production.RedactedProduction`) × `modules.LoadFile.LoadfileOnly`. Delete everything else + `GenerateProductionIds`.
- [ ] `ProductionSetGenerator.cs:42` — `ProductionModule.GenerateProductionIds(request.Production.ProductionId, rollingCount)`. Same assembly; `internal static` is enough. Do not wrap, do not move the helper to a third type.
- [ ] `CrossCuttingValidator.Validate(CliModuleSet modules)` — keep:
  - `ValidateFileTypeMix`: `--types` × `modules.LoadFile.LoadfileOnly`; `--types` × `modules.Metadata.HasColumnProfile`. (Drop `--type`×`--types` + ratio syntax → OutputModule.)
  - `ValidateSourceInput`: `--source-path-mode` requires `modules.Production.ProductionSet`; `--source-path-mode` requires source input (`modules.SourceInput.HasSourceInput`); `--type`/`--types` × source input (read `modules.Output.FileType`/`FileTypes`). (Drop csv+dir conflict, path-safety, existence → SourceInputModule.)
  - `ValidateColumnProfile`: `modules.Metadata.HasColumnProfile` × `modules.Production.ProductionSet`.
  - (Drop `ValidateEncodingAndDistribution` entirely → OutputModule.)
  - All reads via getters; delete `ParsedArguments` param and any unused imports.

### Step 7 — RequestBuilder

New signature:

```csharp
public static FileGenerationRequest? Build(
    OutputConfig output, MetadataConfig metadata, LoadFileConfig loadFile,
    DelimiterConfig delimiters, BatesNumberConfig? bates, TiffConfig tiff,
    ChaosConfig chaos, HashConfig hash, ProductionConfig production,
    IReadOnlyList<SourceInput.SourceRecord>? sourceRecords,
    bool loadfileOnly, bool isLoadFileFormatExplicit)
```

- [ ] Delete: `PathValidator.ResolveSecurePath` block (`:36–40`), fileType/ratio section (`:42–63`), source-reading + count-vs-rows + bates-column + identity-collision block (`:65–107`), `FindGeneratedIdentityCollision` (`:183–234`), `OutputConfig` assembly (`:125–140`), `ProductionConfig` assembly (`:147–172`).
- [ ] Keep, unchanged semantics: image-type override (`:112–121`) now reading `output.FileType` (was local `fileType`), `output.FileTypeRatios` (was local `fileTypeRatios`), `sourceRecords`; assembly `FileGenerationRequest { Output = output, Metadata = metadata, LoadFile = loadFile, Delimiters = delimiters, Bates = bates, Tiff = tiff, Chaos = chaos, Production = production, LoadfileOnly = loadfileOnly, Hash = hash, SourceRecords = sourceRecords }`.
- [ ] `ThrowIfNull` the remaining required configs (`output`, `metadata`, `loadFile`, `delimiters`, `tiff`, `chaos`, `hash`, `production`). `bates` and `sourceRecords` stay nullable.
- [ ] Keep internal statics `ParseSize`, `GetEncodingFromName`, `GetDistributionFromName`, `GetLoadFileFormat` (modules + validators call them transitionally until Phase 4).
- [ ] `hasImageType` must keep reading the **resolved** `output.FileType` (`output.FileType is "tiff" or "jpg"`) plus `output.FileTypeRatios` plus `sourceRecords` — do not switch it to `output.HasFileType("tiff") || output.HasFileType("jpg")` (different when source types and a mix plan interact). Copy the current predicate, substituting the new names.

### Step 8 — Pipeline

```csharp
if (!modules.Production.TryBuild(out var production) ||
    !modules.Bates.TryBuild(modules.Production.ProductionSet, modules.Production.RollingCount, modules.Production.RollingBatesMode, modules.Output.Count, out var bates) ||
    !modules.SourceInput.TryBuild(modules.Output.Count, modules.Production.ProductionSet, bates, out var sourceRecords) ||
    !modules.Output.TryBuild(sourceRecords, out var output) ||
    !modules.Metadata.TryBuild(output.HasFileType("eml"), modules.SourceInput.HasSourceInput, out var metadata) ||
    !modules.LoadFile.TryBuild(
        modules.Metadata.AttachmentRate,
        modules.Output.Encoding,
        modules.Output.IsEncodingExplicit,
        modules.Output.Distribution,
        modules.Output.TargetZipSize,
        modules.Output.IncludeLoadFile,
        out var loadFile) ||
    !modules.Delimiter.TryBuild(modules.LoadFile.LoadfileOnly, modules.Production.ProductionSet, out var delimiters) ||
    !modules.Tiff.TryBuild(out var tiff) ||
    !modules.Chaos.TryBuild(modules.LoadFile.LoadfileOnly, modules.LoadFile.CurrentFormat, out var chaos) ||
    !modules.Hash.TryBuild(modules.LoadFile.LoadfileOnly, out var hash))
{
    return null;
}

return RequestBuilder.Build(output, metadata, loadFile, delimiters, bates, tiff, chaos, hash, production, sourceRecords, modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
```

`LoadFile.TryBuild` reads **`modules.Output.*` getters**, not `output.Encoding` / `output.TargetZipSize`. `OutputConfig` has neither a string encoding nor a string target-zip-size.

**Documented multi-invalid message-order divergences (accepted; do not fix):**

| argv (sketch) | today wins | after Task 4 wins | why |
|---|---|---|---|
| `--count 0 --volume-size 0` | count bounds (`CliValidator`) | `--volume-size requires --production-set` (`Production.TryBuild` pos 1) | count bounds moved to Output pos 4 |
| `--count 0 --volume-size 0 --production-set --bates-prefix X` | count bounds | `--volume-size must be at least 1` | same |
| `--type bogus --rolling-count 0 --production-set --bates-prefix X --count 1 --output-path .` | unsupported type (`StandardModeValidator`) | `--rolling-count must be a positive number` | production TryBuild now before output |
| `--count 0 --input-csv missing.csv --output-path .` | count bounds | `Source CSV … does not exist` | existence moved to SourceInput pos 3; count bounds to Output pos 4 |
| `--folders 0 --encoding BAD --type pdf --count 1 --output-path .` | folders (`StandardModeValidator` before CrossCutting) | folders still (OutputModule keeps folders before encoding) | **not** a divergence if Task 3 order is followed |
| `--count 0 --type bogus --output-path .` | count bounds | count bounds (still first inside OutputModule) | no divergence |

Single-invalid invocations keep exact messages.

### Step 9 — Test retargets (Rule 3: port, never delete without replacement)

- [ ] `RequestBuilderTestHelper.cs` — rewrite to mirror Step 8: `Parse(string[] args)` returns `(ParsedArguments?, CliModuleSet)`; `Build(CliModuleSet? modules = null, Action<CliModuleSet>? configure = null)` runs the 10-module chain and calls new `RequestBuilder.Build(...)`; provide a backward-compatible `Build(ParsedArguments? parsed, Action<CliModuleSet>? configure = null, CliModuleSet? modules = null)` overload delegating to `Build(modules, configure)`; and `Build(string[] args)` delegating to `Cli.Pipeline.Build(args)`. Every remaining helper caller must go through argv `Parse` (or explicit `TryApply` on the shared set) so the new modules are populated.
- [ ] `CliValidatorTests.cs` — `CreateValidArgs` becomes argv-based: `var args = new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory() }; var modules = CliModules.Create(); var parsed = CliParser.Parse(args, modules.All);` then `CliValidator.Validate(parsed, modules)`. Tests that set moved props configure via `TryApply` on the shared set (e.g. `args.FileType = null` → parse without `--type`; `args.ProductionSet = true` → `modules.Production.TryApply("--production-set", null)`). Move the pure-domain cases (`CountBounds`, `FoldersOutOfRange`, `NullOutputPathStr`, `InvalidEncoding`/`Distribution`/`TargetZipSize`) to `OutputModuleTests`; move production cases (`ProductionZip`, `VolumeSize`, supplemental/gap-policy) to `ProductionModuleTests`. Keep on `CliValidatorTests`: type/count required + waivers, target-zip-size-without-count, the three production cross checks, comparison trio, column-profile × production-set, and any remaining `--types` × loadfile-only / profile cases that still live here.
- [ ] `CliParserTests.cs` — comparison-bag + parse-null + unknown-flag cases stay. Retarget bag reads: `Parse_RequiredArgs` (`FileType`/`Count`), `Parse_AllBooleanFlags` (`WithText`/`IncludeLoadFile`/`ProductionSet`/`ProductionZip`), `Parse_ProductionSetArgs` (`ProductionSet`/`VolumeSize`). `Parse_MissingValueForValueTakingFlags` stays (dispatcher). `Parse_OutputPathWithParentTraversal_*` still Parse→Validate→helper.Build; after Task 4 the rejection moves from RequestBuilder to `Output.TryBuild` — assert `Build` still returns null, do not change the test name.
- [ ] `RequestBuilderTests.cs` — `Build_NullArg` / `Build_NullConfigArg` retarget onto the new signature (no `parsed`; add `production`/`output` to the required-null list). `Build_WithInvalidPath` → `OutputModuleTests`. `Build_StandardMode_SetsAllDefaults` / `Build_WithValidPath` / `Build_ProductionSet_SetsVolumeSize` go through the rewritten helper (argv parse, not `new ParsedArguments { FileType = … }`). Keep `ParseSize` / `GetDistributionFromName` / `GetLoadFileFormat` tests here — those statics stay.
- [ ] `BatesModuleTests` — every `TryBuild(parsed, …)` becomes `TryBuild(productionSet, rollingCount, rollingBatesMode, count, …)`. The seven `ProductionSet = true` cases pass those values as args. Delete `TryBuild_NullArg` (its only contract was null `parsed`).
- [ ] `MetadataModuleTests` — `TryBuild(parsed, …)` → `TryBuild(includesEml, hasSourceInput, …)`. `CreateParsedArgs().FileType = "eml"` becomes `includesEml: true`. Family-warning tests in `CliValidatorTests` that call `Metadata.TryBuild(args)` need the same.
- [ ] `LoadFileModuleTests` — drop `parsed`; pass encoding/distribution/targetZipSize/includeLoadFile as params. `TryBuild_LoadfileOnly_WithTargetZipSize` / `WithIncludeLoadFile` currently set bag fields — pass `"100MB"` / `true` instead.
- [ ] `DelimiterModuleTests` — `new ParsedArguments { ProductionSet = true }` → `productionSet: true`.
- [ ] `HashModuleTests` / `ChaosModuleTests` / `TiffModuleTests` — drop the dummy `new ParsedArguments()` first argument.
- [ ] `FieldNamingTests.cs:194–208` — `module.TryApply("--loadfile-only", null); module.TryApply("--load-file-format", "csv"); Assert.False(module.TryBuild(0, null, false, null, null, false, out _))`. Delete the unused `ParsedArguments` / `OutputDirectory` setup.
- [ ] `SourceDrivenCliTests.Parse_InputCsvAndDirectoryTemplate_StoreRawValues` — read `modules.SourceInput.InputCsv` / `DirectoryTemplate` via `RequestBuilderTestHelper.Parse`. Leave every `Pipeline.Build` test alone.
- [ ] `MixedFileTypeCliTests.Parse_TypesArgument_StoresRawValue` — read `modules.Output.FileTypes`. Leave every `Pipeline.Build` test alone.
- [ ] `CliPipelineTests` — must pass **unchanged**.

**Verify:** full combo (`build + format + both test projects`), then E2E + goldens (Task 5).

---

## Task 5: Architecture, E2E, Self-Review

1. **Architecture doc (Rule 5):** update the Component Map CLI Layer in `docs/architecture.md` to the 10-module set (`Bates`, `Metadata`, `LoadFile`, `Delimiter`, `Tiff`, `Chaos`, `Hash`, `Production`, `SourceInput`, `Output`), the new build chain ordering, and note the bag is comparison-only. Keep `CliValidator` / `RequestBuilder` on the diagram (they still exist). Same PR. Re-review the mermaid — this plan review is not architecture approval.
2. **Full gate:**
   ```bash
   dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj
   dotnet build -c Release
   bash tests/goldens/run-goldens.sh          # 20 scenarios, byte-exact
   bash tests/run-tests.sh                     # E2E incl. test-mixed-file-types, test-source-driven, test-production-sets, test-target-zip-size, test-path-traversal-security, test-argument-interactions
   ```
   Goldens + E2E must be green before the PR. Regenerate nothing; if a golden byte drifts, stop and root-cause (it means a porting bug), do not regen.
3. **Perf:** `tests/perf/measure.sh` vs `tests/perf/baselines.json` is a **generation** RSS/wall gate (pdf_50k / eml_20k / loadfile_200k), not a parse microbench. A pure CLI-module move must stay within noise; **do not refresh `baselines.json`**. CI `perf-guard.yml` already runs on `src/**`.
4. **Adversarial review (autoreview skill)** before PR — mandatory (logic + error handling + public contracts).
5. **PR body:** `Refs #750` (or `Towards #750`, never `Fixes`), `## Release Notes` (small-change scale, 1–2 sentences), Architecture checklist checked (diagram updated same PR), corrections to the issue table flagged (comparison flags placement, `--source-path-mode` home, `--prior-manifest` singular), accepted message-order divergences documented.
6. **Post-merge:** `tests/wait-for-reviews.sh` flow per AGENTS.md; close nothing (tracker issue #750 stays open until Phase 4).

---

## Grounding notes (review 2026-08-14)

Corrections against the previous draft of this plan (verified against the tree at `7d5472a` + working copy):

1. **Maintainer decisions (2026-08-14, question tool):** (a) trim-but-keep validators — modules absorb pure-domain checks, cross-domain checks stay until Phase 4; (b) drop `parsed` from every `TryBuild`; (c) comparison flags stay in `CliParser`/`CliValidator`. These three answers shape the entire plan. If any is revisited, the seam design changes (esp. (a): "absorb + delete" would thread `loadfileOnly`/`hasBatesPrefix`/`hasSourceInput` into `ProductionModule.TryBuild` and delete `StandardModeValidator` + `ProductionSetValidator`).
2. **`CliValidator` still takes `ParsedArguments`.** The previous draft said every validator becomes `Validate(CliModuleSet)` with no bag. Only the three *mode* validators drop the bag. Comparison still reads `parsed.CompareProductionManifests` / `ComparisonMode` / `ComparisonOutput`.
3. **Step 8 cannot read `output.Encoding` / `output.IsEncodingExplicit` / `output.Distribution` / `output.TargetZipSize`.** Those are not `OutputConfig` fields. `OutputConfig.TargetZipSize` is `long?`; LoadFile's loadfile-only conflict needs the raw string. Pass `modules.Output.*` getters.
4. **`GenerateProductionIds` is live.** `ProductionSetGenerator.cs:42` calls `ProductionSetValidator.GenerateProductionIds`. Moving it as `internal static` on `ProductionModule` is fine (same assembly) but the generator call site is a required Task 4 edit. The previous draft omitted `ProductionSetGenerator.cs` from the modify list — that would not compile.
5. **`IncludesEml` ≠ `HasFileType("eml")` without the source gate.** Old helper only inspected `--type` / `--types`. `HasFileType` also inspects `SourceFileTypes`. Keep `if (_withFamilies && !hasSourceInput && (!includesEml || _attachmentRate <= 0))` so source-driven still skips the warning.
6. **`HashModule` / `ChaosModule` / `TiffModule` still take `parsed`.** They do not read it (only `ThrowIfNull`). Decision (b) requires dropping it; the previous draft listed their signatures as "unchanged shape" and then omitted them from Step 3.
7. **`Folders` / `Encoding` / `Distribution` defaults.** `ParsedArguments` defaults are `Folders = 1`, `Encoding = "UTF-8"`, `Distribution = "proportional"`. OutputModule must keep those or every no-`--folders` run fails the 1–100 check and LoadFile encoding/distribution drift.
8. **Check order inside OutputModule.** The previous draft ran target-zip / encoding / distribution *before* folders. Today `StandardModeValidator` checks folders before target-zip format, and both before CrossCutting encoding. The new order in Task 3 preserves that so `--folders 0 --encoding BAD` still prints the folders error.
9. **`RequestBuilderTests` has no identity-collision / Bates-column / count-vs-rows cases.** Those live in `SourceDrivenCliTests` (`Pipeline.Build`). Do not go looking in `RequestBuilderTests` for them. `RequestBuilderTests` currently has 18 facts: defaults, two null-arg tests, path resolve/reject, loadfile-only, production volume, bates, profile, multi-format, two encoding tests, plus six static-helper facts (`ParseSize` ×2, `GetDistributionFromName` ×2, `GetLoadFileFormat` ×2).
10. **Two "parity guard" tests *do* read the bag.** `MixedFileTypeCliTests.Parse_TypesArgument_StoresRawValue` and `SourceDrivenCliTests.Parse_InputCsvAndDirectoryTemplate_StoreRawValues` will not compile after Step 5. Retarget those two. Leave the `Pipeline.Build` tests untouched.
11. **`OutputDirectory`** (`ParsedArguments.cs:17`) is dead: set only by `FieldNamingTests` (which is retargeted), read by nothing. Deleted in Step 5.
12. **`FieldNamingTests` has no `CliValidator_*` tests beyond `LoadFileModule_ShouldRejectInvalidFormatInLoadFileOnlyMode`** — that one plus the two parse-bag tests above are the only retargets outside `src/Zipper.Tests/Cli/` + `Modules/`.
13. **Message-order divergences are limited to multi-invalid argv** (documented in Step 8). Single-invalid messages are byte-identical because each check's message text and trigger condition move verbatim.
14. **`--count` bounds move to `OutputModule.TryBuild` but `--count is required` stays in `CliValidator`** — this is why `OutputModule.Count` is a parse-time getter: `CliValidator` must see it before any build. Same for `--type is required` (reads `Output.FileType`/`FileTypes`), `StandardModeValidator` (reads `Output.TargetZipSize`/`Output.Count`/`SourceInput.HasSourceInput`), `ProductionSetValidator` (reads `Production.ProductionSet`/`Production.RedactedProduction`), `CrossCuttingValidator` (reads `Output.FileType`/`FileTypes`, `Production.ProductionSet`/`SourcePathMode`, `SourceInput.HasSourceInput`).
15. **Bates/SourceInput ordering is not circular** despite the issue's concern: `BatesModule` needs `productionSet`/`rollingCount`/`rollingBatesMode`/`count` which are all **parse-time** getters, and `SourceInputModule` needs the **built** `bates` config — so `Production → Bates → SourceInput` resolves cleanly. No module takes the whole set.
16. **Golden coverage is partial** for Phase-3 flags (list in Global Constraints). E2E `test-mixed-file-types.sh`/`test-source-driven.sh`/`test-production-sets.sh`/`test-target-zip-size.sh` cover the gaps; module tests cover the rest. Do not claim goldens exercise `--types`/`--input-csv`. Previous draft missed `eml-full` in the `--with-text` golden list.
17. **DelimiterModule still reads `parsed.ProductionSet` (`DelimiterModule.cs:51`)** — the last Phase-1/2 leaf bag read; Task 4 fixes it via the `productionSet` param. Bates/Metadata/LoadFile transitional bag reads (annotated in Phase 2) go away in the same cut-over.
18. **`RequestBuilder.GetLoadFileFormat`/`GetEncodingFromName`/`GetDistributionFromName`/`ParseSize`** stay on `RequestBuilder` as internal statics; `LoadFileModule` already calls them today (`LoadFileModule.cs:54, 63, 74, 100, 112, 127, 134, 138`). Phase 3 modules call them the same way; Phase 4 relocates them when `RequestBuilder` is deleted.
19. **Perf baselines are generation RSS, not parse time.** Do not refresh `tests/perf/baselines.json` for this phase.
20. **Issue table name: `--prior-manifests`.** Real flag in `CliParser.cs` and `ParsedArguments.PriorManifests` is `--prior-manifest`.

## Phase Roadmap (issue #750)

- **Phase 1 (done, PR #755):** Hash, Delimiter, Tiff, Chaos modules.
- **Phase 2 (done, PR #756):** Bates, Metadata, LoadFile modules; `parsed` bag reads annotated transitional.
- **Phase 3 (this plan):** Production, SourceInput, Output modules; all remaining bag reads removed; validators trimmed; bag → comparison-only; `TryBuild` drops `parsed`.
- **Phase 4 (future):** `CrossCuttingRules` absorbs the retained cross-domain validator checks + comparison checks; delete `ParsedArguments`, `RequestBuilder`, `CliValidator`, the three trimmed validators; relocate `RequestBuilder` statics; retarget `ProductionSetGenerator` off `ProductionModule` if the CLI→generator dependency is cleaned up; final "no double interpretation" cleanup.
