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
# The runner reads runner.py once at process start, so replacing the file while a
# run is in flight cannot affect that run. What must not happen is two git
# operations mutating this checkout at the same time, so this script refuses to
# act whenever the checkout is mid-operation, dirty, or locked by another git
# process, and retries on the next tick instead.
#
# It never discards work: no reset, no checkout, no stash, no clean. If the
# checkout is not a clean fast-forward, the script does nothing and says why.
#
# Known limitation, and why this is not the whole answer: the checks below and the
# merge are not atomic with respect to the runner's own git operations on this
# checkout. `git merge` tolerates local edits to files the incoming commit does
# not touch (measured), so edits created between the clean check and the merge
# would not stop the fast-forward — they survive it, but the checkout moves while
# it is not clean, which the promise above says cannot happen. Excluding the runner
# needs a lock shared around main-repo mutations, and the runner currently holds
# one lock for a whole agent mission, so sharing it here would stall every deploy
# for the 20-42 minutes a mission takes — the very delay this script removes. The
# window closes when the runner's lock is narrowed to main-repo mutations, and
# this script should then take that lock.
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
# The lock must not sit in the checkout. `_repo_is_clean()` counts untracked
# files as dirt, so a stray lock in REPO_PATH makes the runner believe its own
# checkout is dirty and changes how it recovers a failed sync. Next to the
# script, it is host-local state and outside the tree being deployed.
DEPLOY_LOCK="${DEPLOY_LOCK_PATH:-$RUNNER_BASE/.deploy.lock}"
REMOTE_REF="${MAIN_REMOTE_REF:-origin/main}"
# Shorter than the tick interval would be pointless (nothing would retry sooner);
# long enough to survive a slow handshake, short enough to not stall the timer.
FETCH_TIMEOUT="${DEPLOY_FETCH_TIMEOUT:-45}"

mkdir -p "$LOGS_DIR"
LOG_FILE="$LOGS_DIR/deploy_$(date +%Y%m%d_%H%M%S)_$$.log"
exec >> "$LOG_FILE" 2>&1

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

cd "$REPO_PATH" || { log "SKIP: cannot enter $REPO_PATH."; exit 0; }
if ! git rev-parse --git-dir >/dev/null 2>&1; then
    log "SKIP: $REPO_PATH is not a git checkout."
    exit 0
fi

# Serialize this script against itself. A previous tick still fetching would
# otherwise race the current one on the same index.
exec 9> "$DEPLOY_LOCK"
if ! flock -n 9; then
    log "SKIP: another deploy is still running."
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

# Bounded on purpose. This runs once a minute, so a fetch that blocks for minutes
# is worse than a fetch that fails: a transient network stall would hold the unit
# open and delay every deploy behind it, when the next tick would have retried
# sooner anyway. Measured on an unreachable origin: git stalls for ~300s.
if ! fetch_out="$(timeout "$FETCH_TIMEOUT" git fetch origin main 2>&1)"; then
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
