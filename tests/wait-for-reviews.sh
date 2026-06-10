#!/bin/bash

# Review-wait gate — blocks until all expected robot reviewers have either
# reviewed the PR or declared a skip (rate limit), then reports every
# unresolved review thread.
#
# Usage:   bash tests/wait-for-reviews.sh <PR-number> [timeout-minutes]
# Exit:    0 = all bots accounted for, no unresolved review threads
#          1 = unresolved review threads exist (fix or reply + resolve, re-run)
#          2 = usage error / missing prerequisites
#
# A bot that stays silent past the timeout produces a warning, not a failure:
# branch protection (required conversation resolution) still blocks the merge
# if its threads appear later.
#
# Spec: docs/superpowers/specs/2026-06-10-review-wait-gate-design.md

set -euo pipefail

# --- Configuration ---

EXPECTED_BOTS=(
    "gemini-code-assist[bot]"
    "coderabbitai[bot]"
    "chatgpt-codex-connector[bot]"
)
# Matches both explicit skip declarations (rate limits) and CodeRabbit's
# walkthrough-only responses, which carry no formal review object.
SKIP_PATTERN='usage limit|rate limit|review limit|Review skipped|Walkthrough'
POLL_SECONDS=30

# --- Prerequisites ---

for tool in gh jq; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "Error: required tool '$tool' is not installed." >&2
        exit 2
    fi
done

# --- Arguments ---

if [[ $# -lt 1 ]]; then
    echo "Usage: bash tests/wait-for-reviews.sh <PR-number> [timeout-minutes]" >&2
    exit 2
fi
PR="$1"
TIMEOUT_MINUTES="${2:-20}"
if ! [[ "$PR" =~ ^[0-9]+$ ]] || ! [[ "$TIMEOUT_MINUTES" =~ ^[0-9]+$ ]]; then
    echo "Error: PR number and timeout-minutes must be positive integers." >&2
    exit 2
fi
DEADLINE=$(( $(date +%s) + TIMEOUT_MINUTES * 60 ))

REPO=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
OWNER="${REPO%%/*}"
NAME="${REPO##*/}"

print_info()  { printf '\033[44m[ INFO ]\033[0m %s\n' "$1"; }
print_warn()  { printf '\033[43m[ WARN ]\033[0m %s\n' "$1"; }
print_error() { printf '\033[41m[ FAIL ]\033[0m %s\n' "$1"; }
print_ok()    { printf '\033[42m[ OK ]\033[0m %s\n' "$1"; }

# --- Phase 1: wait until every expected bot is accounted for ---

# Fetches reviews and issue comments once per polling cycle; individual bots
# are then checked against the local JSON to keep API usage flat.
reviews_json=""
comments_json=""
fetch_review_state() {
    reviews_json=$(gh api "repos/$REPO/pulls/$PR/reviews" --paginate --slurp 2>/dev/null \
        | jq '[.[][]]' 2>/dev/null) || reviews_json="[]"
    comments_json=$(gh api "repos/$REPO/issues/$PR/comments" --paginate --slurp 2>/dev/null \
        | jq '[.[][]]' 2>/dev/null) || comments_json="[]"
}

bot_accounted_for() {
    local bot="$1"
    # Posted a review?
    if jq -e --arg bot "$bot" '.[] | select(.user.login == $bot)' <<<"$reviews_json" >/dev/null 2>&1; then
        return 0
    fi
    # Declared a skip / rate limit / walkthrough in an issue comment?
    if jq -r --arg bot "$bot" '.[] | select(.user.login == $bot) | .body' <<<"$comments_json" 2>/dev/null \
        | grep -qiE "$SKIP_PATTERN"; then
        return 0
    fi
    return 1
}

print_info "Waiting for robot reviews on PR #$PR (timeout: ${TIMEOUT_MINUTES}m)..."
pending=("${EXPECTED_BOTS[@]}")
while [[ ${#pending[@]} -gt 0 ]]; do
    fetch_review_state
    still_pending=()
    for bot in "${pending[@]}"; do
        if bot_accounted_for "$bot"; then
            print_ok "$bot has reviewed or declared a skip."
        else
            still_pending+=("$bot")
        fi
    done
    pending=("${still_pending[@]+"${still_pending[@]}"}")
    [[ ${#pending[@]} -eq 0 ]] && break
    if [[ $(date +%s) -ge $DEADLINE ]]; then
        for bot in "${pending[@]}"; do
            print_warn "$bot did not respond within ${TIMEOUT_MINUTES}m — proceeding without it."
        done
        break
    fi
    sleep "$POLL_SECONDS"
done

# --- Phase 2: report unresolved review threads ---

# shellcheck disable=SC2016  # $vars below are GraphQL variables, not shell
threads_json=$(gh api graphql \
    -f owner="$OWNER" -f name="$NAME" -F pr="$PR" -f query='
    query($owner: String!, $name: String!, $pr: Int!) {
      repository(owner: $owner, name: $name) {
        pullRequest(number: $pr) {
          reviewThreads(first: 100) {
            nodes {
              isResolved
              comments(first: 1) {
                nodes { author { login } path line body }
              }
            }
          }
        }
      }
    }')

unresolved=$(jq '[.data?.repository?.pullRequest?.reviewThreads?.nodes? // [] | .[] | select(.isResolved | not)]' <<<"$threads_json")
unresolved_count=$(jq 'length' <<<"$unresolved")

# Non-empty review summary bodies (verdicts, overviews) — informational.
print_info "Review summaries:"
jq -r '.[] | select(.body != null and .body != "") | "  \(.user.login) [\(.state)]: \(.body | split("\n")[0])"' <<<"$reviews_json" || true

if [[ "$unresolved_count" -eq 0 ]]; then
    print_ok "No unresolved review threads on PR #$PR."
    exit 0
fi

print_error "$unresolved_count unresolved review thread(s) on PR #$PR:"
jq -r '.[] | (.comments.nodes[0] // {}) | "  \(.author.login // "unknown") \(.path // "?"):\(.line // "?")\n    \([(.body // "") | split("\n")[] | select((. != "") and (startswith("![") | not))][0] // "")"' <<<"$unresolved"
print_info "Fix each finding, or reply with a skip reason and resolve the thread, then re-run."
exit 1
