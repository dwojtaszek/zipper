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

`valid-deflate-stored-block` is hand-serialized from RFC 1951 stored blocks so `deflate-payload-bitflip` can change a literal byte without depending on runtime compressor output or breaking stream decoding. It remains a method-8 DEFLATE fixture and does not add a cross-runtime byte golden.

## Suites and case filtering (CLI)

```
zipper --archive-test-suite <smoke|compatibility|malformed|security|encoding|all>
       [--archive-test-cases <key,key,...>]
       --output-path <new-directory>
       [--seed <n>]
```

- `--archive-test-suite` selects a predefined suite (the catalogue explicitly owns membership; the frozen contract is #834):
  - `smoke` — exactly the five frozen Case Keys: `valid-empty`, `valid-stored`, `valid-deflate`, `crc-both-mismatch`, `missing-eocd` (healthy controls plus the two reader-hostile cases every consumer pipeline must survive).
  - `compatibility` — every valid control/compatibility case, including `valid-deflate-stored-block`, a method-8 DEFLATE stream serialized as stored blocks.
  - `malformed` — malformed atomic/combined cases, including `stored-payload-bitflip` (one payload byte in a Stored entry is flipped while its CRC-32, sizes, headers, and central directory declarations remain unchanged) and `deflate-payload-bitflip` (one literal byte in a stored-block DEFLATE stream is flipped while the stream remains decodable and the original CRC-32 and sizes stay declared), plus the pinned structure cases `unsupported-method`, `unsupported-method-deflate64`, `multidisk-eocd-declared`, `multidisk-central-entry-declared`, `mixed-methods-one-unsupported-member`, `declared-size-oversized`, the #1281 `declared-size-undersized` (both headers under-declare compressed and uncompressed sizes by 16 bytes while the payload remains intact), the #1282 `version-local-central-mismatch` (the local header declares version-needed 2.0 while the central header claims the Zip64 4.5 version the Archive does not use), `zip64-extra-without-sentinels`, the #1286 `zip64-eocd-offset-disagreement` (the regular EOCD keeps the true central-directory offset while its Zip64 counterpart points 16 bytes past it), the #1288 `extra-field-truncated` (the first central header declares 1,024 extra-field bytes where none exist, extending past physical EOF), the #870 parser differential `eocdr-ambiguity-comment`, the #1280 Zip64 detection differential `zip64-locator-ambiguity`, and the #1285 policy-sensitive `trailing-data-undeclared`.
  - `security` — policy-sensitive path, collision, and path-depth cases, the #870 parser differential `eocdr-ambiguity-comment`, the #1280 Zip64 detection differential `zip64-locator-ambiguity`, the #1285 `trailing-data-undeclared` parser-policy case, the #1286 Zip64 EOCD offset-source case (`zip64-eocd-offset-disagreement`), the #878 ADLS Gen2 segment-depth boundary pair (`path-adls-segments-boundary`, `path-adls-segments-exceeded`), the unsupported-feature cases (`unsupported-method`, `unsupported-method-deflate64`, `mixed-methods-one-unsupported-member`), the split-archive declaration cases (`multidisk-eocd-declared`, `multidisk-central-entry-declared`), the #1279 Zip64 header-vs-extra size source case (`zip64-extra-without-sentinels`), and the bounded resource cases (`high-ratio-bounded`, `nested-archives-depth-two`, `nested-archives-cumulative-budget-boundary`, `many-small-entries`, `declared-size-oversized`, `declared-size-undersized`, `zip-bomb-overlapping-deflate`, `bzip2-high-ratio-bounded`).
  - `encoding` — three legacy-codec 0x5C trail-byte controls (ticket #887: `encoding-cp932-trail-backslash`, `encoding-big5-trail-backslash`, `encoding-gbk-trail-backslash`) and the #1283 `encoding-local-central-codec-divergence` control. The trail-byte Archives have bit 11 clear and carry `0x5C` only as a CP932/Big5/GBK character's trail byte (`表` = `0x95 0x5C`, `許` = `0xB3 0x5C`, `乗` = `0x81 0x5C`). The #1283 Archive carries the same logical name (`§.txt`) as CP932 bytes (`81 98 2e 74 78 74`) in the local header with bit 11 clear and UTF-8 bytes (`c2 a7 2e 74 78 74`) in the central header with bit 11 set; equal byte lengths keep every following offset fixed. These controls remain outside the default compatibility suite because only a named codec consumer evaluates them. The verifier decodes the trail-byte names with CP932/Big5/GBK and asserts no spurious directory split after decoding; for #1283 it decodes each header with its own codec and requires both to render `§.txt`. Verifying the #1283 Case Key requires Python 3.11+ because the verifier uses `metadata_encoding="cp932"` for the local header, while bit 11 keeps the central name UTF-8.
  - `all` — the distinct union of every case.
- Generation order is ordinal Case Key. `--archive-test-cases` filters to named Case Keys; unknown, empty, duplicate, or out-of-suite keys fail validation.
- `--seed` accepts only non-negative integers and defaults to 42.
- `--output-path` must be a **new** directory inside the working directory. Only these flags may be combined; all generation flags are rejected (ADR-0008).
- Exit codes: `0` success, `1` validation or generation failure (no partial publication), `130` cancellation.

## Limits (v1 budgets)

| Budget | Value |
|---|---|
| Per-Archive physical size | ≤ 16 MiB |
| Per-Archive recursively expanded recipe content | ≤ 32 MiB |
| Entries per Archive | ≤ 1,000 |
| Entry path depth | ≤ 2 |
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

The **Expectation File** records, per operation (`list`, `read-entry`, `integrity-check`, `extract`), a named reader profile, allowed outcomes, allowed failure stages, and invariants. Every Expectation File declares all four operations. `unsupported` and `not-run` are **not** passing verifications; platform-inapplicable operation records are reported as `not-run` rather than claimed as successes. Valid coded controls (`valid-bzip2`, `valid-deflate64`, `valid-deflate64-long-match`, `valid-mixed-methods`, `bzip2-high-ratio-bounded`) carry two capability profiles: `full-codec` (e.g. 7-Zip) must list, decode, hash-check, integrity-check, and extract successfully, while `unsupported-reader` must cleanly report `unsupported-method-rejected` without producing unverified bytes. `valid-deflate64` is the subset control (a Deflate-subset stream labeled method 9, decodable by ordinary DEFLATE); `valid-deflate64-long-match` is the Deflate64-specific control (a pinned 7-Zip stream with a 38,000-byte match distance, undecodable by ordinary DEFLATE).

The policy-sensitive `zip64-extra-without-sentinels` Case Key (#1279) keeps a Zip64 extended-information extra field whose compressed and uncompressed sizes are 64 bytes above the payload length, while the local and central 32-bit size fields declare the real payload size and the central local-header-offset field declares its real offset. Its expectations allow readers to prefer the 32-bit headers, prefer the extra values, or reject the nonconformant structure, while requiring the declared-size and bounded-read invariants. Observed here: CPython `zipfile`, .NET, and 7-Zip 26.04 read the header-sized content; 7-Zip reports a `Headers Error` warning but its test succeeds.

The policy-sensitive `zip64-eocd-offset-disagreement` Case Key (#1286) keeps both EOCD records and their shared central-directory fields, with real non-sentinel values in the regular EOCD. It changes only the Zip64 EOCD's central-directory offset to 16 bytes past the real location. Its expectations allow readers to follow either offset or reject the disagreement, while requiring the metadata-source disagreement and proving the regular EOCD still finds the intact central directory. The 7-Zip Adapter Check verifies the entry content through the regular EOCD offset.

The malformed `extra-field-truncated` Case Key (#1288) starts from `valid-stored` and changes only entry 0's central-header extra-field length word from zero to `0x0400`. The Archive contains no central extra-field bytes there: the next central header begins immediately, and the declared 1,024-byte region extends past physical EOF. Its expectations allow reader variance while requiring the independent verifier to prove the overrun and bounded handling.

The policy-sensitive `zip64-locator-ambiguity` Case Key (#1280) is a non-Zip64 Archive with a 0xFFFF-byte central directory. Its final central-header comment carries fake Zip64 EOCD and locator records, with the fake locator exactly 20 bytes before the authentic EOCD; the central-directory span and EOCD comment length remain length-anchored. This exercises readers that mistake the 16-bit directory-size value for a Zip64 sentinel and resolve records from the comment.

The policy-sensitive `trailing-data-undeclared` Case Key (#1285) starts from the valid Stored control, then appends 16 ASCII bytes after the EOCD while leaving the EOCD comment-length field at zero. Its expectations allow readers to accept or reject the Archive, but the verifier locates the EOCD by the fixed suffix and EOCD lengths and proves that the central directory ends at the EOCD, leaving the appended bytes undeclared.

### Where each profile is actually exercised

Only one reader runs the expectation engine: the Python `zipfile` adapter inside `verify-fixtures.py`. A `full-codec` or `unsupported-reader` record is therefore enforced by *that* reader, against the operation outcomes it produced.

The 7-Zip adapter is a **separate E2E layer**, driven by each fixture's optional `adapterChecks` list rather than the Python expectation engine or a hard-coded Case Key list. Each **Adapter Check** names the adapter (`7zip`), action, and required outcome: `extract` verifies every file entry's content hash, `test` requires whole-Archive rejection without extraction, `extract-entries` verifies only explicitly named healthy siblings, and `presence` checks only that the Archive exists (the ZIP64 locator declaration is tolerated by 7-Zip). A fixture may declare more than one check, so mixed one-bad-member cases require both rejection and healthy-sibling extraction.

`allowedOutcomes` remains a **permissive set** of outcomes for a reader profile, not an instruction to 7-Zip. The `capability` field remains generator metadata. Adding a new 7-Zip check requires declaring it in the Case Key definition, but no E2E Case Key list edit; the shell and Windows E2E scripts both invoke `verify-7zip-adapter.py` against the generated sidecars.

Adding a new multi-profile expectation does not automatically gain 7-Zip coverage. Declare an Adapter Check in its Case Key definition to request and verify the corresponding action.

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
