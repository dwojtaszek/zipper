# Code Review Guidelines

Patterns in this repo that look like defects but are deliberate.
Check this list before filing a finding. If a finding matches one of
these, either skip it or file it with an explicit note that the
guideline was considered and why an exception applies.

## 1. Output-parity invariant (Critical Rule 6)

Any code that produces Load File / Audit File / Production Set output
must stay byte-for-byte identical to the legacy writers unless a golden
baseline harness proves parity after the change.

- Do NOT flag string.Replace / Substring / path-separator handling in
  LoadfileAuditWriter, ProductionManifestWriter, StandardRowComposer,
  DatComposer, OptComposer, ProductionSetGenerator output paths as
  "inefficient" without first confirming a parity harness exists.
- Whole-string Replace (vs extension-only slicing) is deliberate where
  commented — it preserves historical byte output.
- Filing such a finding: require the author to attach a golden-baseline
  diff before acting on it.

## 2. Required materialization (.ToList() on indexed paths)

ToList() is correct, not wasteful, when the result is:
  (a) stored in a field iterated once per record, or
  (b) passed to LoadFileRecordBuilder.Build, which indexes by position
      and reads .Count on IReadOnlyList<string>.

Lazy IEnumerable would re-run the Select or fail to index.

## 3. Deliberate-difference "duplication"

Implementations that look similar may have deliberate differences for specific formats. For example, `FormatDelimiter` in `LoadfileAuditWriter` vs `ProductionManifestWriter` differ on empty sentinel (`"none"` vs `""`), ascii range (`<32` vs `<32 && >126`), and char fallback (first char vs full string).

Different output formats shouldn't be merged if it adds parameters for a single-use case. YAGNI.

## 4. Trivial-to-abstract duplication

Minor shared logic (e.g., a few lines of shared constructor parse logic in `DateGenerator` vs `DateTimeColumnGenerator`) should not be abstracted if the main bodies differ significantly. A base class for a few lines is YAGNI.

## 5. Already-covered test gaps

Before flagging missing test coverage for specific exception paths (e.g., `NotImplementedException` or `ArgumentException`), ensure existing tests (like `GenerateContent_WithPptx_ShouldThrowNotImplementedException` or `ResolveSecurePath_WithEmbeddedNullCharacter_ReturnsNull`) don't already assert these paths.

## 6. Property-based tests (`src/Zipper.Tests/Properties/`)

See [docs/property-based-testing.md](property-based-testing.md) for the full guide. When reviewing `[Property]` tests:

- **Flag as defects (must fix):**
  - Calling `Math.Abs(int)` on an FsCheck-generated `int` without casting to `long` first (throws `OverflowException` when shrunk to `int.MinValue`).
  - Folding a seed through `Math.Abs((long)seed) % N` inside a `[Property]` body instead of drawing the bounded range in the arbitrary with `Gen.Choose`.
  - Copying production logic into a private test helper instead of invoking the real production type/seam.
  - Creating/deleting temporary directories per iteration inside a `[Property]` method instead of reusing a class-level `IDisposable` temp root.
  - Unconstrained primitive arbitraries where guard clauses discard most generated inputs.
- **Do NOT flag (deliberate patterns):**
  - `#pragma warning disable CA5394` in property tests that exercise `Distributions`, `ChaosSampler`, or `DataGenerator`: those production APIs require `System.Random` for deterministic seeded replay (`REQ-075`).
  - Returning `bool` or `FsCheck.Property` instead of `void` with `Assert.*`: both are idiomatic `FsCheck.Xunit` styles (`Property` with `.Label(...)` is preferred when counterexample diagnostics need context).

## 7. Post-generation validation & Chaos Mode contracts

- `ProductionSetOrchestrator.ValidateProductionSetAsync` invokes `ValidationOrchestrator.RunAfterGeneration` once per Rolling Production Set before ZIP creation; `PostGenerationValidator` runs `ProductionSetPostValidator.Validate` once, populates `context.ProductionSetReport`, and `ValidationOrchestrator` merges any post-generation findings into that report before throwing on errors. Do NOT suggest re-running `ProductionSetPostValidator.Validate` a second time or restoring `ProductionSetResult.EffectiveRequest`.
- In `StandardMode`, `LoadFileOrchestrator` writes `--include-load-file` entries at the ZIP root and always populates `result.LoadFilePath`, while `StandardMode` leaves `IsChaosMode = false` and sets `ValidateChaosExpectations = request.Chaos.ChaosMode` so normal Load File validation runs alongside Chaos Expectation Sidecar verification.
- In cross-format chaos Production Sets, `_validation_report.json` intentionally records the raw `ProductionSetPostValidator` structural findings while `PostGenerationValidator` exempts structural DAT/OPT findings (preserving artifact `PathExistence`, `ManifestSyntax`, `MetadataAzureConstraint`, and `InvalidValue`) and `ChaosExpectationValidator` verifies non-audited rows.

## 8. Automation & CI scripts (`.github/scripts/`, `.zipper-runner/`, `tests/*.sh`)

When reviewing Python or Bash automation scripts:

- **`urllib` exception handling:** Catch `http.client.HTTPException` alongside `OSError` / `urllib.error.URLError` — `HTTPException` (such as `IncompleteRead` or `RemoteDisconnected`) does not inherit from `OSError`.
- **Single-line directive regexes:** Use `[ \t]` rather than `\s` when matching parameters after a line-anchored directive (e.g., `qodo-ack:`) so optional whitespace cannot cross a newline into unrelated lines.
- **Dry-run isolation:** Verify every failure, hand-back, and fallback branch checks `dry_run` / `DRY_RUN` before mutating worktrees, locks, or GitHub state.
- **Fail-closed GraphQL pagination:** Require `isinstance(hasNextPage, bool)` and a strict cursor advance check so malformed or stalled pages cannot drop comments or pass silently.

