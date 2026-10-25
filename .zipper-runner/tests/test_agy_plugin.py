#!/usr/bin/env python3
"""Tests for the agy plugin's mission timeout and health-probe fallbacks.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "agy_plugin", Path(__file__).resolve().parents[1] / "plugins" / "agy.py")
plugin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plugin)

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402

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


class AgyIdleTurnInstructionTests(unittest.TestCase):
    """The intake prompts must forbid backgrounding a long command and idling.

    Measured on the host: agy hands a long command to a background task, its turn
    then has nothing to do, and it idles out the whole print timeout before
    returning partial output (`root agent idle; waiting up to 40m0s for 1
    background task(s)` in its own stderr). 15 runs did this; 11 of them ran over
    25 minutes. The work is usually finished by then, but the runner cannot tell,
    so it re-dispatches next cycle and burns the single worktree slot twice.

    Lowering the print timeout would recover the burn but truncate the runs that
    legitimately take 32-42 minutes, so the mitigation is the prompt. Both intake
    prompts are built inline inside `main()`, so the invariant is pinned on the
    source: a third prompt, or a refactor that drops the sentence, fails here.
    """

    AUTONOMY_TAIL = "make all technical decisions yourself using your best engineering judgment."

    def setUp(self):
        self.source = (Path(__file__).resolve().parents[1] / "runner.py").read_text(encoding="utf-8")

    def test_instruction_shouldNameBothTheForegroundRuleAndTheFailure(self):
        instruction = runner._LONG_COMMAND_INSTRUCTION
        self.assertIn("foreground", instruction)
        self.assertIn("background", instruction)
        self.assertIn("print timeout", instruction,
                      "the prompt must say what goes wrong, not just what to avoid")

    def test_everyIntakePrompt_shouldCarryTheInstruction(self):
        tails = self.source.count(self.AUTONOMY_TAIL)
        self.assertEqual(tails, 2, "the runner builds exactly two intake prompts")

        carried = 0
        start = 0
        while (index := self.source.find(self.AUTONOMY_TAIL, start)) != -1:
            start = index + 1
            if "_LONG_COMMAND_INSTRUCTION" in self.source[max(0, index - 800):index]:
                carried += 1
        self.assertEqual(carried, tails,
                         "every intake prompt must carry _LONG_COMMAND_INSTRUCTION; "
                         "a prompt without it can idle out the print timeout")


if __name__ == "__main__":
    unittest.main()
