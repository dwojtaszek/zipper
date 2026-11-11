#!/usr/bin/env python3
"""Validate candidate hooks in a clone with independent Git metadata before installation."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
INPUTS = (
    '.github/hooks/pre-commit', '.github/hooks/post-commit', '.github/hooks/pre-push',
    '.github/scripts/change-scope.py', '.github/scripts/run-check.py',
    '.github/scripts/validate-hooks.py', 'tests/ci/test_change_scope.py', 'dev.sh',
)


def main():
    env = dict(os.environ)
    for name in subprocess.check_output(['git', 'rev-parse', '--local-env-vars'], cwd=ROOT, text=True).splitlines():
        env.pop(name, None)
    digest = hashlib.sha256()
    for name in INPUTS:
        digest.update(name.encode())
        digest.update((ROOT / name).read_bytes())
    fingerprint = digest.hexdigest()
    git_path = subprocess.check_output(['git', 'rev-parse', '--git-path', 'zipper-hooks/hook-canary.sha256'],
                                       cwd=ROOT, env=env, text=True).strip()
    stamp = ROOT / git_path
    if stamp.is_file() and stamp.read_text() == fingerprint:
        return 0
    with tempfile.TemporaryDirectory(prefix='zipper-hook-canary-') as directory:
        clone = Path(directory) / 'repo'
        subprocess.run(['git', '-c', f'core.hooksPath={os.devnull}', 'clone', '--quiet', '--no-local',
                        '--depth', '1', '--single-branch', str(ROOT), str(clone)],
                       env=env, check=True, capture_output=True)
        for name in INPUTS:
            target = clone / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, target)
        command = [sys.executable, '-m', 'unittest',
                   'test_change_scope.HookInstallationCanaryTests.test_runner_gates_isolate_git_fixture_environment']
        env['PYTHONPATH'] = str(clone / 'tests/ci')
        env['PYTHONUTF8'] = '1'
        result = subprocess.run(command, cwd=clone, env=env, capture_output=True, text=True, encoding='utf-8', timeout=60)
        if result.returncode or 'skipped=' in result.stderr:
            print('Hook canary failed; installed hooks were left unchanged.', file=sys.stderr)
            print((result.stdout + result.stderr)[-8000:], file=sys.stderr)
            return result.returncode or 1
    stamp.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode='w', dir=stamp.parent, delete=False) as output:
        output.write(fingerprint)
        temporary = Path(output.name)
    temporary.replace(stamp)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
