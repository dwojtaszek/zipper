#!/usr/bin/env bash
# validate-req-traceability.sh — verify every active REQ-NNN and REQ_E-NNN in Requirements.md
# is mapped to a test or explicit exemption in req-traceability.tsv, that all
# test references resolve offline to real executable test cases, and that
# no requirement cross-reference points to an undefined ID.
#
# Usage:
#   validate-req-traceability.sh [--strict]
#
#   --strict   Fail on any unmapped active requirement, orphaned reference,
#              missing/ambiguous test reference, malformed manifest row, or
#              unexplained exemption (CI mode).
#              Without --strict, report only (local development).
#
# Exit codes:
#   0  all active requirements mapped or exempted, valid references
#   1  traceability or reference integrity defects remain (--strict)
#   2  missing files / parse errors

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RESOLVER="$REPO_ROOT/tools/typesafe-audit/checks/traceability/validate.py"
PYTHON_BIN="${PYTHON:-python3}"

if [[ ! -f "$RESOLVER" ]]; then
    echo "Error: Traceability validator not found at $RESOLVER" >&2
    exit 2
fi

exec "$PYTHON_BIN" "$RESOLVER" --repo-root "$REPO_ROOT" "$@"
