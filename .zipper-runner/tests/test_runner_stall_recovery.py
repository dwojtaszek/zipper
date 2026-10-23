"""Stall-recovery tests for the runner's authorization escalation.

Covers the failure class that stalled PR #1306 for 10.5 hours on 2026-10-23: a
repair mission force-pushed the branch, the recorded authorization head no longer
matched, and every recovery path was either silent or circular. The single
worktree slot stayed occupied, intake skipped all four cycles, and no email was
ever sent.

The runner deliberately never adopts a head it cannot attribute to its own
recorded work, because the recorded head is exactly what the force-push guard in
_reconcile_active_worktree uses to notice a rewrite. What was missing was the
signal, so these guards are:

  * escalation instead of a silent return when authorization fails,
  * a rate-limited blocked-worktree email naming a reason it can actually see,
  * every fail-closed reason reported, not only a moved head: a draft, a
    retargeted base, a fork head, a renamed branch, a missing record,
  * escalation for PRs whose record names a different PR or no PR at all,
  * escalation for green PRs the runner has no authorization record for,
  * the orphan path escalating when it cannot recreate a worktree,
  * a malformed or null statusCheckRollup treated as not-yet-green, not a crash,
  * distinct log files per runner mode.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import importlib.util
import json
import os
import subprocess
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import patch

import gitenv  # noqa: F401  # Git identity for fixture repositories

BASE = Path(__file__).resolve().parents[1]
# Mirrors the runner's own default so the stale-PR threshold cannot drift.
STALLED_PR_HOURS = float(os.environ.get("RUNNER_STALLED_PR_HOURS", "12"))


def _stale_timestamp():
    """Comfortably past the stall threshold, without a wall-clock literal."""
    return (datetime.now(timezone.utc) - timedelta(hours=STALLED_PR_HOURS + 1)).strftime(
        "%Y-%m-%dT%H:%M:%SZ")


class _EmailRecorder:
    """Records send_email calls at the process boundary."""

    def __init__(self):
        self.messages = []

    def __call__(self, subject, body_html):
        self.messages.append((subject, body_html))

    def subjects(self):
        return [subject for subject, _ in self.messages]


class _FakePlugin:
    """A plugin whose only observable effect is a marker file."""

    def __init__(self, marker: Path):
        self.marker = marker

    def run_mission(self, prompt, cwd, is_continue=False, model=None):
        self.marker.write_text(prompt)
        return 0, "", ""


class _RunnerFixture:
    """Real git refs, a stub gh CLI, and a stub coderabbit/msmtp on PATH."""

    def __init__(self, root: Path, *, pr_overrides=None, pr_list=None):
        self.root = root
        self.repo = root / "repo"
        subprocess.run(["git", "init", "-b", "main", str(self.repo)], check=True, capture_output=True)

        def git(*args):
            return subprocess.check_output(["git", "-C", str(self.repo), *args], text=True).strip()

        (self.repo / "base.txt").write_text("base")
        (self.repo / "tests").mkdir()
        (self.repo / "tests" / "wait-for-reviews.sh").write_text("#!/bin/sh\nexit 0\n")
        git("add", ".")
        git("commit", "-m", "test: initial")
        self.initial = git("rev-parse", "HEAD")

        # The recorded head, then a rewrite of it: old and new diverge, which is
        # what a mission force-push produces.
        git("checkout", "-b", "fix/ISSUE-42-proof")
        (self.repo / "old.txt").write_text("old")
        git("add", ".")
        git("commit", "-m", "wip: recorded head")
        self.old_head = git("rev-parse", "HEAD")

        git("checkout", "main")
        (self.repo / "main2.txt").write_text("main2")
        git("add", ".")
        git("commit", "-m", "test: main advances")
        self.main_head = git("rev-parse", "HEAD")

        git("checkout", "fix/ISSUE-42-proof")
        git("reset", "--hard", self.main_head)
        (self.repo / "new.txt").write_text("new")
        git("add", ".")
        git("commit", "-m", "feat: rewritten work")
        self.new_head = git("rev-parse", "HEAD")

        subprocess.run(["git", "clone", "--bare", str(self.repo), str(root / "origin")],
                       check=True, capture_output=True)
        git("remote", "add", "origin", str(root / "origin"))
        git("push", "-u", "origin", "fix/ISSUE-42-proof")
        # Leave the branch free so tests can add a worktree for it.
        git("checkout", "main")

        self.pr = {
            "number": 99, "state": "open", "draft": False,
            "user": {"login": "dwojtaszek", "type": "User", "id": 1},
            "base": {"ref": "main", "repo": {"full_name": "dwojtaszek/zipper"}},
            "head": {"ref": "fix/ISSUE-42-proof", "sha": self.new_head,
                     "repo": {"full_name": "dwojtaszek/zipper"}},
        }
        self.pr.update(pr_overrides or {})
        (root / "pr.json").write_text(json.dumps(self.pr))
        (root / "reviews.json").write_text(json.dumps([]))
        (root / "pr_list.json").write_text(json.dumps(pr_list if pr_list is not None else []))

        self._write_stub_gh()
        for name, body in (("coderabbit", '#!/bin/sh\necho \'{"type":"complete","status":"review_completed","findings":0}\'\n'),
                           ("msmtp", "#!/bin/sh\ncat > /dev/null\nexit 0\n")):
            executable = root / name
            executable.write_text(body)
            executable.chmod(0o755)

        spec = importlib.util.spec_from_file_location("stall_runner", BASE / "runner.py")
        self.runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runner)
        runner = self.runner
        runner.REPO_PATH = str(self.repo)
        runner.STATE_DIR = str(root / "state")
        runner.WORKTREES_BASE = str(root / "worktrees")
        runner.TRUSTED_AUTHORS = {"dwojtaszek"}
        runner.MERGE_MAINTAINERS = {"dwojtaszek"}
        runner.DRY_RUN = False
        runner.REVIEW_GATE_SCRIPT = "tests/wait-for-reviews.sh"
        runner.wait_for_tokens = lambda: None
        (root / "state").mkdir()
        (root / "state" / "issue-42.json").write_text(json.dumps({
            "pr_number": "99", "branch": "fix/ISSUE-42-proof", "authorized_head": self.old_head,
        }))
        self.emails = _EmailRecorder()
        runner.send_email = self.emails
        self.env = patch.dict(os.environ, {
            "PATH": str(root) + os.pathsep + os.environ["PATH"],
            "FIXTURE": str(root),
        })
        self.env.start()

    def close(self):
        self.env.stop()

    def _write_stub_gh(self):
        gh = self.root / "gh"
        gh.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "from pathlib import Path\n"
            "root = Path(os.environ['FIXTURE'])\n"
            "args = sys.argv[1:]\n"
            "def load(name, default):\n"
            "    p = root / name\n"
            "    return json.loads(p.read_text()) if p.exists() else default\n"
            "if args[:2] == ['api', 'graphql']:\n"
            "    print(json.dumps([0]))\n"
            "elif args[:2] == ['pr', 'merge']:\n"
            "    if (root / 'merge-fail').exists():\n"
            "        sys.exit(1)\n"
            "    (root / 'merged').write_text(args[2])\n"
            "    print('merged')\n"
            "elif args[0] == 'api':\n"
            "    if args[1].endswith('/reviews'):\n"
            "        print(json.dumps([load('reviews.json', [])]))\n"
            "    else:\n"
            "        specific = root / ('pr-%s.json' % args[1].split('/')[-1])\n"
            "        print(specific.read_text() if specific.exists() else (root / 'pr.json').read_text())\n"
            "elif args[:2] == ['pr', 'view']:\n"
            "    pr = load('pr.json', {})\n"
            "    if args[args.index('--json') + 1] == 'commits':\n"
            "        print('feat: completed work')\n"
            "    else:\n"
            "        print(json.dumps({'number': 99, 'state': 'OPEN',\n"
            "            'headRefOid': (pr.get('head') or {}).get('sha'),\n"
            "            'statusCheckRollup': [{'__typename': 'CheckRun', 'name': 'ci-gate',\n"
            "                'status': 'COMPLETED', 'conclusion': 'SUCCESS'}]}))\n"
            "elif args[:2] == ['pr', 'list']:\n"
            "    (root / 'pr-list-fields.txt').write_text(args[args.index('--json') + 1])\n"
            "    print(json.dumps(load('pr_list.json', [])))\n"
            "else:\n"
            "    raise AssertionError(args)\n"
        )
        gh.chmod(0o755)


class BlockedWorktreeEscalationTests(unittest.TestCase):
    """An unauthorized PR must escalate, never fail silent or dispatch a run."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.fixture = _RunnerFixture(self.root)
        self.runner = self.fixture.runner
        self.wt = self.root / "worktrees" / "issue-42"
        subprocess.run(["git", "worktree", "add", str(self.wt), "fix/ISSUE-42-proof"],
                       cwd=self.fixture.repo, check=True, capture_output=True)

    def tearDown(self):
        self.fixture.close()
        self.tmp.cleanup()

    def test_escalation_firstThreeSendsThenRateLimited(self):
        runner = self.runner
        for _ in range(3):
            runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 3)
        runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 3)
        self.assertIn("#99", self.fixture.emails.subjects()[0])

    def test_escalation_offersOnlyAWorkingRemediation(self):
        """Deleting the issue state cannot re-capture a head: provenance is read
        from that state, so offering it would leave the PR stuck forever."""
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        _subject, body = self.fixture.emails.messages[0]
        self.assertIn("APPROVED", body)
        self.assertNotIn("delete", body)
        self.assertNotIn("issue-42.json", body)

    def _write_pr(self, **overrides):
        pr = dict(self.fixture.pr)
        pr.update(overrides)
        (self.root / "pr.json").write_text(json.dumps(pr))
        return pr

    def _make_green_and_stale(self):
        """A gh api body that also satisfies the unrecorded-PR gates."""
        self._write_pr(
            author={"login": "dwojtaszek"},
            updatedAt=_stale_timestamp(),
            statusCheckRollup=[{"__typename": "CheckRun", "name": "ci-gate",
                                "status": "COMPLETED", "conclusion": "SUCCESS"}],
        )

    def test_escalation_stateForAnotherPr_reportsUnrecorded(self):
        """A record that names a different PR authorizes nothing, so the PR must
        still be reported rather than skipped because *some* state exists."""
        self._make_green_and_stale()
        (self.root / "state" / "issue-42.json").write_text(json.dumps({
            "pr_number": "77", "branch": "fix/ISSUE-42-old", "authorized_head": self.fixture.old_head,
        }))
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Authorized", self.fixture.emails.subjects()[0])

    def test_escalation_stateForAnotherBranch_reportsUnrecorded(self):
        self._make_green_and_stale()
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-renamed")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Authorized", self.fixture.emails.subjects()[0])

    def test_escalation_unrecordedContributorPr_staysQuiet(self):
        """A contributor PR that merely mentions an issue number is not the
        runner's to report, even when the issue has a record for another PR."""
        self._write_pr(number=98, user={"login": "outsider", "type": "User", "id": 7},
                       author={"login": "outsider"}, updatedAt=_stale_timestamp())
        self.runner._escalate_blocked_worktree(98, "42", "fix/ISSUE-42-proof")
        self.assertEqual(self.fixture.emails.messages, [])

    def test_escalation_draftPr_reportsDraftReason(self):
        """A maintainer converting the PR to a draft also fails authorization
        closed, with the head unchanged. That used to stall in silence."""
        self._write_pr(draft=True)
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("draft", self.fixture.emails.messages[0][1])

    def test_escalation_retargetedBase_reportsBaseReason(self):
        self._write_pr(base={"ref": "release", "repo": {"full_name": "dwojtaszek/zipper"}})
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("release", self.fixture.emails.messages[0][1])

    def test_escalation_renamedHeadBranch_reportsBranchReason(self):
        self._write_pr(head={"ref": "fix/ISSUE-42-renamed", "sha": self.fixture.new_head,
                             "repo": {"full_name": "dwojtaszek/zipper"}})
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("fix/ISSUE-42-renamed", self.fixture.emails.messages[0][1])

    def test_escalation_forkHead_reportsForkReason(self):
        self._write_pr(head={"ref": "fix/ISSUE-42-proof", "sha": self.fixture.new_head,
                             "repo": {"full_name": "someone/zipper"}})
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("fork", self.fixture.emails.messages[0][1])

    def test_escalation_closedPr_staysQuiet(self):
        """A closed or merged PR needs no authorization, so it must not mail."""
        self._write_pr(state="closed")
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(self.fixture.emails.messages, [])

    def test_escalation_nonDictPrBody_staysQuiet(self):
        """A 200 response that is not a mapping must not crash the cycle."""
        (self.root / "pr.json").write_text('"not a mapping"')
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(self.fixture.emails.messages, [])

    def test_escalation_nonDictHead_staysQuiet(self):
        self._write_pr(head="not-a-mapping")
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(self.fixture.emails.messages, [])

    def test_escalation_headMatchingRecord_staysQuiet(self):
        """Nothing to report while the recorded head is still the PR head and no
        other reason explains the refusal."""
        self._write_pr(head={"ref": "fix/ISSUE-42-proof", "sha": self.fixture.old_head,
                             "repo": {"full_name": "dwojtaszek/zipper"}})
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(self.fixture.emails.messages, [])

    def test_escalation_legacyStateWithoutRecordedHead_reportsReason(self):
        """A record with no authorized_head cannot authorize anything either."""
        self._write_pr()
        (self.root / "state" / "issue-42.json").write_text(json.dumps({
            "pr_number": "99", "branch": "fix/ISSUE-42-proof",
        }))
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("no recorded head", self.fixture.emails.messages[0][1])

    def test_escalation_unreadablePr_staysQuiet(self):
        """A failed PR read also makes authorization fail. Escalating for it
        would assert a head mismatch that does not exist."""
        (self.root / "pr.json").unlink()
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(self.fixture.emails.messages, [])

    def test_escalation_reportsBothHeads(self):
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        _subject, body = self.fixture.emails.messages[0]
        self.assertIn(self.fixture.old_head, body)
        self.assertIn(self.fixture.new_head, body)

    def test_babysit_unauthorizedPr_escalatesWithoutDispatchingAMission(self):
        runner = self.runner
        marker = self.root / "mission-dispatched"
        runner.AGENT_CANDIDATES = [("fake", "model")]
        runner.AGENT_PLUGINS = {"fake": _FakePlugin(marker)}
        runner._babysit_with_fallback("prompt", str(self.wt), "fix/ISSUE-42-proof", 42, 99)
        self.assertFalse(marker.exists(), "no mission may run while the PR is unauthorized")
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Needs Manual Authorization", self.fixture.emails.subjects()[0])

    def test_babysit_draftPr_escalatesWithoutDispatchingAMission(self):
        """Authorization fails closed for reasons other than a moved head too:
        a draft PR, with the head untouched, used to stall this path silently."""
        pr = dict(self.fixture.pr)
        pr["draft"] = True
        (self.root / "pr.json").write_text(json.dumps(pr))
        marker = self.root / "mission-dispatched"
        self.runner.AGENT_CANDIDATES = [("fake", "model")]
        self.runner.AGENT_PLUGINS = {"fake": _FakePlugin(marker)}
        self.runner._babysit_with_fallback("prompt", str(self.wt), "fix/ISSUE-42-proof", 42, 99)
        self.assertFalse(marker.exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("draft", self.fixture.emails.messages[0][1])

    def test_activeWorktree_unauthorizedPr_escalatesAndKeepsSlot(self):
        runner = self.runner
        marker = self.root / "mission-dispatched"
        runner.AGENT_CANDIDATES = [("fake", "model")]
        runner.AGENT_PLUGINS = {"fake": _FakePlugin(marker)}
        runner.babysit_active_worktrees()
        self.assertFalse(marker.exists())
        self.assertFalse((self.root / "merged").exists())
        self.assertTrue(self.wt.exists(), "the worktree must keep its slot until a human decides")
        self.assertIn("Needs Manual Authorization", self.fixture.emails.subjects()[0])

    def test_activeWorktree_missingStateFile_reportsUnrecorded(self):
        """A runner-owned worktree with no authorization record holds the only
        slot and can never be authorized, so it must not go silent either."""
        (self.root / "state" / "issue-42.json").unlink()
        self.runner.babysit_active_worktrees()
        self.assertFalse((self.root / "merged").exists())
        self.assertTrue(self.wt.exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Authorized", self.fixture.emails.subjects()[0])

    def test_activeWorktree_stateForAnotherPr_reportsUnrecorded(self):
        (self.root / "state" / "issue-42.json").write_text(json.dumps({
            "pr_number": "77", "branch": "fix/ISSUE-42-old", "authorized_head": self.fixture.old_head,
        }))
        self.runner.babysit_active_worktrees()
        self.assertFalse((self.root / "merged").exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Authorized", self.fixture.emails.subjects()[0])

    def test_activeWorktree_unauthorizedPr_rateLimited(self):
        """A stalled worktree must not mail on every cycle."""
        runner = self.runner
        for _ in range(4):
            runner.babysit_active_worktrees()
        self.assertEqual(len(self.fixture.emails.messages), 3)


class StateHardeningTests(unittest.TestCase):
    """A damaged state file must not crash the cycle every five minutes."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.fixture = _RunnerFixture(self.root)
        self.runner = self.fixture.runner

    def tearDown(self):
        self.fixture.close()
        self.tmp.cleanup()

    def test_loadPrState_nonDictFile_isIgnored(self):
        path = self.root / "state" / "issue-42.json"
        for content in ("[1, 2]", '"text"', "true"):
            with self.subTest(content=content):
                path.write_text(content)
                self.assertIsNone(self.runner._load_pr_state("42"))

    def test_escalation_nonDictStateFile_doesNotRaise(self):
        (self.root / "state" / "issue-42.json").write_text('"not a mapping"')
        self.runner._escalate_blocked_worktree(99, "42", "fix/ISSUE-42-proof")
        self.assertEqual(self.fixture.emails.messages, [])

    def test_ciStatus_malformedRollup_isPendingNotACrash(self):
        """A null or malformed rollup must not raise: the cycle repeats every
        five minutes, so a crash here would loop identically forever."""
        self.assertEqual(self.runner._ci_status_from_rollup(None)[0], "PENDING")
        self.assertEqual(self.runner._ci_status_from_rollup([])[0], "PENDING")
        malformed = [{"__typename": "CheckRun", "name": "ci-gate",
                      "status": "COMPLETED", "conclusion": "SUCCESS"}, "not-a-check"]
        self.assertEqual(self.runner._ci_status_from_rollup(malformed)[0], "PENDING")


class UnreferencedPrEscalationTests(unittest.TestCase):
    """Green PRs with no issue reference must surface instead of sitting silent."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def _green_pr(self, updated_at, author="dwojtaszek"):
        return {
            "number": 1258, "title": "chore: refresh perf baselines",
            "body": "## Perf Baseline Refresh", "author": {"login": author},
            "headRefName": "perf/baseline-refresh-39231950265", "updatedAt": updated_at,
            "url": "https://github.com/dwojtaszek/zipper/pull/1258",
            "statusCheckRollup": [{"__typename": "CheckRun", "name": "ci-gate",
                                   "status": "COMPLETED", "conclusion": "SUCCESS"}],
        }

    @staticmethod
    def _stale():
        return _stale_timestamp()

    def _scan(self, pr_list):
        fixture = _RunnerFixture(self.root, pr_list=pr_list)
        self.addCleanup(fixture.close)
        fixture.runner.babysit_orphaned_issue_prs()
        return fixture

    def test_orphanScan_staleUnreferencedGreenPr_escalatesWithoutMerging(self):
        fixture = self._scan([self._green_pr(self._stale())])
        self.assertFalse((self.root / "merged").exists())
        self.assertEqual(len(fixture.emails.messages), 1)
        subject, body = fixture.emails.messages[0]
        self.assertIn("#1258", subject)
        self.assertIn("no authorization record", body)

    def test_orphanScan_freshUnreferencedGreenPr_staysQuiet(self):
        recent = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        fixture = self._scan([self._green_pr(recent)])
        self.assertEqual(fixture.emails.messages, [])

    def test_orphanScan_unreferencedPrWithFailingCi_staysQuiet(self):
        pr = self._green_pr(self._stale())
        pr["statusCheckRollup"][0]["conclusion"] = "FAILURE"
        fixture = self._scan([pr])
        self.assertEqual(fixture.emails.messages, [])

    def test_orphanScan_staleUnreferencedDraftPr_staysQuiet(self):
        """A draft PR is not awaiting merge, so escalating for it would be noise.
        gh pr list must therefore request isDraft, not just gh api's draft."""
        pr = self._green_pr(self._stale())
        pr["isDraft"] = True
        fixture = self._scan([pr])
        self.assertEqual(fixture.emails.messages, [])
        self.assertIn("isDraft", (self.root / "pr-list-fields.txt").read_text(),
                      "gh pr list must request isDraft, or _pr_is_draft cannot see it")

    def test_orphanScan_untrustedAuthor_staysQuiet(self):
        """An outsider's PR is not the runner's business to report."""
        fixture = self._scan([self._green_pr(self._stale(), author="outsider")])
        self.assertEqual(fixture.emails.messages, [])

    def test_orphanScan_unparseableUpdatedAt_staysQuiet(self):
        fixture = self._scan([self._green_pr("not-a-timestamp")])
        self.assertEqual(fixture.emails.messages, [])

    def test_orphanScan_repeatedCycles_rateLimitTheEscalation(self):
        """A persistent stall must not mail on every cycle."""
        fixture = _RunnerFixture(self.root, pr_list=[self._green_pr(self._stale())])
        self.addCleanup(fixture.close)
        for _ in range(5):
            fixture.runner.babysit_orphaned_issue_prs()
        self.assertEqual(len(fixture.emails.messages), 3)


class OrphanRecoveryEscalationTests(unittest.TestCase):
    """The orphan path must never skip a stalled PR in silence."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.fixture = _RunnerFixture(self.root)
        self.runner = self.fixture.runner

    def tearDown(self):
        self.fixture.close()
        self.tmp.cleanup()

    def _orphan_pr(self, conclusion="SUCCESS"):
        return {
            "number": 99, "title": "fix: proof", "body": "Fixes #42",
            "author": {"login": "dwojtaszek"}, "headRefName": "fix/ISSUE-42-proof",
            "updatedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "url": "https://github.com/dwojtaszek/zipper/pull/99",
            "statusCheckRollup": [{"__typename": "CheckRun", "name": "ci-gate",
                                   "status": "COMPLETED", "conclusion": conclusion}],
        }

    def _scan(self, pr):
        (self.root / "pr_list.json").write_text(json.dumps([pr]))
        self.runner.babysit_orphaned_issue_prs()

    def _authorize_current_head(self, *, block_recreation=False):
        """Record the PR's current head so authorization succeeds. Optionally
        park the local branch elsewhere so a worktree cannot be recreated."""
        (self.root / "state" / "issue-42.json").write_text(json.dumps({
            "pr_number": "99", "branch": "fix/ISSUE-42-proof", "authorized_head": self.fixture.new_head,
        }))
        if block_recreation:
            subprocess.run(["git", "-C", str(self.fixture.repo), "branch", "-f",
                            "fix/ISSUE-42-proof", self.fixture.old_head], check=True, capture_output=True)

    def test_orphanScan_unauthorizedRecordedPr_escalates(self):
        """The recorded head no longer matches the PR head: the runner used to
        skip this every cycle with no signal at all."""
        self._scan(self._orphan_pr())
        self.assertFalse((self.root / "merged").exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Needs Manual Authorization", self.fixture.emails.subjects()[0])

    def test_orphanScan_stalePrWithNoRecord_escalates(self):
        """A PR naming an issue the runner never recorded work for can never be
        authorized, so it must surface rather than be skipped forever."""
        (self.root / "state" / "issue-42.json").unlink()
        pr = self._orphan_pr()
        pr["updatedAt"] = _stale_timestamp()
        self._scan(pr)
        self.assertFalse((self.root / "merged").exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Authorized", self.fixture.emails.subjects()[0])

    def test_orphanScan_freshPrWithNoRecord_staysQuiet(self):
        """A freshly opened PR is not a stall yet, so it must not mail."""
        (self.root / "state" / "issue-42.json").unlink()
        self._scan(self._orphan_pr())
        self.assertEqual(self.fixture.emails.messages, [])

    def test_orphanScan_unrecreatableWorktree_escalates(self):
        """A worktree that cannot be recreated leaves the PR unmergeable, so the
        operator must be told instead of the runner retrying silently."""
        # The PR head matches the recorded head, so authorization succeeds; the
        # local branch sits elsewhere, so recreation cannot succeed.
        self._authorize_current_head(block_recreation=True)
        self._scan(self._orphan_pr(conclusion="FAILURE"))
        self.assertFalse((self.root / "merged").exists())
        self.assertFalse((self.root / "worktrees" / "issue-42").exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Babied", self.fixture.emails.subjects()[0])

    def test_orphanScan_unsatisfiedReviewGateUnrecreatable_escalates(self):
        """The review-gate branch must escalate too, not just the CI-failure one."""
        self._authorize_current_head(block_recreation=True)
        (self.fixture.repo / "fail-gate.sh").write_text("#!/bin/sh\nexit 1\n")
        self.runner.REVIEW_GATE_SCRIPT = "fail-gate.sh"
        self._scan(self._orphan_pr())
        self.assertFalse((self.root / "merged").exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Babied", self.fixture.emails.subjects()[0])

    def test_orphanScan_failedMergeUnrecreatable_escalates(self):
        """A merge that keeps failing must escalate rather than loop silently."""
        self._authorize_current_head(block_recreation=True)
        (self.root / "merge-fail").write_text("1")
        self._scan(self._orphan_pr())
        self.assertFalse((self.root / "merged").exists())
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("Cannot Be Babied", self.fixture.emails.subjects()[0])

    def test_orphanScan_recreatedWorktree_staysQuiet(self):
        """When a worktree is recreated the runner babysits it, so no escalation."""
        self._authorize_current_head()
        self._scan(self._orphan_pr(conclusion="FAILURE"))
        self.assertTrue((self.root / "worktrees" / "issue-42").exists())
        self.assertEqual(self.fixture.emails.messages, [])

    def test_orphanScan_issueReferenceWithoutRunnerRecord_escalatesUnreferenced(self):
        """Naming an issue must not silence a PR the runner has no record for."""
        (self.root / "state" / "issue-42.json").unlink()
        pr = self._orphan_pr()
        pr["updatedAt"] = _stale_timestamp()
        self._scan(pr)
        self.assertEqual(len(self.fixture.emails.messages), 1)
        self.assertIn("no authorization record", self.fixture.emails.messages[0][1])

    def test_orphanScan_unrecoverablePr_repeatedCycles_rateLimited(self):
        """A worktree that stays unrecreatable must not mail every cycle."""
        self._authorize_current_head(block_recreation=True)
        for _ in range(5):
            self._scan(self._orphan_pr(conclusion="FAILURE"))
        self.assertEqual(len(self.fixture.emails.messages), 3)
        self.assertIn("Cannot Be Babied", self.fixture.emails.subjects()[0])


class CronWrapperLogTests(unittest.TestCase):
    """Intake and babysit fire in the same second; their logs must not collide."""

    def test_cronWrapper_sameSecondRuns_writeDistinctLogs(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory) / "runner"
            base.mkdir()
            (base / "cron-wrapper.sh").write_text((BASE / "cron-wrapper.sh").read_text())
            (base / "cron-wrapper.sh").chmod(0o755)
            (base / "runner.py").write_text("print('stub')\n")

            for args in (["--babysit-only"], ["--babysit-only"], []):
                subprocess.run(["bash", str(base / "cron-wrapper.sh"), *args],
                               check=True, capture_output=True)

            logs = sorted((base / "logs").glob("run_*.log"))
            self.assertEqual(len(logs), 3, f"expected one log per invocation, got {[p.name for p in logs]}")
            modes = {path.name.split("_")[3] for path in logs}
            self.assertEqual(modes, {"babysit", "intake"})
            for path in logs:
                text = path.read_text()
                self.assertEqual(text.count("Starting Autonomous Runner Cron Run"), 1)
                self.assertEqual(text.count("Finished Autonomous Runner Cron Run"), 1)


if __name__ == "__main__":
    unittest.main()
