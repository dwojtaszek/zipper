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

Lazy `IEnumerable` would re-run the Select or fail to index.

## 3. Deliberate-difference "duplication"

Do not flag duplicated code that differs in subtle formatting rules. For example, `FormatDelimiter` in `LoadfileAuditWriter` vs `ProductionManifestWriter`:
The two implementations differ on empty sentinel (`"none"` vs `""`), ascii range (`<32` vs `<32 && >126`), and char fallback (first char vs full string). Different JSON output formats — merging adds parameters for a single-use case. YAGNI.

## 4. Trivial-to-abstract duplication

Do not flag trivial shared logic between two classes as needing a base class.
For example, `DateGenerator` vs `DateTimeColumnGenerator`: ~4 lines of shared constructor parse logic; `Generate` bodies differ (days-only vs days+hours+minutes). A base class for 4 lines is YAGNI.

## 5. Already-covered test gaps

Before flagging missing tests for edge cases, check if existing tests already assert those paths.
For example, `PPTX` `NotImplementedException`, `PathValidator` `ArgumentException`. Existing tests (`GenerateContent_WithPptx_ShouldThrowNotImplementedException`, `WithInvalidCharactersInFileName`) already assert these paths.
