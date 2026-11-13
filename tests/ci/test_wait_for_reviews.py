"""Run the real review gate against controlled GitHub responses.

Codex PR review is retired. CodeRabbit accounting, bounded activity waits,
current-head validation, and unresolved threads from every author remain gates.
"""
from __future__ import annotations

import json
import os
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tests" / "wait-for-reviews.sh"
HEAD_SHA = "883fe06900d8de9f20480a83e905474673a10eb5"
CODEX = "chatgpt-codex-connector[bot]"
CODERABBIT = "coderabbitai[bot]"
CODERABBIT_SKIP = "This repository does not receive automatic reviews because it has fewer than 10 stars."


class _StubGh:
    def __init__(self, root, reviews=None, comments=None, fail=None, threads=None,
                 threads_shape=None, final_head=None, issue_comments=None, issue_comment_pages=None):
        self.root = root
        bin_dir = root / "bin"
        bin_dir.mkdir()
        for review in reviews or []:
            review.setdefault("commit_id", HEAD_SHA)
            review.setdefault("state", "COMMENTED")
        pages = issue_comment_pages or [issue_comments or []]
        has_more = len(pages) > 1
        pr_data = {
            "reviewThreads": {"nodes": threads or []},
            "comments": {
                "pageInfo": {"hasNextPage": has_more, "endCursor": "cursor-1" if has_more else None},
                "nodes": pages[0],
            },
        }
        payloads = {
            "reviews": reviews or [], "comments": comments or [],
            "threads": threads_shape if threads_shape is not None else {
                "data": {"repository": {"pullRequest": pr_data}}},
        }
        for idx, page in enumerate(pages[1:], start=1):
            next_more = idx + 1 < len(pages)
            payloads[f"threads-page-{idx}"] = {
                "data": {"repository": {"pullRequest": {"comments": {
                    "pageInfo": {"hasNextPage": next_more, "endCursor": f"cursor-{idx + 1}" if next_more else None},
                    "nodes": page,
                }}}}
            }
        for name, payload in payloads.items():
            (root / f"{name}.json").write_text(json.dumps(payload))
        gh = bin_dir / "gh"
        gh.write_text(textwrap.dedent(f"""\
            #!/usr/bin/env python3
            import sys
            from pathlib import Path
            root = Path({str(root)!r})
            args = sys.argv[1:]
            with (root / 'calls').open('a') as calls:
                calls.write(' '.join(args) + '\\n')
            if args[:2] == ['repo', 'view']:
                print('dwojtaszek/zipper')
            elif args[:2] == ['pr', 'view']:
                if {fail!r} == 'head':
                    raise SystemExit(1)
                count = root / 'head-count'
                seen = int(count.read_text()) if count.exists() else 0
                count.write_text(str(seen + 1))
                print({final_head!r} if seen > 0 and {final_head!r} else {HEAD_SHA!r})
            elif args[:2] == ['api', '--paginate']:
                name = args[2].rsplit('/', 1)[1]
                if name == {fail!r}:
                    raise SystemExit(1)
                print((root / (name + '.json')).read_text())
            elif args[:2] == ['api', 'graphql']:
                if {fail!r} == 'threads':
                    raise SystemExit(1)
                after = [a.split('=', 1)[1] for a in args if a.startswith('after=')]
                if after:
                    if {fail!r} == 'comments_page':
                        raise SystemExit(1)
                    idx = after[0].split('-', 1)[1]
                    print((root / f'threads-page-{{idx}}.json').read_text())
                else:
                    print((root / 'threads.json').read_text())
            else:
                raise SystemExit('unexpected gh call: ' + ' '.join(args))
            """))
        gh.chmod(0o755)
        self.env = dict(os.environ, PATH=f"{bin_dir}{os.pathsep}{os.environ['PATH']}",
                        STATE_DIR=str(root / "review-state"))

    def run_gate(self, skip=None):
        env = dict(self.env)
        if skip is not None:
            env["WAIT_FOR_REVIEWS_SKIP"] = skip
        return subprocess.run(["bash", str(SCRIPT), "1328", "0"], cwd=ROOT,
                              capture_output=True, text=True, env=env, timeout=15)


class ReviewWaitGateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="review-gate-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def _gate(self, **kwargs):
        return _StubGh(self.root, **kwargs)

    def assert_outcome(self, result, state, code=0):
        self.assertEqual(result.returncode, code, result.stdout + result.stderr)
        self.assertIn(f"REVIEWER-OUTCOME {CODERABBIT}={state}", result.stdout)

    def test_removed_codex_review_never_requests_or_waits_for_refresh(self):
        stub = self._gate(
            reviews=[{"user": {"login": CODEX}, "state": "APPROVED", "commit_id": "f" * 40}],
            comments=[{"user": {"login": CODERABBIT}, "body": CODERABBIT_SKIP}])
        result = stub.run_gate()
        self.assert_outcome(result, "skipped")
        self.assertNotIn("REVIEW-REQUEST", result.stdout)
        self.assertNotIn(f"REVIEWER-OUTCOME {CODEX}", result.stdout)

    def test_formal_current_head_review_counts_as_reviewed(self):
        for state in ("APPROVED", "COMMENTED", "CHANGES_REQUESTED"):
            with self.subTest(state=state), tempfile.TemporaryDirectory() as directory:
                stub = _StubGh(Path(directory), reviews=[{
                    "user": {"login": CODERABBIT}, "state": state,
                    "body": "Review completed; ordinary prose is not a protocol."}])
                self.assert_outcome(stub.run_gate(), "reviewed")

    def test_stale_or_dismissed_review_is_not_review_credit(self):
        for review in ({"commit_id": "f" * 40}, {"state": "DISMISSED"}):
            with self.subTest(review=review), tempfile.TemporaryDirectory() as directory:
                stub = _StubGh(Path(directory), reviews=[{"user": {"login": CODERABBIT}, **review}])
                self.assert_outcome(stub.run_gate(), "silent")

    def test_unrecognised_comment_is_not_a_completed_review(self):
        stub = self._gate(comments=[{"user": {"login": CODERABBIT}, "body": "Thanks, looking into it."}])
        self.assert_outcome(stub.run_gate(), "silent")

    def test_skip_must_be_current_head_or_standing_repository_refusal(self):
        for body, state in (("Review skipped: rate limit", "silent"),
                            ("Review skipped: rate limit " + HEAD_SHA, "skipped"),
                            (CODERABBIT_SKIP, "skipped")):
            with self.subTest(body=body), tempfile.TemporaryDirectory() as directory:
                stub = _StubGh(Path(directory), comments=[{"user": {"login": CODERABBIT}, "body": body}])
                self.assert_outcome(stub.run_gate(), state)

    def test_outsider_cannot_declare_a_reviewer_skip(self):
        stub = self._gate(comments=[{"user": {"login": "outsider"}, "body": CODERABBIT_SKIP + HEAD_SHA}])
        self.assert_outcome(stub.run_gate(), "silent")

    def test_current_activity_overrides_historical_skip(self):
        stub = self._gate(reviews=[{"user": {"login": CODERABBIT}, "state": "PENDING"}],
                          comments=[{"user": {"login": CODERABBIT}, "body": CODERABBIT_SKIP}])
        self.assert_outcome(stub.run_gate(), "pending", code=4)

    def test_terminal_skip_must_be_newer_than_current_activity(self):
        for timestamp, review_id, state, code in (
            ("2026-11-11T00:00:00Z", 10, "skipped", 0),
            ("2026-11-11T00:00:02Z", 10, "pending", 4),
            ("2026-11-11T00:00:01Z", 10, "skipped", 0),
            ("2026-11-11T00:00:01Z", 30, "pending", 4),
        ):
            with self.subTest(timestamp=timestamp, review_id=review_id), tempfile.TemporaryDirectory() as directory:
                stub = _StubGh(Path(directory), reviews=[{
                    "user": {"login": CODERABBIT}, "state": "PENDING",
                    "id": review_id, "submitted_at": timestamp,
                }], comments=[{
                    "user": {"login": CODERABBIT}, "id": 20,
                    "created_at": "2026-11-11T00:00:01Z",
                    "body": "Review skipped: rate limit " + HEAD_SHA,
                }])
                self.assert_outcome(stub.run_gate(), state, code=code)

    def test_large_comment_payload_cannot_hide_current_activity(self):
        stub = self._gate(reviews=[{"user": {"login": CODERABBIT}, "state": "PENDING"}],
                          comments=[{"user": {"login": "maintainer"}, "body": "x" * 50000} for _ in range(3)])
        self.assert_outcome(stub.run_gate(), "pending", code=4)

    def test_abandoned_current_activity_has_a_bounded_persistent_window(self):
        stub = self._gate(reviews=[{"user": {"login": CODERABBIT}, "state": "PENDING"}])
        waiting = Path(stub.env["STATE_DIR"]) / "review-waits"
        waiting.mkdir(parents=True)
        first = stub.run_gate()
        self.assert_outcome(first, "pending", code=4)
        record = waiting / f"1328-{HEAD_SHA}-{CODERABBIT}.started"
        self.assertTrue(record.exists())
        record.write_text("1\n")
        self.assert_outcome(stub.run_gate(), "silent")

    def test_diagnostic_wait_does_not_persist_activity(self):
        stub = self._gate(reviews=[{"user": {"login": CODERABBIT}, "state": "PENDING"}])
        stub.env["WAIT_FOR_REVIEWS_DRY_RUN"] = "1"
        self.assert_outcome(stub.run_gate(), "pending", code=4)
        self.assertFalse(Path(stub.env["STATE_DIR"]).exists())

    def test_known_unavailable_reviewer_is_not_polled(self):
        stub = self._gate(fail="reviews")
        self.assert_outcome(stub.run_gate(skip=CODERABBIT), "skipped-by-policy")
        calls = (self.root / "calls").read_text()
        self.assertNotIn("api --paginate", calls)
        self.assertIn("api graphql", calls)

    def test_all_authors_unresolved_threads_still_block(self):
        for author in (CODERABBIT, CODEX, "human-reviewer"):
            with self.subTest(author=author), tempfile.TemporaryDirectory() as directory:
                stub = _StubGh(Path(directory), threads=[{"isResolved": False, "comments": {"nodes": [
                    {"author": {"login": author}, "path": "a.py", "line": 3, "body": "Fix this"}]}}])
                result = stub.run_gate(skip=CODERABBIT)
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertIn("REVIEW-GATE-RESULT unresolved=1", result.stdout)

    def test_resolved_threads_report_zero(self):
        stub = self._gate(threads=[{"isResolved": True, "comments": {"nodes": []}}])
        result = stub.run_gate(skip=CODERABBIT)
        self.assert_outcome(result, "skipped-by-policy")
        self.assertIn("REVIEW-GATE-RESULT unresolved=0", result.stdout)

    def test_failed_or_malformed_responses_fail_closed(self):
        partial_null_comments = {
            "data": {"repository": {"pullRequest": {
                "reviewThreads": {"nodes": []},
                "comments": None,
            }}},
            "errors": [{"message": "comments timed out"}],
        }
        omitted_comments = {
            "data": {"repository": {"pullRequest": {
                "reviewThreads": {"nodes": []},
            }}},
        }
        missing_has_next = {
            "data": {"repository": {"pullRequest": {
                "reviewThreads": {"nodes": []},
                "comments": {"pageInfo": {}, "nodes": []},
            }}},
        }
        for fail, shape in (
            ("head", None),
            ("reviews", None),
            ("comments", None),
            ("threads", None),
            ("shape", {"errors": []}),
            ("partial_comments", partial_null_comments),
            ("omitted_comments", omitted_comments),
            ("missing_has_next", missing_has_next),
        ):
            with self.subTest(fail=fail), tempfile.TemporaryDirectory() as directory:
                stub = _StubGh(Path(directory), fail=fail, threads_shape=shape,
                               reviews=[{"user": {"login": CODERABBIT}}])
                result = stub.run_gate()
                self.assertEqual(result.returncode, 3, result.stdout + result.stderr)

    def test_head_change_invalidates_result_even_when_wait_is_skipped(self):
        stub = self._gate(final_head="f" * 40)
        self.assertEqual(stub.run_gate(skip=CODERABBIT).returncode, 3)

    def test_qodo_current_head_findings_block_even_when_coderabbit_skipped(self):
        qodo_body = (
            "<h3>Code Review by Qodo</h3>\n"
            "<img alt='Action required'>\n"
            "<details><summary>  1.  Null guard missing <code>Bug</code></summary></details>\n"
            "<details><summary>  2.  Edge test missing <code>Gap</code></summary></details>\n"
            f"<!-- https://github.com/dwojtaszek/zipper/commit/{HEAD_SHA} -->"
        )
        stub = self._gate(issue_comments=[{
            "author": {"login": "qodo-code-review"},
            "authorAssociation": "NONE",
            "body": qodo_body,
        }])
        result = stub.run_gate(skip=CODERABBIT)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("REVIEW-GATE-RESULT unresolved=2", result.stdout)
        self.assertIn("1.  Null guard missing Bug", result.stdout)

    def test_qodo_clean_or_stale_or_acknowledged_findings_pass(self):
        clean_body = (
            "<h3>Code Review by Qodo</h3>\n"
            "<h3>Great, no issues found!</h3>\n"
            f"<!-- https://github.com/dwojtaszek/zipper/commit/{HEAD_SHA} -->"
        )
        stale_body = (
            "<h3>Code Review by Qodo</h3>\n"
            "<img alt='Action required'>\n"
            "<details><summary>  1.  Old issue</summary></details>\n"
            "<!-- https://github.com/dwojtaszek/zipper/commit/" + ("f" * 40) + " -->"
        )
        current_findings = (
            "<h3>Code Review by Qodo</h3>\n"
            "<img alt='Remediation recommended'>\n"
            "<details><summary>  1.  Medium note</summary></details>\n"
            "<details><summary>  2.  Second note</summary></details>\n"
            f"<!-- https://github.com/dwojtaszek/zipper/commit/{HEAD_SHA} -->"
        )
        ack_all = {
            "author": {"login": "dwojtaszek"},
            "authorAssociation": "OWNER",
            "body": f"qodo-ack: {HEAD_SHA[:7]} all — covered in existing tests.",
        }
        ack_partial = {
            "author": {"login": "dwojtaszek"},
            "authorAssociation": "OWNER",
            "body": f"qodo-ack: {HEAD_SHA[:7]} #1 — covered; fixing #2 next.",
        }
        contributor_ack = {
            "author": {"login": "someone-else"},
            "authorAssociation": "CONTRIBUTOR",
            "body": f"qodo-ack: {HEAD_SHA[:7]} all",
        }
        prose_only = {
            "author": {"login": "dwojtaszek"},
            "authorAssociation": "OWNER",
            "body": f"Qodo on {HEAD_SHA[:7]} still needs fixes.",
        }
        fenced_example = {
            "author": {"login": "dwojtaszek"},
            "authorAssociation": "OWNER",
            "body": (
                f"Do NOT run:\n```\nqodo-ack: {HEAD_SHA[:7]} all\n```\n"
                f"~~~\nqodo-ack: {HEAD_SHA[:7]} all\n~~~\n"
                f"    qodo-ack: {HEAD_SHA[:7]} all\n"
                f"> qodo-ack: {HEAD_SHA[:7]} all"
            ),
        }
        updated_earlier_qodo_beats_clean = [
            {
                "author": {"login": "qodo-code-review"},
                "authorAssociation": "NONE",
                "createdAt": "2026-11-13T08:00:00Z",
                "updatedAt": "2026-11-13T08:10:00Z",
                "body": current_findings,
            },
            {
                "author": {"login": "qodo-code-review"},
                "authorAssociation": "NONE",
                "createdAt": "2026-11-13T08:05:00Z",
                "updatedAt": "2026-11-13T08:05:00Z",
                "body": clean_body,
            },
        ]
        updated_qodo_after_ack = [
            {
                "author": {"login": "qodo-code-review"},
                "authorAssociation": "NONE",
                "createdAt": "2026-11-13T08:00:00Z",
                "updatedAt": "2026-11-13T08:10:00Z",
                "body": current_findings,
            },
            {
                "author": {"login": "dwojtaszek"},
                "authorAssociation": "OWNER",
                "createdAt": "2026-11-13T08:05:00Z",
                "updatedAt": "2026-11-13T08:05:00Z",
                "body": f"qodo-ack: {HEAD_SHA[:7]} all",
            },
        ]
        for name, comments, expected_code, expected_unresolved in (
            ("clean", [{"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": clean_body}], 0, 0),
            ("stale", [{"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": stale_body}], 0, 0),
            ("acked_all", [
                {"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": current_findings},
                ack_all,
            ], 0, 0),
            ("acked_partial_keeps_remaining", [
                {"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": current_findings},
                ack_partial,
            ], 1, 1),
            ("contributor_cannot_ack", [
                {"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": current_findings},
                contributor_ack,
            ], 1, 2),
            ("prose_cannot_ack", [
                {"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": current_findings},
                prose_only,
            ], 1, 2),
            ("fenced_or_quoted_cannot_ack", [
                {"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": current_findings},
                fenced_example,
            ], 1, 2),
            ("qodo_updated_after_ack_stays_blocked", updated_qodo_after_ack, 1, 2),
            ("updated_earlier_qodo_beats_later_clean", updated_earlier_qodo_beats_clean, 1, 2),
        ):
            with self.subTest(case=name), tempfile.TemporaryDirectory() as directory:
                stub = _StubGh(Path(directory), issue_comments=comments)
                result = stub.run_gate(skip=CODERABBIT)
                self.assertEqual(result.returncode, expected_code, result.stdout + result.stderr)
                self.assertIn(f"REVIEW-GATE-RESULT unresolved={expected_unresolved}", result.stdout)

    def test_qodo_paginated_comments_reads_all_pages(self):
        page1 = [{"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": (
            "<h3>Code Review by Qodo</h3>\n"
            "<img alt='Action required'>\n"
            "<details><summary>  1.  Page one finding</summary></details>\n"
            f"<!-- https://github.com/dwojtaszek/zipper/commit/{HEAD_SHA} -->"
        )}]
        page2_noise = [{"author": {"login": "github-actions"}, "authorAssociation": "CONTRIBUTOR", "body": "noise"}]
        page2_ack = [{"author": {"login": "dwojtaszek"}, "authorAssociation": "OWNER", "body": f"qodo-ack: {HEAD_SHA[:7]} #1"}]

        with tempfile.TemporaryDirectory() as directory:
            stub = _StubGh(Path(directory), issue_comment_pages=[page1, page2_noise])
            result = stub.run_gate(skip=CODERABBIT)
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertIn("REVIEW-GATE-RESULT unresolved=1", result.stdout)

        with tempfile.TemporaryDirectory() as directory:
            stub = _StubGh(Path(directory), issue_comment_pages=[page1, page2_ack])
            result = stub.run_gate(skip=CODERABBIT)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("REVIEW-GATE-RESULT unresolved=0", result.stdout)

        with tempfile.TemporaryDirectory() as directory:
            stub = _StubGh(Path(directory), issue_comment_pages=[page1, page2_noise], fail="comments_page")
            result = stub.run_gate(skip=CODERABBIT)
            self.assertEqual(result.returncode, 3, result.stdout + result.stderr)

        # Prefix length between 8 and 39 chars works, while a number on the next line is not matched.
        prefix12 = HEAD_SHA[:12].upper()
        multiline_ack = [{"author": {"login": "dwojtaszek"}, "authorAssociation": "OWNER",
                          "body": f"qodo-ack: {prefix12} #1\n2 remaining items"}]
        page1_two = [{"author": {"login": "qodo-code-review"}, "authorAssociation": "NONE", "body": (
            "<h3>Code Review by Qodo</h3>\n<img alt='Action required'>\n"
            "<details><summary>  1.  One</summary></details>\n"
            "<details><summary>  2.  Two</summary></details>\n"
            f"<!-- https://github.com/dwojtaszek/zipper/commit/{HEAD_SHA} -->"
        )}]
        with tempfile.TemporaryDirectory() as directory:
            stub = _StubGh(Path(directory), issue_comments=page1_two + multiline_ack)
            result = stub.run_gate(skip=CODERABBIT)
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertIn("REVIEW-GATE-RESULT unresolved=1", result.stdout)

        with tempfile.TemporaryDirectory() as directory:
            stub = _StubGh(Path(directory), issue_comments=page1_two + [{"author": {"login": "dwojtaszek"}, "authorAssociation": "OWNER", "body": f"qodo-ack: {HEAD_SHA[:7]} all"}])
            stub.env["WAIT_FOR_REVIEWS_ALLOW_QODO_ACK"] = "0"
            result = stub.run_gate(skip=CODERABBIT)
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertIn("REVIEW-GATE-RESULT unresolved=2", result.stdout)


if __name__ == "__main__":
    unittest.main()
