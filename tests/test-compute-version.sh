#!/usr/bin/env bash
# Tests compute-version.sh for both tag-push and main-push cases.
set -euo pipefail

SCRIPT="$(dirname "$0")/../.github/scripts/compute-version.sh"
PASS=0
FAIL=0

assert_eq() {
  local label="$1" expected="$2" actual="$3"
  if [[ "$expected" == "$actual" ]]; then
    echo "  PASS: $label"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $label — expected '$expected', got '$actual'"
    FAIL=$((FAIL + 1))
  fi
}

echo "== compute-version.sh tests =="

# Case 1: tag push — exact version from tag
echo "Testing tag push (refs/tags/v1.2.3)..."
output=$(GITHUB_REF=refs/tags/v1.2.3 bash "$SCRIPT" 2>&1)
version=$(echo "$output" | grep '^version=' | cut -d= -f2)
assert_eq "tag push version" "1.2.3" "$version"

# Case 2: main push — next patch with -dev suffix
# Uses the repo's actual latest tag, so we test the script logic
# without hardcoding a specific version. We verify the -dev suffix
# and that the base version is the latest tag bumped by one patch.
echo "Testing main push (refs/heads/main)..."
latest_tag=$(git tag -l 'v*' | sort -V | tail -1)
if [[ -z "$latest_tag" ]]; then
  latest_tag="v0.0.0"
fi
base="${latest_tag#v}"
IFS='.' read -r major minor patch <<< "$base"
expected="${major}.${minor}.$((patch + 1))-dev"

output=$(GITHUB_REF=refs/heads/main bash "$SCRIPT" 2>&1)
version=$(echo "$output" | grep '^version=' | cut -d= -f2)
assert_eq "main push version" "$expected" "$version"
assert_eq "main push has -dev suffix" "true" "$( [[ "$version" == *-dev ]] && echo true || echo false )"

# Case 3: empty GITHUB_REF — should fall through to main-push logic
echo "Testing empty ref..."
output=$(GITHUB_REF="" bash "$SCRIPT" 2>&1)
version=$(echo "$output" | grep '^version=' | cut -d= -f2)
assert_eq "empty ref version" "$expected" "$version"

echo ""
echo "Results: $PASS passed, $FAIL failed"
if [[ "$FAIL" -gt 0 ]]; then
  exit 1
fi
exit 0
