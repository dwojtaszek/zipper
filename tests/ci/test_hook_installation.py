"""Candidate hooks must pass a real isolated canary before live installation."""
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('hook_validator', ROOT / '.github/scripts/validate-hooks.py')
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


@unittest.skipIf(os.name != 'nt' and not shutil.which('bash'), 'requires Git Bash')
class HookInstallationTests(unittest.TestCase):
    def test_candidates_are_checked_before_install_and_cache_tracks_dependencies(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / 'repo'
            env = {k: v for k, v in os.environ.items() if not k.startswith('GIT_')}
            env['PYTHONUTF8'] = '1'
            subprocess.run(['git', 'clone', '--quiet', '--no-local', '--depth', '1', str(ROOT), str(root)],
                           env=env, check=True, capture_output=True)
            for name in (*validator.INPUTS, 'setup-hook.sh', 'setup-hook.bat'):
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / name, target)
            installer = ['cmd.exe', '/d', '/c', 'setup-hook.bat', '--quiet'] if os.name == 'nt' else ['bash', 'setup-hook.sh', '--quiet']
            installed = root / '.git/hooks/pre-commit'
            installed.write_bytes(b'#!/bin/sh\n# original hook\nexit 0\n')
            installed.chmod(0o755)
            original = installed.read_bytes()
            candidate = root / '.github/hooks/pre-commit'
            valid = candidate.read_text(encoding='utf-8')
            candidate.write_bytes(valid.replace('while IFS= read -r git_env; do unset "$git_env"; done', ':').encode())
            config = (root / '.git/config').read_bytes()
            result = subprocess.run(installer, cwd=root, env=env,
                                    capture_output=True, text=True, encoding='utf-8')
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(installed.read_bytes(), original)
            self.assertEqual((root / '.git/config').read_bytes(), config)

            candidate.write_bytes(b'#!/bin/sh\nexit 0\n')
            result = subprocess.run(installer, cwd=root, env=env, capture_output=True, text=True, encoding='utf-8')
            self.assertNotEqual(result.returncode, 0, 'a no-op hook must fail the execution canary')
            self.assertEqual(installed.read_bytes(), original)
            candidate.write_bytes(valid.encode())
            post_commit = root / '.github/hooks/post-commit'
            valid_post = post_commit.read_text(encoding='utf-8')
            post_commit.write_bytes(b'#!/bin/sh\ngit config core.bare true\n')
            result = subprocess.run(installer, cwd=root, env=env, capture_output=True, text=True, encoding='utf-8')
            self.assertNotEqual(result.returncode, 0, 'unsafe post-commit must fail the canary')
            self.assertEqual(installed.read_bytes(), original)
            post_commit.write_bytes(valid_post.encode())
            result = subprocess.run(installer, cwd=root, env=env,
                                    capture_output=True, text=True, encoding='utf-8')
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(result.stdout, '')
            self.assertEqual(installed.read_bytes(), candidate.read_bytes())
            verbose = subprocess.run(installer[:-1], cwd=root, env=env,
                                     capture_output=True, text=True, encoding='utf-8')
            self.assertEqual(verbose.returncode, 0, verbose.stdout + verbose.stderr)
            self.assertIn('Done. Hooks installed', verbose.stdout)
            stamp = root / '.git/zipper-hooks/hook-canary.sha256'
            fingerprint = stamp.read_text(encoding='utf-8')
            (root / '.github/scripts/run-check.py').write_bytes(b'raise SystemExit(7)\n')
            result = subprocess.run(installer, cwd=root, env=env,
                                    capture_output=True, text=True, encoding='utf-8')
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(installed.read_text(encoding='utf-8'), valid)
            self.assertEqual(stamp.read_text(encoding='utf-8'), fingerprint)
