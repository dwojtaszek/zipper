#!/usr/bin/env python3
"""Robot reviewers are optional; the required checks are not.

The merge gate is the required `ci-gate` context (the zipper test suite) plus
SonarCloud. Both are always present, because they run as repository workflows
rather than at an external app's discretion. The two review bots are neither:
CodeRabbit declines this repository outright (public OSS, fewer than 10 stars)
and rate-limits itself to a few reviews per rolling hour, and Codex sometimes
does not answer at all.

Waiting the full gate timeout for a reviewer that was never going to reply is the
delay this tracking removes. It removes only the *wait*: unresolved review threads
still block the merge through required conversation resolution, so a reviewer that
answers late with findings still stops the merge. The safety property is therefore
"we stopped waiting", never "we stopped checking".

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""
import os
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import patch

RUNNER_BASE = Path(__file__).resolve().parents[1]
# The gate script lives in the repository's own tests/ directory, one level above
# .zipper-runner/, and the wrapper invokes it by that relative path.
REPO_ROOT = RUNNER_BASE.parent
sys.path.insert(0, str(RUNNER_BASE))

import runner  # noqa: E402

CODERABBIT = "coderabbitai[bot]"
CODEX = "chatgpt-codex-connector[bot]"


class ReviewerAvailabilityTests(unittest.TestCase):
    """Folding the gate's outcome lines into the availability record."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        patcher = patch.object(runner, "STATE_DIR", self._tmp.name)
        patcher.start()
        self.addCleanup(patcher.stop)

    def _outcome_line(self, bot, state):
        return f"REVIEWER-OUTCOME {bot}={state}"

    def test_declaredSkip_shouldParkTheReviewer(self):
        runner._record_reviewer_outcomes(self._outcome_line(CODERABBIT, "skipped"))
        self.assertIn(CODERABBIT, runner._reviewers_currently_unavailable())

    def test_oneSilence_shouldNotParkTheReviewer(self):
        """One silence is noise: a busy queue looks identical to a dead bot."""
        runner._record_reviewer_outcomes(self._outcome_line(CODEX, "silent"))
        self.assertNotIn(CODEX, runner._reviewers_currently_unavailable())
        self.assertEqual(
            runner._load_reviewer_availability()[CODEX]["consecutive_silent"], 1)

    def test_repeatedSilence_shouldParkTheReviewer(self):
        """Two silences on separate cycles is a pattern, not noise."""
        for _ in range(runner.REVIEWER_SILENT_STRIKES):
            runner._record_reviewer_outcomes(self._outcome_line(CODEX, "silent"))
        self.assertIn(CODEX, runner._reviewers_currently_unavailable())

    def test_answeredReview_shouldClearTheRecord(self):
        runner._record_reviewer_outcomes(self._outcome_line(CODEX, "silent"))
        runner._record_reviewer_outcomes(self._outcome_line(CODEX, "silent"))
        self.assertIn(CODEX, runner._reviewers_currently_unavailable())
        runner._record_reviewer_outcomes(self._outcome_line(CODEX, "reviewed"))
        self.assertNotIn(CODEX, runner._reviewers_currently_unavailable())

    def test_skippedByPolicy_shouldNotParkTheReviewer(self):
        """Not being asked is not evidence that the reviewer is unavailable."""
        runner._record_reviewer_outcomes(self._outcome_line(CODEX, "skipped-by-policy"))
        self.assertNotIn(CODEX, runner._reviewers_currently_unavailable())

    def test_expiredWindow_shouldNotParkTheReviewerAndShouldBePruned(self):
        runner._record_reviewer_outcomes(self._outcome_line(CODERABBIT, "skipped"))
        data = runner._load_reviewer_availability()
        data[CODERABBIT]["unavailable_until"] = (
            datetime.now(timezone.utc) - timedelta(minutes=1)).isoformat()
        runner._save_reviewer_availability(data)
        self.assertNotIn(CODERABBIT, runner._reviewers_currently_unavailable())
        self.assertEqual(runner._load_reviewer_availability(), {},
                         "an expired entry must be dropped, not left to age forever")

    def test_corruptRecord_shouldNotParkTheReviewer(self):
        runner._save_reviewer_availability({CODEX: {"unavailable_until": "not-a-date"}})
        self.assertNotIn(CODEX, runner._reviewers_currently_unavailable())

    def test_unrelatedOutput_shouldBeIgnored(self):
        runner._record_reviewer_outcomes("just some log noise\nREVIEW-GATE-RESULT unresolved=0")
        self.assertEqual(runner._reviewers_currently_unavailable(), [])

    def test_outcomeWithTrailingNoise_shouldStillParse(self):
        runner._record_reviewer_outcomes(self._outcome_line(CODERABBIT, "skipped") + "\n\n")
        self.assertIn(CODERABBIT, runner._reviewers_currently_unavailable())

    def test_skipCooldown_shouldExpire(self):
        """The window must end on its own, or a one-off skip becomes permanent."""
        runner._record_reviewer_outcomes(self._outcome_line(CODERABBIT, "skipped"))
        data = runner._load_reviewer_availability()
        until = datetime.fromisoformat(data[CODERABBIT]["unavailable_until"])
        expected = datetime.now(timezone.utc) + timedelta(
            hours=runner.REVIEWER_SKIP_COOLDOWN_HOURS)
        self.assertLess(abs((until - expected).total_seconds()), 60)


class ReviewGateWrapperTests(unittest.TestCase):
    """The wrapper must not turn a failed read into a review verdict."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        patcher = patch.object(runner, "STATE_DIR", self._tmp.name)
        patcher.start()
        self.addCleanup(patcher.stop)

    def _run(self, code, out="", err="", unavailable=None):
        if unavailable:
            runner._save_reviewer_availability({
                bot: {"unavailable_until": (
                    datetime.now(timezone.utc) + timedelta(hours=1)).isoformat(),
                      "recorded_at": datetime.now(timezone.utc).isoformat(),
                      "reason": "test", "consecutive_silent": 0}
                for bot in unavailable
            })
        with patch.object(runner, "run_cmd", return_value=(code, out, err)) as rc:
            result = runner._run_review_gate(42, self._tmp.name)
        return result, rc

    def test_success_shouldBeOk(self):
        result, _ = self._run(0, out="REVIEWER-OUTCOME %s=reviewed" % CODEX)
        self.assertTrue(result.ok)
        self.assertFalse(result.environmental)

    def test_unresolvedThreads_shouldNotBeOkAndNotEnvironmental(self):
        """Exit 1 is a review outcome: a mission to fix the threads is correct."""
        result, _ = self._run(1, out="REVIEW-GATE-RESULT unresolved=2")
        self.assertFalse(result.ok)
        self.assertFalse(result.environmental)
        self.assertIn("unresolved=2", result.detail)

    def test_environmentalExit_shouldBeFlaggedNotTreatedAsFailure(self):
        """Exit 3 means nobody was asked; a mission would fix nothing."""
        result, _ = self._run(runner.REVIEW_GATE_ENVIRONMENTAL_EXIT,
                              out="REVIEW-GATE-RESULT unresolved=unknown")
        self.assertFalse(result.ok)
        self.assertTrue(result.environmental)

    def test_usageFailureExit_shouldBeEnvironmental(self):
        """Exit 2 is `gh` missing or `gh repo view` failing -- not a review verdict.

        Only exit 1 says review threads are unresolved. Reading any other nonzero
        code as a review failure sends an agent to fix threads nobody raised.
        """
        result, _ = self._run(2, err="gh: command not found")
        self.assertFalse(result.ok)
        self.assertTrue(result.environmental)
        self.assertIn("command not found", result.detail)

    def test_gateTimeout_shouldBeEnvironmental(self):
        """`run_cmd` returns -1 when it kills the gate; that is not a verdict."""
        result, _ = self._run(-1, err="timed out after 1500s")
        self.assertFalse(result.ok)
        self.assertTrue(result.environmental)

    def test_unavailableReviewers_shouldBePassedToTheGate(self):
        result, rc = self._run(0, unavailable=[CODERABBIT, CODEX])
        env = rc.call_args.kwargs.get("env") or {}
        self.assertEqual(env.get(runner.REVIEW_GATE_ENV_SKIP),
                         ",".join(sorted([CODERABBIT, CODEX])))

    def test_noUnavailableReviewers_shouldNotSetTheSkipVariable(self):
        _, rc = self._run(0)
        self.assertIsNone(rc.call_args.kwargs.get("env"))

    def test_quickCheck_shouldForwardItsWaitBudget(self):
        with patch.object(runner, "run_cmd", return_value=(1, "", "")) as rc:
            runner._run_review_gate(42, self._tmp.name, wait_minutes=1, timeout=300)
        args = rc.call_args.args[0]
        self.assertEqual(args[-1], "1", "the quick passes re-check every cycle")
        self.assertEqual(rc.call_args.kwargs["timeout"], 300)

    def test_outcomes_shouldBeRecordedFromTheGateOutput(self):
        self._run(0, out="REVIEWER-OUTCOME %s=skipped" % CODERABBIT)
        self.assertIn(CODERABBIT, runner._reviewers_currently_unavailable())

    def test_quickPassSilence_shouldNotCountAsAStrike(self):
        """A 1-minute timeout says nothing: the reviewer may just be queued.

        The quick passes run every cron cycle, so counting their silences would park
        a working reviewer within about ten minutes of normal queue delay.
        """
        out = "REVIEWER-OUTCOME %s=silent" % CODEX
        with patch.object(runner, "run_cmd", return_value=(1, out, "")):
            runner._run_review_gate(42, self._tmp.name, wait_minutes=1, timeout=300)
        self.assertEqual(self._strikes(CODEX), 0,
                         "a 1m budget is not evidence the reviewer is gone")

    def test_fullBudgetSilence_shouldCountAsAStrike(self):
        out = "REVIEWER-OUTCOME %s=silent" % CODEX
        with patch.object(runner, "run_cmd", return_value=(1, out, "")):
            runner._run_review_gate(42, self._tmp.name, timeout=300)
        self.assertEqual(self._strikes(CODEX), 1,
                         "a full-budget silence is the signal the strike count is for")

    def test_quickPassSilence_shouldStillRecordASkip(self):
        """Only silences are filtered; a declared skip is evidence at any budget."""
        out = "REVIEWER-OUTCOME %s=skipped" % CODERABBIT
        with patch.object(runner, "run_cmd", return_value=(1, out, "")):
            runner._run_review_gate(42, self._tmp.name, wait_minutes=1, timeout=300)
        self.assertIn(CODERABBIT, runner._reviewers_currently_unavailable())

    def _strikes(self, bot) -> int:
        entry = runner._load_reviewer_availability().get(bot) or {}
        return runner._reviewer_entry_state(entry, datetime.now(timezone.utc))[1]


class RunCmdEnvironmentTests(unittest.TestCase):
    """The skip list has to reach the script, not just the wrapper's head."""

    def test_envParameter_shouldReachTheChildProcess(self):
        variable = "RUNNER_TEST_ENV_PROBE"
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop(variable, None)
            code, out, _ = runner.run_cmd(
                [sys.executable, "-c", f"import os; print(os.environ.get('{variable}', ''))"],
                env={variable: "propagated"},
            )
        self.assertEqual(code, 0)
        self.assertEqual(out.strip(), "propagated")

    def test_envParameter_shouldNotLeakIntoTheParentEnvironment(self):
        variable = "RUNNER_TEST_ENV_LEAK"
        os.environ.pop(variable, None)
        runner.run_cmd([sys.executable, "-c", "pass"], env={variable: "propagated"})
        self.assertNotIn(variable, os.environ,
                         "the override is per-command, not a global mutation")

    def test_baseEnvironment_shouldStillApply(self):
        code, out, _ = runner.run_cmd(
            [sys.executable, "-c", "import os; print(os.environ.get('HOME', ''))"])
        self.assertEqual(code, 0)
        self.assertTrue(out.strip(), "the inherited environment must survive the override")


class ReviewGateEndToEndTests(unittest.TestCase):
    """The real gate script and the real wrapper, with a stubbed `gh`.

    The unit tests above stub `run_cmd`, so they cannot show that the skip list
    actually changes the script's behaviour. This one runs both for real.
    """

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)
        patcher = patch.object(runner, "STATE_DIR", str(self.root / "state"))
        patcher.start()
        self.addCleanup(patcher.stop)
        os.makedirs(self.root / "state", exist_ok=True)
        self._stub_gh()

    def _stub_gh(self):
        bin_dir = self.root / "bin"
        bin_dir.mkdir(parents=True, exist_ok=True)
        gh = bin_dir / "gh"
        gh.write_text(
            "#!/usr/bin/env python3\n"
            "import json, sys\n"
            "args = sys.argv[1:]\n"
            "if args[:2] == ['repo', 'view']:\n"
            "    print('dwojtaszek/zipper')\n"
            "elif args[:2] == ['api', '--paginate']:\n"
            "    print('[]')\n"
            "elif args[0] == 'api':\n"
            "    print(json.dumps({'data': {'repository': {'pullRequest':\n"
            "        {'reviewThreads': {'nodes': []}}}}}))\n"
            "else:\n"
            "    raise SystemExit('unexpected: ' + ' '.join(args))\n"
        )
        gh.chmod(0o755)
        self.env = {"PATH": f"{bin_dir}{os.pathsep}{os.environ['PATH']}"}

    def _park(self, bot, hours=1):
        runner._save_reviewer_availability({
            bot: {"unavailable_until": (
                datetime.now(timezone.utc) + timedelta(hours=hours)).isoformat(),
                "recorded_at": datetime.now(timezone.utc).isoformat(),
                "reason": "declared a skip", "consecutive_silent": 0},
        })

    def _run_gate_with_stub_env(self):
        """Run the real gate with the stubbed `gh` reachable via PATH."""
        real_run_cmd = runner.run_cmd

        def with_stub_env(args, cwd=None, timeout=None, env=None):
            merged = dict(self.env)
            if env:
                merged.update(env)
            return real_run_cmd(args, cwd=cwd, timeout=timeout, env=merged)

        with patch.object(runner, "run_cmd", side_effect=with_stub_env):
            return runner._run_review_gate(42, str(REPO_ROOT), wait_minutes=0)

    def test_knownUnavailableReviewer_shouldNotBeWaitedOn(self):
        """A parked reviewer must be skipped by the script, not just by the wrapper."""
        self._park(CODERABBIT)

        result = self._run_gate_with_stub_env()

        self.assertFalse(result.environmental, result.detail)
        # CodeRabbit was parked, so only Codex is asked -- and it times out at 0m.
        self.assertNotIn(f"{CODERABBIT} did not respond", result.detail)
        self.assertIn(f"{CODEX} did not respond", result.detail)

    def test_skippedByPolicy_shouldKeepTheParkAcrossCycles(self):
        """The park must outlast the gate run that skipped the reviewer.

        The gate reports `skipped-by-policy` for every reviewer the wrapper parked,
        so treating that state as "answering again" would delete the park on the very
        next cycle and the cooldown would never outlast a single pass.
        """
        self._park(CODERABBIT)

        self._run_gate_with_stub_env()

        self.assertIn(CODERABBIT, runner._reviewers_currently_unavailable(),
                      "the gate's own skip report must not clear the park it came from")

    def test_answeringReviewer_shouldClearThePark(self):
        self._park(CODERABBIT)
        self.assertIn(CODERABBIT, runner._reviewers_currently_unavailable())
        # The stub reports no reviews, so both reviewers are silent; one strike each.
        runner._record_reviewer_outcomes(
            f"REVIEWER-OUTCOME {CODERABBIT}=reviewed\nREVIEWER-OUTCOME {CODEX}=silent")
        self.assertNotIn(CODERABBIT, runner._reviewers_currently_unavailable())


if __name__ == "__main__":
    unittest.main()
