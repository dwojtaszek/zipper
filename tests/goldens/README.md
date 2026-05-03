# E2E Goldens — Byte-Exact Regression Gate

This directory holds the **byte-exact regression gate** for Zipper's
end-to-end output. The existing E2E suite under `tests/` validates structure
(file counts, headers, encodings); the goldens layer validates that the
*actual bytes* on disk match a pre-captured fixture.

## Layout

```text
tests/goldens/
├── README.md              # this file
├── run-goldens.sh         # the runner — drives scenarios.tsv
├── scenarios.tsv          # pipe-delimited table (13 scenarios)
├── fixtures/              # checked-in golden output, one dir per scenario
│   ├── pdf-basic/
│   ├── pdf-metadata/
│   ├── ...
│   └── production-set/
└── lib/
    ├── diff-loadfile.sh   # bounded byte-diff with first-divergence context
    ├── sha-manifest.sh    # deterministic sha256 manifest of a tree
    └── _test/             # bats-core unit tests for the helpers
        ├── diff-loadfile.bats
        └── sha-manifest.bats
```

## CI

The `goldens` job in `.github/workflows/pr.yml` runs on every PR after unit
tests pass. It publishes the CLI, then executes `run-goldens.sh` in diff mode
against the committed fixtures.

## scenarios.tsv

The table is **pipe-delimited** (despite the `.tsv` extension). Header:

```
scenario_name | cli_args | seed | description
```

- `scenario_name` — short kebab-case identifier; also the fixture directory
  name under `fixtures/`.
- `cli_args` — exact argv (minus the executable) handed to Zipper. The seed
  is passed as `--seed <N>` in the CLI args.
- `seed` — RNG seed used for that scenario (documentation; the actual seed
  is part of `cli_args`).
- `description` — short human note; not parsed.

Lines starting with `#` and blank lines are ignored.

### Current scenarios (13)

| # | Name | Mode |
|---|------|------|
| 1 | pdf-basic | Standard |
| 2 | pdf-metadata | Standard |
| 3 | pdf-text | Standard |
| 4 | pdf-full | Standard |
| 5 | jpg-folders-gaussian | Standard |
| 6 | tiff-multipage | Standard |
| 7 | eml-attachments | Standard (structural) |
| 8 | eml-full | Standard (structural) |
| 9 | docx-basic | Standard |
| 10 | xlsx-basic | Standard |
| 11 | loadfile-only-dat | Loadfile-Only |
| 12 | loadfile-only-opt | Loadfile-Only |
| 13 | production-set | Production Set |

> **EML scenarios** use structural comparison (tree listing + DAT row count
> and header), not byte-exact diffing.  This is because `EmailTemplateSystem`
> uses `DateTime.Now` and `Random.Shared` internally, making email content
> non-deterministic even with `--seed`.

## Running scenarios

```sh
# Diff every scenario against its fixture.
ZIPPER_CLI=/path/to/zipper ./tests/goldens/run-goldens.sh

# Run just one scenario for debugging.
ZIPPER_CLI=/path/to/zipper ./tests/goldens/run-goldens.sh --scenario foo-basic

# Re-capture fixtures (intentional regeneration only — never in CI).
ZIPPER_CLI=/path/to/zipper ./tests/goldens/run-goldens.sh --capture
```

## Adding a scenario

1. Append a row to `scenarios.tsv`.
2. Run with `--capture` to populate `fixtures/<scenario_name>/`:
   ```sh
   ZIPPER_CLI=$(pwd)/publish-bin/Zipper \
     ./tests/goldens/run-goldens.sh --capture --scenario <scenario_name>
   ```
3. Inspect the fixture by hand — open the load file, check the headers,
   confirm the seed produces the expected categorical distribution. Goldens
   are only useful if a human signed off on the bytes once.
4. Commit the fixture directory alongside the row.

## What's in a fixture

Each fixture directory contains:

- `tree.txt` — deterministic listing of all files the CLI produced.
- `sha-manifest.txt` — SHA-256 hashes of all non-ZIP output files.
- Load files (`*.dat`, `*.opt`) — committed verbatim.
- Metadata (`*.json`) — committed verbatim, with timestamps normalised.

ZIP archives are **not committed** (their internal metadata has timestamps).
Load file content captures the same information deterministically.

## When a golden fails

Goldens go red when **output bytes change**. There are two reasons:

1. **Unintended regression.** Something in the codepath drifted: a refactor
   re-ordered enumerable iteration, a change of culture-aware formatting
   crept in, a default seed flipped. Treat as a real failure — fix the
   code, leave the fixture alone.
2. **Intentional change.** A new field was added to a load file, an output
   format was bumped, a generator's distribution was deliberately tuned.
   Re-capture the affected scenarios with `--capture`, eyeball the diff,
   and commit the new fixture in the same PR as the code change.

The first lines of `run-goldens.sh`'s output point at the divergence; for
load files we never dump the whole thing — just the first mismatching line
plus three lines of context.

## Normalisation

The runner normalises timestamped output before comparison:

- `archive_YYYYMMDD_HHMMSS.*` → `archive.*`
- `loadfile_YYYYMMDD_HHMMSS*` → `loadfile*`
- `PRODUCTION_YYYYMMDD_HHMMSS/` → `PRODUCTION/`
- `productionDate` in JSON → `"NORMALIZED"`

## Helper unit tests

The lib helpers carry their own [bats-core][bats] tests under
`lib/_test/`. They run locally with:

```sh
bats tests/goldens/lib/_test/
```

[bats]: https://github.com/bats-core/bats-core
