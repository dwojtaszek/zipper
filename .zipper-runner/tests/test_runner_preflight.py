"""Exercise preflight against real repositories and executable boundaries."""
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

BASE = Path(__file__).resolve().parents[1]


class PreflightTests(unittest.TestCase):
    def test_preflight_checks_tools_repository_auth_and_sdk(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(['git', 'init', str(root)], check=True, capture_output=True)
            binary = root / 'bin'
            binary.mkdir()
            (binary / 'git').symlink_to(shutil.which('git'))
            for name, text in [('dotnet', '#!/bin/sh\necho 10.0.402\n'),
                               ('gh', '#!/bin/sh\nexit 0\n')]:
                path = binary / name
                path.write_text(text)
                path.chmod(0o755)
            spec = importlib.util.spec_from_file_location('preflight_runner', BASE / 'runner.py')
            runner = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(runner)
            runner.REPO_PATH = str(root)
            previous = os.getcwd()
            try:
                os.chdir(root)
                with patch.dict(os.environ, {'PATH': str(binary)}):
                    self.assertTrue(runner.runtime_preflight())
                    (binary / 'gh').write_text('#!/bin/sh\nexit 1\n')
                    self.assertFalse(runner.runtime_preflight())
                    (binary / 'dotnet').unlink()
                    self.assertFalse(runner.runtime_preflight())
                os.chdir(root.parent)
                self.assertFalse(runner.runtime_preflight())
            finally:
                os.chdir(previous)

    def test_cli_preflight_fails_without_a_healthy_agent_and_never_dispatches(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(['git', 'init', str(root)], check=True, capture_output=True)
            binary = root / 'bin'
            binary.mkdir()
            (binary / 'git').symlink_to(shutil.which('git'))
            for name in ('dotnet', 'gh'):
                executable = binary / name
                executable.write_text('#!/bin/sh\nexit 0\n')
                executable.chmod(0o755)
            plugins = root / 'plugins'
            plugins.mkdir()
            (plugins / 'probe.py').write_text(
                'import os\nfrom pathlib import Path\n'
                'def check_installation(): return True\n'
                'def check_token_health(): return os.environ["PROBE_HEALTHY"] == "1"\n'
                'def list_models(): return ["default"]\n'
                'def run_mission(*args, **kwargs): Path("FORBIDDEN").touch(); raise AssertionError("dispatch")\n')
            (root / 'AGENT_PREFERENCES.md').write_text('| Agent | Model | Priority |\n| probe | default | 1 |\n')
            env = {**os.environ, 'RUNNER_BASE': directory, 'REPO_PATH': directory,
                   'PLUGINS_DIR': str(plugins), 'STATE_DIR': str(root / 'state'),
                   'PATH': str(binary) + os.pathsep + os.environ['PATH']}
            for healthy in ('0', '1'):
                result = subprocess.run([sys.executable, str(BASE / 'runner.py'), '--preflight'],
                                        cwd=root, env={**env, 'PROBE_HEALTHY': healthy},
                                        capture_output=True, text=True, timeout=10)
                self.assertEqual(result.returncode, 0 if healthy == '1' else 1, result.stdout + result.stderr)
                self.assertFalse((root / 'FORBIDDEN').exists())
                self.assertFalse((root / 'runner.lock').exists())
            result = subprocess.run([sys.executable, str(BASE / 'runner.py'), '--preflight', '--dry-run'],
                                    cwd=root, env=env, capture_output=True, text=True, timeout=10)
            self.assertEqual(result.returncode, 2)

    def test_cron_launcher_sets_repository_cwd_and_preserves_preflight_status(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            base = root / '.zipper-runner'
            base.mkdir()
            shutil.copy(BASE / 'cron-wrapper.sh', base / 'cron-wrapper.sh')
            (base / 'runner.py').write_text(
                'import os, pathlib\n'
                'assert pathlib.Path.cwd() == pathlib.Path(__file__).resolve().parent.parent\n'
                'print("PREFLIGHT_REACHED")\n'
                'raise SystemExit(7)\n')
            result = subprocess.run(['bash', str(base / 'cron-wrapper.sh'), '--preflight'],
                                    cwd=root.parent, text=True, capture_output=True)
            self.assertEqual(result.returncode, 7, result.stdout + result.stderr)
            self.assertIn('PREFLIGHT_REACHED', result.stdout)
