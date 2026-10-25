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
6. Publication timestamp parsing tolerates the fractional-second forms providers emit.
7. NuGet registration index pages referenced by @id are followed, not just inlined ones.
8. The SemVer 2.0.0 registration hive is consulted and its gzip bodies inflated, so
   SemVer 2.0.0 versions (dotted prereleases) are resolvable rather than blocked forever.
"""

from datetime import datetime, timedelta, timezone
import gzip
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
import urllib.error
from unittest.mock import MagicMock, patch

import gitenv  # noqa: F401

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402


def _fake_urlopen(routes, calls=None):
    """A urlopen replacement that answers by URL rather than by call order.

    An order-dependent side-effect list hides which hive a lookup actually
    consulted: the same canned responses keep the test passing after the URL
    sequence changes underneath it. Keying on the URL makes the hive explicit.

    The raise on an unregistered URL is only a diagnostic. Production wraps both
    the leaf and the index fetch in ``except Exception``, which swallows it, so a
    test that relies on the raise alone still passes after the lookup starts
    talking to an unexpected hive. Pass ``calls`` and assert the exact URL
    sequence; that is the assertion that actually pins the hive order.
    """
    def _open(req, *args, **kwargs):
        url = getattr(req, "full_url", str(req))
        if calls is not None:
            calls.append(url)
        if url not in routes:
            raise AssertionError(f"unexpected request: {url}")
        status, body = routes[url]
        if status != 200:
            raise urllib.error.HTTPError(url, status, "error", {}, None)
        payload = body if isinstance(body, bytes) else json.dumps(body).encode()
        return MagicMock(__enter__=lambda s: MagicMock(read=lambda: payload))
    return _open


def _nuget_url(hive, name, tail):
    return f"https://api.nuget.org/v3/{hive}/{name}/{tail}"


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
        mock_merge.return_value = (0, "merged", "", None)

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
    def test_fetch_nuget_publish_timestamp_directAndIndexFallback(self, mock_urlopen):
        leaf = _nuget_url("registration5-gz-semver2", "fscheck.xunit", "3.6.0.json")
        index = _nuget_url("registration5-gz-semver2", "fscheck.xunit", "index.json")

        # Direct version hit
        calls = []
        mock_urlopen.side_effect = _fake_urlopen(
            {leaf: (200, {"published": "2026-10-20T10:00:00Z"})}, calls=calls)
        ts = runner._fetch_upstream_publish_timestamp("FsCheck.Xunit", "3.6.0", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 20, 10, 0, 0, tzinfo=timezone.utc))
        self.assertEqual(calls, [leaf], "a direct hit must not touch the index")

        # Direct 404 falling back to the index
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            leaf: (404, None),
            index: (200, {"items": [{"items": [
                {"catalogEntry": {"version": "3.6.0", "published": "2026-10-19T08:00:00Z"}}]}]}),
        }, calls=calls)
        ts_fallback = runner._fetch_upstream_publish_timestamp("FsCheck.Xunit", "3.6.0", "nuget")
        self.assertEqual(ts_fallback, datetime(2026, 10, 19, 8, 0, 0, tzinfo=timezone.utc))
        self.assertEqual(calls, [leaf, index], "the index fallback must stay on the same hive")

    @patch("runner.urllib.request.urlopen")
    def test_fetch_nuget_publish_timestamp_serverErrorOnOneHive_stillResolvesFromTheOther(self, mock_urlopen):
        """A transient server error on one hive must not abort the whole lookup:
        the other hive can still answer, and only a total miss returns None."""
        gz_leaf = _nuget_url("registration5-gz-semver2", "fscheck.xunit", "3.6.0.json")
        gz_index = _nuget_url("registration5-gz-semver2", "fscheck.xunit", "index.json")
        v1_leaf = _nuget_url("registration5-semver1", "fscheck.xunit", "3.6.0.json")
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            gz_leaf: (500, None),
            gz_index: (500, None),
            v1_leaf: (200, {"published": "2026-10-18T08:00:00Z"}),
        }, calls=calls)
        ts = runner._fetch_upstream_publish_timestamp("FsCheck.Xunit", "3.6.0", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 18, 8, 0, 0, tzinfo=timezone.utc))
        self.assertEqual(calls, [gz_leaf, gz_index, v1_leaf],
                         "the gz hive must be tried first and its failure must fall through")

    @patch("runner.urllib.request.urlopen")
    def test_fetch_nuget_publish_timestamp_everyHiveMissing_failsClosed(self, mock_urlopen):
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            _nuget_url(hive, "fscheck.xunit", tail): (404, None)
            for hive in ("registration5-gz-semver2", "registration5-semver1")
            for tail in ("3.6.0.json", "index.json")
        }, calls=calls)
        self.assertIsNone(runner._fetch_upstream_publish_timestamp("FsCheck.Xunit", "3.6.0", "nuget"))
        self.assertEqual(calls, [
            _nuget_url("registration5-gz-semver2", "fscheck.xunit", "3.6.0.json"),
            _nuget_url("registration5-gz-semver2", "fscheck.xunit", "index.json"),
            _nuget_url("registration5-semver1", "fscheck.xunit", "3.6.0.json"),
            _nuget_url("registration5-semver1", "fscheck.xunit", "index.json"),
        ], "every hive must be exhausted before the lookup fails closed")

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

        # Draft release or missing published_at -> None (fail closed, no created_at fallback)
        mock_run_cmd.side_effect = [
            (0, json.dumps({"draft": True, "created_at": "2026-10-10T12:00:00Z", "published_at": None}), ""),
            (0, json.dumps({"draft": False, "created_at": "2026-10-10T12:00:00Z"}), ""),
        ]
        ts_draft = runner._fetch_upstream_publish_timestamp("actions/checkout", "4.2.0", "github-actions")
        self.assertIsNone(ts_draft)

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
                "body": "Approved for the zero-day patch.\nauthorize-security: CVE-2026-0001",
            }],
        )
        # In dry run, records exception in memory/bypass without disk pollution
        runner.DRY_RUN = True
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha106", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("security update approved by maintainer dwojtaszek", msg.lower())
        # Check that dry run did not write the file
        self.assertFalse(os.path.exists(runner._security_exception_path(106)))

        # Incidental mention of "security" without the authorize-security marker does NOT bypass
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
    def test_securityLabel_alone_doesNotBypassTheCooldown(self, mock_fetch):
        """GitHub applies the `security` label automatically to Dependabot
        security-update PRs, so it cannot express intent. Accepting it let an
        approval given for any other reason silently become a cooldown bypass the
        maintainer never asked for. Only the written marker authorizes."""
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=112, name="vulnerable-lib", version="2.0.1",
            reviews=[{
                "user": {"login": "dwojtaszek"},
                "state": "APPROVED",
                "commit_id": "sha112",
                "body": "Looks good, I'll merge once the cooldown expires",
            }],
        )
        pr["labels"] = [{"name": "security"}, {"name": "dependencies"}]
        runner.DRY_RUN = False
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha112", now=self.now)
        self.assertFalse(eligible, "the security label must not authorize a cooldown bypass")
        self.assertIn("too new", msg.lower())
        self.assertFalse(os.path.exists(runner._security_exception_path(112)),
                         "no exception may be recorded from a label alone")

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_securityLabel_withTheMarker_stillBypasses(self, mock_fetch):
        """The label is not a reason to refuse either: with the written marker the
        bypass still applies, so removing the label path cannot strand a real
        security update."""
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=113, name="vulnerable-lib", version="2.0.1",
            reviews=[{
                "user": {"login": "dwojtaszek"},
                "state": "APPROVED",
                "commit_id": "sha113",
                "body": "authorize-security: zero-day patch",
            }],
        )
        pr["labels"] = [{"name": "security"}]
        runner.DRY_RUN = True
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha113", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("security update approved by maintainer dwojtaszek", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_securityMarker_withoutAnyLabel_stillBypasses(self, mock_fetch):
        """The marker alone is sufficient, which is what makes the label path
        removable: a maintainer-authored dependency bump needs no bot label."""
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=114, name="vulnerable-lib", version="2.0.1",
            reviews=[{
                "user": {"login": "dwojtaszek"},
                "state": "APPROVED",
                "commit_id": "sha114",
                "body": "authorize-security: CVE-2026-0001",
            }],
        )
        pr["labels"] = []
        runner.DRY_RUN = True
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha114", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("security update approved by maintainer dwojtaszek", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_securityMarker_mentionedButNotDirected_doesNotBypass(self, mock_fetch):
        """The token is a directive, not a keyword. A bare substring match
        authorized a body that says the opposite of intent, and the token is now
        the only way to state intent, so the ambiguity has to be closed rather
        than assumed away.

        The last four bodies put the token at the start of a line, which is where
        an anchor alone still let them through. Only the token on its own line,
        optionally followed by a colon and a reason, is a directive.
        """
        mock_fetch.return_value = self.now - timedelta(hours=12)
        bodies = [
            "I am NOT authorizing this; do not authorize-security here.",
            "Quoting CI.md: bypass is allowed with `authorize-security` marker. I do not approve.",
            "LGTM but authorize-security should not be used for this one",
            "See the authorize-security policy before approving",
            "authorize-security should not be used for this one",
            "authorize-security is NOT granted for this bump",
            "authorize-security, but I do not approve this one",
            "authorize-security - do not merge early",
            "> authorize-security: not approved",
            "- authorize-security: not approved",
            "**authorize-security**: not approved",
        ]
        for index, body in enumerate(bodies):
            with self.subTest(body=body):
                number = 120 + index
                pr = self._create_dependabot_pr(
                    number=number, name="vulnerable-lib", version="2.0.1",
                    reviews=[{
                        "user": {"login": "dwojtaszek"},
                        "state": "APPROVED",
                        "commit_id": f"sha{number}",
                        "body": body,
                    }],
                )
                runner.DRY_RUN = False
                eligible, msg = runner._check_dependabot_upstream_age(pr, f"sha{number}", now=self.now)
                self.assertFalse(eligible, f"a non-directive mention must not authorize: {body!r}")
                self.assertIn("too new", msg.lower())
                self.assertFalse(os.path.exists(runner._security_exception_path(number)),
                                 f"no exception may be recorded for {body!r}")

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_securityMarker_formsThatAreDirectives_stillBypass(self, mock_fetch):
        """The accepted grammar: the token alone on a line, or the token followed
        by a colon and a reason. Anything else is a mention, not a decision.

        The CRLF bodies are here because GitHub's web UI sends ``\\r\\n``. With a
        ``$`` that only looks for ``\\n``, the bare form matched a ``\\n`` body but
        not a ``\\r\\n`` one, so the documented form would have been silently
        unusable for the maintainer it exists for.
        """
        mock_fetch.return_value = self.now - timedelta(hours=12)
        bodies = [
            "authorize-security",
            "authorize-security:",
            "authorize-security: CVE-2026-0001",
            "authorize-security:no space after the colon",
            "  \tauthorize-security\t",
            "Rationale first.\nauthorize-security: CVE-2026-0001\n\nMore rationale.",
            "authorize-security\r\n",
            "authorize-security\r\nMore rationale.",
            "authorize-security: CVE-2026-0001\r\nMore rationale.",
            "  authorize-security  \r\n",
        ]
        for index, body in enumerate(bodies):
            with self.subTest(body=body):
                number = 140 + index
                pr = self._create_dependabot_pr(
                    number=number, name="vulnerable-lib", version="2.0.1",
                    reviews=[{
                        "user": {"login": "dwojtaszek"},
                        "state": "APPROVED",
                        "commit_id": f"sha{number}",
                        "body": body,
                    }],
                )
                runner.DRY_RUN = True
                eligible, msg = runner._check_dependabot_upstream_age(pr, f"sha{number}", now=self.now)
                self.assertTrue(eligible, f"a well-formed directive must authorize: {body!r}")
                self.assertIn("security update approved by maintainer dwojtaszek", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_securityMarker_onItsOwnLineAfterOtherText_stillBypasses(self, mock_fetch):
        """The directive does not have to be the first line, only its own line, so
        a maintainer can write a rationale above it."""
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=130, name="vulnerable-lib", version="2.0.1",
            reviews=[{
                "user": {"login": "dwojtaszek"},
                "state": "APPROVED",
                "commit_id": "sha130",
                "body": "Zero-day in the XML parser, patched upstream today.\n\n  authorize-security: CVE-2026-0003",
            }],
        )
        runner.DRY_RUN = True
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha130", now=self.now)
        self.assertTrue(eligible)
        self.assertIn("security update approved by maintainer dwojtaszek", msg.lower())

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
                    "body": "authorize-security: CVE-2026-0002",
                },
                {
                    "user": {"login": "dwojtaszek"},
                    "state": "CHANGES_REQUESTED",
                    "commit_id": "sha108",
                    "body": "Hold on, need to verify patch",
                },
            ],
        )

        # Pre-record persisted exception (as if approved in earlier scan)
        runner.DRY_RUN = False
        runner._record_dependabot_security_exception(108, "sha108", "dwojtaszek", "CVE patch")
        self.assertTrue(os.path.exists(runner._security_exception_path(108)))

        # When evaluated with revoked review, persisted exception is invalidated/unlinked
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha108", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("too new", msg.lower())
        self.assertFalse(os.path.exists(runner._security_exception_path(108)))

        # If the exception came from a maintainer review and that review is dismissed (absent), it is revoked
        pr_dismissed = self._create_dependabot_pr(number=108, name="vulnerable-lib", version="2.0.1", reviews=[])
        runner._record_dependabot_security_exception(
            108, "sha108", "dwojtaszek", "Maintainer approval review (security update): authorized"
        )
        self.assertTrue(os.path.exists(runner._security_exception_path(108)))
        eligible_d, msg_d = runner._check_dependabot_upstream_age(pr_dismissed, "sha108", now=self.now)
        self.assertFalse(eligible_d)
        self.assertFalse(os.path.exists(runner._security_exception_path(108)))

    @patch("runner._fetch_upstream_publish_timestamp")
    @patch("runner._gh_api_paginate_slurp")
    def test_check_upstream_age_reviews_fetch_failure_fails_closed(self, mock_slurp, mock_fetch):
        mock_fetch.return_value = self.now - timedelta(hours=12)
        mock_slurp.return_value = None
        pr = self._create_dependabot_pr(number=110, name="vulnerable-lib", version="2.0.1")
        del pr["reviews"]  # Force fetching via API
        eligible, msg = runner._check_dependabot_upstream_age(pr, "sha110", now=self.now)
        self.assertFalse(eligible)
        self.assertIn("could not fetch pr reviews", msg.lower())

    @patch("runner._fetch_upstream_publish_timestamp")
    def test_security_exception_unauthorized_user_review_does_not_bypass(self, mock_fetch):
        # The body is a well-formed directive so this fails on the reviewer's
        # identity alone. A malformed directive would pass for the wrong reason.
        mock_fetch.return_value = self.now - timedelta(hours=12)
        pr = self._create_dependabot_pr(
            number=109, name="vulnerable-lib", version="2.0.1",
            reviews=[{
                "user": {"login": "unauthorized-contributor"},
                "state": "APPROVED",
                "commit_id": "sha109",
                "body": "authorize-security: verified safe",
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




class PublishTimestampParsingTests(unittest.TestCase):
    """Publication timestamps arrive in several fractional-second forms.

    ``datetime.fromisoformat`` accepts only 3 or 6 fractional digits before
    Python 3.11, while NuGet publishes .NET tick precision (7 digits) and npm
    uses millisecond precision. A timestamp that fails to parse fails the
    upstream-age check closed, so these forms must parse rather than block
    every Dependabot PR on such a host.
    """

    def test_parse_publish_timestamp_fractionalSeconds_ofAnyLength_TruncateToMicroseconds(self):
        cases = {
            "2026-10-20T10:30:00.1234567+00:00": "2026-10-20T10:30:00.123456+00:00",
            "2026-10-20T10:30:00.55+00:00": "2026-10-20T10:30:00.550000+00:00",
            "2026-10-20T10:30:00.123+00:00": "2026-10-20T10:30:00.123000+00:00",
            "2026-10-20T10:30:00.123456+00:00": "2026-10-20T10:30:00.123456+00:00",
        }
        for value, expected in cases.items():
            with self.subTest(value=value):
                self.assertEqual(runner._parse_publish_timestamp(value).isoformat(), expected)

    def test_parse_publish_timestamp_zuluAndExplicitOffset_ResolveToTheSameInstant(self):
        self.assertEqual(
            runner._parse_publish_timestamp("2026-10-20T10:30:00Z"),
            runner._parse_publish_timestamp("2026-10-20T10:30:00+00:00"),
        )

    def test_parse_publish_timestamp_unparseable_raisesValueError(self):
        # Callers rely on the raise: NuGet and npm fail the age check closed from
        # their except branch, and GitHub Actions falls through to the next tag.
        # Returning None here would silently change that control flow.
        for value in ["", "not-a-timestamp", "2026-13-45T99:99:99Z"]:
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    runner._parse_publish_timestamp(value)

    @patch("urllib.request.urlopen")
    def test_fetch_nuget_publish_timestamp_sevenDigitFraction_ParsesInsteadOfFailingClosed(self, mock_urlopen):
        mock_resp = MagicMock()
        mock_resp.read.return_value = json.dumps({"published": "2026-10-20T10:30:00.1234567+00:00"}).encode()
        mock_resp.__enter__.return_value = mock_resp
        mock_urlopen.return_value = mock_resp

        ts = runner._fetch_upstream_publish_timestamp("Newtonsoft.Json", "13.0.3", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 20, 10, 30, 0, 123456, tzinfo=timezone.utc))


class NugetRegistrationHiveTests(unittest.TestCase):
    """The SemVer 2.0.0 hive must be consulted, and its gzip bodies inflated.

    NuGet publishes the same catalog under several registration hives, and they do
    not hold the same versions: ``registration5-semver1`` omits every SemVer 2.0.0
    version, which is what a dotted prerelease is. Measured on
    microsoft.extensions.logging, the flat container lists 181 versions (79 dotted
    prereleases), semver1 lists 102 (11 prereleases, none of them dotted), and the
    gz-semver2 hive lists 181 (90 prereleases, the same 79 dotted). So a lookup that
    reads semver1 only cannot resolve 11.0.0-rc.2.26504.105 at all, and the age
    policy blocks that PR forever.
    """

    GZ_LEAF = "https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.logging/11.0.0-rc.2.26504.105.json"
    GZ_INDEX = "https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.logging/index.json"
    V1_LEAF = "https://api.nuget.org/v3/registration5-semver1/microsoft.extensions.logging/11.0.0-rc.2.26504.105.json"
    V1_INDEX = "https://api.nuget.org/v3/registration5-semver1/microsoft.extensions.logging/index.json"

    @patch("urllib.request.urlopen")
    def test_semver2DottedPrerelease_resolvesThroughTheGzHive(self, mock_urlopen):
        """The bug this fix exists for. semver1 404s this exact version."""
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            self.GZ_LEAF: (200, {"published": "2026-10-13T00:00:00Z"}),
            self.V1_LEAF: (404, None),
        }, calls=calls)
        ts = runner._fetch_upstream_publish_timestamp(
            "microsoft.extensions.logging", "11.0.0-rc.2.26504.105", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 13, tzinfo=timezone.utc))
        self.assertEqual(calls, [self.GZ_LEAF],
                         "the gz-semver2 hive must answer without falling through to semver1")

    @patch("urllib.request.urlopen")
    def test_gzipEncodedLeaf_isInflated(self, mock_urlopen):
        """The -gz- hives are served gzip-encoded even with no Accept-Encoding
        header, so an uninflated body is not JSON and the lookup fails closed."""
        payload = gzip.compress(json.dumps({"published": "2026-10-13T00:00:00Z"}).encode())
        self.assertEqual(payload[:2], b"\x1f\x8b", "the fixture must actually be gzip")
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({self.GZ_LEAF: (200, payload)}, calls=calls)
        ts = runner._fetch_upstream_publish_timestamp(
            "microsoft.extensions.logging", "11.0.0-rc.2.26504.105", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 13, tzinfo=timezone.utc))
        self.assertEqual(calls, [self.GZ_LEAF], "the gzip leaf must answer on its own hive")

    @patch("urllib.request.urlopen")
    def test_semver2Miss_fallsBackToSemver1(self, mock_urlopen):
        """semver1 stays as the fallback, so a version the gz hive does not serve
        is still resolvable rather than newly blocked."""
        version = "9.0.0"
        gz_leaf = _nuget_url("registration5-gz-semver2", "microsoft.extensions.logging", f"{version}.json")
        gz_index = _nuget_url("registration5-gz-semver2", "microsoft.extensions.logging", "index.json")
        v1_leaf = _nuget_url("registration5-semver1", "microsoft.extensions.logging", f"{version}.json")
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            gz_leaf: (404, None),
            gz_index: (404, None),
            v1_leaf: (200, {"published": "2024-11-12T00:00:00Z"}),
        }, calls=calls)
        ts = runner._fetch_upstream_publish_timestamp(
            "microsoft.extensions.logging", version, "nuget")
        self.assertEqual(ts, datetime(2024, 11, 12, tzinfo=timezone.utc))
        self.assertEqual(calls, [gz_leaf, gz_index, v1_leaf],
                         "a version the gz hive lacks must still be reachable through semver1")

    @patch("urllib.request.urlopen")
    def test_gzipEncodedIndex_isInflatedAndMatched(self, mock_urlopen):
        """The index path must inflate too: the gz hive serves its index gzipped."""
        index = {"items": [{"items": [
            {"catalogEntry": {"version": "11.0.0-rc.2.26504.105", "published": "2026-10-13T00:00:00Z"}},
            {"catalogEntry": {"version": "9.0.0", "published": "2024-11-12T00:00:00Z"}},
        ]}]}
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            self.GZ_LEAF: (404, None),
            self.GZ_INDEX: (200, gzip.compress(json.dumps(index).encode())),
        }, calls=calls)
        ts = runner._fetch_upstream_publish_timestamp(
            "microsoft.extensions.logging", "11.0.0-rc.2.26504.105", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 13, tzinfo=timezone.utc))
        self.assertEqual(calls, [self.GZ_LEAF, self.GZ_INDEX],
                         "a gzipped index must be inflated on the gz hive, not skipped")

    @patch("urllib.request.urlopen")
    def test_semver2NotFoundWhileSemver1HoldsTheVersion_resolvesFromSemver1(self, mock_urlopen):
        """A SemVer 1.0 version the gz hive lacks must not become unresolvable."""
        gz_leaf = _nuget_url("registration5-gz-semver2", "serilog", "4.0.2.json")
        gz_index = _nuget_url("registration5-gz-semver2", "serilog", "index.json")
        v1_leaf = _nuget_url("registration5-semver1", "serilog", "4.0.2.json")
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            gz_leaf: (404, None),
            gz_index: (404, None),
            v1_leaf: (200, {"published": "2024-09-28T00:00:00Z"}),
        }, calls=calls)
        ts = runner._fetch_upstream_publish_timestamp("Serilog", "4.0.2", "nuget")
        self.assertEqual(ts, datetime(2024, 9, 28, tzinfo=timezone.utc))
        self.assertEqual(calls, [gz_leaf, gz_index, v1_leaf])

    def test_registrationHives_includeTheGzSemver2HiveAndNotTheUnservedOne(self):
        """registration5-semver2 is not served at all: measured 404 for every
        package tried, including ones semver1 serves, so a plain hive swap would
        trade a missing version for a missing package. Only the gz variant may be
        consulted. The order is asserted behaviorally by the call-sequence
        assertions, so a deliberate reorder does not fail this test."""
        self.assertIn("registration5-gz-semver2", runner._NUGET_REGISTRATION_HIVES)
        self.assertNotIn("registration5-semver2", runner._NUGET_REGISTRATION_HIVES)


class NugetIndexPagingTests(unittest.TestCase):
    """The registration index fallback must follow referenced pages.

    NuGet inlines a page only while it stays small. A larger package returns
    pages that carry an @id and no items (Serilog: 10 of 10 pages), so a lookup
    that reads only inline entries finds nothing and fails the age check closed
    forever.
    """

    @patch("urllib.request.urlopen")
    def test_fetch_nuget_publish_timestamp_referencedIndexPage_IsFollowed(self, mock_urlopen):
        page_url = "https://api.nuget.org/v3/registration5-gz-semver2/lib/page/1.json"
        leaf = _nuget_url("registration5-gz-semver2", "lib", "2.0.1.json")
        index = _nuget_url("registration5-gz-semver2", "lib", "index.json")
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({
            leaf: (404, None),
            index: (200, {"items": [{"@id": page_url}]}),
            page_url: (200, {"items": [
                {"catalogEntry": {"version": "2.0.1", "published": "2026-10-19T08:00:00Z"}}]}),
        }, calls=calls)

        ts = runner._fetch_upstream_publish_timestamp("Lib", "2.0.1", "nuget")
        self.assertEqual(ts, datetime(2026, 10, 19, 8, 0, 0, tzinfo=timezone.utc))
        self.assertEqual(calls, [leaf, index, page_url],
                         "a referenced page must be fetched from the gz hive")

    def test_iter_nuget_index_entries_inlinePage_YieldsWithoutFetching(self):
        # An inline page must not trigger a network call; only referenced pages do.
        index = {
            "items": [{"items": [{"catalogEntry": {"version": "1.0.0"}}]},
                      {"@id": "https://api.nuget.org/v3/registration5-semver1/lib/page/1.json"}],
        }
        with patch("urllib.request.urlopen", side_effect=AssertionError("should not fetch")) as mocked:
            entries = list(runner._iter_nuget_index_entries({"items": [index["items"][0]]}))
            self.assertEqual([e["catalogEntry"]["version"] for e in entries], ["1.0.0"])
            mocked.assert_not_called()

    @patch("urllib.request.urlopen")
    def test_iter_nuget_index_entries_bomPrefixedPage_IsStillParsed(self, mock_urlopen):
        # NuGet serves a UTF-8 BOM on some registration responses; a plain
        # decode would raise and fail the age check closed for every version.
        page_url = "https://api.nuget.org/v3/registration5-gz-semver2/lib/page/1.json"
        payload = b"\xef\xbb\xbf" + json.dumps({"items": [{"catalogEntry": {"version": "2.0.1"}}]}).encode()
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({page_url: (200, payload)}, calls=calls)
        entries = list(runner._iter_nuget_index_entries({"items": [{"@id": page_url}]}))
        self.assertEqual([e["catalogEntry"]["version"] for e in entries], ["2.0.1"])
        self.assertEqual(calls, [page_url])

    @patch("urllib.request.urlopen")
    def test_iter_nuget_index_entries_gzipEncodedPage_IsInflated(self, mock_urlopen):
        # A referenced page from a -gz- hive is gzip-encoded like the index, so the
        # page fetch must inflate too or every paged package fails closed again.
        page_url = "https://api.nuget.org/v3/registration5-gz-semver2/lib/page/1.json"
        payload = gzip.compress(json.dumps({"items": [{"catalogEntry": {"version": "2.0.1"}}]}).encode())
        calls = []
        mock_urlopen.side_effect = _fake_urlopen({page_url: (200, payload)}, calls=calls)
        entries = list(runner._iter_nuget_index_entries({"items": [{"@id": page_url}]}))
        self.assertEqual([e["catalogEntry"]["version"] for e in entries], ["2.0.1"])
        self.assertEqual(calls, [page_url], "an inflated page must not trigger a retry")

    def test_iter_nuget_index_entries_noIdAndNoItems_IsSkipped(self):
        self.assertEqual(list(runner._iter_nuget_index_entries({"items": [{}]})), [])


if __name__ == "__main__":
    unittest.main()
