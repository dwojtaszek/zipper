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

**Byte goldens and replay:** the same seed and options into separate fresh directories publish byte-identical pairs — Archive bytes and Expectation File bytes both (E2E-asserted). Stored/header-only controls are byte-stable across OS hosts on the pinned .NET major (`global.json`), so their Fixture IDs are frozen and asserted by the E2E matrix on Linux, Windows, and macOS (`valid-stored` at Seed 42 → `atc-d4998f7846f15ca3c2d709a07579a0ba6723f5f7b524ca39953622407b90a24a`); a future .NET serialization change turns that check red by design. Deflate output has **no cross-runtime byte promise**: deflate controls are replay-checked for same-runtime identity only, and new hashes are never auto-accepted.

## Suites and case filtering (CLI)

```
zipper --archive-test-suite <smoke|compatibility|malformed|security|encoding|all>
       [--archive-test-cases <key,key,...>]
       --output-path <new-directory>
       [--seed <n>]
```

- `--archive-test-suite` selects a predefined suite (the catalogue explicitly owns membership; the frozen contract is #834):
  - `smoke` — exactly the five frozen Case Keys: `valid-empty`, `valid-stored`, `valid-deflate`, `crc-both-mismatch`, `missing-eocd` (healthy controls plus the two reader-hostile cases every consumer pipeline must survive).
  - `compatibility` — every valid control/compatibility case.
  - `malformed` — malformed atomic/combined cases (plus the pinned structure cases `unsupported-method`, `unsupported-method-deflate64`, `multidisk-eocd-declared`, `multidisk-central-entry-declared`, `mixed-methods-one-unsupported-member`, `declared-size-oversized`, and the #870 parser differential `eocdr-ambiguity-comment`).
  - `security` — policy-sensitive path, collision, and path-depth cases, the #870 parser differential `eocdr-ambiguity-comment`, the #878 ADLS Gen2 segment-depth boundary pair (`path-adls-segments-boundary`, `path-adls-segments-exceeded`), the unsupported-feature cases (`unsupported-method`, `unsupported-method-deflate64`, `mixed-methods-one-unsupported-member`), the split-archive declaration cases (`multidisk-eocd-declared`, `multidisk-central-entry-declared`), and the bounded resource cases (`high-ratio-bounded`, `nested-archives-depth-two`, `nested-archives-cumulative-budget-boundary`, `many-small-entries`, `declared-size-oversized`, `zip-bomb-overlapping-deflate`, `bzip2-high-ratio-bounded`).
  - `encoding` — the three legacy-codec 0x5C trail-byte controls (ticket #887: `encoding-cp932-trail-backslash`, `encoding-big5-trail-backslash`, `encoding-gbk-trail-backslash`). Valid single-entry Archives with bit 11 clear whose raw name bytes carry `0x5C` only as a CP932/Big5/GBK character's trail byte (`表` = `0x95 0x5C`, `許` = `0xB3 0x5C`, `乗` = `0x81 0x5C`). Deliberately outside the default compatibility suite per the #887 triage note: only a named codec consumer can evaluate the hazard, so the verifier's `encoding-trail-byte` check decodes the raw name bytes with the codec (Python stdlib `cp932`/`big5`/`gbk`) and asserts no spurious directory split **after** decoding, never on the raw bytes.
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

Every fixture carries a `classification`: `valid` (well-formed), `malformed` (corrupt structure), or `policy-sensitive` (well-formed bytes whose extraction outcome depends on extractor policy — paths, links, collisions, path depth, parser selection).

The **Expectation File** records, per operation (`list`, `read-entry`, `integrity-check`, `extract`), a named reader profile, allowed outcomes, allowed failure stages, and invariants. Every Expectation File declares all four operations. `unsupported` and `not-run` are **not** passing verifications; platform-inapplicable operation records are reported as `not-run` rather than claimed as successes. Valid coded controls (`valid-bzip2`, `valid-deflate64`, `valid-deflate64-long-match`, `valid-mixed-methods`, `bzip2-high-ratio-bounded`) carry two capability profiles: `full-codec` (e.g. 7-Zip) must list, decode, hash-check, integrity-check, and extract successfully, while `unsupported-reader` must cleanly report `unsupported-method-rejected` without producing unverified bytes. `valid-deflate64` is the subset control (a Deflate-subset stream labeled method 9, decodable by ordinary DEFLATE); `valid-deflate64-long-match` is the Deflate64-specific control (a pinned 7-Zip stream with a 38,000-byte match distance, undecodable by ordinary DEFLATE).

### Where each profile is actually exercised

Only one reader runs the expectation engine: the Python `zipfile` adapter inside `verify-fixtures.py`. A `full-codec` or `unsupported-reader` record is therefore enforced by *that* reader, against the operation outcomes it produced.

The 7-Zip adapter is a **separate E2E policy layer** with a hard-coded case list, and it is **not** driven by the expectation engine. This is worth stating precisely, because the two mechanisms are easy to conflate:

- `allowedOutcomes` is a **permissive set** — the set of outcomes a reader may legitimately produce — not a directive about what a given adapter should do. A `malformed` case commonly allows both `extract-fails` and `extract-succeeds`, so the record cannot say which one to assert.
- The Expectation File carries **no adapter directive**: nothing states "run 7-Zip in test mode" or "run it in extract mode" for a given case. The `capability` field that 16 fixtures carry is generator metadata and the verifier never reads it.
- Consequently the E2E list is per-case intent that no declared property reproduces. Measured against the 106-fixture catalogue: of the **18** case keys the 7-Zip block names, only **5** carry a `full-codec` expectation, and those 5 are exactly the coded controls named above. The other 13 — including `valid-zip64-descriptor-signature` and `valid-zip64-descriptor-no-signature`, which the block extracts — carry only a `strict` expectation. Deriving the list from entry codecs reproduces 5 of the 7 extract keys, a strict subset.

The practical consequence: **adding a new multi-profile expectation does not automatically gain 7-Zip coverage** — it needs a matching entry in the E2E adapter list, which is a hard-coded list. That gap is real and is tracked separately; it is not closed by the Expectation File as it stands, and this section does not claim otherwise. A declaration in the Expectation File (for example an explicit adapter/mode field) is the prerequisite for driving the adapter from data rather than from a list.

## Expectation File schema

The machine-readable contract lives at `tests/fixtures/archive-test-case.schema.json` (JSON Schema draft-07, using `definitions`). Required fields: `schemaVersion`, `generatorContractVersion`, `generatorVersion`, `fixtureId`, `caseKey`, `caseRevision`, `expectationRevision`, `seed`, `classification`, `archive`, `entries`, `mutations`, `expectations`, `limits`.

Structural rules the schema and its semantic companion checks enforce:

- `entries` are keyed by `ordinal` and carry raw local/central name bytes (hex), a readable name, name lengths in UTF-8 bytes / UTF-16 code units / Unicode scalars, expected content hash/size when knowable, `kind` (`file`/`directory`), and structure offsets. Duplicate entry names must remain representable; duplicate `ordinal`s are rejected.
- `mutations` record code, structure, offset basis (`before-mutation` — all offsets refer to the pre-mutation Archive), offset, deleted/inserted lengths, before/after sizes and SHA-256 hashes, an explanation, and — for adversarial declared sizes — a string `declaredValue`. Inline `beforeHex`/`afterHex` is capped at 256 bytes (512 hex characters); larger removed ranges are referenced by hash instead.
- Semantic equalities a JSON Schema cannot express (filename ↔ Fixture ID, ordinal uniqueness) are verified by the test suite in `src/Zipper.Tests/ArchiveTests/`.

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
