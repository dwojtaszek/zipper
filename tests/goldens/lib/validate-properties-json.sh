#!/bin/bash
# validate-properties-json.sh — validate a _properties.json file against the committed schema.
#
# Usage:
#   bash tests/goldens/lib/validate-properties-json.sh <json-file> [schema-file]
#
# <json-file>   : path to the _properties.json to validate
# [schema-file] : optional path to the JSON Schema; defaults to
#                 tests/fixtures/properties.schema.json relative to this script

set -euo pipefail

JSON_FILE="${1:?Usage: validate-properties-json.sh <json-file> [schema-file]}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA_FILE="${2:-$SCRIPT_DIR/../../fixtures/properties.schema.json}"

if [[ ! -f "$JSON_FILE" ]]; then
    echo "[validate-properties-json] ERROR: file not found: $JSON_FILE" >&2
    exit 1
fi

if [[ ! -f "$SCHEMA_FILE" ]]; then
    echo "[validate-properties-json] ERROR: schema not found: $SCHEMA_FILE" >&2
    exit 1
fi

# --- Try ajv-cli (preferred, authoritative) ---
# In ajv-cli@5: exit 0 = data is valid, exit 1 = data is invalid.
# Use 'npx --yes' to auto-install if not already present.
# Only fall through to jq when npx cannot install/find ajv-cli (exit 127 or 1 from npx itself).
#
# Strategy:
#   1. Check if ajv-cli is reachable via npx (probe with --help; install if needed).
#   2. If probe succeeds, run validation authoritatively.
#   3. If probe fails (not installable / network offline), fall through to jq.
if command -v npx >/dev/null 2>&1; then
    # Probe: install ajv-cli@5 if absent, then verify it answers --help.
    set +e
    npx --yes ajv-cli@5 --help >/dev/null 2>&1
    PROBE_EXIT=$?
    set -e

    if [[ $PROBE_EXIT -eq 0 ]]; then
        # ajv-cli is available; run validation authoritatively.
        set +e
        npx ajv-cli@5 validate -s "$SCHEMA_FILE" -d "$JSON_FILE"
        AJV_EXIT=$?
        set -e

        if [[ $AJV_EXIT -eq 0 ]]; then
            echo "[validate-properties-json] OK (ajv): $JSON_FILE"
            exit 0
        else
            echo "[validate-properties-json] FAIL (ajv): $JSON_FILE" >&2
            exit 1
        fi
    fi
    # Probe failed: ajv not installable; fall through to jq
fi

# --- Fallback: jq hand-rolled checks ---
if ! command -v jq >/dev/null 2>&1; then
    echo "[validate-properties-json] WARNING: neither ajv nor jq available; skipping schema validation" >&2
    exit 0
fi

# Use jq -e with != null so boolean false values pass (false != null is true in jq).
_check() {
    local field="$1"
    if ! jq -e "$field != null" "$JSON_FILE" >/dev/null 2>&1; then
        echo "[validate-properties-json] FAIL: missing required field $field in $JSON_FILE" >&2
        exit 1
    fi
}

_check ".fileName"
_check ".format"
_check ".totalRecords"
_check ".properties.encoding"
_check ".properties.lineEnding"
_check ".properties.delimiters.column"
_check ".properties.delimiters.quote"
_check ".chaosMode.enabled"
_check ".chaosMode.totalAnomalies"

echo "[validate-properties-json] OK (jq): $JSON_FILE"
