#!/bin/bash
# Regression guard for run-mutation-smoke.sh candidate selection.
# String-literal value producers must get a mutant; log/throw/raw-string
# literals and unscoped files must not.
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TARGET_SCRIPT="$REPO_ROOT/tests/run-mutation-smoke.sh"

WORK=$(mktemp -d "${TMPDIR:-/tmp}/mutation-operators.XXXXXX")
trap 'rm -rf "$WORK"' EXIT

git -C "$WORK" init -q
git -C "$WORK" config user.email "test@example.com"
git -C "$WORK" config user.name "mutation-operators-test"
mkdir -p "$WORK/tests" "$WORK/src/LoadFiles" "$WORK/src/Profiles/Generation"
cp "$TARGET_SCRIPT" "$WORK/tests/run-mutation-smoke.sh"

cat > "$WORK/src/LoadFiles/SyntheticRowValues.cs" <<'EOF'
namespace Zipper.LoadFiles;

internal static class SyntheticRowValues
{
    internal static string Custodian(int folder) => $"Custodian {folder}";
}
EOF

cat > "$WORK/src/LoadFiles/DatComposer.cs" <<'EOF'
namespace Zipper.LoadFiles;

internal static class DatComposer
{
    internal static void Reset()
    {
    }
}
EOF

cat > "$WORK/src/Profiles/Generation/BooleanGenerator.cs" <<'EOF'
namespace Zipper.Profiles.Generation;

internal static class BooleanGenerator
{
    internal static bool Enabled() => true;
}
EOF

cat > "$WORK/src/Program.cs" <<'EOF'
internal static class Program
{
    private const string Banner = "base";
}
EOF

git -C "$WORK" add .
git -C "$WORK" commit -qm "base"

# Working-tree edits: in-scope value literals in all three value-producer
# paths, an in-scope operator, a log literal, an unscoped literal, plus
# literals that must be excluded (raw string, multi-line throw message).
cat >> "$WORK/src/LoadFiles/SyntheticRowValues.cs" <<'EOF'

internal static class AddedValues
{
    internal static string Value() => "column value";

    internal static string Quoted() => "a \"b\" c";

    internal static void Fail()
    {
        throw new System.InvalidOperationException(
            "not a value");
    }

    internal static string Raw() => """raw""";
}
EOF

cat >> "$WORK/src/LoadFiles/DatComposer.cs" <<'EOF'

internal static class AddedLogging
{
    internal static string Value() => "composer value";

    internal static void Report(int a, int b)
    {
        System.Console.WriteLine("Reset not supported");
        var same = a == b;
    }
}
EOF

cat >> "$WORK/src/Profiles/Generation/BooleanGenerator.cs" <<'EOF'

internal static class AddedBoolean
{
    internal static bool Always() => true;

    internal static string Value() => "generated value";
}
EOF

cat >> "$WORK/src/Program.cs" <<'EOF'

internal static class AddedBanner
{
    private const string Other = "unscoped literal";
}
EOF

SHA_BEFORE=$(cksum "$WORK/src/LoadFiles/SyntheticRowValues.cs" | awk '{print $1 ":" $2}')

run_candidates() {
    (cd "$WORK" && bash tests/run-mutation-smoke.sh --base HEAD --list-candidates "$@")
}

echo "Testing default selection reserves a string-literal mutant..."
OUT=$(run_candidates)
echo "$OUT" | grep -q 'string literal → "MUTATED"' || {
    echo "FAIL: no string-literal mutant selected. Output:"
    echo "$OUT"
    exit 1
}
echo "$OUT" | grep -q 'DatComposer.cs:[0-9]* == → !=' || {
    echo "FAIL: existing operator candidate lost. Output:"
    echo "$OUT"
    exit 1
}

echo "Testing every value-producer path gets a string mutant..."
OUT20=$(run_candidates --max 20)
for expected in 'DatComposer.cs:[0-9]* string literal' \
                'SyntheticRowValues.cs:[0-9]* string literal' \
                'BooleanGenerator.cs:[0-9]* string literal'; do
    if ! echo "$OUT20" | grep -q "$expected"; then
        echo "FAIL: missing string mutant matching '$expected'. Output:"
        echo "$OUT20"
        exit 1
    fi
done

echo "Testing log, throw, raw-string, and unscoped literals are excluded..."
if echo "$OUT20" | grep -q 'Program.cs:[0-9]* string literal'; then
    echo "FAIL: unscoped file literal must not be mutated. Output:"
    echo "$OUT20"
    exit 1
fi
if [[ $(echo "$OUT20" | grep -c 'DatComposer.cs:[0-9]* string literal') -ne 1 ]]; then
    echo "FAIL: Console.WriteLine literal must not be mutated. Output:"
    echo "$OUT20"
    exit 1
fi
if [[ $(echo "$OUT20" | grep -c 'SyntheticRowValues.cs:[0-9]* string literal') -ne 2 ]]; then
    echo "FAIL: expected value and escaped-quote literals only (throw/raw excluded). Output:"
    echo "$OUT20"
    exit 1
fi

echo "Testing --max 1 keeps only the string mutant and drops operators..."
OUT1=$(run_candidates --max 1)
[[ $(echo "$OUT1" | grep -c '→') -eq 1 ]] || {
    echo "FAIL: --max 1 must keep exactly one candidate. Output:"
    echo "$OUT1"
    exit 1
}
echo "$OUT1" | grep -q 'string literal → "MUTATED"' || {
    echo "FAIL: --max 1 dropped the string mutant. Output:"
    echo "$OUT1"
    exit 1
}

echo "Testing listing does not modify source..."
SHA_AFTER=$(cksum "$WORK/src/LoadFiles/SyntheticRowValues.cs" | awk '{print $1 ":" $2}')
if [[ "$SHA_BEFORE" != "$SHA_AFTER" ]]; then
    echo "FAIL: --list-candidates modified a source file."
    exit 1
fi

echo "PASS: mutation smoke selects string-literal value mutants and skips noise."
