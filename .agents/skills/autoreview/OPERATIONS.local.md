
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



