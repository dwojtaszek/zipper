#!/bin/bash
# Autonomous Runner Cron Wrapper
# Configures a clean shell environment for headless cron execution.

export HOME="${HOME:-/home/$(whoami)}"
export USER="${USER:-$(whoami)}"

RUNNER_BASE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOGS_DIR="$RUNNER_BASE/logs"
cd "$RUNNER_BASE/.." || exit 1

export PATH="/home/linuxbrew/.linuxbrew/bin:$HOME/.opencode/bin:$HOME/.gemini/antigravity-cli/bin:$HOME/.local/bin:${DOTNET_ROOT:-$HOME/.dotnet}:$HOME/bin:/usr/local/bin:/usr/bin:/bin:$PATH"

if [[ -z "${DOTNET_ROOT:-}" ]]; then
    RUNNER_DOTNET_EXE=$(command -v dotnet || true)
    if [[ -n "$RUNNER_DOTNET_EXE" ]]; then
        DOTNET_ROOT=$(python3 -c 'import os, sys; print(os.path.dirname(os.path.realpath(sys.argv[1])))' "$RUNNER_DOTNET_EXE")
        export DOTNET_ROOT
    fi
fi

mkdir -p "$LOGS_DIR"
find "$LOGS_DIR" -type f -name "run_*.log" -mtime +7 -delete 2>/dev/null || true

TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
# The intake and babysit timers can fire in the same minute, so a name built
# from the timestamp alone makes two processes append to one file: interleaved
# output, duplicated banners, and an exit line that reports the other run's
# duration. Mode and PID keep every invocation's log distinct.
MODE="intake"
for arg in "$@"; do
    case "$arg" in
        --babysit-only) MODE="babysit" ;;
        *) ;;  # any other flag leaves the mode alone
    esac
done
LOG_FILE="$LOGS_DIR/run_${TIMESTAMP}_${MODE}_$$.log"

echo "==== Starting Autonomous Runner Cron Run: $(date) ====" >> "$LOG_FILE"
/usr/bin/env python3 -u "$RUNNER_BASE/runner.py" "$@" >> "$LOG_FILE" 2>&1
RUNNER_EXIT_CODE=$?
echo "==== Finished Autonomous Runner Cron Run: $(date) with exit code $RUNNER_EXIT_CODE ====" >> "$LOG_FILE"
if [[ " $* " == *" --preflight "* ]]; then
    tail -n 40 "$LOG_FILE"
fi
exit $RUNNER_EXIT_CODE
