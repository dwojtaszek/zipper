#!/bin/bash
# Critical Rule 6 parity harness for src/Validation/ProductionSetPostValidator.cs.
#
# Captures a content-hash manifest of Production Set output across several seeded
# scenarios, so a refactor can be proven byte-identical rather than merely green.
# Timestamp- and filename-independent: every path is made relative to the scenario
# root before hashing.
#
# Usage: tests/perf/../validation-parity.sh <label> <out-dir>
#   ./tests/validation-parity.sh before /tmp/parity-before
#
# Local and gitignored by design (see the header in AGENTS.md Critical Rule 6).

set -euo pipefail

LABEL="${1:?usage: validation-parity.sh <label> [out-dir]}"
# --output-path is confined to the working directory, so the harness writes under
# the repo unless an explicit relative root is given. Absolute paths are rejected.
OUT="${2:-results/validation-parity}"
if [[ "$OUT" = /* ]]; then
    echo "Error: out-dir must be relative to the repository root (--output-path is sandboxed)" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ZIPPER="$REPO_ROOT/src/bin/Release/net10.0/Zipper"

if [[ ! -x "$ZIPPER" ]]; then
    echo "Error: build Release first (dotnet build -c Release)" >&2
    exit 1
fi

WORK="$OUT/$LABEL"
rm -rf "$WORK"
mkdir -p "$WORK"

# name | extra args
SCENARIOS=(
  "basic|--count 40 --bates-prefix PROD --volume-size 7 --seed 42"
  "eml|--type eml --count 30 --attachment-rate 40 --bates-prefix PROD --seed 42"
  "mixed|--types pdf:50,eml:30,tiff:20 --count 40 --bates-prefix PROD --seed 7"
  "metadata|--count 30 --bates-prefix PROD --with-metadata --with-text --seed 3"
  "profile|--count 25 --bates-prefix PROD --load-file-format dat --column-profile litigation --seed 11"
  "rolling|--count 20 --bates-prefix PROD --rolling-count 2 --rolling-bates-mode restart --seed 5"
  "redacted|--count 20 --bates-prefix PROD --redacted-production --with-text --seed 9"
  "loadfile|--loadfile-only --count 500 --column-profile standard --seed 42"
)

for entry in "${SCENARIOS[@]}"; do
    name="${entry%%|*}"
    args="${entry#*|}"
    dir="$WORK/$name"
    mkdir -p "$dir"
    # shellcheck disable=SC2086
    "$ZIPPER" --production-set $args --output-path "$dir" >/dev/null 2>&1 || true
done

# Hash every produced file, relative to the scenario root so absolute paths and
# generated timestamp names do not leak into the manifest.
MANIFEST="$OUT/$LABEL.manifest"
: > "$MANIFEST"
while IFS= read -r f; do
    # _manifest.json and _validation_report.json embed a clock-derived Production ID,
    # so their raw bytes can never match across runs; they are hashed once, normalised,
    # by the block below.
    case "$(basename "$f")" in
        _manifest.json|_validation_report.json) continue ;;
    esac
    rel="${f#"$WORK"/}"
    # The generated Production directory is named PRODUCTION_<yyyyMMdd_HHmmss>;
    # normalise it so the manifest is timestamp-independent.
    rel="$(printf '%s' "$rel" | sed -E 's#PRODUCTION_[0-9]{8}_[0-9]{6}#PRODUCTION_<TS>#g')"
    printf '%s  %s\n' "$(sha256sum "$f" | cut -d' ' -f1)" "$rel" >> "$MANIFEST"
done < <(find "$WORK" -type f | sort)

# _manifest.json and _validation_report.json embed a generation timestamp, so
# their volatile fields are normalised before hashing; the validator refactor
# under test must not change any other byte of them.
python3 - "$WORK" >> "$MANIFEST" <<'PY'
import hashlib, json, os, re, sys
root = sys.argv[1]
for dirpath, _, files in os.walk(root):
    for name in sorted(files):
        if name not in ("_manifest.json", "_validation_report.json"):
            continue
        p = os.path.join(dirpath, name)
        raw = open(p, "rb").read()
        try:
            doc = json.loads(raw)
        except Exception:
            continue
        # Drop only the wall-clock / auto-id fields; every other byte must hash
        # identically. The auto-generated Production ID is derived from the clock,
        # so it is normalised the same way the path is.
        for key in ("productionDate", "productionId", "generationTime",
                    "generatedAt", "timestamp", "durationSeconds"):
            doc.pop(key, None)
        meta = doc.get("metadata")
        if isinstance(meta, dict):
            for k in list(meta):
                if k == "production_id":
                    meta[k] = "PRODUCTION_<TS>"
        prod = doc.get("production")
        if isinstance(prod, dict) and "production_id" in prod:
            prod["production_id"] = "PRODUCTION_<TS>"
        blob = json.dumps(doc, sort_keys=True, separators=(",", ":")).encode()
        rel = re.sub(r"PRODUCTION_\d{8}_\d{6}", "PRODUCTION_<TS>", os.path.relpath(p, root))
        print("%s  %s::normalised" % (hashlib.sha256(blob).hexdigest(), rel))
PY

sort -o "$MANIFEST" "$MANIFEST"
echo "wrote $MANIFEST ($(wc -l < "$MANIFEST") entries)"
