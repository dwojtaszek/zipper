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
CRON_JOB="0 */6 * * * $WRAPPER_PATH"

if crontab -l 2>/dev/null | grep -F "$WRAPPER_PATH" &>/dev/null; then
    echo "Cron job already exists in crontab. Skipping installation."
else
    (crontab -l 2>/dev/null || true; echo "$CRON_JOB") | crontab -
    echo "Registered cron job to run every 6 hours successfully."
fi

echo "==== Installation Complete! ===="
echo "You can check the logs of your runs at $RUNNER_BASE/logs/"
