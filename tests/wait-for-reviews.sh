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
#          4 = a current-head review is active; retry
#          3 = could not determine review state (API failure) — environmental,
#              not a review outcome
#
# A bot that stays silent past the timeout produces a warning, not a failure:
# branch protection (required conversation resolution) still blocks the merge
# if its threads appear later. The same reasoning is why exit 3 is distinct from
# exit 1: "the reviewers could not be asked" is not "the reviewers objected".
#
# Machine-readable contract for the runner: one `REVIEWER-OUTCOME <bot>=<state>`
# line per reviewer (states: reviewed, skipped, skipped-by-policy, silent, pending) and a
# final `REVIEW-GATE-RESULT unresolved=<n>` line. The runner persists these to
# track reviewer availability so it stops waiting on a reviewer that is gone.
#
# Spec: docs/superpowers/specs/2026-06-10-review-wait-gate-design.md

set -euo pipefail

# --- Configuration ---

EXPECTED_BOTS=(
    "coderabbitai[bot]"
)
# Matches both explicit skip declarations (rate limits, repo star threshold) and
# CodeRabbit's walkthrough-only responses, which carry no formal review object.
SKIP_PATTERN='usage limit|rate limit|review limit|Review skipped|skip review|Review available on request|Walkthrough|fewer than 10 stars|does not receive automatic reviews'
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

read_head() {
    with_timeout gh pr view "$PR" --json headRefOid --jq .headRefOid
}
if ! REVIEW_HEAD=$(read_head) || [[ ! "$REVIEW_HEAD" =~ ^[0-9a-f]{40}$ ]]; then
    print_error "Could not read the current PR head."
    exit 3
fi
REVIEW_PENDING=0
if [[ -n "${STATE_DIR:-}" ]]; then
    REVIEW_WAIT_DIR="$STATE_DIR/review-waits"
else
    REVIEW_WAIT_DIR=$(git rev-parse --git-path zipper-hooks/review-waits) || exit 3
fi
MAX_ACTIVE_WAIT_SECONDS=1200

# First observation is shared across quick passes and survives runner restarts.
active_window_expired() {
    local path="$REVIEW_WAIT_DIR/$PR-$REVIEW_HEAD-$1.started" first temporary
    if [[ ! -f "$path" ]]; then
        [[ "${WAIT_FOR_REVIEWS_DRY_RUN:-0}" == "1" ]] && return 1
        mkdir -p "$REVIEW_WAIT_DIR" || exit 3
        temporary=$(mktemp "$REVIEW_WAIT_DIR/.started.XXXXXX") || exit 3
        date +%s > "$temporary" || { rm -f "$temporary"; exit 3; }
        # A hard link publishes a complete timestamp without overwriting another observer.
        ln "$temporary" "$path" 2>/dev/null || true
        rm -f "$temporary"
    fi
    [[ -f "$path" ]] || exit 3
    first=$(<"$path")
    [[ "$first" =~ ^[0-9]{1,12}$ ]] || exit 3
    (( $(date +%s) - 10#$first >= MAX_ACTIVE_WAIT_SECONDS ))
}

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
    local reviews comments head
    if ! head=$(read_head); then
        return 1
    fi
    if [[ "$head" != "$REVIEW_HEAD" ]]; then
        print_error "PR head changed; retry the review gate."
        exit 3
    fi
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

# Keep paginated bodies on stdin: one argv string is limited to ~128 KiB on Linux.
bot_query() {
    local status=0 bot="$1" expression="$2"
    printf '%s\n%s\n' "$reviews_json" "$comments_json" | jq -se \
      --arg bot "$bot" --arg head "$REVIEW_HEAD" --arg skip "$SKIP_PATTERN" \
      '.[0] as $reviews | .[1] as $comments | '"$expression" >/dev/null 2>&1 || status=$?
    if [[ "$status" -gt 1 ]]; then
        print_error "Review data could not be evaluated; retry the gate."
        exit 3
    fi
    return "$status"
}

bot_has_reviewed() {
    local bot="$1"
    bot_query "$bot" '
      any($reviews[]; .user.login == $bot and .commit_id == $head
        and (.state == "APPROVED" or .state == "COMMENTED" or .state == "CHANGES_REQUESTED"))
    '
}

bot_declared_skip() {
    local bot="$1"
    bot_query "$bot" '
      def response_time: .updated_at // .submitted_at // .created_at // "";
      any($comments[]; . as $skip_comment |
        .user.login == $bot and ((.body // "") | test($skip; "i"))
        and (((.body // "") | contains($head))
          or ((.body // "") | test("fewer than 10 stars|does not receive automatic reviews"; "i")))
        and all($reviews[] | select(.user.login == $bot and .commit_id == $head);
          response_time != "" and (response_time < ($skip_comment.created_at // "")
            or (response_time == ($skip_comment.created_at // "")
              and (.id // 0) < ($skip_comment.id // 0)))))'
}

bot_has_current_activity() {
    local bot="$1"
    bot_query "$bot" '
      any($reviews[]; .user.login == $bot and .commit_id == $head and .state == "PENDING")
    '
}

bot_accounted_for() {
    local bot="$1"
    if bot_has_reviewed "$bot"; then return 0; fi
    bot_declared_skip "$bot"
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
            if bot_has_reviewed "$bot"; then
                set_outcome "$bot" "reviewed"
                if [[ "${WAIT_FOR_REVIEWS_DRY_RUN:-0}" != "1" ]]; then
                    rm -f "$REVIEW_WAIT_DIR/$PR-$REVIEW_HEAD-$bot.started"
                fi
            else
                set_outcome "$bot" "skipped"
            fi
        else
            if bot_has_current_activity "$bot" && active_window_expired "$bot"; then
                print_warn "$bot did not complete the current-head review within 20m; proceeding without review credit."
                set_outcome "$bot" "silent"
            else
                still_pending+=("$bot")
            fi
        fi
    done
    pending=("${still_pending[@]+"${still_pending[@]}"}")
    [[ ${#pending[@]} -eq 0 ]] && break
    if [[ $(date +%s) -ge $DEADLINE ]]; then
        for bot in "${pending[@]}"; do
            if bot_has_current_activity "$bot"; then
                print_warn "$bot has not completed a review of $REVIEW_HEAD; retry later."
                set_outcome "$bot" "pending"
                REVIEW_PENDING=1
            else
                print_warn "$bot did not respond within ${TIMEOUT_MINUTES}m — proceeding without it."
                set_outcome "$bot" "silent"
            fi
        done
        break
    fi
    sleep "$POLL_SECONDS"
done

for bot in "${EXPECTED_BOTS[@]}"; do
    echo "REVIEWER-OUTCOME $bot=$(get_outcome "$bot")"
done

UNKNOWN_RESULT_LINE="REVIEW-GATE-RESULT unresolved=unknown"
ALLOW_QODO_ACK="${WAIT_FOR_REVIEWS_ALLOW_QODO_ACK:-1}"
# Never fetched state at all: the reviewers were not asked, so this says nothing
# about the code. Reported as environmental (exit 3) rather than as a review
# result, so the runner retries instead of recording the reviewers as silent.
if [[ $NEEDED_STATE -gt 0 && $FETCH_OK -eq 0 ]]; then
    echo "$UNKNOWN_RESULT_LINE"
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
          comments(first: 100) {
            pageInfo { hasNextPage endCursor }
            nodes {
              author { login }
              authorAssociation
              createdAt
              updatedAt
              body
            }
          }
        }
      }
    }'); then
    echo "$UNKNOWN_RESULT_LINE"
    print_error "Could not read review threads for PR #$PR (gh api graphql failed)."
    exit 3
fi

# An unreadable, partial-error, or unexpected response must not be read as
# "no threads / no comments". Without rejecting `.errors` and verifying both
# arrays and pagination metadata, a partial GraphQL failure could drop Qodo
# comments and let a PR merge unverified.
if ! jq -e '((.errors // []) | length == 0)
  and (.data.repository.pullRequest.reviewThreads.nodes | type == "array")
  and (.data.repository.pullRequest.comments.nodes | type == "array")
  and (.data.repository.pullRequest.comments.pageInfo.hasNextPage | type == "boolean")' <<<"$threads_json" >/dev/null 2>&1; then
    echo "$UNKNOWN_RESULT_LINE"
    print_error "Review-thread response for PR #$PR was not readable (unexpected GraphQL shape or errors)."
    exit 3
fi

all_comments_json=$(jq '.data.repository.pullRequest.comments.nodes' <<<"$threads_json")
has_next_comments=$(jq -r '.data.repository.pullRequest.comments.pageInfo.hasNextPage' <<<"$threads_json")
comments_cursor=$(jq -r '(.data.repository.pullRequest.comments.pageInfo.endCursor // "")' <<<"$threads_json")
while [[ "$has_next_comments" == "true" ]]; do
    if [[ -z "$comments_cursor" ]]; then
        echo "$UNKNOWN_RESULT_LINE"
        print_error "Incomplete comment pagination cursor for PR #$PR."
        exit 3
    fi
    # shellcheck disable=SC2016
    if ! comments_page=$(with_timeout gh api graphql \
        -f owner="$OWNER" -f name="$NAME" -F pr="$PR" -f after="$comments_cursor" -f query='
        query($owner: String!, $name: String!, $pr: Int!, $after: String!) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $pr) {
              comments(first: 100, after: $after) {
                pageInfo { hasNextPage endCursor }
                nodes {
                  author { login }
                  authorAssociation
                  createdAt
                  updatedAt
                  body
                }
              }
            }
          }
        }'); then
        echo "$UNKNOWN_RESULT_LINE"
        print_error "Could not paginate PR #$PR comments (gh api graphql failed)."
        exit 3
    fi
    if ! jq -e '((.errors // []) | length == 0)
      and (.data.repository.pullRequest.comments.nodes | type == "array")
      and (.data.repository.pullRequest.comments.pageInfo.hasNextPage | type == "boolean")' <<<"$comments_page" >/dev/null 2>&1; then
        echo "$UNKNOWN_RESULT_LINE"
        print_error "Comment pagination response for PR #$PR was unreadable."
        exit 3
    fi
    next_comments_cursor=$(jq -r '(.data.repository.pullRequest.comments.pageInfo.endCursor // "")' <<<"$comments_page")
    has_next_comments=$(jq -r '.data.repository.pullRequest.comments.pageInfo.hasNextPage' <<<"$comments_page")
    if [[ "$has_next_comments" == "true" && ( -z "$next_comments_cursor" || "$next_comments_cursor" == "$comments_cursor" ) ]]; then
        echo "$UNKNOWN_RESULT_LINE"
        print_error "Comment pagination did not advance for PR #$PR."
        exit 3
    fi
    all_comments_json=$(jq -s '.[0] + .[1]' <(printf '%s\n' "$all_comments_json") <(jq '.data.repository.pullRequest.comments.nodes' <<<"$comments_page"))
    comments_cursor="$next_comments_cursor"
done

if ! FINAL_HEAD=$(read_head) || [[ "$FINAL_HEAD" != "$REVIEW_HEAD" ]]; then
    print_error "PR head changed or could not be verified; retry the review gate."
    exit 3
fi

unresolved=$(jq '[.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved | not)]' <<<"$threads_json")
unresolved_threads_count=$(jq 'length' <<<"$unresolved")

# Qodo posts its Code Review as an issue-level PR comment (not a GraphQL
# reviewThread). Flag unacknowledged current-head Qodo findings so they block
# merge alongside CodeRabbit / human review threads.
# Only repository OWNER acknowledgements with explicit finding directives
# (`qodo-ack: <sha> #1, #2` or `qodo-ack: <sha> all`) can dismiss findings,
# and only when WAIT_FOR_REVIEWS_ALLOW_QODO_ACK=1 (runner cycles set it to 0).
# Intentional: Qodo is checked opportunistically when present (not in EXPECTED_BOTS)
# because it skips some force-pushes and would otherwise stall every repair cycle.
short_head="${REVIEW_HEAD:0:7}"
qodo_unresolved_json=$(jq --arg head "$REVIEW_HEAD" --arg owner "$OWNER" --arg allow_ack "$ALLOW_QODO_ACK" '
  . as $comments
  | [range(0; $comments | length) as $idx
      | ($comments[$idx]) as $c
      | select((($c.author.login // "") | test("^qodo-code-review(\\[bot\\])?$"))
          and (($c.body // "") | contains("<h3>Code Review by Qodo</h3>"))
          and (($c.body // "") | contains($head)))
      | {index: $idx, comment: $c}] as $qodo_matches
  | if ($qodo_matches | length) == 0 then {count: 0, summaries: []}
    else
      ($qodo_matches | max_by([(.comment.updatedAt // .comment.createdAt // ""), .index])) as $latest
      | ($latest.comment.body // "") as $body
      | if ($body | test("Great, no issues found!"; "i"))
           or (($body | test("Action required|Remediation recommended|Agent prompt"; "i")) | not)
        then {count: 0, summaries: []}
        else
          ([$body | scan("<summary>\\s*\\d+\\.[^<]*(?:<code>[^<]*</code>[^<]*)*</summary>")]
            | map(gsub("<[^>]+>"; "") | gsub("^\\s+|\\s+$"; ""))) as $summaries
          | [range(0; $summaries | length) as $i
              | {
                  num: (($summaries[$i] | capture("^(?<n>\\d+)\\.") | .n) // (($i + 1) | tostring)),
                  text: $summaries[$i]
                }] as $items
          | (if ($items | length) > 0 then $items else [{num: "1", text: "Qodo reported current-head findings"}] end) as $findings
          | ($latest.comment.updatedAt // $latest.comment.createdAt // "") as $qodo_ts
          | (if $allow_ack == "1" then
              [range(0; $comments | length) as $j
                | ($comments[$j]) as $ack
                | ($ack.updatedAt // $ack.createdAt // "") as $ack_ts
                | select((($ack.author.login // "") == $owner or ($ack.authorAssociation // "") == "OWNER")
                    and (if ($qodo_ts != "" and $ack_ts != "" and $ack_ts != $qodo_ts)
                         then ($ack_ts > $qodo_ts)
                         else ($j > $latest.index) end))
                | (($ack.body // "")
                    | gsub("```[\\s\\S]*?```"; "")
                    | gsub("~~~[\\s\\S]*?~~~"; "")
                    | split("\n")
                    | map(select(test("^\\s*>") | not))
                    | join("\n"))
                | scan("(?im)^ {0,3}qodo-(?:ack|skip)[ \\t]*:[ \\t]*([0-9a-f]{7,40})[ \\t]+(all\\b|#?\\d+(?:(?:[ \\t]*,[ \\t]*|[ \\t]+)#?\\d+)*)")
                | (.[0] | ascii_downcase) as $ack_sha
                | select($head | startswith($ack_sha))
                | .[1]]
             else [] end) as $ack_specs
          | ($ack_specs | any(test("\\ball\\b"; "i"))) as $ack_all
          | ([$ack_specs[] | scan("#?(\\d+)")] | flatten) as $acked_nums
          | (if $ack_all then [] else [$findings[] | select(.num as $n | ($acked_nums | index($n)) | not)] end) as $remaining
          | {count: ($remaining | length), summaries: [$remaining[].text]}
        end
    end
' <<<"$all_comments_json")
qodo_count=$(jq '.count' <<<"$qodo_unresolved_json")
unresolved_count=$((unresolved_threads_count + qodo_count))
echo "REVIEW-GATE-RESULT unresolved=$unresolved_count"

# Non-empty review summary bodies (verdicts, overviews) — informational.
print_info "Review summaries:"
jq -r '.[] | select(.body != null and .body != "") | "  \(.user.login) [\(.state)]: \(.body | split("\n")[0])"' <<<"$reviews_json" || true

if [[ "$unresolved_count" -eq 0 ]]; then
    if [[ "$REVIEW_PENDING" -eq 1 ]]; then
        print_warn "Review is pending for the current head; merge deferred."
        exit 4
    fi
    print_ok "No unresolved review threads on PR #$PR."
    exit 0
fi

if [[ "$unresolved_threads_count" -gt 0 ]]; then
    print_error "$unresolved_threads_count unresolved review thread(s) on PR #$PR:"
    jq -r '.[] | (.comments.nodes[0] // {}) | "  \(.author.login // "unknown") \(.path // "?"):\(.line // "?")\n    \([(.body // "") | split("\n")[] | select((. != "") and (startswith("![") | not))][0] // "")"' <<<"$unresolved"
    print_info "Fix each finding, or reply with a skip reason and resolve the thread, then re-run."
fi
if [[ "$qodo_count" -gt 0 ]]; then
    print_error "$qodo_count unaddressed Qodo finding(s) on PR #$PR ($short_head):"
    jq -r '.summaries[] | "  qodo-code-review: \(.)"' <<<"$qodo_unresolved_json"
    print_info "Address each valid Qodo finding and push an updated commit."
fi
exit 1
