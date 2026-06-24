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

Before flagging missing test coverage for specific exception paths (e.g., `NotImplementedException` or `ArgumentException`), ensure existing tests (like `GenerateContent_WithPptx_ShouldThrowNotImplementedException` or `ValidateAndCreateDirectory_WithInvalidCharactersInFileName_HandlesGracefully`) don't already assert these paths.
