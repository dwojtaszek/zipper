"""Silent-close audit tests.

Covers the failure found by the 2026-10-27 closed-issue audit: issue #1363, a
weekly CLI QA improvement review, recorded a genuinely failing QA gate
(``qa_report_validation_failed``) and was then closed COMPLETED with zero
comments and no merged PR behind it. The failure it documented vanished without
a trace, and nothing in the runner noticed.

The guard re-derives GitHub's own two backing signals -- the closing-PR
references GitHub maintains for a ``Closes #N`` merge, and the author's own
comment -- and emails a digest. It is warn-only by construction: it must never
comment, close, or reopen, because an audit that can mutate the state it reports
on cannot be trusted to report accurately.

Measured precision over the 38 issues closed in the seven days to 2026-10-27:
exactly one flagged (#1363), zero false positives.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import ast
import importlib.util
import json
import os
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]


def _load_runner():
    spec = importlib.util.spec_from_file_location("silent_close_runner", BASE / "runner.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class _FakeRunCmd:
    """Stands in for run_cmd, recording calls and returning a canned result.

    ``result`` may be a callable taking the argument list, for tests that need a
    different answer per invocation.
    """

    def __init__(self, result=(0, "[]", "")):
        self.result = result
        self.calls = []

    def __call__(self, args, cwd=None, timeout=None, env=None):
        self.calls.append(list(args))
        return self.result(args) if callable(self.result) else self.result


class _EmailRecorder:
    def __init__(self):
        self.messages = []

    def __call__(self, subject, body_html):
        self.messages.append((subject, body_html))
        return True

    def subjects(self):
        return [subject for subject, _ in self.messages]


def _issue(number, closed_at, *, state_reason="COMPLETED", comments=0, backing_prs=0):
    return {
        "number": number,
        "title": f"issue {number}",
        "url": f"https://github.com/dwojtaszek/zipper/issues/{number}",
        "closedAt": closed_at,
        "stateReason": state_reason,
        "comments": [{"body": "note"}] * comments,
        "closedByPullRequestsReferences": [{"number": 900 + n} for n in range(backing_prs)],
    }


class _GuardCase(unittest.TestCase):
    """A loaded runner with a temp state dir and stubbed process boundaries."""

    def setUp(self):
        self.runner = _load_runner()
        self._tmp = tempfile.TemporaryDirectory(prefix="zipper-silent-close-")
        self.root = Path(self._tmp.name)
        self.runner.STATE_DIR = str(self.root / "state")
        self.runner.DRY_RUN = False
        self.runner.REPO_PATH = str(self.root / "repo")
        self.emails = _EmailRecorder()
        self.runner.send_email = self.emails
        self.run_cmd = _FakeRunCmd()
        self.runner.run_cmd = self.run_cmd

    def tearDown(self):
        self._tmp.cleanup()

    def _issue_list(self, issues):
        self.run_cmd.result = (0, json.dumps(issues), "")

    def _state(self):
        path = self.root / "state" / "silent-close-audit.json"
        return json.loads(path.read_text()) if path.exists() else None

    def _sweep(self, since, until=None):
        """Read the sweep with a default upper bound of now."""
        return self.runner._silently_closed_issues(since, until or datetime.now(timezone.utc))


class TimestampParsingTests(_GuardCase):
    def test_zuluSuffix_shouldParseAsAware(self):
        parsed = self.runner._parse_github_timestamp("2026-10-26T18:31:31Z")
        self.assertEqual(parsed, datetime(2026, 10, 26, 18, 31, 31, tzinfo=timezone.utc))

    def test_offsetSuffix_shouldParseAsAware(self):
        parsed = self.runner._parse_github_timestamp("2026-10-26T18:31:31+00:00")
        self.assertEqual(parsed, datetime(2026, 10, 26, 18, 31, 31, tzinfo=timezone.utc))

    def test_bothSpellings_shouldCompareAsTheSameInstant(self):
        self.assertEqual(
            self.runner._parse_github_timestamp("2026-10-26T18:31:31Z"),
            self.runner._parse_github_timestamp("2026-10-26T18:31:31+00:00"),
        )

    def test_timezoneFreeStamp_shouldReturnNone(self):
        self.assertIsNone(self.runner._parse_github_timestamp("2026-10-26T18:31:31"))

    def test_garbage_shouldReturnNone(self):
        self.assertIsNone(self.runner._parse_github_timestamp("not-a-timestamp"))

    def test_nonString_shouldReturnNone(self):
        for value in (None, 17, [], {}):
            with self.subTest(value=value):
                self.assertIsNone(self.runner._parse_github_timestamp(value))


class WindowComparisonTests(_GuardCase):
    """The window filter must compare instants, not strings.

    GitHub emits 'Z' and the state file writes '+00:00'. Compared as strings,
    '...49Z' sorts *after* '...49.5+00:00' because 'Z' > '.', so a close half a
    second before the window opened would be re-reported on every cycle.
    """

    def test_closeJustBeforeTheWindow_shouldBeExcludedDespiteStringOrdering(self):
        since = datetime(2026, 10, 26, 18, 31, 31, 500000, tzinfo=timezone.utc)
        self._issue_list([_issue(1, "2026-10-26T18:31:31Z")])
        self.assertEqual(self._sweep(since).flagged, [])

    def test_closeJustAfterTheWindow_shouldBeIncluded(self):
        since = datetime(2026, 10, 26, 18, 31, 30, tzinfo=timezone.utc)
        self._issue_list([_issue(1, "2026-10-26T18:31:31Z")])
        sweep = self._sweep(since)
        self.assertEqual([i["number"] for i in sweep.flagged], [1])

    def test_closeExactlyAtTheWindow_shouldBeExcluded(self):
        since = datetime(2026, 10, 26, 18, 31, 31, tzinfo=timezone.utc)
        self._issue_list([_issue(1, "2026-10-26T18:31:31Z")])
        self.assertEqual(self._sweep(since).flagged, [])

    def test_unparseableClosedAt_shouldBeSkippedNotCrash(self):
        since = datetime(2026, 10, 26, 0, 0, 0, tzinfo=timezone.utc)
        self._issue_list([_issue(1, None), _issue(2, "garbage"), _issue(3, "2026-10-26T18:00:00Z")])
        sweep = self._sweep(since)
        self.assertEqual([i["number"] for i in sweep.flagged], [3])

    def test_futureClosedAt_shouldBeExcluded(self):
        """A host clock behind GitHub's must not re-report the same issue forever."""
        now = datetime(2026, 10, 27, 12, 0, tzinfo=timezone.utc)
        self._issue_list([_issue(1, "2026-10-27T13:00:00Z")])
        self.assertEqual(self._sweep(now - timedelta(days=1), now).flagged, [])

    def test_closedAtExactlyAtTheUpperBound_shouldBeIncluded(self):
        now = datetime(2026, 10, 27, 12, 0, tzinfo=timezone.utc)
        self._issue_list([_issue(1, "2026-10-27T12:00:00Z")])
        self.assertEqual([i["number"] for i in self._sweep(now - timedelta(days=1), now).flagged], [1])


class FlaggingRuleTests(_GuardCase):
    def setUp(self):
        super().setUp()
        self.since = datetime(2026, 10, 26, 0, 0, 0, tzinfo=timezone.utc)
        self.closed_at = "2026-10-26T18:31:31Z"

    def test_completedWithNoCommentAndNoBackingPr_shouldBeFlagged(self):
        self._issue_list([_issue(1363, self.closed_at)])
        sweep = self._sweep(self.since)
        self.assertEqual([i["number"] for i in sweep.flagged], [1363])

    def test_aComment_shouldSuppressTheFlag(self):
        self._issue_list([_issue(1268, self.closed_at, comments=1)])
        self.assertEqual(self._sweep(self.since).flagged, [])

    def test_aBackingPr_shouldSuppressTheFlag(self):
        self._issue_list([_issue(1347, self.closed_at, backing_prs=1)])
        self.assertEqual(self._sweep(self.since).flagged, [])

    def test_notPlanned_shouldSuppressTheFlag(self):
        self._issue_list([_issue(1204, self.closed_at, state_reason="NOT_PLANNED")])
        self.assertEqual(self._sweep(self.since).flagged, [])

    def test_missingStateReason_shouldSuppressTheFlag(self):
        issue = _issue(1204, self.closed_at)
        del issue["stateReason"]
        self._issue_list([issue])
        self.assertEqual(self._sweep(self.since).flagged, [])

    def test_nonDictEntry_shouldBeSkipped(self):
        self._issue_list([None, "x", 7, _issue(1363, self.closed_at)])
        sweep = self._sweep(self.since)
        self.assertEqual([i["number"] for i in sweep.flagged], [1363])

    def test_emptyList_shouldReturnNoFlags(self):
        self._issue_list([])
        self.assertEqual(self._sweep(self.since).flagged, [])

    def test_openPrReference_shouldSuppressTheFlag(self):
        """A documented limitation, pinned so the choice stays visible.

        `closedByPullRequestsReferences` carries a PR's number but not its merged
        state, so a close with a referencing PR that never merged is not reported.
        The rule the guard was asked for is "no comment and no backing PR", and a
        referenced PR is the backing signal GitHub maintains.
        """
        self._issue_list([_issue(1268, self.closed_at, backing_prs=1)])
        self.assertEqual(self._sweep(self.since).flagged, [])


class ReadFailureTests(_GuardCase):
    """A failed read must return None so the caller retries the same window."""

    def test_nonZeroExit_shouldReturnNone(self):
        self.run_cmd.result = (1, "", "gh: not found")
        self.assertIsNone(self._sweep(datetime.now(timezone.utc) - timedelta(hours=1)))

    def test_unparseableJson_shouldReturnNone(self):
        self.run_cmd.result = (0, "not json", "")
        self.assertIsNone(self._sweep(datetime.now(timezone.utc) - timedelta(hours=1)))

    def test_nonListJson_shouldReturnNone(self):
        self.run_cmd.result = (0, json.dumps({"number": 1}), "")
        self.assertIsNone(self._sweep(datetime.now(timezone.utc) - timedelta(hours=1)))

    def test_nullJson_shouldReturnNone(self):
        self.run_cmd.result = (0, "null", "")
        self.assertIsNone(self._sweep(datetime.now(timezone.utc) - timedelta(hours=1)))


class QueryShapeTests(_GuardCase):
    def test_query_shouldAskForTheBackingSignalsAndBoundTheWindow(self):
        self._issue_list([])
        self._sweep(datetime(2026, 10, 26, 18, 0, tzinfo=timezone.utc))
        args = self.run_cmd.calls[0]
        self.assertEqual(args[:3], ["gh", "issue", "list"])
        self.assertEqual(args[args.index("--state") + 1], "closed")
        self.assertEqual(args[args.index("--search") + 1], "closed:>=2026-10-26")
        fields = args[args.index("--json") + 1]
        for field in ("closedAt", "stateReason", "comments", "closedByPullRequestsReferences"):
            self.assertIn(field, fields)

    def test_query_shouldNotRequestAnyMutatingSubcommand(self):
        """The audit reads and nothing else; pin the argv it actually issues."""
        self._issue_list([])
        self._sweep(datetime.now(timezone.utc) - timedelta(hours=1))
        for args in self.run_cmd.calls:
            self.assertEqual(args[:3], ["gh", "issue", "list"])


class AuditSweepTests(_GuardCase):
    def test_noFlags_shouldSendNoEmailAndAdvanceTheWindow(self):
        self._issue_list([])
        self.assertEqual(self.runner.audit_silently_closed_issues(), 0)
        self.assertEqual(self.emails.messages, [])
        self.assertIsNotNone(self._state())

    def test_flags_shouldSendOneDigestNamingEveryIssue(self):
        self._issue_list([
            _issue(1363, datetime.now(timezone.utc).isoformat()),
            _issue(1364, datetime.now(timezone.utc).isoformat()),
        ])
        self.assertEqual(self.runner.audit_silently_closed_issues(), 2)
        self.assertEqual(len(self.emails.messages), 1)
        subject, body = self.emails.messages[0]
        self.assertIn("#1363", subject)
        self.assertIn("#1364", subject)
        self.assertIn("1363", body)
        self.assertIn("1364", body)

    def test_readFailure_shouldSendNoEmailAndNotAdvanceTheWindow(self):
        self.run_cmd.result = (1, "", "boom")
        self.assertEqual(self.runner.audit_silently_closed_issues(), 0)
        self.assertEqual(self.emails.messages, [])
        self.assertIsNone(self._state())

    def test_dryRun_shouldNotQueryOrEmailOrAdvance(self):
        self.runner.DRY_RUN = True
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.assertEqual(self.runner.audit_silently_closed_issues(), 0)
        self.assertEqual(self.run_cmd.calls, [])
        self.assertEqual(self.emails.messages, [])
        self.assertIsNone(self._state())

    def test_secondSweep_shouldNotRepeatAnAlreadyReportedIssue(self):
        """The watermark trails, so the overlap is re-read; dedupe must absorb it.

        Asserted on the sweep's own return value rather than on the email count:
        the subject rate limit also suppresses a repeat, so an email-count
        assertion would pass even with the dedupe removed.
        """
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.assertEqual(self.runner.audit_silently_closed_issues(), 1)
        self.assertEqual(len(self.emails.messages), 1)
        self.assertEqual(self.runner.audit_silently_closed_issues(), 0,
                         "the re-read overlap must not be reported again")
        self.assertEqual(len(self.emails.messages), 1)

    def test_laterSweepWithANewClose_shouldNameOnlyTheNewIssue(self):
        """Without dedupe the changed subject would re-report the older issue too."""
        old = (datetime.now(timezone.utc) - timedelta(minutes=10)).isoformat()
        self._issue_list([_issue(1363, old)])
        self.runner.audit_silently_closed_issues()
        self._issue_list([_issue(1363, old), _issue(1364, datetime.now(timezone.utc).isoformat())])
        self.assertEqual(self.runner.audit_silently_closed_issues(), 1)
        subject = self.emails.messages[-1][0]
        self.assertIn("#1364", subject)
        self.assertNotIn("#1363", subject)

    def test_searchIndexLag_shouldStillReportACloseTheReadMissedFirstTime(self):
        """The watermark must trail far enough to catch a not-yet-indexed close."""
        closed_at = datetime.now(timezone.utc) - timedelta(minutes=5)
        # First sweep: the search index has not caught up, so the read is empty.
        self._issue_list([])
        self.runner.audit_silently_closed_issues()
        self.assertEqual(self.emails.messages, [])
        # Second sweep: the close is indexed now, and it is still inside the window.
        self._issue_list([_issue(1363, closed_at.isoformat())])
        self.runner.audit_silently_closed_issues()
        self.assertEqual(len(self.emails.messages), 1)

    def test_firstSweep_shouldBoundItsLookbackInsteadOfScanningHistory(self):
        self.runner.audit_silently_closed_issues()
        args = self.run_cmd.calls[0]
        searched = datetime.fromisoformat(args[args.index("--search") + 1].split(">=")[1])
        expected = datetime.now(timezone.utc) - timedelta(minutes=90)
        self.assertEqual(searched.date(), expected.date())

    def test_firstSweep_shouldIgnoreACloseOlderThanTheLookback(self):
        stale = (datetime.now(timezone.utc) - timedelta(minutes=180)).isoformat()
        self._issue_list([_issue(1363, stale)])
        self.assertEqual(self.runner.audit_silently_closed_issues(), 0)
        self.assertEqual(self.emails.messages, [])

    def test_windowShouldTrailNowByTheMargin_soAnUnindexedCloseStaysInScope(self):
        self._issue_list([])
        before = datetime.now(timezone.utc)
        self.runner.audit_silently_closed_issues()
        recorded = datetime.fromisoformat(self._state()["audited_through"])
        margin = self.runner._SILENT_CLOSE_AUDIT_MARGIN_MINUTES
        self.assertGreaterEqual(recorded, before - timedelta(minutes=margin))
        self.assertLessEqual(recorded, datetime.now(timezone.utc) - timedelta(minutes=margin) + timedelta(seconds=5))

    def test_failedDelivery_shouldKeepTheWindowOpenForARetry(self):
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.runner.send_email = lambda subject, body: False
        self.assertEqual(self.runner.audit_silently_closed_issues(), 1)
        self.assertIsNone(self._state(), "a failed send must not clear the window")

    def test_failedDelivery_shouldRetryOnceTheRetryWindowPasses(self):
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.runner.send_email = lambda subject, body: False
        self.runner.audit_silently_closed_issues()
        # Inside the retry window the alert is suppressed rather than re-attempted.
        self.runner.send_email = self.emails
        self.runner.audit_silently_closed_issues()
        self.assertEqual(self.emails.messages, [])
        # Past it, the same issues are retried and the window then advances.
        self._expire_retry_window()
        self.runner.audit_silently_closed_issues()
        self.assertEqual(len(self.emails.messages), 1)
        self.assertIsNotNone(self._state())

    def test_deliveredAlert_shouldAdvanceTheWindow(self):
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.runner.audit_silently_closed_issues()
        self.assertIsNotNone(self._state())

    def test_deliveredAlert_shouldRecordTheReportedNumber(self):
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.runner.audit_silently_closed_issues()
        self.assertIn("1363", self._state()["reported"])

    def test_failedDelivery_shouldNotRecordTheReportedNumber(self):
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.runner.send_email = lambda subject, body: False
        self.runner.audit_silently_closed_issues()
        self.assertIsNone(self._state())

    def test_partialRead_shouldNotReportAndNotAdvanceTheWindow(self):
        self._issue_list([_issue(n, datetime.now(timezone.utc).isoformat())
                          for n in range(self.runner._SILENT_CLOSE_READ_LIMIT)])
        before = datetime.now(timezone.utc)
        self.assertEqual(self.runner.audit_silently_closed_issues(), 0)
        self.assertEqual(self.emails.messages, [])
        recorded = datetime.fromisoformat(self._state()["audited_through"])
        lookback = before - timedelta(minutes=self.runner._SILENT_CLOSE_AUDIT_LOOKBACK_MINUTES)
        self.assertLess(recorded, before - timedelta(minutes=self.runner._SILENT_CLOSE_AUDIT_MARGIN_MINUTES))
        self.assertGreaterEqual(recorded, lookback - timedelta(seconds=5))

    def test_partialRead_shouldCountStrikesAndKeepTheOriginalWindow(self):
        self._issue_list([_issue(n, datetime.now(timezone.utc).isoformat())
                          for n in range(self.runner._SILENT_CLOSE_READ_LIMIT)])
        first = datetime.now(timezone.utc) - timedelta(minutes=90)
        self.runner._save_silent_close_audit(first, {})
        self.runner.audit_silently_closed_issues()
        state = self._state()
        self.assertEqual(state["incomplete_strikes"], 1)
        self.assertEqual(datetime.fromisoformat(state["audited_through"]), first)

    def test_partialReadStrikes_shouldEscalateOnceTheyReachTheThreshold(self):
        self._issue_list([_issue(n, datetime.now(timezone.utc).isoformat())
                          for n in range(self.runner._SILENT_CLOSE_READ_LIMIT)])
        threshold = self.runner._SILENT_CLOSE_INCOMPLETE_STRIKES
        for _ in range(threshold):
            self.runner.audit_silently_closed_issues()
        self.assertEqual(self._state()["incomplete_strikes"], threshold)
        self.assertEqual(len(self.emails.messages), 1)
        self.assertIn("Cannot Read All Closed Issues", self.emails.messages[0][0])

    def test_partialReadStrikes_shouldNotEscalateBelowTheThreshold(self):
        self._issue_list([_issue(n, datetime.now(timezone.utc).isoformat())
                          for n in range(self.runner._SILENT_CLOSE_READ_LIMIT)])
        for _ in range(self.runner._SILENT_CLOSE_INCOMPLETE_STRIKES - 1):
            self.runner.audit_silently_closed_issues()
        self.assertEqual(self.emails.messages, [])

    def test_wholeReadAfterStrikes_shouldClearTheCount(self):
        self._issue_list([_issue(n, datetime.now(timezone.utc).isoformat())
                          for n in range(self.runner._SILENT_CLOSE_READ_LIMIT)])
        self.runner.audit_silently_closed_issues()
        self._issue_list([])
        self.runner.audit_silently_closed_issues()
        self.assertNotIn("incomplete_strikes", self._state())

    def test_wholeReadJustUnderTheLimit_shouldBeComplete(self):
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        sweep = self._sweep(datetime.now(timezone.utc) - timedelta(hours=1))
        self.assertTrue(sweep.complete)

    def test_readAtTheLimit_shouldBeIncomplete(self):
        self._issue_list([_issue(n, datetime.now(timezone.utc).isoformat())
                          for n in range(self.runner._SILENT_CLOSE_READ_LIMIT)])
        sweep = self._sweep(datetime.now(timezone.utc) - timedelta(hours=1))
        self.assertFalse(sweep.complete)

    def _expire_retry_window(self):
        """Age the rate-limit record past the shared cooldown so the retry is due."""
        state_dir = self.root / "state"
        for path in state_dir.glob("rate-*.json"):
            state = json.loads(path.read_text())
            state["last_sent"] = (datetime.now(timezone.utc) - timedelta(hours=25)).isoformat()
            path.write_text(json.dumps(state))


class AuditWindowPersistenceTests(_GuardCase):
    def _write_state(self, payload):
        state_dir = self.root / "state"
        state_dir.mkdir(parents=True, exist_ok=True)
        (state_dir / "silent-close-audit.json").write_text(payload)

    def test_absentState_shouldLoadAsNone(self):
        since, reported = self.runner._load_silent_close_audit()
        self.assertIsNone(since)
        self.assertEqual(reported, {})

    def test_corruptState_shouldLoadAsNoneRatherThanRaise(self):
        self._write_state("{ not json")
        since, _ = self.runner._load_silent_close_audit()
        self.assertIsNone(since)

    def test_wrongShapeState_shouldLoadAsNone(self):
        for payload in ("null", "[]", '"text"', "{}", "7"):
            with self.subTest(payload=payload):
                self._write_state(payload)
                since, _ = self.runner._load_silent_close_audit()
                self.assertIsNone(since)

    def test_naiveStamp_shouldLoadAsNoneRatherThanCrashTheRunner(self):
        """fromisoformat accepts a naive stamp, and comparing it to an aware
        `closedAt` raises TypeError -- which would escape main() and abort the
        whole runner every cycle until a human deleted the file."""
        for stamp in ("2026-10-27T19:38:49", "2026-10-27"):
            with self.subTest(stamp=stamp):
                self._write_state(json.dumps({"audited_through": stamp}))
                since, _ = self.runner._load_silent_close_audit()
                self.assertIsNone(since)

    def test_naiveStamp_shouldNotRaiseOutOfTheSweep(self):
        self._write_state(json.dumps({"audited_through": "2026-10-27T19:38:49"}))
        self._issue_list([_issue(1363, datetime.now(timezone.utc).isoformat())])
        self.assertEqual(self.runner.audit_silently_closed_issues(), 1)

    def test_nonStringStamp_shouldLoadAsNone(self):
        self._write_state(json.dumps({"audited_through": 17}))
        since, _ = self.runner._load_silent_close_audit()
        self.assertIsNone(since)

    def test_reportedWrongShape_shouldLoadAsEmpty(self):
        self._write_state(json.dumps({"audited_through": "2026-10-27T19:38:49Z",
                                      "reported": ["1363"]}))
        since, reported = self.runner._load_silent_close_audit()
        self.assertEqual(reported, {})
        self.assertIsNotNone(since)

    def test_roundTrip_shouldPreserveTheInstant(self):
        moment = datetime(2026, 10, 27, 19, 38, 49, 123456, tzinfo=timezone.utc)
        self.runner._save_silent_close_audit(moment, {})
        since, _ = self.runner._load_silent_close_audit()
        self.assertEqual(since, moment)

    def test_roundTrip_shouldPreserveReportedNumbers(self):
        self.runner._save_silent_close_audit(datetime.now(timezone.utc), {"1363": "2026-10-27T19:00:00+00:00"})
        _, reported = self.runner._load_silent_close_audit()
        self.assertEqual(reported, {"1363": "2026-10-27T19:00:00+00:00"})

    def test_reportedList_shouldStayBoundedKeepingTheNewest(self):
        limit = self.runner._SILENT_CLOSE_REPORTED_MAX
        reported = {str(n): f"2026-10-{n % 28 + 1:02d}T00:00:00+00:00" for n in range(limit + 50)}
        self.runner._save_silent_close_audit(datetime.now(timezone.utc), reported)
        _, stored = self.runner._load_silent_close_audit()
        self.assertEqual(len(stored), limit)


class WarnOnlyConstructionTests(unittest.TestCase):
    """The audit must be unable to mutate what it reports on.

    Asserted against the argv the module can issue, not against prose: the words
    "issue close" appear nowhere in the source, so a text scan would pass on code
    that calls `run_cmd(["gh", "issue", "close", ...])`.
    """

    MUTATING_VERBS = {"close", "reopen", "comment", "edit", "delete", "transfer", "lock"}

    def setUp(self):
        self.tree = ast.parse((BASE / "runner.py").read_text())

    def _gh_issue_list_literals(self):
        """Every list literal in the module that spells a gh issue subcommand."""
        found = []
        for node in ast.walk(self.tree):
            if not isinstance(node, ast.List) or len(node.elts) < 3:
                continue
            head = [e.value for e in node.elts[:3] if isinstance(e, ast.Constant)]
            if len(head) == 3 and head[0] == "gh" and head[1] == "issue":
                found.append(head)
        return found

    def test_module_shouldNeverSpellAMutatingGhIssueSubcommand(self):
        offending = [head for head in self._gh_issue_list_literals() if head[2] in self.MUTATING_VERBS]
        self.assertEqual(offending, [], f"the runner must not mutate issues: {offending}")

    def test_theScanShouldSeeTheReadTheAuditActuallyPerforms(self):
        """Guards the guard: if the scan finds nothing, it proves nothing."""
        reads = [head for head in self._gh_issue_list_literals() if head[2] == "list"]
        self.assertTrue(reads, "the AST scan found no gh issue list at all")

    def test_theScanShouldDetectAnInjectedMutatingCommand(self):
        """A mutation injected in the spelling this codebase actually uses is caught."""
        injected = ast.parse(
            'def f():\n'
            '    run_cmd(["gh", "issue", "close", "1363"], cwd=REPO_PATH)\n'
            '    run_cmd(["gh", "issue", "reopen", "1363"], cwd=REPO_PATH)\n'
        )
        original = self.tree
        try:
            self.tree = injected
            offending = [head for head in self._gh_issue_list_literals()
                         if head[2] in self.MUTATING_VERBS]
            self.assertEqual(len(offending), 2)
        finally:
            self.tree = original


class CallSiteTests(unittest.TestCase):
    """The sweep runs once per process, outside the babysit lock."""

    def setUp(self):
        self.tree = ast.parse((BASE / "runner.py").read_text())

    def _main(self):
        for node in ast.walk(self.tree):
            if isinstance(node, ast.FunctionDef) and node.name == "main":
                return node
        raise AssertionError("main() not found")

    def _is_audit_call(self, node):
        return (isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
                and node.func.id == "audit_silently_closed_issues")

    def test_main_shouldCallTheAuditExactlyOnce(self):
        calls = [n for n in ast.walk(self._main()) if self._is_audit_call(n)]
        self.assertEqual(len(calls), 1)

    def test_main_shouldCallTheAuditOutsideEveryNestedBlock(self):
        top_level = [stmt for stmt in self._main().body
                     if isinstance(stmt, ast.Expr) and self._is_audit_call(stmt.value)]
        self.assertEqual(len(top_level), 1)

    def test_main_shouldCallTheAuditAfterTheBabysitPass(self):
        body = self._main().body
        audit = next(i for i, stmt in enumerate(body)
                     if isinstance(stmt, ast.Expr) and self._is_audit_call(stmt.value))
        babysit = next(i for i, stmt in enumerate(body)
                       if any(isinstance(n, ast.Call) and isinstance(n.func, ast.Name)
                              and n.func.id == "babysit_active_worktrees" for n in ast.walk(stmt)))
        self.assertGreater(audit, babysit)


if __name__ == "__main__":
    unittest.main()
