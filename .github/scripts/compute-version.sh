#!/usr/bin/env bash
# Computes the build version for CI.
#
# Tag push (refs/tags/v*):  exact tag version, e.g. v1.2.3 → 1.2.3
# Main-branch push:        next patch from latest tag with -dev suffix,
#                          e.g. latest tag v1.2.3 → 1.2.4-dev
#
# Usage: GITHUB_REF=refs/tags/v1.2.3 bash compute-version.sh
# Output: writes "version=X.Y.Z[-dev]" to GITHUB_OUTPUT (or stdout if unset)
set -euo pipefail

ref="${GITHUB_REF:-}"

if [[ "$ref" == refs/tags/v* ]]; then
  version="${ref#refs/tags/v}"
  echo "Version from tag: $version"
else
  latest_tag=$(git tag -l 'v*' | sort -V | tail -1)
  if [[ -z "$latest_tag" ]]; then
    latest_tag="v0.0.0"
  fi
  base="${latest_tag#v}"
  IFS='.' read -r major minor patch <<< "$base"
  new_patch=$((patch + 1))
  version="$major.$minor.$new_patch-dev"
  echo "Version from bump: $version (latest tag: $latest_tag)"
fi

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "version=$version" >> "$GITHUB_OUTPUT"
else
  echo "version=$version"
fi
