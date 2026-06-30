
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

