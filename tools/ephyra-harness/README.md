# Ephyra CLI trial

Ephyra 0.0.1 fits as an opt-in test harness around Zipper’s public CLI. The
Rust crate generates arguments, runs the .NET executable in a temporary
directory, and checks real Load Files, Archives, and Production Sets. It can
shrink failures and preserve cases for replay. Ephyra’s Rust instrumentation
measures the harness process. Use Zipper’s .NET coverage tools for product
coverage. [Upstream startup guide](https://github.com/datadog-labs/ephyra/blob/main/STARTUP.md)
and [CLI documentation](https://github.com/datadog-labs/ephyra/tree/main/crates/ephyra-cli).

## Run locally

From the repository root, with .NET 10 and Rust installed (tested with Rust 1.100.0):

```sh
cargo install ephyra-cli --version 0.0.1 --locked
dotnet build src/Zipper.csproj -c Release
cd tools/ephyra-harness
ephyra check --tests 10000 --no-instrument
EPHYRA_SEED=42 ephyra test --no-instrument --test cli_properties -- --test-threads=2 --skip prop_cli_fuzz_no_crash
```

Use an absolute `ZIPPER_BIN` path to override the default Release executable.
The CLI writes reports, observations, and saved failures under `.ephyra/` by
default. Pass `--dir` to keep runs separate. To replay a reported seed and
iteration, use a command filtered to that property:

```sh
EPHYRA_REPLAY=10833044368834433933:11 ephyra test --no-instrument --test cli_properties prop_loadfile_only_invariants
```

## Findings

- Concordance wrapped values in thorn characters with `--quote-delim none`.
  The serializer now omits the wrappers, and the Chaos Engine excludes the
  quote-escape anomaly when there are no quotes to remove. Historical replay
  was `10833044368834433933:11` for `prop_loadfile_only_invariants`.
- Standard Mode gave DAT and Concordance the same target path. Concordance now
  uses `.concordance.dat` when both formats are selected. The CLI also
  deduplicates formats after alias resolution. Historical replay was
  `18071072646946733044:5` for `prop_standard_multiformat_invariants`.
- The multi-format property now reads all five expected Load Files from the
  Archive or disk according to `--include-load-file`. File-backed stdout and
  stderr capture lets Zipper write without filling an unread pipe.

## Trial results

- Generator checks passed for all eight generators with 10,000 cases each.
- The seeded CLI run passed seven properties across 370 cases and replayed
  both saved regressions. It covered Loadfile-Only, Chaos Engine behavior,
  Source CSV, Archive generation, Standard Mode, and Production Sets.
- A separate CLI smoke run opened the ZIP Archive and found distinct DAT,
  Concordance, and EDRM-XML entries. The unquoted Concordance header contained
  DC4 separators and no thorn wrappers.
