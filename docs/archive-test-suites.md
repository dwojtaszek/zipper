# Archive Test Suites

**Status: Shipped.** The contract is frozen and delivered; the implementation was tracked in [issue #834](https://github.com/dwojtaszek/zipper/issues/834) as twelve sequential slices. The CLI flags shipped in slice 10 (#844) — see [ADR-0008](adr/ADR-0008-archive-test-dispatch.md).

## Purpose

Zipper generates Archives; unzip implementations consume them. An **Archive Test Fixture** is a small, deliberately constructed ZIP Archive — valid, malformed, or **policy-sensitive** — published together with a machine-readable **Expectation File** so that extractor implementations can be tested against known bytes and known outcomes. Fixtures live in a flat output folder: one `atc-<64-hex>.zip` plus one `atc-<64-hex>.json` pair, with the same **Fixture ID** in both filenames and inside the JSON.

## Output contract

- Output is a **folder only**: flat `<fixtureId>.zip` + `<fixtureId>.json` pairs. No outer Archive, no per-case subfolders, no embedded expectation JSON, no suite manifest in v1.
- `fixtureId` is `atc-` plus 64 lowercase SHA-256 hex characters. It appears in both basenames and in the JSON `fixtureId` field; JSON `archive.fileName` is exactly `<fixtureId>.zip`.
- Malicious paths, unusual names, and adversarial declared sizes live **inside** the Archive bytes and the JSON only — never in output filenames.
- **Case Key** is the human-readable scenario name (e.g. `valid-empty`); it identifies the scenario, not the instance.

## Fixture ID identity

`fixtureId` = `atc-` + lowercase hex of SHA-256 over the canonical descriptor, a UTF-8, LF-terminated sequence with no trailing whitespace:

```
zipper-archive-test\n
1\n                          descriptor version
<generatorContractVersion>\n
<caseKey>\n
<caseRevision>\n
<expectationRevision>\n
<seed>\n
<archiveSha256>\n
```

where `<archiveSha256>` is the SHA-256 of the final Archive bytes.

**Determinism boundary:** the same Archive bytes, case, revisions, and Seed always produce the same Fixture ID. A different case, Seed, revision, or final byte changes it. The output directory and suite selection do not affect it. Duplicate Fixture IDs must be detected before any publication.

**Test vector** (frozen in `tests/fixtures/archive-tests/valid-empty.json`): the 22-byte empty Archive `504b0506000000000000000000000000000000000000` (EOCD only), SHA-256 `8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85`, Case Key `valid-empty`, Case Revision 1, Expectation Revision 2, Seed 42 → Fixture ID `atc-4d275f1fe266174e43c71d2cbe42084da23ce42369a17af7b84a9d165b6c489f`.

**Byte goldens and replay:** the same seed and options into separate fresh directories publish byte-identical pairs — Archive bytes and Expectation File bytes both (E2E-asserted). The fixture builder pins the two host-dependent central-directory fields (the version-made-by host byte and the default external attributes) to the Unix values the goldens were frozen with, on the standard writer path and in the embedded inner Archives alike, so no entry metadata, and therefore no byte, varies by build host; stored/header-only controls are byte-stable across OS hosts on the pinned .NET major (`global.json`), so their Fixture IDs are frozen and asserted by the E2E matrix on Linux, Windows, and macOS (`valid-stored` at Seed 42 → `atc-d4998f7846f15ca3c2d709a07579a0ba6723f5f7b524ca39953622407b90a24a`); a future .NET serialization change turns that check red by design. Deflate output has **no cross-runtime byte promise**: deflate controls are replay-checked for same-runtime identity only, and new hashes are never auto-accepted.

The runtime pin also determines the .NET reference reader's CRC contract. .NET 11 starts rejecting CRC-32 mismatches while reading ZIP entries; before .NET 11, `crc-*` mutations whose returned content still matches normalize to `crc-mismatch-unchecked`. Payload-flip cases such as `stored-payload-bitflip` and `deflate-payload-bitflip` are also affected, but currently normalize to `integrity-fails` because their returned bytes fail the content-hash check. On a .NET 11 upgrade, update `NormalizeIntegrityCheck` so CRC-related read rejections normalize to `crc-mismatch-rejected`; the outcome transition is `crc-mismatch-unchecked` → `crc-mismatch-rejected` for CRC-declaration mismatches, and `integrity-fails` → `crc-mismatch-rejected` for payload flips. Audit and update Expectation Revisions for all `crc-*` mutations and payload-flip cases before updating the runtime canary. The canary intentionally fails with this remediation path when a CRC-validating runtime is detected.

The malformed `crc-both-mismatch-deflate` Case Key (#1289) flips both CRC-32 header values on the `valid-deflate` control while leaving its payload and sizes unchanged. It stays in the `malformed` suite, outside the frozen `smoke` suite. Readers that validate CRC while inflating may fail during `read-entry`; readers that return the inflated bytes without checking CRC report `crc-mismatch-unchecked`.

`valid-deflate-stored-block` is hand-serialized from RFC 1951 stored blocks so `deflate-payload-bitflip` can change a literal byte without depending on runtime compressor output or breaking stream decoding. It remains a method-8 DEFLATE fixture and does not add a cross-runtime byte golden.

## Suites and case filtering (CLI)

```
zipper --archive-test-suite <smoke|compatibility|malformed|security|encoding|directory-workload|all>
       [--archive-test-cases <key,key,...>]
       --output-path <new-directory>
       [--seed <n>]
```

- `--archive-test-suite` selects a predefined suite (the catalogue explicitly owns membership; the frozen contract is #834):
  - `smoke` — exactly the five frozen Case Keys: `valid-empty`, `valid-stored`, `valid-deflate`, `crc-both-mismatch`, `missing-eocd` (healthy controls plus the two reader-hostile cases every consumer pipeline must survive).
  - `compatibility` — valid cases outside the opt-in workload, including `valid-deflate-stored-block`, a method-8 DEFLATE stream serialized as stored blocks.
  - `malformed` — malformed atomic/combined cases, including `stored-payload-bitflip` (one payload byte in a Stored entry is flipped while its CRC-32, sizes, headers, and central directory declarations remain unchanged) and `deflate-payload-bitflip` (one literal byte in a stored-block DEFLATE stream is flipped while the stream remains decodable and the original CRC-32 and sizes stay declared), plus the pinned structure cases `unsupported-method`, `unsupported-method-deflate64`, `multidisk-eocd-declared`, `multidisk-central-entry-declared`, `mixed-methods-one-unsupported-member`, `declared-size-oversized`, the #1281 `declared-size-undersized` (both headers under-declare compressed and uncompressed sizes by 16 bytes while the payload remains intact), the #1282 `version-local-central-mismatch` (the local header declares version-needed 2.0 while the central header claims the Zip64 4.5 version the Archive does not use), `zip64-extra-without-sentinels`, the #1286 `zip64-eocd-offset-disagreement` (the regular EOCD keeps the true central-directory offset while its Zip64 counterpart points 16 bytes past it), the #1288 `extra-field-truncated` (the first central header declares 1,024 extra-field bytes where none exist, extending past physical EOF), the #870 parser differential `eocdr-ambiguity-comment`, the #1280 Zip64 detection differential `zip64-locator-ambiguity`, and the #1285 policy-sensitive `trailing-data-undeclared`.
  - `security` — policy-sensitive path, collision, and path-depth cases, including the #1290 `name-sibling-prefix-traversal` hostile-name corpus; the #870 parser differential `eocdr-ambiguity-comment`, the #1280 Zip64 detection differential `zip64-locator-ambiguity`, the #1285 `trailing-data-undeclared` parser-policy case, the #1286 Zip64 EOCD offset-source case (`zip64-eocd-offset-disagreement`), the #878 ADLS Gen2 segment-depth boundary pair (`path-adls-segments-boundary`, `path-adls-segments-exceeded`), the unsupported-feature cases (`unsupported-method`, `unsupported-method-deflate64`, `mixed-methods-one-unsupported-member`), the split-archive declaration cases (`multidisk-eocd-declared`, `multidisk-central-entry-declared`), the #1279 Zip64 header-vs-extra size source case (`zip64-extra-without-sentinels`), and the bounded resource cases (`high-ratio-bounded`, `nested-archives-depth-two`, `nested-archives-cumulative-budget-boundary`, `many-small-entries`, `declared-size-oversized`, `declared-size-undersized`, `zip-bomb-overlapping-deflate`, `bzip2-high-ratio-bounded`).
  - `encoding` — three legacy-codec 0x5C trail-byte controls (ticket #887: `encoding-cp932-trail-backslash`, `encoding-big5-trail-backslash`, `encoding-gbk-trail-backslash`) and the #1283 `encoding-local-central-codec-divergence` control. The trail-byte Archives have bit 11 clear and carry `0x5C` only as a CP932/Big5/GBK character's trail byte (`表` = `0x95 0x5C`, `許` = `0xB3 0x5C`, `乗` = `0x81 0x5C`). The #1283 Archive carries the same logical name (`§.txt`) as CP932 bytes (`81 98 2e 74 78 74`) in the local header with bit 11 clear and UTF-8 bytes (`c2 a7 2e 74 78 74`) in the central header with bit 11 set; equal byte lengths keep every following offset fixed. These controls remain outside the default compatibility suite because only a named codec consumer evaluates them. The verifier decodes the trail-byte names with CP932/Big5/GBK and asserts no spurious directory split after decoding; for #1283 it decodes each header with its own codec and requires both to render `§.txt`. Verifying the #1283 Case Key requires Python 3.11+ because the verifier uses `metadata_encoding="cp932"` for the local header, while bit 11 keeps the central name UTF-8.
  - `directory-workload` — opt-in workload with 10,000 empty directory entries distributed across ten fixtures.
  - `all` — the distinct union of the non-opt-in suites.
- Generation order is ordinal Case Key. `--archive-test-cases` filters to named Case Keys; unknown, empty, duplicate, or out-of-suite keys fail validation.
- `--seed` accepts only non-negative integers and defaults to 42.
- `--output-path` must be a **new** directory inside the working directory. Only these flags may be combined; all generation flags are rejected (ADR-0008).
- Exit codes: `0` success, `1` validation or generation failure (no partial publication), `130` cancellation.

### Directory trees and name boundaries

```bash
zipper --archive-test-suite compatibility --archive-test-cases directory-tree-explicit,directory-tree-implicit,directory-tree-only,valid-name-length-boundaries --seed 42 --output-path ./directory-controls
zipper --archive-test-suite directory-workload --seed 42 --output-path ./empty-directory-workload
```

| Case Key | ZIP directory entries | Files and materialized directories |
| --- | --- | --- |
| `directory-tree-explicit` | `café/`, `café/文/`, and the empty branch through `café/空/a/b/`, with parents first | All five directories and `café/文/file.txt` |
| `directory-tree-implicit` | None | `café/文/file.txt` creates its two ancestors. The empty branch has no ZIP entry and produces no destination directory. |
| `directory-tree-only` | The same five directory entries as the explicit tree | All five directories, with no files |

The explicit and implicit cases carry identical bytes for the shared file. Directory entries have zero expanded size and no payload. The `directory-workload` suite publishes `empty-directories-01` through `empty-directories-10`, with 1,000 globally unique named directory entries per fixture. It emits the entries directly because Directory Templates enumerate files. Choose one shard with `--archive-test-cases` or omit that option to publish all ten.

The boundary measurements are for the whole ZIP member name. The byte and scalar totals include slash separators. Segment depth includes the filename, with the Archive root excluded.

| Case Key / ordinal | UTF-8 bytes | Unicode scalars | Segments |
| --- | ---: | ---: | ---: |
| `valid-name-length-boundaries` / 0 | 1,024 | 1,023 | 5 |
| `valid-name-length-boundaries` / 1 | 1,025 | 1,024 | 5 |
| `valid-name-length-boundaries` / 2 | 1,026 | 1,025 | 5 |
| `path-adls-segments-boundary` / 0 | 258 | 258 | 63 |
| `path-adls-segments-exceeded` / 0 | 262 | 262 | 64 |

The name-length fixture crosses 1,024/1,025 in both units using four 240-character ASCII directory components and a filename containing `é`. The reader matrix reports this fixture as a platform skip by default. To run all four reader operations, set `ZIPPER_ATC_PLATFORMS=long-path-filesystem` on a destination that supports all three paths, including the extraction-root prefix. The independent ZIP tests verify the Archive entries on every host. The ADLS pair reuses the container-relative paths. An ADLS profile accounts for the account and container separately when applying its own limits. Zipper tests probe their destination and report an explicit skip when it cannot preserve the paths, including for the reused `case-collision` and `unicode-normalization-collision` fixtures. ZIP verification checks both spellings and distinct per-entry content hashes even when a destination cannot store both.

## ZIP producer compatibility controls

- `valid-max-eocd-comment` pins a seekable CPython 3.14.10 Archive with one Stored member and an EOCD comment of exactly 65,535 bytes. The Android CTS recorded package installation rejection at this length. The independent APK verifier tests acceptance at the same length. This control isolates the ZIP comment from APK signing and installation policy. [CTS rejection record](https://android.googlesource.com/platform/cts/%2B/8a73803e7db19c75a436071e7ebd51a88784c2e3%5E2..8a73803e7db19c75a436071e7ebd51a88784c2e3/) · [APK verifier test](https://android.googlesource.com/platform/tools/apksig/%2B/2bbf5d704da8667803af95d8c8625ec98bd7cef4/src/test/java/com/android/apksig/ApkVerifierTest.java)
- `valid-directory-attributes` isolates the observed reader split with two empty, slashless entries. The DOS entry has FAT host 0 and attributes `0x00000010`. The Unix entry has Unix host 3 and attributes `0x41ed0000`. The Expectation File and the `dotnet-reference` and `python-zipfile` profiles treat both as files with the empty-content hash. The optional `7zip` check records both names as directories in its technical listing and checks that reading the selected entries yields empty content. Info-ZIP 6.00 also displays directory modes for both entries in its technical listing. The split was verified with CPython 3.14.10 and 7-Zip 26.06. [CPython directory classification](https://docs.python.org/3/library/zipfile.html#zipfile.ZipInfo.is_dir)
- `valid-extra-field` already carries the unknown local extra field `0x9999` with four data bytes. The existing signed and unsigned descriptor cases exercise both forms. At Seed 42, the pinned seekable control and `valid-descriptor-signature` have the same member name and payload SHA-256, while bit 3 and descriptor presence differ. The cases retain their original Seed derivation.

Regenerate the pinned Archive with CPython 3.14.10. Its Archive bytes are independent of the requested Seed, which still participates in the Fixture ID.

```python
from hashlib import sha256
from zipfile import ZipFile, ZipInfo

payload = b"".join(
    sha256(b"valid-descriptor-signature\n42\nd.txt\n" + i.to_bytes(4, "little")).digest()
    for i in range(4)
)[:100]
entry = ZipInfo("d.txt")
entry.create_system = 3
entry.external_attr = 0o600 << 16
with ZipFile("valid-max-eocd-comment.zip", "w") as archive:
    archive.writestr(entry, payload)
    archive.comment = b"X" * 65535
```

The full Archive SHA-256 is `375c216e6e14e50b5905b87b905e1c9a470f77dadcdf4b44281ef8bf650a0969`. The payload SHA-256 is `b4b497d4a5a17c953cdf31226c8d28e330ad08b4250140dadd16088b7ed4d00f`.

### Opaque nested ZIP deliveries

Generate two compatibility fixtures with the same payloads and opposite entry order.

```bash
zipper --archive-test-suite compatibility --archive-test-cases nested-opaque-forward,nested-opaque-reversed --seed 42 --output-path ./opaque-nested
```

Both outer Archives contain exactly `inner.zip` and `zip-looking.bin`. The first member in the forward fixture reuses the bounded `inner.zip` payload from `nested-archives-depth-two`. The second starts with the ZIP local-header signature and holds ordinary bytes. A one-level extraction should yield exactly those two files with the raw SHA-256 digests in the Expectation Files. Each payload hash is identical across both orderings.

For an e-discovery delivery with ZIP-based Office Native Files, use Standard mode.

```bash
zipper --types docx:1,xlsx:1 --count 2 --folders 1 --seed 42 --output-path ./opaque-office
```

The Standard Archive contains `folder_001/00000001.docx` and `folder_001/00000002.xlsx` as complete Office packages. A one-level extraction yields the two Native Files.

## Limits (v1 budgets)

| Budget | Value |
|---|---|
| Per-Archive physical size | ≤ 16 MiB |
| Per-Archive recursively expanded recipe content | ≤ 32 MiB |
| Entries per Archive | ≤ 1,000 |
| Entry path depth | ≤ 2 for ordinary paths; bounded deep paths explicitly under test allow up to 64 segments, and policy-sensitive names can bypass the ordinary path guard and are excluded from ordinary extraction |
| Expectation File JSON | ≤ 1 MiB |
| Total output folder | ≤ 256 MiB |
| Per-fixture deadline | ≤ 10 s |

Budgets are enforced with checked arithmetic **during writes**, not only afterward, and one fixture artifact is processed at a time. Adversarial *declared* sizes (which may be far larger than physical bytes) are recorded in the Expectation File as decimal strings or raw hex, never as trusted JSON integers; expansion budgets are declared independently of them.

## Publication safety

- An existing output directory is rejected; nothing inside it is read, written, or deleted.
- Fixtures are staged in an owned sibling directory of the target, published with exclusive-create file pairs, then atomically renamed into place.
- Only the owned staging directory is cleaned up on failure. User files are never overwritten.

## Classifications and expectations

Every fixture carries a `classification`: `valid` (well-formed), `malformed` (corrupt structure), or `policy-sensitive` (reader or extractor behavior depends on implementation policy or support — for example, extraction safety, unsupported methods, or nonconformant structures with parser/source-selection variance).

The **Expectation File** records, per operation (`list`, `read-entry`, `integrity-check`, `extract`), a named reader profile, allowed outcomes, allowed failure stages, and invariants. Every Expectation File declares all four operations. `unsupported` and `not-run` are **not** passing verifications; platform-inapplicable operation records are reported as `not-run` rather than claimed as successes. If no Reader Profile expectation applies on the host, the Archive Test Fixture is reported as `skipped`, not passed. Valid coded controls (`valid-bzip2`, `valid-deflate64`, `valid-deflate64-long-match`, `valid-mixed-methods`, `bzip2-high-ratio-bounded`) carry two capability profiles: `full-codec` (e.g. 7-Zip) must list, decode, hash-check, integrity-check, and extract successfully, while `unsupported-reader` must cleanly report `unsupported-method-rejected` without producing unverified bytes. `valid-deflate64` is the subset control (a Deflate-subset stream labeled method 9, decodable by ordinary DEFLATE); `valid-deflate64-long-match` is the Deflate64-specific control (a pinned 7-Zip stream with a 38,000-byte match distance, undecodable by ordinary DEFLATE).

The policy-sensitive `zip64-extra-without-sentinels` Case Key (#1279) keeps a Zip64 extended-information extra field whose compressed and uncompressed sizes are 64 bytes above the payload length, while the local and central 32-bit size fields declare the real payload size and the central local-header-offset field declares its real offset. Its expectations allow readers to prefer the 32-bit headers, prefer the extra values, or reject the nonconformant structure, while requiring the declared-size and bounded-read invariants. Observed here: CPython `zipfile`, .NET, and 7-Zip 26.04 read the header-sized content; 7-Zip reports a `Headers Error` warning but its test succeeds.

The policy-sensitive `zip64-eocd-offset-disagreement` Case Key (#1286) keeps both EOCD records and their shared central-directory fields, with real non-sentinel values in the regular EOCD. It changes only the Zip64 EOCD's central-directory offset to 16 bytes past the real location. Its expectations allow readers to follow either offset or reject the disagreement, while requiring the metadata-source disagreement and proving the regular EOCD still finds the intact central directory. The 7-Zip Adapter Check verifies the entry content through the regular EOCD offset.

The malformed `extra-field-truncated` Case Key (#1288) starts from `valid-stored` and changes only entry 0's central-header extra-field length word from zero to `0x0400`. The Archive contains no central extra-field bytes there: the next central header begins immediately, and the declared 1,024-byte region extends past physical EOF. Its expectations allow reader variance while requiring the independent verifier to prove the overrun and bounded handling.

The policy-sensitive `zip64-locator-ambiguity` Case Key (#1280) is a non-Zip64 Archive with a 0xFFFF-byte central directory. Its final central-header comment carries fake Zip64 EOCD and locator records, with the fake locator exactly 20 bytes before the authentic EOCD; the central-directory span and EOCD comment length remain length-anchored. This exercises readers that mistake the 16-bit directory-size value for a Zip64 sentinel and resolve records from the comment.

The policy-sensitive `trailing-data-undeclared` Case Key (#1285) starts from the valid Stored control, then appends 16 ASCII bytes after the EOCD while leaving the EOCD comment-length field at zero. Its expectations allow readers to accept or reject the Archive, but the verifier locates the EOCD by the fixed suffix and EOCD lengths and proves that the central directory ends at the EOCD, leaving the appended bytes undeclared.

The policy-sensitive `name-sibling-prefix-traversal` Case Key (#1290) replaces two same-length placeholder names in both ZIP headers with `x/../../docs../e.txt` and `..%2f..%2fescape.txt`. The first name resolves outside an extraction root named `docs` to `docs../e.txt`, which shares the root's string prefix; the second carries percent-encoded separators and traversal segments. The Archive structure stays valid, and the Expectation File records the exact UTF-8 name bytes. The Python verifier and .NET reference reader pin the decoded names to those bytes. Its `containment-required` and `no-writes-outside-root` invariants are reader-side extraction oracles for a separate reader-specific extraction test, not evidence that ordinary verification observed containment. Ordinary verification leaves extraction `not-run`; any claim that a reader's extraction is contained requires a test that exercises that reader's extraction.

### Reader Adapter Profile matrix

The Reader Adapter Profile registry in `ArchiveTestCatalog` names the required .NET reference reader and Python `zipfile`, plus optional Info-ZIP `unzip` and 7-Zip. The E2E matrix collects per-fixture .NET results from `ArchiveReferenceReaderTests`, consumes the independent Python verifier report, and runs Info-ZIP's list, read-entry, and integrity-check operations when `unzip` is on `PATH`. The .NET and Python reference readers do not extract malformed or policy-sensitive fixtures, so their `extract` Adapter Checks are valid only for fixtures classified as `valid`. Separately declared 7-Zip `extract` checks may stream selected member content to standard output for malformed or policy-sensitive fixtures, but never extract the full Archive to disk. Info-ZIP's line-delimited listing is not compared for Archive members whose names contain line separators; the matrix reports that operation as `not-run` with a reason. Info-ZIP `read-entry` is also `not-run` for encoding Case Keys, unsafe or duplicate member names, incomplete content expectations, or declared member sizes that exceed the fixture's byte budget. Missing optional command-line readers are reported as skips with a reason, not failures.

For every fixture, `verify-reader-matrix.py` reports each reader's normalized outcomes and whether the readers agree or split per operation. It verifies the current Archive bytes against the sidecar's size, SHA-256, and canonical Fixture ID, then binds every required reader report to the current Case Key, Expectation File hash, and Archive hash. The .NET and Python reference readers remain gates against the existing allowed outcomes; Info-ZIP outcomes are comparative observations, with an `expectationStatus` in the report to show whether each observation fits those outcomes. This preserves the current Expectation Files while surfacing new reader differences. For coded controls, outcomes are evaluated against the matching `full-codec` or `unsupported-reader` Reader Profile. Splits and ordinary out-of-oracle Info-ZIP observations are evidence, not matrix failures. A completed, explicitly declared Info-ZIP Adapter Check must match its expected outcome; a mismatch fails that check and the matrix. Declared Adapter Checks for .NET and Python run independently of whether a platform-scoped Reader Profile expectation applies. Unavailable optional profiles record checks as skipped with a reason. Execution errors and timeouts use status `execution-error`, do not enter reader agreement, and are counted in `readerExecutionErrors`; they do not gate matrix success, including when an Info-ZIP execution error or timeout prevents a declared check from producing an outcome. Info-ZIP's `deadlineSeconds` limit is shared across its list, read-entry, and integrity-check operations for each fixture. Skipped and failed implementation adapters are counted in `readerAdapterProfileSkips` and `failedReaderAdapterProfiles`, while platform-inapplicable fixtures are counted in `fixturesSkipped`. .NET/Python results and declared 7-Zip Adapter Checks remain gates. The matrix writes a JSON report beside the fixture directory and prints one agreement summary line per fixture.

The 7-Zip adapter remains a **separate E2E layer**, driven by each fixture's optional `adapterChecks` list rather than a hard-coded Case Key list. Each **Adapter Check** names a Reader Adapter Profile, action, and required normalized outcome. The .NET reference reader and Python `zipfile` support `list`, `read-entry`, `integrity-check`, and `extract`; Info-ZIP supports `list`, `read-entry`, and `integrity-check`; 7-Zip retains `extract`/`success`, `test`/`failure`, `extract-entries`/`success` with explicit `entryNames`, and `presence`/`present`. A fixture may declare more than one check. The 7-Zip runner shares each fixture's declared `deadlineSeconds` across all of its Adapter Checks and records timeout failures in the check report. The matrix evaluates checks for .NET, Python, and Info-ZIP against their per-fixture results; the 7-Zip runner executes only 7-Zip checks and publishes its report for the matrix. Missing optional readers skip their checks with a reason.

`allowedOutcomes` remains a **permissive set** for each Reader Profile expectation; an Adapter Check requires the named Reader Adapter Profile to produce its specific expected outcome. The `capability` field remains generator metadata. Adapter Check actions and outcomes are validated against the named Reader Adapter Profile's registry entry. Adding a check requires declaring it in the Case Key definition, but no E2E Case Key list edit; both shell and Windows E2E scripts invoke the reader matrix and the 7-Zip runner against the generated sidecars.

Adding a new multi-profile expectation does not automatically gain a 7-Zip Adapter Check. Declare the check in its Case Key definition to request and verify the corresponding action.

## Expectation File schema

The machine-readable contract lives at `tests/fixtures/archive-test-case.schema.json` (JSON Schema draft-07, using `definitions`). Required fields: `schemaVersion`, `generatorContractVersion`, `generatorVersion`, `fixtureId`, `caseKey`, `caseRevision`, `expectationRevision`, `seed`, `classification`, `archive`, `entries`, `mutations`, `expectations`, `limits`. The optional `adapterChecks` list declares external reader checks for selected Case Keys.

Structural rules the schema and its semantic companion checks enforce:

- `entries` are keyed by `ordinal` and carry raw local/central name bytes (hex), a readable name, name lengths in UTF-8 bytes / UTF-16 code units / Unicode scalars, wire compression method codes (`localHeaderMethod` and `centralDirectoryMethod` as uint16 `0..65535`), generator knowledge (`payloadCodec` enum `stored`, `deflate`, `deflate64`, `bzip2`, `unknown` for files; omitted for directories), expected content hash/size when knowable, `kind` (`file`/`directory`), and structure offsets. Duplicate entry names must remain representable; duplicate `ordinal`s are rejected.
- `mutations` record code, structure, offset basis (`before-mutation` — all offsets refer to the pre-mutation Archive), offset, deleted/inserted lengths, before/after sizes and SHA-256 hashes, an explanation, and — for adversarial declared sizes — a string `declaredValue`. Inline `beforeHex`/`afterHex` is capped at 256 bytes (512 hex characters); larger removed ranges are referenced by hash instead.
- Semantic equalities a JSON Schema cannot express (filename ↔ Fixture ID, ordinal uniqueness) are verified by the test suite in `src/Zipper.Tests/ArchiveTests/`.

### Entry codec fields

The entry-level compression method and codec fields defined in `tests/fixtures/archive-test-case.schema.json` (per REQ-217):

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `localHeaderMethod` | integer (`0..65535`) | Always | Compression method code declared in the local file header (APPNOTE §4.4.5). |
| `centralDirectoryMethod` | integer (`0..65535`) | Always | Compression method code declared in the central directory header (APPNOTE §4.4.5). |
| `payloadCodec` | string (enum) | Files only | Generator-known payload compression codec: `stored`, `deflate`, `deflate64`, `bzip2`, or `unknown`. Required for `kind: "file"`, omitted for directories (`kind: "directory"`). |

```json
{
  "ordinal": 0,
  "kind": "file",
  "localNameRaw": "68656c6c6f2e747874",
  "centralNameRaw": "68656c6c6f2e747874",
  "readableName": "hello.txt",
  "localHeaderMethod": 8,
  "centralDirectoryMethod": 8,
  "payloadCodec": "deflate",
  "contentSha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
  "contentSize": 5,
  "nameLengthUtf8Bytes": 9,
  "nameLengthUtf16Units": 9,
  "nameLengthScalars": 9,
  "localHeaderOffset": 0,
  "dataOffset": 39,
  "centralDirectoryOffset": 53
}
```

## Catalog governance

The Archive Test catalog (`ArchiveTestCatalog`) and the Chaos Engine Anomaly Type catalog (`ChaosAnomalyTypes`) grow by method, not ad hoc. Two rules apply to every proposal that adds a Case Key or an Anomaly Type.

### Fault-mining rule

Every new Anomaly Type or Case Key must cite a documented real-world failure in its issue: a platform import error, a CVE, or a parser or reader issue report. The citation names the affected implementation and links the source. A defect that is only plausible, with no observed failure behind it, does not enter the catalog. The catalog is a taxonomy of faults mined from real failures, following [TeXFix-Bench](https://arxiv.org/abs/2603.00311) (a 168-fault taxonomy mined from real failures) and the PDF Association's [UNSAFE-DOCS](https://pdfa.org/unsafe-docs-a-new-safedocs-corpus-including-the-voice-of-the-offense/) corpus ([pdf-association/pdf-corpora](https://github.com/pdf-association/pdf-corpora)). Recent catalog growth (#1292, #1297, #1300) already follows this rule.

### Regression-seed rule

Every fixed real-world parse or import defect becomes a pinned Case Key or Anomaly Type with a test, never just a code fix. Each fixed defect gets a minimized regression seed: the smallest Archive Test Fixture or Chaos Engine input that still reproduces the failure, pinned by Case Key (or Anomaly Type) and Seed so it replays byte-for-byte. The precedent is MimeKit's [FuzzingPlan](https://github.com/jstedfast/mimekit/blob/72ab0e2018ef483f102248c926715e9eeac81e21/FuzzingPlan.md), which keeps "a minimized regression seed per fixed bug" as a standing corpus policy.

### Case Key naming

A Case Key is lowercase ASCII kebab-case and names the structure under test and the defect (for example `missing-eocd`, `zip64-eocd-offset-disagreement`, `encoding-cp932-trail-backslash`). Healthy controls start with `valid-`. A variant of an existing Case Key keeps its base name and appends a suffix (`crc-both-mismatch` → `crc-both-mismatch-deflate`). A Case Key names the scenario, never the source issue or the instance. Because the Case Key is part of the canonical descriptor, renaming it changes every Fixture ID it produces: a behavior change bumps the Case Revision or Expectation Revision instead.

### Case Key or Anomaly Type

- Prefer a **Case Key** when the fault lives in ZIP Archive bytes (headers, central directory, payload, entry names) and is consumed by unzip implementations. Case Keys are built by the Archive Test workflow's own byte-level writer and carry an Expectation File.
- Prefer an **Anomaly Type** when the fault lives in a Load File or a generated Email and is consumed by an ingestion platform. Anomaly Types are injected by the Chaos Engine and recorded in the Audit File.
- A fault that spans both surfaces gets one entry per surface, each citing the same real-world failure.

### Issue template

Paste this into the issue body when proposing catalog growth:

```markdown
## Real-world evidence
- Failure: <platform import error, CVE, or parser issue report>
- Source: <link>
- Affected implementation(s): <reader, parser, or platform and version>

## Catalog entry
- [ ] Case Key or Anomaly Type name: `<name>` (see Catalog governance in docs/archive-test-suites.md)
- [ ] Minimized regression seed pinned by Case Key or Anomaly Type and Seed
- [ ] Test that replays the seed and asserts the expected outcome
```

## Architecture

The Archive Test workflow is a dedicated Program short-circuit dispatched before `Pipeline.Build`, analogous to the comparison module. It is **not a fourth `IGenerationMode`** and does not extend `FileGenerationRequest` — see [ADR-0008](adr/ADR-0008-archive-test-dispatch.md). Production code lives under `src/ArchiveTests/`. Standard and Production Set output, `ZipArchiveSink`, `SourcePathSanitizer`, `ChaosEngine`, and the Composer → Serializer → Emitter seam are never altered to produce intentional defects: fixtures are built by their own byte-level writer.

## Consuming fixtures safely

The Expectation File is **external** to the Archive on purpose: expectations never ride inside the bytes under test, an unopenable or truncated Archive still leaves a fully readable standalone JSON contract, and no fixture ever embeds a second copy of itself. Safe consumer workflow:

1. Read the `.json` first — never the Archive. The JSON names the classification, the operations, and the allowed outcomes before any reader touches hostile bytes.
2. Treat `unsupported` and `not-run` as facts, not passes. `not-run` extraction on `malformed` and `policy-sensitive` fixtures is the honest record that unsafe extraction was not executed on the host.
3. Extract only what the JSON independently justifies: valid controls with safe names and in-budget expansion. Path/link/collision fixtures and every `malformed` fixture stay unextracted on normal hosts; exercising unsafe extractor behavior requires an isolated VM/container with explicit opt-in (out of scope for v1).
4. Reproduce a fixture by Case Key + revisions + Seed with the independent verifier (`tests/archive-tests/verify-fixtures.py`), which recomputes the canonical Fixture ID and audits mutations without sharing code with the generator.


## Independent verification

Published pairs are verified by `tests/archive-tests/verify-fixtures.py` (ticket #845), an implementation that shares no code with the generator: exact pair enumeration, authoritative draft-07 schema validation through the pinned Ajv CLI (a missing tool is a failure, never a silent skip), an independently recomputed canonical Fixture ID, a mutation-chain audit with control reconstruction from inline hex, ordinal-based entry reads with bounded streams, real extraction for safe valid controls only (extraction safety is derived independently — no mutations, safe entry names, expansion within budget — never from the declared classification), and no extraction for `malformed` or `policy-sensitive` fixtures (`extract` stays `not-run`). An Expectation File missing any operation record fails verification. Every file entry must declare a `contentSha256`, and the requirement is bound to the Archive rather than to the sidecar alone: the schema makes `contentSha256` optional, and `kind` is a sidecar claim, so a publisher could otherwise label a real file member as a `directory` (legitimately hash-free), move the hash-bearing `file` record onto a directory member, and have `extract` write the real content while reporting `extract-succeeds` and `read-entry` report `content-matches` having read nothing. For every Archive member that is not a directory and that the sidecar describes, the record at that ordinal must be `kind: "file"` with a declared hash. Count and ordinal coverage remain the `list` operation's comparison of the reader's own member count against the declared `entryCount`, so an undescribed member is caught there rather than pre-empting `list`. A directory entry legitimately carries no content and is unaffected, and an Archive that cannot be opened at all is left to the operations, which already report the honest rejection for it. The .NET reference reader in `ArchiveReferenceReaderTests.cs` keeps the permissive skip and is safe on generated fixtures, which always carry hashes. Every declared invariant is asserted against the observed evidence (an unsatisfied or unverifiable invariant and an unrecognized invariant token fail verification). An accepted staged failure — the operation's failure token or a codec-rejection outcome — requires a non-empty declared failure-stage set containing the observed stage; an omitted or mismatched stage fails verification. A JSON report is written outside the fixture directory; the exit status is nonzero for required failures, malformed oracles, timeouts, resource exhaustion, or missing prerequisites. The .NET reference reader's normalized operation results are covered by `src/Zipper.Tests/ArchiveTests/ArchiveReferenceReaderTests.cs`; tamper scenarios for the verifier itself live in `tests/archive-tests/test_verify_fixtures.py` (E2E Test 11b runs both).

### Verifier prerequisites (offline-first)

Schema validation runs the Ajv CLI pinned in `tests/archive-tests/package.json` (`ajv-cli` 5.0.0, with `ajv` 8.20.0 transitively pinned by `package-lock.json` integrity hashes) via the local `node` runtime — never `npx`, never the network:

1. Restore once: `npm ci --ignore-scripts --prefix tests/archive-tests` (the only step that may use the network; scripts stay off for supply-chain safety).
2. Verify offline any time after: `python3 tests/archive-tests/verify-fixtures.py <pairs> --report <report>.json`, including with an empty npm cache and an unreachable registry (E2E Test 14 proves the full catalogue verifies that way).
3. A missing restore fails closed naming the exact `npm ci` step; nothing is downloaded implicitly. Node.js itself must be on `PATH` (it ships everywhere `npx` does).
