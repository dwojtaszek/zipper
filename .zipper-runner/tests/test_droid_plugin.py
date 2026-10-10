#!/usr/bin/env python3
"""Run with python3 .zipper-runner/tests/test_droid_plugin.py."""
import importlib.util
import json
import os
import tempfile
import subprocess
import time
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("droid_plugin", Path(__file__).resolve().parents[1] / "plugins" / "droid.py")
plugin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plugin)
assert plugin.list_models() == ["gpt-6-luna"]
with patch.object(plugin.subprocess, "run") as run:
    run.return_value.returncode = 0
    run.return_value.stdout = "pong"
    run.return_value.stderr = ""
    assert plugin.check_token_health()
    args = run.call_args.args[0]
    assert args[2:6] == ["--model", "gpt-6-luna", "--reasoning-effort", "max"]
    run.return_value.returncode = 1
    run.return_value.stdout = ""
    run.return_value.stderr = "unauthorized"
    assert not plugin.check_token_health()
# Real child process validates argument transport without spending API credits.
with tempfile.TemporaryDirectory() as directory:
    fake = Path(directory) / "droid"
    fake.write_text("#!/usr/bin/env python3\nimport json, sys\nprint(json.dumps(sys.argv[1:]))\nprint('pong')\n")
    fake.chmod(0o755)
    with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"]}):
        prompt = "Keep spaces, 'quotes', and $variables literal."
        for model in [None, "gpt-6-sol"]:
            code, out, err = plugin.run_mission(prompt, directory, is_continue=True, model=model)
            assert code == 0
            assert not err
            args = json.loads(out.splitlines()[0])
            assert args == ["exec", "--model", model or "gpt-6-luna", "--reasoning-effort", "max", "--auto", "high", "--output-format", "stream-json", "--cwd", directory, prompt]
        assert plugin.check_token_health()
# Successful missions preserve background children that release captured pipes.
with tempfile.TemporaryDirectory() as directory:
    marker = Path(directory) / "child-survived"
    fake = Path(directory) / "droid"
    fake.write_text("#!/usr/bin/env python3\nimport subprocess, sys\nsubprocess.Popen([sys.executable, '-c', \"import time; from pathlib import Path; time.sleep(0.5); Path(" + repr(str(marker)) + ").touch()\"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)\nprint('done', flush=True)\n")
    fake.chmod(0o755)
    with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"]}):
        code, out, err = plugin.run_mission("success check", directory)
        assert (code, out, err) == (0, "done\n", "")
    time.sleep(0.8)
    assert marker.exists(), "Successful Droid mission killed its background child"
# A real timed-out agent and its child must stop before runner control returns.
with tempfile.TemporaryDirectory() as directory:
    marker = Path(directory) / "child-survived"
    fake = Path(directory) / "droid"
    fake.write_text("#!/usr/bin/env python3\nimport subprocess, sys, time\nsubprocess.Popen([sys.executable, '-c', \"import time; from pathlib import Path; time.sleep(0.5); Path(" + repr(str(marker)) + ").touch()\"])\nprint('started', flush=True)\ntime.sleep(30)\n")
    fake.chmod(0o755)
    with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"]}), patch.object(plugin, "MISSION_TIMEOUT", 0.2):
        code, out, err = plugin.run_mission("timeout check", directory)
        assert code == -1
        assert "timed out" in err
        assert "started" in out
    time.sleep(0.6)
    assert not marker.exists(), "Timed-out Droid child survived process-group termination"
print("Droid plugin checks passed")
