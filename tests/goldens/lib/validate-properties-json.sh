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
# Run ajv only when npx is available. Capture its exit code:
#   0  → JSON passed validation → exit 0 (done)
#   1  → JSON failed validation → exit 1 (error; do NOT fall through to jq)
#   127→ ajv-cli not installed  → fall through to jq fallback
if command -v npx >/dev/null 2>&1; then
    set +e
    npx --yes ajv-cli@5 validate -s "$SCHEMA_FILE" -d "$JSON_FILE" \
        --valid --errors=text 2>/dev/null
    AJV_EXIT=$?
    set -e
    if [[ $AJV_EXIT -eq 0 ]]; then
        # Validation passed
        echo "[validate-properties-json] OK (ajv): $JSON_FILE"
        exit 0
    elif [[ $AJV_EXIT -ne 127 ]]; then
        # ajv ran but found the JSON invalid; do not fall through to jq
        echo "[validate-properties-json] FAIL (ajv): $JSON_FILE" >&2
        exit 1
    fi
    # AJV_EXIT=127 → ajv binary unavailable; fall through to jq fallback
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
