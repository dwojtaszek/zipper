#!/bin/bash
# Validate semantic version format (MAJOR.MINOR.BUILD)
# Usage: validate-version.sh <version_string>
# Returns: 0 if valid, 1 if invalid

set -euo pipefail

VERSION="${1:-}"

if [[ -z "$VERSION" ]]; then
  echo "Error: No version string provided" >&2
  exit 1
fi

# Check for semantic version format: X.Y.Z where X, Y, Z are non-negative integers
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Invalid version format: $VERSION" >&2
  echo "Expected format: MAJOR.MINOR.BUILD (e.g., 1.2.3)" >&2
  exit 1
fi

# Extract version components
IFS='.' read -r MAJOR MINOR BUILD <<< "$VERSION"

# Validate components are within reasonable bounds
if [[ "$MAJOR" -gt 999 ]] || [[ "$MINOR" -gt 999 ]] || [[ "$BUILD" -gt 9999 ]]; then
  echo "Warning: Version component exceeds reasonable bounds: $VERSION" >&2
fi

echo "Version valid: $VERSION"
exit 0
