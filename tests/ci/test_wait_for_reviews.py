"""Regressions for the review-wait gate's bot accounting (tests/wait-for-reviews.sh).

The gate blocks until every expected robot reviewer has either posted a review or
declared a skip, and it gives up after a 20-minute timeout. Codex reports some
reviews as a *summary issue comment* carrying a machine-readable
`codex-review-state:v1` marker instead of a formal review object, and the gate did
not recognise that form. It therefore waited out the entire 20-minute timeout on a
PR Codex had already finished reviewing, and the runner's babysit pass that calls
the gate spent that window doing nothing.

These tests run the real script against a stubbed `gh`, so they exercise the
script's own detection logic rather than a reimplementation of it.
"""
from __future__ import annotations

import json
import os
import stat
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tests" / "wait-for-reviews.sh"

CODEX = "chatgpt-codex-connector[bot]"
CODERABBIT = "coderabbitai[bot]"

# What Codex posts when it finishes a review without findings: an issue comment
# whose hidden marker records the completed review. Observed on PR #1328.
CODEX_SUMMARY_MARKER = (
    "<!-- codex-pull-request-review-summary -->\n"
    "<!-- codex-review-state:v1 "
    '{"code_review":{"commit_sha":"883fe06900d8de9f20480a83e905474673a10eb5",'
    '"has_any_findings":false,"has_unresolved_findings":false,"status":"completed"},'
    '"security_review":{"commit_sha":null,"has_any_findings":null,'
    '"has_unresolved_findings":null,"status":null}} -->\n'
    "## Codex Review Summary\n"
)

CODERABBIT_SKIP = (
    "<!-- coderabbit-ai-generated:v1 -->\n"
    "This repository does not receive automatic reviews because it has fewer than 10 stars.\n"
)


class _StubGh:
    """A fake `gh` serving one reviews list and one issue-comments list."""

    def __init__(self, root: Path, reviews: list[dict], comments: list[dict]):
        self.root = root
        self.bin = root / "bin"
        self.bin.mkdir(parents=True, exist_ok=True)
        (root / "reviews.json").write_text(json.dumps(reviews))
        (root / "comments.json").write_text(json.dumps(comments))
        self.calls = root / "calls"
        gh = self.bin / "gh"
        gh.write_text(
            textwrap.dedent(
                f"""\
                #!/usr/bin/env python3
                import json, sys
                from pathlib import Path
                root = Path({str(root)!r})
                with open(root / "calls", "a") as handle:
                    handle.write(" ".join(sys.argv[1:]) + "\\n")
                args = sys.argv[1:]
                if args[:2] == ["repo", "view"]:
                    print("dwojtaszek/zipper")
                elif args[:2] == ["api", "--paginate"] and args[2].endswith("/reviews"):
                    print((root / "reviews.json").read_text())
                elif args[:2] == ["api", "--paginate"] and args[2].endswith("/comments"):
                    print((root / "comments.json").read_text())
                elif args[0] == "api":
                    # The GraphQL thread query: no unresolved threads.
                    print(json.dumps({{"data": {{"repository": {{"pullRequest":
                        {{"reviewThreads": {{"nodes": []}}}}}}}}}}))
                else:
                    raise SystemExit("unexpected gh call: " + " ".join(args))
                """
            )
        )
        gh.chmod(gh.stat().st_mode | stat.S_IEXEC | stat.S_IXGRP | stat.S_IXOTH)
        self.env = dict(os.environ)
        self.env["PATH"] = f"{self.bin}{os.pathsep}{self.env.get('PATH', '')}"

    def run_gate(self, timeout_minutes: int = 1) -> subprocess.CompletedProcess:
        """Run the real gate against the stub.

        The default is one minute, not the script's 20: a test that fails because a
        bot is not accounted for would otherwise sit in the poll loop for the whole
        real timeout instead of failing promptly.
        """
        return subprocess.run(
            ["bash", str(SCRIPT), "1328", str(timeout_minutes)],
            capture_output=True, text=True, env=self.env, cwd=str(ROOT),
        )


class ReviewWaitGateTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.mkdtemp(prefix="review-gate-test-")
        self.addCleanup(__import__("shutil").rmtree, self._tmp, ignore_errors=True)
        self.root = Path(self._tmp)

    def _gate(self, reviews=None, comments=None) -> _StubGh:
        return _StubGh(self.root, reviews or [], comments or [])

    def test_codexSummaryComment_isAcceptedAsACompletedReview(self):
        """Codex finished on PR #1328 by posting a summary comment with a
        `codex-review-state:v1` marker and no formal review, so the gate waited out
        its full 20-minute timeout on a review that had already happened."""
        stub = self._gate(
            reviews=[],
            comments=[
                {"user": {"login": CODEX}, "body": CODEX_SUMMARY_MARKER},
                {"user": {"login": CODERABBIT}, "body": CODERABBIT_SKIP},
            ],
        )

        result = stub.run_gate()

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("has reviewed or declared a skip", result.stdout)
        self.assertNotIn("did not respond within", result.stdout,
                         "the gate must not fall through to its timeout warning")

    def test_codexMarkerForAnotherCommit_isStillAccountedFor(self):
        """The marker names the reviewed commit. The gate's contract is that the bot
        has responded, and a stale marker is a thread-level problem (branch
        protection blocks on unresolved threads), not a reason to wait 20 minutes."""
        other = CODEX_SUMMARY_MARKER.replace("883fe06900d8de9f20480a83e905474673a10eb5", "f" * 40)
        stub = self._gate(
            reviews=[],
            comments=[
                {"user": {"login": CODEX}, "body": other},
                {"user": {"login": CODERABBIT}, "body": CODERABBIT_SKIP},
            ],
        )

        result = stub.run_gate()

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn("did not respond within", result.stdout)

    def test_unrecognisedCodexComment_isNotTreatedAsAReview(self):
        """The marker must be required, not any comment by the bot: a stray comment
        is not evidence that a review ran."""
        stub = self._gate(
            reviews=[],
            comments=[
                {"user": {"login": CODEX}, "body": "Thanks, looking into it."},
                {"user": {"login": CODERABBIT}, "body": CODERABBIT_SKIP},
            ],
        )

        result = stub.run_gate(timeout_minutes=0)

        self.assertIn("did not respond within", result.stdout,
                      "a comment without the completion marker is not a completed review")

    def test_formalReviewObject_isStillAccepted(self):
        """The existing path must keep working: a real review object counts."""
        stub = self._gate(
            reviews=[
                {"user": {"login": CODEX}, "state": "COMMENTED", "body": ""},
                {"user": {"login": CODERABBIT}, "state": "COMMENTED", "body": ""},
            ],
            comments=[],
        )

        result = stub.run_gate()

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn("did not respond within", result.stdout)

    def test_codexMarkerInAReviewBody_isAlsoAccepted(self):
        """Codex has posted the marker as a review body too, so both carriers count."""
        stub = self._gate(
            reviews=[{"user": {"login": CODEX}, "state": "COMMENTED", "body": CODEX_SUMMARY_MARKER}],
            comments=[{"user": {"login": CODERABBIT}, "body": CODERABBIT_SKIP}],
        )

        result = stub.run_gate()

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn("did not respond within", result.stdout)


if __name__ == "__main__":
    unittest.main()
