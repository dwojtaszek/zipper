#!/bin/bash
# 24h runner monitor — shows latest events and watches for new logs
# Run: bash .zipper-runner/monitor.sh

RUNNER_BASE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$RUNNER_BASE/.." && pwd)"
LOGS_DIR="$RUNNER_BASE/logs"
WORKTREES_DIR="$REPO_DIR/.worktrees"

echo "=== Zipper Runner Monitor ==="
echo "Started: $(date)"
echo ""

# Show latest log summaries (last 6 runs)
echo "=== Recent Activity ==="
latest_logs=$(ls -t "$LOGS_DIR"/*.log 2>/dev/null | head -6)
for log in $latest_logs; do
    ts=$(basename "$log" .log | sed 's/run_//' | sed 's/\(....\)\(..\)\(..\)_\(..\)\(..\)\(..\)/\1-\2-\3 \4:\5:\6/')
    events=$(grep -E "(Selected eligible|PR #.*(Created|Merged)|Started:|SUCCESS|FAILED|Error|FATAL|CRITICAL|Token Exhaustion|exceed|at capacity)" "$log" 2>/dev/null | tail -3 | paste -sd '; ')
    echo "  $ts | $events"
done

echo ""
echo "=== Active Worktrees ==="
if [ -d "$WORKTREES_DIR" ]; then
    for wt in "$WORKTREES_DIR"/issue-*; do
        if [ -d "$wt" ]; then
            branch=$(cd "$wt" 2>/dev/null && git branch --show-current 2>/dev/null)
            echo "  $(basename $wt) | branch: $branch"
        fi
    done
fi
if ! ls "$WORKTREES_DIR"/issue-* 2>/dev/null >/dev/null; then
    echo "  None"
fi

echo ""
echo "=== Last Log Tail ==="
latest=$(ls -t "$LOGS_DIR"/*.log 2>/dev/null | head -1)
if [ -n "$latest" ]; then
    grep -vE "^(Running command:|CLEANUP:|\[model discovery\]|\[select\]|WARNING:|INFO:|\[opencode\]|\[agy\]|\[claude\]|\[droid\])" "$latest" | tail -20
fi

echo ""
echo "=== Watching for new logs (every 30s, Ctrl+C to stop) ==="
echo ""
last_count=$(ls "$LOGS_DIR"/*.log 2>/dev/null | wc -l)
while true; do
    sleep 30
    current_count=$(ls "$LOGS_DIR"/*.log 2>/dev/null | wc -l)
    if [ "$current_count" -gt "$last_count" ]; then
        new_logs=$(ls -t "$LOGS_DIR"/*.log 2>/dev/null | head -$((current_count - last_count)))
        for log in $new_logs; do
            ts=$(basename "$log" .log | sed 's/run_//' | sed 's/\(....\)\(..\)\(..\)_\(..\)\(..\)\(..\)/\1-\2-\3 \4:\5:\6/')
            echo "=== NEW LOG: $ts ==="
            grep -vE "^(Running command:|CLEANUP:|\[model discovery\]|\[select\]|WARNING:|INFO:|\[opencode\]|\[agy\]|\[claude\]|\[droid\])" "$log" | grep -v "^$"
            echo "---"
        done
        last_count=$current_count
    fi
done
