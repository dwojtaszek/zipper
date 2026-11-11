# Runner operations

Use the installed launcher rather than constructing a new PATH, working directory, or authentication environment for a trial.

## Preflight

Run this before pausing timers or starting a controlled mission:

```bash
bash .zipper-runner/cron-wrapper.sh --preflight
```

The launcher sets the repository working directory and normal tool PATH. Preflight verifies Git, the configured .NET SDK, GitHub authentication/API access, and an enabled healthy agent. It returns nonzero on failure and performs no issue pickup, Git mutation, or timer changes. `runner.py --status` adds lock, worktree, and PR diagnostics and also reports a failing runtime with a nonzero status. `--status --dry-run` remains a diagnostic preview without live health checks.

For controlled launches, use this same wrapper with `--agent codex --model openai/gpt-6.1-sol`. Keep the production state and lock locations when testing a different plugin checkout. Stop only the relevant timers after preflight passes, preserve active services, and arrange timer restoration on every exit. Verify timer state afterward. The parent runner owns CI monitoring after implementation returns.

## Gates and logs

`change-scope.py` is the shared classifier used by `dev.sh`, hooks, and PR CI. An eligible runner-only change runs runner tests and static checks plus applicable documentation checks. Product, build, workflow, hook, and unknown paths retain the broader gates. Rename detection includes both paths; an unreadable diff cannot enable a skip. Main/release workflows retain the complete product matrix by default.

Keep full logs on disk and return the real exit status:

```bash
python3 .github/scripts/run-check.py --log /tmp/zipper-runner-tests.log -- python3 -m unittest discover -s .zipper-runner/tests -p 'test_*.py'
rg -n 'FAIL:|ERROR:|Traceback|FAILED' /tmp/zipper-runner-tests.log
```

The wrapper displays at most 20 lines of bounded length, including an early failure excerpt when available, and prints the retained log path and exit status. Reuse a completed check's log; fetch only the failed CI step before requesting complete job logs. During waits, report changed states or a new blocker instead of dumping unchanged tables.

## Delivery

A request to prepare a PR ends with the branch, review result, checks, and any pending approval clearly identified. A merge/deploy request also requires exact-head merge verification and confirmation that the running checkout contains the merged configuration. Retain user approvals across turns without expanding them to a different action. Request approval only when required, after producing the concrete result to review.
