#!/bin/bash

# Review-wait gate — blocks until all expected robot reviewers have either
# reviewed the PR or declared a skip (rate limit), then reports every
# unresolved review thread.
#
# Usage:   bash tests/wait-for-reviews.sh <PR-number> [timeout-minutes]
# Env:     WAIT_FOR_REVIEWS_SKIP=<bot>[,<bot>...]  reviewers known to be
#          unavailable, which are not waited on at all (see below).
# Exit:    0 = all bots accounted for, no unresolved review threads
#          1 = unresolved review threads exist (fix or reply + resolve, re-run)
#          2 = usage error / missing prerequisites
#          3 = could not determine review state (API failure) — environmental,
#              not a review outcome
#
# A bot that stays silent past the timeout produces a warning, not a failure:
# branch protection (required conversation resolution) still blocks the merge
# if its threads appear later. The same reasoning is why exit 3 is distinct from
# exit 1: "the reviewers could not be asked" is not "the reviewers objected".
#
# Machine-readable contract for the runner: one `REVIEWER-OUTCOME <bot>=<state>`
# line per reviewer (states: reviewed, skipped, skipped-by-policy, silent) and a
# final `REVIEW-GATE-RESULT unresolved=<n>` line. The runner persists these to
# track reviewer availability so it stops waiting on a reviewer that is gone.
#
# Spec: docs/superpowers/specs/2026-06-10-review-wait-gate-design.md

set -euo pipefail

# --- Configuration ---

EXPECTED_BOTS=(
    "coderabbitai[bot]"
    "chatgpt-codex-connector[bot]"
)
# Matches both explicit skip declarations (rate limits, repo star threshold) and
# CodeRabbit's walkthrough-only responses, which carry no formal review object.
SKIP_PATTERN='usage limit|rate limit|review limit|Review skipped|skip review|Review available on request|Walkthrough|fewer than 10 stars|does not receive automatic reviews'
# Codex reports some finished reviews as a summary issue comment carrying a
# machine-readable completion marker instead of a formal review object. Without
# this the gate cannot see that review at all and waits out its whole timeout on a
# PR Codex has already finished — measured at 20 minutes on PR #1328.
CODEX_COMPLETION_PATTERN='codex-review-state:v1'
POLL_SECONDS=30

# Reviewers the caller already knows to be unavailable (a rate-limit window, a
# previous silent timeout). Waiting on one costs the whole timeout to learn what
# the caller already recorded, so they are reported as skipped-by-policy and the
# wait is skipped entirely.
SKIP_BOTS="${WAIT_FOR_REVIEWS_SKIP:-}"
is_skip_bot() {
    local bot="$1"
    [[ -n "$SKIP_BOTS" ]] || return 1
    local entry
    local IFS=','
    for entry in $SKIP_BOTS; do
        [[ "$entry" == "$bot" ]] && return 0
    done
    return 1
}

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

# Every gh call is bounded: the script's own deadline only fires between
# polls, so a single wedged TCP connection would otherwise block the script
# (and its runner caller) indefinitely. GNU timeout on Linux, gtimeout on
# macOS; unbounded only when neither exists (the runner-side run_cmd timeout
# still bounds the whole script there).
GH_CALL_TIMEOUT=60

with_timeout() {
    if command -v timeout >/dev/null 2>&1; then
        timeout "$GH_CALL_TIMEOUT" "$@"
    elif command -v gtimeout >/dev/null 2>&1; then
        gtimeout "$GH_CALL_TIMEOUT" "$@"
    else
        "$@"
    fi
}

if ! REPO=$(with_timeout gh repo view --json nameWithOwner --jq .nameWithOwner); then
    echo "Error: could not resolve repository (gh repo view failed or timed out)." >&2
    exit 2
fi
OWNER="${REPO%%/*}"
NAME="${REPO##*/}"

print_info()  { printf '\033[44m[ INFO ]\033[0m %s\n' "$1"; }
print_warn()  { printf '\033[43m[ WARN ]\033[0m %s\n' "$1"; }
print_error() { printf '\033[41m[ FAIL ]\033[0m %s\n' "$1"; }
print_ok()    { printf '\033[42m[ OK ]\033[0m %s\n' "$1"; }

# --- Phase 1: wait until every expected bot is accounted for ---

# Fetches reviews and issue comments once per polling cycle; individual bots
# are then checked against the local JSON to keep API usage flat.
#
# A failed fetch is recorded rather than silently read as "no reviews". That
# distinction is the whole point: an API outage that looked like an empty review
# list would make the gate wait out its timeout and then report every reviewer as
# silent, which reads as "the reviewers did not answer" when in fact nobody was
# asked. `FETCH_OK` counts the cycles that actually returned state.
reviews_json=""
comments_json=""
FETCH_OK=0
fetch_review_state() {
    local reviews comments
    if ! reviews=$(with_timeout gh api --paginate "repos/$REPO/pulls/$PR/reviews" 2>/dev/null) \
            || ! jq -s 'flatten' <<<"$reviews" >/dev/null 2>&1; then
        return 1
    fi
    if ! comments=$(with_timeout gh api --paginate "repos/$REPO/issues/$PR/comments" 2>/dev/null) \
            || ! jq -s 'flatten' <<<"$comments" >/dev/null 2>&1; then
        return 1
    fi
    reviews_json=$(jq -s 'flatten' <<<"$reviews")
    comments_json=$(jq -s 'flatten' <<<"$comments")
    FETCH_OK=$((FETCH_OK + 1))
    return 0
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
    # Reported a finished review through a completion marker rather than a review
    # object? Codex does this, so the marker is checked in both carriers: it posts
    # the marker as an issue comment and has also posted it as a review body.
    if jq -r --arg bot "$bot" '.[] | select(.user.login == $bot) | .body' <<<"$reviews_json" 2>/dev/null \
        | grep -qE "$CODEX_COMPLETION_PATTERN"; then
        return 0
    fi
    if jq -r --arg bot "$bot" '.[] | select(.user.login == $bot) | .body' <<<"$comments_json" 2>/dev/null \
        | grep -qE "$CODEX_COMPLETION_PATTERN"; then
        return 0
    fi
    return 1
}

print_info "Waiting for robot reviews on PR #$PR (timeout: ${TIMEOUT_MINUTES}m)..."

# Records each reviewer's outcome for the machine-readable contract. `silent`
# and `skipped-by-policy` are both non-blocking here, but they are different
# facts: the first means the reviewer was asked and did not answer, the second
# means it was not asked because the caller already knew it was unavailable.
#
# A newline-delimited string rather than `declare -A`: bot names such as
# `coderabbitai[bot]` are not valid associative-array keys on bash 3.2 (the stock
# macOS /bin/bash, where the runner suite runs), and an indexed assignment would
# evaluate the name arithmetically. The stress runner uses the same indexed-record
# shape for the same reason.
REVIEWER_OUTCOMES=""
set_outcome() { REVIEWER_OUTCOMES+="$1=$2"$'\n'; }
get_outcome() {
    local line result="unknown"
    while IFS= read -r line; do
        [[ "$line" == "$1="* ]] && result="${line#*=}"
    done <<<"$REVIEWER_OUTCOMES"
    printf '%s' "$result"
}
pending=()
for bot in "${EXPECTED_BOTS[@]}"; do
    if is_skip_bot "$bot"; then
        print_info "$bot is known unavailable; not waiting for it."
        set_outcome "$bot" "skipped-by-policy"
    else
        pending+=("$bot")
    fi
done
NEEDED_STATE=${#pending[@]}

while [[ ${#pending[@]} -gt 0 ]]; do
    if ! fetch_review_state; then
        print_warn "Could not read review state (gh api failed); retrying."
        if [[ $(date +%s) -ge $DEADLINE ]]; then
            break
        fi
        sleep "$POLL_SECONDS"
        continue
    fi
    still_pending=()
    for bot in "${pending[@]}"; do
        if bot_accounted_for "$bot"; then
            print_ok "$bot has reviewed or declared a skip."
            # Distinguish a real review from a declared skip: only the former is
            # evidence about the code, and only the latter is evidence the
            # reviewer is unavailable.
            if jq -e --arg bot "$bot" '.[] | select(.user.login == $bot)' <<<"$reviews_json" >/dev/null 2>&1; then
                set_outcome "$bot" "reviewed"
            elif jq -r --arg bot "$bot" '.[] | select(.user.login == $bot) | .body' <<<"$comments_json" 2>/dev/null \
                    | grep -qiE "$SKIP_PATTERN"; then
                set_outcome "$bot" "skipped"
            else
                set_outcome "$bot" "reviewed"
            fi
        else
            still_pending+=("$bot")
        fi
    done
    pending=("${still_pending[@]+"${still_pending[@]}"}")
    [[ ${#pending[@]} -eq 0 ]] && break
    if [[ $(date +%s) -ge $DEADLINE ]]; then
        for bot in "${pending[@]}"; do
            print_warn "$bot did not respond within ${TIMEOUT_MINUTES}m — proceeding without it."
            set_outcome "$bot" "silent"
        done
        break
    fi
    sleep "$POLL_SECONDS"
done

for bot in "${EXPECTED_BOTS[@]}"; do
    echo "REVIEWER-OUTCOME $bot=$(get_outcome "$bot")"
done

# Never fetched state at all: the reviewers were not asked, so this says nothing
# about the code. Reported as environmental (exit 3) rather than as a review
# result, so the runner retries instead of recording the reviewers as silent.
if [[ $NEEDED_STATE -gt 0 && $FETCH_OK -eq 0 ]]; then
    echo "REVIEW-GATE-RESULT unresolved=unknown"
    print_error "Could not read review state for PR #$PR (gh api failed every attempt)."
    exit 3
fi

# --- Phase 2: report unresolved review threads ---

# shellcheck disable=SC2016  # $vars below are GraphQL variables, not shell
if ! threads_json=$(with_timeout gh api graphql \
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
    }'); then
    echo "REVIEW-GATE-RESULT unresolved=unknown"
    print_error "Could not read review threads for PR #$PR (gh api graphql failed)."
    exit 3
fi

# An unreadable or unexpected response must not be read as "no threads". The
# query is the only thing that proves the threads are resolved, so a response
# that does not actually contain the thread list is an environmental failure:
# without this, a transient API error would report zero unresolved threads and
# let the PR merge unverified.
if ! jq -e '.data.repository.pullRequest.reviewThreads.nodes' <<<"$threads_json" >/dev/null 2>&1; then
    echo "REVIEW-GATE-RESULT unresolved=unknown"
    print_error "Review-thread response for PR #$PR was not readable (unexpected GraphQL shape)."
    exit 3
fi

unresolved=$(jq '[.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved | not)]' <<<"$threads_json")
unresolved_count=$(jq 'length' <<<"$unresolved")
echo "REVIEW-GATE-RESULT unresolved=$unresolved_count"

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
