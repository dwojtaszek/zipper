"""Tests for the per-worktree mission lock in runner.py.

The intake and babysit locks decide *who runs*; neither stops two runs from driving
the same worktree. Measured on the host: 3 of 318 completed runs had an intake and a
babysit mission in one worktree at the same time, one pair overlapping for 40
minutes. Both agents edit the same files and commit to the same branch, so one
side's work is overwritten or its commit orphaned while both report success.

The lock is `flock` on a file per worktree, so the kernel releases it when the
holder dies. These tests use real processes for the exclusion property, because
that is the only way to exercise two independent file descriptors.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import ast
import os
import queue
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

# `runner.STATE_DIR` is deliberately NOT assigned at import time. Discovery imports
# every test module before running any of them, so an assignment here would win over
# the one `test_runner_state` makes for its own fixtures (this module sorts after it)
# and break the tests that compare against their own pinned directory. Each test
# pins and restores the directory in setUp/tearDown instead.

# Holds a worktree's mission lock in its own process and says so, so the parent can
# assert exclusion against a genuinely independent file descriptor. The lock is
# released by the kernel when this process exits, which the release test relies on.
HOLDER_PROGRAM = """
import sys, time
sys.path.insert(0, sys.argv[1])
import runner

runner.STATE_DIR = sys.argv[3]
with runner._worktree_mission_lock(sys.argv[2]) as may_drive:
    print("held" if may_drive else "blocked", flush=True)
    if may_drive:
        time.sleep(60)
"""


def _await_marker(process: subprocess.Popen, timeout: float = 30) -> str:
    """Read a holder's stdout until it reports, skipping import-time chatter.

    Importing `runner` prints the plugin-loader lines, so the marker is not the
    first line. A bare `readline()` would not honour `timeout` at all: it returns
    only when a line arrives or the pipe closes, so a holder that hung would wedge
    the test rather than fail it. Draining the pipe on a thread keeps the deadline
    real for both a hang and a silent exit.
    """
    lines: "queue.Queue[str | None]" = queue.Queue()

    def pump() -> None:
        for line in process.stdout:
            lines.put(line)
        lines.put(None)

    threading.Thread(target=pump, daemon=True).start()

    deadline = time.monotonic() + timeout
    seen: list[str] = []
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AssertionError(f"holder never reported within {timeout}s; saw {seen}")
        try:
            line = lines.get(timeout=remaining)
        except queue.Empty:
            raise AssertionError(f"holder never reported within {timeout}s; saw {seen}")
        if line is None:
            raise AssertionError(f"holder exited without reporting; saw {seen}")
        seen.append(line.strip())
        if seen[-1] in ("held", "blocked"):
            return seen[-1]


def _spawn_holder(wt_path: Path, add_cleanup=None) -> subprocess.Popen:
    """A real second process holding this worktree's lock.

    The program lives in its own temp directory, which is registered for removal
    when `add_cleanup` is supplied: one directory per call would otherwise leak into
    the runner's temp space for every test that holds a lock.
    """
    holder_dir = Path(tempfile.mkdtemp(prefix="zipper-holder-"))
    program = holder_dir / "holder.py"
    program.write_text(HOLDER_PROGRAM)
    process = subprocess.Popen(
        [sys.executable, str(program), str(Path(runner.__file__).resolve().parent),
         str(wt_path), runner.STATE_DIR],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if add_cleanup is not None:
        add_cleanup(process.stdout.close)
        add_cleanup(shutil.rmtree, holder_dir, ignore_errors=True)
    return process


class WorktreeMissionLockTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-mission-lock-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def _worktree(self, name: str) -> Path:
        path = self.root / name
        path.mkdir()
        return path

    def _holder(self, wt_path: Path) -> subprocess.Popen:
        holder = _spawn_holder(wt_path, self.addCleanup)
        # LIFO: kill must be registered last so it runs first. Reversed, wait()
        # blocks for the holder's whole sleep before anything kills it.
        self.addCleanup(holder.wait)
        self.addCleanup(holder.kill)
        self.assertEqual(_await_marker(holder), "held",
                         "the holder process must take the lock before the test proceeds")
        return holder

    def test_lock_whenFree_shouldBeHeldInsideAndReleasedAfter(self):
        wt_path = self._worktree("issue-1")

        with runner._worktree_mission_lock(wt_path) as may_drive:
            self.assertTrue(may_drive)

        # A released flock must let the same process take it again; otherwise the
        # context manager leaked the descriptor and the worktree stays wedged.
        with runner._worktree_mission_lock(wt_path) as may_drive:
            self.assertTrue(may_drive, "the lock must be re-acquirable after release")

    def test_lock_whenAnotherProcessHoldsIt_shouldRefuseWithoutWaiting(self):
        """Two runs must not drive one worktree. The refusal must be immediate:
        a mission runs for tens of minutes, so waiting would hold a whole run open
        behind another when the next timer tick retries in minutes."""
        wt_path = self._worktree("issue-2")
        self._holder(wt_path)

        started = time.monotonic()
        with runner._worktree_mission_lock(wt_path) as may_drive:
            elapsed = time.monotonic() - started

        self.assertFalse(may_drive, "a held worktree lock must refuse a second driver")
        self.assertLess(elapsed, 5, "the refusal must not wait on the holder")

    def test_lock_whenAnotherWorktreeIsHeld_shouldStillAllowThisOne(self):
        """The lock is per worktree. A global lock would serialize every worktree
        and reintroduce the starvation the separate intake and babysit locks removed."""
        held = self._worktree("issue-3")
        free = self._worktree("issue-4")
        self._holder(held)

        with runner._worktree_mission_lock(free) as may_drive:
            self.assertTrue(may_drive, "a lock on one worktree must not block another")

    def test_lock_whenTheHolderDies_shouldBecomeAvailableWithoutCleanup(self):
        """The kernel releases flock on process death, which is why this needs no
        stale-lock convention. A killed mission must not wedge its worktree."""
        wt_path = self._worktree("issue-5")
        holder = self._holder(wt_path)

        with runner._worktree_mission_lock(wt_path) as may_drive:
            self.assertFalse(may_drive)

        holder.kill()
        holder.wait()

        deadline = time.monotonic() + 10
        acquired = False
        while time.monotonic() < deadline:
            with runner._worktree_mission_lock(wt_path) as may_drive:
                if may_drive:
                    acquired = True
                    break
            time.sleep(0.1)
        self.assertTrue(acquired, "a dead holder's worktree lock must be reclaimable")

    def test_lock_whenAcquiredTwiceInOneProcess_shouldReportAlreadyMine(self):
        """An intake run babysits its own worktree as soon as its mission returns,
        so the lock is re-entered by the process that already holds it. `flock` is
        per open file description and refuses a second `open()` even from the
        holder, so without the reentrancy check the run's own babysit pass would
        skip the PR it just published, reporting "another run" that does not exist.
        """
        wt_path = self._worktree("issue-8")

        with runner._worktree_mission_lock(wt_path) as outer:
            self.assertTrue(outer)
            with runner._worktree_mission_lock(wt_path) as nested:
                self.assertTrue(nested, "the holder must be able to re-enter its own lock")

        # The nesting must not have released the lock early: a second process still
        # has to be refused while the outer block runs, and the lock must be free
        # afterwards.
        with runner._worktree_mission_lock(wt_path) as outer:
            self.assertTrue(outer)
            holder = _spawn_holder(wt_path, self.addCleanup)
            try:
                self.assertEqual(_await_marker(holder), "blocked",
                                 "a nested acquire must not weaken exclusion")
            finally:
                holder.kill()
                holder.wait()

        with runner._worktree_mission_lock(wt_path) as may_drive:
            self.assertTrue(may_drive, "the lock must be free once the outer block exits")

    def test_lockPath_shouldDifferPerWorktreeAndBeStable(self):
        first = runner._worktree_mission_lock_path(self._worktree("issue-6"))
        second = runner._worktree_mission_lock_path(self._worktree("issue-7"))
        again = runner._worktree_mission_lock_path(self.root / "issue-6")

        self.assertNotEqual(first, second)
        self.assertEqual(first, again, "the same worktree must always map to one lock file")

    def test_lockPath_shouldSurviveAWorktreePathWithCharactersThatNeedSlugging(self):
        """The lock file name comes from the worktree directory name. A path that is
        not a plain identifier must not produce a path outside STATE_DIR."""
        path = runner._worktree_mission_lock_path(self.root / "issue-9.some/branch name")

        self.assertTrue(path.startswith(runner.STATE_DIR + os.sep),
                        f"the lock must stay inside STATE_DIR, got {path}")
        self.assertNotIn("/", os.path.basename(path))
        self.assertTrue(os.path.basename(path).endswith("-mission.lock"))


class MissionLockStateTests(unittest.TestCase):
    """The lock file's life in `state/`: it must not be aged out, and its holder
    must be visible to `--status`."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-mission-state-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.wt_path = self.root / "issue-1224"
        self.wt_path.mkdir()

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def test_pruneSweep_shouldNeverDeleteAHeldMissionLock(self):
        """`open(path, "a")` does not advance mtime, so an old lock file is old
        forever. Deleting it does not release the lock (the inode lives on), but the
        next acquire creates a new inode that a second process can lock, leaving two
        runs convinced they own the worktree."""
        name = os.path.basename(runner._worktree_mission_lock_path(self.wt_path))
        with runner._worktree_mission_lock(self.wt_path) as may_drive:
            self.assertTrue(may_drive)
            lock_path = runner._worktree_mission_lock_path(self.wt_path)
            ancient = time.time() - (runner.STATE_MAX_AGE_DAYS + 1) * 86400
            os.utime(lock_path, (ancient, ancient))

            runner._prune_stale_state_files()

            self.assertTrue(os.path.exists(lock_path),
                            "the age sweep must not delete a mission lock file")
            self.assertTrue(runner._lock_is_held(lock_path),
                            "the lock must still be held after a prune sweep")

    def test_status_shouldReportTheHolderOfABusyWorktree(self):
        """A mission lock has no heartbeat and is deliberately not reclaimed by the
        watchdog, so without a holder line a wedged worktree is indistinguishable
        from an idle one."""
        with runner._worktree_mission_lock(self.wt_path) as may_drive:
            self.assertTrue(may_drive)
            described = runner._describe_worktree_mission_lock(self.wt_path)

        self.assertIn(f"pid {os.getpid()}", described)
        self.assertIn("held", described)

        self.assertEqual(runner._describe_worktree_mission_lock(self.wt_path), "free")

    def test_status_shouldSaySoWhenTheHolderDidNotRecordItself(self):
        """The recorded pid is what tells an operator whether removing the file is
        safe, so an unreadable record must be reported rather than assumed idle."""
        path = runner._worktree_mission_lock_path(self.wt_path)
        with runner._worktree_mission_lock(self.wt_path) as may_drive:
            self.assertTrue(may_drive)
            with open(path, "w", encoding="utf-8") as f:
                f.write("not json")

            described = runner._describe_worktree_mission_lock(self.wt_path)

        self.assertIn("HELD", described)
        self.assertIn("unrecorded", described)


class MissionDispatchCoverageTests(unittest.TestCase):
    """Every agent mission must be dispatched under a worktree lock.

    The two guards inside the long `while True` loops cannot be driven in a unit
    test without standing up the whole runner, and they are the parts of the change
    a programmatic re-indent could silently misplace. Parsing the module pins the
    invariant directly: a new dispatch site, or a guard deleted from an existing
    one, fails here.
    """

    def _mission_lock_names(self, node: ast.With) -> bool:
        return any(
            isinstance(item.context_expr, ast.Call)
            and getattr(item.context_expr.func, "id", "") == "_worktree_mission_lock"
            for item in node.items)

    def test_everyMissionDispatch_shouldBeInsideAWorktreeLock(self):
        source = Path(runner.__file__).read_text(encoding="utf-8")
        tree = ast.parse(source)

        dispatches = [
            node for node in ast.walk(tree)
            if isinstance(node, ast.Call) and getattr(node.func, "attr", "") == "run_mission"
        ]
        self.assertEqual(len(dispatches), 3,
                         "the runner dispatches missions from exactly three sites; a new "
                         "one must be wrapped in _worktree_mission_lock and counted here")

        wrapped = 0
        for call in dispatches:
            for node in ast.walk(tree):
                if not isinstance(node, ast.With) or not self._mission_lock_names(node):
                    continue
                if node.lineno <= call.lineno <= node.end_lineno:
                    wrapped += 1
                    break
        self.assertEqual(wrapped, len(dispatches),
                         "every run_mission call must sit inside a _worktree_mission_lock block")


class BabysitMissionExclusionTests(unittest.TestCase):
    """The lock's effect at the call site, not just on the primitive."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="zipper-mission-exclusion-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self._saved_state = runner.STATE_DIR
        runner.STATE_DIR = str(self.root / "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.wt_path = self.root / "issue-1224"
        self.wt_path.mkdir()
        self.dispatched = []
        self.held_during_dispatch = []

    def tearDown(self):
        runner.STATE_DIR = self._saved_state

    def _run_babysit(self) -> None:
        """Drive the real `_babysit_with_fallback` with a plugin that records dispatch."""
        lock_path = runner._worktree_mission_lock_path(str(self.wt_path))

        def run_mission(_self, *args, **_kwargs):
            # Sampled from inside the mission: the lock must cover the agent's work,
            # not just be taken and released around the call. An implementation that
            # released before dispatching would still pass the refusal tests.
            self.dispatched.append(args)
            self.held_during_dispatch.append(runner._lock_is_held(lock_path))
            return (0, "", "")

        plugin = type("_Plugin", (), {"run_mission": run_mission})()

        def snapshot(*_args, **_kwargs):
            return ("OPEN", 1, "head-sha")

        with patch.object(runner, "AGENT_CANDIDATES", [("stub", "stub-model")]), \
                patch.dict(runner.AGENT_PLUGINS, {"stub": plugin}, clear=True), \
                patch.object(runner, "_worktree_backoff_should_skip", return_value=False), \
                patch.object(runner, "_babysit_snapshot", side_effect=snapshot), \
                patch.object(runner, "is_model_exhausted", return_value=False):
            runner._babysit_with_fallback(
                "babysit. test prompt", str(self.wt_path), "fix/ISSUE-1224-x", 1224, 0,
            )

    def test_babysit_whenTheWorktreeIsFree_shouldDispatchTheMission(self):
        """The guard must not stop the normal path from running."""
        self._run_babysit()

        self.assertEqual(len(self.dispatched), 1,
                         "a free worktree must still get its babysit mission")
        self.assertEqual(self.held_during_dispatch, [True],
                         "the worktree must stay locked for the whole mission, not just "
                         "around the dispatch call")

    def test_babysit_whenAnotherRunDrivesTheWorktree_shouldNotDispatchAnyMission(self):
        """This is the measured hazard: an intake mission and a babysit mission in
        one worktree at once, both editing the same files and branch."""
        holder = _spawn_holder(self.wt_path, self.addCleanup)
        # LIFO: kill must be registered last so it runs first. Reversed, wait()
        # blocks for the holder's whole sleep before anything kills it.
        self.addCleanup(holder.wait)
        self.addCleanup(holder.kill)
        self.assertEqual(_await_marker(holder), "held")

        self._run_babysit()

        self.assertEqual(self.dispatched, [],
                         "no agent may be dispatched into a worktree another run is driving")

    def test_babysit_whenSkippedForABusyWorktree_shouldNotRecordAFailure(self):
        """A skip is not a failure: recording one would escalate the worktree and
        email after the backoff threshold, for work that simply belonged to the
        other run."""
        recorded = []
        holder = _spawn_holder(self.wt_path, self.addCleanup)
        # LIFO: kill must be registered last so it runs first. Reversed, wait()
        # blocks for the holder's whole sleep before anything kills it.
        self.addCleanup(holder.wait)
        self.addCleanup(holder.kill)
        self.assertEqual(_await_marker(holder), "held")

        with patch.object(runner, "_record_worktree_failure", side_effect=recorded.append), \
                patch.object(runner, "send_email"):
            self._run_babysit()

        self.assertEqual(recorded, [], "a busy worktree must not count as a babysit failure")


if __name__ == "__main__":
    unittest.main()
