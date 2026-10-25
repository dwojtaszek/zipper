"""Tests for the shared main-checkout mutation lock in runner.py.

The runner and .zipper-runner/deploy-main.sh mutate the same git checkout from
different processes. The runner's `_cleanup_git_state` deletes `.git/index.lock`
without checking its age, so a run starting while the deploy is mid-merge would
remove the lock an active git process is holding and then run `git rebase --abort`
on the same tree. Both sides therefore take the lock these tests cover.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import fcntl
import os
import shutil
import subprocess
import sys
import tempfile
import threading
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

    def test_lockIsHeld_whenTheLockIsAbsent_shouldNotCreateIt(self):
        """Asking whether a lock is held must never create one.

        `_lock_is_held` opened the path with "a", which creates the file. Against
        the main-checkout lock's path that manufactured a zero-byte regular file
        where the protocol requires a directory, so every later mkdir failed with
        EEXIST and read as "another process holds it". Observed on the host: one
        `--status` run wedged deploys for 20 minutes, and a second did it again for 11.
        """
        self.assertFalse(self.lock_path.exists())

        self.assertFalse(runner._lock_is_held(str(self.lock_path)))

        self.assertFalse(self.lock_path.exists(),
                         "probing a free lock must leave the path absent, not create a file")

    def test_lockIsHeld_whenThePathVanishesBetweenTheCheckAndTheOpen_shouldNotRecreateIt(self):
        """The guard has to hold under the race, not just when nothing else is running.

        `os.path.exists` and the open are two steps, and this path is a *shared* lock
        that other processes remove routinely, so "it exists" does not mean it still
        exists a moment later. A probe that opened with "a" would then recreate the
        exact poison file it is supposed to stop making, so the regression would come
        back whenever `--status` raced a deploy's reclaim.
        """
        # Report the path as present while it is absent, which is what the race looks
        # like from inside the probe.
        with patch.object(runner.os.path, "exists", return_value=True):
            self.assertFalse(runner._lock_is_held(str(self.lock_path)))

        self.assertFalse(self.lock_path.exists(),
                         "a probe must not create the path even when it believes it exists")

    def test_lockIsHeld_whenThePathIsAFifo_shouldReportFreeWithoutBlocking(self):
        """Two failures in one path: opening a fifo for writing blocks until a reader
        appears, and `flock` on a fifo fails on macOS (ENOTSUP) while succeeding on
        Linux. Trusting `flock` there would make the answer platform-dependent, so
        the type is classified from the descriptor and a fifo reads as free.

        The no-block property is checked on every platform. The classification is
        only *discriminated* on macOS, because `flock` on a fifo succeeds on Linux
        and would return free by accident; macOS CI is what catches a regression in
        the descriptor check, and it did.
        """
        os.mkfifo(self.lock_path)

        # A hang would hit the suite's own timeout rather than this assertion, so the
        # probe runs on a thread and the test fails fast with a real message.
        result: list[bool] = []
        thread = threading.Thread(target=lambda: result.append(
            runner._lock_is_held(str(self.lock_path))), daemon=True)
        thread.start()
        thread.join(timeout=10)

        self.assertFalse(thread.is_alive(), "probing a fifo must not block")
        self.assertEqual(result, [False], "a fifo cannot hold a lock")
        self.assertTrue(self.lock_path.exists(), "the probe must leave the fifo in place")

    def test_lockIsHeld_whenThePathIsACharacterDevice_shouldReportFree(self):
        """The same classification for a descriptor `flock` cannot lock. Like the fifo
        case, Linux's `flock` accepts it anyway, so the discriminating platform is
        macOS; this pins the reported answer on both."""
        self.assertFalse(runner._lock_is_held(os.devnull))

    def test_lockIsHeld_whenTheLockIsAFreeDirectory_shouldReportHeld(self):
        """The main-checkout lock is a directory, not a flock file: its existence is
        the lock, so the probe must read it as held."""
        self.lock_path.mkdir()

        self.assertTrue(runner._lock_is_held(str(self.lock_path)))

    def test_lockIsHeld_whenTheLockIsAFlockFile_shouldReportIt(self):
        """The other protocol this helper serves: a real flock must still be
        detected, or `--status` would report every agent lock as free."""
        self.lock_path.touch()
        with open(self.lock_path, "a") as handle:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertTrue(runner._lock_is_held(str(self.lock_path)))
            fcntl.flock(handle, fcntl.LOCK_UN)

        self.assertFalse(runner._lock_is_held(str(self.lock_path)))

    def test_lock_whenAFileSitsAtTheLockPath_shouldReclaimImmediately(self):
        """A regular file cannot be a held lock under a mkdir protocol, so it must be
        reclaimed at once rather than deferred until the 20-minute stale bound."""
        self.lock_path.touch()

        with runner._repo_mutation_lock() as locked:
            self.assertTrue(locked, "a non-directory lock path must be reclaimed at once")
            self.assertTrue(self.lock_path.is_dir(), "the acquired lock must be a directory")

    def test_lock_whenAFileSitsAtTheLockPath_shouldNotWaitForTheStaleBound(self):
        """The reclaim must not depend on age: a fresh poison file is just as
        unusable as an old one, and waiting out the bound is the whole bug.

        The timeout is raised above its fail-fast default so this actually measures
        something. A reclaim that fell through to the age path would spend the whole
        timeout before yielding False, so both assertions below would fail; with the
        timeout at 0 the elapsed check could never fail at all.
        """
        self.lock_path.touch()
        runner.REPO_MUTATION_LOCK_TIMEOUT = 20

        started = time.monotonic()
        with runner._repo_mutation_lock() as locked:
            elapsed = time.monotonic() - started

        self.assertTrue(locked, "a fresh poison file must be reclaimed, not waited out")
        self.assertLess(elapsed, 2, "the reclaim must not consume the lock timeout")

    def test_removePoisonedLock_whenThePathIsASymlinkToALock_shouldLeaveTheRealLockAlone(self):
        """Removing a symlink removes the link, never its target, so a redirect aimed
        at a real lock directory cannot be used to delete that lock."""
        real_lock = self.root / "elsewhere"
        real_lock.mkdir()
        self.lock_path.symlink_to(real_lock)

        self.assertFalse(runner._remove_poisoned_repo_mutation_lock())
        self.assertTrue(real_lock.is_dir(), "the symlink target must survive")
        self.assertTrue(self.lock_path.is_symlink(), "a link to a real lock must be left alone")

    def test_removePoisonedLock_whenThePathIsARealLock_shouldLeaveItAlone(self):
        """The reclaim must not become a way to delete a live holder's lock."""
        self.hold_lock_asAnotherProcess(pid=self._live_pid())

        self.assertFalse(runner._remove_poisoned_repo_mutation_lock())
        self.assertTrue(self.lock_path.is_dir(), "a real lock must survive the poison check")

    def test_removePoisonedLock_whenThePathIsAbsent_shouldReportNothingRemoved(self):
        self.assertFalse(runner._remove_poisoned_repo_mutation_lock())
        self.assertFalse(self.lock_path.exists())

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

    def test_claimLockForRemoval_whenTheLockIsUnchanged_shouldRemoveIt(self):
        """The claim must not break the ordinary reclaim it exists to guard."""
        self.hold_lock_asAnotherProcess(pid=self._unused_pid())
        identity = runner._read_repo_mutation_lock_identity()

        self.assertTrue(runner._claim_repo_mutation_lock_for_removal(identity))
        self.assertFalse(self.lock_path.exists())

    def test_claimLockForRemoval_whenTheLockWasReplaced_shouldRestoreIt(self):
        """The identity read after the move is what makes the claim safe: a lock
        replaced between the judgement and the move is a live holder's lock, and
        deleting it would admit a third process while that holder mutates."""
        self.hold_lock_asAnotherProcess(pid=self._unused_pid())
        judged = runner._read_repo_mutation_lock_identity()
        # The other contender won the race: it removed the judged lock and created
        # its own at the same path before this process moved anything.
        shutil.rmtree(self.lock_path)
        self.hold_lock_asAnotherProcess(pid=self._live_pid())
        (self.lock_path / "owner").write_text("the-winner")

        self.assertFalse(runner._claim_repo_mutation_lock_for_removal(judged),
                         "the claim must report that it did not remove the judged lock")
        self.assertTrue(self.lock_path.is_dir(), "the winner's lock must be restored")
        self.assertEqual((self.lock_path / "owner").read_text(), "the-winner")
        self.assertEqual(list(self.root.glob("repo-mutation.lock.claimed.*")), [],
                         "a restored lock must not leave the moved-aside copy behind")

    def test_stealStaleLock_whenTheLockIsReplacedDuringTheClaim_shouldNotDeleteTheReplacement(self):
        """The window between judging a lock stale and removing it is where the race
        lives. Here another process substitutes its own lock inside that window; the
        reclaim must notice and hand the checkout back instead of deleting it."""
        self.hold_lock_asAnotherProcess(pid=self._unused_pid())
        real_rename = os.rename

        def substitute_then_rename(src, dst):
            if src == str(self.lock_path) and not (self.lock_path / "owner").exists():
                shutil.rmtree(self.lock_path)
                self.hold_lock_asAnotherProcess(pid=self._live_pid())
                (self.lock_path / "owner").write_text("the-winner")
            return real_rename(src, dst)

        with patch.object(runner.os, "rename", side_effect=substitute_then_rename):
            self.assertFalse(runner._steal_stale_repo_mutation_lock(),
                             "the reclaim must not report stealing a lock it did not remove")

        self.assertTrue(self.lock_path.is_dir(), "the live holder's lock must survive")
        self.assertEqual((self.lock_path / "owner").read_text(), "the-winner")

    def test_stealStaleLock_whenTheLockIsReplacedDuringAStaleSteal_shouldNotDeleteTheReplacement(self):
        """The same window on the age-based path, which judges by mtime rather than
        by the recorded pid."""
        self.hold_lock_asAnotherProcess(age_seconds=9999)
        real_rename = os.rename

        def substitute_then_rename(src, dst):
            if src == str(self.lock_path) and not (self.lock_path / "owner").exists():
                shutil.rmtree(self.lock_path)
                self.hold_lock_asAnotherProcess(pid=self._live_pid())
                (self.lock_path / "owner").write_text("the-winner")
            return real_rename(src, dst)

        with patch.object(runner.os, "rename", side_effect=substitute_then_rename):
            self.assertFalse(runner._steal_stale_repo_mutation_lock())

        self.assertTrue(self.lock_path.is_dir(), "the live holder's lock must survive")
        self.assertEqual((self.lock_path / "owner").read_text(), "the-winner")

    def test_releaseLock_whenItIsReplacedBetweenTheOwnerCheckAndTheDelete_shouldNotDeleteTheReplacement(self):
        """Release is the mirror of the reclaim race: checking the owner and then
        deleting lets a steal move this lock aside and create its own in between,
        and the delete would then remove the thief's live lock."""
        runner._repo_mutation_lock_token = "our-token"
        self.hold_lock_asAnotherProcess(pid=self._unused_pid())
        (self.lock_path / "owner").write_text("our-token")
        real_rename = os.rename

        def substitute_then_rename(src, dst):
            if src == str(self.lock_path) and (self.lock_path / "owner").read_text() == "our-token":
                shutil.rmtree(self.lock_path)
                self.hold_lock_asAnotherProcess(pid=self._live_pid())
                (self.lock_path / "owner").write_text("the-winner")
            return real_rename(src, dst)

        with patch.object(runner.os, "rename", side_effect=substitute_then_rename):
            runner._release_repo_mutation_lock()

        self.assertTrue(self.lock_path.is_dir(), "the new holder's lock must survive")
        self.assertEqual((self.lock_path / "owner").read_text(), "the-winner")

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
