#!/bin/bash
# Autonomous Runner Cron Wrapper
# Configures a clean shell environment for headless cron execution.

export HOME="${HOME:-/home/$(whoami)}"
export USER="${USER:-$(whoami)}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/dotnet}"

RUNNER_BASE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOGS_DIR="$RUNNER_BASE/logs"

export PATH="/home/linuxbrew/.linuxbrew/bin:$HOME/.opencode/bin:$HOME/.gemini/antigravity-cli/bin:$HOME/.local/bin:$DOTNET_ROOT:$HOME/bin:/usr/local/bin:/usr/bin:/bin:$PATH"

mkdir -p "$LOGS_DIR"
find "$LOGS_DIR" -type f -name "run_*.log" -mtime +7 -delete 2>/dev/null || true

TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
LOG_FILE="$LOGS_DIR/run_$TIMESTAMP.log"

echo "==== Starting Autonomous Runner Cron Run: $(date) ====" >> "$LOG_FILE"
/usr/bin/env python3 -u "$RUNNER_BASE/runner.py" "$@" >> "$LOG_FILE" 2>&1
RUNNER_EXIT_CODE=$?
echo "==== Finished Autonomous Runner Cron Run: $(date) with exit code $RUNNER_EXIT_CODE ====" >> "$LOG_FILE"
exit $RUNNER_EXIT_CODE
