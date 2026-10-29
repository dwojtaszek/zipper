#!/usr/bin/env python3
"""An absent required check must be re-armed, not "fixed" by an agent.

Branch protection on `main` requires exactly one status context (`ci-gate`). When
that context is missing from a PR head's rollup, GitHub treats it as never
reported and refuses the merge indefinitely -- no diff can change that, because
the problem is a status that was never posted, not code.

Observed on #1340 (25 Oct): a push to the branch produced no `pull_request`
workflow run at all (while `pull_request_target` runs did fire), so `ci-gate` was
never posted. The PR sat `BLOCKED` with every reported check green and zero
unresolved review threads, and the runner responded by dispatching "Fix the
blocker and push updates" missions -- 40 minutes each, holding the only worktree
slot, editing code that was already correct.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

RUNNER_BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNNER_BASE))

import runner  # noqa: E402


def _git(cwd, *args, check=True):
    result = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True)
    if check and result.returncode != 0:
        raise AssertionError(f"git {' '.join(args)} failed: {result.stderr}")
    return result.stdout.strip()


class RequiredCheckAbsentTests(unittest.TestCase):
    """The rollup shapes that decide whether a merge is even possible."""

    def test_rollup_withTheRequiredContext_shouldNotBeReportedAbsent(self):
        rollup = [
            {"__typename": "CheckRun", "name": "ci-gate", "status": "COMPLETED", "conclusion": "SUCCESS"},
            {"__typename": "StatusContext", "context": "CodeRabbit", "state": "SUCCESS"},
        ]
        self.assertFalse(runner._required_check_absent(rollup))

    def test_rollup_withoutTheRequiredContext_shouldBeReportedAbsent(self):
        """The exact #1340 shape: every reported check green, ci-gate never posted."""
        rollup = [
            {"__typename": "CheckRun", "name": "Analyze (python)", "status": "COMPLETED", "conclusion": "SUCCESS"},
            {"__typename": "CheckRun", "name": "SonarCloud Code Analysis", "status": "COMPLETED", "conclusion": "SUCCESS"},
            {"__typename": "StatusContext", "context": "CodeRabbit", "state": "SUCCESS"},
        ]
        self.assertTrue(runner._required_check_absent(rollup))

    def test_requiredContextAsAStatusContext_shouldBeFound(self):
        """The name may arrive as a commit status rather than a check run."""
        rollup = [{"__typename": "StatusContext", "context": "ci-gate", "state": "SUCCESS"}]
        self.assertFalse(runner._required_check_absent(rollup))

    def test_emptyOrMalformedRollup_shouldBeReportedAbsent(self):
        """Fail toward the safe reading: no evidence the context was posted."""
        for rollup in ([], None, [None], ["nonsense"]):
            with self.subTest(rollup=rollup):
                self.assertTrue(runner._required_check_absent(rollup))

    def test_aFailedRequiredContext_isNotAbsent(self):
        """A failing check is a code problem and must keep the repair path."""
        rollup = [{"__typename": "CheckRun", "name": "ci-gate", "status": "COMPLETED", "conclusion": "FAILURE"}]
        self.assertFalse(runner._required_check_absent(rollup),
                         "a failed check is fixable by a diff; only an absent one is not")

    def test_anExpectedRequiredContext_shouldBeReportedAbsent(self):
        """`EXPECTED` means branch protection expects it and no run started.

        That is the same defect as an entry that is missing entirely, so it must
        not read as "present" -- otherwise the PR waits forever for a run that was
        never created.
        """
        rollup = [{"__typename": "StatusContext", "context": "ci-gate", "state": "EXPECTED"}]
        self.assertTrue(runner._required_check_absent(rollup))

    def test_aPendingRequiredContext_isNotAbsent(self):
        """A running check exists, so re-arming would only add another run."""
        rollup = [{"__typename": "StatusContext", "context": "ci-gate", "state": "PENDING"}]
        self.assertFalse(runner._required_check_absent(rollup))

    def test_theGuardIsReachableForTheObservedShape(self):
        """The #1340 shape must satisfy the runner's own CI policy.

        If `_ci_status_from_rollup` ever stopped calling this shape SUCCESS, the
        guard would silently become dead code and the merge path would never reach
        it -- a source-text assertion would not catch that.
        """
        rollup = [
            {"__typename": "CheckRun", "name": "Analyze (python)", "status": "COMPLETED", "conclusion": "SUCCESS"},
            {"__typename": "StatusContext", "context": "CodeRabbit", "state": "SUCCESS"},
        ]
        self.assertEqual(runner._ci_status_from_rollup(rollup)[0], "SUCCESS")
        self.assertTrue(runner._required_check_absent(rollup))


class RearmCountTests(unittest.TestCase):
    """The budget is per head, so a new head gets a fresh allowance."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        patcher = patch.object(runner, "STATE_DIR", self._tmp.name)
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_rearmCount_shouldStartAtZeroAndIncrement(self):
        self.assertEqual(runner._get_rearm_count(7, "a" * 40), 0)
        self.assertEqual(runner._bump_rearm_count(7, "a" * 40), 1)
        self.assertEqual(runner._get_rearm_count(7, "a" * 40), 1)
        self.assertEqual(runner._bump_rearm_count(7, "a" * 40), 2)

    def test_rearmCount_shouldResetWhenTheHeadMoves(self):
        runner._bump_rearm_count(7, "a" * 40)
        self.assertEqual(runner._get_rearm_count(7, "b" * 40), 0,
                         "a pushed fix is a new head and deserves its own budget")

    def test_clearRearmCount_shouldRemoveTheRecord(self):
        runner._bump_rearm_count(7, "a" * 40)
        runner._clear_rearm_count(7)
        self.assertEqual(runner._get_rearm_count(7, "a" * 40), 0)

    def test_clearRearmCount_whenNoRecord_shouldNotRaise(self):
        runner._clear_rearm_count(999)

    def test_unreadableRecord_shouldNotStrandTheBudget(self):
        path = runner._rearm_count_path(7)
        Path(path).write_text("{not json")
        self.assertEqual(runner._get_rearm_count(7, "a" * 40), 0)


class RearmRefusalTests(unittest.TestCase):
    """Every path that must refuse to push. A push is a publication."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        patcher = patch.object(runner, "STATE_DIR", self._tmp.name)
        patcher.start()
        self.addCleanup(patcher.stop)
        # DRY_RUN is derived from argv at import; pin it so these tests assert
        # real behaviour regardless of how the module was loaded.
        dry = patch.object(runner, "DRY_RUN", False)
        dry.start()
        self.addCleanup(dry.stop)
        self.head = "a" * 40
        self.worktree = self._tmp.name

    def _call(self, authorized=True, **kwargs):
        """Invoke the re-arm with git stubbed, so only guards are exercised."""
        with patch.object(runner, "_authorized_pr", return_value=({"number": 1} if authorized else None)), \
                patch.object(runner, "run_cmd", return_value=(0, "fix/x\n", "")) as run_cmd:
            result = runner._rearm_required_check(1, "fix/x", self.head, self.worktree,
                                                  issue_number=1, **kwargs)
        return result, run_cmd

    def test_unauthorizedPr_shouldNotPush(self):
        (ok, detail), run_cmd = self._call(authorized=False)
        self.assertFalse(ok)
        self.assertIn("not authorized", detail)
        run_cmd.assert_not_called()

    def test_missingWorktree_shouldNotPush(self):
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}), \
                patch.object(runner, "run_cmd") as rc:
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, "/nonexistent/worktree")
        self.assertFalse(ok)
        self.assertIn("no worktree", detail)
        rc.assert_not_called()

    def test_unsetWorktree_shouldNotPush(self):
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}), \
                patch.object(runner, "run_cmd") as rc:
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, "")
        self.assertFalse(ok)
        self.assertIn("no worktree", detail)
        rc.assert_not_called()

    def test_dryRun_shouldNotPush(self):
        with patch.object(runner, "DRY_RUN", True), \
                patch.object(runner, "run_cmd") as rc:
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, self.worktree)
        self.assertFalse(ok)
        self.assertEqual(detail, "dry run")
        rc.assert_not_called()

    def test_exhaustedBudget_shouldNotPush(self):
        for _ in range(runner.REARM_MAX_ATTEMPTS):
            runner._bump_rearm_count(1, self.head)
        (ok, detail), run_cmd = self._call()
        self.assertFalse(ok)
        self.assertIn("budget exhausted", detail)
        run_cmd.assert_not_called()

    def test_worktreeOnAnotherBranch_shouldNotPush(self):
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}), \
                patch.object(runner, "run_cmd", return_value=(0, "main\n", "")) as rc:
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, self.worktree)
        self.assertFalse(ok)
        self.assertIn("not on 'fix/x'", detail)
        # Only the branch check ran; no commit or push was attempted.
        self.assertEqual(rc.call_count, 1)


class RearmAgainstRealRemoteTests(unittest.TestCase):
    """The re-arm must push a new head whose tree is byte-identical.

    This is the safety property the whole mechanism rests on: the new head
    re-runs every gate against exactly the content that was already reviewed, so
    re-arming cannot smuggle an unreviewed change past branch protection.
    """

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        base = self._tmp.name
        self.origin = os.path.join(base, "origin.git")
        self.work = os.path.join(base, "work")
        _git(base, "init", "--bare", "-q", self.origin)
        _git(base, "clone", "-q", self.origin, self.work)
        for key, value in (("user.email", "runner@example.invalid"), ("user.name", "Runner Test")):
            _git(self.work, "config", key, value)
        (Path(self.work) / "seed.txt").write_text("seed\n")
        _git(self.work, "add", "-A")
        _git(self.work, "commit", "-q", "-m", "seed")
        _git(self.work, "branch", "-M", "main")
        _git(self.work, "push", "-q", "-u", "origin", "main")
        _git(self.work, "checkout", "-q", "-b", "fix/x")
        (Path(self.work) / "change.txt").write_text("reviewed work\n")
        _git(self.work, "add", "-A")
        _git(self.work, "commit", "-q", "-m", "fix: reviewed work")
        _git(self.work, "push", "-q", "-u", "origin", "fix/x")

        patcher = patch.object(runner, "STATE_DIR", os.path.join(base, "state"))
        patcher.start()
        self.addCleanup(patcher.stop)
        dry = patch.object(runner, "DRY_RUN", False)
        dry.start()
        self.addCleanup(dry.stop)
        os.makedirs(os.path.join(base, "state"), exist_ok=True)
        self.head = _git(self.work, "rev-parse", "HEAD")
        self.tree = _git(self.work, "rev-parse", "HEAD^{tree}")

    def test_rearm_shouldPushANewHeadWithTheSameTree(self):
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, self.work)
        self.assertTrue(ok, detail)

        new_head = _git(self.work, "rev-parse", "HEAD")
        self.assertNotEqual(new_head, self.head, "the head must move to fire a new event")
        self.assertEqual(_git(self.work, "rev-parse", "HEAD^{tree}"), self.tree,
                         "the tree must be identical: same content, so the gates re-verify the same code")
        remote_head = _git(self.work, "rev-parse", "origin/fix/x")
        self.assertEqual(remote_head, new_head, "the re-armed head must reach the remote")

    def test_rearm_shouldSpendOneAttemptAgainstTheNewHead(self):
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            runner._rearm_required_check(1, "fix/x", self.head, self.work)
        new_head = _git(self.work, "rev-parse", "HEAD")
        self.assertEqual(runner._get_rearm_count(1, new_head), 1,
                         "the spent attempt must be keyed to the head the push created")
        self.assertEqual(runner._get_rearm_count(1, self.head), 0,
                         "the pre-push head is no longer the PR's head")

    def test_rearmWithStagedWork_shouldNotPublishIt(self):
        """A failed WIP checkpoint leaves the worktree staged; a re-arm must not ship it.

        `git commit --allow-empty` commits the index, so it would have published
        unreviewed agent work under a "re-arm ci-gate" message -- and the new head
        would carry a different tree from the one that was reviewed.
        """
        (Path(self.work) / "agent-leftovers.txt").write_text("half-finished work\n")
        _git(self.work, "add", "-A")
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, self.work)
        self.assertTrue(ok, detail)

        new_head = _git(self.work, "rev-parse", "HEAD")
        self.assertNotEqual(new_head, self.head)
        self.assertEqual(_git(self.work, "rev-parse", "HEAD^{tree}"), self.tree,
                         "the re-armed head must carry the reviewed tree, not the staged work")
        self.assertEqual(_git(self.work, "rev-parse", "origin/fix/x"), new_head)
        # The staged work must still be staged, not committed and not discarded.
        staged = _git(self.work, "diff", "--cached", "--name-only")
        self.assertIn("agent-leftovers.txt", staged,
                      "the re-arm must leave the agent's staged work exactly as it found it")

    def test_rearmWithStagedWork_shouldNotAdvanceTheCommitToTheStagedTree(self):
        """The re-arm commit's tree must equal its parent's, always."""
        (Path(self.work) / "leftover.txt").write_text("staged\n")
        _git(self.work, "add", "-A")
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            runner._rearm_required_check(1, "fix/x", self.head, self.work)
        parent_tree = _git(self.work, "rev-parse", "HEAD~1^{tree}")
        self.assertEqual(_git(self.work, "rev-parse", "HEAD^{tree}"), parent_tree)

    def test_failedPush_shouldSpendTheAttemptAgainstTheUnchangedHead(self):
        """A push that keeps being rejected must back off, not retry forever."""
        # Point origin at a path that cannot be pushed to.
        _git(self.work, "remote", "set-url", "origin", os.path.join(self._tmp.name, "gone.git"))
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, self.work)
        self.assertFalse(ok)
        self.assertIn("push failed", detail)
        self.assertEqual(_git(self.work, "rev-parse", "HEAD"), self.head,
                         "a failed push must leave the branch where it was")
        self.assertEqual(runner._get_rearm_count(1, self.head), 1,
                         "the failed attempt must be spent against the unchanged head")

    def test_failedPushLoop_shouldStopAtTheBudget(self):
        _git(self.work, "remote", "set-url", "origin", os.path.join(self._tmp.name, "gone.git"))
        push_attempts = 0
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            for _ in range(runner.REARM_MAX_ATTEMPTS + 1):
                ok, detail = runner._rearm_required_check(1, "fix/x", self.head, self.work)
                self.assertFalse(ok, "the push cannot succeed against a missing remote")
                if "budget exhausted" in detail:
                    break
                self.assertIn("push failed", detail)
                push_attempts += 1
        self.assertEqual(push_attempts, runner.REARM_MAX_ATTEMPTS,
                         "a persistently failing push must be bounded too")

    def test_worktreeAtADifferentHead_shouldNotPush(self):
        """The byte-identical guarantee must not rest on a caller invariant."""
        (Path(self.work) / "moved.txt").write_text("someone else pushed\n")
        _git(self.work, "add", "-A")
        _git(self.work, "commit", "-q", "-m", "fix: external move")
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            ok, detail = runner._rearm_required_check(1, "fix/x", self.head, self.work)
        self.assertFalse(ok)
        self.assertIn("not the authorized head", detail)
        self.assertEqual(runner._get_rearm_count(1, self.head), 0,
                         "a refused re-arm must not spend budget")

    def test_rearmLoop_shouldStopAtTheBudget(self):
        """The production loop: each cycle re-reads the head the last push made.

        Keying the count to the pre-push head would give every cycle a fresh
        budget, turning the bound into an endless empty-commit loop.
        """
        pushed_heads = []
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            for _ in range(runner.REARM_MAX_ATTEMPTS + 1):
                current = _git(self.work, "rev-parse", "HEAD")
                ok, detail = runner._rearm_required_check(1, "fix/x", current, self.work)
                if ok:
                    pushed_heads.append(_git(self.work, "rev-parse", "HEAD"))
                else:
                    self.assertIn("budget exhausted", detail)
                    break
        self.assertEqual(len(pushed_heads), runner.REARM_MAX_ATTEMPTS,
                         "the budget must stop the loop after REARM_MAX_ATTEMPTS pushes")
        self.assertEqual(_git(self.work, "rev-parse", "HEAD"), pushed_heads[-1],
                         "the refused cycle must not have committed anything")

    def test_externalPushMovingTheHead_shouldResetTheBudget(self):
        with patch.object(runner, "_authorized_pr", return_value={"number": 1}):
            for _ in range(runner.REARM_MAX_ATTEMPTS):
                runner._rearm_required_check(1, "fix/x", _git(self.work, "rev-parse", "HEAD"), self.work)
        exhausted = _git(self.work, "rev-parse", "HEAD")
        self.assertEqual(runner._get_rearm_count(1, exhausted), runner.REARM_MAX_ATTEMPTS)

        # Someone else pushes real work: a new head is a new situation, so the
        # budget starts over rather than refusing a PR that changed underneath us.
        (Path(self.work) / "external.txt").write_text("external\n")
        _git(self.work, "add", "-A")
        _git(self.work, "commit", "-q", "-m", "fix: external change")
        self.assertEqual(runner._get_rearm_count(1, _git(self.work, "rev-parse", "HEAD")), 0)


class RearmAuthorizationTests(unittest.TestCase):
    """A re-arm pushes, so it must authorize against the PR's own record."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        patcher = patch.object(runner, "STATE_DIR", self._tmp.name)
        patcher.start()
        self.addCleanup(patcher.stop)
        dry = patch.object(runner, "DRY_RUN", False)
        dry.start()
        self.addCleanup(dry.stop)

    def test_rearm_shouldAuthorizeAgainstTheIssueRecord(self):
        seen = []

        def fake_authorized(pr_number, issue_number=None, expected_head=None):
            seen.append((pr_number, issue_number, expected_head))
            return {"number": pr_number}

        with patch.object(runner, "_authorized_pr", side_effect=fake_authorized), \
                patch.object(runner, "run_cmd", return_value=(0, "fix/x\n", "")):
            runner._rearm_required_check(7, "fix/x", "b" * 40, self._tmp.name, issue_number=1347)
        self.assertEqual(seen, [(7, 1347, "b" * 40)],
                         "the issue number is what lets _authorized_pr consult the runner's record")

    def test_rearmWithoutIssueNumber_shouldNotAuthorize(self):
        """A normal runner PR has no maintainer approval to fall back on."""
        with patch.object(runner, "run_cmd", return_value=(0, "{}", "")) as rc:
            with patch.object(runner, "_authorized_pr", wraps=runner._authorized_pr) as authorized:
                ok, detail = runner._rearm_required_check(7, "fix/x", "b" * 40, self._tmp.name)
        self.assertFalse(ok)
        self.assertIn("not authorized", detail)
        self.assertIsNone(authorized.call_args.args[1],
                          "guard: without an issue number the record path cannot match")
        git_calls = [c for c in rc.call_args_list if c.args and c.args[0][:1] == ["git"]]
        self.assertEqual(git_calls, [], "an unauthorized re-arm must never touch git")


class MergeRollupTests(unittest.TestCase):
    """The rollup handed to the guard must be the one the merge decision used."""

    GREENTREE = [{"__typename": "StatusContext", "context": "CodeRabbit", "state": "SUCCESS"}]

    def _merge(self, head_ref_oid, rollup, authorized_head="a" * 40):
        with patch.object(runner, "_is_coderabbit_gate_failed", return_value=False), \
                patch.object(runner, "_authorized_pr",
                             return_value={"number": 1, "head": {"sha": authorized_head}}) as authorized, \
                patch.object(runner, "run_cmd", return_value=(
                        0, json.dumps({"headRefOid": head_ref_oid, "statusCheckRollup": rollup}), "")):
            result = runner._merge_authorized_pr(1, 2, "a" * 40)
        return result, authorized

    def test_mergeAuthorizedPr_authorizedPayloadHeadPastTheReviewedOne_shouldRefuseWithoutRollup(self):
        """Authorization is bound to the PR, so the reviewed head is compared here."""
        (code, _, err, rollup), _ = self._merge("a" * 40, self.GREENTREE, authorized_head="c" * 40)
        self.assertNotEqual(code, 0)
        self.assertEqual(err, runner.REVIEWED_HEAD_MOVED)
        self.assertIsNone(rollup,
                          "a head the reviews never covered must not be judged as if it had been")

    def test_mergeAuthorizedPr_shouldAskForAuthorizationWithoutPinningTheReviewedHead(self):
        """Pinning it made a moved head indistinguishable from a lost provenance.

        PR #1380 was green, recorded and maintainer-authored, and its head never
        moved; the pin turned that into a silent refusal the caller answered with a
        repair mission.
        """
        _, authorized = self._merge("a" * 40, self.GREENTREE)
        authorized.assert_called_once_with(1, 2)

    def test_movedHead_shouldYieldNoRollup(self):
        """A moved head must be re-evaluated, not re-armed."""
        (code, _, _, rollup), _ = self._merge("b" * 40, self.GREENTREE)
        self.assertNotEqual(code, 0)
        self.assertIsNone(rollup,
                          "the rollup describes a different commit, so it must not be "
                          "used to decide a push")

    def test_unverifiableRollup_shouldYieldNoRollup(self):
        with patch.object(runner, "_is_coderabbit_gate_failed", return_value=False), \
                patch.object(runner, "_authorized_pr",
                             return_value={"number": 1, "head": {"sha": "a" * 40}}), \
                patch.object(runner, "run_cmd", return_value=(0, "{not json", "")):
            code, _, _, rollup = runner._merge_authorized_pr(1, 2, "a" * 40)
        self.assertNotEqual(code, 0)
        self.assertIsNone(rollup,
                          "an unparseable snapshot is not evidence of a missing context")

    def test_failedCheck_shouldYieldTheRollupItJudged(self):
        rollup = [{"__typename": "CheckRun", "name": "ci / test",
                   "status": "COMPLETED", "conclusion": "FAILURE"}] + self.GREENTREE
        (code, _, _, fresh), _ = self._merge("a" * 40, rollup)
        self.assertNotEqual(code, 0)
        self.assertEqual(fresh, rollup, "the guard must see the snapshot CI was judged on")

    def test_unauthorizedPr_shouldYieldNoRollup(self):
        with patch.object(runner, "_is_coderabbit_gate_failed", return_value=False), \
                patch.object(runner, "_authorized_pr", return_value=None), \
                patch.object(runner, "run_cmd") as rc:
            code, _, _, rollup = runner._merge_authorized_pr(1, 2, "a" * 40)
        self.assertNotEqual(code, 0)
        self.assertIsNone(rollup)
        rc.assert_not_called()

    def test_mergeRefusedAfterTheSnapshotTookAMovedHead_shouldYieldNoRollup(self):
        """`--match-head-commit` refusing is the head moving, whatever the snapshot says.

        The snapshot was taken seconds before the merge, so the refusal still
        carries a current rollup. Reporting it as an ordinary branch-protection
        block sends a mission to a PR whose only signal is "re-evaluate the new
        head".
        """
        reads = {"snapshot": 0}

        def fake_run_cmd(args, cwd=None, timeout=None, env=None):
            if args[:3] == ["gh", "pr", "merge"]:
                return 1, "", "X Pull request is not mergeable: the head branch was modified."
            if args[:3] == ["gh", "pr", "view"] and "headRefOid" in args:
                reads["snapshot"] += 1
                return 0, json.dumps({"headRefOid": "b" * 40}), ""
            if args[:3] == ["gh", "pr", "view"]:
                return 0, json.dumps({"headRefOid": "a" * 40,
                                      "statusCheckRollup": self.GREENTREE}), ""
            raise AssertionError(args)

        with patch.object(runner, "_is_coderabbit_gate_failed", return_value=False), \
                patch.object(runner, "_authorized_pr",
                             return_value={"number": 1, "head": {"sha": "a" * 40}}), \
                patch.object(runner, "run_cmd", side_effect=fake_run_cmd):
            code, _, err, rollup = runner._merge_authorized_pr(1, 2, "a" * 40)

        self.assertNotEqual(code, 0)
        self.assertEqual(err, runner.REVIEWED_HEAD_MOVED)
        self.assertEqual(reads["snapshot"], 1, "the move must be read back from the PR")
        self.assertIsNone(rollup, "the next cycle re-gates the new head; nothing to repair")


class WipCheckpointHookTests(unittest.TestCase):
    """Runner-internal WIP checkpoints must not pay the full pre-commit suite.

    The hook short-circuits to DocsDriftTests only for docs changes; a
    `.zipper-runner/**` change ran `dotnet format` plus 3,400 tests, which in a
    worktree without build output means building first. Measured over two minutes,
    on the agent-dispatch critical path.
    """

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        dry = patch.object(runner, "DRY_RUN", False)
        dry.start()
        self.addCleanup(dry.stop)
        self.repo = self._tmp.name
        _git(self.repo, "init", "-q", "-b", "main")
        _git(self.repo, "config", "user.email", "runner@example.invalid")
        _git(self.repo, "config", "user.name", "Runner Test")
        (Path(self.repo) / "seed.txt").write_text("seed\n")
        _git(self.repo, "add", "-A")
        _git(self.repo, "commit", "-q", "-m", "seed")

    def _touch(self, relative: str):
        path = Path(self.repo) / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("change\n")

    def test_runnerInternalChangesOnly_shouldSkipHooks(self):
        self._touch(".zipper-runner/plugins/example.py")
        self.assertTrue(runner._wip_checkpoint_should_skip_hooks(self.repo))

    def test_productChange_shouldNotSkipHooks(self):
        self._touch("src/Program.cs")
        self.assertFalse(runner._wip_checkpoint_should_skip_hooks(self.repo),
                         "a product change must still face the repo gates")

    def test_mixedChange_shouldNotSkipHooks(self):
        self._touch(".zipper-runner/plugins/example.py")
        self._touch("src/Program.cs")
        self.assertFalse(runner._wip_checkpoint_should_skip_hooks(self.repo),
                         "one product file is enough to keep the hook")

    def test_cleanWorktree_shouldNotSkipHooks(self):
        self.assertFalse(runner._wip_checkpoint_should_skip_hooks(self.repo))

    def test_renameIntoTheRunner_shouldSkipHooks(self):
        self._touch(".zipper-runner/plugins/notes.md")
        _git(self.repo, "add", "-A")
        _git(self.repo, "commit", "-q", "-m", "add notes")
        _git(self.repo, "mv", ".zipper-runner/plugins/notes.md", ".zipper-runner/notes.md")
        self.assertTrue(runner._wip_checkpoint_should_skip_hooks(self.repo))

    def test_renameOutOfTheProductTree_shouldNotSkipHooks(self):
        """Moving a product file into .zipper-runner/ still changes product code."""
        self._touch("src/Program.cs")
        _git(self.repo, "add", "-A")
        _git(self.repo, "commit", "-q", "-m", "add Program")
        os.makedirs(os.path.join(self.repo, ".zipper-runner"), exist_ok=True)
        _git(self.repo, "mv", "src/Program.cs", ".zipper-runner/Program.cs")
        self.assertFalse(runner._wip_checkpoint_should_skip_hooks(self.repo),
                         "both sides of a rename must be runner-internal")

    def test_prefixLookalikeDirectory_shouldNotSkipHooks(self):
        self._touch(".zipper-runnerX/example.py")
        self.assertFalse(runner._wip_checkpoint_should_skip_hooks(self.repo))

    def test_checkpoint_shouldActuallyCommitRunnerInternalWork(self):
        self._touch(".zipper-runner/plugins/example.py")
        runner._auto_commit_wip_checkpoint(self.repo, 1, "stub")
        self.assertTrue(runner._worktree_is_clean(self.repo),
                        "the checkpoint must have committed the work")
        self.assertTrue(runner._latest_commit_is_wip(self.repo))

    def _install_hook(self):
        """A pre-commit hook that records that it ran and then rejects the commit."""
        hook = Path(self.repo) / ".git" / "hooks" / "pre-commit"
        hook.write_text(f'#!/bin/sh\necho ran >> "{self.repo}/hook-ran"\nexit 1\n')
        hook.chmod(0o755)

    def test_checkpoint_runnerInternal_shouldNotInvokeTheHook(self):
        self._install_hook()
        self._touch(".zipper-runner/plugins/example.py")
        runner._auto_commit_wip_checkpoint(self.repo, 1, "stub")
        self.assertFalse(os.path.exists(os.path.join(self.repo, "hook-ran")),
                         "a runner-internal checkpoint must not run the pre-commit hook")
        self.assertTrue(runner._worktree_is_clean(self.repo))

    def test_checkpoint_productChange_shouldInvokeTheHook(self):
        self._install_hook()
        self._touch("src/Program.cs")
        runner._auto_commit_wip_checkpoint(self.repo, 1, "stub")
        self.assertTrue(os.path.exists(os.path.join(self.repo, "hook-ran")),
                        "a product change must still face the pre-commit hook")


class MergePathWiringTests(unittest.TestCase):
    """The guard must run before any repair mission is dispatched.

    `main()` builds the babysit prompt inline, so -- like the intake-prompt tests
    in test_agy_plugin -- the invariant is pinned on the source: dropping the
    absent-check branch would send an agent to fix a blocker no diff can fix.
    """

    def setUp(self):
        self.source = (RUNNER_BASE / "runner.py").read_text(encoding="utf-8")

    def test_mergeFailurePath_shouldConsultTheAbsentCheckGuard(self):
        self.assertIn("if _handle_absent_required_check(\n", self.source)

    def test_absentCheck_shouldBeHandledBeforeTheRepairPrompt(self):
        guard = self.source.index("_handle_absent_required_check(\n")
        repair = self.source.index("Fix the blocker and push updates.")
        self.assertLess(guard, repair,
                        "the absent-check branch must come before the prompt that "
                        "dispatches a mission to fix it")

    def test_hangingPendingPath_shouldConsultTheAbsentCheckGuard(self):
        """A push that created no run at all leaves an empty rollup -> PENDING.

        Without the guard here, that shape waits two hours and then sends an agent
        to investigate a workflow run that was never created.
        """
        pending = self.source.index('elif ci_status == "PENDING":')
        hanging = self.source.index("if is_hanging:", pending)
        guard = self.source.index("_handle_absent_required_check(\n", hanging)
        prompt = self.source.index("CI checks have been pending for over 2 hours", hanging)
        self.assertLess(guard, prompt,
                        "the absent-check guard must precede the hanging-CI mission")
        self.assertIn("empty_is_evidence=True", self.source[guard:guard + 260],
                      "two hours with no checks reported is itself the evidence that "
                      "no run was created")

    def test_rearm_shouldReVerifyAuthorizationBeforePushing(self):
        body = self.source[self.source.index("def _rearm_required_check("):]
        body = body[:body.index("\ndef ")]
        self.assertIn("_authorized_pr(pr_number, issue_number, head_sha)", body,
                      "a push is a publication and must be re-authorized against the "
                      "PR's own record, which needs the issue number")

    def test_rearm_shouldBuildTheCommitFromTheTreeNotTheIndex(self):
        body = self.source[self.source.index("def _rearm_required_check("):]
        body = body[:body.index("\ndef ")]
        self.assertIn('"commit-tree", tree', body,
                      "taking the tree from HEAD makes the byte-identical guarantee "
                      "structural; `git commit --allow-empty` would commit the index")
        self.assertNotIn('"--allow-empty"', body)

    def test_rearmCallSite_shouldPassTheIssueNumber(self):
        """Authorization silently degrades to the approval path without it."""
        helper = self.source[self.source.index("def _handle_absent_required_check("):]
        helper = helper[:helper.index("\ndef ")]
        self.assertIn("issue_number=issue_number", helper,
                      "the merge path already holds the issue number; dropping it "
                      "leaves only maintainer approval, which GitHub forbids on "
                      "your own PR")

    def test_absentCheckGuardCallSites_shouldPassTheIssueNumber(self):
        """Every caller of the guard must forward the issue number."""
        calls, idx = [], 0
        while True:
            idx = self.source.find("_handle_absent_required_check(\n", idx)
            if idx == -1:
                break
            calls.append(self.source[idx:idx + 260])
            idx += 1
        self.assertEqual(len(calls), 2,
                         "both the merge-failure and hanging-PENDING paths must guard")
        for call in calls:
            self.assertIn("issue_number", call,
                          f"each call site must forward the issue number:\n{call}")

    def test_mergeFailureGuard_shouldUseTheFreshRollup(self):
        """The loop's rollup can be 25 minutes stale by merge time.

        `pr_data` is fetched before the robot-review gate, which can block for
        minutes, so judging an irreversible push from it could mistake a moved
        head or a newly-red check for a missing context.
        """
        repair = self.source.index("Fix the blocker and push updates.")
        call = self.source.rindex("_handle_absent_required_check(\n", 0, repair)
        self.assertIn("merge_rollup", self.source[call:call + 260],
                      "the guard must judge the snapshot the merge itself was judged "
                      "against, not the caller's earlier view")

    def test_budget_shouldBeKeyedToThePostPushHead(self):
        body = self.source[self.source.index("def _rearm_required_check("):]
        body = body[:body.index("\ndef ")]
        self.assertIn("base_count=count", body,
                      "the push moves the head, so the spent attempt must carry the "
                      "running total forward or every cycle gets a fresh budget")


class AbsentCheckHandlerTests(unittest.TestCase):
    """The shared handler: re-arm when absent, defer when present."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        patcher = patch.object(runner, "STATE_DIR", self._tmp.name)
        patcher.start()
        self.addCleanup(patcher.stop)
        dry = patch.object(runner, "DRY_RUN", False)
        dry.start()
        self.addCleanup(dry.stop)

    def _call(self, rollup, rearmed, **kwargs):
        with patch.object(runner, "_rearm_required_check", return_value=(rearmed, "why")) as rearm, \
                patch.object(runner, "_should_send_rate_limited", return_value=False):
            handled = runner._handle_absent_required_check(
                1, "fix/x", "a" * 40, self._tmp.name, rollup, **kwargs)
        return handled, rearm

    # The #1340 shape: every reported check green, ci-gate never posted.
    ABSENT = [{"__typename": "StatusContext", "context": "CodeRabbit", "state": "SUCCESS"}]

    def test_presentRequiredCheck_shouldDeferToTheCaller(self):
        rollup = [{"__typename": "CheckRun", "name": runner.REQUIRED_STATUS_CONTEXT,
                   "status": "COMPLETED", "conclusion": "FAILURE"}]
        handled, rearm = self._call(rollup, rearmed=False)
        self.assertFalse(handled,
                         "a FAILED required check is a real code problem and must keep "
                         "the repair path")
        rearm.assert_not_called()

    def test_absentRequiredCheck_shouldReArm(self):
        handled, rearm = self._call(self.ABSENT, rearmed=True)
        self.assertTrue(handled, "the caller must skip this PR once re-armed")
        rearm.assert_called_once()

    def test_absentRequiredCheck_whenRearmRefused_shouldStillBeHandled(self):
        handled, rearm = self._call(self.ABSENT, rearmed=False)
        self.assertTrue(handled,
                        "an unblockable PR must be reported, not handed back to a "
                        "repair mission")
        rearm.assert_called_once()

    def test_emptyRollup_shouldDeferToTheCaller(self):
        """No snapshot at all is not evidence the context is missing.

        `_merge_authorized_pr` returns no rollup when the head moved or the API
        call failed, and neither is an absent-context problem -- re-arming on it
        would publish a head in response to a transient error.
        """
        for rollup in ([], None):
            with self.subTest(rollup=rollup):
                handled, rearm = self._call(rollup, rearmed=True)
                self.assertFalse(handled)
                rearm.assert_not_called()

    def test_emptyRollupWithEvidence_shouldReArm(self):
        """The hanging-CI caller holds evidence an empty rollup alone lacks."""
        handled, rearm = self._call([], rearmed=True, empty_is_evidence=True)
        self.assertTrue(handled,
                        "two hours of an unchanged authorized head reporting no checks "
                        "at all means no run was created")
        rearm.assert_called_once()

    def test_noSnapshotWithEvidence_shouldStillDefer(self):
        """`None` means the snapshot could not be read, whatever the caller claims."""
        handled, rearm = self._call(None, rearmed=True, empty_is_evidence=True)
        self.assertFalse(handled)
        rearm.assert_not_called()

    def test_rollupWithAFailingCheck_shouldDeferToTheCaller(self):
        """Re-arming publishes a head; doing that over a red build discards the diagnosis."""
        rollup = self.ABSENT + [{"__typename": "CheckRun", "name": "ci / test",
                                 "status": "COMPLETED", "conclusion": "FAILURE"}]
        handled, rearm = self._call(rollup, rearmed=True)
        self.assertFalse(handled, "a genuine regression must keep the repair path")
        rearm.assert_not_called()

    def test_pendingRollupWithAbsentContext_shouldReArm(self):
        """The hanging-CI shape: another check pending, but ci-gate never created."""
        rollup = self.ABSENT + [{"__typename": "CheckRun", "name": "Analyze (python)",
                                 "status": "IN_PROGRESS"}]
        self.assertEqual(runner._ci_status_from_rollup(rollup)[0], "PENDING")
        handled, rearm = self._call(rollup, rearmed=True)
        self.assertTrue(handled)
        rearm.assert_called_once()

    def test_reportedBlockedEmail_shouldBeRateLimited(self):
        with patch.object(runner, "_rearm_required_check", return_value=(False, "no worktree")), \
                patch.object(runner, "send_email") as mail, \
                patch.object(runner, "_should_send_rate_limited", return_value=False) as limited:
            runner._handle_absent_required_check(9, "fix/x", "a" * 40, self._tmp.name, self.ABSENT)
        limited.assert_called_once_with(
            f"[Runner] Blocked: PR #9 missing {runner.REQUIRED_STATUS_CONTEXT}")
        mail.assert_not_called()


if __name__ == "__main__":
    unittest.main()
