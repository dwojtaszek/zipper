"""Tests for quota exhaustion detection, model switching, and fallback per harness."""
import contextlib
import io
import shutil
import sys
from pathlib import Path
import unittest
from unittest.mock import MagicMock, patch

BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BASE))

import os
import tempfile

_STATE_DIR = tempfile.mkdtemp(prefix="zipper-runner-switching-")
os.environ.setdefault("STATE_DIR", _STATE_DIR)

import runner
runner.STATE_DIR = _STATE_DIR


def _clear_quota_state() -> None:
    """Remove persisted quota-exhaustion marks between tests.

    mark_model_exhausted persists its mark in STATE_DIR so quota windows are
    remembered across cron cycles; without this sweep one test's mark would
    leak into every later test in the same process."""
    import glob
    for path in glob.glob(os.path.join(runner.STATE_DIR, "quota-*.json")):
        try:
            os.remove(path)
        except OSError:
            pass


class QuotaDetectionTests(unittest.TestCase):
    def test_detects_resource_exhausted_from_issue_1120(self):
        err = (
            'error: Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 3h30m2s.\n'
            'AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429): Individual quota reached. Please upgrade your '
            'subscription to increase your limits. Resets in 3h30m2s.","status":"RESOURCE_EXHAUSTED","error_code":429,'
            '"code_kind":"http","retryable":true,"error_id":"67fada94-aaf9-42f5-928f-42ea351495df-1"}'
        )
        self.assertTrue(runner.is_quota_exhausted("", err))

    def test_detects_rate_limit_and_quota_keywords(self):
        cases = [
            "Rate limit exceeded. Try again later.",
            "insufficient quota for model claude-opus-4-6",
            "429 Too Many Requests",
            "Error: credit balance is too low",
            "You have exceeded your current quota",
            "status: 429 (quota exceeded)",
        ]
        for case in cases:
            with self.subTest(case=case):
                self.assertTrue(runner.is_quota_exhausted(case, ""))

    def test_does_not_false_positive_on_test_passes_or_lint_errors(self):
        cases = [
            "Ran 429 tests in 12.3s. 429 passed, 0 failed.",
            "error CS0103: The name 'myVar' does not exist in the current context",
            "src/Zipper/Writer.cs(429,15): error CS1002: ; expected",
            "Build FAILED. 0 Warning(s), 1 Error(s)",
        ]
        for case in cases:
            with self.subTest(case=case):
                self.assertFalse(runner.is_quota_exhausted(case, ""))


class ModelExhaustionTrackingTests(unittest.TestCase):
    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def test_mark_and_check_model_exhausted(self):
        self.assertFalse(runner.is_model_exhausted("agy", "claude-opus-4-6-thinking"))
        runner.mark_model_exhausted("agy", "claude-opus-4-6-thinking")
        self.assertTrue(runner.is_model_exhausted("agy", "claude-opus-4-6-thinking"))
        # Sibling model on same harness remains unexhausted
        self.assertFalse(runner.is_model_exhausted("agy", "gemini-3.8-flash-high"))

    def test_prefix_tolerant_exhaustion_matching(self):
        # When google/gemini-3.8-flash is marked exhausted, opencode/gemini-3.8-flash is also recognized
        runner.mark_model_exhausted("opencode", "google/gemini-3.8-flash")
        self.assertTrue(runner.is_model_exhausted("opencode", "google/gemini-3.8-flash"))
        self.assertTrue(runner.is_model_exhausted("opencode", "opencode/gemini-3.8-flash"))
        self.assertTrue(runner.is_model_exhausted("opencode", "gemini-3.8-flash"))
        # Unrelated model remains unexhausted
        self.assertFalse(runner.is_model_exhausted("opencode", "opencode/big-pickle"))


class ModelPriorityMatchingTests(unittest.TestCase):
    def test_exact_match(self):
        prefs = {("agy", "gemini-3.8-flash-high"): 1, ("agy", "claude-opus-4-6-thinking"): 2}
        prio, matched = runner.get_model_priority(prefs, "agy", "gemini-3.8-flash-high")
        self.assertEqual(prio, 1)
        self.assertEqual(matched, "gemini-3.8-flash-high")

    def test_suffix_tolerance_match(self):
        prefs = {("opencode", "google/gemini-3.8-flash"): 2}
        prio, matched = runner.get_model_priority(prefs, "opencode", "opencode/gemini-3.8-flash")
        self.assertEqual(prio, 2)
        self.assertEqual(matched, "google/gemini-3.8-flash")

    def test_unmatched_returns_zero(self):
        prefs = {("opencode", "google/gemini-3.8-flash"): 2}
        prio, _ = runner.get_model_priority(prefs, "opencode", "deepseek-chat")
        self.assertEqual(prio, 0)


class FallbackQueueTests(unittest.TestCase):
    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()
        self.orig_candidates = list(runner.AGENT_CANDIDATES)

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()
        runner.AGENT_CANDIDATES[:] = self.orig_candidates

    def test_fallback_advances_to_next_model_on_same_agent(self):
        runner.AGENT_CANDIDATES[:] = [
            ("agy", "claude-opus-4-6-thinking"),
            ("agy", "gemini-3.8-flash-high"),
            ("opencode", "big-pickle"),
        ]
        self.assertEqual(runner._current_agent(), "agy")
        self.assertEqual(runner._current_model(), "claude-opus-4-6-thinking")

        has_next = runner._fallback()
        self.assertTrue(has_next)
        self.assertEqual(runner._current_agent(), "agy")
        self.assertEqual(runner._current_model(), "gemini-3.8-flash-high")

    def test_fallback_skips_pre_exhausted_models(self):
        runner.AGENT_CANDIDATES[:] = [
            ("agy", "claude-opus-4-6-thinking"),
            ("agy", "gemini-3.8-flash-high"),
            ("opencode", "big-pickle"),
        ]
        # gemini-3.8-flash-high is already exhausted
        runner.mark_model_exhausted("agy", "gemini-3.8-flash-high")

        has_next = runner._fallback()
        self.assertTrue(has_next)
        # Should skip gemini-3.8-flash-high directly to opencode
        self.assertEqual(runner._current_agent(), "opencode")
        self.assertEqual(runner._current_model(), "big-pickle")

    def test_fallback_returns_false_when_all_exhausted(self):
        runner.AGENT_CANDIDATES[:] = [
            ("agy", "claude-opus-4-6-thinking"),
        ]
        has_next = runner._fallback()
        self.assertFalse(has_next)


class TokenHealthCheckWithModelTests(unittest.TestCase):
    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def test_short_circuits_if_model_already_exhausted(self):
        runner.mark_model_exhausted("agy", "claude-opus-4-6-thinking")
        with patch.dict(runner.AGENT_PLUGINS, {"agy": MagicMock()}):
            plugin = runner.AGENT_PLUGINS["agy"]
            healthy = runner.check_api_token_health("agy", "claude-opus-4-6-thinking")
            self.assertFalse(healthy)
            plugin.check_token_health.assert_not_called()

    def test_passes_model_to_plugin_check_token_health(self):
        mock_plugin = MagicMock()
        mock_plugin.check_installation.return_value = True
        mock_plugin.check_token_health.return_value = True

        with patch.dict(runner.AGENT_PLUGINS, {"agy": mock_plugin}):
            with patch.object(runner, "DRY_RUN", False):
                healthy = runner.check_api_token_health("agy", "gemini-3.8-flash-high")
                self.assertTrue(healthy)
                mock_plugin.check_token_health.assert_called_once_with(model="gemini-3.8-flash-high")


class ProbeAndSelectAgentsModelSwitchingTests(unittest.TestCase):
    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()
        self.orig_active_agent = runner.ACTIVE_AGENT
        self.orig_active_model = runner.ACTIVE_MODEL

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()
        runner.ACTIVE_AGENT = self.orig_active_agent
        runner.ACTIVE_MODEL = self.orig_active_model

    def test_probe_and_select_skips_exhausted_models(self):
        runner.ACTIVE_AGENT = None
        runner.ACTIVE_MODEL = None

        mock_agy = MagicMock()
        mock_agy.check_installation.return_value = True
        mock_agy.check_token_health.return_value = True
        mock_agy.list_models.return_value = ["gemini-3.8-flash-high", "claude-opus-4-6-thinking"]

        prefs = {
            ("agy", "gemini-3.8-flash-high"): 1,
            ("agy", "claude-opus-4-6-thinking"): 1,
        }

        # Mark gemini-3.8-flash-high exhausted
        runner.mark_model_exhausted("agy", "gemini-3.8-flash-high")

        with patch.dict(runner.AGENT_PLUGINS, {"agy": mock_agy}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                candidates = runner.probe_and_select_agents()
                # Should only contain claude-opus-4-6-thinking
                self.assertEqual(len(candidates), 1)
                self.assertEqual(candidates[0], ("agy", "claude-opus-4-6-thinking"))

    def test_probe_and_select_promotes_active_model(self):
        runner.ACTIVE_AGENT = "agy"
        runner.ACTIVE_MODEL = "claude-opus-4-6-thinking"

        mock_agy = MagicMock()
        mock_agy.check_installation.return_value = True
        mock_agy.check_token_health.return_value = True
        mock_agy.list_models.return_value = ["gemini-3.8-flash-high", "claude-opus-4-6-thinking"]

        # Even though gemini-3.8-flash-high has priority 1 and claude has priority 2:
        prefs = {
            ("agy", "gemini-3.8-flash-high"): 1,
            ("agy", "claude-opus-4-6-thinking"): 2,
        }

        with patch.dict(runner.AGENT_PLUGINS, {"agy": mock_agy}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                candidates = runner.probe_and_select_agents()
                # claude-opus-4-6-thinking should be promoted to first
                self.assertEqual(candidates[0], ("agy", "claude-opus-4-6-thinking"))
                self.assertEqual(candidates[1], ("agy", "gemini-3.8-flash-high"))

    def test_probe_and_select_matches_suffix_model(self):
        runner.ACTIVE_AGENT = None
        runner.ACTIVE_MODEL = None

        mock_opencode = MagicMock()
        mock_opencode.check_installation.return_value = True
        mock_opencode.check_token_health.return_value = True
        # opencode list_models returns opencode/gemini-3.8-flash
        mock_opencode.list_models.return_value = ["opencode/gemini-3.8-flash"]

        # AGENT_PREFERENCES has google/gemini-3.8-flash
        prefs = {
            ("opencode", "google/gemini-3.8-flash"): 2,
        }

        with patch.dict(runner.AGENT_PLUGINS, {"opencode": mock_opencode}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                candidates = runner.probe_and_select_agents()
                self.assertEqual(len(candidates), 1)
                self.assertEqual(candidates[0], ("opencode", "opencode/gemini-3.8-flash"))

    def test_is_quota_exhausted_detects_opencode_free_usage_limit(self):
        stderr = "\x1b[91m\x1b[1mError: \x1b[0mFree usage limit reached. Subscribe to OpenCode Go to keep going: https://opencode.ai/go"
        self.assertTrue(runner.is_quota_exhausted("", stderr))


def _clear_rate_state() -> None:
    """Remove persisted rate-limit marks so one test's send cannot suppress the
    next test's expected email (they share STATE_DIR in this process).

    This deletes files, so it refuses to run outside the system temp directory.
    Discovery imports every test module before running any test, and several
    modules pin the shared ``runner.STATE_DIR`` to their own temp dir, so the
    guard cannot name a single directory. The invariant that matters is that the
    target is never the live state directory the unattended runner uses: those
    hold the real email rate-limit budgets, and sweeping them would let the next
    cycle send up to three more emails per persistent alert."""
    state_dir = os.path.realpath(runner.STATE_DIR)
    if not state_dir.startswith(os.path.realpath(tempfile.gettempdir()) + os.sep):
        raise AssertionError(
            f"refusing to clear rate state outside the temp directory: {runner.STATE_DIR!r}")
    import glob
    for path in glob.glob(os.path.join(state_dir, "rate-*.json")):
        try:
            os.remove(path)
        except OSError:
            pass


class MissingConfiguredModelTests(unittest.TestCase):
    """A model configured at a non-zero priority that the agent no longer offers
    must be reported, because the runner otherwise queues it, selects it, and
    fails the mission at runtime."""

    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_rate_state()
        self.sent = []
        self._send = runner.send_email
        runner.send_email = lambda subject, body: self.sent.append((subject, body))
        self.orig_active_agent = runner.ACTIVE_AGENT
        self.orig_active_model = runner.ACTIVE_MODEL
        runner.ACTIVE_AGENT = None
        runner.ACTIVE_MODEL = None

    def tearDown(self):
        runner.send_email = self._send
        runner.EXHAUSTED_MODELS.clear()
        _clear_rate_state()
        runner.ACTIVE_AGENT = self.orig_active_agent
        runner.ACTIVE_MODEL = self.orig_active_model

    def _plugin(self, models):
        mock = MagicMock()
        mock.check_installation.return_value = True
        mock.check_token_health.return_value = True
        mock.list_models.return_value = list(models)
        return mock

    def test_configuredModelAbsentFromDiscovery_isReported(self):
        """exo-free was configured at priority 3 but opencode no longer offers it."""
        plugin = self._plugin(["opencode/big-pickle"])
        prefs = {("opencode", "opencode/big-pickle"): 1, ("opencode", "opencode/exo-free"): 3}
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                runner.probe_and_select_agents()
        subjects = [s for s, _ in self.sent]
        self.assertIn("[Runner] Configured Models No Longer Exist", subjects)
        body = [b for s, b in self.sent if "No Longer Exist" in s][0]
        self.assertIn("opencode/exo-free", body)
        self.assertNotIn("opencode/big-pickle", body)

    def test_configuredModelPresentInDiscovery_staysQuiet(self):
        plugin = self._plugin(["opencode/big-pickle", "opencode/glm-5.3-flash-free"])
        prefs = {("opencode", "opencode/big-pickle"): 1, ("opencode", "opencode/glm-5.3-flash-free"): 2}
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                runner.probe_and_select_agents()
        self.assertEqual(self.sent, [])

    def test_suffixMatch_countsAsPresent(self):
        """The preference may be prefixed while discovery is bare, so matching
        must be suffix-based, or every prefixed row would look missing."""
        plugin = self._plugin(["glm-5.3-flash-free"])
        prefs = {("opencode", "opencode/glm-5.3-flash-free"): 2}
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                runner.probe_and_select_agents()
        self.assertEqual(self.sent, [])

    def test_discoveryFallbackToDefault_staysQuiet(self):
        """A provider outage that falls back to 'default' must not mark every
        configured model as missing."""
        plugin = self._plugin(["default"])
        prefs = {("opencode", "opencode/exo-free"): 3, ("opencode", "opencode/glm-5.3-flash-free"): 2}
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                runner.probe_and_select_agents()
        self.assertEqual(self.sent, [])

    def test_defaultPlaceholder_isNotReported(self):
        """'default' names the agent's own default model, not a model discovery
        can return, so real discovery must not report it as gone. The shipped
        preferences carry claude/default and agy/default at priority 1."""
        plugin = self._plugin(["opencode/big-pickle"])
        prefs = {("opencode", "default"): 1, ("opencode", "opencode/big-pickle"): 1}
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                runner.probe_and_select_agents()
        self.assertEqual(self.sent, [])

    def test_statusOnlyRun_warnsOnConsoleButDoesNotMail(self):
        """--status is read-only: it must not consume the rate-limit budget or
        send mail, matching the 'All Agents Unhealthy' alert. The console
        warning is the point of running status, so it must survive."""
        plugin = self._plugin(["opencode/big-pickle"])
        prefs = {("opencode", "opencode/big-pickle"): 1, ("opencode", "opencode/exo-free"): 3}
        buf = io.StringIO()
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                with patch.object(runner, "STATUS_ONLY", True):
                    with contextlib.redirect_stdout(buf):
                        runner.probe_and_select_agents()
        self.assertEqual(self.sent, [], "a status-only run must not send mail")
        self.assertIn("no longer offered", buf.getvalue(),
                      "the console warning must survive a status-only run")
        # The budget must be intact: a real run still gets all its immediate sends.
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                for _ in range(3):
                    runner.probe_and_select_agents()
        self.assertEqual(len(self.sent), 3,
                         "a status-only run must not consume the rate-limit budget")

    def test_disabledConfiguredModel_isNotReported(self):
        """Priority 0 means deliberately disabled, not gone."""
        plugin = self._plugin(["opencode/big-pickle"])
        prefs = {("opencode", "opencode/big-pickle"): 1, ("opencode", "opencode/exo-free"): 0}
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                runner.probe_and_select_agents()
        self.assertEqual(self.sent, [])

    def test_missingModelReport_isRateLimited(self):
        """A persistent configuration mistake must not mail on every cycle."""
        plugin = self._plugin(["opencode/big-pickle"])
        prefs = {("opencode", "opencode/big-pickle"): 1, ("opencode", "opencode/exo-free"): 3}
        with patch.dict(runner.AGENT_PLUGINS, {"opencode": plugin}, clear=True):
            with patch.object(runner, "load_preferences", return_value=prefs):
                for _ in range(5):
                    runner.probe_and_select_agents()
        self.assertEqual(len(self.sent), 3)


class RateStateIsolationTests(unittest.TestCase):
    """_clear_rate_state deletes files, so it must refuse to touch a state
    directory outside the system temp directory, which is where the live
    .zipper-runner/state the unattended runner uses lives."""

    def test_clearRateState_refusesANonTempStateDir(self):
        """The probe directory sits beside the repo's own files, not under the temp
        root, so a run that slipped past the guard would sweep the live
        .zipper-runner/state marks. The temp root is patched so the test does not
        depend on where the checkout itself lives (a clone under /tmp would
        otherwise make BASE look temporary)."""
        foreign = Path(BASE) / "_rate_state_guard_probe"
        foreign.mkdir(exist_ok=True)
        victim = foreign / "rate--guard-probe.json"
        victim.write_text("{}")
        try:
            with patch.object(tempfile, "gettempdir",
                              return_value=str(Path(BASE) / "_rate_state_fake_temp")):
                with patch.object(runner, "STATE_DIR", str(foreign)):
                    with self.assertRaises(AssertionError):
                        _clear_rate_state()
            self.assertTrue(victim.exists(), "a non-temp state dir must not be cleaned")
        finally:
            shutil.rmtree(foreign, ignore_errors=True)

    def test_clearRateState_refusesASymlinkOutOfTheTempDir(self):
        """A symlink whose own path is inside the temp root but resolves into the
        repo must be judged by its target, not by its name."""
        target = Path(BASE) / "_rate_state_symlink_probe"
        target.mkdir(exist_ok=True)
        victim = target / "rate--probe.json"
        victim.write_text("{}")
        link_dir = Path(tempfile.mkdtemp(prefix="zipper-symlink-"))
        link = link_dir / "state"
        os.symlink(target, link)
        try:
            with patch.object(tempfile, "gettempdir", return_value=str(link_dir)):
                with patch.object(runner, "STATE_DIR", str(link)):
                    with self.assertRaises(AssertionError):
                        _clear_rate_state()
            self.assertTrue(victim.exists(), "a symlink out of the temp dir must not be followed")
        finally:
            shutil.rmtree(target, ignore_errors=True)
            shutil.rmtree(link_dir, ignore_errors=True)

    def test_clearRateState_refusesASiblingOfTheTempDir(self):
        """A path that merely shares the temp dir's name prefix, such as /tmpfoo
        beside /tmp, is not inside it."""
        root = Path(BASE) / "_rate_state_prefix_probe"
        fake_temp = root / "tmp"
        fake_temp.mkdir(parents=True, exist_ok=True)
        sibling = root / "tmpfoo"
        sibling.mkdir(exist_ok=True)
        victim = sibling / "rate--probe.json"
        victim.write_text("{}")
        try:
            with patch.object(tempfile, "gettempdir", return_value=str(fake_temp)):
                with patch.object(runner, "STATE_DIR", str(sibling)):
                    with self.assertRaises(AssertionError):
                        _clear_rate_state()
            self.assertTrue(victim.exists(), "a sibling of the temp dir must not be cleaned")
        finally:
            shutil.rmtree(root, ignore_errors=True)

    def test_clearRateState_removesMarksInTheTestDir(self):
        mark = Path(runner.STATE_DIR) / "rate--isolation-probe.json"
        mark.write_text("{}")
        _clear_rate_state()
        self.assertFalse(mark.exists(), "the test directory's own marks must be cleared")


class DryRunRateLimitTests(unittest.TestCase):
    """A dry run must never record a send. send_email already suppresses delivery
    under --dry-run, so a recorded mark would spend the budget on an email nobody
    received and hide the real alert for a cooldown window afterwards."""

    def setUp(self):
        _clear_rate_state()

    def tearDown(self):
        _clear_rate_state()

    def test_dryRun_neverRecordsASend(self):
        subject = "[Runner] Dry Run Probe"
        with patch.object(runner, "DRY_RUN", True):
            self.assertEqual([runner._should_send_rate_limited(subject) for _ in range(5)],
                             [False] * 5)
        with patch.object(runner, "DRY_RUN", False):
            self.assertEqual([runner._should_send_rate_limited(subject) for _ in range(4)],
                             [True, True, True, False],
                             "the budget must be intact after any number of dry runs")


if __name__ == "__main__":
    unittest.main()

