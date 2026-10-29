
### 2026-06-09 claude-code:branch:feat/ISSUE-422-chore-escalate-analysismode-enable-singl

- **findings**: 0 ACTION, 12 INFO (across 4 specialists). Key: multi-source confirmed (testing+adversarial+correctness) — Condition assertion too loose (needed IsTargetFrameworkCompatible check). Others: filename literal constant (maintainability), GetPropertyElement helper refactor (maintainability), Assert.Single instead of FirstOrDefault (testing), AnalysisLevel+AnalysisMode redundancy (correctness, rejected per issue spec).
- **outcome**: all AUTO-FIX items applied (4 fixes); 1 INFO finding rejected (AnalysisLevel+AnalysisMode collapse — not prescribed by issue). PR created: #477.
- **telemetry**: host=claude-code mode=branch specialists=4 bundle=~2500c
- **lessons**: For build-config-only diffs, all specialists return INFO at most. The Condition-assertion-too-loose finding was the only multi-source finding and most valuable — caught by testing (conf 8), adversarial (conf 8), and correctness (conf 8).
- **suppressions**: none
### 2026-06-11 claude-code:branch:feat/ISSUE-427-spike-evaluate-invariantglobalization-fo

- **findings**: 2 INFO
- **outcome**: accepted (0 action needed, coverage confirmed elsewhere)
- **telemetry**: host=claude-code mode=branch specialists=1 bundle=294c
- **lessons**: none
- **suppressions**: none

### 2026-06-15 claude-code:branch:issue-470

- **findings**: 9 (2 ACTION, 7 INFO) - False positive missing required parameters, explicitly requested coupling.
- **outcome**: rejected all ACTION findings (false positives from diff tool / explicit requirements).
- **telemetry**: host=claude-code mode=branch specialists=7 bundle=50K
- **lessons**: Subagents misread the diff context when a method wrapper is removed but its contents are preserved inline, leading to multiple false positives for "deleted code".
- **suppressions**: Suppress: "Missing required parameter checks" when the checks were merely moved inline.

### 2026-06-16 antigravity:branch:issue-488

- **findings**: 6 (0 ACTION, 6 INFO) - 3 coverage gaps (Concordance tests, opt/csv fallbacks), 2 stale comments, 1 structural/abstraction warning.
- **outcome**: partial - accepted 5 INFO findings (added 5 tests, updated 2 stale comments). Rejected the structural warning about "unnecessary object abstraction" because consolidating the logic into a central policy was the explicitly documented purpose of the spike (Issue #488).
- **telemetry**: host=claude-code mode=branch specialists=5 bundle=242c
- **lessons**: The correctness specialist will naturally complain about abstraction boundaries (object allocation instead of inline static method) when code is extracted from a focused writer into a broad policy class. Project-specific spike goals override these generic structural preferences.
- **suppressions**: none
### 2026-06-23 antigravity:local:issue-525

- **findings**: 0
- **outcome**: accepted / clean review
- **telemetry**: host=antigravity mode=local specialists=0 bundle=1200c
- **lessons**: none
- **suppressions**: none

### 2026-06-28 antigravity:local:issue-546

- **findings**: 0
- **outcome**: accepted / clean review
- **telemetry**: host=antigravity mode=local specialists=1 bundle=890c
- **lessons**: none
- **suppressions**: none

### 2026-06-29 antigravity:branch:issue-535

- **findings**: 2 ACTION, 7 INFO (across 4 specialists). Empty formats list index out of range checks, test clone isolation, stale analyzer check.
- **outcome**: accepted all findings (fixed RequestBuilder fallback, added defensive checks to index-0 access sites, corrected clone test isolation, and updated FgrFlatAccessAnalyzer to guard Formats). All 1155 tests passed.
- **telemetry**: host=antigravity mode=branch specialists=4 bundle=21736c
- **lessons**: The clone isolation test was using re-assignment which masked reference equality bugs. Asserting NotSame immediately after cloning ensures list isolation is verified properly.
- **suppressions**: none

### 2026-07-01 antigravity:branch:issue-557

- **findings**: 0
- **outcome**: accepted / clean review
- **telemetry**: host=antigravity mode=branch specialists=1 bundle=1800c
- **lessons**: none
- **suppressions**: none

### 2026-07-01 antigravity:branch:issue-561

- **findings**: 1 INFO (unused chaosEngine parameters in ZipArchiveSink helper methods)
- **outcome**: accepted (cleaned up unused parameters from the four helper methods in ZipArchiveSink.cs).
- **telemetry**: host=antigravity mode=branch specialists=1 bundle=2800c
- **lessons**: none
- **suppressions**: none

### 2026-07-09 antigravity:branch:issue-572

- **findings**: 0
- **outcome**: accepted / clean review
- **telemetry**: host=antigravity mode=branch specialists=1 bundle=1500c
- **lessons**: Mentally verified correctness; confirmed formatting and build are fully clean with 0 warnings.
- **suppressions**: none


### 2026-08-15 opencode:branch:phase4-750

- **findings**: 12 (0 ACTION, 12 INFO across correctness/adversarial/security/performance/testing/maintainability; security+performance clean)
- **outcome**: partial — fixed 4 (test-coverage gap on new Parse value-taking flags, 2 sibling null-guard asserts, dead PipelineTestHelper overload+param, Requirements.md stray space); rejected 8 as intentional (D3 null-guard drop, provenance comments the plan keeps, direct-API companion-flag message shift folded into accepted-divergence PR note, req-traceability.tsv pre-existing debt, ArgumentHelpers public-on-internal cosmetic)
- **telemetry**: host=opencode mode=branch specialists=6 bundle=4824c
- **lessons**: new Parse-boundary test arrays drift when new value-taking flags land — the review caught 4 missing; subagents reliably cross-check deleted-type grep gates.
- **suppressions**: none

### 2026-08-15 opencode:branch:dwojtaszek/deepen-source-record-intake-consolidate-path-typ-2

- **findings**: 10 (0 ACTION, 10 INFO across 6 specialists; security clean). Multi-source confirmed (correctness+adversarial): 2 directory error-message drifts — 'file'→'entry' + normalized→raw path in extension/no-extension messages, and rewritten duplicate-path message (I claimed "all other messages verbatim", review proved otherwise). Others: 3 testing gaps (empty FileType, no-extension branch, char.IsControl identity), 2 additive coverage (metadata passthrough, case-insensitive dup), 2 performance (sort copy, unpre-sized list), maintainability (_seenPaths mutation ordering, double cap).
- **outcome**: fixed 6 (both message drifts restored verbatim — branch on `fileTypeText is null`; 5 direct-intake tests added), documented double-cap in architecture.md; accepted 4 (state-mutation ordering is pre-existing + callers fail-fast, sort copy is adapter policy, unpre-sized list amortized O(1)). All 1831 unit + 44 analyzer tests + source-driven E2E green, format clean.
- **telemetry**: host=opencode mode=branch specialists=6 bundle=4824c
- **lessons**: when a refactor claims byte-identical messages, verify by diffing every message literal against the old file — two slipped through until cross-model review caught them; the deep-module seam made the directory-vs-CSV message branch a one-line `fileTypeText is null` ternary, no adapter complexity returned.
- **suppressions**: none

### 2026-09-12 antigravity:branch:fix/ISSUE-823-preserve-cc-and-attachment-length-throug

- **findings**: 2 ACTION (XmlLoadFileWriter child FileSize tag accessing stripped byte array length, child attachment hashing on stripped byte array), 1 INFO test gap (EDRM XML Tag assertion). Multi-source confirmed (correctness + adversarial): XmlLoadFileWriter line 319 emitted FileSize=0 for child records.
- **outcome**: accepted and fixed XmlLoadFileWriter line 319 to use `fileData.AttachmentLength`, added Tag FileSize and CC assertions to `StandardModeSpoolingMetadataTests.cs`. All 2,276 unit tests + 60 analyzer tests + 20 golden scenarios passed.
- **telemetry**: host=antigravity mode=branch specialists=7 bundle=~6000c
- **lessons**: Even when updating multiple load file writers, double check every occurrence of `.content.Length` (and not just `.FileSizeOverride`) across all composers and writers (e.g. `XmlLoadFileWriter.cs` had two places: line 207 and line 319).
- **suppressions**: none

### 2026-09-12 antigravity:branch:fix/ISSUE-824-reject-native-file-collisions-with-gener

- **findings**: 2 INFO (1 maintainability DRY helper extraction on ZipArchiveSink, 1 testing coverage gap on native-text companion collisions). Correctness, Adversarial, and API Contract reported 0 findings.
- **outcome**: accepted both — extracted `CreateTrackedEntry` helper in `ZipArchiveSink.cs` and added `StandardArchive_NativeFileCollidesWithExtractedTextCompanion_Throws` unit test in `SourceDrivenGenerationTests.cs`. All 2,285 unit + 60 analyzer tests + source-driven E2E green, format clean.
- **telemetry**: host=antigravity mode=branch specialists=5 bundle=32725c
- **lessons**: Centralizing archive entry tracking and collision checks in `CreateTrackedEntry` eliminates code duplication across native file, attachment, attachment text, extracted text, and load file archive sinks.
- **suppressions**: none

### 2026-09-13 antigravity:branch:fix/ISSUE-825-bound-multi-value-coded-selection-when-d

- **findings**: 0 ACTION, 6 INFO (1 stream-draining in subprocess test confirmed by adversarial + testing, 4 testing coverage gaps on null RangeConfig fallback, non-positive count, targetCount=1 multi-pool, custom delimiter, 1 doc/assert improvement). Correctness, Performance, and Maintainability clean (0 findings).
- **outcome**: accepted all 6 INFO suggestions — updated `CodedGeneratorTests.cs` to drain subprocess standard streams asynchronously, added unit tests for non-positive count, null RangeConfig fallback, targetCount=1 on multi-element pools, and custom delimiters. All 2,295 unit tests + 60 analyzer tests pass cleanly.
- **telemetry**: host=antigravity mode=branch specialists=5 bundle=~4200c
- **lessons**: Precomputing distinct pool values and applying sparse Fisher-Yates selection guarantees bounded execution O(k) for multi-value selection regardless of pool duplicates or skew.
- **suppressions**: none
### 2026-09-14 antigravity:branch:fix/ISSUE-831-prevent-overflow-in-comparison-report-sk

- **findings**: 0 ACTION, 7 INFO (2 adversarial: case-insensitive prefix matching and null collection/item defense; 1 maintainability: DRY checked block; 4 testing: supplemental mode large gap test, null guard test, fallback overflow test, sequence large gap test). Correctness, Performance, and API Contract clean (0 findings).
- **outcome**: accepted all 7 findings — refactored `ResultSummaryBatesAnalysis` with unified checked block, case-insensitive prefix comparison (`StringComparison.OrdinalIgnoreCase`), and null-defense; added unit and integration tests covering supplemental mode gaps > Int32.MaxValue, null guards, fallback overflow, and aggregate overflow in `ProductionManifestComparer.CompareAndReportAsync`. All 2,357 unit tests + 60 analyzer tests pass cleanly.
- **telemetry**: host=antigravity mode=branch specialists=6 bundle=18830c
- **lessons**: Using checked scalar arithmetic over gap boundaries ((v2 - v1) + 1) preserves exact O(1) space and time per gap without enumerating identifiers, while safely scaling beyond 32-bit integer limits.
- **suppressions**: none

### 2026-09-19 droid:local:#870-eocd-ambiguity

- **findings**: cycle 1: 4 ACTION (verifier accepts forged authentic pair; unbounded zf.read; unbounded signature-scan list; false invariant name + stale docs) + 3 INFO. cycle 2 (after fixes): 0 ACTION, 11 INFO across 7 specialists (2 multi-source budget-default, 2 multi-source deflate-binding, 2 multi-source ushort ceiling, pairing-order, sidecar-missing message, UTF-8 decode, 5 testing breadth, span reuse, literal/docs/perf no-actions).
- **outcome**: accepted — cycle 1 findings all fixed (local/central cross-validation + sidecar hash binding, bounded chunked shadow read, >2-signature early abort, invariant renamed shadow-eocd-selects-b-bin, docs synced, +5 tests). Cycle 2: fixed 6 INFO one-liners + 4 tamper tests; rejected 5 as consistent-with-practice/bounded (documented above). All gates green: 58 Python, 2,643 unit, 60 analyzer, format, archive E2E.
- **telemetry**: host=droid mode=local specialists=7 bundle=40263c
- **lessons**: ApplyPatch tool unavailable in this environment (authorization error) — use Edit with exact strings. Task-subagent prompts must inline the diff explicitly; a placeholder token ships an empty review bundle. Multi-source agreement (budget default, deflate binding) converged on the same fix from 3/2 specialists independently — good fingerprinting signal.
- **suppressions**: none

### 2026-09-19 droid:local:#878-adls-segment-depth

- **findings**: cycle 1: 0 ACTION, 13 INFO across 6 specialists (unanimous "patch is correct"). Merged: 3 accepted fixes (guard internalized + C# guard test, guard violation+1 semantics documented, verifier dict lookup), 8 rejected with reasons (deliberate duplication, unevaluated platform mark per REQ-212, convention literals, unreachable degenerate cases).
- **outcome**: accepted — all fixes applied, gates green (62 Python, 2,651 unit, 60 analyzer, format, archive E2E 103 pairs). Fast-pass rerun clean (0 findings, confidence 9).
- **telemetry**: host=droid mode=local specialists=6+1fast bundle=25280c
- **lessons**: Decision-driven work: maintainer block comments on #878 named exactly the two product decisions (budget route, segment basis); user AskUser answers translated directly into the exempt-recipe design. Placeholder-token bug recurred once (DIFF_878_PLACEHOLDER) — caught after single dispatch, stopped and relaunched; inline the diff into every subagent prompt.
- **suppressions**: none

### 2026-09-19 droid:local:#881-manifest-metadata-contract

- **findings**: 0 ACTION, 16 INFO across 7 specialists (correctness 3, adversarial 3, security 2, testing 6, maintainability 3, performance 3, api-contract 3) — unanimous "patch is correct", confidence 8–9. Merged to 10 fingerprints: 5 accepted (non-object metadata silently skipped — 4-source agreement, 8192 named constant, lossy-derivative doc clause, release-note callout, boundary/empty/multi-violation tests), 5 rejected (control-chars-beyond-CRLF = user-decided contract boundary, non-string size-accounting moot since type error fails report, key-literal duplication = convention, .bat errorlevel = existing pattern, E2E negative case = unit-covered).
- **outcome**: accepted — all fixes applied (validator else-branch finding, MaxAzureMetadataBytes const, README clause, +6 tests). Fast-pass rerun clean (0 ACTION, 4 self-resolving INFO, confidence 9). Gates green: 2,665 unit, 60 analyzer, format, production-sets E2E, 62 Python verifier.
- **telemetry**: host=droid mode=local specialists=7+1fast bundle=20150c
- **lessons**: Security specialist notification showed "No output available" but TaskOutput block=false retrieved the full JSON — always fetch via TaskOutput, never trust the empty-notification shape. MA0009 (regex DoS) bans runtime Regex in this repo — character-loop validation avoids the analyzer and is simpler. Accepting the 4-source INFO (non-object skip) closed a real contract hole the user decision didn't name explicitly.
- **suppressions**: none

### 2026-09-19 droid:local:#887-encoding-trail-byte-suite

- **findings**: 0 ACTION, 12 INFO across 7 specialists (correctness 2, adversarial 5, security 2, testing 6, maintainability 2, performance 1, api-contract 1) — unanimous "patch is correct", confidence 8–9. Merged to 10 fingerprints: 6 accepted (local-header bit-11 check — 3-source agreement, C# lead-byte + central bit-11 asserts, decode-failure test, local-raw mismatch test, unknown-key + multi-entry tests, `case_key and` idiom cleanup), 4 rejected (EOCD-at-−22 and offset-literal conventions, Cp437 post-patch shape duplication, codec tuple in four places — all established independent-oracle patterns).
- **outcome**: accepted — all fixes applied (verifier local bit-11 check + 4 Python tests + C# assert hardening). Fast-pass rerun clean (0 ACTION, 3 self-resolving INFO, confidence 8). Gates green: 71 Python, 2,671 unit, 60 analyzer, format, archive E2E (encoding suite verified by named-codec consumer), basic smoke.
- **telemetry**: host=droid mode=local specialists=7+1fast bundle=36905c
- **lessons**: Python's strict cp932 codec maps invalid leads to private-use error chars (U+F8F0–U+F8F3) instead of raising — UnicodeDecodeError needs a genuinely invalid sequence (0xEF 0x5C raises, 0xFF 0x5C does not). VerificationError has no .message attr — the message is str(exception). ListSuite output is ordinal Case-Key order — contract-test arrays must match that order, not declaration order.
- **suppressions**: none

### 2026-09-22 opencode:issue-review:1009-1020

- **findings**: 6 (2 ACTION-tighten, 4 INFO) — blocking-edge over-serialization, README-note overlap, arch-severity overstatement, test-vector gaps
- **outcome**: partial — 3 issue edits recommended (#1013 partial block + dedupe, #1015 severity note, #1010/#1011 test vectors), no bodies rewritten inline
- **telemetry**: host=opencode mode=issue-review specialists=0(subagent rate-limit, inline review) bundle=12-issues
- **lessons**: subagent wave can rate-limit; inline fallback with direct grep verification of every file:line cite works
- **suppressions**: none

### 2026-09-22 opencode:issue-review:1009-1020 (cycle 2, post-fixall)

- **findings**: 2 rejected (#1015 false-premise, #1020 self-negating AC + drive-by), 4 weakened (#1009 P1->P2 + MD gap re-scoped, #1011 P1->P2, #1014/#1016 P2->P3), 3 corrected (#1011 lines 518/529->521/532, #1017 15->21 props, #1018 30->51 arms), #1019 blocker cleared + 5->6 checks
- **outcome**: applied — 2 closed, labels re-tuned, bodies re-grounded, native blocker removed
- **telemetry**: host=opencode mode=issue-review specialists=2 (general ×2) bundle=12-issues
- **lessons**: challenge reviewers disagree with grounders; every numeric claim must be re-verified by orchestrator grep before acting — both agents made their own errors (run_check count, bat false-pass). Rejection comments must carry a reopen path (premise-false → reopen if maintainer disagrees).
- **suppressions**: none

### 2026-09-22 opencode:issue-review:archive-mode-verification

- **findings**: 18 claims re-grounded (REQ-208..219) — 14 GROUND, 2 PARTIAL (REQ-208 encoding suite, REQ-213 deadline), 2 discrepancies; frozen REQ-210 vector math verified byte-exact by subagent; 7 verifier gaps (3 MAJOR: invariants unasserted, failureStages unenforced, single-reader expect engine; 130/cancel untested)
- **outcome**: filed 7 tickets #1022-#1028 (P1 deadline; P2 seed/suite/verifier/cancel; P3 hash-skip/engine)
- **telemetry**: host=opencode mode=issue-review specialists=3 (general x3: grounding, adversarial, delta) bundle=archive-domain
- **lessons**: seq-of-verification beats single-pass: advocates confirm claims while attackers find overclaim. Frozen-vector math is independently checkable in seconds — always run it.
- **suppressions**: none

### 2026-09-22 opencode:issue-review:priority-bug-logging (archive mode)

- logged fast-impl plans on #1022/#1023/#1024/#1025/#1026 (exact file:line grounding, minimal patch direction, test plan)
- upgraded #1025 (verifier oracle: invariants unasserted + failureStages unenforced) P2 -> P1 — trust anchor of extractor testing, coverage hole on new critical code
- re-grounded during logging: generator DOES emit failureStages for unsupported-reader profiles (ArchiveTestSuiteGenerator.cs:688-696); schema:322 + ArchiveTestCase.cs:57 define it; fixtures dir only holds smoke subset (valid-empty pair + invalid/) so failureStages absent from checked-in fixtures but live in generated ones
- cancel wiring re-verified: Program.cs:67-79 SIGINT/SIGTERM -> cts.Cancel; ArchiveTestCliWorkflow.cs:121-125 -> 130; staging try/finally cleanup ArchiveTestSuiteGenerator.cs:41-155
- suppressions: none

### 2026-09-27 opencode:refactor-review:1042-archive-suite-registry

- **findings**: 19 raw → 11 fingerprints after dedup. Accepted 6 (case-tolerance restore in `ResolveSuite`, derive help-text suite list, derive diagnostic list keeping message shape literal, derive `all`-union suite set, `Assert.NotEmpty` on the registry, `ListSuite` canonical-name doc). Rejected 5 with reasons: one-directional docs gate (measured prose frequency: `malformed` 9×, `all` 6× — set equality would false-positive), `ResolveSuite` null guard (unreachable, non-nullable signature), REQ-208 governance (AGENTS.md Critical Rule 4 sanctions revising requirement *text*; only IDs are immutable), name-level docs coverage + fragile reflection guard (pre-existing, out of scope).
- **orchestrator error corrected**: initially rejected the case-tolerance finding on a *coverage* argument ("a test catches it"). The correctness specialist proved by mutation that the deleted switch returned constants of any casing, so matching constants ordinally **narrowed** accepted input — a parity regression, not a coverage gap. Coverage is not parity. Re-accepted and fixed; this was the single highest-value finding in the wave.
- **process defect found**: testing + adversarial specialists wrote to the shared working tree despite read-only dispatch, and each one's `git restore` cleanup reverted legitimate orchestrator fixes (3 and 1 respectively). One revert was caught only incidentally by a file reappearing in `git status`.
- **outcome**: applied — PR #1076 merged (squash) as `86d8873`; issue #1042 closed. All 16 CI checks green including `goldens` (byte-parity) and `perf-guard`; SonarCloud gate passed with 0 new issues; CodeRabbit 0 findings; `wait-for-reviews.sh` exit 0.
- **telemetry**: host=opencode mode=refactor specialists=5 (correctness, adversarial, testing, maintainability, performance) bundle=7-file/97-line diff
- **lessons**: (1) Multi-source agreement caught a real parity regression the orchestrator reasoned away — never let a single specialist's "keep the literal" note override a 2-source empirical mutation proof. (2) Specialists that "verified" via live mutation produced their highest-value findings, so the capability is worth keeping — it just has to be *described*, not applied. (3) Verify the tree with `git status --porcelain` + `git diff HEAD --stat` after every wave; never trust a subagent's self-report about the tree. (4) A bot's `Review skipped` status is not an approval — only `wait-for-reviews.sh` exit 0 counts.
- **skill changes**: added a mandatory "Hard constraints — you are a READ-ONLY reviewer" block to all 9 `agents/*.md` files (defense in depth for standalone dispatch) and to the SKILL.md dispatch appendix (the chokepoint every prompt passes through); added a mandatory **Tree-integrity gate** to SKILL.md step 4 with a 4-step divergence protocol; added the no-mutation insight to `OPERATIONS.md` so it reaches agents through the insights channel too.
- **deferred (flagged in PR, maintainer's call)**: reading `SupportedSuites` triggers `beforefieldinit` on `ArchiveTestCatalog`, so the help path builds all 106 cases + a 4 KiB buffer — measured **+26 ms** median on `zipper` with no args (65.7→91.6, interleaved, no overlap; `--version` unchanged). No documented cold-start budget, human-error path, `perf-guard` passed — accepted deliberately rather than adding a holder type.
- **suppressions**: none

### 2026-10-12 antigravity:branch:feat/ISSUE-1093-test-expand-coverage-for-roslyn-analyzer

- **findings**: 0 ACTION, 5 INFO across 4 specialists (correctness, adversarial, testing, maintainability) — null provider handling in DeterministicFormattableAnalyzer, static class naming matching domain pattern in SealedDomainTypeAnalyzerTests, SourceFileTypes property coverage in MetadataConfigTests/TiffConfigTests.
- **outcome**: accepted all — fixed null provider handling with IsNullArgument expression unwrapping, added static class DefaultConfig test, and added SourceFileTypes mixed-type tests. All 2,967 unit + 74 analyzer tests pass cleanly.
- **telemetry**: host=antigravity mode=branch specialists=4 bundle=~12000c
- **lessons**: Passing `(IFormatProvider)null` to `string.Format` falls back to `CultureInfo.CurrentCulture`. The analyzer must inspect the argument expression syntax (`IsNullArgument`) rather than only checking parameter type definition to prevent unflagged culture-sensitive calls.
- **suppressions**: none

### 2026-10-15 antigravity:branch:feat/ISSUE-1121-test-independently-cover-production-mani

- **findings**: 0 ACTION, 1 INFO across 4 specialists (correctness, adversarial, testing, maintainability) — testing specialist suggested asserting individual properties on Changed details in missing-hash changed test.
- **outcome**: accepted and applied — added pathChanged and controlChanged property assertions. All 3,072 unit + 78 analyzer tests pass cleanly. All 4 mutation tests (isPathMatch, isControlMatch, match precedence, duplicate-control) failed as required before restoring.
- **telemetry**: host=antigravity mode=branch specialists=4 bundle=~15000c
- **lessons**: Explicit assertions on each detail collection entry prevent false negatives where count matches but fields are misassigned.
- **suppressions**: none


### 2026-10-17 antigravity:branch:feat/ISSUE-1123-test-decode-serialized-email-attach

- **findings**: 1 ACTION (adversarial), 1 INFO (adversarial), 0 other across 4 specialists (correctness, adversarial, testing, maintainability). Adversarial noted that `(i * 31 + 7) & 0xFF` cycles modulo 256, meaning 58,368-byte chunks (which are multiples of 256) were identical across multi-chunk boundary tests, and that repeated-chunk corruption test should duplicate chunk 1 into chunk 2 keeping total length equal.
- **outcome**: accepted and applied both — mixed higher-order bits `(i >> 8) ^ (i >> 16)` into payload generation and updated `WhenChunkIsRepeated` to duplicate chunk 1 into chunk 2 keeping length at 116,736 bytes. All 3,097 unit tests and 78 analyzer tests pass cleanly.
- **telemetry**: host=antigravity mode=branch specialists=4 bundle=~11000c
- **lessons**: When generating test payloads for chunk-boundary testing, ensure the generator's period exceeds the chunk size (and is coprime to chunk boundaries) so chunk offset advancement defects cannot be masked by cyclic repetition.
- **suppressions**: none

### 2026-10-18 antigravity:branch:feat/ISSUE-1127-test-distinguish-numeric-gaussian-and-ex

- **findings**: 4 (0 ACTION, 4 INFO across 4 specialists). 1 test-design note on tail clamping elevating Q4, 3 testing gaps (distinct check in AdditionalGeneratorTests, Min==Max test across distributions, negative range test).
- **outcome**: accepted 2 findings (added distinct count check on expSamples in AdditionalGeneratorTests, expanded Min==Max test in NumberGeneratorTests to Theory covering all distributions). All 3,120 unit tests and 78 analyzer tests passed.
- **telemetry**: host=antigravity mode=branch specialists=4 bundle=~5500c
- **lessons**: Statistical distribution tests benefit from checking distinct count to ensure constant mutants (even when in lower half or bounded) are rejected.
- **suppressions**: none

### 2026-10-26 droid:branch:feat/ISSUE-1281-declared-size-undersized

- **findings**: 0 ACTION, 2 INFO across 5 specialists (correctness, testing, maintainability, security, adversarial) — .NET reader test did not assert a non-empty partial prefix; bounded-resource suite comment named only #843.
- **outcome**: accepted and fixed both INFO findings; follow-up correctness review clean. Full fixture suite and validation gates passed.
- **telemetry**: host=droid mode=branch specialists=5+1fast bundle=15-file/243-line diff
- **lessons**: A normalized `read-entry-returns-unverified-bytes` outcome proves only a hash mismatch; assert returned byte length/content when the test name claims a partial boundary read.
- **suppressions**: none

### 2026-10-26 droid:branch:1282-version-local-central-mismatch

- **findings**: 0 ACTION, 0 INFO across 5 specialists (correctness, testing, maintainability, adversarial, api-contract).
- **outcome**: accepted / clean review. Fixture verification, full .NET tests, mutation smoke, formatting, and complete 113-case E2E passed.
- **telemetry**: host=droid mode=branch specialists=5 bundle=25702c
- **lessons**: none
- **suppressions**: none

### 2026-10-26 droid:branch:1282-version-local-central-mismatch-followup

- **findings**: 0 ACTION, 0 INFO across 5 reviewers (correctness, testing, maintainability, adversarial, API contract).
- **outcome**: clean review after adding the missing local-header MutationSpec and regression assertions; the test failed first against the single-record implementation. Focused tests, independent fixture verification, format, mutation smoke, and the pre-commit full unit suite passed.
- **telemetry**: host=droid mode=branch specialists=5 bundle=27519c
- **lessons**: none
- **suppressions**: none

### 2026-10-26 droid:branch:1282-resume-validation

- **findings**: 0 ACTION, 0 INFO across 6 reviewers (correctness, adversarial, testing, maintainability, performance, API contract).
- **outcome**: clean review. Focused Archive Test Fixture tests (259), replay tests (2), single-fixture verification, complete 113-case Archive Test E2E, format, and mutation smoke gate passed.
- **telemetry**: host=droid mode=branch specialists=6 bundle=28141c
- **lessons**: none
- **suppressions**: none

### 2026-10-26 droid:branch:feat/ISSUE-1283-archive-codec-divergence

- **findings**: 0 ACTION, 3 INFO across 6 specialists; correctness, adversarial, and security clean.
- **outcome**: fixed all three: added local/central raw-name tampering tests, corrected the helper docstring, and documented Python 3.11+; follow-up review clean.
- **telemetry**: host=droid mode=branch specialists=6 bundle=~37K
- **lessons**: Verifier tests should tamper with actual Archive header bytes as well as sidecar metadata; document runtime prerequisites next to the suite contract.
- **suppressions**: none

### 2026-10-26 droid:branch:1283-per-header-unicode-path-crc

- **findings**: 2 INFO on first pass (per-header CRC test gap; stale comment); 0 ACTION, 0 INFO across 5 specialists after fixes.
- **outcome**: fixed review findings with local/central CRC validation against each header's name, separate CRC rejection tests, and corrected documentation. No actionable findings remain.
- **telemetry**: host=droid mode=branch specialists=5 bundle=10352c
- **lessons**: Unicode Path CRC tests should cover local and central names independently when a fixture allows header-name divergence.
- **suppressions**: none

### 2026-10-26 droid:branch:1283-resume-validation

- **findings**: 1 INFO (duplicate central-name hex conversion); 0 ACTION; no findings after the fix review.
- **outcome**: reused one hex conversion for both initial layout fields. All 3,468 .NET tests, 126 verifier tests, 114 generated fixtures, formatting, and mutation smoke passed.
- **telemetry**: host=droid mode=branch specialists=8 bundle=68066c
- **lessons**: none
- **suppressions**: none

### 2026-10-26 droid:branch:issue-1284-net11-crc-canary

- **findings**: 2 initial documentation-outcome findings (maintainability + adversarial, same issue); 0 after correction.
- **outcome**: accepted and fixed by distinguishing the current normalized outcomes for CRC-declaration mismatches (`crc-mismatch-unchecked`) and payload flips (`integrity-fails`), then documenting their .NET 11 transition. Focused reader tests (123) and full solution tests (3,547) passed; format verification passed. .NET 11 behavior branch could not be run because only .NET 10 runtimes are installed.
- **telemetry**: host=droid mode=branch specialists=5 bundle=5104c
- **lessons**: Document each affected case's current normalized outcome before describing runtime-upgrade transitions.
- **suppressions**: none

### 2026-10-26 droid:branch:issue-1285-resume-validation

- **findings**: 0 ACTION, 0 INFO across 7 reviewers (correctness, adversarial, security, testing, maintainability, performance, API contract).
- **outcome**: clean review. Focused .NET tests (415), replay/golden tests (2), Python verifier tests (150), strict traceability, generated-fixture verification, mutation smoke (3 killed, 0 survived), and format verification passed. Full Archive Test E2E was not run because it would delete the pre-existing `results/archive-test-suites` directory.
- **telemetry**: host=droid mode=branch specialists=7 bundle=78157c
- **lessons**: none
- **suppressions**: none

### 2026-10-26 droid:branch:issue-1289

- **findings**: 0 ACTION, 0 INFO across 5 reviewers (correctness, adversarial, testing, maintainability, performance).
- **outcome**: clean review. Full solution tests, format, 118-case Archive Test E2E, independent verification, 7-Zip, offline verification, and mutation smoke passed.
- **telemetry**: host=droid mode=branch specialists=5 bundle=31857c
- **lessons**: none
- **suppressions**: none

### 2026-10-27 droid:branch:issue-1290

- **findings**: 1 INFO (missing negative test for hostile-name sidecar fields); fixed with direct tampering cases. 0 findings across 5 follow-up reviewers.
- **outcome**: verified the full 119-case catalogue and unchanged 5-case smoke set, ran the archive E2E including 7-Zip and offline verification, focused .NET/Python tests, format check, and mutation smoke (2 killed, 0 survived). The unrelated macOS runner liveness test failed in CI but passed when reproduced locally. No publication performed.
- **telemetry**: host=droid mode=branch specialists=12 bundle=~59K
- **lessons**: hostile-name verifier tests should tamper the sidecar independently of the ZIP headers.
- **suppressions**: none

### 2026-10-27 droid:branch:issue-1290-follow-up

- **findings**: 1 ACTION rejected (safe rewriting is already allowed as `entry-renamed-by-policy`); 2 INFO fixed (second-entry header tampering and stale liveness timing description).
- **outcome**: four follow-up reviewers returned no findings; focused liveness and full verifier tests passed. No publication performed.
- **telemetry**: host=droid mode=branch specialists=4 bundle=~2500c
- **lessons**: Check existing outcome tokens before proposing a new success outcome; a contained renamed path is already represented.
- **suppressions**: none

### 2026-10-27 droid:branch:issue-1291-opt-encoding

- **findings**: 0 ACTION, 0 INFO across 7 reviewers (correctness, adversarial, security, testing, maintainability, performance, API contract); CodeRabbit found 0.
- **outcome**: clean review. Focused tests, all 3,516 unit tests, 78 analyzer tests, format, chaos coverage E2E (18/18), mutation smoke (3 killed, 0 survived), and pre-push E2E smoke (6/6) passed. PR #1372 opened; robot review gate had no unresolved threads.
- **telemetry**: host=droid mode=branch specialists=7 bundle=41490c
- **lessons**: none
- **suppressions**: none

### 2026-10-28 droid:branch:issue-1300-edrm-xml-chaos-followup

- **findings**: 0 ACTION, 0 INFO across 6 reviewers (correctness, testing, maintainability, adversarial, performance, API contract).
- **outcome**: aligned direct XML Chaos Engine tests to emitted document counts and added exact-count two-document coverage. Production already passes EDRM-XML's document count without a header. Focused tests (10) and formatting passed; CodeRabbit's follow-up was verified.
- **telemetry**: host=droid mode=branch specialists=6 bundle=~4K
- **lessons**: EDRM-XML Chaos Engine tests should count emitted `<Document>` elements, not delimited-file header lines.

### 2026-10-28 droid:local:pr1376-recordid

- **findings**: 0 (one fast-pass correctness reviewer).
- **outcome**: clean autoreview after capturing the original XML RecordID before mutation. Focused ChaosEngine tests (59) passed; mutation smoke killed all 3 tested mutations.
- **telemetry**: host=droid mode=local specialists=1 bundle=~500c
- **lessons**: none
- **suppressions**: none
- **suppressions**: none

### 2026-10-28 droid:branch:pr1376-quote-aware-anomalies

- **findings**: Initial review found a doubled-quote test gap; CodeRabbit found two CSV embedded-newline defects. Final autoreview and CodeRabbit returned no findings.
- **outcome**: Fixed quote-aware Concordance boundary counting and CSV newline placement. Added regression/property coverage. Focused tests, 90 properties, mutation smoke, formatting, and 3,636 unit tests passed. Two commits created locally; no publication performed.
- **telemetry**: host=droid mode=branch specialists=6 bundle=~13K across follow-up review waves
- **lessons**: Quoted-delimited anomaly tests should cover embedded delimiters and consecutive doubled-quote pairs.
- **suppressions**: none

### 2026-10-29 droid:branch:issue-1304-platform-skips

- **findings**: 1 INFO (added negative-path test for skipped reader report with applicable expectations); 1 adversarial ACTION rejected because the full E2E run produced the .NET skip report. No correctness, maintainability, or performance findings.
- **outcome**: Fixed the matrix platform-skip report bug, reran focused Python/.NET checks and the isolated 119-case Archive Test E2E. The final 45-test matrix run, .NET format check, and fast-pass review of the added test passed. CodeRabbit still pending.
- **telemetry**: host=droid mode=branch specialists=6 bundle=39358c
- **lessons**: Keep the .NET report status tied directly to whether any expectation applies; the full E2E report is the proof for report-emission paths.
- **suppressions**: none

### 2026-10-29 droid:branch:issue-1304-reader-matrix-closeout

- **findings**: Review cycle 1 found a Windows-specific test issue and an Info-ZIP Adapter Check path when no expectation applies; cycle 2 found missing adapter-only coverage for read-entry/integrity-check and an existing timeout-test runtime ceiling; final cycle found 1 INFO about `expectationStatus`.
- **outcome**: Fixed the cross-platform test setup, made Info-ZIP actions run when declared by Adapter Check, normalized outcomes, classified deadline exhaustion as execution-error, and covered all three adapter-only actions plus probe failure. Deferred the runtime-ceiling note because its timing assertion is unchanged by this diff. Kept the report-status note non-gating: `expectationStatus` is the separate Reader Profile comparison, while the Adapter Check and fixture statuses retain their own contracts. Final 52-test matrix suite, mutation smoke, full 119-fixture E2E, and formatting passed.
- **telemetry**: host=droid mode=branch specialists=6 bundle=14335
- **lessons**: Test adapter-only Reader Adapter Profile actions across every supported action, including host-inapplicable expectations and probe errors.
- **suppressions**: none

### 2026-10-29 droid:branch:issue-1304-coderabbit-fixes

- **findings**: Autoreview found 1 INFO (testing, confidence 8) — new empty-Archive deadline guard lacked a direct regression. CodeRabbit cycle 1 reported a missing .NET Adapter Check comparison, but source inspection confirmed the comparison already exists; added a negative regression test. Cycle 2 found ambiguous Python adapter availability wording, clarified that Python `zipfile` is required.
- **outcome**: Fixed the deadline-test gap and clarified README/REQ-212. Full Python matrix suite (62 tests), focused .NET report test, DocsDriftTests (13), format check, and mutation smoke passed.
- **telemetry**: host=droid mode=branch specialists=6+1fast bundle=10695c
- **lessons**: none
- **suppressions**: none

### 2026-10-29 droid:branch:issue-1304-gate-clarification

- **findings**: 0 ACTION, 0 INFO across 5 reviewers (correctness, adversarial, testing, maintainability, security).
- **outcome**: Clean review. Focused matrix (63), 7-Zip (13), and fixture-verifier (167) suites plus mutation smoke passed.
- **telemetry**: host=droid mode=branch specialists=5 bundle=27325c
- **lessons**: none
- **suppressions**: none

### 2026-10-29 droid:branch:issue-1304-python-check-regression

- **findings**: 0 ACTION, 0 INFO from one fast-pass correctness reviewer.
- **outcome**: Added a direct regression for a mismatched Python Adapter Check; the complete matrix suite (64 tests) passed.
- **telemetry**: host=droid mode=branch specialists=1 bundle=3586c
- **lessons**: none
- **suppressions**: none

### 2026-10-29 droid:branch:issue-1304-e2e-log-capture

- **findings**: 0 ACTION, 0 INFO from one fast-pass correctness reviewer.
- **outcome**: The shell syntax check and 64-test matrix suite passed with output captured to a log for failure diagnostics.
- **telemetry**: host=droid mode=branch specialists=1 bundle=817c
- **lessons**: none
- **suppressions**: none
