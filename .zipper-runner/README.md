# Zipper Autonomous Runner

This directory contains the autonomous runner pipeline that manages GitHub issues, triggers the AI agent, and creates pull requests.

## Operating contract

Run from the repository root:

```bash
.zipper-runner/cron-wrapper.sh --agent droid
.zipper-runner/cron-wrapper.sh --babysit-only
python3 .zipper-runner/runner.py --status --agent droid
```

`--agent` prefers that agent for this run only, ahead of other priority tiers. Disabled models (priority 0) and unhealthy agents remain excluded; normal fallbacks remain available. It does not edit `AGENT_PREFERENCES.md` or timers. Configuration precedence is command-line agent preference, process environment, ignored `.env`, then defaults. Existing environment values, including empty values, are preserved.

Agent health probes run only when a coding mission is needed, or during `--status`; idle babysitting and green-PR merge evaluation need no agent credits. `--dry-run` skips health calls. Babysit-only, capacity reached, and no eligible issue each have distinct logs; only issue directories count toward capacity.

After successful intake, the runner immediately evaluates the PR through the existing CI, WIP, local CodeRabbit, and robot-review gates. Pending checks remain pending, not failures. CodeRabbit's successful zero-finding review may be reused within the same runner process only for a clean, unchanged `HEAD` and `main`; a changed commit or base requires another review. After a merge, clean local `main` fast-forwards immediately. Dirty, divergent, or non-main repositories are preserved and sync is deferred.

Automatic actions additionally fail closed on PR authorization. Active, orphan, and Dependabot merge paths share one guard: an open, non-draft PR into this repository's `main`, a same-repository head, and a well-formed head SHA. Runner-created work records the PR number and branch in its issue state and requires a `TRUSTED_AUTHORS` author; authorization is bound to that PR, not to a frozen SHA, so a head that moved past the recorded `authorized_head` stays authorized. The maintainer is the sole merge maintainer and GitHub forbids approving your own pull request, so demanding an unchanged head would strand their own PRs after any rebase or force-push. Any other origin needs an explicit `APPROVED` review of the current head from `MERGE_MAINTAINERS` (default `dwojtaszek`, separate from intake authors). Forks are never automatically merged or recovered. Dependabot is identified by REST account ID `49699333`, login `dependabot[bot]`, and account type `Bot`, never a `dependabot/` branch prefix. Every contributing commit must have verified signed bot authorship and a bot or GitHub web-flow committer; the commit listing must end at the current head. Mixed, unsigned, or changed bot heads require exact-head maintainer approval. Orphan recovery requires an issue reference and the same authorization before fetching/creating a worktree. Existing local branches are not replaced.

After review, the runner rechecks authorization, head, and successful CI, then merges with `--match-head-commit` and without `--admin`. Branch protection remains authoritative. A head that moved after review is refused and re-evaluated on the new head; green CI alone never authorizes execution or merge.

A rewritten remote head is handled by the recorded `authorized_head`, not by authorization: when that record no longer names the reviewed head, the worktree's local tip is the head the rewrite abandoned rather than an unpublished completion, so the runner pins any local-only commit on a backup branch and blocks for a human, or, when there is no local-only work and the tree is clean, follows the rewrite. Reading the abandoned tip as unpublished instead would push the content straight back over a force-push. A record that stores no head (a legacy record, or one whose local branch no longer resolved when it was written) cannot tell the two apart, because a head the runner published and a head a rewrite abandoned look identical locally; that case resolves toward inaction, so the worktree resumes through the agent and nothing is republished.

A PR the runner cannot authorize — it was converted to a draft, its base was retargeted off `main`, its head branch was renamed, or its provenance is unverifiable — keeps the runner's single worktree slot and is never acted on: `_authorized_pr` fails closed, and no repair missions are dispatched for it. The runner sends a rate-limited email naming the concrete reason, the recorded head, the current head, and the remediation that works for that reason. The email is deliberately not sent when the PR simply could not be read, so a `gh` outage cannot raise a false alarm, and a contributor PR that merely mentions an issue number is suppressed by the record check. Deleting the issue state is never offered as a fix: provenance is read from that state, so removing it would leave the PR unauthorized permanently. A PR the runner holds work for but has no record of — state never written, or the record belongs to a different PR — is reported the same way instead of being skipped silently.

A green PR the runner has no authorization record for is never evaluated by the orphan scanner, so nothing would ever merge it. Two shapes hit this: a PR whose branch, title, and body carry no issue reference (PR #1258 sat green and unmerged for four days), and a PR that names an issue the runner never recorded work for. After `RUNNER_STALLED_PR_HOURS` (12 default) the runner sends a rate-limited email naming the PR and the remediation. It never merges such a PR on its own: an unverifiable origin stays fail-closed.

The orphan scanner can also stall on a PR it *can* authorize: when the review gate is unsatisfied, a merge keeps failing, or CI is red, it recreates a worktree to babysit the PR. If that recreation fails — a local branch parked at a different head, a fetch mismatch, or a failed worktree add — the PR used to be skipped every cycle with no signal. It now sends a rate-limited `Cannot Be Babied` email naming the PR and branch, so the operator can reconcile the local branch by hand. A successful recreation stays silent, because the runner then babysits the PR normally.

An open PR with a WIP checkpoint resumes its existing worktree through the normal agent completion path, once per worktree per invocation. Failed continuations preserve uncommitted work; dry-run never dispatches a continuation. All existing-PR repairs validate and commit locally without pushing or importing another remote PR head. The runner checks the captured authorized remote head, a clean descendant completion, and the committed local CodeRabbit gate, then rechecks authorization immediately before publication. It publishes that exact completion SHA using an atomic lease against the captured remote SHA. The ancestry check permits only fast-forwards; the lease does not authorize history replacement. Provenance advances and a prior blocker clears only after that publication succeeds. Failed review or publication preserves the local commit and records the blocker for a later retry. Initial runner-created PR provenance records the published PR SHA, not an unpublished local checkpoint. Interrupted mission progress retains a separate `local_head` with `completion_ready: false`; it resumes through the agent without discarding dirty edits. Only publication requires a clean tree. Initial review failure exits intake, so further repair uses the captured-head babysitter instead of the old push-enabled intake prompt. A local-only commit is not PR progress. A completion commit must represent actual finished work, not a renamed checkpoint or empty commit. Readiness still depends on existing CI and reviews, with advisory Jev verification; a tree diff alone cannot prove semantic completion. Progress checks take read-only thread snapshots; the full robot-review wait remains a merge gate, not a delay before or after each repair.

Droid uses `stream-json`, drains stdout and stderr concurrently, and logs assistant-progress markers plus a 60-second heartbeat. Prompts, message text, tool arguments, and results are not echoed into live logs. The plugin still returns `(exit_code, stdout, stderr)`, with stdout containing final assistant text (partial assistant messages on interrupted runs), not raw tool payloads. The 45-minute mission timeout still terminates the process group.

### Cadence

The recommended schedule is intake every 15 minutes and babysitting every 5 minutes. The existing runner lock and one-worktree limit remain authoritative. A successful intake does not start a second issue in that invocation.

- **Cron:** run `bash .zipper-runner/setup-cron.sh`. It migrates the exact legacy six-hour intake entry, preserves custom schedules and unrelated jobs, and adds babysitting if absent.
- **Existing systemd user timers:** after the PR is merged, install the checked-in timer drop-ins:

```bash
mkdir -p "$HOME/.config/systemd/user/zipper-intake.timer.d" "$HOME/.config/systemd/user/zipper-babysit.timer.d"
cp .zipper-runner/systemd/zipper-intake.timer.d/cadence.conf "$HOME/.config/systemd/user/zipper-intake.timer.d/"
cp .zipper-runner/systemd/zipper-babysit.timer.d/cadence.conf "$HOME/.config/systemd/user/zipper-babysit.timer.d/"
systemctl --user daemon-reload
systemctl --user restart zipper-intake.timer zipper-babysit.timer
```

Use one scheduler, not both. Committing the drop-ins does not change live timers; installing them replaces the calendar cadence while preserving existing service paths and boot/persistence settings.

Install **both** drop-ins together. The intake timer and the babysit timer are independent units, so installing one and forgetting the other leaves no error anywhere: the host keeps a working runner that simply loses new-issue pickup for the rest of each calendar window. Verify the live cadence after installing, and treat a stale intake interval as a deployment bug rather than a scheduler quirk:

```bash
systemctl --user show zipper-intake.timer -p TimersCalendar
systemctl --user list-timers zipper-intake.timer zipper-babysit.timer --no-pager
```

A 15-minute intake answers a structural gap rather than load: each intake firing is a single instant, so one long mission occupying the only worktree slot at that instant costs the whole interval until the next firing. Frequent firing lets the existing lock and worktree limit decide, instead of the schedule.

### Deploying merged changes

The runner syncs its own checkout (`sync_main_repo`) only while it holds the lock, and it holds the lock for the whole of a long agent mission. A merged change therefore waited for the next lock-winning run before it went live, which is tens of minutes when intake is busy. Deploy is a fast-forward of a checkout that is supposed to sit on `main` and stay clean, so it does not need to wait for a run; it only needs to not collide with one.

`deploy-main.sh` runs on its own one-minute timer, outside the runner lock:

```bash
install -Dm755 .zipper-runner/deploy-main.sh "$HOME/.local/bin/zipper-deploy-main.sh"
install -Dm644 .zipper-runner/systemd/zipper-deploy.service .zipper-runner/systemd/zipper-deploy.timer \
    "$HOME/.config/systemd/user/"
systemctl --user daemon-reload
systemctl --user enable --now zipper-deploy.timer
systemctl --user list-timers zipper-deploy.timer --no-pager
```

Install the script **outside** the checkout, as above. A copy inside the tree is an untracked file in the very commit that adds it, so the first fast-forward carrying it would refuse to run and deploy nothing. `REPO_PATH` in the service unit points at the checkout; the script derives nothing from its own location when `REPO_PATH` is set.

The script never discards work: no `reset`, no `checkout`, no `stash`, no `clean`. It does nothing and logs why when the checkout is on another branch, has modifications to tracked files, is mid-rebase/merge/cherry-pick, holds an `index.lock`, or is not a fast-forward. Two of those refusals exist because a merge is not as harmless as it looks: `--ff-only` refuses a diverged checkout instead of creating a merge commit, and `--no-overwrite-ignore` refuses when the incoming commit would overwrite an ignored local file — the tracked-file check cannot see ignored files, and git overwrites them by default, which is how a deploy could destroy the runner's own ignored `runner.lock` or logs. Outcomes land in `.zipper-runner/logs/deploy_*.log`; the already-current case is silent. The runner reads `runner.py` once at process start, so a deploy during a run cannot affect that run.

The script and the runner share one lock around the main checkout's git mutations, and that is what makes deploying safe rather than merely fast. The runner mutates the same checkout, and `_cleanup_git_state` deletes `.git/index.lock` without checking its age, so a run starting mid-deploy would remove the lock an active git process is holding and then run `git rebase --abort` on the same tree. Sharing the runner's own lock would not work: it is held for a whole agent mission, so every deploy would stall for the 20-42 minutes a mission takes. The shared lock is therefore taken only around mutations, never across a mission, so a deploy waits at most one mutation. On the runner side that means the whole-checkout operations — `_cleanup_git_state`, `verify_repo_on_main`, `sync_main_repo`, `_sync_main_after_merge` — take it, and each defers rather than proceeds when it cannot. Narrow ref and worktree operations (branch and worktree creation, the per-branch fetch for an orphan PR) do not take it: they are not the hazard above, and making the runner wait on a lock a shell timer holds would trade a real stall for a theoretical one. The orphan-PR fetch reads its own remote-tracking ref with `--no-write-fetch-head` instead of `FETCH_HEAD`, because `FETCH_HEAD` is a single per-repository file the deploy's fetch also overwrites.

The lock is a directory rather than `flock` because `flock` and `timeout` are GNU utilities absent on macOS, where the runner suite runs in CI. It lives at `.zipper-runner/repo-mutation.lock` (`REPO_MUTATION_LOCK_PATH`) and is gitignored because `_repo_is_clean()` counts untracked files as dirt. Both sides write a `created` timestamp and an `owner` token inside it, and both judge staleness against the same bound (1200s default, `REPO_MUTATION_LOCK_STALE_SECONDS` / `DEPLOY_LOCK_STALE_SECONDS`), deliberately longer than any single locked command so a legitimately slow mutation is never mistaken for a crashed holder and stolen mid-write. The shell falls back to the directory's mtime when the `created` file is missing, so a holder that died between creating the lock and recording its timestamp is still reclaimable. On release both sides verify they are still the recorded owner, so a holder that lost its lock to a steal cannot delete the thief's lock and admit a third writer.

The fetch is bounded on every host too. `timeout`/`gtimeout` is used where it exists; where neither does, the fetch runs in the background with a watchdog that kills it. The watchdog is used rather than the tempting `perl -e 'alarm shift; exec @ARGV'`, which is not equivalent: the alarm survives the exec only until the exec'd program is itself a shell script, after which the signal is deferred until that script's foreground child finishes, so the bound silently does nothing. Git's own low-speed abort is the third, weakest layer — it covers a stalled transfer but only for HTTP(S) remotes, so it is a backstop rather than the bound.

To verify it end to end, rewind the checkout and watch the timer restore it. The guard below is deliberately strict, because `git reset --hard` destroys work that a bare `HEAD == origin/main` check does not see: local edits to tracked files (the commit matches, the working tree does not), and any branch that happens to point at `origin/main` (the reset would rewind that branch instead of `main`). A checkout *ahead* of the remote is a state the script deliberately tolerates, so "it looked clean" is not enough to make a reset safe.

```bash
cd ~/repos/zipper
if [ "$(git branch --show-current)" != "main" ]; then
    echo "not on main; refusing to rewind. Use a disposable clone instead."
elif [ -n "$(git status --porcelain)" ]; then
    echo "checkout has local changes; refusing to rewind. Commit or stash them first."
elif [ "$(git rev-parse HEAD)" != "$(git rev-parse origin/main)" ]; then
    echo "HEAD is not exactly origin/main; refusing to rewind."
else
    git reset --hard HEAD~1
    for _ in $(seq 1 20); do                      # a timer may fire up to a minute late
        [ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] && break
        sleep 5
    done
    [ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] \
        && echo "deployed" || echo "TIMED OUT: deploy did not run"
    tail -1 .zipper-runner/logs/deploy_*.log
fi
```

### Harness limits

`AGENTS.md` documents bounded tool discovery, native subagent schema mapping, valid GitHub CLI examples, and focused iteration with unchanged final review/test gates. The runner cannot repair Factory's tool catalog, search relevance, or Loop availability. Missing capabilities must be reported rather than retried indefinitely or bypassed against harness policy.

Runner regressions: `cd .zipper-runner && python3 -m unittest discover -s tests`.

## Unattended endurance

The runner is designed to run for days without human intervention. Every known freeze, crash-loop, flood, and churn path has a bound:

- **Bounded commands:** every `run_cmd` call gets a default timeout (`RUNNER_CMD_TIMEOUT`, 300s; explicit values for longer gates — 25m for the full review-gate wait, 10m for CodeRabbit, and `RUNNER_GIT_NETWORK_TIMEOUT`, 15m, for push/fetch). Git HTTP I/O additionally aborts on stalled transfers (`GIT_HTTP_LOW_SPEED_LIMIT`/`GIT_HTTP_LOW_SPEED_TIME`); operator-configured values always win, and `GIT_SSH_COMMAND` is deliberately not injected so repository `core.sshCommand` stays authoritative. `tests/wait-for-reviews.sh` bounds each of its own `gh` calls (`timeout`/`gtimeout`) because its deadline only fires between polls.
- **Stale-lock watchdog:** each invocation writes `state/runner-heartbeat.json` (pid, pgid, start time). A later cycle that cannot take the lock after `RUNNER_LOCK_TIMEOUT` reclaims it by killing the holder's process group once the holder exceeds `RUNNER_MAX_RUN_SECONDS` (12h default) — a wedged run can no longer freeze the pipeline silently. Fail-closed: no usable heartbeat, no takeover.
- **Process-group cleanup:** all agent plugins launch missions with `start_new_session=True` and kill the whole group on timeout, so orphaned LSP servers or build nodes cannot hold the output pipes (which would block `communicate()` forever while the lock is held).
- **Nested mission timeouts:** an agent CLI with its own print timeout must be told to fire *before* the plugin's 45-minute (2700s) kill, or the agent is SIGKILLed mid-turn and its partial work is never reported. agy runs with `--print-timeout 40m`, leaving five minutes to flush output and exit; the earlier 30m value truncated long missions routinely, and a truncated mission still produces a PR that then needs repair cycles.
- **gh outage guard:** before a failed `gh pr view` is interpreted as "no PR", the runner proves gh can reach the API (`gh api rate_limit`, free). During network/auth outages the worktree is skipped for the cycle instead of dispatching repair missions on a false premise; one rate-limited alert email is sent.
- **Crash-loop guards:** every `json.loads` on `gh` output is guarded; unparseable output skips the worktree for a cycle instead of crashing identically every 5 minutes.
- **Cross-cycle quota cooldown:** quota exhaustion is persisted in `state/quota-*.json`. When the provider states its own window (`Resets in 85h0m6s`, as agy embeds in its 429 payload), that window is authoritative and recorded as `resets_at`; `RUNNER_QUOTA_COOLDOWN_MINUTES` (60 default) is the fallback for providers that state nothing. The window is read from the provider's **error channel only** — agy reports its 429 on stderr with stdout empty — because stdout is assistant text, including partial text from an interrupted mission, that can quote an unrelated reset and park the model for the wrong span. Retrying inside a stated window can only fail, so honoring it hands the cycle to a working model instead of relaunching a doomed mission every hour for the life of the window. `RUNNER_QUOTA_RESET_MAX_HOURS` (168 default) caps an implausible or garbled hint so it cannot park a model indefinitely, and the marks still expire so silently-reset quotas retry automatically.
- **Worktree mission backoff:** three consecutive fruitless missions on one branch trigger an exponential cooldown (8m → … → 6h cap, `state/worktree-*.json`). Any progress — commits, PR state, thread resolution — clears it; after a served cooldown the ladder restarts low.
- **Blocked-worktree escalation:** a PR the runner cannot authorize never fails silently. The runner sends a rate-limited email naming the concrete reason (draft, retargeted base, renamed branch, missing record) and the remediation that works for that reason, and dispatches no repair missions while the PR stays unauthorized. The worktree keeps its slot, so a human decision is what moves the pipeline again.
- **Missing-model notification:** a model still configured at a non-zero priority that its agent no longer lists is reported once, rate-limited, because the runner otherwise queues it, selects it, and fails the mission at runtime. Only agents whose discovery returned real models are checked, so a provider outage that falls back to `default` cannot name every configured model as missing, and the `default` placeholder itself is never reported because it names the agent's own default model rather than a model discovery can return. A read-only `--status` run prints the warning but sends nothing and spends no rate-limit budget, matching the `All Agents Unhealthy` alert.
- **Unrecorded-PR escalation:** a green PR the runner has no authorization record for — no issue reference at all, or an issue it never worked on — can never be authorized, and the orphan scanner would otherwise skip it forever (PR #1258 sat four days). After `RUNNER_STALLED_PR_HOURS` (12 default) the runner sends a rate-limited `Cannot Be Authorized` email naming the PR and the remediation. It never merges such a PR: an unverifiable origin stays fail-closed.
- **Orphan-recovery escalation:** the orphan path merges without a worktree, so when it cannot recreate one to babysit a PR — an existing local branch at a different head, a fetch mismatch, or a failed worktree add — it sends a rate-limited `Cannot Be Babied` email instead of retrying silently every cycle. A recreation that succeeds stays silent.
- **Environmental gate failures:** a PR blocked by a missing coderabbit CLI (something a mission cannot fix) skips mission dispatch entirely.
- **Notification discipline:** every email that can recur with a persistent condition is rate-limited (3 immediate, then one per 24h per subject). Success emails stay one-shot. Recovery stashes are named, and repeated main-repo recovery reuses the existing `runner-backup-*` branch instead of minting one per cycle. A `--dry-run` records no send at all: `send_email` already suppresses delivery, so a recorded mark would spend the budget on an email nobody received and hide the real alert for a cooldown window.
- **Delivery needs an entry point, not an import:** `send_email` delivers only when the file is executed (production runs it via `cron-wrapper.sh`, so `__main__` holds) or when a test sets `EMAIL_DELIVERY_ENABLED`. Tests import the module, so before this guard any test that exercised an email path mailed the maintainer on every run — issue #1218's Dependabot test did, repeatedly. The few tests that assert delivery opt in explicitly, and `EmailDeliveryGuardTests` pins both directions.
- **Bounded state:** `state/` is swept at startup — `issue-*`, `pr-*`, `quota-*`, `worktree-*`, and `opencode-poisoned-*` files older than `RUNNER_STATE_MAX_AGE_DAYS` (90 default) are deleted, except state for the issues and PRs a live worktree still owns — a `pr-<N>-coderabbit-gate.json` is named by PR number while its worktree is named by issue number, so each prefix is compared against the numbers of its own kind. `rate-*` files keep their own 24h pruning; logs are rotated at 7 days by `cron-wrapper.sh`.
- **Distinct run logs:** `cron-wrapper.sh` names each log `run_<timestamp>_<mode>_<pid>.log`. The intake and babysit timers can fire in the same minute, so a timestamp-only name made two processes append to one file — interleaved output, duplicated banners, and an exit line reporting the other run's duration.
- **Session poisoning:** an overflowed opencode session that cannot be archived (locked DB) is marked poisoned in state and never resumed again, breaking the resume → overflow → fail cycle.
- **Preference wipe guard:** `--refresh-models` refuses to rewrite `AGENT_PREFERENCES.md` when model discovery fell back entirely to `default` (outage/credential expiry), so transient failures cannot destroy priorities or re-enable disabled models.

## Jev gate (TypeSafe reflex layer)

`.zipper-runner/jev_gate.py` adds advisory TypeSafe (Jev) judgments to the runner loop. Set `TYPESAFE_API_KEY` in `.zipper-runner/.env` to enable; without it every judgment returns `None` and the runner keeps its deterministic behavior.

- **CI failure triage:** failed checks are classified real_regression / flaky / environment / dependency / unrelated (one bounded Jev request per PR failure). All-flaky/infra failures with rerun budget left auto-rerun the failed jobs (`gh run rerun --failed`) instead of burning agent tokens; anything else goes to babysit with the triage attached. Rerun budget: 1 per PR (state file `state/pr-<N>-rerun.json`).
- **Completion verification:** after a babysit success, Jev scores the branch diff against the issue body; a low score sends a rate-limited advisory email (never blocks).
- **Stuck classification:** on agent failure, Jev classifies the output tail (progressing / repeating / blocked / wrong_direction); stuck classes trigger the same fallback path as the repeated-line heuristic, and the retry prompt says what the previous attempt looked like.
- **Prompt-injection gate:** before dispatching a new issue, Jev scores the issue text for injection/jailbreak attempts. High confidence blocks pickup (marker file `state/issue-<N>-injection.json`, rate-limited email; delete the marker to re-enable pickup). Uncertain signals proceed with an advisory email. Fails open — existing mitigations (trusted-author comment filter, `<issue-data>` wrapper in the mission prompt) stay in place.

The gate never writes to GitHub and never grants permissions; `runner.py` owns every enforcement decision.

Tests: `cd .zipper-runner && python3 tests/test_jev_gate.py`

## Architecture: Why two files?

The pipeline is split into a shell script (`.sh`) and a Python script (`.py`) to enforce a strict **Separation of Concerns**.

### 1. `cron-wrapper.sh` (The Environment Bootstrapper)
When the Linux `cron` daemon runs a background job, it executes in a completely "naked" environment. It does **not** load user profiles (`~/.bashrc`, `~/.profile`).
* If Python were executed directly by cron, tools like `dotnet`, `gh`, and `agy` would instantly fail because they wouldn't be in the system's restricted `$PATH`.
* The bash script exists purely to construct the proper environment (injecting `PATH`, `HOME`, `DOTNET_ROOT`), manage log directory creation, handle log rotation, and safely route output to timestamped files.

### 2. `runner.py` (The Brain / Orchestrator)
While Bash is excellent for setting up environments, it is notoriously fragile when handling JSON, complex conditionals, and state management.
* The runner needs to query the GitHub API via `gh pr view --json`, parse arrays of CI checks, compute time differences for hanging checks, and safely acquire file locks (`runner.lock`) to prevent concurrent executions.
* Implementing this logic in Bash would require a fragile mess of `jq` queries and convoluted `if/else` blocks. Python handles these complex API interactions and logic workflows cleanly and robustly.

---

## Configuration

Native checkout paths are resolved relative to the scripts. Override `RUNNER_BASE`, `REPO_PATH`, `WORKTREES_BASE`, `DOTNET_ROOT`, and `EMAIL_RECIPIENT` for another installation. Keep credentials in the ignored `.env` or the process environment. GitHub repository endpoints remain Zipper-specific.
