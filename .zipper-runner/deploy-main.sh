#!/bin/bash
# Fast-forward the runner's main checkout to origin/main.
#
# Why this is separate from the runner: the runner only syncs its checkout while
# it holds its lock, and it holds that lock for the whole of a long agent
# mission. A merged change therefore sat undeployed for as long as the next
# lock-winning run took to arrive, which is tens of minutes. Deploying is a
# fast-forward of a checkout that is supposed to sit on main and stay clean, so
# it does not need to wait for a run: it needs to not collide with one.
#
# The collision is real and not theoretical. The runner mutates this same
# checkout (`_cleanup_git_state`, `sync_main_repo`, `_sync_main_after_merge`), and
# `_cleanup_git_state` deletes `.git/index.lock` without checking its age, so a run
# starting mid-merge would remove the lock an active git process is holding and
# then run `git rebase --abort` on the same tree. Both sides therefore take the
# same lock around checkout mutations: this script takes it for the whole run
# (non-blocking, so a held lock costs one tick), and the runner takes it only
# around its mutations, never across an agent mission. That is what makes the
# deploy prompt without making it unsafe.
#
# The lock is a directory, not `flock`, because `flock` and `timeout` are GNU
# utilities that macOS does not ship, and this repository's runner suite runs on
# macOS in CI. `mkdir` is atomic everywhere.
#
# It never discards work: no reset, no checkout, no stash, no clean. If the
# checkout is not a clean fast-forward, the script does nothing and says why.
#
# Install this OUTSIDE the checkout it deploys, and set REPO_PATH. A copy living
# inside the tree is an untracked file in the very commit that adds it, so the
# first fast-forward that carries the file would refuse to run and deploy nothing:
#
#   install -Dm755 .zipper-runner/deploy-main.sh ~/.local/bin/zipper-deploy-main.sh
#
# The default below only works for a copy run straight out of the checkout, which
# is fine for a manual run and wrong for an installed one.

set -uo pipefail

RUNNER_BASE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -n "${REPO_PATH:-}" ]; then
    :
elif [ -d "$RUNNER_BASE/../.git" ] || [ -f "$RUNNER_BASE/../.git" ]; then
    REPO_PATH="$(cd "$RUNNER_BASE/.." && pwd)"
else
    echo "deploy-main.sh: set REPO_PATH to the checkout to deploy." >&2
    exit 0
fi
LOGS_DIR="${DEPLOY_LOGS_DIR:-$REPO_PATH/.zipper-runner/logs}"
# Shared with the runner, and inside the checkout because that is where the
# runner's own lock already lives. It is gitignored for the same reason
# `runner.lock` is: `_repo_is_clean()` counts untracked files as dirt, so an
# unignored lock here would make the runner believe its own checkout is dirty.
#
# This default must resolve to the same path as the runner's
# `REPO_MUTATION_LOCK_PATH`, whose default is `$RUNNER_BASE/repo-mutation.lock`
# (RUNNER_BASE being the directory holding runner.py). The two agree while the
# runner runs from `<checkout>/.zipper-runner`. An installation that overrides
# RUNNER_BASE to a directory outside the checkout must set
# REPO_MUTATION_LOCK_PATH on both sides, or the two stop sharing a lock and the
# protection disappears with no error.
REPO_LOCK="${REPO_MUTATION_LOCK_PATH:-$REPO_PATH/.zipper-runner/repo-mutation.lock}"
REMOTE_REF="${MAIN_REMOTE_REF:-origin/main}"
# Shorter than the tick interval would be pointless (nothing would retry sooner);
# long enough to survive a slow handshake, short enough to not stall the timer.
FETCH_TIMEOUT="${DEPLOY_FETCH_TIMEOUT:-45}"
# Longer than the runner's own slowest single mutation (DEFAULT_CMD_TIMEOUT, 300s,
# which bounds every locked command), so a legitimately slow mutation is never
# mistaken for a crashed one and stolen.
LOCK_STALE_SECONDS="${DEPLOY_LOCK_STALE_SECONDS:-1200}"

mkdir -p "$LOGS_DIR"
LOG_FILE="$LOGS_DIR/deploy_$(date +%Y%m%d_%H%M%S)_$$.log"
exec >> "$LOG_FILE" 2>&1

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

now_epoch() { date +%s; }

# Set only once this process actually holds the lock. The EXIT trap is installed
# before the lock is attempted, so an unguarded release would delete a lock this
# process does not hold — including the runner's, on the very skip path that
# exists to leave it alone. That would hand the lock to the next tick while the
# runner is still mid-mutation, which is the hazard the lock was added to remove.
HOLDS_LOCK=0
# Unique per process, so a release can tell "still mine" from "someone stole it".
LOCK_TOKEN="$$-$RANDOM-$RANDOM"

# Whether the lock records a holder pid that is no longer running. A holder killed
# without its cleanup (SIGKILL, OOM, reboot) leaves the lock behind, and age alone
# would keep this timer out for the whole stale bound — a full 20 minutes of missed
# deploys. A recorded pid that is not alive is proof the holder is gone.
#
# Requires /proc, because `kill -0` alone cannot separate a dead process from a
# live one this user may not signal. Where /proc is absent (macOS) this reports
# nothing and the age bound decides, rather than risking a live holder's lock.
lock_holder_pid_is_gone() {
    local pid
    [ -d /proc ] || return 1
    pid="$(cat "$REPO_LOCK/pid" 2>/dev/null || true)"
    case "$pid" in
        ''|*[!0-9]*) return 1 ;;   # absent or malformed: not proof of anything
    esac
    [ "$pid" = "$$" ] && return 1
    kill -0 "$pid" 2>/dev/null && return 1
    [ -d "/proc/$pid" ] && return 1
    return 0
}

# Seconds the current holder has held the lock, or empty when that cannot be told.
# The holder's own `created` file is authoritative; the directory mtime is the
# fallback for a lock whose holder died between mkdir and writing it — without
# that fallback the runner's own lock could never be reclaimed by the shell.
lock_age_seconds() {
    local created mtime
    created="$(cat "$REPO_LOCK/created" 2>/dev/null || true)"
    if [ -n "$created" ]; then
        echo "$(( $(now_epoch) - created ))"
        return 0
    fi
    # stat -c is GNU, stat -f is BSD/macOS; both are exercised by the runner suite.
    mtime="$(stat -c %Y "$REPO_LOCK" 2>/dev/null || stat -f %m "$REPO_LOCK" 2>/dev/null || true)"
    [ -n "$mtime" ] && echo "$(( $(now_epoch) - mtime ))"
}

# Take the shared main-checkout lock, or report why not. Non-blocking on purpose:
# waiting would hold this unit open behind a runner mutation, and the next tick
# is a minute away.
acquire_repo_lock() {
    if mkdir "$REPO_LOCK" 2>/dev/null; then
        now_epoch > "$REPO_LOCK/created"
        echo "$$" > "$REPO_LOCK/pid"
        echo "$LOCK_TOKEN" > "$REPO_LOCK/owner"
        HOLDS_LOCK=1
        return 0
    fi
    if lock_holder_pid_is_gone; then
        log "WARN: reclaiming the main-checkout lock; its recorded holder is gone."
        rm -rf "$REPO_LOCK"
        if mkdir "$REPO_LOCK" 2>/dev/null; then
            now_epoch > "$REPO_LOCK/created"
            echo "$$" > "$REPO_LOCK/pid"
            echo "$LOCK_TOKEN" > "$REPO_LOCK/owner"
            HOLDS_LOCK=1
            return 0
        fi
        return 1
    fi
    age="$(lock_age_seconds)"
    if [ -n "$age" ] && [ "$age" -gt "$LOCK_STALE_SECONDS" ]; then
        log "WARN: stealing a stale main-checkout lock (age ${age}s)."
        rm -rf "$REPO_LOCK"
        if mkdir "$REPO_LOCK" 2>/dev/null; then
            now_epoch > "$REPO_LOCK/created"
            echo "$$" > "$REPO_LOCK/pid"
            echo "$LOCK_TOKEN" > "$REPO_LOCK/owner"
            HOLDS_LOCK=1
            return 0
        fi
    fi
    return 1
}

release_repo_lock() {
    [ "$HOLDS_LOCK" = 1 ] || return 0
    # Only remove the lock while this process is still its recorded owner. After a
    # stale steal the lock belongs to another process, and removing it here would
    # let a third one in while the thief is mid-merge.
    if [ "$(cat "$REPO_LOCK/owner" 2>/dev/null || true)" != "$LOCK_TOKEN" ]; then
        log "WARN: lock was reclaimed by another process; leaving it alone."
        return 0
    fi
    rm -rf "$REPO_LOCK" 2>/dev/null || true
}
trap release_repo_lock EXIT

cd "$REPO_PATH" || { log "SKIP: cannot enter $REPO_PATH."; exit 0; }
if ! git rev-parse --git-dir >/dev/null 2>&1; then
    log "SKIP: $REPO_PATH is not a git checkout."
    exit 0
fi

# Everything below runs under the lock: the state checks are only meaningful if
# nothing else can mutate the checkout between them and the merge.
if ! acquire_repo_lock; then
    log "SKIP: the main checkout is locked by the runner or another deploy."
    exit 0
fi

branch="$(git branch --show-current 2>/dev/null)"
if [ "$branch" != "main" ]; then
    log "SKIP: checkout is on '$branch', not main. The runner will recover it."
    exit 0
fi

# A rebase, merge, or cherry-pick in progress means the runner is mid-recovery.
# Interfering would leave the checkout in a state neither process expects.
git_dir="$(git rev-parse --git-dir 2>/dev/null)"
for marker in rebase-merge rebase-apply MERGE_HEAD CHERRY_PICK_HEAD REVERT_HEAD; do
    if [ -e "$git_dir/$marker" ]; then
        log "SKIP: git operation in progress ($marker)."
        exit 0
    fi
done

# index.lock means another git process is mutating this checkout right now.
if [ -e "$git_dir/index.lock" ]; then
    log "SKIP: index.lock held by another git process."
    exit 0
fi

# Only tracked modifications matter. Untracked files are left alone: if the
# merge would overwrite one, git refuses and we log that instead of deleting it.
if ! git diff --quiet || ! git diff --cached --quiet; then
    log "SKIP: checkout has uncommitted changes to tracked files."
    exit 0
fi

before="$(git rev-parse HEAD 2>/dev/null)"

# The fetch is bounded on every host. `timeout`/`gtimeout` is used where it exists;
# where neither does — macOS, which this repository's runner suite runs on in CI —
# the fetch runs in the background with a watchdog that kills it. The watchdog is
# used rather than `perl -e 'alarm shift; exec @ARGV'`, which looks equivalent but
# is not: the alarm survives the exec only until the exec'd program is itself a
# shell script, and then the signal is deferred until that script's foreground
# child finishes, so the bound silently does nothing (measured: a 2s bound on a
# 30s hang returned after 30s).
#
# Git's own low-speed abort is the third, weakest layer: it covers the measured
# failure (a stalled transfer, ~300s) but only for HTTP(S) remotes, so it is a
# backstop, not the bound. This runs once a minute, so a fetch that blocks for
# minutes is worse than a fetch that fails: the next tick retries.
FETCH_CMD=(git -c "http.lowSpeedLimit=1" -c "http.lowSpeedTime=$FETCH_TIMEOUT" fetch origin main)

fetch_out_file="$(mktemp)"
fetch_rc=1
if command -v timeout >/dev/null 2>&1; then
    timeout "$FETCH_TIMEOUT" "${FETCH_CMD[@]}" > "$fetch_out_file" 2>&1
    fetch_rc=$?
elif command -v gtimeout >/dev/null 2>&1; then
    gtimeout "$FETCH_TIMEOUT" "${FETCH_CMD[@]}" > "$fetch_out_file" 2>&1
    fetch_rc=$?
else
    "${FETCH_CMD[@]}" > "$fetch_out_file" 2>&1 &
    fetch_pid=$!
    ( sleep "$FETCH_TIMEOUT"; kill -TERM "$fetch_pid" 2>/dev/null ) &
    watchdog_pid=$!
    wait "$fetch_pid"
    fetch_rc=$?
    kill "$watchdog_pid" 2>/dev/null || true
    wait "$watchdog_pid" 2>/dev/null || true
fi
fetch_out="$(cat "$fetch_out_file")"
rm -f "$fetch_out_file"

if [ "$fetch_rc" -ne 0 ]; then
    log "SKIP: git fetch failed or exceeded ${FETCH_TIMEOUT}s: $fetch_out"
    exit 0
fi

if ! git rev-parse --verify --quiet "$REMOTE_REF" >/dev/null; then
    log "SKIP: $REMOTE_REF not found after fetch."
    exit 0
fi

# Only a checkout strictly behind the remote is a deploy, and `--ff-only` is what
# enforces it: it refuses a diverged checkout instead of creating a merge commit.
# A checkout that is *ahead* (local commits on main) also differs from the remote,
# but there `--ff-only` succeeds as a no-op, so the moved-nothing check below
# keeps it quiet instead of logging a DEPLOYED line every minute.
#
# `--no-overwrite-ignore` is not decoration. The tracked-file check above cannot
# see ignored files, and git overwrites them during a merge by default — measured:
# an ignored file was replaced by the incoming tracked one. That is how a deploy
# could destroy the runner's own ignored `runner.lock` or its logs if upstream
# ever started tracking a colliding path. Refusing and staying behind is the
# correct trade against silently deleting a local file.
if [ "$before" = "$(git rev-parse "$REMOTE_REF")" ]; then
    exit 0  # already current; stay quiet
fi

if ! merge_out="$(git merge --ff-only --no-overwrite-ignore "$REMOTE_REF" 2>&1)"; then
    log "SKIP: not a fast-forward, leaving the checkout alone: $merge_out"
    exit 0
fi

after="$(git rev-parse HEAD 2>/dev/null)"
if [ "$after" = "$before" ]; then
    exit 0  # the merge moved nothing; a DEPLOYED line here would be a lie
fi
log "DEPLOYED: ${before:0:8} -> ${after:0:8} ($(git log -1 --format=%s "$after"))"
exit 0
