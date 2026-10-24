"""Unattended-endurance tests for runner.py and the opencode plugin.

Covers the mechanisms that keep the runner self-healing over multi-day
unattended operation: bounded commands, stale-lock watchdog reclaim,
cross-cycle quota cooldowns, state-file sweeps, gh-outage guards,
worktree mission backoff, and opencode process-group cleanup.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import MagicMock, patch

import gitenv  # noqa: F401  # Git identity for fixture repositories

_STATE_DIR = tempfile.mkdtemp(prefix="zipper-runner-endurance-")
os.environ["STATE_DIR"] = _STATE_DIR

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402
runner.STATE_DIR = _STATE_DIR


def _clear_quota_state() -> None:
    for name in os.listdir(runner.STATE_DIR):
        if name.startswith(("quota-", "worktree-", "rate-", "opencode-poisoned-")):
            try:
                os.remove(os.path.join(runner.STATE_DIR, name))
            except OSError:
                pass


class RunCmdDefaultTimeoutTests(unittest.TestCase):
    def setUp(self):
        self.orig_default = runner.DEFAULT_CMD_TIMEOUT

    def tearDown(self):
        runner.DEFAULT_CMD_TIMEOUT = self.orig_default

    def test_runCmd_withoutExplicitTimeout_usesDefaultTimeout(self):
        runner.DEFAULT_CMD_TIMEOUT = 1
        code, _, err = runner.run_cmd(["sleep", "30"])
        self.assertEqual(code, -1)
        self.assertIn("timed out", err)

    def test_runCmd_withExplicitTimeout_overridesDefault(self):
        runner.DEFAULT_CMD_TIMEOUT = 1
        code, _, _ = runner.run_cmd(["sleep", "0.2"], timeout=10)
        self.assertEqual(code, 0)

    def test_runCmd_injectsGitLowSpeedAbortEnv(self):
        captured = {}
        # setdefault must inject only when the operator has not configured
        # their own values.
        saved = {k: os.environ.pop(k, None) for k in
                 ("GIT_HTTP_LOW_SPEED_LIMIT", "GIT_HTTP_LOW_SPEED_TIME", "GIT_SSH_COMMAND")}
        try:
            def fake_popen(args, **kwargs):
                captured.update(kwargs.get("env") or {})
                proc = MagicMock()
                proc.communicate.return_value = ("", "")
                proc.returncode = 0
                return proc

            with patch("subprocess.Popen", side_effect=fake_popen):
                runner.run_cmd(["git", "status"])
        finally:
            for key, value in saved.items():
                if value is not None:
                    os.environ[key] = value
        self.assertEqual(captured["GIT_HTTP_LOW_SPEED_LIMIT"], "1000")
        self.assertEqual(captured["GIT_HTTP_LOW_SPEED_TIME"], "60")
        # GIT_SSH_COMMAND must never be injected: it would override the
        # repository's core.sshCommand configuration.
        self.assertNotIn("GIT_SSH_COMMAND", captured)

    def test_runCmd_preservesOperatorConfiguredGitEnv(self):
        captured = {}

        def fake_popen(args, **kwargs):
            captured.update(kwargs.get("env") or {})
            proc = MagicMock()
            proc.communicate.return_value = ("", "")
            proc.returncode = 0
            return proc

        with patch.dict(os.environ, {"GIT_SSH_COMMAND": "ssh -o BatchMode=yes"}):
            with patch("subprocess.Popen", side_effect=fake_popen):
                runner.run_cmd(["git", "status"])
        self.assertEqual(captured["GIT_SSH_COMMAND"], "ssh -o BatchMode=yes")


class StaleLockWatchdogTests(unittest.TestCase):
    def setUp(self):
        self.orig_max_run = runner.MAX_RUN_SECONDS
        _clear_quota_state()

    def tearDown(self):
        runner.MAX_RUN_SECONDS = self.orig_max_run
        try:
            os.remove(runner._heartbeat_path())
        except OSError:
            pass

    def _write_heartbeat(self, pid, pgid, started):
        with open(runner._heartbeat_path(), "w") as f:
            json.dump({"pid": pid, "pgid": pgid, "started": started.isoformat()}, f)

    def test_reclaimStaleLock_withFreshHeartbeat_returnsFalse(self):
        self._write_heartbeat(os.getpid(), os.getpgid(0), datetime.now(timezone.utc))
        self.assertFalse(runner._reclaim_stale_lock())

    def test_reclaimStaleLock_withoutHeartbeat_returnsFalse(self):
        try:
            os.remove(runner._heartbeat_path())
        except OSError:
            pass
        self.assertFalse(runner._reclaim_stale_lock())

    def test_reclaimStaleLock_withStaleHeartbeatAndDeadHolder_returnsTrue(self):
        runner.MAX_RUN_SECONDS = 3600
        dead = subprocess.Popen(["true"])
        dead.wait()
        self._write_heartbeat(dead.pid, dead.pid, datetime.now(timezone.utc) - timedelta(hours=2))
        with patch.object(runner, "send_email"):
            with patch.object(runner, "_should_send_rate_limited", return_value=False):
                self.assertTrue(runner._reclaim_stale_lock())

    def test_reclaimStaleLock_withStaleHeartbeatAndLiveHolder_killsProcessGroup(self):
        runner.MAX_RUN_SECONDS = 3600
        holder = subprocess.Popen(["sleep", "60"], start_new_session=True)
        self.addCleanup(holder.wait)
        self._write_heartbeat(holder.pid, os.getpgid(holder.pid), datetime.now(timezone.utc) - timedelta(hours=2))
        with patch.object(runner, "send_email"):
            with patch.object(runner, "_should_send_rate_limited", return_value=False):
                self.assertTrue(runner._reclaim_stale_lock())
        self.assertIsNotNone(holder.poll())

    def test_reclaimStaleLock_withHeartbeatPointingAtSelf_returnsFalse(self):
        runner.MAX_RUN_SECONDS = 3600
        self._write_heartbeat(os.getpid(), os.getpgid(0), datetime.now(timezone.utc) - timedelta(hours=2))
        self.assertFalse(runner._reclaim_stale_lock())


class QuotaResetHintTests(unittest.TestCase):
    """A provider that states its reset window must not be retried inside it.

    agy embeds "Resets in 85h0m6s" in its 429 payload. The flat
    QUOTA_COOLDOWN_MINUTES fallback (60) re-selected, re-launched, and re-failed
    those models every hour for the whole 85-hour window, burning a mission
    launch per cycle instead of handing off to a working model.
    """
    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def _rewind_marks(self, prefix: str, minutes: int) -> None:
        """Push the ``at`` stamp past the flat cooldown, leaving ``resets_at`` intact.

        Load-bearing: without rewinding, the flat fallback alone would satisfy an
        "is exhausted" assertion, so the test would pass even if the stated
        window were ignored entirely.
        """
        stale = (datetime.now(timezone.utc) - timedelta(minutes=minutes)).isoformat()
        for name in os.listdir(runner.STATE_DIR):
            if not name.startswith(prefix):
                continue
            path = os.path.join(runner.STATE_DIR, name)
            with open(path) as f:
                state = json.load(f)
            state["at"] = stale
            with open(path, "w") as f:
                json.dump(state, f)

    def test_statedReset_isHonoredPastTheFlatCooldown(self):
        runner.mark_model_exhausted(
            "agy", "agy/claude-sonnet-4-6",
            stderr="error: Individual quota reached. Resets in 85h0m6s.",
        )
        # Simulate a fresh cron spawn: in-memory state wiped.
        runner.EXHAUSTED_MODELS.clear()
        self.assertGreater(runner.QUOTA_COOLDOWN_MINUTES, 0)
        self._rewind_marks("quota-agy-", runner.QUOTA_COOLDOWN_MINUTES + 5)
        # Only the provider-stated window can still hold the model here.
        self.assertTrue(runner.is_model_exhausted("agy", "agy/claude-sonnet-4-6"))

    def test_statedReset_expiresAfterItsOwnWindow(self):
        runner.mark_model_exhausted("agy", "agy/claude-sonnet-4-6", stderr="Resets in 2h0m0s.")
        runner.EXHAUSTED_MODELS.clear()
        past = (datetime.now(timezone.utc) - timedelta(minutes=1)).isoformat()
        for name in os.listdir(runner.STATE_DIR):
            if not name.startswith("quota-agy-"):
                continue
            path = os.path.join(runner.STATE_DIR, name)
            with open(path) as f:
                state = json.load(f)
            state["resets_at"] = past
            with open(path, "w") as f:
                json.dump(state, f)
        self.assertFalse(runner.is_model_exhausted("agy", "agy/claude-sonnet-4-6"))

    def test_withoutResetHint_theFlatCooldownStillGoverns(self):
        # A provider that states no window must keep the existing behavior.
        runner.mark_model_exhausted("opencode", "opencode/big-pickle")
        runner.EXHAUSTED_MODELS.clear()
        self.assertTrue(runner.is_model_exhausted("opencode", "opencode/big-pickle"))
        with open(runner._quota_state_path("opencode", "opencode/big-pickle")) as f:
            self.assertNotIn("resets_at", json.load(f))

    def test_resetHint_isCappedSoItCannotParkAModelIndefinitely(self):
        runner.mark_model_exhausted("agy", "agy/claude-opus-4-6-thinking", stderr="Resets in 9999h0m0s.")
        with open(runner._quota_state_path("agy", "agy/claude-opus-4-6-thinking")) as f:
            resets_at = datetime.fromisoformat(json.load(f)["resets_at"])
        ceiling = datetime.now(timezone.utc) + timedelta(hours=runner.QUOTA_RESET_MAX_HOURS)
        self.assertLessEqual(resets_at, ceiling + timedelta(minutes=1))

    def test_resetHint_isPersistedOnEveryLookupVariant(self):
        runner.mark_model_exhausted("agy", "agy/claude-sonnet-4-6", stderr="Resets in 5h0m0s.")
        runner.EXHAUSTED_MODELS.clear()
        self._rewind_marks("quota-agy-", runner.QUOTA_COOLDOWN_MINUTES + 5)
        # Suffix-tolerant lookup must see the stated window, not just the clean key.
        self.assertTrue(runner.is_model_exhausted("agy", "claude-sonnet-4-6"))

    def test_nonObjectQuotaState_isIgnoredRatherThanCrashing(self):
        # A malformed or truncated state file must not crash every cycle: this
        # runs during candidate selection, so an uncaught shape error here is a
        # crash loop, not a skipped mark.
        path = runner._quota_state_path("agy", "agy/claude-sonnet-4-6")
        for content in ["null", "[]", '"text"', "5"]:
            with self.subTest(content=content):
                with open(path, "w") as f:
                    f.write(content)
                self.assertFalse(runner.is_model_exhausted("agy", "agy/claude-sonnet-4-6"))

    def test_timezoneFreeResetStamp_fallsBackToTheFlatCooldown(self):
        # A hand-edited or corrupt state file can hold a stamp with no offset,
        # which cannot be compared against an aware "now". Treating it as
        # unusable keeps the flat fallback; guessing an offset would park the
        # model for an arbitrary span.
        runner.mark_model_exhausted("agy", "agy/claude-sonnet-4-6")
        runner.EXHAUSTED_MODELS.clear()
        path = runner._quota_state_path("agy", "agy/claude-sonnet-4-6")
        for stamp in ["2026-01-01T00:00:00", "2026-01-01T00:00:00.123456", "not-a-timestamp"]:
            with self.subTest(stamp=stamp):
                with open(path) as f:
                    state = json.load(f)
                state["resets_at"] = stamp
                with open(path, "w") as f:
                    json.dump(state, f)
                # The mark is minutes old, so the flat cooldown still governs.
                self.assertTrue(runner.is_model_exhausted("agy", "agy/claude-sonnet-4-6"))

    def test_timezoneAwareResetStamp_isStillHonored(self):
        # Guards the fallback above from swallowing the real feature: an offset
        # stamp in the future must still hold the model past the flat cooldown.
        runner.mark_model_exhausted("agy", "agy/claude-sonnet-4-6", stderr="Resets in 5h0m0s.")
        runner.EXHAUSTED_MODELS.clear()
        self._rewind_marks("quota-agy-", runner.QUOTA_COOLDOWN_MINUTES + 5)
        self.assertTrue(runner.is_model_exhausted("agy", "agy/claude-sonnet-4-6"))

    def test_resetHint_skipsUnusablePhraseForLaterValidOne(self):
        # An unusable phrase with no duration must not shadow a later valid one,
        # or the provider's real window is discarded and retried inside.
        self.assertEqual(
            runner._quota_reset_seconds(
                "Retrying shortly. Resets in 5 minutes.\n"
                'AGY_ERROR: {"status":"RESOURCE_EXHAUSTED","error_code":429} Resets in 85h0m6s.'
            ),
            85 * 3600 + 6,
        )

    def test_resetHint_comesFromTheErrorChannelOnly(self):
        # stdout is assistant text (including partial text from an interrupted
        # mission) and is deliberately not parsed: a quoted or unrelated reset
        # there would shorten or lengthen the real window. With no window in the
        # error text, the flat fallback governs.
        runner.mark_model_exhausted(
            "agy", "agy/claude-sonnet-4-6",
            stderr="429 quota exhausted; no reset window stated.",
        )
        with open(runner._quota_state_path("agy", "agy/claude-sonnet-4-6")) as f:
            self.assertNotIn("resets_at", json.load(f))

    def test_unparseableOrZeroReset_yieldsNoWindow(self):
        # "500ms" must not be read as 500 minutes: that would park a model for
        # 8h20m past its actual reset.
        for text in ["", "rate limit exceeded", "Resets in 0m0s", "Resets in 0h0m0s", "Resets in 500ms"]:
            with self.subTest(text=text):
                self.assertIsNone(runner._quota_reset_seconds(text))

    def test_resetHint_parsesEachComponentForm(self):
        cases = {
            "error: Individual quota reached. Resets in 85h0m6s.": 85 * 3600 + 6,
            "resets in 2h30m": 2 * 3600 + 30 * 60,
            "Resets in 45m": 45 * 60,
            "Resets in 90s": 90,
        }
        for text, expected in cases.items():
            with self.subTest(text=text):
                self.assertEqual(runner._quota_reset_seconds(text), expected)


class QuotaResetChannelTests(unittest.TestCase):
    """The runner must record the window from the provider's error channel.

    The channel choice lives at the call site, so this drives a real quota
    failure through the babysit fallback and asserts the persisted window. A
    decoy reset in assistant output must not become the recorded duration, or the
    model is parked for the wrong span.
    """
    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()
        self._candidates = list(runner.AGENT_CANDIDATES)
        self._plugins = dict(runner.AGENT_PLUGINS)

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()
        runner.AGENT_CANDIDATES[:] = self._candidates
        runner.AGENT_PLUGINS.clear()
        runner.AGENT_PLUGINS.update(self._plugins)

    def test_quotaFailure_recordsTheErrorChannelWindowNotAssistantOutput(self):
        plugin = MagicMock()
        plugin.run_mission.return_value = (
            1,
            "Earlier I hit a limit. Resets in 5m.",                 # assistant output decoy
            "error: Individual quota reached. Resets in 85h0m6s.",  # provider payload
        )
        runner.AGENT_PLUGINS.clear()
        runner.AGENT_PLUGINS["stub"] = plugin
        runner.AGENT_CANDIDATES[:] = [("stub", "stub/model-a")]

        with patch.object(runner, "_worktree_backoff_should_skip", return_value=False), \
             patch.object(runner, "_babysit_snapshot", return_value=("", -1, "")), \
             patch.object(runner, "is_model_exhausted", return_value=False), \
             patch.object(runner, "_record_worktree_failure"), \
             patch.object(runner, "_should_send_rate_limited", return_value=False):
            runner._babysit_with_fallback("prompt", "/tmp/wt", "branch", 1)

        with open(runner._quota_state_path("stub", "stub/model-a")) as f:
            state = json.load(f)
        self.assertIn("resets_at", state)
        expected = datetime.now(timezone.utc) + timedelta(hours=85, seconds=6)
        actual = datetime.fromisoformat(state["resets_at"])
        self.assertLess(abs((actual - expected).total_seconds()), 120)


class QuotaPersistenceTests(unittest.TestCase):
    def setUp(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def tearDown(self):
        runner.EXHAUSTED_MODELS.clear()
        _clear_quota_state()

    def test_markModelExhausted_persistsAcrossCronCycles(self):
        runner.mark_model_exhausted("opencode", "opencode/big-pickle")
        # Simulate a fresh cron spawn: in-memory state wiped.
        runner.EXHAUSTED_MODELS.clear()
        self.assertTrue(runner.is_model_exhausted("opencode", "opencode/big-pickle"))
        # Suffix-tolerant lookup also consults the persisted mark.
        self.assertTrue(runner.is_model_exhausted("opencode", "google/big-pickle"))
        # Sibling model unaffected.
        self.assertFalse(runner.is_model_exhausted("opencode", "opencode/nemotron-3-ultra-free"))

    def test_isModelExhausted_afterCooldownExpiry_returnsFalse(self):
        runner.mark_model_exhausted("opencode", "opencode/big-pickle")
        runner.EXHAUSTED_MODELS.clear()
        # Rewind every persisted variant (clean key + suffix) past the cooldown.
        expired = (datetime.now(timezone.utc) - timedelta(minutes=runner.QUOTA_COOLDOWN_MINUTES + 5)).isoformat()
        for name in os.listdir(runner.STATE_DIR):
            if not (name.startswith("quota-opencode-") and name.endswith(".json")):
                continue
            path = os.path.join(runner.STATE_DIR, name)
            with open(path) as f:
                state = json.load(f)
            state["at"] = expired
            with open(path, "w") as f:
                json.dump(state, f)
        self.assertFalse(runner.is_model_exhausted("opencode", "opencode/big-pickle"))

    def test_isModelExhausted_withCooldownDisabled_ignoresPersistedMarks(self):
        runner.mark_model_exhausted("opencode", "opencode/big-pickle")
        runner.EXHAUSTED_MODELS.clear()
        with patch.object(runner, "QUOTA_COOLDOWN_MINUTES", 0):
            self.assertFalse(runner.is_model_exhausted("opencode", "opencode/big-pickle"))

    def test_markModelExhausted_withAgentLevelExhaustion_persistsDefaultKey(self):
        runner.mark_model_exhausted("agy", None)
        runner.EXHAUSTED_MODELS.clear()
        self.assertTrue(runner.is_model_exhausted("agy", None))

    def test_markModelExhausted_withPersistFalse_staysInMemoryOnly(self):
        # Transient health-probe failures must not lock a model out of the
        # next cron cycle.
        runner.mark_model_exhausted("opencode", "opencode/big-pickle", persist=False)
        self.assertTrue(runner.is_model_exhausted("opencode", "opencode/big-pickle"))
        runner.EXHAUSTED_MODELS.clear()
        self.assertFalse(runner.is_model_exhausted("opencode", "opencode/big-pickle"))
        self.assertFalse(os.path.exists(runner._quota_state_path("opencode", "opencode/big-pickle")))

    def test_rateKey_withDifferentPrNumbers_yieldsDistinctKeys(self):
        # Digits were previously stripped, merging PR #12 and #13 into one
        # rate-limit counter (cross-PR alert suppression).
        self.assertNotEqual(runner._rate_key("[Runner] Hanging CI Alert: PR #12"),
                            runner._rate_key("[Runner] Hanging CI Alert: PR #13"))
        subject = "[Runner] Hanging CI Alert: PR #12"
        first_key = runner._rate_key(subject)
        second_key = runner._rate_key(subject)
        self.assertEqual(first_key, second_key)


class StateSweepTests(unittest.TestCase):
    def setUp(self):
        _clear_quota_state()

    def tearDown(self):
        _clear_quota_state()

    def _touch(self, name, age_days):
        path = os.path.join(runner.STATE_DIR, name)
        with open(path, "w") as f:
            f.write("{}")
        old = time.time() - age_days * 86400
        os.utime(path, (old, old))
        return path

    def test_pruneStaleStateFiles_withOldFiles_deletesThem(self):
        for name in ("issue-9.json", "pr-9-rerun.json", "pr-9-coderabbit-gate.json",
                     "quota-agy-default.json", "worktree-old-branch-backoff.json"):
            self._touch(name, runner.STATE_MAX_AGE_DAYS + 1)
        fresh = self._touch("issue-10.json", 1)
        # Injection markers are sticky by design: only manual removal
        # re-enables pickup of a flagged issue.
        sticky = self._touch("issue-9-injection.json", runner.STATE_MAX_AGE_DAYS + 5)
        runner._prune_stale_state_files()
        for name in ("issue-9.json", "pr-9-rerun.json", "pr-9-coderabbit-gate.json",
                     "quota-agy-default.json", "worktree-old-branch-backoff.json"):
            self.assertFalse(os.path.exists(os.path.join(runner.STATE_DIR, name)), name)
        self.assertTrue(os.path.exists(fresh))
        self.assertTrue(os.path.exists(sticky))

    def test_pruneStaleStateFiles_withActiveWorktreeIssue_protectsItsState(self):
        with tempfile.TemporaryDirectory() as wt_base:
            os.makedirs(os.path.join(wt_base, "issue-777"))
            stale = self._touch("issue-777.json", runner.STATE_MAX_AGE_DAYS + 5)
            with patch.object(runner, "WORKTREES_BASE", wt_base):
                runner._prune_stale_state_files()
            self.assertTrue(os.path.exists(stale))

    def test_pruneStaleStateFiles_ignoresRateAndHeartbeatFiles(self):
        kept = self._touch("rate-anything.json", runner.STATE_MAX_AGE_DAYS + 5)
        runner._prune_stale_state_files()
        self.assertTrue(os.path.exists(kept))


class GhOutageGuardTests(unittest.TestCase):
    def test_ghIsFunctional_whenProbeSucceeds_returnsTrue(self):
        with patch.object(runner, "run_cmd", return_value=(0, "4999", "")):
            self.assertTrue(runner._gh_is_functional())

    def test_ghIsFunctional_whenProbeFails_returnsFalseAndAlerts(self):
        with patch.object(runner, "run_cmd", return_value=(1, "", "dial tcp: no route")):
            with patch.object(runner, "send_email") as mock_mail:
                with patch.object(runner, "_should_send_rate_limited", return_value=True):
                    self.assertFalse(runner._gh_is_functional())
        mock_mail.assert_called_once()

    def test_babysitActiveWorktrees_whenGhUnavailable_skipsMissionDispatch(self):
        with tempfile.TemporaryDirectory() as wt_base:
            os.makedirs(os.path.join(wt_base, "issue-900"))

            def fake_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "branch"] and "--show-current" in args:
                    return 0, "feat/ISSUE-900-test\n", ""
                if args[:3] == ["gh", "pr", "view"]:
                    return 1, "", "gh: no route to host"
                if args[:2] == ["gh", "api"] and "rate_limit" in args:
                    return 1, "", "gh: no route to host"
                return 0, "", ""

            mock_plugin = MagicMock()
            with (
                patch.object(runner, "WORKTREES_BASE", wt_base),
                patch.object(runner, "run_cmd", side_effect=fake_run_cmd),
                patch.dict(runner.AGENT_PLUGINS, {"opencode": mock_plugin}, clear=True),
                patch.object(runner, "AGENT_CANDIDATES", [("opencode", "opencode/big-pickle")]),
                patch.object(runner, "_load_pr_state", return_value=None),
                patch.object(runner, "send_email"),
                patch.object(runner, "_should_send_rate_limited", return_value=False),
            ):
                runner.babysit_active_worktrees()
            mock_plugin.run_mission.assert_not_called()
            self.assertTrue(os.path.isdir(os.path.join(wt_base, "issue-900")))

    def test_babysitActiveWorktrees_withUnparseablePrJson_skipsWorktreeWithoutCrash(self):
        with tempfile.TemporaryDirectory() as wt_base:
            os.makedirs(os.path.join(wt_base, "issue-901"))

            def fake_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "branch"] and "--show-current" in args:
                    return 0, "feat/ISSUE-901-test\n", ""
                if args[:3] == ["gh", "pr", "view"]:
                    return 0, "gh: warning output that is not json{{{", ""
                if args[:2] == ["gh", "api"] and "rate_limit" in args:
                    return 0, "4999", ""
                return 0, "", ""

            mock_plugin = MagicMock()
            with (
                patch.object(runner, "WORKTREES_BASE", wt_base),
                patch.object(runner, "run_cmd", side_effect=fake_run_cmd),
                patch.dict(runner.AGENT_PLUGINS, {"opencode": mock_plugin}, clear=True),
                patch.object(runner, "AGENT_CANDIDATES", [("opencode", "opencode/big-pickle")]),
                patch.object(runner, "send_email"),
            ):
                # Must not raise.
                runner.babysit_active_worktrees()
            mock_plugin.run_mission.assert_not_called()

    def test_babysitActiveWorktrees_whenCoderabbitCliMissing_skipsMissionDispatch(self):
        with tempfile.TemporaryDirectory() as wt_base:
            os.makedirs(os.path.join(wt_base, "issue-902"))

            def fake_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "branch"] and "--show-current" in args:
                    return 0, "feat/ISSUE-902-test\n", ""
                if args[:3] == ["gh", "pr", "view"]:
                    return 1, "", "no pull requests found"
                if args[:2] == ["gh", "api"] and "rate_limit" in args:
                    return 0, "4999", ""
                if args[:3] == ["gh", "issue", "view"]:
                    return 0, '{"state": "OPEN"}', ""
                return 0, "", ""

            mock_plugin = MagicMock()
            with (
                patch.object(runner, "WORKTREES_BASE", wt_base),
                patch.object(runner, "run_cmd", side_effect=fake_run_cmd),
                patch.dict(runner.AGENT_PLUGINS, {"opencode": mock_plugin}, clear=True),
                patch.object(runner, "AGENT_CANDIDATES", [("opencode", "opencode/big-pickle")]),
                patch.object(runner, "_load_pr_state", return_value=None),
                patch.object(runner, "_fetch_issue_body", return_value=("Issue 902", "body")),
                patch.object(runner, "_branch_has_commits", return_value=True),
                patch.object(runner, "_worktree_is_clean", return_value=True),
                patch.object(runner, "_latest_commit_is_wip", return_value=False),
                patch.object(runner, "_coderabbit_bin", return_value=None),
                patch.object(runner, "send_email"),
                patch.object(runner, "_should_send_rate_limited", return_value=False),
            ):
                runner.babysit_active_worktrees()
            mock_plugin.run_mission.assert_not_called()


class WorktreeBackoffTests(unittest.TestCase):
    BRANCH = "feat/ISSUE-903-test"

    def setUp(self):
        _clear_quota_state()

    def tearDown(self):
        _clear_quota_state()

    def test_backoff_belowThreshold_neverSkips(self):
        for _ in range(2):
            runner._record_worktree_failure(self.BRANCH)
        self.assertFalse(runner._worktree_backoff_should_skip(self.BRANCH))

    def test_backoff_afterThreeFailures_skipsMissions(self):
        for _ in range(3):
            runner._record_worktree_failure(self.BRANCH)
        self.assertTrue(runner._worktree_backoff_should_skip(self.BRANCH))

    def test_backoff_progressClearsState(self):
        for _ in range(5):
            runner._record_worktree_failure(self.BRANCH)
        runner._record_worktree_progress(self.BRANCH)
        self.assertFalse(runner._worktree_backoff_should_skip(self.BRANCH))

    def test_backoff_afterCooldownElapse_proceedsAndKeepsCountForEscalation(self):
        state = {"failures": 8, "last_failure": (datetime.now(timezone.utc) - timedelta(hours=7)).isoformat()}
        with open(runner._worktree_backoff_path(self.BRANCH), "w") as f:
            json.dump(state, f)
        self.assertFalse(runner._worktree_backoff_should_skip(self.BRANCH))
        with open(runner._worktree_backoff_path(self.BRANCH)) as f:
            kept = json.load(f)
        self.assertEqual(kept["failures"], 8)

    def test_backoff_cooldownEscalatesWithFailureCount(self):
        # 4 failures → 16-minute cooldown: 10 minutes in, still skipping.
        state = {"failures": 4, "last_failure": (datetime.now(timezone.utc) - timedelta(minutes=10)).isoformat()}
        with open(runner._worktree_backoff_path(self.BRANCH), "w") as f:
            json.dump(state, f)
        self.assertTrue(runner._worktree_backoff_should_skip(self.BRANCH))
        # 17 minutes in, the 16-minute cooldown has been served.
        state = {"failures": 4, "last_failure": (datetime.now(timezone.utc) - timedelta(minutes=17)).isoformat()}
        with open(runner._worktree_backoff_path(self.BRANCH), "w") as f:
            json.dump(state, f)
        self.assertFalse(runner._worktree_backoff_should_skip(self.BRANCH))

    def test_babysitWithFallback_whenCoolingDown_doesNotDispatchMission(self):
        for _ in range(3):
            runner._record_worktree_failure(self.BRANCH)
        mock_plugin = MagicMock()
        with (
            patch.dict(runner.AGENT_PLUGINS, {"opencode": mock_plugin}, clear=True),
            patch.object(runner, "AGENT_CANDIDATES", [("opencode", "opencode/big-pickle")]),
            patch.object(runner, "_babysit_snapshot", return_value=("", -1, "")),
            patch.object(runner, "send_email"),
        ):
            runner._babysit_with_fallback("prompt", "/tmp/wt", self.BRANCH, 903)
        mock_plugin.run_mission.assert_not_called()

    def test_babysitWithFallback_whenAllCandidatesFail_recordsFailure(self):
        mock_plugin = MagicMock()
        mock_plugin.run_mission.return_value = (1, "", "Error: unrelated failure")
        with (
            patch.dict(runner.AGENT_PLUGINS, {"opencode": mock_plugin}, clear=True),
            patch.object(runner, "AGENT_CANDIDATES", [("opencode", "opencode/big-pickle")]),
            patch.object(runner, "_babysit_snapshot", return_value=("", -1, "")),
            patch.object(runner, "send_email"),
            patch.object(runner, "_should_send_rate_limited", return_value=False),
        ):
            runner._babysit_with_fallback("prompt", "/tmp/wt", self.BRANCH, 903)
        with open(runner._worktree_backoff_path(self.BRANCH)) as f:
            state = json.load(f)
        self.assertEqual(state["failures"], 1)


class RefreshModelsGuardTests(unittest.TestCase):
    def test_refreshModelsTable_withAllDefaultDiscovery_abortsInsteadOfWiping(self):
        with tempfile.TemporaryDirectory() as tmp:
            prefs = os.path.join(tmp, "AGENT_PREFERENCES.md")
            with open(prefs, "w") as f:
                f.write("| Agent | Model | Priority |\n|-------|-------|----------|\n| `agy` | `default` | 1|\n")
            with (
                patch.object(runner, "PREFERENCES_PATH", prefs),
                patch.object(runner, "list_agent_models", return_value={"agy": ["default"], "claude": ["default"]}),
            ):
                runner.refresh_models_table()
            with open(prefs) as f:
                content = f.read()
        self.assertIn("`agy` | `default` | 1", content)
        self.assertNotIn("How it works", content)


class OpenCodeEnduranceTests(unittest.TestCase):
    """opencode plugin: process-group cleanup, stdout context errors, poison markers."""

    def test_isContextLengthError_withStdoutTailOnly_returnsTrue(self):
        from plugins import opencode
        stdout = "transcript...\nError: Your request resulted in more tokens than this model's maximum context length allows."
        self.assertTrue(opencode._is_context_length_error("", stdout))
        self.assertTrue(opencode._is_context_length_error("Error: prompt is too long", ""))

    def test_isContextLengthError_withKeywordOutsideStdoutTail_returnsFalse(self):
        from plugins import opencode
        # Keyword buried early in a long transcript must not match: the agent
        # may legitimately print file content that contains the phrase.
        stdout = "maximum context length" + "x" * 5000
        self.assertFalse(opencode._is_context_length_error("", stdout))

    def test_invokeOpencode_onTimeout_killsWholeProcessGroup(self):
        from plugins import opencode
        with tempfile.TemporaryDirectory() as tmp:
            marker = os.path.join(tmp, "child-alive.txt")
            fake_cli = os.path.join(tmp, "opencode")
            with open(fake_cli, "w") as f:
                f.write(
                    "#!/bin/sh\n"
                    "(while true; do echo hi >> \"$OPENCODE_CHILD_MARKER\"; sleep 0.5; done) &\n"
                    "sleep 60\n"
                )
            os.chmod(fake_cli, 0o755)
            env = {
                "PATH": tmp + os.pathsep + os.environ.get("PATH", ""),
                "OPENCODE_CHILD_MARKER": marker,
            }
            with patch.dict(os.environ, env):
                with patch.object(opencode, "MISSION_TIMEOUT", 2):
                    code, _, err = opencode._invoke_opencode(["opencode", "run", "--auto", "x"], tmp)
            self.assertEqual(code, -1)
            self.assertIn("hung", err)
            # The orphaned child must be dead: no further marker writes after
            # the kill. (p.kill() alone would leave it holding the pipes.)
            size_after_kill = os.path.getsize(marker)
            time.sleep(1.6)
            self.assertEqual(os.path.getsize(marker), size_after_kill)

    def test_findLatestSession_withPoisonedLatestSession_skipsToUnpoisoned(self):
        import sqlite3
        from plugins import opencode
        with tempfile.TemporaryDirectory() as tmp:
            db_path = os.path.join(tmp, "opencode.db")
            wt_dir = os.path.join(tmp, "worktree-910")
            os.makedirs(wt_dir)
            conn = sqlite3.connect(db_path)
            conn.execute(
                "CREATE TABLE session_v2 ("
                "id TEXT PRIMARY KEY, parent_id TEXT, directory TEXT, "
                "time_updated INTEGER, time_archived INTEGER)"
            )
            conn.execute("INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)", ("ses_old", None, os.path.abspath(wt_dir), 100, None))
            conn.execute("INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)", ("ses_new", None, os.path.abspath(wt_dir), 200, None))
            conn.commit()
            conn.close()
            with patch.dict(os.environ, {"OPENCODE_DB_PATH": db_path, "STATE_DIR": os.path.join(tmp, "state")}):
                self.assertEqual(opencode._find_latest_session_for_cwd(wt_dir), "ses_new")
                opencode._mark_session_poisoned("ses_new", "context overflow; archive failed")
                self.assertEqual(opencode._find_latest_session_for_cwd(wt_dir), "ses_old")
                opencode._mark_session_poisoned("ses_old", "context overflow; archive failed")
                self.assertIsNone(opencode._find_latest_session_for_cwd(wt_dir))

    def test_runMission_whenArchiveFails_marksSessionPoisonedAndStartsFresh(self):
        import sqlite3
        from plugins import opencode
        with tempfile.TemporaryDirectory() as tmp:
            db_path = os.path.join(tmp, "opencode.db")
            state_dir = os.path.join(tmp, "state")
            wt_dir = os.path.join(tmp, "worktree-911")
            os.makedirs(wt_dir)
            conn = sqlite3.connect(db_path)
            conn.execute(
                "CREATE TABLE session_v2 ("
                "id TEXT PRIMARY KEY, parent_id TEXT, directory TEXT, "
                "time_updated INTEGER, time_archived INTEGER)"
            )
            conn.execute(
                "CREATE TABLE session_message ("
                "id TEXT PRIMARY KEY, session_id TEXT NOT NULL, type TEXT NOT NULL, "
                "seq INTEGER NOT NULL, time_created INTEGER NOT NULL, "
                "time_updated INTEGER NOT NULL, data TEXT NOT NULL)"
            )
            conn.execute("INSERT INTO session_v2 VALUES (?, ?, ?, ?, ?)", ("ses_911", None, os.path.abspath(wt_dir), 100, None))
            conn.execute(
                "INSERT INTO session_message VALUES (?, ?, ?, ?, ?, ?, ?)",
                ("msg_1", "ses_911", "assistant", 1, 100, 100,
                 json.dumps({"finish": "stop", "model": {"providerID": "opencode", "id": "nemotron-3-ultra-free"}})),
            )
            conn.commit()
            conn.close()

            with patch.dict(os.environ, {"OPENCODE_DB_PATH": db_path, "STATE_DIR": state_dir}):
                with patch.object(opencode, "_archive_session", return_value=False):
                    with patch("subprocess.Popen") as mock_popen:
                        proc_fail = MagicMock()
                        proc_fail.communicate.return_value = (
                            "",
                            "Error: Your request resulted in more tokens than this model's maximum context length allows.",
                        )
                        proc_fail.returncode = 1
                        proc_fresh = MagicMock()
                        proc_fresh.communicate.return_value = ("fresh session ok", "")
                        proc_fresh.returncode = 0
                        mock_popen.side_effect = [proc_fail, proc_fail, proc_fresh]

                        code, out, _ = opencode.run_mission("resume issue 911", wt_dir, is_continue=True, model="opencode/big-pickle")
                        self.assertEqual(code, 0)
                        self.assertEqual(out, "fresh session ok")
                        self.assertEqual(mock_popen.call_count, 3)

                        marker = opencode._poison_marker_path("ses_911")
                        self.assertTrue(os.path.exists(marker))
                        # The unarchivable session is never resumed again.
                        self.assertIsNone(opencode._find_latest_session_for_cwd(wt_dir))


if __name__ == "__main__":
    unittest.main()
