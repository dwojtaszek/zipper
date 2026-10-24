"""Unit tests for runner.py state helpers (rerun budget, injection marker).

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import MagicMock, patch

import gitenv  # noqa: F401  # Git identity for fixture repositories

# STATE_DIR must be pinned before runner.py reads it at import time.
_STATE_DIR = tempfile.mkdtemp(prefix="zipper-runner-state-")
os.environ["STATE_DIR"] = _STATE_DIR

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402
runner.STATE_DIR = _STATE_DIR


class RerunBudgetTests(unittest.TestCase):
    def tearDown(self):
        for name in os.listdir(_STATE_DIR):
            os.remove(os.path.join(_STATE_DIR, name))

    def test_bump_then_get_withSameHead_ExpectedCount(self):
        self.assertEqual(runner._get_rerun_count(1, "sha-a"), 0)
        self.assertEqual(runner._bump_rerun_count(1, "sha-a"), 1)
        self.assertEqual(runner._get_rerun_count(1, "sha-a"), 1)

    def test_get_withDifferentHead_ExpectedBudgetReset(self):
        runner._bump_rerun_count(2, "sha-a")
        self.assertEqual(runner._get_rerun_count(2, "sha-b"), 0)
        # Bumping on the new head stores the new head and starts at 1 again.
        self.assertEqual(runner._bump_rerun_count(2, "sha-b"), 1)
        self.assertEqual(runner._get_rerun_count(2, "sha-b"), 1)

    def test_bump_persists_head_sha(self):
        runner._bump_rerun_count(3, "sha-a")
        with open(runner._rerun_count_path(3)) as f:
            data = json.load(f)
        self.assertEqual(data, {"count": 1, "head_sha": "sha-a"})

    def test_get_withoutHead_ReadsLegacyAndNewFiles(self):
        # Legacy files (pre-head-binding) still count.
        with open(runner._rerun_count_path(4), "w") as f:
            json.dump({"count": 2}, f)
        self.assertEqual(runner._get_rerun_count(4), 2)

    def test_get_withHead_AgainstLegacyFile_ExpectedBudgetReset(self):
        # A legacy file (no head_sha) cannot prove it belongs to this head,
        # so the head-bound read treats the budget as fresh.
        with open(runner._rerun_count_path(6), "w") as f:
            json.dump({"count": 2}, f)
        self.assertEqual(runner._get_rerun_count(6, "sha-a"), 0)

    def test_clear_removes_state(self):
        runner._bump_rerun_count(5, "sha-a")
        runner._clear_rerun_count(5)
        self.assertFalse(os.path.exists(runner._rerun_count_path(5)))


class CiRollupTests(unittest.TestCase):
    """_ci_status_from_rollup: shared rollup walk for all three babysit passes."""

    @staticmethod
    def _check_run(name, status, conclusion):
        return {"__typename": "CheckRun", "name": name, "status": status, "conclusion": conclusion}

    @staticmethod
    def _status_context(context, state):
        return {"__typename": "StatusContext", "context": context, "state": state}

    def test_rollup_withAnyFailedCheck_ExpectedFailed(self):
        rollup = [
            self._check_run("build", "COMPLETED", "FAILURE"),
            self._check_run("lint", "COMPLETED", "SUCCESS"),
        ]
        status, failures = runner._ci_status_from_rollup(rollup)
        self.assertEqual(status, "FAILED")
        self.assertEqual(failures, ["build (FAILURE)"])

    def test_rollup_withAllChecksPassed_ExpectedSuccess(self):
        rollup = [
            self._check_run("build", "COMPLETED", "SUCCESS"),
            self._check_run("lint", "COMPLETED", "NEUTRAL"),
            self._check_run("goldens", "COMPLETED", "SKIPPED"),
        ]
        self.assertEqual(runner._ci_status_from_rollup(rollup), ("SUCCESS", []))

    def test_rollup_withPendingCheck_ExpectedPending(self):
        rollup = [
            self._check_run("build", "COMPLETED", "SUCCESS"),
            self._check_run("goldens", "IN_PROGRESS", None),
        ]
        self.assertEqual(runner._ci_status_from_rollup(rollup), ("PENDING", []))

    def test_rollup_empty_ExpectedPending(self):
        self.assertEqual(runner._ci_status_from_rollup([]), ("PENDING", []))

    def test_rollup_withFailedStatusContext_ExpectedFailedWithContext(self):
        rollup = [
            self._status_context("ci-gate", "FAILURE"),
            self._status_context("other", "PENDING"),
            self._check_run("build", "COMPLETED", "SUCCESS"),
        ]
        status, failures = runner._ci_status_from_rollup(rollup)
        self.assertEqual(status, "FAILED")
        self.assertEqual(failures, ["ci-gate (FAILURE)"])

    def test_rollup_withErroredStatusContext_ExpectedFailed(self):
        rollup = [self._status_context("sonar", "ERROR")]
        status, failures = runner._ci_status_from_rollup(rollup)
        self.assertEqual(status, "FAILED")
        self.assertEqual(failures, ["sonar (ERROR)"])

    def test_rollup_withOnlyPendingStatusContext_ExpectedPending(self):
        rollup = [self._status_context("ci-gate", "PENDING")]
        self.assertEqual(runner._ci_status_from_rollup(rollup), ("PENDING", []))

    def test_rollup_withExpectedStatusContext_ExpectedPendingNotSuccess(self):
        # EXPECTED = a required context that has not started; it must not
        # count as passed (CodeRabbit finding, #988).
        rollup = [
            self._check_run("build", "COMPLETED", "SUCCESS"),
            self._status_context("ci-gate", "EXPECTED"),
        ]
        self.assertEqual(runner._ci_status_from_rollup(rollup), ("PENDING", []))


import time
from unittest.mock import patch


class InjectionMarkerTests(unittest.TestCase):
    def test_marker_path_UsesStateDir(self):
        self.assertEqual(
            runner._injection_marker_path(42),
            os.path.join(_STATE_DIR, "issue-42-injection.json"),
        )


class PruneRateFilesTests(unittest.TestCase):
    def tearDown(self):
        for name in os.listdir(_STATE_DIR):
            try:
                os.remove(os.path.join(_STATE_DIR, name))
            except OSError:
                pass

    def test_prune_deletesFilesOlderThanCooldown(self):
        path = os.path.join(_STATE_DIR, "rate-old.json")
        with open(path, "w") as f:
            f.write("{}")
        past = time.time() - 25 * 3600
        os.utime(path, (past, past))

        runner._prune_rate_files(cooldown_hours=24)
        self.assertFalse(os.path.exists(path))

    def test_prune_preservesFilesWithinCooldown(self):
        path = os.path.join(_STATE_DIR, "rate-recent.json")
        with open(path, "w") as f:
            f.write("{}")
        recent = time.time() - 1 * 3600
        os.utime(path, (recent, recent))

        runner._prune_rate_files(cooldown_hours=24)
        self.assertTrue(os.path.exists(path))

    def test_prune_ignoresNonRateFiles(self):
        path = os.path.join(_STATE_DIR, "issue-42.json")
        with open(path, "w") as f:
            f.write("{}")
        past = time.time() - 48 * 3600
        os.utime(path, (past, past))

        runner._prune_rate_files(cooldown_hours=24)
        self.assertTrue(os.path.exists(path))


class CodeRabbitPrePrCheckTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.mkdtemp(prefix="fake-cr-")
        self.orig_path = os.environ.get("PATH", "")
        os.environ["PATH"] = f"{self.temp_dir}:{self.orig_path}"
        # The rate-limit window is persisted, so it must not leak between tests or
        # into the real state directory.
        self.orig_state_dir = runner.STATE_DIR
        runner.STATE_DIR = os.path.join(self.temp_dir, "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)

    def tearDown(self):
        os.environ["PATH"] = self.orig_path
        runner.STATE_DIR = self.orig_state_dir
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def _create_fake_cr(self, script_body: str, exit_code: int = 0) -> str:
        path = os.path.join(self.temp_dir, "coderabbit")
        with open(path, "w") as f:
            f.write(f"#!/bin/sh\n{script_body}\nexit {exit_code}\n")
        os.chmod(path, 0o755)
        return path

    def test_whenCoderabbitNotInstalled_returnsFalse(self):
        empty_dir = os.path.join(self.temp_dir, "empty")
        os.makedirs(empty_dir, exist_ok=True)
        fake_home = os.path.join(self.temp_dir, "fake_home")
        os.makedirs(fake_home, exist_ok=True)
        with patch.dict(os.environ, {"PATH": empty_dir, "HOME": fake_home}):
            gate = runner._run_coderabbit_pre_pr_check("/fake/path")
            self.assertFalse(gate.ok)
            self.assertFalse(gate.skipped)
            self.assertIn("not found", gate.detail)

    def test_whenCoderabbitZeroFindings_returnsTrue(self):
        output = 'echo \'{"type":"complete","status":"review_completed","findings":0}\''
        self._create_fake_cr(output, exit_code=0)
        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertTrue(gate.ok)
        self.assertFalse(gate.skipped, "a real review is not a skip")
        self.assertEqual(gate.detail, "")

    def test_sameReviewedCommits_reusesReview_untilHeadOrBaseChanges(self):
        repo = os.path.join(self.temp_dir, "repo")
        os.makedirs(repo)
        subprocess.run(["git", "init", "-b", "main", repo], check=True, capture_output=True)
        def git(*args):
            subprocess.run(["git", "-C", repo, *args], check=True, stdout=subprocess.PIPE)
        git("commit", "--allow-empty", "-m", "test: base")
        git("switch", "-c", "fix/review")
        calls = os.path.join(self.temp_dir, "calls")
        self._create_fake_cr(f"echo call >> '{calls}'\necho '{{\"type\":\"complete\",\"status\":\"review_completed\",\"findings\":0}}'")
        self.assertTrue(runner._run_coderabbit_pre_pr_check(repo).ok)
        self.assertTrue(runner._run_coderabbit_pre_pr_check(repo).ok)
        self.assertEqual(Path(calls).read_text().splitlines(), ["call"])
        git("commit", "--allow-empty", "-m", "test: changed head")
        self.assertTrue(runner._run_coderabbit_pre_pr_check(repo).ok)
        git("branch", "-f", "main", "HEAD")
        self.assertTrue(runner._run_coderabbit_pre_pr_check(repo).ok)
        self.assertEqual(len(Path(calls).read_text().splitlines()), 3)
        dirty = Path(repo) / "uncommitted"
        dirty.write_text("pending change")
        gate = runner._run_coderabbit_pre_pr_check(repo)
        self.assertFalse(gate.ok)
        self.assertIn("Commit all changes", gate.detail)
        self.assertEqual(len(Path(calls).read_text().splitlines()), 3)
        dirty.unlink()
        git("commit", "--allow-empty", "-m", "test: next head")
        self._create_fake_cr(
            "git commit --allow-empty -m 'test: changed during review' >/dev/null\n"
            "echo '{\"type\":\"complete\",\"status\":\"review_completed\",\"findings\":0}'"
        )
        gate = runner._run_coderabbit_pre_pr_check(repo)
        self.assertFalse(gate.ok)
        self.assertIn("changed during", gate.detail)

    def test_whenCoderabbitReviewSkipped_returnsFalse(self):
        output = 'echo \'{"type":"complete","status":"review_skipped","findings":0}\''
        self._create_fake_cr(output, exit_code=0)
        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(gate.ok)
        self.assertIn("review_skipped", gate.detail)

    def test_whenCoderabbitHasFindings_returnsFalse(self):
        output = (
            'echo \'{"type":"finding","ruleId":"bug","message":"bad code"}\'\n'
            'echo \'{"type":"complete","status":"review_completed","findings":1}\''
        )
        self._create_fake_cr(output, exit_code=0)
        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(gate.ok)
        self.assertIn("unresolved", gate.detail)

    def test_whenCoderabbitHasMultipleFindings_aggregatesAllFindings(self):
        output = (
            'echo \'{"type":"finding","ruleId":"bug1","message":"first bad code"}\'\n'
            'echo \'{"type":"finding","ruleId":"bug2","message":"second bad code"}\'\n'
            'echo \'{"type":"complete","status":"review_completed","findings":2}\''
        )
        self._create_fake_cr(output, exit_code=0)
        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(gate.ok)
        self.assertIn("2 unresolved finding(s)", gate.detail)
        self.assertIn("bug1", gate.detail)
        self.assertIn("bug2", gate.detail)

    def test_whenCoderabbitMissingCompleteRecord_returnsFalse(self):
        output = 'echo \'{"type":"review_context","reviewType":"committed"}\''
        self._create_fake_cr(output, exit_code=0)
        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(gate.ok)
        self.assertIn("completion record", gate.detail)

    def test_whenCoderabbitFails_returnsFalse(self):
        output = 'echo "auth failure" >&2'
        self._create_fake_cr(output, exit_code=1)
        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(gate.ok)
        self.assertIn("failed (exit 1)", gate.detail)


class CodeRabbitRateLimitTests(unittest.TestCase):
    """A rate-limit refusal is a skip, never a gate failure.

    Reading it as a failure blocked PR creation and merging on a review that never
    ran and dispatched a babysit mission to fix it: 34 runs were blocked this way,
    25 naming a rate or usage limit.
    """

    def setUp(self):
        self.temp_dir = tempfile.mkdtemp(prefix="fake-cr-limit-")
        self.orig_path = os.environ.get("PATH", "")
        os.environ["PATH"] = f"{self.temp_dir}:{self.orig_path}"
        self.orig_state_dir = runner.STATE_DIR
        runner.STATE_DIR = os.path.join(self.temp_dir, "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        runner._CODERABBIT_REVIEW_CACHE.clear()

    def tearDown(self):
        os.environ["PATH"] = self.orig_path
        runner.STATE_DIR = self.orig_state_dir
        runner._CODERABBIT_REVIEW_CACHE.clear()
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def _create_fake_cr(self, script_body: str, exit_code: int = 0) -> str:
        path = os.path.join(self.temp_dir, "coderabbit")
        with open(path, "w") as f:
            f.write(f"#!/bin/sh\n{script_body}\nexit {exit_code}\n")
        os.chmod(path, 0o755)
        return path

    def _rate_limit_record(self, wait_time: str) -> str:
        payload = json.dumps({
            "type": "error", "errorType": "rate_limit", "message": "Rate limit exceeded",
            "recoverable": True, "details": {}, "metadata": {"isProUser": False, "waitTime": wait_time},
        })
        return f"echo '{payload}'"

    def test_whenCoderabbitIsRateLimited_theGateIsSkippedNotFailed(self):
        """This is the defect: `{"type":"error","errorType":"rate_limit"}` was parsed
        as neither a finding nor a completion, so it fell through to a gate failure."""
        self._create_fake_cr(self._rate_limit_record("19 minutes"), exit_code=1)

        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertTrue(gate.ok, "a rate limit must not block the pipeline")
        self.assertTrue(gate.skipped, "it must be reported as a skip, not as evidence")

    def test_whenCoderabbitIsRateLimited_theWindowIsPersisted(self):
        self._create_fake_cr(self._rate_limit_record("19 minutes"), exit_code=1)

        runner._run_coderabbit_pre_pr_check(self.temp_dir)

        state = json.loads(Path(runner._coderabbit_rate_limit_path()).read_text())
        self.assertEqual(state["wait_seconds"], 19 * 60)
        self.assertIn("Rate limit exceeded", state["detail"])

    def test_whenInsideTheWindow_theCliIsNotInvokedAgain(self):
        """The persisted backoff is the point: the CLI call costs a 10-minute timeout
        and can only fail again."""
        calls = os.path.join(self.temp_dir, "calls")
        self._create_fake_cr(f"echo call >> '{calls}'\n{self._rate_limit_record('19 minutes')}", exit_code=1)
        runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertEqual(len(Path(calls).read_text().splitlines()), 1)

        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertTrue(gate.ok)
        self.assertTrue(gate.skipped)
        self.assertEqual(len(Path(calls).read_text().splitlines()), 1,
                         "the CLI must not be called while the window is open")

    def test_whenTheWindowHasPassed_theCliIsInvokedAgain(self):
        """The skip must expire, or one refusal would disable the gate forever."""
        calls = os.path.join(self.temp_dir, "calls")
        self._create_fake_cr(f"echo call >> '{calls}'\n{self._rate_limit_record('19 minutes')}", exit_code=1)
        runner._run_coderabbit_pre_pr_check(self.temp_dir)
        Path(runner._coderabbit_rate_limit_path()).write_text(json.dumps({
            "resume_at": (datetime.now(timezone.utc) - timedelta(minutes=1)).isoformat(),
        }))

        runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertEqual(len(Path(calls).read_text().splitlines()), 2)

    def test_whenAReviewSucceeds_anyRecordedWindowIsCleared(self):
        """Otherwise a stale window would keep skipping a gate that now works."""
        self._create_fake_cr(self._rate_limit_record("19 minutes"), exit_code=1)
        runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertTrue(Path(runner._coderabbit_rate_limit_path()).exists())
        # Expire the window, as waiting it out does, then let the review succeed.
        Path(runner._coderabbit_rate_limit_path()).write_text(json.dumps({
            "resume_at": (datetime.now(timezone.utc) - timedelta(minutes=1)).isoformat(),
        }))
        self._create_fake_cr('echo \'{"type":"complete","status":"review_completed","findings":0}\'')

        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertTrue(gate.ok)
        self.assertFalse(gate.skipped)
        self.assertFalse(Path(runner._coderabbit_rate_limit_path()).exists(),
                         "a completed review must clear the recorded window")

    def test_rateLimitOnStderrWithNonZeroExit_isSkipped(self):
        """The refusal is reported on stderr with an exit code when the CLI fails
        before it can emit a record."""
        self._create_fake_cr('echo "Error: Rate limit exceeded" >&2', exit_code=1)

        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertTrue(gate.ok)
        self.assertTrue(gate.skipped)

    def test_usageLimitWording_isAlsoSkipped(self):
        self._create_fake_cr('echo "You have used all 3 included reviews" >&2', exit_code=1)

        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertTrue(gate.ok)
        self.assertTrue(gate.skipped, "a usage-limit refusal is the same refusal")

    def test_nonRateLimitFailure_isStillAGateFailure(self):
        """The skip must not swallow real failures: an auth error is not a rate limit."""
        self._create_fake_cr('echo "authentication failed" >&2', exit_code=1)

        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertFalse(gate.ok, "a genuine CLI failure must still block")
        self.assertFalse(gate.skipped)
        self.assertFalse(Path(runner._coderabbit_rate_limit_path()).exists())

    def test_waitTimeForms_areAllParsed(self):
        for wait_time, expected in (("19 minutes", 19 * 60), ("37m", 37 * 60),
                                    ("1h", 3600), ("2s", 2), ("5 mins", 300)):
            with self.subTest(wait_time=wait_time):
                self.assertEqual(runner._coderabbit_wait_seconds(wait_time), expected)

    def test_whenWaitTimeIsAbsentOrGarbled_theDefaultWindowIsUsed(self):
        for wait_time in ("", "soon", "a while"):
            with self.subTest(wait_time=wait_time):
                self.assertIsNone(runner._coderabbit_wait_seconds(wait_time))
        self._create_fake_cr(self._rate_limit_record("soon"), exit_code=1)

        runner._run_coderabbit_pre_pr_check(self.temp_dir)

        state = json.loads(Path(runner._coderabbit_rate_limit_path()).read_text())
        self.assertEqual(state["wait_seconds"], runner.CODERABBIT_RATE_LIMIT_MINUTES * 60)

    def test_anImplausibleWaitTime_isCapped(self):
        """A garbled hint must not park the gate for days."""
        self.assertEqual(runner._coderabbit_wait_seconds("99999 hours"),
                         runner.CODERABBIT_RATE_LIMIT_MAX_MINUTES * 60)

    def test_aBareNumberWithNoUnit_isNotADuration(self):
        """A count in the message text must not be read as a wait."""
        self.assertIsNone(runner._coderabbit_wait_seconds("3 reviews used, retry later"))

    def test_whenTheStateFileIsCorrupt_theGateIsNotSkipped(self):
        """Fail open on a damaged window: skipping forever would disable the gate."""
        Path(runner._coderabbit_rate_limit_path()).write_text("{not json")
        self._create_fake_cr('echo \'{"type":"complete","status":"review_completed","findings":0}\'')

        gate = runner._run_coderabbit_pre_pr_check(self.temp_dir)

        self.assertTrue(gate.ok)
        self.assertFalse(gate.skipped)


class CodeRabbitSkipCallSiteTests(unittest.TestCase):
    """A skip must let the caller proceed, and must not be recorded as a failure."""

    def setUp(self):
        self.temp_dir = tempfile.mkdtemp(prefix="fake-cr-site-")
        self.orig_state_dir = runner.STATE_DIR
        runner.STATE_DIR = os.path.join(self.temp_dir, "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)

    def tearDown(self):
        runner.STATE_DIR = self.orig_state_dir
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_createPrForBranch_whenTheGateIsSkipped_stillCreatesThePR(self):
        """Blocking here is what left finished work unpublished for up to an hour."""
        calls = []

        def fake_run_cmd(args, cwd=None, timeout=None):
            calls.append(args)
            if args[:2] == ["gh", "pr"]:
                return 0, "https://example.test/99\n", ""
            return 0, "", ""

        skipped = runner.CodeRabbitGate(True, True, "skipped: rate limited for 19m")
        with patch.object(runner, "_run_coderabbit_pre_pr_check", return_value=skipped), \
                patch.object(runner, "_check_pr", return_value=("99", "https://example.test/99")), \
                patch.object(runner, "_save_pr_state"), \
                patch.object(runner, "run_cmd", side_effect=fake_run_cmd):
            pr_num, url, error = runner._create_pr_for_branch("fix/ISSUE-1-x", "1", "Title")

        self.assertEqual(error, "", "a rate-limited review must not fail PR creation")
        self.assertEqual(pr_num, "99")
        self.assertEqual(url, "https://example.test/99")
        self.assertTrue(any(c[:2] == ["gh", "pr"] for c in calls), "the PR must actually be created")

    def test_createPrForBranch_whenTheGateFailed_stillBlocksThePR(self):
        """The skip must not weaken the gate for a real failure."""
        failed = runner.CodeRabbitGate(False, False, "coderabbit review failed (exit 1)")
        with patch.object(runner, "_run_coderabbit_pre_pr_check", return_value=failed), \
                patch.object(runner, "run_cmd") as run_cmd:
            _, _, error = runner._create_pr_for_branch("fix/ISSUE-1-x", "1", "Title")

        self.assertIn("CodeRabbit review failed", error)
        run_cmd.assert_not_called()


class CodeRabbitGateStateTests(unittest.TestCase):
    def tearDown(self):
        for name in os.listdir(_STATE_DIR):
            try:
                os.remove(os.path.join(_STATE_DIR, name))
            except OSError:
                pass

    def test_gate_failure_lifecycle(self):
        self.assertFalse(runner._is_coderabbit_gate_failed(123))
        runner._record_coderabbit_gate_failure(123, "fix/test", "finding alert")
        self.assertTrue(runner._is_coderabbit_gate_failed(123))
        self.assertEqual(
            runner._coderabbit_gate_path(123),
            os.path.join(_STATE_DIR, "pr-123-coderabbit-gate.json"),
        )
        runner._clear_coderabbit_gate_failure(123)
        self.assertFalse(runner._is_coderabbit_gate_failed(123))


class RunCmdTimeoutTests(unittest.TestCase):
    def test_run_cmd_terminates_descendant_processes_on_timeout(self):
        temp_dir = tempfile.mkdtemp(prefix="timeout-test-")
        try:
            marker = os.path.join(temp_dir, "marker.txt")
            cmd = ["sh", "-c", f"(sleep 0.3 && touch {marker}) & wait"]
            code, out, err = runner.run_cmd(cmd, timeout=0.05)
            self.assertEqual(code, -1)
            self.assertIn("timed out after 0.05s", err)
            time.sleep(0.4)
            self.assertFalse(os.path.exists(marker))
        finally:
            shutil.rmtree(temp_dir, ignore_errors=True)


class CountReviewThreadsTests(unittest.TestCase):
    def test_whenReviewOutputProvided_doesNotRunCommand(self):
        output = "[ WARNING ] 3 unresolved review threads on PR #42"
        with patch("runner.run_cmd") as mock_run_cmd:
            threads = runner._count_review_threads(42, review_output=output)
            self.assertEqual(threads, 3)
            mock_run_cmd.assert_not_called()

    def test_whenReviewOutputNone_runsCommand(self):
        with patch("runner.run_cmd", return_value=(0, "2", "")) as mock_run_cmd:
            threads = runner._count_review_threads(42, review_output=None)
            self.assertEqual(threads, 2)
            mock_run_cmd.assert_called_once()
            args = mock_run_cmd.call_args[0][0]
            self.assertEqual(args[:3], ["gh", "api", "graphql"])
            self.assertIn("number=42", args)

    def test_whenNoUnresolvedThreadsInOutput_returnsZero(self):
        output = "[ OK ] No unresolved review threads on PR #42"
        threads = runner._count_review_threads(42, review_output=output)
        self.assertEqual(threads, 0)


class BabysitPromptTests(unittest.TestCase):
    def test_buildBabysitPrompt_withoutError_containsBasicStructure(self):
        prompt = runner._build_babysit_prompt(
            issue_number="1117",
            issue_title="test: cancellation",
            safe_body="Repro details",
            branch="fix/ISSUE-1117-test",
        )
        self.assertIn("Resume work on GitHub issue #1117.", prompt)
        self.assertIn("Title: test: cancellation", prompt)
        self.assertIn("Body and Comments:\nRepro details", prompt)
        self.assertIn("You are on branch 'fix/ISSUE-1117-test'", prompt)
        self.assertNotIn("CRITICAL: Direct PR creation failed", prompt)

    def test_buildBabysitPrompt_withError_injectsReviewFeedback(self):
        prompt = runner._build_babysit_prompt(
            issue_number="1117",
            issue_title="test: cancellation",
            safe_body="Repro details",
            branch="fix/ISSUE-1117-test",
            pr_create_err="CodeRabbit found 1 issue",
        )
        self.assertIn("<gate-error-data>", prompt)
        self.assertIn("CodeRabbit found 1 issue", prompt)

    def test_buildBabysitPrompt_withMutationSmokeError_injectsMutationGuidance(self):
        prompt = runner._build_babysit_prompt(
            issue_number="1117",
            issue_title="test: cancellation",
            safe_body="Repro details",
            branch="fix/ISSUE-1117-test",
            pr_create_err="❌ Mutation smoke found surviving mutants. Push aborted.",
        )
        self.assertIn("CRITICAL MUTATION SMOKE GATE FAILURE:", prompt)
        self.assertIn("run-mutation-smoke.sh", prompt)
        self.assertIn("SKIP_MUTATION_SMOKE is forbidden", prompt)


class WipCommitTests(unittest.TestCase):
    def test_whenCommitStarts_withWip_returnsTrue(self):
        with patch("runner.run_cmd", return_value=(0, "wip: progress on issue #1117 by agy\n", "")):
            self.assertTrue(runner._latest_commit_is_wip("/tmp/fake"))

    def test_whenCommitDoesNotStart_withWip_returnsFalse(self):
        with patch("runner.run_cmd", return_value=(0, "test: fix cancellation\n", "")):
            self.assertFalse(runner._latest_commit_is_wip("/tmp/fake"))

    def test_whenCommandFails_returnsFalse(self):
        with patch("runner.run_cmd", return_value=(1, "", "fatal: not a git repo")):
            self.assertFalse(runner._latest_commit_is_wip("/tmp/fake"))

    def test_whenPrHeadStarts_withWip_returnsTrue(self):
        with patch("runner.run_cmd", return_value=(0, "wip: progress on issue #1117 by agy\n", "")):
            self.assertTrue(runner._pr_head_commit_is_wip(1117))

    def test_whenPrHeadDoesNotStart_withWip_returnsFalse(self):
        with patch("runner.run_cmd", return_value=(0, "test: clean cancellation\n", "")):
            self.assertFalse(runner._pr_head_commit_is_wip(1117))

    def test_whenPrHeadCommandFails_returnsFalse(self):
        with patch("runner.run_cmd", return_value=(1, "", "gh: error")):
            self.assertFalse(runner._pr_head_commit_is_wip(1117))


class OpenCodeSessionScopingTests(unittest.TestCase):
    def test_opencodeRunMission_scopesContinueToWorktreeSessionAndSetsPwd(self):
        opencode = runner.AGENT_PLUGINS["opencode"]
        with tempfile.TemporaryDirectory() as tmpdir:
            db_path = os.path.join(tmpdir, "opencode.db")
            wt_dir = os.path.join(tmpdir, "worktree-1267")
            main_dir = os.path.join(tmpdir, "main-repo")
            other_wt = os.path.join(tmpdir, "worktree-other")
            os.makedirs(wt_dir, exist_ok=True)
            os.makedirs(main_dir, exist_ok=True)
            os.makedirs(other_wt, exist_ok=True)

            import sqlite3
            conn = sqlite3.connect(db_path)
            conn.execute(
                "CREATE TABLE session_v2 ("
                "id TEXT PRIMARY KEY, parent_id TEXT, directory TEXT, "
                "time_updated INTEGER, time_archived INTEGER)"
            )
            # Newer session in main_dir must NOT be selected when running in wt_dir
            conn.execute(
                "INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)",
                ("ses_main", None, os.path.abspath(main_dir), 200, None),
            )
            conn.execute(
                "INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)",
                ("ses_wt_child", "ses_wt", os.path.abspath(wt_dir), 190, None),
            )
            conn.execute(
                "INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)",
                ("ses_wt", None, os.path.abspath(wt_dir), 150, None),
            )
            conn.commit()
            conn.close()

            with patch.dict(os.environ, {"OPENCODE_DB_PATH": db_path, "PWD": main_dir}):
                with patch("subprocess.Popen") as mock_popen:
                    proc = MagicMock()
                    proc.communicate.return_value = ("ok", "")
                    proc.returncode = 0
                    mock_popen.return_value = proc

                    code, _, _ = opencode.run_mission("fix issue", wt_dir, is_continue=True, model="opencode/big-pickle")
                    self.assertEqual(code, 0)
                    called_args = mock_popen.call_args.args[0]
                    called_kwargs = mock_popen.call_args.kwargs
                    self.assertIn("--session", called_args)
                    self.assertEqual(called_args[called_args.index("--session") + 1], "ses_wt")
                    self.assertNotIn("--continue", called_args)
                    self.assertEqual(called_kwargs["cwd"], os.path.abspath(wt_dir))
                    self.assertEqual(called_kwargs["env"]["PWD"], os.path.abspath(wt_dir))

                    # When no session exists for other_wt, omit --continue/--session to start fresh in other_wt
                    mock_popen.reset_mock()
                    code, _, _ = opencode.run_mission("fix issue", other_wt, is_continue=True, model="opencode/big-pickle")
                    self.assertEqual(code, 0)
                    called_args = mock_popen.call_args.args[0]
                    self.assertNotIn("--continue", called_args)
                    self.assertNotIn("--session", called_args)
                    self.assertEqual(mock_popen.call_args.kwargs["env"]["PWD"], os.path.abspath(other_wt))

    def test_autoCommitWipCheckpoint_whenPreCommitHookRejects_fallsBackToNoVerify(self):
        with tempfile.TemporaryDirectory() as tmpdir:
            repo_dir = os.path.join(tmpdir, "repo")
            os.makedirs(repo_dir)
            subprocess.run(["git", "init", "-b", "main"], cwd=repo_dir, check=True, capture_output=True)
            subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=repo_dir, check=True)
            subprocess.run(["git", "config", "user.name", "Test"], cwd=repo_dir, check=True)
            hook_path = os.path.join(repo_dir, ".git", "hooks", "pre-commit")
            with open(hook_path, "w") as f:
                f.write("#!/usr/bin/env bash\nexit 1\n")
            os.chmod(hook_path, 0o755)

            with open(os.path.join(repo_dir, "file.txt"), "w") as f:
                f.write("wip content\n")

            runner._auto_commit_wip_checkpoint(repo_dir, 1267, "opencode")
            self.assertTrue(runner._worktree_is_clean(repo_dir))
            self.assertTrue(runner._latest_commit_is_wip(repo_dir))

    def test_opencodeRunMission_prunesFailedTrailingTurnsAndRetriesOriginalSessionModelOnContextOverflow(self):
        import json
        import sqlite3
        from plugins import opencode

        with tempfile.TemporaryDirectory() as tmpdir:
            db_path = os.path.join(tmpdir, "opencode.db")
            wt_dir = os.path.join(tmpdir, "worktree-1268")
            os.makedirs(wt_dir, exist_ok=True)

            conn = sqlite3.connect(db_path)
            conn.execute(
                "CREATE TABLE session_v2 ("
                "id TEXT PRIMARY KEY, parent_id TEXT, directory TEXT, "
                "time_updated INTEGER, time_archived INTEGER)"
            )
            conn.execute(
                "CREATE TABLE session_message ("
                "id TEXT PRIMARY KEY, session_id TEXT NOT NULL, type TEXT NOT NULL, "
                "seq INTEGER NOT NULL, time_created INTEGER NOT NULL, "
                "time_updated INTEGER NOT NULL, data TEXT NOT NULL)"
            )
            conn.execute(
                "INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)",
                ("ses_1268", None, os.path.abspath(wt_dir), 100, None),
            )
            conn.execute(
                "INSERT INTO session_message VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    "msg_10",
                    "ses_1268",
                    "assistant",
                    10,
                    100,
                    100,
                    json.dumps(
                        {
                            "finish": "stop",
                            "model": {"providerID": "opencode", "id": "nemotron-3-ultra-free"},
                        }
                    ),
                ),
            )
            conn.execute(
                "INSERT INTO session_message VALUES (?, ?, ?, ?, ?, ?, ?)",
                ("msg_10_idle", "ses_1268", "idle", 11, 100, 100, json.dumps({"outcome": "succeeded"})),
            )
            # Trailing failed retry rows (including an empty error assistant stub) that bloated context
            conn.execute(
                "INSERT INTO session_message VALUES (?, ?, ?, ?, ?, ?, ?)",
                ("msg_11", "ses_1268", "user", 12, 101, 101, json.dumps({"text": "Resume work"})),
            )
            conn.execute(
                "INSERT INTO session_message VALUES (?, ?, ?, ?, ?, ?, ?)",
                ("msg_12", "ses_1268", "compaction", 13, 102, 102, json.dumps({"status": "failed"})),
            )
            conn.execute(
                "INSERT INTO session_message VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    "msg_13",
                    "ses_1268",
                    "assistant",
                    14,
                    103,
                    103,
                    json.dumps(
                        {
                            "finish": "error",
                            "content": [],
                            "model": {"providerID": "opencode", "id": "big-pickle"},
                        }
                    ),
                ),
            )
            conn.commit()
            conn.close()

            with patch.dict(os.environ, {"OPENCODE_DB_PATH": db_path}):
                with patch("subprocess.Popen") as mock_popen:
                    proc_fail = MagicMock()
                    proc_fail.communicate.return_value = (
                        "",
                        "Error: Your request resulted in more tokens than this model's maximum context length allows.",
                    )
                    proc_fail.returncode = 1

                    proc_ok = MagicMock()
                    proc_ok.communicate.return_value = ("resumed with original model", "")
                    proc_ok.returncode = 0

                    mock_popen.side_effect = [proc_fail, proc_ok]

                    code, out, _ = opencode.run_mission(
                        "resume issue 1268",
                        wt_dir,
                        is_continue=True,
                        model="opencode/big-pickle",
                    )
                    self.assertEqual(code, 0)
                    self.assertEqual(out, "resumed with original model")
                    self.assertEqual(mock_popen.call_count, 2)

                    first_cmd = mock_popen.call_args_list[0].args[0]
                    second_cmd = mock_popen.call_args_list[1].args[0]
                    self.assertEqual(first_cmd[first_cmd.index("--session") + 1], "ses_1268")
                    self.assertEqual(first_cmd[first_cmd.index("--model") + 1], "opencode/big-pickle")
                    self.assertEqual(second_cmd[second_cmd.index("--session") + 1], "ses_1268")
                    self.assertEqual(second_cmd[second_cmd.index("--model") + 1], "opencode/nemotron-3-ultra-free")

                    conn = sqlite3.connect(db_path)
                    remaining = conn.execute(
                        "SELECT seq FROM session_message WHERE session_id = 'ses_1268' ORDER BY seq"
                    ).fetchall()
                    conn.close()
                    self.assertEqual(remaining, [(10,), (11,)])

    def test_opencodeRunMission_whenSessionModelAlsoOverflowsContext_archivesSessionAndStartsFresh(self):
        import json
        import sqlite3
        from plugins import opencode

        with tempfile.TemporaryDirectory() as tmpdir:
            db_path = os.path.join(tmpdir, "opencode.db")
            wt_dir = os.path.join(tmpdir, "worktree-1268")
            os.makedirs(wt_dir, exist_ok=True)

            conn = sqlite3.connect(db_path)
            conn.execute(
                "CREATE TABLE session_v2 ("
                "id TEXT PRIMARY KEY, parent_id TEXT, directory TEXT, "
                "time_updated INTEGER, time_archived INTEGER)"
            )
            conn.execute(
                "CREATE TABLE session_message ("
                "id TEXT PRIMARY KEY, session_id TEXT NOT NULL, type TEXT NOT NULL, "
                "seq INTEGER NOT NULL, time_created INTEGER NOT NULL, "
                "time_updated INTEGER NOT NULL, data TEXT NOT NULL)"
            )
            conn.execute(
                "INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)",
                ("ses_overflow", None, os.path.abspath(wt_dir), 100, None),
            )
            conn.execute(
                "INSERT INTO session_message VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    "msg_10",
                    "ses_overflow",
                    "assistant",
                    10,
                    100,
                    100,
                    json.dumps(
                        {
                            "finish": "stop",
                            "model": {"providerID": "opencode", "id": "big-pickle"},
                        }
                    ),
                ),
            )
            conn.commit()
            conn.close()

            with patch.dict(os.environ, {"OPENCODE_DB_PATH": db_path}):
                with patch("subprocess.Popen") as mock_popen:
                    proc_fail = MagicMock()
                    proc_fail.communicate.return_value = (
                        "",
                        "Error: Your request resulted in more tokens than this model's maximum context length allows.",
                    )
                    proc_fail.returncode = 1

                    proc_fresh = MagicMock()
                    proc_fresh.communicate.return_value = ("fresh session ok", "")
                    proc_fresh.returncode = 0

                    mock_popen.side_effect = [proc_fail, proc_fresh]

                    code, out, _ = opencode.run_mission(
                        "resume issue 1268",
                        wt_dir,
                        is_continue=True,
                        model="opencode/big-pickle",
                    )
                    self.assertEqual(code, 0)
                    self.assertEqual(out, "fresh session ok")
                    self.assertEqual(mock_popen.call_count, 2)

                    first_cmd = mock_popen.call_args_list[0].args[0]
                    second_cmd = mock_popen.call_args_list[1].args[0]
                    self.assertIn("--session", first_cmd)
                    self.assertNotIn("--session", second_cmd)

                    conn = sqlite3.connect(db_path)
                    archived = conn.execute(
                        "SELECT time_archived FROM session_v2 WHERE id = 'ses_overflow'"
                    ).fetchone()
                    conn.close()
                    self.assertIsNotNone(archived[0])

    def test_babysitActiveWorktrees_whenFirstCandidateFailsOnUnPrdWorktree_fallsBackToNextCandidate(self):
        with tempfile.TemporaryDirectory() as tmpdir:
            wt_dir = os.path.join(tmpdir, "issue-1268")
            os.makedirs(wt_dir, exist_ok=True)

            mock_plugin = MagicMock()
            called_models: list[str | None] = []

            def fake_run_mission(prompt, cwd, is_continue=False, model=None):
                called_models.append(model)
                if len(called_models) == 1:
                    return 1, "", "Error: Model unavailable"
                return 0, "done", ""

            mock_plugin.run_mission.side_effect = fake_run_mission

            def fake_run_cmd(cmd, cwd=None, timeout=60):
                if cmd[:2] == ["git", "branch"]:
                    return 0, "feat/ISSUE-1268-test\n", ""
                if cmd[:3] == ["gh", "pr", "view"]:
                    return 1, "", "no pull requests found"
                if cmd[:3] == ["gh", "pr", "list"]:
                    return 0, "[]", ""
                if cmd[:3] == ["gh", "issue", "view"]:
                    return 0, '{"state": "OPEN"}', ""
                if cmd[:3] == ["git", "rev-parse", "HEAD"]:
                    # Second candidate records a new commit after running so made_progress=True
                    return (0, "bbbbb\n", "") if len(called_models) >= 2 and fake_run_cmd.rev_calls >= 3 else (0, "aaaaa\n", "")
                if cmd[:3] == ["git", "status", "--porcelain"]:
                    return 0, "", ""
                return 0, "", ""

            fake_run_cmd.rev_calls = 0
            orig_fake_run_cmd = fake_run_cmd

            def counting_run_cmd(cmd, cwd=None, timeout=60):
                if cmd[:3] == ["git", "rev-parse", "HEAD"]:
                    orig_fake_run_cmd.rev_calls += 1
                return orig_fake_run_cmd(cmd, cwd=cwd, timeout=timeout)

            with (
                patch.object(runner, "ACTIVE_AGENT", "opencode"),
                patch.object(runner, "ACTIVE_MODEL", "opencode/big-pickle"),
                patch.object(
                    runner,
                    "AGENT_CANDIDATES",
                    [
                        ("opencode", "opencode/big-pickle"),
                        ("opencode", "opencode/nemotron-3-ultra-free"),
                    ],
                ),
                patch.object(runner, "WORKTREES_BASE", tmpdir),
                patch.dict(runner.AGENT_PLUGINS, {"opencode": mock_plugin}, clear=True),
                patch.object(runner, "run_cmd", side_effect=counting_run_cmd),
                patch.object(runner, "wait_for_tokens"),
                patch.object(runner, "_load_pr_state", return_value=None),
                patch.object(runner, "_fetch_issue_body", return_value=("Issue 1268", "body")),
                patch.object(runner, "_branch_has_commits", return_value=True),
                patch.object(runner, "_worktree_is_clean", return_value=True),
                patch.object(runner, "_latest_commit_is_wip", return_value=True),
                patch.object(runner, "_check_pr", return_value=("", "")),
            ):
                runner.babysit_active_worktrees()

            self.assertEqual(
                called_models,
                ["opencode/big-pickle", "opencode/nemotron-3-ultra-free"],
            )


if __name__ == "__main__":
    unittest.main()



