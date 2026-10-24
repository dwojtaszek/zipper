---
name: verify-zipper-change
description: Use when about to claim a Zipper change is done, fixed, or passing, especially for CLI behavior, Load File formats, Production Sets, or output bytes, to gather real-artifact evidence before the claim.
---

# Verify a Zipper change

A gate that did not print green is not green. An artifact you did not open is not correct.

An agent that cannot see the result of its own work cannot iterate. The point of
this loop is to put eyes on the real artifact, not to collect passing commands.

## 1. Know which gates apply

```bash
./dev.sh --dry-run   # preview the gates affected by the diff against main
./dev.sh             # run them
```

Use `./dev.sh all` when the change crosses modes or the diff is unclear. It does
not run the goldens; run those separately when output bytes can change. Do not
hand-roll a subset and call it verified.

## 2. Run the real surface

For any CLI behavior change, run the command a user would run, from the worktree,
with a fixed seed and a fresh output directory inside the working directory
(`--output-path` outside it is rejected as path traversal):

```bash
OUT=$(mktemp -d ./verify-check-XXXXXX)
dotnet run --project src/Zipper.csproj -- --type pdf --count 2 --seed 42 --with-metadata --output-path "$OUT"
find "$OUT" -type f
```

Then open what it produced. Name the archive from the listing instead of using a
wildcard that could match more than one file:

```bash
ARCHIVE=$(find "$OUT" -name '*.zip' -print -quit)
unzip -l "$ARCHIVE"
unzip -p "$ARCHIVE" path/from/the/listing.dat | head -5

DAT=$(find "$OUT" -name '*.dat' -print -quit)
xxd "$DAT" | head -5    # delimiter, encoding, line endings
file "$DAT"

rm -rf "$OUT"           # after inspecting
```

A passing unit test proves the code path ran. It does not prove the DAT uses the
delimiter you claimed, the archive entry paths use `/`, or the Production Set tree
matches its manifest.

## 3. Output-producing changes need parity

Load File, Audit File, Production Set, and manifest bytes must not change unless
the issue says so (Critical Rule 6). Compare the golden manifest before and after:

```bash
dotnet publish src/Zipper.csproj -c Release -r linux-x64 --self-contained true -o ./publish/linux-x64
ZIPPER_CLI=$(pwd)/publish/linux-x64/Zipper bash tests/goldens/run-goldens.sh
```

Hashes must match. If the change intentionally alters bytes, say which bytes and
why, and capture those scenarios in the same PR. Always name the scenario, or
`--capture` rewrites every fixture in the suite:

```bash
ZIPPER_CLI=$(pwd)/publish/linux-x64/Zipper bash tests/goldens/run-goldens.sh --capture --scenario pdf-metadata
```

## 4. Read the whole failure

Fix the root cause, rerun the same gate, keep the output. Narrow the rerun to the
failing gate while iterating, then run the final gate set once against the final
diff.

If a gate cannot run, name exactly what is unverified and why. Missing tool,
missing platform, or exhausted quota is a report, never a pass.

## 5. Report

The final message names the commands run and their result. For an artifact check,
name the file inspected and what you saw in it. If nothing was run, say that.

A CLI behavior change also triggers Critical Rule 4 (README, Requirements,
glossary, E2E scripts). Check that list before calling the change done.

## Red flags

- "The tests pass" with no command line and no summary.
- A filtered test run standing in for the full class or suite.
- A screenshot, log, or trace the agent never read.
- Re-running everything after narrowing the cause. Rerun the failing gate, then
  the final gate set once.
- "Looks right." Look at the bytes.
