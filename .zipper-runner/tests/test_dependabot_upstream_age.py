"""Unit tests for Dependabot 3-day upstream age policy enforcement.

Tests:
1. Parsing updated dependencies from PR body, commits, and titles (single and grouped).
2. Resolving upstream publish timestamps for NuGet packages and GitHub Actions.
3. Enforcing 3-day minimum age policy:
   - Upstream packages >= 3 days old are eligible.
   - Upstream packages < 3 days old are blocked.
   - Grouped updates are evaluated individually; any < 3 day member blocks the group.
4. Explicit and auditable security-update authorization exceptions:
   - Valid recorded security authorization in STATE_DIR allows bypass.
   - Tampered/mismatched head or unauthorized author fails closed.
   - Unrecorded security updates do not accidentally bypass.
5. Integration with babysit_dependabot_prs auto-merge decision flow and logging.
"""

from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import MagicMock, patch

import gitenv  # noqa: F401

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402


class DependabotUpstreamAgeTests(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp(prefix="zipper-runner-test-")
        self._orig_state_dir = runner.STATE_DIR
        self._orig_dry_run = runner.DRY_RUN
        runner.STATE_DIR = os.path.join(self.test_dir, "state")
        os.makedirs(runner.STATE_DIR, exist_ok=True)
        self.now = datetime(2026, 10, 24, 12, 0, 0, tzinfo=timezone.utc)

    def tearDown(self):
        runner.STATE_DIR = self._orig_state_dir
        runner.DRY_RUN = self._orig_dry_run
        shutil.rmtree(self.test_dir, ignore_errors=True)

    def _create_dependabot_pr(self, number=101, name="Foo", version="1.1", title=None, reviews=None):
        if title is None:
            title = f"deps: bump {name} from 1.0 to {version}"
        return {
            "number": number,
            "title": title,
            "body": f"---\nupdated-dependencies:\n- dependency-name: {name}\n  dependency-version: {version}\n...",
            "headRefName": f"dependabot/nuget/{name.lower()}-{version}",
            "author": {"login": "dependabot[bot]"},
            "statusCheckRollup": [{"__typename": "CheckRun", "name": "ci-gate", "status": "COMPLETED", "conclusion": "SUCCESS"}],
            "reviews": reviews if reviews is not None else [],
            "commits": [],
        }

    def _setup_mock_bot_runner(self, pr, mock_auth, mock_run_cmd, mock_merge):
        mock_auth.return_value = {
            "number": pr["number"],
            "user": {"login": "dependabot[bot]", "id": 49699333, "type": "Bot"},
            "head": {"sha": f"sha{pr['number']}", "ref": pr["headRefName"]},
        }
        def fake_run_cmd(cmd, *args, **kwargs):
            if cmd[:2] == ["gh", "pr"] and cmd[2] == "list":
                return (0, json.dumps([pr]), "")
            if cmd[0] == "bash":
                return (0, "review passed", "")
            return (0, "", "")

        mock_run_cmd.side_effect = fake_run_cmd
        mock_merge.return_value = (0, "merged", "")

    def test_parse_updated_dependencies_grouped_yaml(self):
        body = """
Bumps the nuget-minor-patch group with 2 updates.

---
updated-dependencies:
- dependency-name: FsCheck.Xunit
  dependency-version: 3.6.0
  dependency-type: direct:production
  update-type: version-update:semver-minor
  dependency-group: nuget-minor-patch
- dependency-name: Meziantou.Analyzer
  dependency-version: 3.0.316
  dependency-type: direct:production
  update-type: version-update:semver-patch
  dependency-group: nuget-minor-patch
...
"""
        deps = runner._parse_dependabot_updated_dependencies(body)
        self.assertEqual(len(deps), 2)
        self.assertEqual(deps[0]["name"], "FsCheck.Xunit")
        self.assertEqual(deps[0]["version"], "3.6.0")
        self.assertEqual(deps[1]["name"], "Meziantou.Analyzer")
        self.assertEqual(deps[1]["version"], "3.0.316")

    def test_parse_updated_dependencies_single_yaml_and_fallback(self):
        body = """
Bumps [actions/checkout](https://github.com/actions/checkout) from 3 to 4.2.0.

---
updated-dependencies:
- dependency-name: actions/checkout
  dependency-version: 4.2.0
...
"""
        deps = runner._parse_dependabot_updated_dependencies(body)
        self.assertEqual(len(deps), 1)
        self.assertEqual(deps[0]["name"], "actions/checkout")
        self.assertEqual(deps[0]["version"], "4.2.0")

        # Fallback from title when body has no YAML
        fallback = runner._parse_dependabot_updated_dependencies("", title="deps(nuget): bump Foo.Bar from 1.0.0 to 1.2.0")
        self.assertEqual(len(fallback), 1)
        self.assertEqual(fallback[0]["name"], "Foo.Bar")
        self.assertEqual(fallback[0]["version"], "1.2.0")

        # Fallback from body with markdown link and trailing period
        body_linked = "Bumps [actions/checkout](https://github.com/actions/checkout) from 3 to 4.2.0."
        fallback_linked = runner._parse_dependabot_updated_dependencies(body_linked)
        self.assertEqual(len(fallback_linked), 1)
        self.assertEqual(fallback_linked[0]["name"], "actions/checkout")
        self.assertEqual(fallback_linked[0]["version"], "4.2.0")

        # Changelog / release note lines in body do not get falsely extracted
        body_release_notes = """
Release Notes:
- Included commit: bump legacy-pkg from 0.1 to 0.2
- Notes: We decided to bump older-lib from 1.0 to 2.0 in last sprint.
"""
        fallback_notes = runner._parse_dependabot_updated_dependencies(body_release_notes)
        self.assertEqual(len(fallback_notes), 0)

    def test_parse_updated_dependencies_unquoted_version_scalar_preserved(self):
        body = """
---
updated-dependencies:
- dependency-name: Foo.Bar
  dependency-version: 2.10
...
"""
        deps = runner._parse_dependabot_updated_dependencies(body)
        self.assertEqual(len(deps), 1)
        self.assertEqual(deps[0]["name"], "Foo.Bar")
        self.assertEqual(deps[0]["version"], "2.10")

    def test_resolve_dependency_ecosystem(self):
        self.assertEqual(runner._resolve_dependency_ecosystem("Foo", "dependabot/nuget/foo-1.0"), "nuget")
        self.assertEqual(runner._resolve_dependency_ecosystem("actions/checkout", "dependabot/github_actions/actions-checkout-4"), "github-actions")
        self.assertEqual(runner._resolve_dependency_ecosystem("actions/setup-node", "dependabot/github-actions/actions-setup-node-4"), "github-actions")
        self.assertEqual(runner._resolve_dependency_ecosystem("express", "dependabot/npm_and_yarn/express-4.18"), "npm")
        self.assertEqual(runner._resolve_dependency_ecosystem("actions/checkout", "custom-branch"), "github-actions")
        self.assertEqual(runner._resolve_dependency_ecosystem("Newtonsoft.Json", "custom-branch"), "nuget")

    @patch("runner.urllib.request.urlopen")
    def test_fetch_nuget_publish_timestamp_direct_and_index_fallback(self, mock_urlopen):
        # Direct version hit
        mock_resp = MagicMock()
        mock_resp.read.return_value = json.dumps({"published": "2026-10-20T10:00:00Z"}).encode()
        mock_resp.__enter__.return_value = mock_resp
        mock_urlopen.return_value = mock_resp

        ts = runner._fetch_upstream_publish_timestamp("FsCheck.Xunit", "3.6.0", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 20, 10, 0, 0, tzinfo=timezone.utc))

        # Direct 404 falling back to index
        import urllib.error
        mock_urlopen.side_effect = [
            urllib.error.HTTPError("https://api.nuget.org", 404, "Not Found", {}, None),
            MagicMock(__enter__=lambda s: MagicMock(read=lambda: json.dumps({
                "items": [{"items": [{"catalogEntry": {"version": "3.6.0", "published": "2026-10-19T08:00:00Z"}}]}]
            }).encode()))
        ]
        ts_fallback = runner._fetch_upstream_publish_timestamp("FsCheck.Xunit", "3.6.0", "nuget")
        self.assertEqual(ts_fallback, datetime(2026, 10, 19, 8, 0, 0, tzinfo=timezone.utc))

        # HTTP error other than 404 aborts immediately without attempting index fallback
        mock_urlopen.side_effect = urllib.error.HTTPError("https://api.nuget.org", 500, "Internal Server Error", {}, None)
        ts_err = runner._fetch_upstream_publish_timestamp("FsCheck.Xunit", "3.6.0", "nuget")
        self.assertIsNone(ts_err)

    @patch("runner.run_cmd")
    def test_fetch_github_actions_publish_timestamp_release_hit_and_unverified_none(self, mock_run_cmd):
        # Hit on release tag
        mock_run_cmd.return_value = (0, json.dumps({"published_at": "2026-10-18T12:00:00Z"}), "")
        ts = runner._fetch_upstream_publish_timestamp("actions/checkout", "4.2.0", "github-actions")
        self.assertEqual(ts, datetime(2026, 10, 18, 12, 0, 0, tzinfo=timezone.utc))

        # Tag without release -> None (fail closed)
        mock_run_cmd.side_effect = [
            (1, "", "Not Found"),  # v4 tag release 404
            (1, "", "Not Found"),  # 4 tag release 404
        ]
        ts_none = runner._fetch_upstream_publish_timestamp("actions/checkout", "4", "github-actions")
        self.assertIsNone(ts_none)

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_check_upstream_age_single_eligible_and_blocked(self, mock_fetch):
        # Older than 3 days -> eligible
        mock_fetch.return_value = self.now - timedelta(days=4)
        pr = self._create_dependabot_pr(number=101, name="Foo", version="1.1")
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha101", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("eligible", msg.lower())

        # Freshly published (< 3 days, e.g. 1.5 days) -> blocked
        mock_fetch.return_value = self.now - timedelta(days=1.5)
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha101", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("too new", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_check_upstream_age_exact_boundary(self, mock_fetch):
        pr = self._create_dependabot_pr(number=101, name="Foo", version="1.1")
        # Exact boundary: exactly 3.0 days (259200 seconds) -> eligible
        mock_fetch.return_value = self.now - timedelta(days=3.0)
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha101", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("eligible", msg.lower())

        # Just under boundary: 2.999 days -> blocked
        mock_fetch.return_value = self.now - timedelta(days=2.999)
        eligible_under, msg_under = runner._check_dependabot_upstream_age(pr, "sha101", now=self.now)
        self.assertFalse(eligible_under)
        self.assertIn("too new", msg_under.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_check_upstream_age_grouped_updates_member_blocks_group(self, mock_fetch):
        pr = {
            "number": 102,
            "title": "deps: bump nuget-minor-patch group",
            "body": """
---
updated-dependencies:
- dependency-name: PkgA
  dependency-version: 2.0.0
- dependency-name: PkgB
  dependency-version: 3.1.0
...
""",
            "headRefName": "dependabot/nuget/nuget-minor-patch",
            "reviews": [],
            "commits": [],
        }

        # Case 1: Both members >= 3 days old -> Eligible
        mock_fetch.side_effect = [
            self.now - timedelta(days=5),
            self.now - timedelta(days=3.5),
        ]
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha102", now=self.now)
        self.assertTrue(eligible)

        # Case 2: PkgA is 10 days old, but PkgB is only 1 day old -> Entire group blocked
        mock_fetch.side_effect = [
            self.now - timedelta(days=10),
            self.now - timedelta(days=1.0),
        ]
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha102", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("PkgB", msg)
        self.assertIn("too new", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_security_exception_explicit_and_auditable(self, mock_fetch):
        # A freshly published package (< 3 days)
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=103, name="vulnerable-lib", version="2.0.1",
            title="deps: bump vulnerable-lib to fix CVE-2026-9999",
        )

        # Without recorded authorization, a security update does NOT accidentally bypass
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha103", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("too new", msg.lower())

        # With explicit recorded authorization in STATE_DIR by a trusted author
        runner._record_dependabot_security_exception(
            pr_number=103,
            head_sha="sha103",
            authorized_by="dwojtaszek",
            reason="CVE-2026-9999 emergency patch",
        )
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha103", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("security exception", msg.lower())
        self.assertIn("dwojtaszek", msg)

        # Tampered authorization: head sha moved -> Fails closed
        eligible_drift, msg_drift = runner._check_dependabot_upstream_age(pr, "sha103-modified", now=self.now)
        self.assertFalse(eligible_drift)

        # Tampered authorization: unauthorized author -> Fails closed
        runner._record_dependabot_security_exception(
            pr_number=103,
            head_sha="sha103",
            authorized_by="untrusted-attacker",
            reason="CVE bypass",
        )
        eligible_spoof, _ = runner._check_dependabot_upstream_age(pr, "sha103", now=self.now)
        self.assertFalse(eligible_spoof)

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_unreachable_registry_fails_closed(self, mock_fetch):
        mock_fetch.return_value = None  # Failed to fetch or network error
        pr = self._create_dependabot_pr(number=104, name="Foo", version="1.1")
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha104", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("could not verify", msg.lower())

    def test_check_upstream_age_no_dependencies_fails_closed(self):
        pr = {
            "number": 105,
            "title": "unparseable dependabot pr title",
            "body": "no updated dependencies metadata",
            "headRefName": "dependabot/nuget/foo",
            "reviews": [],
            "commits": [],
        }
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha105", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("could not determine", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_security_exception_maintainer_review_authorization(self, mock_fetch):
        # A freshly published package (< 3 days)
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=106, name="vulnerable-lib", version="2.0.1",
            reviews=[{
                "user": {"login": "dwojtaszek"},
                "state": "APPROVED",
                "commit_id": "sha106",
                "body": "Approved with authorize-security for zero-day patch",
            }],
        )
        # In dry run, records exception in memory/bypass without disk pollution
        runner.DRY_RUN = True
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha106", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("security update approved by maintainer dwojtaszek", msg.lower())
        # Check that dry run did not write the file
        self.assertFalse(os.path.exists(runner._security_exception_path(106)))

        # Incidental mention of "security" without authorize-security marker or security label does NOT bypass
        pr_incidental = self._create_dependabot_pr(
            number=107, name="lib", version="2.0.1",
            reviews=[{
                "user": {"login": "dwojtaszek"},
                "state": "APPROVED",
                "commit_id": "sha107",
                "body": "LGTM, no security concerns here",
            }],
        )
        eligible_inc, msg_inc = runner._check_dependabot_upstream_age(pr_incidental, "sha107", now=self.now)
        self.assertFalse(eligible_inc)
        self.assertIn("too new", msg_inc.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_security_exception_revoked_maintainer_approval_does_not_bypass(self, mock_fetch):
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=108, name="vulnerable-lib", version="2.0.1",
            reviews=[
                {
                    "user": {"login": "dwojtaszek"},
                    "state": "APPROVED",
                    "commit_id": "sha108",
                    "body": "authorize-security",
                },
                {
                    "user": {"login": "dwojtaszek"},
                    "state": "CHANGES_REQUESTED",
                    "commit_id": "sha108",
                    "body": "Hold on, need to verify patch",
                },
            ],
        )
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha108", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("too new", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_security_exception_unauthorized_user_review_does_not_bypass(self, mock_fetch):
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=109, name="vulnerable-lib", version="2.0.1",
            reviews=[{
                "user": {"login": "unauthorized-contributor"},
                "state": "APPROVED",
                "commit_id": "sha109",
                "body": "authorize-security - verified safe",
            }],
        )
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha109", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("too new", msg.lower())


    @patch("runner._sync_main_after_merge")
    @patch("runner.send_email")
    @patch("runner._merge_authorized_pr")
    @patch("runner.run_cmd")
    @patch("runner._authorized_pr")
    @patch("runner._fetch_upstream_publish_timestamp")
    def test_babysit_dependabot_prs_fresh_blocks_old_merges(
        self, mock_fetch, mock_auth, mock_run_cmd, mock_merge, mock_email, mock_sync
    ):
        pr = self._create_dependabot_pr(number=99, name="Foo", version="1.1.0")
        self._setup_mock_bot_runner(pr, mock_auth, mock_run_cmd, mock_merge)

        # 1. Freshly published (< 3 days): auto-merge is blocked
        mock_fetch.return_value = datetime.now(timezone.utc) - timedelta(days=1)
        runner.DRY_RUN = False
        runner.babysit_dependabot_prs()
        mock_merge.assert_not_called()

        # 2. Old package (>= 3 days): auto-merge succeeds
        mock_fetch.return_value = datetime.now(timezone.utc) - timedelta(days=5)
        runner.babysit_dependabot_prs()
        mock_merge.assert_called_once_with(99, None, "sha99", delete_branch=True)

    @patch("runner._sync_main_after_merge")
    @patch("runner.send_email")
    @patch("runner._merge_authorized_pr")
    @patch("runner.run_cmd")
    @patch("runner._authorized_pr")
    @patch("runner._fetch_upstream_publish_timestamp")
    def test_babysit_dependabot_prs_security_exception_merges_fresh(
        self, mock_fetch, mock_auth, mock_run_cmd, mock_merge, mock_email, mock_sync
    ):
        pr = self._create_dependabot_pr(
            number=99, name="Foo", version="1.1.0",
            title="deps(nuget): bump Foo to fix CVE-2026-0001",
        )
        self._setup_mock_bot_runner(pr, mock_auth, mock_run_cmd, mock_merge)

        # Fresh package (< 3 days)
        mock_fetch.return_value = datetime.now(timezone.utc) - timedelta(hours=6)

        # Record explicit security authorization
        runner._record_dependabot_security_exception(99, "sha99", "dwojtaszek", "CVE-2026-0001")

        runner.DRY_RUN = False
        runner.babysit_dependabot_prs()
        mock_merge.assert_called_once_with(99, None, "sha99", delete_branch=True)


if __name__ == "__main__":
    unittest.main()
