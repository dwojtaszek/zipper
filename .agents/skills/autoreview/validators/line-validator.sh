#!/bin/bash

# Line/schema validator for autoreview findings
# Verifies that:
# 1. The file exists
# 2. The line number is valid (within the file's line count)
# Input: JSON array of findings from STDIN
# Output: JSON array of valid findings to STDOUT

set -euo pipefail

input=$(cat)

# If input is empty or just whitespace, output empty array
if [[ -z "${input// /}" ]]; then
    echo "[]"
    exit 0
fi

jq -c '.[]' <<< "$input" | while IFS= read -r finding; do
    IFS=$'\t' read -r file line < <(jq -r '[.file // "", .line // ""] | @tsv' <<< "$finding")

    if [[ -z "$file" || "$file" == "null" ]]; then
        # Missing file, keep it if it's a general repo finding?
        # The schema requires a file. Reject.
        continue
    fi

    # Check if file exists
    if [[ ! -f "$file" ]]; then
        continue
    fi

    # Check if line exists in file
    if [[ -n "$line" && "$line" =~ ^[0-9]+$ ]]; then
        # wc -l might return 0 for a 1-line file with no newline.
        # A safer way to count lines:
        max_line=$(awk 'END{print NR}' "$file")
        if (( line < 1 || line > max_line )); then
            continue
        fi
    fi

    echo "$finding"
done | jq -s '. // []'
