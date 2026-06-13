#!/bin/bash

# Exit immediately if a command exits with a non-zero status
set -euo pipefail

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/validator"
VALIDATOR_SCRIPT="./.agents/skills/autoreview/validators/line-validator.sh"

# --- Helper Functions ---

function print_success() {
  local msg="$1"
  echo -e "\e[42m[ SUCCESS ]\e[0m $msg"
}

function print_info() {
  local msg="$1"
  echo -e "\e[44m[ INFO ]\e[0m $msg"
}

function print_error() {
  local msg="$1"
  echo -e "\e[41m[ ERROR ]\e[0m $msg" >&2
  exit 1
}

# --- Test Setup ---

print_info "Running Autoreview Line Validator Test"

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# Create dummy file with 3 lines
printf "line1\nline2\nline3\n" > "$TEST_OUTPUT_DIR/dummy.ts"

# Create input JSON
cat <<EOF > "$TEST_OUTPUT_DIR/input.json"
[
  {
    "file": "$TEST_OUTPUT_DIR/dummy.ts",
    "line": 2,
    "finding": "valid finding"
  },
  {
    "file": "$TEST_OUTPUT_DIR/missing.ts",
    "line": 1,
    "finding": "invalid file"
  },
  {
    "file": "$TEST_OUTPUT_DIR/dummy.ts",
    "line": 10,
    "finding": "invalid line"
  }
]
EOF

chmod +x "$VALIDATOR_SCRIPT"

# --- Test Case 1: Filter Invalid Findings ---

print_info "Test Case 1: Filter invalid findings"

output=$("$VALIDATOR_SCRIPT" < "$TEST_OUTPUT_DIR/input.json")
valid_count=$(echo "$output" | jq 'length')

if [[ "$valid_count" -ne 1 ]]; then
  print_error "Test 1: Expected 1 valid finding, got $valid_count"
fi

valid_finding=$(echo "$output" | jq -r '.[0].finding')
if [[ "$valid_finding" != "valid finding" ]]; then
  print_error "Test 1: Expected 'valid finding', got '$valid_finding'"
fi

print_success "Test Case 1 passed"
print_success "All Line Validator tests passed!"
