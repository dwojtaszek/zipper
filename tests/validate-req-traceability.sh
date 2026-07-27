#!/usr/bin/env bash
# validate-req-traceability.sh — verify every active REQ-NNN in Requirements.md
# is mapped to a test or explicit exemption in req-traceability.tsv, and that
# no requirement cross-reference points to an undefined ID.
#
# Usage:
#   validate-req-traceability.sh [--strict]
#
#   --strict   Fail on any unmapped active requirement or orphaned reference
#              (CI mode). Without --strict, report only (local development).
#
# Exit codes:
#   0  all active requirements mapped or exempted, no orphan references
#   1  unmapped active requirements or orphaned references remain (--strict)
#   2  missing files / parse errors
#
# Manifest format (tab-separated, header row required):
#   req_id    coverage    reference    notes
#
#   coverage:  unit | e2e | exemption
#   reference: test class.method, file:scenario, or "-" for exemptions
#   notes:     free-text description (optional)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REQUIREMENTS="$SCRIPT_DIR/../Requirements.md"
MANIFEST="$SCRIPT_DIR/req-traceability.tsv"

STRICT=0
[[ "${1:-}" == "--strict" ]] && STRICT=1

if [[ ! -f "$REQUIREMENTS" ]]; then
    echo "Error: Requirements.md not found at $REQUIREMENTS" >&2
    exit 2
fi

if [[ ! -f "$MANIFEST" ]]; then
    echo "Error: req-traceability.tsv not found at $MANIFEST" >&2
    exit 2
fi

# --- Extract defined REQ-NNN IDs from Requirements.md ---
# Only count bullet-point definitions (e.g., "- **REQ-NNN**:"), not in-text
# references or HTML comment reservation markers.
mapfile -t ACTIVE_REQS < <(grep -oE '^\- \*\*REQ-[0-9]+\*\*' "$REQUIREMENTS" | grep -oE 'REQ-[0-9]+' | sort -u -t- -k2 -n)

if [[ ${#ACTIVE_REQS[@]} -eq 0 ]]; then
    echo "Error: no REQ-NNN IDs found in Requirements.md" >&2
    exit 2
fi

# --- Parse manifest ---
declare -A MANIFEST_ENTRIES
declare -A EXEMPTIONS
UNKNOWN_REFS=()
DUPLICATES=()
MANIFEST_COUNT=0

header_seen=0
while IFS=$'\t' read -r req_id coverage reference notes || [[ -n "$req_id" ]]; do
    [[ -z "$req_id" ]] && continue
    [[ "$req_id" == req_id ]] && header_seen=1 && continue  # skip header
    [[ $header_seen -eq 0 ]] && continue  # no header yet, skip

    # Validate REQ format
    if ! [[ "$req_id" =~ ^REQ-[0-9]+$ ]]; then
        echo "Warning: manifest entry has invalid REQ format: '$req_id'" >&2
        continue
    fi

    # Validate coverage type and reference consistency
    case "$coverage" in
        unit|e2e)
            if [[ -z "$reference" || "$reference" == "-" ]]; then
                echo "Error: $req_id requires a test reference (coverage=$coverage)" >&2
                exit 2
            fi
            ;;
        exemption)
            if [[ "$reference" != "-" ]]; then
                echo "Error: $req_id exemptions must use '-' as the reference" >&2
                exit 2
            fi
            ;;
        *)
            echo "Error: $req_id has invalid coverage type '$coverage'" >&2
            exit 2
            ;;
    esac

    MANIFEST_COUNT=$((MANIFEST_COUNT + 1))

    if [[ "$coverage" == "exemption" ]]; then
        EXEMPTIONS["$req_id"]=1
    fi

    if [[ -n "${MANIFEST_ENTRIES[$req_id]:-}" ]]; then
        DUPLICATES+=("$req_id")
    fi
    MANIFEST_ENTRIES["$req_id"]=1

    # Check for unknown references (in manifest but not in Requirements.md)
    if ! printf '%s\n' "${ACTIVE_REQS[@]}" | grep -qx "$req_id"; then
        UNKNOWN_REFS+=("$req_id")
    fi
done < "$MANIFEST"

# --- Report ---
UNMAPPED=()
MAPPED=0
EXEMPTED=0

for req in "${ACTIVE_REQS[@]}"; do
    if [[ -n "${MANIFEST_ENTRIES[$req]:-}" ]]; then
        if [[ -n "${EXEMPTIONS[$req]:-}" ]]; then
            EXEMPTED=$((EXEMPTED + 1))
        else
            MAPPED=$((MAPPED + 1))
        fi
    else
        UNMAPPED+=("$req")
    fi
done

TOTAL=${#ACTIVE_REQS[@]}
UNMAPPED_COUNT=${#UNMAPPED[@]}
UNKNOWN_COUNT=${#UNKNOWN_REFS[@]}
DUPLICATE_COUNT=${#DUPLICATES[@]}

echo "=== Requirement Traceability Report ==="
echo "Active requirements:  $TOTAL"
echo "Mapped to tests:      $MAPPED"
echo "Exempted:             $EXEMPTED"
echo "Unmapped:             $UNMAPPED_COUNT"
echo "Unknown references:   $UNKNOWN_COUNT"
echo "Duplicate entries:    $DUPLICATE_COUNT"
echo ""

if [[ $UNKNOWN_COUNT -gt 0 ]]; then
    echo "--- Unknown references (in manifest but not in Requirements.md) ---"
    printf '  %s\n' "${UNKNOWN_REFS[@]}" | sort -u -t- -k2 -n
    echo ""
fi

if [[ $DUPLICATE_COUNT -gt 0 ]]; then
    echo "--- Duplicate entries (same REQ mapped multiple times — informational) ---"
    printf '  %s\n' "${DUPLICATES[@]}" | sort -u -t- -k2 -n
    echo ""
fi

if [[ $UNMAPPED_COUNT -gt 0 ]]; then
    echo "--- Unmapped active requirements ---"
    printf '  %s\n' "${UNMAPPED[@]}" | sort -u -t- -k2 -n
    echo ""
fi

COVERAGE_PCT=$(( (MAPPED + EXEMPTED) * 100 / TOTAL ))
echo "Coverage: ${COVERAGE_PCT}% (${MAPPED} mapped + ${EXEMPTED} exempted = $((MAPPED + EXEMPTED))/$TOTAL)"

# --- Orphan reference check (#644) ---
# Verify that every REQ-NNN / REQ_E-NNN mentioned in the text resolves to a
# defined requirement (bullet-point definition) and is not just a dangling
# reference. HTML comment lines (reservation markers) are excluded.
mapfile -t DEFINED_IDS < <(grep -oE '^\- \*\*REQ_E?-[0-9]+\*\*' "$REQUIREMENTS" | grep -oE 'REQ_E?-[0-9]+' | sort -u)
mapfile -t ALL_MENTIONS < <(grep -v '^<!--' "$REQUIREMENTS" | grep -oE 'REQ_E?-[0-9]+' | sort -u)

declare -A DEFINED_SET
for id in "${DEFINED_IDS[@]}"; do
    DEFINED_SET["$id"]=1
done

ORPHAN_REFS=()
for id in "${ALL_MENTIONS[@]}"; do
    if [[ -z "${DEFINED_SET[$id]:-}" ]]; then
        ORPHAN_REFS+=("$id")
    fi
done

ORPHAN_COUNT=${#ORPHAN_REFS[@]}
echo "Orphan references:    $ORPHAN_COUNT"
echo ""

if [[ $ORPHAN_COUNT -gt 0 ]]; then
    echo "--- Orphan references (mentioned but never defined) ---"
    printf '  %s\n' "${ORPHAN_REFS[@]}" | sort -u
    echo ""
fi

if [[ $UNMAPPED_COUNT -gt 0 && $STRICT -eq 1 ]]; then
    echo ""
    echo "FAIL: $UNMAPPED_COUNT unmapped active requirements remain." >&2
    exit 1
fi

if [[ $ORPHAN_COUNT -gt 0 && $STRICT -eq 1 ]]; then
    echo "FAIL: $ORPHAN_COUNT orphaned requirement references found." >&2
    exit 1
fi

exit 0
