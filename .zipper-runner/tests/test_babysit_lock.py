"""Tests for the releasable babysit lock in runner.py.

A babysit pass is not bounded work: it dispatches agent missions, and a mission runs
for tens of minutes. Holding the babysit lock across one starves the 5-minute babysit
timer for the whole mission, so its firings exit at the lock having done nothing --
measured on the host at 23 of 24 firings over two hours, with the last productive one
two hours before the measurement. Splitting the lock away from the intake lock did
not fix that; it moved the starvation from `runner.lock` to this one, because the
pass itself runs the mission.

The mission does not need this lock: `_worktree_mission_lock` already stops two runs
driving one worktree. So the pass lock is dropped for that stretch and taken again
afterwards, which keeps the bounded bookkeeping serialized without starving the timer.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import fcntl
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone
from io import StringIO
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402

# A genuinely separate process taking the pass lock, so the property is exercised
# against production's real shape (two processes) rather than only two descriptors.
# `flock` is per open file description, so a second `open()` in one process is
# already independent -- the subprocess is here to prove it end to end.
TAKER_PROGRAM = """
import sys, time
sys.path.insert(0, sys.argv[1])
import runner

runner.STATE_DIR = sys.argv[3]
print("state=" + runner.STATE_DIR, flush=True)
lock = runner._BabysitLock(sys.argv[2], 0)
if lock.acquire():
    print("taken", flush=True)
    time.sleep(60)
else:
    print("busy", flush=True)
"""


def _dead_pid() -> int:
    """A pid that is provably not running: spawn, then reap, so `kill(pid, 0)` fails
    and the reclaim takes the dead-holder path rather than the SIGKILL path."""
    process = subprocess.Popen([sys.executable, "-c", "pass"])
    process.wait()
    return process.pid


def _stale_heartbeat(kind: str, pid: int) -> None:
    """Write a heartbeat for `kind` that is past `MAX_RUN_SECONDS`, as a wedged
    holder's would be."""
    os.makedirs(runner.STATE_DIR, exist_ok=True)
    started = (datetime.now(timezone.utc)
               - timedelta(seconds=runner.MAX_RUN_SECONDS + 60)).isoformat()
    with open(runner._heartbeat_path(kind), "w") as handle:
        json.dump({"pid": pid, "pgid": pid, "started": started}, handle)


class _BabysitLockTests(unittest.TestCase):
    """The lock object itself: acquire, release, and drop-for-a-stretch."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-babysit-lock-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.path = str(self.root / "babysit.lock")
        # A refused acquire runs `_reclaim_stale_lock`, which reads this kind's
        # heartbeat and, if it is stale and its pid is alive, SIGKILLs that process
        # group and sends an email. Pointed at the real state dir, running this suite
        # on a host with a live runner could kill it. Every other class here isolates
        # the directory for the same reason.
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.addCleanup(self._restore_state)

    def _restore_state(self) -> None:
        runner.STATE_DIR = self._saved_state

    def test_stateDir_shouldBeIsolatedToThisTest(self):
        """The guard on the isolation above: a reclaim must never read, or write,
        the real state directory."""
        self.assertTrue(runner.STATE_DIR.startswith(str(self.root)))

    def test_refusedAcquire_shouldConsultTheStaleLockReclaim(self):
        """A refused acquire is where the reclaim runs, which is why the state dir
        above has to be isolated: on a host whose real heartbeat is stale and names a
        live process, this path SIGKILLs that process group. With a *dead* holder it
        reclaims without killing anything, so the path is safe to exercise here."""
        holder = self._lock()
        self.assertTrue(holder.acquire())
        _stale_heartbeat("babysit", _dead_pid())

        buffer = StringIO()
        with redirect_stdout(buffer):
            refused = self._lock().acquire()

        self.assertFalse(refused, "the holder is still alive, so the lock stays held")
        self.assertIn("reclaiming its lock", buffer.getvalue(),
                      "the refused acquire must reach the stale-lock reclaim")

    def _lock(self) -> "runner._BabysitLock":
        lock = runner._BabysitLock(self.path, 0)
        self.addCleanup(lock.release)
        return lock

    def test_acquire_whenFree_shouldHoldIt(self):
        lock = self._lock()
        self.assertTrue(lock.acquire())
        self.assertTrue(lock.held)

    def test_acquire_whenAnotherHolderExists_shouldRefuseImmediately(self):
        """Non-blocking is the contract: the timer retries in minutes, so waiting
        inside a firing only delays the next one."""
        first, second = self._lock(), self._lock()
        self.assertTrue(first.acquire())

        started = time.monotonic()
        refused = second.acquire()
        elapsed = time.monotonic() - started

        self.assertFalse(refused)
        self.assertLess(elapsed, 5, "the refusal must not wait on the holder")

    def test_acquire_whenAlreadyHeld_shouldBeIdempotent(self):
        """The pass acquires once in `main()` and the worktree lock takes it again
        after a mission, so a second acquire must not deadlock against itself."""
        lock = self._lock()
        self.assertTrue(lock.acquire())
        self.assertTrue(lock.acquire(), "re-acquiring a lock this instance holds must succeed")
        self.assertTrue(lock.held)

    def test_release_shouldLetAnotherHolderTakeIt(self):
        first, second = self._lock(), self._lock()
        self.assertTrue(first.acquire())
        self.assertFalse(second.acquire())

        first.release()

        self.assertFalse(first.held)
        self.assertTrue(second.acquire(), "a released lock must become available")

    def test_release_whenNotHeld_shouldBeSafe(self):
        lock = self._lock()
        lock.release()
        self.assertFalse(lock.held)

    def test_released_whenHeld_shouldDropItForTheStretchAndTakeItBack(self):
        """Nobody else takes it, so the same instance gets it back: the pass keeps
        its bookkeeping serialized for the rest of the run."""
        lock, other = self._lock(), self._lock()
        self.assertTrue(lock.acquire())

        with lock.released():
            self.assertFalse(lock.held, "the lock must be down for the stretch")
            self.assertTrue(other.acquire(), "another firing must get in during the stretch")
            other.release()

        self.assertTrue(lock.held, "the lock must come back once the stretch ends")

    def test_released_whenNotHeld_shouldBeANoOp(self):
        """An intake run's babysit pass holds the lock, but the intake path also
        enters the worktree lock without it. That must not acquire it as a side
        effect, or every intake mission would grab the babysit lock it never wanted."""
        lock = self._lock()

        with lock.released():
            self.assertFalse(lock.held)

        self.assertFalse(lock.held, "a lock that was never held must stay unheld")

    def test_released_whenStolenMidStretch_shouldNotErrorAndStayUnheld(self):
        """Another firing taking the lock mid-mission is the outcome this exists to
        allow, not an error: the run finishes its pass without the lock."""
        lock, other = self._lock(), self._lock()
        self.assertTrue(lock.acquire())

        with lock.released():
            self.assertTrue(other.acquire(), "another firing takes the pass lock")

        self.assertFalse(lock.held,
                         "a lock taken by someone else must not be reported as held")
        # The stolen lock must still belong to the thief, not have been released here.
        self.assertFalse(self._lock().acquire(),
                         "the thief's lock must survive the first holder's stretch")


class _WorktreeMissionLockInteractionTests(unittest.TestCase):
    """The property that matters: a mission in flight must not starve the timer."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-babysit-mission-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.lock = runner._BabysitLock(str(self.root / "babysit.lock"), 0)
        self.addCleanup(self.lock.release)

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def _worktree(self, name: str) -> Path:
        path = self.root / name
        path.mkdir()
        return path

    def test_missionInFlight_shouldNotHoldThePassLock(self):
        """The starvation fix. While a worktree is being driven, the pass lock must
        be down so a dedicated babysit firing can run instead of exiting at it."""
        wt_path = self._worktree("issue-1")
        rival = runner._BabysitLock(self.lock._path, 0)
        self.addCleanup(rival.release)
        with patch.object(runner, "BABYSIT_LOCK", self.lock):
            self.assertTrue(self.lock.acquire())
            with runner._worktree_mission_lock(wt_path) as may_drive:
                self.assertTrue(may_drive)
                self.assertFalse(self.lock.held,
                                 "a mission must not hold the pass lock")
                self.assertTrue(rival.acquire(),
                                "another firing must be able to run during a mission")
                rival.release()
            self.assertTrue(self.lock.held, "the pass lock must return after the mission")

    def test_busyWorktree_shouldLeaveThePassLockAlone(self):
        """A refusal must not drop the pass lock: this run still has bookkeeping to
        serialize, and it is not the one driving the worktree."""
        wt_path = self._worktree("issue-2")
        # Hold the worktree lock on a raw descriptor, which reads as another process
        # because `_worktree_mission_lock` is reentrant only for its own instances.
        raw = open(runner._worktree_mission_lock_path(wt_path), "a")
        self.addCleanup(raw.close)
        fcntl.flock(raw, fcntl.LOCK_EX | fcntl.LOCK_NB)

        with patch.object(runner, "BABYSIT_LOCK", self.lock):
            self.assertTrue(self.lock.acquire())
            with runner._worktree_mission_lock(wt_path) as may_drive:
                self.assertFalse(may_drive)
                self.assertTrue(self.lock.held,
                                "a worktree this run does not own must not drop the lock")

    def test_intakeMissionWithoutThePassLock_shouldNotAcquireIt(self):
        """An intake run enters the worktree lock without ever holding the pass lock.
        The drop-for-the-stretch must not turn into an acquire."""
        wt_path = self._worktree("issue-3")
        with patch.object(runner, "BABYSIT_LOCK", self.lock):
            with runner._worktree_mission_lock(wt_path) as may_drive:
                self.assertTrue(may_drive)
                self.assertFalse(self.lock.held)
            self.assertFalse(self.lock.held,
                             "the intake path must not end up holding the pass lock")


class _SeparateProcessTests(unittest.TestCase):
    """Two real processes, which is the shape production runs in."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-babysit-proc-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.lock = runner._BabysitLock(str(self.root / "babysit.lock"), 0)
        self.addCleanup(self.lock.release)
        self.wt_path = self.root / "issue-4"
        self.wt_path.mkdir()

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def _take_in_subprocess(self) -> "tuple[str, str]":
        """Run a separate process that tries to take the pass lock.

        Returns (marker, state_dir): the marker is "taken" or "busy", and the state
        dir is the one the taker actually used. A refused acquire runs the stale-lock
        reclaim, which reads the heartbeat and can SIGKILL the process group it names,
        so the taker must be on the isolated directory too -- and the test has to check
        that rather than assume it.
        """
        holder_dir = Path(tempfile.mkdtemp(prefix="zipper-taker-"))
        self.addCleanup(shutil.rmtree, holder_dir, ignore_errors=True)
        program = holder_dir / "taker.py"
        program.write_text(TAKER_PROGRAM)
        process = subprocess.Popen(
            [sys.executable, str(program), str(Path(runner.__file__).resolve().parent),
             self.lock._path, runner.STATE_DIR],
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        self.addCleanup(process.stdout.close)
        self.addCleanup(process.wait)
        self.addCleanup(process.kill)

        lines = self._await_marker(process)
        # A taker that crashed reports no marker. Defaulting that to "busy" would make
        # the refusal test pass for the wrong reason, so an absent marker is a failure.
        marker = next((line for line in lines if line in ("taken", "busy")), None)
        if marker is None:
            raise AssertionError(f"taker never reported a marker; saw {lines}")
        state = next((line.split("=", 1)[1] for line in lines
                      if line.startswith("state=")), "")
        return marker, state

    @staticmethod
    def _await_marker(process: subprocess.Popen, timeout: float = 30) -> "list[str]":
        """Collect stdout until the taker reports, draining on a thread so a hang
        fails the test instead of wedging it."""
        lines: "list[str]" = []
        done = threading.Event()

        def pump() -> None:
            for line in process.stdout:
                lines.append(line.strip())
                if line.strip() in ("taken", "busy"):
                    break
            done.set()

        threading.Thread(target=pump, daemon=True).start()
        if not done.wait(timeout):
            raise AssertionError(f"taker never reported within {timeout}s; saw {lines}")
        return lines

    def test_separateProcess_canTakeThePassLockWhileAMissionRuns(self):
        """The end-to-end property: during a mission, a different process takes the
        pass lock. Before the fix it reported "busy" and exited having done nothing."""
        with patch.object(runner, "BABYSIT_LOCK", self.lock):
            self.assertTrue(self.lock.acquire())
            with runner._worktree_mission_lock(self.wt_path) as may_drive:
                self.assertTrue(may_drive)
                marker, state = self._take_in_subprocess()

        self.assertEqual(marker, "taken",
                         "a separate firing must take the lock during a mission")
        self.assertEqual(state, runner.STATE_DIR,
                         "the taker must share the isolated state dir, or its refused "
                         "acquire would read the real heartbeat")

    def test_separateProcess_isRefusedWhileThePassLockIsHeld(self):
        """The lock still serializes the bounded part of a pass."""
        with patch.object(runner, "BABYSIT_LOCK", self.lock):
            self.assertTrue(self.lock.acquire())
            marker, state = self._take_in_subprocess()

        self.assertEqual(marker, "busy",
                         "two passes must not run their bookkeeping at once")
        self.assertEqual(state, runner.STATE_DIR,
                         "this is the refused-acquire case, so the isolation matters most")


class _WiringTests(unittest.TestCase):
    """The module-level lock has to be the one `main()` and the worktree lock use."""

    def test_globalLock_shouldBeABabysitLockOnTheConfiguredPath(self):
        self.assertIsInstance(runner.BABYSIT_LOCK, runner._BabysitLock)
        self.assertEqual(runner.BABYSIT_LOCK._path, runner.BABYSIT_LOCK_PATH)
        self.assertEqual(runner.BABYSIT_LOCK._timeout, runner.BABYSIT_LOCK_TIMEOUT)

    def test_main_shouldNotAcquireTheBabysitPathDirectly(self):
        """`main()` must go through the droppable lock. Acquiring the path directly
        reintroduces the starvation, because that handle cannot be released for a
        mission and nothing else can tell it apart from the droppable one."""
        source = Path(runner.__file__).read_text()
        acquire_calls = [
            line for line in source.splitlines()
            if "_acquire_lock(" in line and "BABYSIT_LOCK_PATH" in line
        ]
        self.assertEqual(acquire_calls, [],
                         "the babysit lock must be taken through BABYSIT_LOCK, "
                         "not by path, so it can be dropped for a mission")


class _WorktreeDriveGuardTests(unittest.TestCase):
    """Dropping the pass lock for a mission must not let a second firing reset a
    worktree another run's agent is writing.

    `babysit_active_worktrees` is not read-only: `_reconcile_active_worktree` runs
    `git reset --hard` and `git rebase`, and the merged/closed path removes the
    worktree. Before the pass lock was droppable, the pass lock serialized all of
    that; now a firing can run during a mission, so the worktree mission lock has to
    cover the whole per-worktree body rather than only the mission.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-drive-guard-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.wt_path = self.root / "issue-1"
        self.wt_path.mkdir()

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def _rival(self):
        """A second holder of this worktree's lock, on an independent descriptor."""
        handle = open(runner._worktree_mission_lock_path(self.wt_path), "a")
        self.addCleanup(handle.close)
        return handle

    def test_bodyRuns_shouldHoldTheWorktreeLock(self):
        """The guard that closes the reconcile race: a rival must be refused for as
        long as the loop body is executing, not just while a mission is."""
        refused = None
        for _wt in runner._worktrees_we_may_drive([str(self.wt_path)]):
            rival = self._rival()
            try:
                fcntl.flock(rival, fcntl.LOCK_EX | fcntl.LOCK_NB)
                refused = False
            except (IOError, BlockingIOError):
                refused = True

        self.assertTrue(refused, "the worktree lock must cover the whole loop body")

    def test_bodyFinishes_shouldReleaseTheWorktreeLock(self):
        """Holding it past the body would wedge the worktree for every later pass."""
        for _wt in runner._worktrees_we_may_drive([str(self.wt_path)]):
            pass

        rival = self._rival()
        fcntl.flock(rival, fcntl.LOCK_EX | fcntl.LOCK_NB)
        self.assertTrue(True, "the lock must be free once the body has finished")

    def test_worktreeAnotherRunDrives_shouldBeSkippedNotWaitedOn(self):
        rival = self._rival()
        fcntl.flock(rival, fcntl.LOCK_EX | fcntl.LOCK_NB)

        started = time.monotonic()
        driven = list(runner._worktrees_we_may_drive([str(self.wt_path)]))
        elapsed = time.monotonic() - started

        self.assertEqual(driven, [], "a worktree another run drives must not be yielded")
        self.assertLess(elapsed, 5, "the skip must not wait on the other run")

    def test_mixedWorktrees_shouldYieldOnlyTheFreeOne(self):
        free = self.root / "issue-2"
        free.mkdir()
        rival = self._rival()
        fcntl.flock(rival, fcntl.LOCK_EX | fcntl.LOCK_NB)

        driven = list(runner._worktrees_we_may_drive([str(self.wt_path), str(free)]))

        self.assertEqual(driven, [str(free)])


class _SweepSerializationTests(unittest.TestCase):
    """The orphan and Dependabot sweeps are the bounded work the pass lock serializes.

    Once a mission has handed the pass lock to another firing, this run must not also
    run them: two passes would sweep the same PRs with neither holding the lock.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-sweep-guard-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.worktrees = self.root / "worktrees"
        self.worktrees.mkdir()
        self.lock = runner._BabysitLock(str(self.root / "babysit.lock"), 0)
        self.addCleanup(self.lock.release)

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def _run_pass(self, hold: bool) -> "list[str]":
        """Run the pass with no active worktrees, recording which sweeps ran."""
        calls: "list[str]" = []
        with patch.object(runner, "WORKTREES_BASE", str(self.worktrees)), \
                patch.object(runner, "BABYSIT_LOCK", self.lock), \
                patch.object(runner, "babysit_orphaned_issue_prs",
                             side_effect=lambda: calls.append("orphans")), \
                patch.object(runner, "babysit_dependabot_prs",
                             side_effect=lambda: calls.append("dependabot")):
            if hold:
                self.assertTrue(self.lock.acquire())
            runner.babysit_active_worktrees()
        return calls

    def test_sweeps_whenThePassHoldsTheLock_shouldRun(self):
        self.assertEqual(self._run_pass(hold=True), ["orphans", "dependabot"])

    def test_sweeps_whenTheLockWasLost_shouldBeSkipped(self):
        """The other firing that took the lock is running them; this pass must not."""
        self.assertEqual(self._run_pass(hold=False), [])


class _HeartbeatKindTests(unittest.TestCase):
    """The heartbeat a lock writes must be the heartbeat its watchdog reads.

    `_acquire_lock` passes the display form ("Babysit", "Runner") to
    `_reclaim_stale_lock`, while the writers pass the lowercase kind. The two built
    different filenames, so no heartbeat was ever found and the stale-lock watchdog
    failed closed on every lock: a wedged holder could never be reclaimed.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-heartbeat-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root)

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def test_path_shouldNotDependOnKindCase(self):
        for display, written in (("Babysit", "babysit"), ("Runner", "runner")):
            with self.subTest(display=display):
                self.assertEqual(runner._heartbeat_path(display),
                                 runner._heartbeat_path(written))

    def test_reclaim_shouldFindTheHeartbeatTheWriterWrote(self):
        """The end-to-end property: a stale heartbeat written the way `main()` writes
        it must be found by the reclaim path the way `_acquire_lock` asks for it."""
        _stale_heartbeat("babysit", _dead_pid())

        self.assertTrue(runner._reclaim_stale_lock("Babysit"),
                        "the display-form kind must resolve to the written heartbeat")

    def test_reclaim_shouldStillRefuseWhileTheHolderIsWithinItsBudget(self):
        """Normalizing the path must not weaken the age bound."""
        started = datetime.now(timezone.utc).isoformat()
        with open(runner._heartbeat_path("babysit"), "w") as handle:
            json.dump({"pid": os.getpid(), "pgid": os.getpgid(0), "started": started}, handle)

        self.assertFalse(runner._reclaim_stale_lock("Babysit"))


class _PassWorktreeWiringTests(unittest.TestCase):
    """The pass must actually go through the drive guard.

    Testing the generator alone is not enough: the guard only helps if the loop uses
    it. These assert on the worktrees the pass touches, with a free worktree as the
    control so a guard that skipped everything would not read as a pass.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-pass-wiring-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.worktrees = self.root / "worktrees"
        self.worktrees.mkdir()
        self.lock = runner._BabysitLock(str(self.root / "babysit.lock"), 0)
        self.addCleanup(self.lock.release)

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def _run_pass_touching(self, wt_path: Path, lock_it: bool) -> "list":
        """Run the pass over one worktree, returning the calls made inside it."""
        if lock_it:
            rival = open(runner._worktree_mission_lock_path(wt_path), "a")
            self.addCleanup(rival.close)
            fcntl.flock(rival, fcntl.LOCK_EX | fcntl.LOCK_NB)

        calls: "list" = []

        def recording(args, cwd=None, timeout=None, env=None):
            calls.append(cwd)
            return (1, "", "")

        with patch.object(runner, "WORKTREES_BASE", str(self.worktrees)), \
                patch.object(runner, "BABYSIT_LOCK", self.lock), \
                patch.object(runner, "run_cmd", side_effect=recording), \
                patch.object(runner, "babysit_orphaned_issue_prs"), \
                patch.object(runner, "babysit_dependabot_prs"):
            self.assertTrue(self.lock.acquire())
            runner.babysit_active_worktrees()
        return [cwd for cwd in calls if cwd == str(wt_path)]

    def test_worktreeAnotherRunDrives_shouldNotBeTouchedAtAll(self):
        """`_reconcile_active_worktree` resets and rebases; a second firing must not
        reach it for a worktree whose agent is mid-mission."""
        wt_path = self.worktrees / "issue-1"
        wt_path.mkdir()

        self.assertEqual(self._run_pass_touching(wt_path, lock_it=True), [],
                         "a worktree another run drives must not be touched")

    def test_freeWorktree_shouldBeTouched(self):
        """The control. Without this, a guard that skipped every worktree -- or a
        loop that never iterated -- would satisfy the test above."""
        wt_path = self.worktrees / "issue-2"
        wt_path.mkdir()

        self.assertNotEqual(self._run_pass_touching(wt_path, lock_it=False), [],
                            "a free worktree must be processed by the pass")


if __name__ == "__main__":
    unittest.main()
