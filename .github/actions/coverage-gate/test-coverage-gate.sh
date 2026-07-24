#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Testing check-per-file-coverage.py against passing fixture..."
python3 "${SCRIPT_DIR}/check-per-file-coverage.py" \
  --reports "${SCRIPT_DIR}/testdata/passing_coverage.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing check-per-file-coverage.py against failing fixture..."
set +e
python3 "${SCRIPT_DIR}/check-per-file-coverage.py" \
  --reports "${SCRIPT_DIR}/testdata/failing_coverage.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20
EXIT_CODE=$?
set -e

if [ "${EXIT_CODE}" -ne 1 ]; then
  echo "ERROR: Expected exit code 1 for failing fixture, got ${EXIT_CODE}"
  exit 1
fi

echo "[SUCCESS] All coverage gate script tests passed!"
