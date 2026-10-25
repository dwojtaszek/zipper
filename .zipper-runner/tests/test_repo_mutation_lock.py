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

    def hold_lock_asAnotherProcess(self, age_seconds: int = 0, pid: int | None = None) -> None:
        """Take the lock the way deploy-main.sh does: an atomic mkdir on the path."""
        self.lock_path.mkdir()
        (self.lock_path / "created").write_text(str(int(time.time()) - age_seconds))
        if pid is not None:
            (self.lock_path / "pid").write_text(str(pid))
        # The runner judges staleness by the directory's mtime, the shell by the
        # `created` file, so an old holder must look old to both.
        stamp = time.time() - age_seconds
        os.utime(self.lock_path, (stamp, stamp))

    @staticmethod
    def _unused_pid() -> int:
        """A pid that is provably not running, without spawning a process to find out."""
        for candidate in range(40000, 40100):
            try:
                os.kill(candidate, 0)
            except ProcessLookupError:
                return candidate
            except OSError:
                continue
        raise unittest.SkipTest("no unused pid available in the probe range")

    def _live_pid(self) -> int:
        """A pid that is provably running, cleaned up by the test."""
        process = subprocess.Popen(["sleep", "60"])
        self.addCleanup(process.wait)
        self.addCleanup(process.kill)
        return process.pid

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

    def test_lock_whenAcquired_shouldRecordMetadataTheShellCanRead(self):
        """deploy-main.sh reports the holder's age from `created`. If the runner did
        not write it, the shell could never judge a runner-created lock stale and
        every deploy would skip while the runner was down."""
        with runner._repo_mutation_lock() as locked:
            self.assertTrue(locked)
            created = int((self.lock_path / "created").read_text())
            self.assertLess(abs(time.time() - created), 60)
            self.assertEqual((self.lock_path / "owner").read_text(),
                             runner._repo_mutation_lock_token)
            self.assertTrue(runner._repo_mutation_lock_token)
            self.assertEqual((self.lock_path / "pid").read_text(), str(os.getpid()))

    def test_lock_whenTheHolderWasKilled_shouldBeReclaimedImmediately(self):
        """Age alone would keep every other process out for the whole 20-minute stale
        bound, so a killed deploy stopped the checkout from being maintained: the
        runner deferred its main-sync and branch recovery with `REPO LOCK: main
        checkout busy for 120s` on every run for that entire window."""
        self.hold_lock_asAnotherProcess(age_seconds=0, pid=self._unused_pid())

        with runner._repo_mutation_lock() as locked:
            self.assertTrue(locked, "a lock whose recorded holder is gone must be reclaimed at once")

    def test_lock_whenTheRecordedHolderIsAlive_shouldNotBeReclaimedEarly(self):
        """The pid check must not become a way to steal a live holder's lock."""
        self.hold_lock_asAnotherProcess(age_seconds=0, pid=self._live_pid())

        with runner._repo_mutation_lock() as locked:
            self.assertFalse(locked, "a live holder must keep its lock until the age bound")

    def test_lock_whenTheHolderIsThisProcess_shouldNotReclaimItsOwnLock(self):
        """A re-entrant caller reaching the steal path must not delete its own lock."""
        self.hold_lock_asAnotherProcess(age_seconds=0, pid=os.getpid())

        with runner._repo_mutation_lock() as locked:
            self.assertFalse(locked, "a process must not steal the lock it already holds")

    def test_lock_whenThePidIsAbsentOrMalformed_shouldFallBackToTheAgeBound(self):
        """An unverifiable holder is not a dead one: the lock must survive."""
        for pid in (None, 0, -1, "not-a-pid", ""):
            with self.subTest(pid=pid):
                if self.lock_path.exists():
                    shutil.rmtree(self.lock_path)
                self.hold_lock_asAnotherProcess(age_seconds=0, pid=pid)
                if pid is None:
                    (self.lock_path / "pid").unlink(missing_ok=True)
                elif not isinstance(pid, int) or pid <= 0:
                    (self.lock_path / "pid").write_text(str(pid))

                with runner._repo_mutation_lock() as locked:
                    self.assertFalse(locked, f"pid={pid!r} is not proof the holder is gone")

    def test_lockHolderPidIsGone_whenTheHolderWasKilled_shouldReportGone(self):
        self.hold_lock_asAnotherProcess(pid=self._unused_pid())

        self.assertTrue(runner._lock_holder_pid_is_gone())

    def test_lockHolderPidIsGone_whenTheHolderIsAlive_shouldReportAlive(self):
        self.hold_lock_asAnotherProcess(pid=self._live_pid())

        self.assertFalse(runner._lock_holder_pid_is_gone())

    def test_lockHolderPidIsGone_whenThereIsNoPidFile_shouldReportUnknown(self):
        self.hold_lock_asAnotherProcess()

        self.assertFalse(runner._lock_holder_pid_is_gone(),
                         "an absent pid is unknown, not proof of death")

    def test_lockHolderPidIsGone_whenTheLockIsAbsent_shouldReportUnknown(self):
        self.assertFalse(runner._lock_holder_pid_is_gone())

    def test_lock_whenAnotherProcessStoleIt_shouldNotDeleteTheThiefsLock(self):
        """A stale steal leaves the lock to another process. Removing it on release
        would let a third process in while the thief is mid-merge."""
        with runner._repo_mutation_lock():
            (self.lock_path / "owner").write_text("a-different-holder")

        self.assertTrue(self.lock_path.exists(), "the new owner's lock must survive")

    def test_lock_whenOwnTokenIsStillRecorded_shouldDeleteTheLock(self):
        """The token check must not stop a holder from releasing its own lock."""
        with runner._repo_mutation_lock():
            pass

        self.assertFalse(self.lock_path.exists())

    def test_lock_whenMetadataCannotBeWritten_shouldStillAcquireAndRelease(self):
        """The mkdir is the lock; a failed metadata write must not break acquisition
        or leave the lock behind."""
        with patch.object(runner, "_write_repo_mutation_lock_metadata", return_value=False):
            with runner._repo_mutation_lock() as locked:
                self.assertTrue(locked)
                self.assertTrue(self.lock_path.is_dir())
                self.assertEqual(runner._repo_mutation_lock_token, "",
                                 "no recorded identity means nothing to verify on release")

        self.assertFalse(self.lock_path.exists())

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

    def test_syncMainRepo_whenCheckoutIsNotOnMain_shouldDeferInsteadOfRebasing(self):
        """`verify_repo_on_main` recovers a stray branch, but it defers when the
        lock is busy. Rebasing a feature branch onto origin/main would rewrite that
        branch, so the sync itself must refuse rather than trust the ordering."""
        repo = self._git_repo()
        subprocess.run(["git", "-C", str(repo), "checkout", "--quiet", "-b", "feat/x"], check=True)
        runner.REPO_PATH = str(repo)

        with patch.object(runner, "_fetch_origin_or_abort") as fetch, \
                patch.object(runner, "run_cmd", wraps=runner.run_cmd) as run_cmd:
            result = runner.sync_main_repo()

        self.assertFalse(result)
        fetch.assert_not_called()
        rebase_calls = [c for c in run_cmd.call_args_list if "rebase" in " ".join(c.args[0])]
        self.assertEqual(rebase_calls, [], "a non-main checkout must never be rebased")

    def test_syncMainAfterMerge_whenLockUnavailable_shouldDeferWithoutMutating(self):
        self.hold_lock_asAnotherProcess()

        with patch.object(runner, "run_cmd") as run_cmd:
            result = runner._sync_main_after_merge()

        self.assertFalse(result)
        run_cmd.assert_not_called()


if __name__ == "__main__":
    unittest.main()
