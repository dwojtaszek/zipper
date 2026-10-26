"""An escalation must name what actually blocks the PR, and mail once per head.

Two defects reached the operator on PR #1362. The mail advised an APPROVED review
or a direct merge, and neither could be carried out: main requires no approvals at
all, GitHub forbids approving your own pull request, and the unresolved review
thread blocked the merge server-side — a blocker the mail never mentioned. The
same unchanging condition was also mailed three times in twenty minutes, because
the orphan scanner runs from both the intake and the babysit entry point and the
subject rate limit sends its first three unconditionally.
"""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import gitenv  # noqa: F401

BASE = Path(__file__).resolve().parents[1]
HEAD_A = "a" * 40
HEAD_B = "b" * 40


class _EscalationFixture(unittest.TestCase):
    """Load runner.py against a temporary STATE_DIR with no network and no mail."""

    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)
        spec = importlib.util.spec_from_file_location("escalation_runner", BASE / "runner.py")
        self.runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runner)
        self.runner.STATE_DIR = str(self.root / "state")
        self.runner.DRY_RUN = False
        (self.root / "state").mkdir()
        self.sent = []
        # Kept so a test can exercise the real delivery path; the patch below
        # replaces the module attribute, not this reference.
        self.real_send_email = self.runner.send_email
        mail = patch.object(
            self.runner, "send_email",
            side_effect=lambda subject, body: self.sent.append((subject, body)) or True,
        )
        mail.start()
        self.addCleanup(mail.stop)
        # Every escalation reads the thread count from GitHub. Stub it here so no
        # test in this file reaches the network; tests that care re-patch it.
        threads = patch.object(self.runner, "_query_review_thread_count", return_value=0)
        threads.start()
        self.addCleanup(threads.stop)

    def tearDown(self):
        self._temp.cleanup()

    def pr(self, *, head=HEAD_A, number=1362, labels=None):
        """A gh pr list entry for an automated PR that names no issue."""
        entry = {
            "number": number,
            "title": "chore: refresh perf baselines",
            "body": "## Perf Baseline Refresh\n\nNo issue reference anywhere.",
            "author": {"login": "dwojtaszek"},
            "headRefName": "perf/baseline-refresh-40302990823",
            "headRefOid": head,
            "isDraft": False,
            "url": f"https://github.com/dwojtaszek/zipper/pull/{number}",
        }
        if labels is not None:
            entry["labels"] = labels
        return entry

    def escalate(self, pr, *, threads=0, **kwargs):
        with patch.object(self.runner, "_query_review_thread_count", return_value=threads):
            self.runner._escalate_unrecorded_pr(pr, **kwargs)
        return self.sent


class EscalationTextTests(_EscalationFixture):
    def test_unrecordedPr_withAnUnresolvedThread_namesItInsteadOfAnApproval(self):
        self.escalate(self.pr(), threads=1, require_green_and_stale=False)
        self.assertEqual(len(self.sent), 1)
        body = self.sent[0][1]
        self.assertIn("1 unresolved review thread", body)
        self.assertNotIn("APPROVED review", body)
        self.assertIn("name the issue", body)

    def test_unrecordedPr_withSeveralThreads_countsThem(self):
        self.escalate(self.pr(), threads=3, require_green_and_stale=False)
        self.assertIn("3 unresolved review threads", self.sent[0][1])

    def test_unrecordedPr_withNoUnresolvedThread_doesNotInventOne(self):
        self.escalate(self.pr(), threads=0, require_green_and_stale=False)
        body = self.sent[0][1]
        self.assertNotIn("unresolved review thread", body)
        self.assertIn("name the issue", body)

    def test_unrecordedPr_withUnreadableThreadState_saysSoInsteadOfClaimingNone(self):
        """An unreadable thread count must not read as "no threads outstanding"."""
        self.escalate(self.pr(), threads=-1, require_green_and_stale=False)
        self.assertIn("could not read", self.sent[0][1])

    def test_escalation_neverAdvertisesAnApprovalItCannotReceive(self):
        """The only merge maintainer is the author, so an approval is unreachable."""
        for index, threads in enumerate((-1, 0, 1, 5)):
            with self.subTest(threads=threads):
                self.sent.clear()
                # A distinct head per case, because the guard dedupes by head.
                self.escalate(self.pr(head=f"{index:040d}"), threads=threads, require_green_and_stale=False)
                self.assertNotIn("APPROVED review", self.sent[0][1])


class EscalationDedupeTests(_EscalationFixture):
    def test_sameHead_isMailedOnce(self):
        """The intake and babysit scanners both see this PR in one cycle."""
        pr = self.pr()
        with patch.object(self.runner, "_query_review_thread_count", return_value=1):
            self.runner._escalate_unrecorded_pr(pr, require_green_and_stale=False)
            self.runner._escalate_unrecorded_pr(pr, require_green_and_stale=False)
            self.runner._escalate_unrecorded_pr(pr, require_green_and_stale=False)
        self.assertEqual(len(self.sent), 1)

    def test_newHead_isMailedAgain(self):
        """A new commit is the point at which a human has something new to read."""
        with patch.object(self.runner, "_query_review_thread_count", return_value=1):
            self.runner._escalate_unrecorded_pr(self.pr(head=HEAD_A), require_green_and_stale=False)
            self.runner._escalate_unrecorded_pr(self.pr(head=HEAD_B), require_green_and_stale=False)
        self.assertEqual(len(self.sent), 2)

    def test_differentSubjectAtTheSameHead_isStillMailed(self):
        """One PR can raise several different alerts at the same head — an
        unauthorized draft that is then marked ready and trips the review gate.
        Those are different problems, so the second must not be suppressed."""
        with patch.object(self.runner, "_query_review_thread_count", return_value=1):
            self.runner._escalate_unrecorded_pr(self.pr(), require_green_and_stale=False)
            self.runner._escalate_automated_pr(1362, "perf/baseline-refresh", head=HEAD_A, reason="r")
        self.assertEqual([s for s, _ in self.sent],
                         ["[Runner] PR #1362 Cannot Be Authorized", "[Runner] PR #1362 Cannot Be Merged"])

    def test_sameSubjectAtTheSameHead_isStillMailedOnce(self):
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_A, reason="r")
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_A, reason="r")
        self.assertEqual(len(self.sent), 1)

    def test_changedBlockerAtTheSameHead_isStillMailed(self):
        """The blocker is part of the key. An adopted PR whose CI first fails and
        then passes exposes a different, unsatisfied gate: the head is unchanged
        but the action a human must take is not."""
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_A, reason="CI is failing: ci-gate")
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_A, reason="the robot review gate is not satisfied")
        self.assertEqual(len(self.sent), 2)
        self.assertIn("CI is failing: ci-gate", self.sent[0][1])
        self.assertIn("the robot review gate is not satisfied", self.sent[1][1])

    def test_changedRefusalReasonAtTheSameHead_isStillMailed(self):
        """Same for the authorization path: the refusal reason is part of the key, so
        a PR that stops being a draft and is then retargeted off main reports both."""
        pr = self.pr(head=HEAD_A)
        with patch.object(self.runner, "_query_review_thread_count", return_value=0):
            self.runner._send_pr_escalation(99, HEAD_A, "S", "draft body", detail="the PR is a draft")
            self.runner._send_pr_escalation(99, HEAD_A, "S", "base body", detail="its base branch is 'other', not 'main'")
            self.runner._send_pr_escalation(99, HEAD_A, "S", "draft again", detail="the PR is a draft")
        self.assertEqual([b for _, b in self.sent], ["draft body", "base body"])

    def test_newHead_clearsTheSubjectList(self):
        """A new head is a new state of the PR, so every alert is eligible again —
        including one that was already sent against the previous head."""
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_A, reason="r")
        self.runner._escalate_unrecorded_pr(self.pr(head=HEAD_A), require_green_and_stale=False)
        self.assertEqual(len(self.sent), 2)
        self.sent.clear()
        # Same two subjects, new head: both must mail again.
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_B, reason="r")
        self.runner._escalate_unrecorded_pr(self.pr(head=HEAD_B), require_green_and_stale=False)
        self.assertEqual(len(self.sent), 2)
        record = json.loads((self.root / "state" / "pr-1362-escalated.json").read_text())
        self.assertEqual(record["head"], HEAD_B)
        self.assertEqual(len(record["keys"]), 2)

    def test_escalatedHead_isRecordedUnderAPrunableStateName(self):
        with patch.object(self.runner, "_query_review_thread_count", return_value=1):
            self.runner._escalate_unrecorded_pr(self.pr(), require_green_and_stale=False)
        path = self.root / "state" / "pr-1362-escalated.json"
        self.assertEqual(json.loads(path.read_text())["head"], HEAD_A)
        # The age sweep must be able to reclaim it, or one file accumulates per PR.
        self.assertTrue(self.runner._prunable_state_file(path.name, set(), set()))

    def test_dryRun_neitherMailsNorRecords(self):
        self.runner.DRY_RUN = True
        with patch.object(self.runner, "_query_review_thread_count", return_value=1):
            self.runner._escalate_unrecorded_pr(self.pr(), require_green_and_stale=False)
        self.assertEqual(self.sent, [])
        self.assertFalse((self.root / "state" / "pr-1362-escalated.json").exists())

    def test_unreadableHead_fallsBackToTheSubjectRateLimit(self):
        """An unreadable head has no record to key on, so the subject cooldown is
        what bounds the repeats: three sends, then silence for the cooldown."""
        subject = "[Runner] PR #1362 Cannot Be Authorized"
        results = [self.runner._escalation_head_already_sent(1362, "", subject) for _ in range(4)]
        self.assertEqual(results, [False, False, False, True])
        self.assertFalse((self.root / "state" / "pr-1362-escalated.json").exists())

    def test_unreadableHead_stillMailsThroughTheEscalation(self):
        pr = self.pr()
        pr.pop("headRefOid")
        with patch.object(self.runner, "_query_review_thread_count", return_value=1):
            self.runner._escalate_unrecorded_pr(pr, require_green_and_stale=False)
        self.assertEqual(len(self.sent), 1)

    def test_failedDelivery_doesNotRecordTheHeadSoTheNextCycleRetries(self):
        """A recorded send that never arrived silences the alert for that head, and a
        transient msmtp failure is exactly when the operator most needs the mail."""
        self.sent.clear()
        with patch.object(self.runner, "send_email", return_value=False) as failed:
            with patch.object(self.runner, "_query_review_thread_count", return_value=1):
                self.runner._escalate_unrecorded_pr(self.pr(), require_green_and_stale=False)
                self.runner._escalate_unrecorded_pr(self.pr(), require_green_and_stale=False)
        self.assertEqual(failed.call_count, 2)
        self.assertFalse((self.root / "state" / "pr-1362-escalated.json").exists())


class AutomatedAdoptionEscalationTests(_EscalationFixture):
    def test_adoptedAutomatedPr_namesTheBlockerAndTheAbsentIssue(self):
        self.runner._escalate_automated_pr(
            1362, "perf/baseline-refresh-40302990823", head=HEAD_A, reason="the robot review gate is not satisfied")
        self.assertEqual(len(self.sent), 1)
        subject, body = self.sent[0]
        self.assertIn("Cannot Be Merged", subject)
        self.assertIn("the robot review gate is not satisfied", body)
        self.assertIn("automated", body)
        self.assertNotIn("worktree", body.lower())
        # Nothing else is outstanding, so the advice must still say something.
        self.assertIn("review the PR by hand", body)

    def test_adoptedAutomatedPr_isDedupedByHeadToo(self):
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_A, reason="r")
        self.runner._escalate_automated_pr(1362, "b", head=HEAD_A, reason="r")
        self.assertEqual(len(self.sent), 1)


class SendEmailOutcomeTests(_EscalationFixture):
    """The dedupe record trusts send_email's return, so pin what it returns."""

    def _send(self, script):
        binary = self.root / "msmtp"
        binary.write_text(script)
        binary.chmod(0o755)
        with patch.dict(os.environ, {"PATH": str(self.root) + os.pathsep + os.environ["PATH"]}), \
                patch.object(self.runner, "_email_delivery_permitted", return_value=True):
            return self.real_send_email("subject", "<p>body</p>")

    def test_sendEmail_successfulDelivery_reportsTrue(self):
        self.assertIs(self._send("#!/bin/sh\ncat >/dev/null\nexit 0\n"), True)

    def test_sendEmail_failedDelivery_reportsFalse(self):
        """A transient msmtp failure must not be recorded as having told a human."""
        self.assertIs(self._send("#!/bin/sh\ncat >/dev/null\nexit 1\n"), False)

    def test_sendEmail_missingBinary_reportsFalse(self):
        with patch.dict(os.environ, {"PATH": str(self.root)}), \
                patch.object(self.runner, "_email_delivery_permitted", return_value=True):
            self.assertIs(self.real_send_email("subject", "<p>body</p>"), False)

    def test_sendEmail_suppressedDelivery_reportsTrue(self):
        """Nothing was attempted, so there is no failure to report."""
        with patch.object(self.runner, "_email_delivery_permitted", return_value=False):
            self.assertIs(self.real_send_email("subject", "<p>body</p>"), True)


class HandOrphanPrBackTests(_EscalationFixture):
    """A worktree is named by issue, so an issue-less PR cannot get one."""

    def test_prWithNoIssue_reportsTheBlockerInsteadOfCreatingAWorktree(self):
        with patch.object(self.runner, "_recreate_worktree_for_pr") as recreate:
            self.runner._hand_orphan_pr_back(
                1362, None, "perf/baseline-refresh-40302990823", HEAD_A, "CI is failing: ci-gate")
        recreate.assert_not_called()
        self.assertEqual(len(self.sent), 1)
        self.assertIn("CI is failing: ci-gate", self.sent[0][1])

    def test_prWithAnIssue_stillRecreatesTheWorktree(self):
        with patch.object(self.runner, "_recreate_worktree_for_pr", return_value=True) as recreate:
            self.runner._hand_orphan_pr_back(99, "42", "fix/ISSUE-42-proof", HEAD_A, "CI is failing")
        recreate.assert_called_once_with("42", "fix/ISSUE-42-proof", 99, HEAD_A)
        self.assertEqual(self.sent, [])

    def test_unrecreatableWorktreeWithAnIssue_reportsCannotBeBabied(self):
        with patch.object(self.runner, "_recreate_worktree_for_pr", return_value=False), \
                patch.object(self.runner, "_query_review_thread_count", return_value=2):
            self.runner._hand_orphan_pr_back(99, "42", "fix/ISSUE-42-proof", HEAD_A, "CI is failing")
        self.assertIn("Cannot Be Babied", self.sent[0][0])
        self.assertIn("2 unresolved review threads", self.sent[0][1])
