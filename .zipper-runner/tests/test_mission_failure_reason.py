"""A mission that ends non-zero must say why in the run log.

Without this, a ceiling cut, a silence kill, and an ordinary no-progress cycle
read identically: observed on the host, issue #1291 was cut at the 5400s droid
ceiling with the agent mid-read and a WIP commit on disk, and the run log showed
only "Progress recorded on issue #1291". The reason existed solely inside an
escalation email body, so nothing scanning the logs could see the truncation.

Every call site runs through `run_mission_logged`, so these tests pin both halves:
the reason extraction, and the fact that a non-zero exit prints one.
"""
import ast
import importlib.util
import io
import json
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]


def _load():
    spec = importlib.util.spec_from_file_location("reason_runner", BASE / "runner.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class _FakePlugin:
    """A plugin that returns whatever the test tells it to."""

    def __init__(self, result):
        self.result = result
        self.calls = []

    def run_mission(self, prompt, cwd, is_continue=False, model=None):
        self.calls.append({"prompt": prompt, "cwd": cwd, "is_continue": is_continue, "model": model})
        return self.result


class _CaptureCase(unittest.TestCase):
    """Runs the seam the way production does: under a real worktree mission lock.

    The seam verifies the lock at runtime and warns without it, so a fixture that
    dispatched unlocked would test the warning path in every case and hide the
    behaviour under test.
    """

    def setUp(self):
        self.runner = _load()
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.runner.STATE_DIR = str(self.root / "state")
        self.worktree = self.root / "issue-42"
        self.worktree.mkdir()

    def tearDown(self):
        self._tmp.cleanup()

    def _logged(self, result, phase="intake"):
        """Run one mission through the shared seam and return what it printed."""
        plugin = _FakePlugin(result)
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            with self.runner._worktree_mission_lock(str(self.worktree)) as may_drive:
                self.assertTrue(may_drive)
                returned = self.runner.run_mission_logged(
                    plugin, "droid", "gpt-6-luna", "prompt", str(self.worktree),
                    is_continue=False, phase=phase)
        return returned, buffer.getvalue(), plugin


class MissionFailureReasonTests(unittest.TestCase):
    """The extraction itself: which line names the failure."""

    def setUp(self):
        self.runner = _load()

    def test_errorLine_shouldWinOverATraceback(self):
        """Plugins announce their termination with `Error:`; a traceback above it
        is noise that would otherwise be reported as the reason."""
        err = (
            "Traceback (most recent call last):\n"
            '  File "runner.py", line 1, in <module>\n'
            "RuntimeError: boom\n"
            "Error: Droid mission timed out after 5400 seconds.\n"
        )
        self.assertEqual(
            self.runner._mission_failure_reason("", err),
            "Error: Droid mission timed out after 5400 seconds.",
        )

    def test_errorLine_shouldBeFoundEvenWhenItIsNotLast(self):
        """The reason can be followed by a trailing blank or partial-output dump."""
        err = "Error: Droid mission timed out after 7200 seconds.\n\n--- partial ---\nnoise"
        self.assertEqual(
            self.runner._mission_failure_reason("", err),
            "Error: Droid mission timed out after 7200 seconds.",
        )

    def test_noErrorLine_shouldFallBackToTheLastStderrLine(self):
        """An unexpected failure still has to name something, and stderr is where
        an unhandled exception's own message lands."""
        self.assertEqual(
            self.runner._mission_failure_reason("", "warning: something\nTypeError: bad arg"),
            "TypeError: bad arg",
        )

    def test_stderrShouldWinOverStdout(self):
        """stdout is assistant prose and can mention anything; stderr is the
        channel every plugin reports failure on."""
        self.assertEqual(
            self.runner._mission_failure_reason(
                "I finished the work successfully.", "Error: Agent process hung and was forcefully killed after 15 minutes."),
            "Error: Agent process hung and was forcefully killed after 15 minutes.",
        )

    def test_stdoutErrorLine_shouldStillCountAsAReason(self):
        """A provider that writes its failure to stdout is still evidence, so an
        announced error is accepted from either stream."""
        self.assertEqual(
            self.runner._mission_failure_reason("Error: quota exhausted, resets in 5h", ""),
            "Error: quota exhausted, resets in 5h",
        )

    def test_stdoutProseWithoutAnErrorLine_shouldNotBecomeTheReason(self):
        """stdout is assistant prose: its last line is as likely to be a cheerful
        summary as the failure, so naming it would point the reader at the wrong
        thing. With stderr empty, there is no reason to report."""
        self.assertEqual(
            self.runner._mission_failure_reason(
                "All gates pass. I have finished the implementation and pushed nothing.", ""),
            "",
        )

    def test_multilineReason_shouldCollapseToOneLine(self):
        """The log is scanned line by line, so a reason must not break it."""
        reason = self.runner._mission_failure_reason("", "Error: first part\n   second part")
        self.assertEqual(reason, "Error: first part second part")
        self.assertNotIn("\n", reason)

    def test_longReason_shouldBeTruncatedAndMarked(self):
        """stderr can carry a whole traceback; one line has to stay bounded."""
        reason = self.runner._mission_failure_reason("", "Error: " + "x" * 5000)
        self.assertLessEqual(len(reason), self.runner._MISSION_REASON_LIMIT)
        self.assertTrue(reason.endswith("..."))

    def test_nothingReported_shouldReturnEmptyRatherThanAFabricatedReason(self):
        """A plugin that says nothing must not produce a misleading message."""
        self.assertEqual(self.runner._mission_failure_reason("", ""), "")
        self.assertEqual(self.runner._mission_failure_reason("   \n  \n", "\n"), "")

    def test_whitespaceOnlyLines_shouldNotBecomeTheReason(self):
        """Blank lines around the real reason must not shadow it."""
        self.assertEqual(
            self.runner._mission_failure_reason("", "\n\n   \nError: killed\n\n"),
            "Error: killed",
        )


class RealPluginShapeTests(unittest.TestCase):
    """The extraction against the exact strings the plugins actually return.

    These are the shapes that a general rule gets wrong, so each is pinned from
    the plugin source rather than invented: codex composes a provider error and
    then its kill message, and opencode wraps its errors in colour codes.
    """

    def setUp(self):
        self.runner = _load()
        self.reason = self.runner._mission_failure_reason

    def test_codexComposedKill_shouldReportTheKillNotTheEarlierProviderError(self):
        """`codex.py` returns `f"{partial_err}\\n{_HUNG_KILL_MESSAGE}"`. Taking the
        first error would report a 900s silence kill as a provider rate limit,
        which is the exact confusion this change exists to remove."""
        composed = (
            "Error: stream disconnected before completion: rate limit exceeded\n"
            "\n"
            "Error: Agent process hung and was forcefully killed after 15 minutes."
        )
        self.assertEqual(
            self.reason("", composed),
            "Error: Agent process hung and was forcefully killed after 15 minutes.",
        )

    def test_codexKillAfterTrailingNewline_shouldStillBeReported(self):
        """The partial stderr often ends in its own newline, which puts a blank
        line between the provider error and the appended kill message. The kill
        must still be the reported reason, not be swallowed by that separator."""
        partial_err = "Error: stream disconnected before completion: rate limit exceeded\n"
        err = f"{partial_err}\nError: Agent process hung and was forcefully killed after 15 minutes."
        self.assertIn("\n\n", err, "this fixture must exercise the blank separator")
        self.assertEqual(
            self.reason("", err),
            "Error: Agent process hung and was forcefully killed after 15 minutes.",
        )

    def test_boundKillWithNoProviderError_shouldBeReported(self):
        """The common case: the kill message is the whole stderr."""
        self.assertEqual(
            self.reason("", "Error: Agent process hung and was forcefully killed after 15 minutes."),
            "Error: Agent process hung and was forcefully killed after 15 minutes.",
        )

    def test_opencodeAnsiError_shouldBeReportedWithoutEscapes(self):
        """opencode's own detector tolerates colour codes before `Error:`; the
        bytes are in test_model_switching.py. Escapes must not reach the log, and
        must not stop the line being recognised."""
        ansi = "\x1b[91m\x1b[1mError: \x1b[0mFree usage limit reached. Free models work in OpenCode"
        reason = self.reason("", ansi)
        self.assertNotIn("\x1b", reason)
        self.assertIn("Free usage limit reached", reason)

    def test_opencodeAnsiErrorWithATrailingTip_shouldNotReportOnlyTheTip(self):
        """The cause must survive a following decoration line: reporting the tip
        instead would name the wrong thing."""
        ansi = ("\x1b[91m\x1b[1mError: \x1b[0mFree usage limit reached\n"
                "└ Tip: run opencode models to list models")
        reason = self.reason("", ansi)
        self.assertIn("Free usage limit reached", reason)
        self.assertNotIn("\x1b", reason)

    def test_droidCeilingKill_shouldBeReported(self):
        self.assertEqual(
            self.reason("", "Error: Droid mission timed out after 7200 seconds."),
            "Error: Droid mission timed out after 7200 seconds.",
        )

    def test_droidSilenceKill_shouldBeReportedWithItsOwnWording(self):
        """The two droid bounds are different failures and must not be conflated."""
        message = ("Error: Droid mission hung and was forcefully killed after 900 seconds "
                   "with no output and no new commit.")
        self.assertEqual(self.reason("", message), message)

    def test_wrappedProviderMessage_shouldKeepItsContinuationLine(self):
        """Provider messages wrap, and the continuation carries the useful part
        (when the quota resets). A blank line, not a newline, ends the reason."""
        err = "Error: 429 Resource exhausted\nResets in 85h0m6s\n\npartial output dump"
        self.assertEqual(
            self.reason("", err),
            "Error: 429 Resource exhausted Resets in 85h0m6s",
        )

    def test_claudeMergedStream_shouldReportAnErrorAnchoredLine(self):
        """claude merges stderr into stdout and returns "" for err
        (`claude.py`), so an error-announced line at the start of a line is the
        only shape it can report. Its bound-cuts are unaffected: those set stderr
        explicitly."""
        self.assertEqual(
            self.reason("Error: tool call failed\nError: Claude Code process exited with code 1", ""),
            "Error: Claude Code process exited with code 1",
        )

    def test_claudeProseOnAnEmptyStderr_shouldReportNothingRatherThanProse(self):
        """Known limitation, pinned so it stays a decision rather than a surprise:
        a claude failure whose text is not error-anchored has no reported reason.
        Putting its merged stream on stderr instead would expose assistant prose
        to the quota-reset parser, which reads that channel only."""
        self.assertEqual(
            self.reason("Invalid API key · Please run /login", ""),
            "",
        )

    def test_bareExceptionString_shouldStillBeReported(self):
        """Every plugin's `except Exception` returns `str(e)` on stderr."""
        self.assertEqual(
            self.reason("", "ConnectionResetError(104, 'Connection reset by peer')"),
            "ConnectionResetError(104, 'Connection reset by peer')",
        )

    def test_proseMentioningAnErrorMidLine_shouldNotBeTreatedAsTheCause(self):
        """The error marker is anchored at the start of a line. Assistant prose
        that merely mentions a past error must not be promoted to the cause, or a
        log scan reads a cheerful summary as the failure."""
        err = "I fixed the error in the parser\nAll 3504 tests pass now."
        self.assertEqual(self.reason("", err), "All 3504 tests pass now.")

    def test_wordStartingWithErr_shouldNotCountAsAnErrorMarker(self):
        """`error` must end at a word boundary, so a line that merely begins with
        a word containing "err" is not read as the cause."""
        err = "errors in the parser were fixed\nAll 3504 tests pass now."
        self.assertEqual(self.reason("", err), "All 3504 tests pass now.")

    def test_lowercaseErrorMarker_shouldStillBeFound(self):
        """Provider messages are not consistently capitalised."""
        self.assertEqual(
            self.reason("", "error: stream disconnected"),
            "error: stream disconnected",
        )

    def test_indentedErrorLine_shouldStillBeFound(self):
        """Real plugin output indents its errors; the marker is matched after the
        line is stripped, not against the raw text."""
        self.assertEqual(
            self.reason("", "    Error: Droid mission timed out after 7200 seconds."),
            "Error: Droid mission timed out after 7200 seconds.",
        )


class MissionExitLoggingTests(_CaptureCase):
    """The seam: a non-zero exit prints its reason, a success stays quiet."""

    def test_nonZeroExit_shouldPrintTheReason(self):
        returned, printed, _ = self._logged((-1, "", "Error: Droid mission timed out after 7200 seconds."))
        self.assertEqual(returned[0], -1, "the seam must pass the result through unchanged")
        self.assertIn("mission ended non-zero (exit=-1)", printed)
        self.assertIn("Error: Droid mission timed out after 7200 seconds.", printed)

    def test_silenceKill_shouldPrintTheSilenceMessageNotACeilingMessage(self):
        """The two bounds are different failures and must not be conflated."""
        returned, printed, _ = self._logged((
            -1, "",
            "Error: Droid mission hung and was forcefully killed after 900 seconds "
            "with no output and no new commit.",
        ))
        self.assertEqual(returned[0], -1)
        self.assertIn("no output and no new commit", printed)
        self.assertNotIn("timed out after", printed)

    def test_stdoutProse_shouldNotMaskAnEmptyStderr(self):
        """A mission that failed with only cheerful prose on stdout must not have
        that prose reported as its cause; the seam says it has no reason."""
        returned, printed, _ = self._logged((-1, "Everything is done and correct.", ""))
        self.assertEqual(returned[0], -1)
        self.assertIn("with no reported reason", printed)
        self.assertNotIn("Everything is done", printed)

    def test_raisingPlugin_shouldBeReportedAndStillPropagate(self):
        """A plugin that raises is the same defect by another route: the sweep
        ended with no reason line. Crash semantics must not change."""
        class _Raising:
            def run_mission(self, prompt, cwd, is_continue=False, model=None):
                raise OSError("cannot open session db")

        buffer = io.StringIO()
        with redirect_stdout(buffer):
            with self.assertRaises(OSError):
                self.runner.run_mission_logged(
                    _Raising(), "opencode", "m", "prompt", "/tmp/wt",
                    is_continue=False, phase="intake")
        printed = buffer.getvalue()
        self.assertIn("mission raised OSError", printed)
        self.assertIn("cannot open session db", printed)

    def test_successfulMission_shouldPrintNothing(self):
        returned, printed, _ = self._logged((0, "done", ""))
        self.assertEqual(returned[0], 0)
        self.assertEqual(printed, "", "a clean mission must not add log noise")

    def test_silentFailure_shouldSaySoRatherThanPrintATrailingColon(self):
        returned, printed, _ = self._logged((-1, "", ""))
        self.assertEqual(returned[0], -1)
        self.assertIn("with no reported reason", printed)
        self.assertNotIn("(exit=-1):", printed)

    def test_phase_shouldNameTheCallerSoTheLogIsAttributable(self):
        """Three sweeps share the seam, so the line has to say which one ran."""
        for phase in ("intake", "babysit-orphan", "babysit-repair"):
            with self.subTest(phase=phase):
                _returned, printed, _ = self._logged((-1, "", "Error: nope"), phase=phase)
                self.assertIn(f"[{phase}]", printed)

    def test_result_shouldBePassedThroughUnchanged(self):
        """Callers branch on all three values, so the seam must not alter them."""
        returned, _printed, _ = self._logged((-1, "out text", "Error: boom"))
        self.assertEqual(returned, (-1, "out text", "Error: boom"))

    def test_arguments_shouldReachThePluginUnchanged(self):
        """The seam must not change how a mission is dispatched."""
        _returned, _printed, plugin = self._logged((0, "", ""))
        self.assertEqual(plugin.calls, [{
            "prompt": "prompt", "cwd": str(self.worktree),
            "is_continue": False, "model": "gpt-6-luna",
        }])


class DroidTurnOutcomeTests(unittest.TestCase):
    """A droid turn that did not complete must reach the log.

    droid's stream-json reports its own verdict on the turn in an
    `agent_turn_outcome` event, and `model_usage_exhausted` is the one that occurs
    in practice (measured in the host's session files). The accumulator used to
    keep only `message` and `completion`, so that reason was dropped and a failed
    droid mission reported "with no reported reason" even though the CLI had said
    exactly what happened.
    """

    def setUp(self):
        self.runner = _load()
        spec = importlib.util.spec_from_file_location(
            "droid_plugin_for_reason", BASE / "plugins" / "droid.py")
        self.plugin = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.plugin)

    def _feed(self, *events):
        """Drive the accumulator the way the pipe drain does."""
        output = self.plugin._MissionOutput()
        for event in events:
            output._record_event(json.dumps(event))
        return output.stdout_text()

    def test_nonCompletedTurn_shouldSurfaceItsReason(self):
        text = self._feed({"type": "agent_turn_outcome", "reason": "model_usage_exhausted"})
        self.assertIn("model_usage_exhausted", text)
        self.assertEqual(
            self.runner._mission_failure_reason(text, ""),
            "Error: droid turn ended early: model_usage_exhausted",
        )

    def test_completedTurn_shouldNotBecomeAReason(self):
        """`completed` is the ordinary case and must not be logged as a failure."""
        text = self._feed(
            {"type": "agent_turn_outcome", "reason": "completed"},
            {"type": "completion", "finalText": "All done."},
        )
        self.assertNotIn("completed", text)
        self.assertNotIn("Error", text)

    def test_turnReason_shouldSurviveACompletionEvent(self):
        """`completion` wins `stdout_text`, so folding the reason into the message
        list would hide it exactly when the CLI also emitted a final text."""
        text = self._feed(
            {"type": "agent_turn_outcome", "reason": "model_usage_exhausted"},
            {"type": "completion", "finalText": "Partial work saved."},
        )
        self.assertIn("model_usage_exhausted", text)
        self.assertIn("Partial work saved.", text)
        self.assertEqual(
            self.runner._mission_failure_reason(text, ""),
            "Error: droid turn ended early: model_usage_exhausted",
        )

    def test_boundKill_shouldStillWinOverTheTurnReason(self):
        """A bound that fired is more actionable than the turn verdict, and the
        plugin puts it on stderr, which the extraction reads first."""
        out = self._feed({"type": "agent_turn_outcome", "reason": "model_usage_exhausted"})
        err = "Error: Droid mission timed out after 7200 seconds."
        self.assertEqual(self.runner._mission_failure_reason(out, err), err)

    def test_ordinaryAssistantOutput_shouldBeUnaffected(self):
        """The new branch must not change what a normal mission returns."""
        text = self._feed(
            {"type": "message", "role": "assistant", "text": "working"},
            {"type": "completion", "finalText": "done"},
        )
        self.assertEqual(text, "done\n")


class MissionLockRuntimeGuardTests(_CaptureCase):
    """The seam verifies the worktree lock at runtime, not only by static test.

    A static AST check only recognises the spellings it knows, so the seam itself
    reports a dispatch that happens outside the lock. It warns rather than raises:
    it is on the path of every mission, so a false positive must not break a run.
    """

    def test_dispatchWithoutTheLock_shouldWarn(self):
        plugin = _FakePlugin((0, "", ""))
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            self.runner.run_mission_logged(
                plugin, "droid", "m", "prompt", "/tmp/not-locked",
                is_continue=False, phase="intake")
        printed = buffer.getvalue()
        self.assertIn("WARNING", printed)
        self.assertIn("without this process holding its worktree mission lock", printed)

    def test_dispatchWithTheLockHeld_shouldNotWarn(self):
        """Driven through the real lock, so this is the shape every call site has."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.runner.STATE_DIR = str(root)
            worktree = root / "issue-42"
            worktree.mkdir()
            plugin = _FakePlugin((0, "", ""))
            buffer = io.StringIO()
            with redirect_stdout(buffer):
                with self.runner._worktree_mission_lock(str(worktree)) as may_drive:
                    self.assertTrue(may_drive)
                    self.runner.run_mission_logged(
                        plugin, "droid", "m", "prompt", str(worktree),
                        is_continue=False, phase="intake")
            self.assertNotIn("WARNING", buffer.getvalue())

    def test_theGuard_shouldNeverBreakAMission(self):
        """A diagnostic that raises would be worse than the hazard it reports."""
        self.runner._worktree_mission_lock_path = lambda _p: (_ for _ in ()).throw(RuntimeError("boom"))
        plugin = _FakePlugin((0, "fine", ""))
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            returned = self.runner.run_mission_logged(
                plugin, "droid", "m", "prompt", "/tmp/wt",
                is_continue=False, phase="intake")
        self.assertEqual(returned, (0, "fine", ""))
        self.assertNotIn("WARNING", buffer.getvalue())


class MissionCallSiteCoverageTests(unittest.TestCase):
    """Every mission call site must go through the seam.

    A call site that bypasses it is silent about its failures again, which is the
    defect this file exists for -- and it would be invisible to the tests above,
    since they exercise the seam directly.
    """

    def setUp(self):
        self.source = (BASE / "runner.py").read_text()
        self.tree = ast.parse(self.source)

    def _plugin_calls(self):
        """Every `<plugin>.run_mission(...)` call, with its enclosing function."""
        found = []
        for node in ast.walk(self.tree):
            if not isinstance(node, ast.FunctionDef):
                continue
            for child in ast.walk(node):
                if not isinstance(child, ast.Call):
                    continue
                func = child.func
                if isinstance(func, ast.Attribute) and func.attr == "run_mission":
                    found.append((node.name, child.lineno))
        return found

    def test_phases_shouldCoverAllThreeSweeps(self):
        """Each dispatch site names a distinct sweep, and the two babysit paths
        keep that prefix so `[babysit` still finds them alongside the `[babysit]`
        lines those functions already print."""
        phases = sorted(self._seam_phases())
        self.assertEqual(phases, ["babysit-orphan", "babysit-repair", "intake"])

    def _seam_phases(self):
        found = []
        for node in ast.walk(self.tree):
            if not isinstance(node, ast.Call):
                continue
            if getattr(node.func, "id", "") != "run_mission_logged":
                continue
            for keyword in node.keywords:
                if keyword.arg == "phase":
                    self.assertIsInstance(
                        keyword.value, ast.Constant,
                        "phase must be a literal, or this check cannot see it")
                    found.append(keyword.value.value)
        return found

    def test_theSeam_shouldBeTheOnlyCaller(self):
        calls = self._plugin_calls()
        self.assertEqual(
            [name for name, _line in calls], ["run_mission_logged"],
            "these call sites bypass run_mission_logged, so a non-zero exit is "
            f"logged nowhere: {calls}",
        )

    def test_asyncOrAliasedCallSites_shouldNotEscapeTheCheck(self):
        """The guard must recognise a dispatch however it is spelled, or it is a
        convention rather than a check. An aliased or attribute call to the seam
        is still a dispatch, and a `plugin.run_mission` attribute call is still a
        bypass, so both spellings are matched."""
        synthetic = ast.parse(
            "async def a():\n"
            "    await run_mission_logged(p, 'a', 'm', 'x', 'y', is_continue=False, phase='intake')\n"
            "def b():\n"
            "    mod.run_mission_logged(p, 'a', 'm', 'x', 'y', is_continue=False, phase='intake')\n"
            "def c():\n"
            "    plugin.run_mission('x', 'y')\n"
        )
        seam = [
            node for node in ast.walk(synthetic)
            if isinstance(node, ast.Call)
            and (getattr(node.func, "id", "") == "run_mission_logged"
                 or getattr(node.func, "attr", "") == "run_mission_logged")
        ]
        self.assertEqual(len(seam), 2, "an attribute-spelled seam call must be counted")
        direct = [
            node for node in ast.walk(synthetic)
            if isinstance(node, ast.Call) and getattr(node.func, "attr", "") == "run_mission"
        ]
        self.assertEqual(len(direct), 1, "an attribute-spelled direct call must be caught")


if __name__ == "__main__":
    unittest.main()
