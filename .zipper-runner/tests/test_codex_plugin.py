"""Check the Codex CLI contract through real child processes."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

BASE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("codex_plugin", BASE / "plugins/codex.py")
plugin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plugin)


class CodexPluginTests(unittest.TestCase):
    def test_health_and_missions_use_sol_with_low_effort(self):
        self.assertIn("openai/gpt-6.1-sol", plugin.list_models())
        with tempfile.TemporaryDirectory() as directory:
            executable = Path(directory) / "codex"
            executable.write_text(
                "#!/usr/bin/env python3\n"
                "import json, os, sys\n"
                "from pathlib import Path\n"
                "Path(os.environ['CODEX_TEST_INVOCATION']).write_text(json.dumps(sys.argv[1:]))\n"
                "print('ok')\n"
            )
            executable.chmod(0o755)
            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"],
                                         "CODEX_TEST_INVOCATION": str(Path(directory) / "invocation.json")}):
                self.assertTrue(plugin.check_token_health())
                args = json.loads((Path(directory) / "invocation.json").read_text())
                self.assertEqual(args, ["exec", "--ephemeral", "-m", "gpt-6.1-sol", "-c", 'model_reasoning_effort="low"', "say ok"])
                for model in (None, "default", "openai/gpt-6.1-sol", "openai/gpt-5.5"):
                    code, _, err = plugin.run_mission("Keep quotes ' and $ literal", directory, model=model)
                    self.assertEqual((code, err), (0, ""))
                    argv = json.loads((Path(directory) / "invocation.json").read_text())
                    self.assertEqual(argv, ["exec", "--ephemeral", "-m", "gpt-5.5" if model == "openai/gpt-5.5" else "gpt-6.1-sol", "-c", 'model_reasoning_effort="low"'])
