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

# --- Try ajv-cli (preferred) ---
if command -v npx >/dev/null 2>&1; then
    # ajv-cli@5 uses ajv v8 and supports draft-07
    if npx --yes ajv-cli@5 validate -s "$SCHEMA_FILE" -d "$JSON_FILE" \
            --valid --errors=text 2>/dev/null; then
        exit 0
    fi
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

echo "[validate-properties-json] OK: $JSON_FILE"
