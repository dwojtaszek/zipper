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
with patch.object(plugin.subprocess, "Popen") as popen:
    popen.return_value.communicate.return_value = ("pong", "")
    popen.return_value.returncode = 0
    for model in [None, "gpt-6-sol"]:
        assert plugin.run_mission("test prompt", "/tmp", model=model)[0] == 0
        assert popen.call_args.kwargs["start_new_session"] is True
        args = popen.call_args.args[0]
        assert args == ["droid", "exec", "--model", model or "gpt-6-luna", "--reasoning-effort", "max", "--auto", "high", "--cwd", "/tmp", "test prompt"]
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
        code, out, err = plugin.run_mission(prompt, directory, is_continue=True, model="gpt-6-sol")
        assert code == 0 and not err
        args = json.loads(out.splitlines()[0])
        assert args == ["exec", "--model", "gpt-6-sol", "--reasoning-effort", "max", "--auto", "high", "--cwd", directory, prompt]
        assert plugin.check_token_health()
# A real timed-out agent and its child must stop before runner control returns.
with tempfile.TemporaryDirectory() as directory:
    marker = Path(directory) / "child-survived"
    fake = Path(directory) / "droid"
    fake.write_text("#!/usr/bin/env python3\nimport subprocess, sys, time\nsubprocess.Popen([sys.executable, '-c', \"import time; from pathlib import Path; time.sleep(0.5); Path(" + repr(str(marker)) + ").touch()\"])\nprint('started', flush=True)\ntime.sleep(30)\n")
    fake.chmod(0o755)
    original_popen = subprocess.Popen
    class ShortMission(original_popen):
        def communicate(self, input=None, timeout=None):
            return super().communicate(input=input, timeout=0.2 if timeout is not None else None)
    with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"]}), patch.object(plugin.subprocess, "Popen", ShortMission):
        code, out, err = plugin.run_mission("timeout check", directory)
        assert code == -1 and "timed out" in err and "started" in out
    time.sleep(0.6)
    assert not marker.exists(), "Timed-out Droid child survived process-group termination"
print("Droid plugin checks passed")
