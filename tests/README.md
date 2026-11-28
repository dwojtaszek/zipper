# Tests

## Entry Points

| Script | When to run | What it does |
|--------|-------------|--------------|
| `run-tests.sh` / `.bat` | Before push (full) | Orchestrates all E2E suites below in sequence |
| `run-e2e-basic.sh` / `.bat` | Pre-push hook (fast) | Smoke subset — Standard mode only, no load-file variants |
| `run-e2e-loadfile.sh` / `.bat` | Included by `run-tests` | Loadfile-only + Chaos Engine scenarios |
| `run-mutation-smoke.sh` | Pre-push hook (fast) | Lightweight operator + value-literal mutation smoke on changed source lines |
| `test-mutation-smoke.sh` / `.bat` | Manual only | Bounded integration smoke for pinned Stryker CLI (#1110) |

Core cross-platform E2E suites (called by both `run-tests.sh` and `run-tests.bat`) have `.bat` counterparts for Windows CI. Utility scripts (`test-autoreview-validator.sh`, `test-build-version.sh`, `test-mutation-smoke-operators.sh`, `test-perf-measure.sh`, `test-stress-harness.sh`, `test-zip64-boundary.sh`, `validate-e2e-parity.sh`, `validate-req-traceability.sh`, `wait-for-reviews.sh`) are Linux-only. `test-mutation-smoke.sh` / `.bat` is a standalone, manual-only integration smoke for the pinned Stryker CLI and report normalizer (#1110), omitted from `run-tests` due to runtime cost. `validate-e2e-parity.sh` runs in PR CI (strict mode) and fails if the inline test cases or invoked sub-script suites drift between `run-tests.sh` and `run-tests.bat` (#624); platform-specific suites need a documented exemption in the script.

Run the ZIP64 workload checks on Linux with Python 3.11 or later and a built CLI.

```bash
dotnet build src/Zipper.csproj -c Release
python3 tests/zip64-workload-boundaries.py src/bin/Release/net10.0/Zipper count
```

The `count` recipe verifies the six counts adjacent to 10,000 and 65,535. Use
`scale` for 100,000 and 1,000,000 entries. Use `size` for exact 4 GiB size and
offset cases, with at least 4 GiB plus 1 MiB free in the temporary directory.
Each recipe reports generation and verification time, peak memory, Archive
size, and sampled member hashes.

## Platform-only runner checks

- `run-tests.sh` runs `test-mutation-smoke-operators.sh`, `test-perf-measure.sh`, and `test-stress-harness.sh`. It also runs the coverage-gate self-test, `test_perf_compare.py`, the golden helper and runner unit tests, and the `FileGenerationRequest` flat-property guard.
- `run-tests.bat` runs `test-run-tests-fatal.bat` to check how the Windows runner propagates a failing child suite.

A passing run covers the paired E2E suites and the checks invoked by that runner. GitHub Actions can also run checks separately from these entry points. See [CI.md](../CI.md) for CI coverage.

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `goldens/` | Content-hash parity check — captures expected output hashes and diffs them on each run. See [goldens/README.md](goldens/README.md). |
| `perf/` | Performance regression guard — runs `measure.sh` 5×, compares RSS and wall time against `baselines.json`. See [perf/README.md](perf/README.md). |
| `stress/` | Long-running throughput tests — generates large archives to catch memory leaks and throughput regressions. See [stress/README.md](stress/README.md). |
| `fixtures/` | Static reference data shared across E2E scripts: chaos type lists, column profile examples, JSON schema, target-size tolerance table. |

## E2E Script Index

Each script below is called by `run-tests.sh` and has a `.bat` parity file.

| Script | Covers |
|--------|--------|
| `test-archive-test-suites` | Archive test suites: publication, validation, exit codes, frozen smoke membership, replay determinism, and byte goldens |
| `test-argument-interactions` | Combinations of flags that interact (e.g., `--attachment-rate` + `--with-families`) |
| `test-artifact-handling` | Zip structure, file paths, embedded load files |
| `test-bates-numbering` | Bates prefix/suffix/padding correctness |
| `test-chaos-anomaly-coverage` | Every chaos anomaly type and scenario fires without crash |
| `test-ci-build-props` | MSBuild properties for reproducible builds (Deterministic, ContinuousIntegrationBuild, SourceRoot) |
| `test-cli-coverage-gaps` | CLI flag edge cases not covered elsewhere |
| `test-column-profile-builtin-matrix` | All built-in column profiles produce valid output |
| `test-column-profile-custom-kinds` | Custom column-profile kinds (every generator type) |
| `test-column-profile-empty-pct` | Empty-percentage distribution correctness |
| `test-cross-platform` | Output is consistent across Linux/Windows line endings |
| `test-eml-comprehensive` | Email generation: structure, attachments, headers. EML golden tests use structural comparison (tree listing + DAT row count/header), not byte-exact diff. See tests/goldens/README.md for rationale. |
| `test-load-file-formats` | All 5 load file formats (DAT, OPT, CSV, EDRM-XML, Concordance) |
| `test-mixed-file-types` | Mixed file-type distributions (`--types`) and format-dependent load-file output |
| `test-multipage-tiff` | Multi-page TIFF generation and OPT page entries |
| `test-office-formats` | DOCX/XLSX generation |
| `test-path-traversal-security` | Rejects paths that would escape the output directory |
| `test-production-sets` | Production Set directory tree + manifest |
| `test-run-tests-guard-parity` | E2E parity validator: verifies that unguarded `.bat` child calls are caught (#1040) |
| `test-source-driven` | Source-driven generation: input CSV validation, archive structure, and metadata mapping |
| `test-target-zip-size` | `--target-zip-size` accuracy within tolerance |
| `test-typesafe-audit` | TypeSafe audit runner: unit tests, fixture-mode execution, and workflow structure checks |
| `test-unified-workflow` | End-to-end Standard + Loadfile-Only + Production Set in one run |

## Requirement Traceability

`tests/req-traceability.tsv` maps active requirements (`REQ-NNN` and `REQ_E-NNN`) from `Requirements.md` to representative test cases (unit test methods or E2E scenarios) or reasoned exemptions. It is deterministically validated by `tests/validate-req-traceability.sh --strict` in CI.
- **Forward traceability:** every active requirement must have at least one mapped test or reasoned exemption (100% coverage enforced).
- **Reverse traceability:** `tests/validate-req-traceability.sh --reverse` provides an informational summary of test classes in `src/Zipper.Tests` relative to manifest mappings. Test classes without a dedicated REQ mapping represent test helpers, contract guards, profile arbitraries, or internal execution primitives.

## Related

- Unit tests: `src/Zipper.Tests/` (run with `dotnet test`)
- CI gate map: [docs/cicd.md](../docs/cicd.md#quick-reference-for-agents)
