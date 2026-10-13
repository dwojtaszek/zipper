#!/usr/bin/env bash
# REQ-033, REQ-034, REQ-035: execute only the supplied shipped binary.
set -euo pipefail
source "$(dirname "$0")/_zipper-cli.sh"
python3 "$(dirname "$0")/published_artifact.py"
