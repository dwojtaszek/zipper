# E2E Goldens — Harness

This directory holds the **byte-exact regression gate** for Zipper's
end-to-end output. The existing E2E suite under `tests/` validates structure
(file counts, headers, encodings); the goldens layer validates that the
*actual bytes* on disk match a pre-captured fixture.

This ticket (#197) ships **only the plumbing** — runner, lib helpers, an
empty scenarios table, and this README. Captured scenarios and CI wiring
land in follow-up tickets:

- `#198` — capture and commit 15 scenarios.
- `#199` — wire the goldens job into `pr.yml`.

## Layout

```
tests/goldens/
├── README.md              # this file
├── run-goldens.sh         # the runner — drives scenarios.tsv
├── scenarios.tsv          # pipe-delimited table; populated by #198
├── fixtures/              # checked-in golden output, one dir per scenario
└── lib/
    ├── diff-loadfile.sh   # bounded byte-diff with first-divergence context
    ├── sha-manifest.sh    # deterministic sha256 manifest of a tree
    └── _test/             # bats-core unit tests for the helpers
        ├── diff-loadfile.bats
        └── sha-manifest.bats
```

## scenarios.tsv

The table is **pipe-delimited** (despite the `.tsv` extension — kept for
compatibility with downstream tooling that expects the filename). Header:

```
scenario_name | cli_args | seed | description
```

- `scenario_name` — short kebab-case identifier; also the fixture directory
  name under `fixtures/`.
- `cli_args` — exact argv (minus the executable) handed to Zipper. The seed
  is exposed as the `SEED` env var; the CLI command is responsible for
  consuming it.
- `seed` — RNG seed used for that scenario. Must be deterministic.
- `description` — short human note; not parsed.

Lines starting with `#` and blank lines are ignored.

## Running scenarios

```sh
# Diff every scenario against its fixture.
ZIPPER_CLI=/path/to/zipper ./tests/goldens/run-goldens.sh

# Run just one scenario for debugging.
ZIPPER_CLI=/path/to/zipper ./tests/goldens/run-goldens.sh --scenario foo-basic

# Re-capture fixtures (intentional regeneration only — never in CI).
ZIPPER_CLI=/path/to/zipper ./tests/goldens/run-goldens.sh --capture
```

The runner produces an `sha256` manifest of both the captured fixture
directory and the freshly-generated output, diffs the manifests, and on any
divergence drops down to a per-file byte diff that prints the first
mismatching line plus three lines of context on each side.

## Adding a scenario

1. Append a row to `scenarios.tsv`.
2. Run with `--capture` to populate `fixtures/<scenario_name>/`:
   ```sh
   ZIPPER_CLI=$(pwd)/src/Zipper/bin/Release/net9.0/Zipper \
     ./tests/goldens/run-goldens.sh --capture --scenario <scenario_name>
   ```
3. Inspect the fixture by hand — open the load file, check the headers,
   confirm the seed produces the expected categorical distribution. Goldens
   are only useful if a human signed off on the bytes once.
4. Commit the fixture directory alongside the row.

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

## Helper unit tests

The lib helpers carry their own [bats-core][bats] tests under
`lib/_test/`. They run locally with:

```sh
bats tests/goldens/lib/_test/
```

CI wiring for the helpers + scenarios is part of `#199`.

[bats]: https://github.com/bats-core/bats-core
