#!/bin/bash
# Agy Autonomous Runner Cron Setup Script
set -e

RUNNER_BASE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WRAPPER_PATH="$RUNNER_BASE/cron-wrapper.sh"
RUNNER_PATH="$RUNNER_BASE/runner.py"

echo "==== Configuring Permissions ===="
chmod +x "$RUNNER_PATH"
chmod +x "$WRAPPER_PATH"
echo "Scripts set as executable."

echo "==== Verifying Dependencies ===="
export PATH="$HOME/.opencode/bin:$HOME/.gemini/antigravity-cli/bin:$HOME/.local/bin:$HOME/dotnet:$PATH"

if ! command -v python3 &>/dev/null; then
    echo "ERROR: python3 is not installed or not in PATH."
    exit 1
fi

if ! command -v gh &>/dev/null; then
    echo "ERROR: gh (GitHub CLI) is not installed or not in PATH."
    exit 1
fi

if ! command -v opencode &>/dev/null; then
    echo "ERROR: opencode is not installed or not in PATH."
    exit 1
fi

if ! command -v msmtp &>/dev/null; then
    echo "WARNING: msmtp is not installed or not in PATH. Please install it and configure ~/.msmtprc for email notifications."
fi

echo "Dependency checks completed."

echo "==== Installing Cron Job ===="
# Fetch existing crontab
CRON_JOB="*/15 * * * * $WRAPPER_PATH"
LEGACY_JOB="0 */6 * * * $WRAPPER_PATH"
BABYSIT_JOB="*/5 * * * * $WRAPPER_PATH --babysit-only"
CURRENT_CRON="$(crontab -l 2>/dev/null || true)"

has_runner_job() {
    local wanted="$1"
    printf '%s\n' "$CURRENT_CRON" | awk -v wrapper="$WRAPPER_PATH" -v wanted="$wanted" '
        /^[[:space:]]*(#|$)/ {next}
        {
            cmd = ($1 ~ /^@/) ? 2 : 6
            if ($cmd != wrapper) next
            babysit = 0
            for (i = cmd + 1; i <= NF; i++) {
                if ($i ~ /^#/) break
                if ($i == "--babysit-only") babysit = 1
            }
            if (babysit == wanted) found = 1
        }
        END {exit !found}'
}

if printf '%s\n' "$CURRENT_CRON" | grep -Fx "$LEGACY_JOB" &>/dev/null; then
    CURRENT_CRON="$(printf '%s\n' "$CURRENT_CRON" | awk -v old="$LEGACY_JOB" -v new="$CRON_JOB" '$0 == old {$0 = new} {print}')"
    echo "Migrated legacy six-hour intake to every 15 minutes."
elif has_runner_job 0; then
    echo "Existing intake schedule preserved."
else
    CURRENT_CRON="${CURRENT_CRON}${CURRENT_CRON:+$'\n'}$CRON_JOB"
    echo "Registered intake every 15 minutes."
fi

if ! has_runner_job 1; then
    CURRENT_CRON="${CURRENT_CRON}${CURRENT_CRON:+$'\n'}$BABYSIT_JOB"
    echo "Registered babysitting every 5 minutes."
fi
printf '%s\n' "$CURRENT_CRON" | crontab -

echo "==== Installation Complete! ===="
echo "You can check the logs of your runs at $RUNNER_BASE/logs/"
