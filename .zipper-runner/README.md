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

An open PR with a WIP checkpoint resumes its existing worktree through the normal agent completion path, once per worktree per invocation. Failed continuations preserve uncommitted work; dry-run never dispatches a continuation. A local-only commit is not PR progress: the agent must publish updates through the normal local CodeRabbit gate. A completion commit must represent actual finished work, not a renamed checkpoint or empty commit. Readiness still depends on existing CI and reviews, with advisory Jev verification; a tree diff alone cannot prove semantic completion. Progress checks take read-only thread snapshots; the full robot-review wait remains a merge gate, not a delay before or after each repair.

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

### Harness limits

`AGENTS.md` documents bounded tool discovery, native subagent schema mapping, optional RTK, valid GitHub CLI examples, and focused iteration with unchanged final review/test gates. The runner cannot repair Factory's tool catalog, search relevance, or Loop availability. Missing capabilities must be reported rather than retried indefinitely or bypassed against harness policy.

Runner regressions: `cd .zipper-runner && python3 -m unittest discover -s tests`.

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
