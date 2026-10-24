"""Tests for the shared main-checkout mutation lock in runner.py.

The runner and .zipper-runner/deploy-main.sh mutate the same git checkout from
different processes. The runner's `_cleanup_git_state` deletes `.git/index.lock`
without checking its age, so a run starting while the deploy is mid-merge would
remove the lock an active git process is holding and then run `git rebase --abort`
on the same tree. Both sides therefore take the lock these tests cover.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch

import gitenv  # noqa: F401  # Git identity for fixture repositories

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402


class RepoMutationLockTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-mutation-lock-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.lock_path = self.root / "repo-mutation.lock"
        self._saved = (runner.REPO_MUTATION_LOCK_PATH, runner.REPO_MUTATION_LOCK_TIMEOUT,
                       runner.REPO_MUTATION_LOCK_STALE_SECONDS, runner.REPO_PATH,
                       runner.DRY_RUN, runner._repo_mutation_lock_depth)
        runner.REPO_MUTATION_LOCK_PATH = str(self.lock_path)
        runner.REPO_MUTATION_LOCK_TIMEOUT = 0  # tests must not wait out the real bound
        runner.REPO_MUTATION_LOCK_STALE_SECONDS = 1200
        runner.DRY_RUN = False
        runner._repo_mutation_lock_depth = 0

    def tearDown(self):
        (runner.REPO_MUTATION_LOCK_PATH, runner.REPO_MUTATION_LOCK_TIMEOUT,
         runner.REPO_MUTATION_LOCK_STALE_SECONDS, runner.REPO_PATH,
         runner.DRY_RUN, runner._repo_mutation_lock_depth) = self._saved

    def hold_lock_asAnotherProcess(self, age_seconds: int = 0) -> None:
        """Take the lock the way deploy-main.sh does: an atomic mkdir on the path."""
        self.lock_path.mkdir()
        (self.lock_path / "created").write_text(str(int(time.time()) - age_seconds))
        # The runner judges staleness by the directory's mtime, the shell by the
        # `created` file, so an old holder must look old to both.
        stamp = time.time() - age_seconds
        os.utime(self.lock_path, (stamp, stamp))

    def _git_repo(self) -> Path:
        repo = self.root / "checkout"
        repo.mkdir()
        subprocess.run(["git", "init", "--quiet", "--initial-branch=main", str(repo)], check=True)
        return repo

    def test_lock_whenFree_shouldBeHeldInsideAndReleasedAfter(self):
        with runner._repo_mutation_lock() as locked:
            self.assertTrue(locked)
            self.assertTrue(self.lock_path.is_dir(), "the lock must be visible to the shell side")

        self.assertFalse(self.lock_path.exists(), "the lock must be released on exit")

    def test_lock_whenReleasedAfterAnException_shouldNotRemainHeld(self):
        with self.assertRaises(RuntimeError):
            with runner._repo_mutation_lock():
                raise RuntimeError("boom")

        self.assertFalse(self.lock_path.exists(), "a crashed mutation must not wedge the checkout")

    def test_lock_whenNested_shouldNotDeadlockAndReleaseOnce(self):
        """A locked helper calls other locked helpers, so re-entrancy is required
        for the wrappers to be usable at all."""
        with runner._repo_mutation_lock() as outer:
            with runner._repo_mutation_lock() as inner:
                self.assertTrue(outer)
                self.assertTrue(inner)
            self.assertTrue(self.lock_path.exists(), "the outer holder must keep the lock")

        self.assertFalse(self.lock_path.exists())

    def test_lock_whenHeldByAnotherProcess_shouldRefuseToMutate(self):
        self.hold_lock_asAnotherProcess()

        with runner._repo_mutation_lock() as locked:
            self.assertFalse(locked, "a held lock must yield False, never True")

        self.assertTrue(self.lock_path.exists(), "the other process's lock must be left alone")

    def test_lock_whenHolderIsStale_shouldBeStolen(self):
        """A crashed deploy must not block every later run until a human notices."""
        self.hold_lock_asAnotherProcess(age_seconds=3600)

        with runner._repo_mutation_lock() as locked:
            self.assertTrue(locked, "a stale lock must be reclaimed")

    def test_lock_whenHolderIsYoungerThanTheRunnerSlowestMutation_shouldNotBeStolen(self):
        """The bound must exceed NETWORK_GIT_TIMEOUT, or a slow fetch would be
        mistaken for a crashed holder and its lock stolen mid-write."""
        self.assertGreater(runner.REPO_MUTATION_LOCK_STALE_SECONDS,
                           runner.NETWORK_GIT_TIMEOUT)
        self.hold_lock_asAnotherProcess(age_seconds=600)

        with runner._repo_mutation_lock() as locked:
            self.assertFalse(locked, "a live holder must not be stolen from")

    def test_cleanupGitState_whenLockUnavailable_shouldNotRemoveTheIndexLock(self):
        """This is the exact hazard the lock exists for: removing a live index.lock
        would let two processes mutate the same index."""
        repo = self._git_repo()
        runner.REPO_PATH = str(repo)
        index_lock = repo / ".git" / "index.lock"
        index_lock.write_text("held by an active git process\n")
        self.hold_lock_asAnotherProcess()

        with patch.object(runner, "run_cmd") as run_cmd:
            runner._cleanup_git_state()

        run_cmd.assert_not_called()
        self.assertTrue(index_lock.exists(), "another process's index lock must survive")

    def test_cleanupGitState_whenLockFree_shouldStillRemoveTheIndexLock(self):
        """The lock must not silently disable the cleanup it guards."""
        repo = self._git_repo()
        runner.REPO_PATH = str(repo)
        index_lock = repo / ".git" / "index.lock"
        index_lock.write_text("stale\n")

        runner._cleanup_git_state()

        self.assertFalse(index_lock.exists(), "a stale index lock must still be cleaned up")

    def test_verifyRepoOnMain_whenLockUnavailable_shouldDeferWithoutMutating(self):
        runner.REPO_PATH = str(self._git_repo())
        self.hold_lock_asAnotherProcess()

        with patch.object(runner, "run_cmd") as run_cmd:
            result = runner.verify_repo_on_main()

        self.assertFalse(result, "recovery must defer rather than mutate a busy checkout")
        run_cmd.assert_not_called()

    def test_syncMainRepo_whenLockUnavailable_shouldDeferWithoutMutating(self):
        self.hold_lock_asAnotherProcess()

        with patch.object(runner, "_fetch_origin_or_abort") as fetch:
            result = runner.sync_main_repo()

        self.assertFalse(result)
        fetch.assert_not_called()

    def test_syncMainAfterMerge_whenLockUnavailable_shouldDeferWithoutMutating(self):
        self.hold_lock_asAnotherProcess()

        with patch.object(runner, "run_cmd") as run_cmd:
            result = runner._sync_main_after_merge()

        self.assertFalse(result)
        run_cmd.assert_not_called()


if __name__ == "__main__":
    unittest.main()
