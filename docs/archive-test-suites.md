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

**Test vector** (frozen in `tests/fixtures/archive-tests/valid-empty.json`): the 22-byte empty Archive `504b0506000000000000000000000000000000000000` (EOCD only), SHA-256 `8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85`, Case Key `valid-empty`, all revisions 1, Seed 42 → Fixture ID `atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950`.

## Suites and case filtering (CLI)

```
zipper --archive-test-suite <smoke|compatibility|malformed|security|all>
       [--archive-test-cases <key,key,...>]
       --output-path <new-directory>
       [--seed <n>]
```

- `--archive-test-suite` selects a predefined suite: `smoke` (a few fast valid controls), `compatibility` (well-formed but structurally unusual), `malformed` (corrupt structures), `security` (path/link/collision policy), `all`.
- `--archive-test-cases` filters to named Case Keys; unknown keys fail validation.
- `--seed` defaults to 42.
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

Every fixture carries a `classification`: `valid` (well-formed), `malformed` (corrupt structure), or `policy-sensitive` (well-formed bytes whose extraction outcome depends on extractor policy — paths, links, collisions).

The **Expectation File** records, per operation (`list`, `read-entry`, `integrity-check`, `extract`), a named reader profile, allowed outcomes, allowed failure stages, and invariants. `unsupported` and `not-run` are **not** passing verifications; a profile that lacks an operation records that fact rather than claiming success.

## Expectation File schema

The machine-readable contract lives at `tests/fixtures/archive-test-case.schema.json` (JSON Schema draft-07, using `definitions`). Required fields: `schemaVersion`, `generatorContractVersion`, `generatorVersion`, `fixtureId`, `caseKey`, `caseRevision`, `expectationRevision`, `seed`, `classification`, `archive`, `entries`, `mutations`, `expectations`, `limits`.

Structural rules the schema and its semantic companion checks enforce:

- `entries` are keyed by `ordinal` and carry raw local/central name bytes (hex), a readable name, expected content hash/size when knowable, `kind` (`file`/`directory`), and structure offsets. Duplicate entry names must remain representable; duplicate `ordinal`s are rejected.
- `mutations` record code, structure, offset basis (`before-mutation` — all offsets refer to the pre-mutation Archive), offset, deleted/inserted lengths, before/after sizes and SHA-256 hashes, an explanation, and — for adversarial declared sizes — a string `declaredValue`. Inline `beforeHex`/`afterHex` is capped at 256 bytes (512 hex characters); larger removed ranges are referenced by hash instead.
- Semantic equalities a JSON Schema cannot express (filename ↔ Fixture ID, ordinal uniqueness) are verified by the test suite in `src/Zipper.Tests/ArchiveTests/`.

## Architecture

The Archive Test workflow is a dedicated Program short-circuit dispatched before `Pipeline.Build`, analogous to the comparison module. It is **not a fourth `IGenerationMode`** and does not extend `FileGenerationRequest` — see [ADR-0008](adr/ADR-0008-archive-test-dispatch.md). Production code lives under `src/ArchiveTests/`. Standard and Production Set output, `ZipArchiveSink`, `SourcePathSanitizer`, `ChaosEngine`, and the Composer → Serializer → Emitter seam are never altered to produce intentional defects: fixtures are built by their own byte-level writer.

## Independent verification

Published pairs are verified by `tests/archive-tests/verify-fixtures.py` (ticket #845), an implementation that shares no code with the generator: exact pair enumeration, authoritative draft-07 schema validation through the pinned Ajv CLI (`npx --yes ajv-cli@5.0.0` — a missing tool is a failure, never a silent skip), an independently recomputed canonical Fixture ID, a mutation-chain audit with control reconstruction from inline hex, ordinal-based entry reads with bounded streams, real extraction for safe valid controls only (extraction safety is derived independently — no mutations, safe entry names, expansion within budget — never from the declared classification), and no extraction for `malformed` or `policy-sensitive` fixtures (`extract` stays `not-run`). A JSON report is written outside the fixture directory; the exit status is nonzero for required failures, malformed oracles, timeouts, resource exhaustion, or missing prerequisites. The .NET reference reader's normalized operation results are covered by `src/Zipper.Tests/ArchiveTests/ArchiveReferenceReaderTests.cs`; tamper scenarios for the verifier itself live in `tests/archive-tests/test_verify_fixtures.py` (E2E Test 11b runs both).
