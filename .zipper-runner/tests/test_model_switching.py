"""Tests for quota exhaustion detection, model switching, and fallback per harness."""
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

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()

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
        self.orig_candidates = list(runner.AGENT_CANDIDATES)

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
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

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()

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
        self.orig_active_agent = runner.ACTIVE_AGENT
        self.orig_active_model = runner.ACTIVE_MODEL

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
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


if __name__ == "__main__":
    unittest.main()

