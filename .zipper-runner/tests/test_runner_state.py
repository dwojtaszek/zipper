"""Unit tests for runner.py state helpers (rerun budget, injection marker).

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests -t .
"""

import json
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

# STATE_DIR must be pinned before runner.py reads it at import time.
_STATE_DIR = tempfile.mkdtemp(prefix="zipper-runner-state-")
os.environ["STATE_DIR"] = _STATE_DIR

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402


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

    def tearDown(self):
        os.environ["PATH"] = self.orig_path
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
            ok, err = runner._run_coderabbit_pre_pr_check("/fake/path")
            self.assertFalse(ok)
            self.assertIn("not found", err)

    def test_whenCoderabbitZeroFindings_returnsTrue(self):
        output = 'echo \'{"type":"complete","status":"review_completed","findings":0}\''
        self._create_fake_cr(output, exit_code=0)
        ok, err = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertTrue(ok)
        self.assertEqual(err, "")

    def test_whenCoderabbitReviewSkipped_returnsFalse(self):
        output = 'echo \'{"type":"complete","status":"review_skipped","findings":0}\''
        self._create_fake_cr(output, exit_code=0)
        ok, err = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(ok)
        self.assertIn("review_skipped", err)

    def test_whenCoderabbitHasFindings_returnsFalse(self):
        output = (
            'echo \'{"type":"finding","ruleId":"bug","message":"bad code"}\'\n'
            'echo \'{"type":"complete","status":"review_completed","findings":1}\''
        )
        self._create_fake_cr(output, exit_code=0)
        ok, err = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(ok)
        self.assertIn("unresolved", err)

    def test_whenCoderabbitMissingCompleteRecord_returnsFalse(self):
        output = 'echo \'{"type":"review_context","reviewType":"committed"}\''
        self._create_fake_cr(output, exit_code=0)
        ok, err = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(ok)
        self.assertIn("completion record", err)

    def test_whenCoderabbitFails_returnsFalse(self):
        output = 'echo "auth failure" >&2'
        self._create_fake_cr(output, exit_code=1)
        ok, err = runner._run_coderabbit_pre_pr_check(self.temp_dir)
        self.assertFalse(ok)
        self.assertIn("failed (exit 1)", err)


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
        output = "[ WARNING ] 2 unresolved review threads on PR #42"
        with patch("runner.run_cmd", return_value=(1, output, "")) as mock_run_cmd:
            threads = runner._count_review_threads(42, review_output=None)
            self.assertEqual(threads, 2)
            mock_run_cmd.assert_called_once()
            args = mock_run_cmd.call_args[0][0]
            self.assertEqual(args, ["bash", "tests/wait-for-reviews.sh", "42"])

    def test_whenNoUnresolvedThreadsInOutput_returnsMinusOne(self):
        output = "[ OK ] No unresolved review threads on PR #42"
        threads = runner._count_review_threads(42, review_output=output)
        self.assertEqual(threads, -1)


if __name__ == "__main__":
    unittest.main()

