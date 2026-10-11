"""Fixture Git identity for the runner test repositories.

The runner suites create throwaway repositories and commit in them. A CI runner
has no global Git identity, so those commits fail with
"Author identity unknown" — which is what reddened main CI for 15 hours
(run 38032960810, fixed by #1173 with a workflow-level `git config`).

Setting the identity in the test process environment removes that dependency
instead of patching it around: every fixture repository becomes hermetic on
every machine and OS. Existing identities outside this process are never
touched, because the variables live only in this process's environment and are
inherited by its Git subprocesses.
"""

import os

_FIXTURE_IDENTITY = ("zipper-runner-test", "zipper-runner-test@example.invalid")

os.environ["GIT_AUTHOR_NAME"], os.environ["GIT_AUTHOR_EMAIL"] = _FIXTURE_IDENTITY
os.environ["GIT_COMMITTER_NAME"], os.environ["GIT_COMMITTER_EMAIL"] = _FIXTURE_IDENTITY
