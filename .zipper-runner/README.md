# Zipper Autonomous Runner

This directory contains the autonomous runner pipeline that manages GitHub issues, triggers the AI agent, and creates pull requests.

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

## ⚠️ Pre-Commit Warning: Hardcoded Paths
Currently, these scripts contain paths hardcoded to `/home/dom/...`. 
Before fully committing these to the repository to be shared with other developers or deployed to a server, the scripts must be refactored to:
1. Dynamically resolve their own paths (`dirname $0` in bash, `os.path.dirname(__file__)` in Python).
2. Extract user-specific configurations (like `DOTNET_ROOT` and `EMAIL_RECIPIENT`) into an untracked `.env` file.
