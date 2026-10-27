#!/usr/bin/env python3
"""Droid's mission bound is silence, not the clock.

droid streams `assistant progress` and commits as it works, so a wall-clock bound
cuts missions that are still making progress. Measured on the host (#1361), four
droid missions were killed at exactly 2640s while still working, and because
publication only happens on a clean mission exit, each cut stranded that work
locally and the next firing redid it -- PR #1357 sat green and mergeable for two
hours with three unpublished commits behind it, holding the only worktree slot.

These tests drive real child processes and a real git repository, so the liveness
rule is exercised where it is applied rather than only where it is declared.

Every magnitude here is chosen so timing jitter cannot flip the outcome: the
silence bound is several times the progress cadence, and the child outlives the
bound by a wide margin. A test whose verdict depends on where a commit lands
relative to a poll is not a test.

Run from .zipper-runner:  python3 -m unittest tests.test_droid_liveness
"""
import importlib.util
import os
import subprocess
import sys
import tempfile
import time
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from unittest.mock import patch

RUNNER_BASE = Path(__file__).resolve().parents[1]
PLUGINS = RUNNER_BASE / "plugins"

# The longest droid mission measured under the silence watchdog: a retry of issue
# #1290 that finished 60s under the 5400s ceiling that had cut its first attempt.
# The ceiling has to clear real work rather than land on top of it.
LONGEST_MEASURED_MISSION_SECONDS = 5340
CEILING_HEADROOM = 5 / 4  # at least 25% above the longest measured mission

sys.path.insert(0, str(RUNNER_BASE))

# Prints every 0.1s for 2s: never silent for the 1s bound, so it must finish.
PROGRESSING = (
    "import time\n"
    "for i in range(20):\n"
    "    print('tick', i, flush=True)\n"
    "    time.sleep(0.1)\n"
)
# One line, then nothing: a hang.
SILENT = (
    "import time\n"
    "print('started', flush=True)\n"
    "time.sleep(30)\n"
)
# Commits every 0.1s for 2.5s while printing nothing: progress with no output,
# lasting well past the 1s silence bound.
COMMITTING = (
    "import subprocess, time\n"
    "print('started', flush=True)\n"
    "for i in range(25):\n"
    "    subprocess.run(['git', 'commit', '--allow-empty', '--no-edit', '-m', 'wip'],\n"
    "                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)\n"
    "    time.sleep(0.1)\n"
    "print('done', flush=True)\n"
)
# Commits every 0.5s for 4s: progress at a cadence well inside the 1s bound.
SLOW_COMMITTING = (
    "import subprocess, time\n"
    "print('started', flush=True)\n"
    "for i in range(8):\n"
    "    subprocess.run(['git', 'commit', '--allow-empty', '--no-edit', '-m', 'wip'],\n"
    "                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)\n"
    "    time.sleep(0.5)\n"
    "print('done', flush=True)\n"
)
# Never silent, never finishing: only the ceiling can stop this.
CHATTY = (
    "import time\n"
    "for i in range(400):\n"
    "    print('tick', i, flush=True)\n"
    "    time.sleep(0.1)\n"
)
# Exits 0 at once, but leaves a child holding the captured pipes open.
EXITED_WITH_LINGERING_CHILD = (
    "import subprocess, sys\n"
    "print('started', flush=True)\n"
    "subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(5)'])\n"
)
# Commits 3s before a 4s bound, then stays silent past it. The wide margin keeps
# scheduler and Git latency from changing the result; only a fresh HEAD check at
# the silence deadline can tell this apart from a hang.
DEADLINE_COMMITTING = (
    "import subprocess, time\n"
    "print('started', flush=True)\n"
    "time.sleep(1.0)\n"
    "subprocess.run(['git', 'commit', '--allow-empty', '--no-edit', '-m', 'wip'],\n"
    "               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)\n"
    "time.sleep(4.0)\n"
    "print('done', flush=True)\n"
)
# Closes both output streams, then keeps running. The watchdog must stay alive
# with no pipes left to drain.
CLOSES_STREAMS_THEN_HANGS = (
    "import os, time\n"
    "print('started', flush=True)\n"
    "os.close(1)\n"
    "os.close(2)\n"
    "time.sleep(30)\n"
)
# Exits at once, leaving a child that holds the pipes and prints well after the
# drain's own wait would have expired. Only the grace window captures its line.
EXITED_WITH_CHILD_THAT_PRINTS = (
    "import subprocess, sys\n"
    "subprocess.Popen([sys.executable, '-c',\n"
    "    \"import sys, time; time.sleep(2.0); sys.stdout.write('trailing\\\\n'); sys.stdout.flush()\"])\n"
    "print('started', flush=True)\n"
)
# Exits at once, leaving a child that holds the pipes for far longer than the
# grace window. run_mission must return promptly rather than wait it out.
EXITED_WITH_LONG_LIVED_CHILD = (
    "import subprocess, sys\n"
    "print('started', flush=True)\n"
    "subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])\n"
)


def _load(name="droid"):
    spec = importlib.util.spec_from_file_location(f"{name}_plugin", PLUGINS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _git_repo(directory):
    subprocess.run(["git", "init", "-q", "-b", "main", directory], check=True, capture_output=True)
    (Path(directory) / "tracked.txt").write_text("tracked")
    subprocess.run(["git", "-C", directory, "add", "."], check=True, capture_output=True)
    subprocess.run(["git", "-C", directory, "commit", "-q", "-m", "init"], check=True, capture_output=True)


class _LivenessCase(unittest.TestCase):
    """A stub `droid` on PATH and a temporary working directory."""

    LIVENESS = 1.0
    CEILING = 30.0
    GRACE = 5.0

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.directory = self.tmp.name
        self.plugin = _load()
        path = patch.dict(os.environ, {"PATH": self.directory + os.pathsep + os.environ["PATH"]})
        path.start()
        self.addCleanup(path.stop)

    def tearDown(self):
        self.tmp.cleanup()

    def _run(self, body, *, liveness=None, ceiling=None, head_check=0.05, heartbeat=60.0, grace=None):
        fake = Path(self.directory) / "droid"
        fake.write_text("#!/usr/bin/env python3\n" + body)
        fake.chmod(0o755)
        buffer = StringIO()
        with patch.object(self.plugin, "LIVENESS_TIMEOUT", self.LIVENESS if liveness is None else liveness), \
                patch.object(self.plugin, "MISSION_TIMEOUT", self.CEILING if ceiling is None else ceiling), \
                patch.object(self.plugin, "HEAD_CHECK_SECONDS", head_check), \
                patch.object(self.plugin, "HEARTBEAT_SECONDS", heartbeat), \
                patch.object(self.plugin, "MISSION_EXIT_GRACE_SECONDS",
                             self.GRACE if grace is None else grace), \
                redirect_stdout(buffer):
            result = self.plugin.run_mission("go", self.directory)
        return result, buffer.getvalue()


class ProgressKeepsAMissionAliveTests(_LivenessCase):
    """A mission that is working must not be cut for taking a long time."""

    def test_output_shouldResetTheSilenceBound(self):
        (code, out, err), _ = self._run(PROGRESSING)
        self.assertEqual(code, 0, err)
        self.assertIn("tick 19", out, "the mission must have been allowed to finish")
        self.assertEqual(err, "")

    def test_commits_shouldResetTheSilenceBoundWhileNothingIsPrinted(self):
        """The worktree HEAD is a liveness signal in its own right: an agent that
        commits while silent is working, not hung. The mission outlives the bound
        by 2.5x, so only the HEAD signal can keep it alive."""
        _git_repo(self.directory)
        before = subprocess.check_output(["git", "-C", self.directory, "rev-parse", "HEAD"], text=True).strip()
        (code, out, err), _ = self._run(COMMITTING)
        after = subprocess.check_output(["git", "-C", self.directory, "rev-parse", "HEAD"], text=True).strip()
        self.assertEqual(code, 0, err)
        self.assertIn("done", out)
        self.assertNotEqual(after, before, "the fixture must actually have committed")

    def test_commitsAtASlowCadence_shouldStillCountAsProgress(self):
        """Committing every 0.5s against a 1s bound leaves half the window unused.
        Progress only has to arrive inside the window, not continuously."""
        _git_repo(self.directory)
        (code, out, err), _ = self._run(SLOW_COMMITTING)
        self.assertEqual(code, 0, err)
        self.assertIn("done", out)

    def test_commitInsideTheFinalPollInterval_shouldSurviveTheDeadline(self):
        """A commit made since the last poll is progress this clock has not seen.

        The poll interval is set far above the bound, so no scheduled poll runs
        before the deadline: the deadline itself has to ask for a fresh HEAD
        before it may call the mission silent. Here the commit is made 1s into a
        4s window; without that fresh check, the mission is killed at the
        deadline despite having committed about 3s earlier.
        """
        _git_repo(self.directory)
        before = subprocess.check_output(["git", "-C", self.directory, "rev-parse", "HEAD"], text=True).strip()
        (code, out, err), _ = self._run(DEADLINE_COMMITTING, liveness=4.0, head_check=30.0)
        after = subprocess.check_output(["git", "-C", self.directory, "rev-parse", "HEAD"], text=True).strip()
        self.assertEqual(code, 0, err)
        self.assertIn("done", out)
        self.assertNotEqual(after, before, "the fixture must actually have committed")

    def test_missionThatExited_shouldNotBeReportedAsKilledForALingeringChild(self):
        """droid can exit while a tool child still holds the pipes. The bound
        applies to the mission process, so a finished mission is not a hang."""
        (code, out, err), _ = self._run(EXITED_WITH_LINGERING_CHILD)
        self.assertEqual(code, 0, err)
        self.assertIn("started", out)
        self.assertNotIn("hung", err)

    def test_trailingChildOutput_shouldStillReachTheRunner(self):
        """The bounded grace drain exists so a child that inherited the pipes can
        flush its last line. Returning the instant the mission exits would lose
        it, which is what the old unbounded drain did collect.

        The child prints at 2s, past the drain's own wait, so only the grace
        window can capture it.
        """
        (code, out, err), _ = self._run(EXITED_WITH_CHILD_THAT_PRINTS)
        self.assertEqual(code, 0, err)
        self.assertIn("trailing", out, "the child's tail must survive the mission's exit")

    def test_closedPipesOnExit_shouldNotPayTheGraceWindow(self):
        """The grace is only for a child still holding the pipes. A mission that
        closed them must return at once, or every normal mission pays the window
        for nothing."""
        start = time.monotonic()
        (code, out, err), _ = self._run("print('done', flush=True)\n", grace=30.0)
        elapsed = time.monotonic() - start
        self.assertEqual(code, 0, err)
        self.assertIn("done", out)
        self.assertLess(elapsed, 10.0, "a closed-pipe mission must not wait out the grace window")

    def test_graceDrain_shouldBeBoundedRatherThanWaitOutALongLivedChild(self):
        """The old loop waited on the pipes, so a child holding them open kept
        the runner there until the ceiling and then reported a *successful*
        mission as timed out. The grace window is what makes that impossible."""
        start = time.monotonic()
        (code, out, err), _ = self._run(EXITED_WITH_LONG_LIVED_CHILD, grace=1.0)
        elapsed = time.monotonic() - start
        self.assertEqual(code, 0, err)
        self.assertIn("started", out)
        self.assertLess(elapsed, 10.0, "the grace window must bound the wait")
    def test_nonGitWorkingDirectory_shouldNotBreakTheWatchdog(self):
        """`_worktree_head` cannot read a HEAD outside a repository. That is not
        progress, but it must not stop the watchdog either."""
        self.assertIsNone(self.plugin._worktree_head(self.directory))
        (code, out, err), _ = self._run(PROGRESSING)
        self.assertEqual(code, 0, err)


class SilenceEndsAMissionTests(_LivenessCase):
    """A mission that stops producing anything is a hang, however long it ran."""

    def test_silentMission_shouldBeCutAndNameTheSilenceBound(self):
        (code, out, err), _ = self._run(SILENT)
        self.assertEqual(code, -1)
        self.assertIn("started", out, "the partial output must survive the kill")
        self.assertIn("hung", err)
        self.assertIn("no output and no new commit", err)

    def test_heartbeat_shouldNotCountAsProgress(self):
        """The heartbeat is this plugin's own timer, not droid's output. Feeding
        it into the progress clock would make the watchdog unfireable."""
        (code, out, err), captured = self._run(SILENT, heartbeat=0.1)
        self.assertEqual(code, -1)
        self.assertIn("Mission active", captured, "the heartbeat must have been printed")
        self.assertIn("hung", err, "the heartbeat must not have saved the mission")

    def test_silentMissionInANonGitDirectory_shouldStillBeCut(self):
        (code, _out, err), _ = self._run(SILENT)
        self.assertEqual(code, -1)
        self.assertIn("hung", err)

    def test_closedOutputStreams_shouldNotDisableTheSilenceBound(self):
        """A mission that closes both streams and keeps running still has to be
        bounded by silence. If draining the pipes is what keeps the loop alive,
        EOF on both ends would leave the ceiling as the only bound."""
        start = time.monotonic()
        (code, out, err), _ = self._run(CLOSES_STREAMS_THEN_HANGS)
        elapsed = time.monotonic() - start
        self.assertEqual(code, -1)
        self.assertIn("hung", err)
        self.assertLess(elapsed, self.LIVENESS * 4,
                        "the silence bound must fire with no pipes left to drain")

    def test_chattyMission_shouldBeCutByTheCeilingNotTheSilenceBound(self):
        """The ceiling is independent of the silence bound, or a mission that
        emits output forever could never be stopped."""
        (code, out, err), _ = self._run(CHATTY, liveness=1.0, ceiling=0.6)
        self.assertEqual(code, -1)
        self.assertIn("timed out after", err)
        self.assertNotIn("no output and no new commit", err)
        self.assertIn("tick", out, "the partial output must survive the kill")


class BoundShapeTests(unittest.TestCase):
    """The declared bounds, so a revert to a wall-clock cut is caught."""

    def setUp(self):
        # The bound tests assert the shipped defaults, so an ambient override left
        # in a developer's shell must not be able to fail them.
        for name in ("RUNNER_DROID_LIVENESS_TIMEOUT", "RUNNER_DROID_MISSION_TIMEOUT"):
            os.environ.pop(name, None)
        self.plugin = _load()

    def test_silenceBound_shouldSitBelowTheCeiling(self):
        self.assertLess(self.plugin.LIVENESS_TIMEOUT, self.plugin.MISSION_TIMEOUT)

    def test_headPoll_shouldBeFrequentEnoughToNoticeProgress(self):
        """A poll interval at or above the silence bound could let a committing
        mission be declared hung between two polls, so the soundness of the
        watchdog rests on this relationship."""
        self.assertLess(self.plugin.HEAD_CHECK_SECONDS, self.plugin.LIVENESS_TIMEOUT)

    def test_headPoll_shouldNotBeConfigurableAroundTheInvariant(self):
        """The poll interval is a constant, not an env override: a deployment
        that raised it above the silence bound would silently disable the only
        signal that keeps a committing mission alive."""
        for name in ("RUNNER_DROID_HEAD_CHECK_SECONDS", "RUNNER_DROID_HEAD_CHECK"):
            os.environ[name] = "99999"
        try:
            fresh = _load()
        finally:
            for name in ("RUNNER_DROID_HEAD_CHECK_SECONDS", "RUNNER_DROID_HEAD_CHECK"):
                os.environ.pop(name, None)
        self.assertEqual(fresh.HEAD_CHECK_SECONDS, 30)

    def test_ceiling_shouldSitClearOfTheLongestMeasuredMission(self):
        """A ceiling inside normal work truncates instead of protecting.

        This subsumes the earlier floor of 2700s, which cut missions that were
        still working. See plugins/droid.py MISSION_TIMEOUT for the measurements.
        """
        self.assertGreaterEqual(
            self.plugin.MISSION_TIMEOUT,
            LONGEST_MEASURED_MISSION_SECONDS * CEILING_HEADROOM,
            "the ceiling must keep headroom over the longest measured mission",
        )

    def test_defaults_shouldMatchTheDeclaredBounds(self):
        fresh = _load()
        self.assertEqual(fresh.LIVENESS_TIMEOUT, 900)
        self.assertEqual(fresh.MISSION_TIMEOUT, 7200)

    def test_documentedBounds_shouldMatchTheDefaults(self):
        """The README states both bounds in two places. A value change that misses
        one of them is doc/code drift, and nothing else in the tree reads this
        file, so the numbers are asserted here rather than left to review."""
        readme = (RUNNER_BASE / "README.md").read_text()
        self.assertIn("a 900s silence bound with a 7200s ceiling for droid", readme)
        self.assertIn("with 7200s as a generous absolute ceiling", readme)


if __name__ == "__main__":
    unittest.main()
