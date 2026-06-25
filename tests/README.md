# Tests

## Entry Points

| Script | When to run | What it does |
|--------|-------------|--------------|
| `run-tests.sh` / `.bat` | Before push (full) | Orchestrates all E2E suites below in sequence |
| `run-e2e-basic.sh` / `.bat` | Pre-push hook (fast) | Smoke subset — Standard mode only, no load-file variants |
| `run-e2e-loadfile.sh` / `.bat` | Included by `run-tests` | Loadfile-only + Chaos Engine scenarios |

Each `.sh` script has a `.bat` counterpart with identical coverage for Windows CI.

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
| `test-argument-interactions` | Combinations of flags that interact (e.g., `--attachment-rate` + `--with-families`) |
| `test-artifact-handling` | Zip structure, file paths, embedded load files |
| `test-bates-numbering` | Bates prefix/suffix/padding correctness |
| `test-chaos-anomaly-coverage` | Every chaos anomaly type and scenario fires without crash |
| `test-cli-coverage-gaps` | CLI flag edge cases not covered elsewhere |
| `test-column-profile-builtin-matrix` | All built-in column profiles produce valid output |
| `test-column-profile-custom-kinds` | Custom column-profile kinds (every generator type) |
| `test-column-profile-empty-pct` | Empty-percentage distribution correctness |
| `test-cross-platform` | Output is consistent across Linux/Windows line endings |
| `test-eml-comprehensive` | Email generation: structure, attachments, headers. EML golden tests use structural comparison (tree listing + DAT row count/header), not byte-exact diff. See tests/goldens/README.md for rationale. |
| `test-load-file-formats` | All 5 load file formats (DAT, OPT, CSV, EDRM-XML, Concordance) |
| `test-multipage-tiff` | Multi-page TIFF generation and OPT page entries |
| `test-office-formats` | DOCX/XLSX generation |
| `test-path-traversal-security` | Rejects paths that would escape the output directory |
| `test-production-sets` | Production Set directory tree + manifest |
| `test-target-zip-size` | `--target-zip-size` accuracy within tolerance |
| `test-unified-workflow` | End-to-end Standard + Loadfile-Only + Production Set in one run |

## Related

- Unit tests: `src/Zipper.Tests/` (run with `dotnet test`)
- CI gate map: [docs/cicd.md](../docs/cicd.md#quick-reference-for-agents)
