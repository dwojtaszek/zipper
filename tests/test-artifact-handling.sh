#!/bin/bash
set -euo pipefail

# Test script to validate artifact handling in current workflows (Unix wrapper)
# Invokes shared deterministic YAML-aware structural validation in workflow_validator.py (#1112)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "=== Artifact Handling Test (Unix) ==="
echo "Testing artifact creation and handling patterns"

python3 "$REPO_ROOT/tools/typesafe-audit/workflow_validator.py" --artifacts "$@"
