#!/usr/bin/env python3
"""Tests for the agy plugin's mission timeout and health-probe fallbacks.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "agy_plugin", Path(__file__).resolve().parents[1] / "plugins" / "agy.py")
plugin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plugin)

# run_mission kills the process group after this many seconds.
PLUGIN_KILL_SECONDS = 2700


def _seconds(value: str) -> int:
    return int(value[:-1]) * {"s": 1, "m": 60, "h": 3600}[value[-1]]


class AgyMissionTimeoutTests(unittest.TestCase):
    def test_printTimeout_firesBeforeThePluginKill(self):
        """agy must stop itself, not be SIGKILLed mid-turn.

        When the nested print timeout exceeds the plugin kill, the agent is
        killed with a turn in progress and its partial work is never reported;
        30m did this routinely on long missions.
        """
        with tempfile.TemporaryDirectory() as directory:
            fake = Path(directory) / "agy"
            fake.write_text("#!/usr/bin/env python3\nimport json, sys\nprint(json.dumps(sys.argv[1:]))\n")
            fake.chmod(0o755)
            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"]}):
                code, out, err = plugin.run_mission(
                    "prompt", directory, is_continue=False, model="gemini-3.8-flash-high")
            self.assertEqual(code, 0, err)
            args = json.loads(out.splitlines()[0])
            self.assertIn("--print-timeout", args)
            stated = _seconds(args[args.index("--print-timeout") + 1])
            self.assertLess(stated, PLUGIN_KILL_SECONDS)
            # Not so short that the agent is cut off while it still has budget.
            self.assertGreaterEqual(stated, PLUGIN_KILL_SECONDS - 600)


class AgyHealthProbeFallbackTests(unittest.TestCase):
    def test_fallbackProbes_onlyModelsAgyStillOffers(self):
        """A removed model in the fallback list only burns a subprocess."""
        probed: list[str] = []

        def record(model):
            probed.append(model)
            return False

        with patch.object(plugin, "_probe_model", side_effect=record):
            self.assertFalse(plugin.check_token_health())
        self.assertEqual(probed, ["gemini-3.8-flash-high", "claude-sonnet-4-6", "claude-opus-4-6-thinking"])
        self.assertNotIn("gemini-3.7-flash-high", probed)

    def test_pinnedModel_probesOnlyThatModel(self):
        probed: list[str] = []

        def record(model):
            probed.append(model)
            return True

        with patch.object(plugin, "_probe_model", side_effect=record):
            self.assertTrue(plugin.check_token_health(model="claude-sonnet-4-6"))
        self.assertEqual(probed, ["claude-sonnet-4-6"])


if __name__ == "__main__":
    unittest.main()
