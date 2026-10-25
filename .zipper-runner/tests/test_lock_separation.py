"""Tests for the intake/babysit lock separation in runner.py.

The babysit timer fires every 5 minutes while an intake run holds its lock for the
whole of a 20-42 minute agent mission, so most babysit firings used to exit at the
lock having done nothing (67% of the last 60, 47 of 235 overall). Babysitting is
bounded work on an existing PR, so it takes its own lock; an intake run takes that
same lock only around its own babysit pass. The checkout stays protected by the
shared main-checkout lock, which both processes take around git mutations.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import fcntl
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import patch

import gitenv  # noqa: F401  # Git identity for fixture repositories

_STATE_DIR = tempfile.mkdtemp(prefix="zipper-lock-separation-state-")
os.environ["STATE_DIR"] = _STATE_DIR

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402
runner.STATE_DIR = _STATE_DIR


class LockSeparationUnitTests(unittest.TestCase):
    """The primitives the two processes agree on."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-lock-separation-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved = (runner.LOCK_FILE_PATH, runner.BABYSIT_LOCK_PATH, runner.STATE_DIR)
        runner.LOCK_FILE_PATH = str(self.root / "runner.lock")
        runner.BABYSIT_LOCK_PATH = str(self.root / "babysit.lock")
        runner.STATE_DIR = str(self.root / "state")

    def tearDown(self):
        (runner.LOCK_FILE_PATH, runner.BABYSIT_LOCK_PATH, runner.STATE_DIR) = self._saved

    def hold(self, path: str):
        """Hold a lock from this process, as another invocation would."""
        handle = open(path, "a")
        self.addCleanup(handle.close)
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return handle

    def acquire(self, path: str):
        """Take a lock through the runner's own helper, closing it on cleanup."""
        handle = runner._acquire_lock(path, 0, "Test")
        if handle is not None:
            self.addCleanup(handle.close)
        return handle

    def test_acquireLock_whenIntakeLockIsHeld_shouldStillTakeTheBabysitLock(self):
        """This is the whole point of the change: a babysit firing must not be
        blocked by an intake run that is mid-mission."""
        self.hold(runner.LOCK_FILE_PATH)

        acquired = self.acquire(runner.BABYSIT_LOCK_PATH)

        self.assertIsNotNone(acquired, "the intake lock must not block babysitting")
        self.assertTrue(runner._lock_is_held(runner.LOCK_FILE_PATH),
                        "the intake holder's lock must be untouched")

    def test_acquireLock_whenBabysitLockIsHeld_shouldRefuseASecondBabysit(self):
        """Two babysit passes must never run at once: they would both mutate the
        same worktrees and the same PR."""
        self.hold(runner.BABYSIT_LOCK_PATH)

        self.assertIsNone(runner._acquire_lock(runner.BABYSIT_LOCK_PATH, 0, "Babysit"))

    def test_acquireLock_whenBabysitLockIsHeld_shouldNotBlockTheIntakeLock(self):
        """The intake lock and the babysit lock are independent, so a babysit pass
        must not stop an intake run from starting."""
        self.hold(runner.BABYSIT_LOCK_PATH)

        self.assertIsNotNone(self.acquire(runner.LOCK_FILE_PATH))

    def test_releaseLock_shouldFreeTheLockForTheNextHolder(self):
        """An intake run releases the babysit lock after its pass. If it did not,
        every later babysit firing would skip until the process exited."""
        acquired = runner._acquire_lock(runner.BABYSIT_LOCK_PATH, 0, "Babysit")
        self.assertIsNotNone(acquired)

        runner._release_lock(acquired)

        again = self.acquire(runner.BABYSIT_LOCK_PATH)
        self.assertIsNotNone(again, "the released lock must be acquirable again")

    def test_releaseLock_whenGivenNone_shouldBeANoOp(self):
        """The intake path releases a lock it may never have taken."""
        runner._release_lock(None)

    def test_status_reportsBothLocksIndependently(self):
        """`--status` is how an operator tells which half is stuck."""
        self.hold(runner.LOCK_FILE_PATH)

        self.assertTrue(runner._lock_is_held(runner.LOCK_FILE_PATH))
        self.assertFalse(runner._lock_is_held(runner.BABYSIT_LOCK_PATH))
        self.assertFalse(runner._lock_is_held(str(self.root / "absent.lock")))


class HeartbeatSeparationTests(unittest.TestCase):
    """Each lock needs its own heartbeat, or one kind's reclaim judges the other."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-heartbeat-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved = (runner.STATE_DIR, runner.MAX_RUN_SECONDS)
        runner.STATE_DIR = str(self.root)
        # The reclaim bound is 12h by default, so a 2h-old heartbeat is fresh under
        # it. These tests are about which heartbeat is consulted, not the bound.
        runner.MAX_RUN_SECONDS = 3600

    def tearDown(self):
        runner.STATE_DIR, runner.MAX_RUN_SECONDS = self._saved

    def write_heartbeat(self, kind, pid, pgid, started):
        with open(runner._heartbeat_path(kind), "w") as f:
            import json
            json.dump({"pid": pid, "pgid": pgid, "started": started.isoformat()}, f)

    def test_heartbeatPaths_areDistinctPerLock(self):
        self.assertNotEqual(runner._heartbeat_path("runner"), runner._heartbeat_path("babysit"))
        self.assertTrue(runner._heartbeat_path("runner").endswith("runner-heartbeat.json"))

    def test_writeHeartbeat_forBabysit_shouldNotClobberTheRunnerHeartbeat(self):
        """Both locks are held at once when an intake run babysits, so a shared file
        would let each overwrite the other's holder record."""
        runner._write_heartbeat("runner")
        runner_heartbeat = Path(runner._heartbeat_path("runner")).read_text()

        runner._write_heartbeat("babysit")

        self.assertEqual(Path(runner._heartbeat_path("runner")).read_text(), runner_heartbeat)
        self.assertTrue(Path(runner._heartbeat_path("babysit")).exists())

    def test_reclaimStaleLock_forRunner_shouldNeverConsultTheBabysitHeartbeat(self):
        """A stale babysit heartbeat must not authorize killing the intake lock's
        holder: the two locks are independent, so their reclaim decisions must be."""
        holder = subprocess.Popen(["sleep", "60"], start_new_session=True)
        self.addCleanup(holder.wait)
        self.write_heartbeat("babysit", holder.pid, os.getpgid(holder.pid),
                             datetime.now(timezone.utc) - timedelta(hours=2))

        with patch.object(runner, "send_email"):
            self.assertFalse(runner._reclaim_stale_lock("runner"),
                             "a babysit heartbeat must never authorize reclaiming the intake lock")

        self.assertIsNone(holder.poll(), "the live holder must not have been killed")
        holder.terminate()

    def test_reclaimStaleLock_forBabysit_shouldNotReclaimAFreshHolder(self):
        self.write_heartbeat("babysit", os.getpid(), os.getpgid(0), datetime.now(timezone.utc))

        self.assertFalse(runner._reclaim_stale_lock("babysit"))

    def test_reclaimStaleLock_forBabysit_shouldReclaimADeadHolder(self):
        dead = subprocess.Popen([sys.executable, "-c", "pass"])
        dead.wait()
        self.write_heartbeat("babysit", dead.pid, dead.pid,
                             datetime.now(timezone.utc) - timedelta(hours=2))

        with patch.object(runner, "send_email"), \
                patch.object(runner, "_should_send_rate_limited", return_value=False):
            self.assertTrue(runner._reclaim_stale_lock("babysit"))


class BabysitLockIntegrationTests(unittest.TestCase):
    """A real babysit invocation, against a real checkout and a real held lock."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-babysit-lock-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.base = self.root / "runner"
        self.base.mkdir()
        shutil.copy(Path(__file__).resolve().parents[1] / "runner.py", self.base / "runner.py")
        (self.base / "plugins").mkdir()
        (self.base / "AGENT_PREFERENCES.md").write_text(
            "| Agent | Model | Priority |\n| droid | default | 1 |\n")
        (self.root / "worktrees").mkdir()
        self._stub_gh()
        self._make_repo()

    def _stub_gh(self):
        """A `gh` that answers the read-only calls startup makes with empty lists.

        Without it an intake run would query the real repository: slow, not
        hermetic, and it makes the test depend on GitHub being reachable.

        When `GH_REACHED` is set, the issue-list call parks until `GH_GATE` exists.
        That is what lets a test observe the checkout while the intake process is
        still alive, after its babysit pass and before it picks up work.
        """
        bin_dir = self.root / "stub-bin"
        bin_dir.mkdir()
        gh = bin_dir / "gh"
        gh.write_text(
            "#!/bin/sh\n"
            "case \"$*\" in\n"
            "  *\"issue list\"*)\n"
            "    if [ -n \"$GH_REACHED\" ]; then\n"
            "      : > \"$GH_REACHED\"\n"
            "      while [ ! -e \"$GH_GATE\" ]; do sleep 0.1; done\n"
            "    fi\n"
            "    ;;\n"
            "esac\n"
            "echo '[]'\n"
        )
        gh.chmod(0o755)
        self.stub_bin = bin_dir

    def _git(self, *args, cwd=None):
        return subprocess.run(["git", *args], cwd=str(cwd or self.repo),
                              capture_output=True, text=True, check=True)

    def _make_repo(self):
        """A checkout on main with a working origin, so startup does not abort."""
        origin = self.root / "origin.git"
        self.repo = self.root / "repo"
        subprocess.run(["git", "init", "--quiet", "--bare", "--initial-branch=main", str(origin)],
                       check=True, capture_output=True)
        subprocess.run(["git", "clone", "--quiet", str(origin), str(self.repo)],
                       check=True, capture_output=True)
        (self.repo / "f.txt").write_text("one\n")
        self._git("add", "-A")
        self._git("commit", "--quiet", "-m", "c1")
        self._git("branch", "-M", "main")
        self._git("push", "--quiet", "-u", "origin", "main")

    def _env(self):
        return {**os.environ,
                "PATH": f"{self.stub_bin}{os.pathsep}{os.environ['PATH']}",
                "RUNNER_BASE": str(self.base), "REPO_PATH": str(self.repo),
                "WORKTREES_BASE": str(self.root / "worktrees"),
                "PLUGINS_DIR": str(self.base / "plugins"),
                "LOCK_FILE_PATH": str(self.base / "runner.lock"),
                "BABYSIT_LOCK_PATH": str(self.base / "babysit.lock"),
                "STATE_DIR": str(self.root / "state"),
                # The intake lock waits RUNNER_LOCK_TIMEOUT (120s) before giving up.
                # These tests are about *which* lock each mode takes, not about how
                # long it waits, and a 120s wait would race the subprocess timeout.
                "RUNNER_LOCK_TIMEOUT": "0"}

    def run_babysit(self, env):
        return subprocess.run(
            [sys.executable, str(self.base / "runner.py"), "--babysit-only", "--dry-run"],
            env=env, capture_output=True, text=True, timeout=120)

    def run_intake(self, env):
        """An intake run that stops before issue pickup.

        `MAX_ACTIVE_WORKTREES=0` makes the capacity check false, so the run finishes
        after its babysit pass without reaching `select_next_issue()` and its `gh`
        calls — which keeps this about the locks rather than about GitHub.
        """
        return subprocess.run(
            [sys.executable, str(self.base / "runner.py"), "--dry-run"],
            env={**env, "MAX_ACTIVE_WORKTREES": "0"}, capture_output=True, text=True, timeout=120)

    def assert_unlocked(self, path):
        probe = open(path, "a")
        try:
            fcntl.flock(probe, fcntl.LOCK_EX | fcntl.LOCK_NB)
            fcntl.flock(probe, fcntl.LOCK_UN)
        except OSError:
            self.fail(f"{path} is still locked")
        finally:
            probe.close()

    def test_babysitRun_whenIntakeLockIsHeld_shouldRunInsteadOfExiting(self):
        """The starvation this change removes: intake holds its lock for a whole
        mission, and every babysit firing in that window used to do nothing."""
        env = self._env()
        holder = open(env["LOCK_FILE_PATH"], "a")
        self.addCleanup(holder.close)
        fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)

        result = self.run_babysit(env)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn("Another instance of the runner is active", result.stdout)
        self.assertIn("Babysit-only: new issue pickup disabled", result.stdout,
                      "the run must reach the end of its pass, not stop at the lock")
        # Still held by this process, so the run did not steal or release it.
        with self.assertRaises(OSError):
            probe = open(env["LOCK_FILE_PATH"], "a")
            try:
                fcntl.flock(probe, fcntl.LOCK_EX | fcntl.LOCK_NB)
                fcntl.flock(probe, fcntl.LOCK_UN)
                self.fail("the intake holder's lock must survive a babysit run")
            finally:
                probe.close()

    def test_babysitRun_whenBabysitLockIsHeld_shouldExitAtTheLock(self):
        """The separation must still serialize two babysit passes."""
        env = self._env()
        holder = open(env["BABYSIT_LOCK_PATH"], "a")
        self.addCleanup(holder.close)
        fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)

        result = self.run_babysit(env)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Another instance of the runner is active", result.stdout)

    def test_intakeRun_shouldReleaseTheBabysitLockBeforeItStartsWorking(self):
        """The intake process stays alive for a whole mission after its babysit pass.
        If it kept this lock, every babysit firing during that mission would skip —
        exactly the starvation the separate lock exists to remove. The lock must
        therefore be free while the run is still alive, not merely at process exit
        (the kernel releases a lock when its holder dies, which would hide the bug).
        """
        reached = self.root / "gh-reached"
        gate = self.root / "gh-gate"
        env = {**self._env(), "GH_REACHED": str(reached), "GH_GATE": str(gate)}
        process = subprocess.Popen(
            [sys.executable, str(self.base / "runner.py"), "--dry-run"],
            env={**env, "MAX_ACTIVE_WORKTREES": "1"},
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        self.addCleanup(process.kill)
        self.addCleanup(gate.touch)

        deadline = time.monotonic() + 60
        while not reached.exists() and time.monotonic() < deadline and process.poll() is None:
            time.sleep(0.1)
        self.assertTrue(reached.exists(),
                        "the run must reach issue pickup, which is after its babysit pass")
        self.assertIsNone(process.poll(), "the run must still be alive for this to prove anything")

        self.assert_unlocked(env["BABYSIT_LOCK_PATH"])

        gate.touch()
        out, _ = process.communicate(timeout=60)
        self.assertEqual(process.returncode, 0, out)

    def test_intakeRun_whenBabysitLockIsHeld_shouldSkipItsPassAndStillFinish(self):
        """A dedicated babysit firing may hold the lock when intake reaches its own
        pass. Skipping is correct; failing to release, or exiting, is not."""
        env = self._env()
        holder = open(env["BABYSIT_LOCK_PATH"], "a")
        self.addCleanup(holder.close)
        fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)

        result = self.run_intake(env)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Babysit pass skipped: another babysit is active", result.stdout)
        self.assertIn("At capacity", result.stdout, "the run must still finish")
        self.assertTrue(runner._lock_is_held(env["BABYSIT_LOCK_PATH"]),
                        "the other holder's lock must survive")

    def test_intakeRun_whenIntakeLockIsHeld_shouldExitAtTheLock(self):
        """The intake lock must still serialize two intake runs."""
        env = self._env()
        holder = open(env["LOCK_FILE_PATH"], "a")
        self.addCleanup(holder.close)
        fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)

        result = self.run_intake(env)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Another instance of the runner is active", result.stdout)

    def test_status_whenIntakeLockIsHeld_shouldReportBabysitUnlocked(self):
        env = self._env()
        holder = open(env["LOCK_FILE_PATH"], "a")
        self.addCleanup(holder.close)
        fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)

        result = subprocess.run(
            [sys.executable, str(self.base / "runner.py"), "--status", "--dry-run"],
            env=env, capture_output=True, text=True, timeout=120)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Runner Lock Status: ACTIVE / LOCKED", result.stdout)
        self.assertIn("Babysit Lock Status: IDLE / UNLOCKED", result.stdout)

    def test_status_shouldNotCreateTheMainCheckoutLockItReportsOn(self):
        """`_lock_is_held` probed with `open(path, "a")`, which creates the file.

        `show_status` probes the main-checkout lock, which is a *directory*, so the
        probe manufactured a zero-byte regular file where the protocol requires a
        directory: every later mkdir failed with EEXIST and read as "held", wedging
        deploys and runner mutations for the 20-minute stale bound. This drives the
        real CLI, which is the only place the two halves meet.
        """
        env = self._env()
        holder = open(env["LOCK_FILE_PATH"], "a")
        self.addCleanup(holder.close)
        fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)
        lock_path = Path(env["RUNNER_BASE"]) / "repo-mutation.lock"
        self.assertFalse(lock_path.exists())

        result = subprocess.run(
            [sys.executable, str(self.base / "runner.py"), "--status", "--dry-run"],
            env=env, capture_output=True, text=True, timeout=120)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Main checkout Lock Status: IDLE / UNLOCKED", result.stdout)
        self.assertFalse(lock_path.exists(),
                         "a --status run must not leave a file at the directory-lock path")

    def test_status_whenTheMainCheckoutLockIsHeld_shouldReportItHeld(self):
        """The other half of the same bug: `open(dir, "a")` raised IsADirectoryError,
        so `--status` reported IDLE / UNLOCKED while the checkout was genuinely
        locked. A diagnostic that reports the opposite of the truth is worse than
        none, because it is what an operator checks before intervening."""
        env = self._env()
        intake_holder = open(env["LOCK_FILE_PATH"], "a")
        self.addCleanup(intake_holder.close)
        fcntl.flock(intake_holder, fcntl.LOCK_EX | fcntl.LOCK_NB)
        lock_path = Path(env["RUNNER_BASE"]) / "repo-mutation.lock"
        lock_path.mkdir()
        self.addCleanup(shutil.rmtree, lock_path, ignore_errors=True)

        result = subprocess.run(
            [sys.executable, str(self.base / "runner.py"), "--status", "--dry-run"],
            env=env, capture_output=True, text=True, timeout=120)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Main checkout Lock Status: ACTIVE / LOCKED", result.stdout)
        self.assertTrue(lock_path.is_dir(), "the held lock must be left as a directory")


if __name__ == "__main__":
    unittest.main()
