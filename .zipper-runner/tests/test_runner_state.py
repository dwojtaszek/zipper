"""Unit tests for runner.py state helpers (rerun budget, injection marker).

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests -t .
"""

import json
import os
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


class InjectionMarkerTests(unittest.TestCase):
    def test_marker_path_UsesStateDir(self):
        self.assertEqual(
            runner._injection_marker_path(42),
            os.path.join(_STATE_DIR, "issue-42-injection.json"),
        )


if __name__ == "__main__":
    unittest.main()
