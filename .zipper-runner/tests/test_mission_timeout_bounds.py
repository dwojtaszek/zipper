#!/usr/bin/env python3
"""Per-plugin mission bounds, and the progress rule that depends on them.

A plugin's bound is the runner's entire recovery budget for one agent attempt, so
the number has to match *how* the agent fails:

* An agent that emits nothing until it exits needs a short bound, because the
  bound is the only thing between a silent hang and a wasted mission.
* An agent that streams progress or stops itself must keep a long one, or a
  mission that is working gets truncated and redone.

Measured on the host (#1342), run `run_20261025_064928_intake` (116.6 min total
for one issue, holding the only worktree slot):

    agy           idled out its 40m print timeout        -> fallback
    opencode      silent for 45 min, killed by the bound -> fallback
    opencode x2   failed                                 -> fallback
    droid         made progress

~85 minutes went to agents that produced nothing. The opencode hang was genuine
and not slow work: `Running mission` was followed by the kill 45 minutes later
with zero progress lines in between.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""
import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest
from contextlib import ExitStack, redirect_stdout
from io import StringIO
from pathlib import Path
from unittest.mock import patch

RUNNER_BASE = Path(__file__).resolve().parents[1]
PLUGINS = RUNNER_BASE / "plugins"

sys.path.insert(0, str(RUNNER_BASE))

# Agents that emit nothing until they exit: the bound is the only hang guard.
SILENT_AGENTS = ("opencode", "hermes", "codex", "claude")
SILENT_HANG_SECONDS = 900
# Agents that stop themselves (agy) or stream progress (droid): a wall-clock cut
# would truncate work that is actually happening.
LIVE_AGENTS = ("agy", "droid")
LIVE_AGENT_SECONDS = 2700


def _load(name):
    spec = importlib.util.spec_from_file_location(f"{name}_plugin", PLUGINS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _hanging_command() -> list[str]:
    """A command that prints once and then hangs, like a stalled agent."""
    return [sys.executable, "-c", "import time; print('started', flush=True); time.sleep(60)"]


def _hanging_executable(directory: str, name: str, stderr_text: str = "") -> str:
    fake = Path(directory) / name
    fake.write_text(
        "#!/usr/bin/env python3\n"
        "import sys, time\n"
        "print('started', flush=True)\n"
        + (f"print({stderr_text!r}, file=sys.stderr, flush=True)\n" if stderr_text else "")
        + "time.sleep(60)\n"
    )
    fake.chmod(0o755)
    return str(fake)


class MissionBoundTests(unittest.TestCase):
    def test_silentAgents_shouldBoundAtFifteenMinutes(self):
        """An agent with no output must not be able to burn 45 minutes."""
        for name in SILENT_AGENTS:
            with self.subTest(plugin=name):
                self.assertEqual(
                    _load(name).MISSION_TIMEOUT, SILENT_HANG_SECONDS,
                    f"{name} emits nothing until it exits; a long bound is pure waste")

    def test_liveAgents_shouldKeepTheLongerBound(self):
        """agy stops itself and droid streams progress; cutting either truncates real work."""
        for name in LIVE_AGENTS:
            with self.subTest(plugin=name):
                self.assertEqual(
                    _load(name).MISSION_TIMEOUT, LIVE_AGENT_SECONDS,
                    f"{name} signals progress; a wall-clock cut would truncate it")

    def test_agyPrintTimeout_shouldStayInsideItsKill(self):
        """agy's own print timeout must fire first, or its partial work is lost."""
        plugin = _load("agy")
        self.assertLess(plugin.PRINT_TIMEOUT_SECONDS, plugin.MISSION_TIMEOUT)


class KillMessageTests(unittest.TestCase):
    """A kill message must state the bound that fired.

    A message reading "45 minutes" while the bound is 15 sends the next reader (or
    agent) looking for the wrong thing in the logs. The rendered message is checked
    against the plugin's real constant, so this is not degenerate: it fails if the
    text stops deriving from MISSION_TIMEOUT. The subprocess half of each test
    patches the bound to a fraction of a second so it stays fast, and checks the
    contract that matters there -- the partial output survives the kill.
    """

    TINY_BOUND = 1.5

    def _assertRendersTheRealBound(self, plugin):
        self.assertIn(f"{plugin.MISSION_TIMEOUT // 60} minutes", plugin._HUNG_KILL_MESSAGE)
    def test_everySilentAgent_shouldRenderItsOwnBoundInTheKillMessage(self):
        for name in SILENT_AGENTS:
            with self.subTest(plugin=name):
                plugin = _load(name)
                self._assertRendersTheRealBound(plugin)
                self.assertIn("15 minutes", plugin._HUNG_KILL_MESSAGE)

    def test_agy_shouldRenderItsOwnBoundInTheKillMessage(self):
        plugin = _load("agy")
        self._assertRendersTheRealBound(plugin)
        self.assertIn("45 minutes", plugin._HUNG_KILL_MESSAGE)

    def test_opencodeKill_shouldKeepPartialOutputAndNameTheBoundThatFired(self):
        plugin = _load("opencode")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND), \
                redirect_stdout(buffer):
            code, out, err = plugin._invoke_opencode(_hanging_command(), directory)
        self.assertEqual(code, -1, err)
        self.assertIn("started", out, "the partial output must survive the kill")
        self.assertIn("hung", err)
        self.assertNotIn("45 minutes", buffer.getvalue())

    def test_hermesKill_shouldKeepPartialOutputAndNameTheBoundThatFired(self):
        plugin = _load("hermes")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory:
            fake = _hanging_executable(directory, "hermes")
            with patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND), \
                    patch.object(plugin, "HERMES_BIN", fake), \
                    redirect_stdout(buffer):
                code, out, err = plugin.run_mission("hang", directory)
        self.assertEqual(code, -1, err)
        self.assertIn("started", out, "the partial output must survive the kill")
        self.assertIn("hung", err)
        self.assertNotIn("45 minutes", buffer.getvalue())

    def test_agyKill_shouldKeepPartialOutputAndNameTheBoundThatFired(self):
        plugin = _load("agy")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory:
            _hanging_executable(directory, "agy")
            env = {"PATH": directory + os.pathsep + os.environ["PATH"]}
            with ExitStack() as stack:
                stack.enter_context(patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND))
                stack.enter_context(patch.dict(os.environ, env))
                stack.enter_context(redirect_stdout(buffer))
                code, out, err = plugin.run_mission("hang", directory, is_continue=False)
        self.assertEqual(code, -1, err)
        self.assertIn("started", out, "the partial output must survive the kill")
        self.assertIn("hung", err)
        self.assertNotIn("45 minutes", buffer.getvalue())

    def test_codexKill_shouldKeepPartialOutputAndNameTheBoundThatFired(self):
        """codex threw its partial output away (#1345).

        It returns `-1, "", "Timeout"` on `TimeoutExpired`, so a timeout left no
        record of why the agent stalled -- while opencode, hermes, and agy all
        preserve theirs. The output is on the exception; it just was not read.
        """
        plugin = _load("codex")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory:
            _hanging_executable(directory, "codex")
            env = {"PATH": directory + os.pathsep + os.environ["PATH"]}
            with ExitStack() as stack:
                stack.enter_context(patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND))
                stack.enter_context(patch.dict(os.environ, env))
                stack.enter_context(redirect_stdout(buffer))
                code, out, err = plugin.run_mission("hang", directory)
        self.assertEqual(code, -1, err)
        self.assertIn("started", out, "the partial output must survive the kill")
        self.assertIn("hung", err)
        self.assertNotIn("45 minutes", buffer.getvalue())

    def test_codexPartialOutput_shouldBeDecodedText(self):
        """`subprocess.run` hands back bytes even with `text=True`.

        Returning the raw value would put a `b'started\\n'` repr in the diagnostic
        trail, which is not what the agent wrote.
        """
        plugin = _load("codex")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory:
            _hanging_executable(directory, "codex")
            env = {"PATH": directory + os.pathsep + os.environ["PATH"]}
            with ExitStack() as stack:
                stack.enter_context(patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND))
                stack.enter_context(patch.dict(os.environ, env))
                stack.enter_context(redirect_stdout(buffer))
                _, out, _ = plugin.run_mission("hang", directory)
        self.assertNotIn("b'", out, "the partial output must be decoded, not a bytes repr")
        self.assertNotIn("\\n", out.replace("started\n", ""),
                         "the decoded text must carry real newlines")

    def test_codexKill_shouldReturnPartialStderrSoQuotaIsSeen(self):
        """codex captures stderr separately, so its partial stderr must come back.

        The runner scans the *returned* streams with `is_quota_exhausted(out, err)`.
        Printing the partial stderr without returning it hides a quota reported
        there, and the runner relaunches a model that is cooling down.
        """
        import runner as runner_module

        plugin = _load("codex")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory:
            _hanging_executable(directory, "codex",
                                stderr_text="429 RESOURCE_EXHAUSTED: quota exceeded")
            env = {"PATH": directory + os.pathsep + os.environ["PATH"]}
            with ExitStack() as stack:
                stack.enter_context(patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND))
                stack.enter_context(patch.dict(os.environ, env))
                stack.enter_context(redirect_stdout(buffer))
                code, out, err = plugin.run_mission("hang", directory)
        self.assertEqual(code, -1, err)
        self.assertIn("hung", err, "the kill message must survive alongside it")
        self.assertTrue(runner_module.is_quota_exhausted(out, err),
                        "a quota reported on stderr must reach the quota check")

    def test_claudeKill_shouldKeepPartialOutputAndNameTheBoundThatFired(self):
        """claude threw away the temp file it had already written (#1345).

        Its output goes to a temp file rather than a pipe, so the partial output is
        on disk at the moment of the kill -- and the `finally` block deletes it.
        Reading it before that is the whole fix.
        """
        plugin = _load("claude")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory:
            _hanging_executable(directory, "claude")
            env = {"PATH": directory + os.pathsep + os.environ["PATH"]}
            with ExitStack() as stack:
                stack.enter_context(patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND))
                stack.enter_context(patch.dict(os.environ, env))
                stack.enter_context(redirect_stdout(buffer))
                code, out, err = plugin.run_mission("hang", directory)
        self.assertEqual(code, -1, err)
        self.assertIn("started", out, "the partial output must survive the kill")
        self.assertIn("hung", err)
        self.assertNotIn("45 minutes", buffer.getvalue())

    def test_claudePartialOutput_shouldSurviveTheTempFileCleanup(self):
        """The kill path must read the log before the `finally` unlinks it."""
        plugin = _load("claude")
        buffer = StringIO()
        with tempfile.TemporaryDirectory() as directory:
            _hanging_executable(directory, "claude")
            env = {"PATH": directory + os.pathsep + os.environ["PATH"]}
            with ExitStack() as stack:
                stack.enter_context(patch.object(plugin, "MISSION_TIMEOUT", self.TINY_BOUND))
                stack.enter_context(patch.dict(os.environ, env))
                stack.enter_context(redirect_stdout(buffer))
                _, out, _ = plugin.run_mission("hang", directory)
        self.assertTrue(out.strip(), "a timeout must not return an empty diagnostic")
        self.assertNotIn("claude.log", buffer.getvalue(),
                         "the partial output must be read, not a leftover path")


class BoundIsActuallyAppliedTests(unittest.TestCase):
    """The bound must reach the process, not just sit in a module constant.

    codex and claude bound the mission by passing `timeout=` to `subprocess.run`,
    so a constant-only test would keep passing if that argument reverted to a
    literal — the plugin would hang for 45 minutes with a green suite. These
    tests observe the argument the process would actually receive.
    """

    def test_codex_shouldPassItsBoundToTheProcess(self):
        plugin = _load("codex")
        with patch.object(plugin.subprocess, "run") as run:
            run.return_value.returncode = 0
            run.return_value.stdout = "done"
            run.return_value.stderr = ""
            plugin.run_mission("prompt", tempfile.gettempdir(), model="openai/gpt-5.6-terra")
        self.assertEqual(run.call_args.kwargs.get("timeout"), plugin.MISSION_TIMEOUT)

    def test_claude_shouldPassItsBoundToTheProcess(self):
        plugin = _load("claude")
        with patch.object(plugin.subprocess, "run") as run:
            run.return_value.returncode = 0
            run.return_value.stdout = ""
            run.return_value.stderr = ""
            plugin.run_mission("prompt", tempfile.gettempdir(), model="default")
        self.assertEqual(run.call_args.kwargs.get("timeout"), plugin.MISSION_TIMEOUT)

    def test_codex_whenTheBoundFires_shouldReportFailure(self):
        plugin = _load("codex")
        with patch.object(plugin.subprocess, "run") as run:
            run.side_effect = plugin.subprocess.TimeoutExpired(cmd="codex", timeout=1)
            buffer = StringIO()
            with redirect_stdout(buffer):
                code, _, _ = plugin.run_mission("prompt", tempfile.gettempdir())
        self.assertEqual(code, -1, "a timed-out mission must not look successful")
        self.assertIn("15 minutes", buffer.getvalue())

    def test_claude_whenTheBoundFires_shouldReportFailure(self):
        plugin = _load("claude")
        with patch.object(plugin.subprocess, "run") as run:
            run.side_effect = plugin.subprocess.TimeoutExpired(cmd="claude", timeout=1)
            buffer = StringIO()
            with redirect_stdout(buffer):
                code, _, _ = plugin.run_mission("prompt", tempfile.gettempdir())
        self.assertEqual(code, -1, "a timed-out mission must not look successful")
        self.assertIn("15 minutes", buffer.getvalue())


class ProgressRuleIsWiredInTests(unittest.TestCase):
    """`_mission_produced_work` must be what the intake loop actually calls.

    The rule is only worth anything if it is wired in. `main()` builds its intake
    prompts and dispatch inline, so — like `test_agy_plugin`'s prompt assertions —
    this pins the invariant on the source: re-inlining the old expression, or
    extracting the helper and forgetting to call it, fails here.
    """

    def setUp(self):
        self.source = (RUNNER_BASE / "runner.py").read_text(encoding="utf-8")

    def test_intake_shouldComputeWorkDoneWithTheSharedRule(self):
        self.assertIn("work_done = _mission_produced_work(", self.source)

    def test_intake_shouldNotReinlineTheExpression(self):
        self.assertNotIn(
            "work_done = pr_num_val or (has_commits and added_commits > 0)", self.source,
            "the WIP-excluding rule must not be bypassed by an inline expression")


class MissionProgressRuleTests(unittest.TestCase):
    """A WIP checkpoint is not progress, and must not end the cycle.

    `_auto_commit_wip_checkpoint` runs after every mission, so an agent cut short
    while leaving edits behind always leaves a commit. Counting it as progress
    takes the "Progress recorded" branch and breaks out of the candidate loop,
    leaving the single worktree slot idle until the next timer tick instead of
    handing it to the next agent. These tests exercise the real rule against a
    real git repository.
    """

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.repo = self._tmp.name
        for args in (
            ["init", "-q", "-b", "main"],
            ["config", "user.email", "runner@example.invalid"],
            ["config", "user.name", "Runner Test"],
        ):
            subprocess.run(["git", *args], cwd=self.repo, check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        (Path(self.repo) / "seed.txt").write_text("seed\n")
        subprocess.run(["git", "add", "-A"], cwd=self.repo, check=True)
        subprocess.run(["git", "commit", "-q", "-m", "seed"], cwd=self.repo, check=True)

    def _commit(self, message: str):
        (Path(self.repo) / "change.txt").write_text(f"{message}\n")
        subprocess.run(["git", "add", "-A"], cwd=self.repo, check=True)
        subprocess.run(["git", "commit", "-q", "-m", message], cwd=self.repo, check=True)

    def test_wipCheckpointAlone_shouldNotCountAsProgress(self):
        import runner
        self._commit("wip: progress on issue #1 by opencode")
        self.assertFalse(
            runner._mission_produced_work(None, has_commits=True, added_commits=1, wt_path=self.repo),
            "a WIP-only checkpoint must not end the cycle; the slot goes to the next agent")

    def test_realCommit_shouldCountAsProgress(self):
        import runner
        self._commit("fix: resolve the thing (#1)")
        self.assertTrue(
            runner._mission_produced_work(None, has_commits=True, added_commits=1, wt_path=self.repo))

    def test_wipCheckpointOnTopOfRealCommit_shouldHandOverToTheNextCandidate(self):
        """The latest commit decides: an interrupted agent still needs a healthy one."""
        import runner
        self._commit("fix: resolve the thing (#1)")
        self._commit("wip: progress on issue #1 by droid")
        self.assertFalse(
            runner._mission_produced_work(None, has_commits=True, added_commits=2, wt_path=self.repo),
            "an interrupted agent on top of real work still needs the next candidate")

    def test_openPr_shouldCountAsProgressEvenWhenTheHeadIsWip(self):
        """A PR is reviewable work regardless of the head commit's message."""
        import runner
        self._commit("wip: progress on issue #1 by opencode")
        self.assertTrue(
            runner._mission_produced_work(1234, has_commits=True, added_commits=1, wt_path=self.repo))

    def test_noNewCommits_shouldNotCountAsProgress(self):
        import runner
        self.assertFalse(
            runner._mission_produced_work(None, has_commits=True, added_commits=0, wt_path=self.repo))
        self.assertFalse(
            runner._mission_produced_work(None, has_commits=False, added_commits=0, wt_path=self.repo))


if __name__ == "__main__":
    unittest.main()
