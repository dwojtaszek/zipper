"""Verify real Git change classification and bounded check status propagation."""
import os
import shutil
from contextlib import contextmanager
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCOPE = ROOT / '.github/scripts/change-scope.py'
CHECK = ROOT / '.github/scripts/run-check.py'


class ChangeScopeTests(unittest.TestCase):
    def test_runner_only_never_admits_product_or_gate_changes(self):
        for paths, expected in [
            ([], False),
            (['.zipper-runner/runner.py'], True),
            (['.zipper-runner/AGENT_PREFERENCES.md'], True),
            (['.zipper-runner/tests/test.py', 'README.md'], True),
            (['README.md'], False),
            (['.zipper-runner/runner.py', 'src/Program.cs'], False),
            (['.zipper-runner/runner.py', '.github/workflows/pr.yml'], False),
            (['.zipper-runner/runner.py', 'Directory.Build.props'], False),
            (['.zipper-runner/runner.py', 'src/docs/Program.cs'], False),
            (['.zipper-runner/../src/Program.cs'], False),
        ]:
            with self.subTest(paths=paths):
                result = subprocess.run([sys.executable, str(SCOPE)], input='\0'.join(paths),
                                        text=True, capture_output=True, check=True)
                self.assertIn('only_runner=' + str(expected).lower(), result.stdout)

    def test_staged_move_from_product_to_runner_remains_full_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args):
                return subprocess.run(['git', '-C', directory, *args], check=True, capture_output=True,
                                      env={**os.environ, 'GIT_AUTHOR_NAME': 'Test', 'GIT_AUTHOR_EMAIL': 'test@example.com',
                                           'GIT_COMMITTER_NAME': 'Test', 'GIT_COMMITTER_EMAIL': 'test@example.com'})
            git('init')
            (root / 'src').mkdir()
            (root / 'src/old.py').write_text('retained content')
            git('add', '.')
            git('commit', '-m', 'base')
            (root / '.zipper-runner').mkdir()
            git('mv', 'src/old.py', '.zipper-runner/moved.py')
            result = subprocess.run([sys.executable, str(SCOPE), '--staged'], cwd=root,
                                    text=True, capture_output=True, check=True)
            self.assertIn('only_runner=false', result.stdout)

    def test_failed_diff_cannot_produce_a_skip(self):
        result = subprocess.run([sys.executable, str(SCOPE), '--base', 'missing-ref'], cwd=ROOT,
                                text=True, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn('=true', result.stdout)


class BoundedCheckTests(unittest.TestCase):
    def test_keeps_full_log_and_nonzero_exit_without_dumping_large_lines(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'check.log'
            result = subprocess.run([sys.executable, str(CHECK), '--log', str(log), '--', sys.executable,
                                     '-c', 'print("x" * 100000); print("FAIL: actual failure"); raise SystemExit(7)'],
                                    text=True, capture_output=True)
            self.assertEqual(result.returncode, 7)
            self.assertGreater(log.stat().st_size, 100000)
            self.assertLess(len(result.stdout), 12000)
            self.assertIn('FAIL: actual failure', result.stdout)
            self.assertIn('exit_status=7', result.stdout)

    def test_reports_early_failure_even_when_cleanup_fills_tail(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, str(CHECK), '--log', str(Path(directory) / 'check.log'),
                                     '--', sys.executable, '-c',
                                     'print("FAIL: early failure"); [print("cleanup") for _ in range(100)]; raise SystemExit(3)'],
                                    text=True, capture_output=True)
            self.assertEqual(result.returncode, 3)
            self.assertIn('FAIL: early failure', result.stdout)
            self.assertLessEqual(len(result.stdout.splitlines()), 21)


@unittest.skipIf(os.name == 'nt' or shutil.which('bash') is None, 'POSIX hook boundary')
class RunnerHookTests(unittest.TestCase):
    @contextmanager
    def fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / 'repo'
            root.mkdir()
            env = {**os.environ, 'GIT_AUTHOR_NAME': 'Test', 'GIT_AUTHOR_EMAIL': 'test@example.com',
                   'GIT_COMMITTER_NAME': 'Test', 'GIT_COMMITTER_EMAIL': 'test@example.com',
                   'GIT_CONFIG_GLOBAL': os.devnull, 'GIT_CONFIG_NOSYSTEM': '1'}
            def git(*args):
                return subprocess.run(['git', *args], cwd=root, env=env, check=True, capture_output=True)
            git('init', '-b', 'main')
            git('config', 'core.hooksPath', os.devnull)
            (root / '.github/scripts').mkdir(parents=True)
            for script in (SCOPE, CHECK):
                shutil.copy(script, root / '.github/scripts' / script.name)
            shutil.copy(ROOT / 'dev.sh', root / 'dev.sh')
            (root / 'src/Zipper.Tests').mkdir(parents=True)
            (root / 'src/Zipper.Tests/Zipper.Tests.csproj').write_text('<Project />')
            (root / 'src/Foo.cs').write_text('class Foo {}')
            suite = root / '.zipper-runner/tests'
            suite.mkdir(parents=True)
            (root / '.zipper-runner/input.txt').write_text('expected')
            (root / '.zipper-runner/AGENT_PREFERENCES.md').write_text('baseline')
            (suite / 'test_outcome.py').write_text('import pathlib, unittest\nclass Outcome(unittest.TestCase):\n    def test_file(self):\n        self.assertEqual(pathlib.Path(".zipper-runner/input.txt").read_text(), "expected")\n')
            (root / 'tests').mkdir()
            for script in ('run-mutation-smoke.sh', 'run-e2e-basic.sh'):
                (root / 'tests' / script).write_text('#!/bin/sh\nexit 0\n')
            git('add', '.')
            git('commit', '-m', 'test: base')
            base = git('rev-parse', 'HEAD').stdout.decode().strip()
            git('update-ref', 'refs/remotes/origin/main', base)
            binary = Path(directory) / 'bin'
            binary.mkdir()
            dotnet = binary / 'dotnet'
            dotnet.write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$DOTNET_CALLS"\n')
            dotnet.chmod(0o755)
            calls = Path(directory) / 'dotnet-calls'
            env.update(PATH=str(binary) + os.pathsep + env['PATH'], DOTNET_CALLS=str(calls))
            yield root, env, git, calls, base

    def push(self, root, env, head, base, extra=False):
        refs = f'refs/heads/main {head} refs/heads/main {base}\n'
        if extra:
            refs += f'refs/heads/copy {head} refs/heads/copy {base}\n'
        return subprocess.run(['bash', str(ROOT / '.github/hooks/pre-push')], cwd=root, env=env,
                              input=refs, text=True, capture_output=True)

    def test_runner_only_commit_and_failure_run_real_runner_suite(self):
        for fail in (False, True):
            with self.subTest(fail=fail), self.fixture() as (root, env, git, calls, _):
                (root / '.zipper-runner/input.txt').write_text('unexpected' if fail else 'expected change')
                # A passing fixture reads the actual committed data file.
                if not fail:
                    (root / '.zipper-runner/input.txt').write_text('expected')
                pref = root / '.zipper-runner/AGENT_PREFERENCES.md'
                pref.write_text('changed')
                git('add', '.zipper-runner')
                result = subprocess.run(['bash', str(ROOT / '.github/hooks/pre-commit')], cwd=root,
                                        env=env, text=True, capture_output=True)
                self.assertEqual(result.returncode, 1 if fail else 0, result.stdout + result.stderr)
                self.assertIn('Ran 1 test', result.stdout)
                self.assertTrue(all('--filter FullyQualifiedName~DocsDriftTests' in line for line in calls.read_text().splitlines()))

    def test_runner_receipt_cannot_depend_on_untracked_helper(self):
        with self.fixture() as (root, env, git, _, _):
            test = root / '.zipper-runner/tests/test_outcome.py'
            test.write_text('import unittest\nclass Outcome(unittest.TestCase):\n    def test_dependency(self):\n        from unstaged_dependency import VALUE\n        self.assertEqual(VALUE, 7)\n')
            helper = root / '.zipper-runner/tests/unstaged_dependency.py'
            helper.write_text('VALUE = 7\n')
            git('add', '.zipper-runner/tests/test_outcome.py')
            result = subprocess.run(['bash', str(ROOT / '.github/hooks/pre-commit')], cwd=root,
                                    env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertIn('ModuleNotFoundError', result.stdout + result.stderr)
            self.assertTrue(helper.exists(), 'unstaged work must be restored after failed validation')
            self.assertFalse((root / '.git/zipper-hooks/pre-commit.pending').exists())

    def test_runner_gates_isolate_git_fixture_environment(self):
        for mode in ('commit', 'push'):
            with self.subTest(mode=mode), self.fixture() as (root, env, git, _, base):
                work = root
                if mode == 'commit':
                    work = root.parent / 'linked'
                    git('worktree', 'add', '-b', 'runner-check', str(work))
                test = work / '.zipper-runner/tests/test_outcome.py'
                test.write_text(
                    'import os, pathlib, subprocess, tempfile, unittest\n'
                    'class Outcome(unittest.TestCase):\n'
                    '    def test_git_fixture(self):\n'
                    '        for name in ("GIT_DIR", "GIT_INDEX_FILE", "GIT_WORK_TREE", "GIT_COMMON_DIR"):\n'
                    '            self.assertNotIn(name, os.environ)\n'
                    '        with tempfile.TemporaryDirectory() as directory:\n'
                    '            subprocess.run(["git", "init", "--bare", directory], check=True, capture_output=True)\n'
                    '            result = subprocess.check_output(["git", "--git-dir", directory, "rev-parse", "--is-bare-repository"], text=True)\n'
                    '            self.assertEqual(result.strip(), "true")\n')
                subprocess.run(['git', 'add', '.zipper-runner'], cwd=work, env=env, check=True)
                expected = git('ls-tree', '-r', '--name-only', 'HEAD').stdout
                if mode == 'commit':
                    hooks = root / '.git/hooks'
                    hook = hooks / 'pre-commit'
                    shutil.copy(ROOT / '.github/hooks/pre-commit', hook)
                    hook.chmod(0o755)
                    git('config', 'core.hooksPath', str(hooks))
                    result = subprocess.run(['git', 'commit', '-m', 'test: linked runner gate'],
                                            cwd=work, env=env, text=True, capture_output=True)
                else:
                    git('commit', '-m', 'test: runner gate')
                    head = git('rev-parse', 'HEAD').stdout.decode().strip()
                    hook_env = {**env, 'GIT_DIR': str(root / '.git'),
                                'GIT_INDEX_FILE': str(root / '.git/index'),
                                'GIT_COMMON_DIR': str(root / '.git'), 'GIT_WORK_TREE': str(root)}
                    result = self.push(root, hook_env, head, base)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(git('config', '--get', 'core.bare').stdout.strip(), b'false')
                actual = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', 'HEAD'],
                                                 cwd=work, env=env)
                self.assertEqual(actual, expected, 'fixture Git must preserve the committed snapshot')

    def test_precommit_product_to_docs_rename_keeps_product_checks(self):
        with self.fixture() as (root, env, git, calls, _):
            (root / 'docs').mkdir()
            git('mv', 'src/Foo.cs', 'docs/Foo.md')
            result = subprocess.run(['bash', str(ROOT / '.github/hooks/pre-commit')], cwd=root,
                                    env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertTrue(any('--filter' not in line for line in calls.read_text().splitlines()))

    def test_prepush_runtime_preferences_and_runner_failure(self):
        for fail in (False, True):
            with self.subTest(fail=fail), self.fixture() as (root, env, git, calls, base):
                (root / '.zipper-runner/AGENT_PREFERENCES.md').write_text('changed')
                if fail:
                    (root / '.zipper-runner/input.txt').write_text('unexpected')
                git('add', '.zipper-runner')
                git('commit', '-m', 'test: runner change')
                head = git('rev-parse', 'HEAD').stdout.decode().strip()
                result = self.push(root, env, head, base)
                self.assertEqual(result.returncode, 1 if fail else 0, result.stdout + result.stderr)
                self.assertIn('Ran 1 test', result.stdout)
                self.assertTrue(all('--filter FullyQualifiedName~DocsDriftTests' in line for line in calls.read_text().splitlines()))

    def test_runner_marker_reused_only_for_complete_runner_scope(self):
        for mixed in (False, True):
            with self.subTest(mixed=mixed), self.fixture() as (root, env, git, calls, base):
                if mixed:
                    (root / 'src/Foo.cs').write_text('class Foo { int Value; }')
                    git('add', 'src/Foo.cs')
                    git('commit', '-m', 'test: product change')
                (root / '.zipper-runner/AGENT_PREFERENCES.md').write_text('changed')
                git('add', '.zipper-runner')
                checked = subprocess.run(['bash', str(ROOT / '.github/hooks/pre-commit')], cwd=root,
                                         env=env, text=True, capture_output=True)
                self.assertEqual(checked.returncode, 0, checked.stdout + checked.stderr)
                git('commit', '-m', 'test: runner change')
                subprocess.run(['bash', str(ROOT / '.github/hooks/post-commit')], cwd=root, env=env, check=True)
                self.assertIn('scope=runner', (root / '.git/zipper-hooks/pre-commit.ok').read_text())
                head = git('rev-parse', 'HEAD').stdout.decode().strip()
                git('branch', 'copy', head)
                before = calls.read_text()
                result = self.push(root, env, head, base, extra=True)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                if mixed:
                    self.assertTrue(any('--filter' not in line for line in calls.read_text().splitlines()))
                else:
                    self.assertEqual(calls.read_text(), before)
                    self.assertIn('skipping duplicate checks', result.stdout)
                    self.assertNotIn('Ran 1 test', result.stdout)

    def test_failed_base_diff_cannot_enable_partial_runner_scope(self):
        with self.fixture() as (root, env, git, calls, base):
            old_tree = git('rev-parse', base + '^{tree}').stdout.decode().strip()
            (root / 'src/Foo.cs').write_text('class Foo { int Value; }')
            git('add', 'src/Foo.cs')
            git('commit', '-m', 'test: product change')
            head = git('rev-parse', 'HEAD').stdout.decode().strip()
            (root / '.zipper-runner/AGENT_PREFERENCES.md').write_text('working change')
            (root / '.git/objects' / old_tree[:2] / old_tree[2:]).unlink()
            result = self.push(root, env, head, base)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertTrue(any('--filter' not in line for line in calls.read_text().splitlines()))

    def test_dev_keeps_rename_sources_and_fails_on_unrelated_base(self):
        with self.fixture() as (root, env, git, _, base):
            git('mv', 'src/Foo.cs', '.zipper-runner/Foo.cs')
            result = subprocess.run(['bash', 'dev.sh', '--dry-run', '--base', base], cwd=root,
                                    env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('Run unit + property tests', result.stdout)
            self.assertIn('Run zipper-runner tests', result.stdout)
            tree = git('rev-parse', 'HEAD^{tree}').stdout.decode().strip()
            unrelated = git('commit-tree', tree, '-m', 'unrelated').stdout.decode().strip()
            result = subprocess.run(['bash', 'dev.sh', '--dry-run', '--base', unrelated], cwd=root,
                                    env=env, text=True, capture_output=True)
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_dev_mixed_runtime_preferences_still_select_runner_tests(self):
        result = subprocess.run(['bash', str(ROOT / 'dev.sh'), '--dry-run', '--files',
                                 '.zipper-runner/AGENT_PREFERENCES.md,.github/workflows/pr.yml'],
                                cwd=ROOT, text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Run zipper-runner tests', result.stdout)
        self.assertIn('Run CI workflow contract tests', result.stdout)


class AgentInstructionsTests(unittest.TestCase):
    def test_entrypoint_stays_small_and_relative_pointers_resolve(self):
        import re
        text = (ROOT / 'AGENTS.md').read_text()
        self.assertLessEqual(len(text.splitlines()), 150)
        for target in re.findall(r'\]\(([^)]+)\)', text):
            if not target.startswith(('http:', 'https:', '#', '/')):
                self.assertTrue((ROOT / target.split('#')[0]).exists(), target)


class RunnerWorkflowGraphTests(unittest.TestCase):
    def test_runner_only_keeps_static_runner_and_helper_jobs_enabled(self):
        import re
        text = (ROOT / '.github/workflows/ci.yml').read_text()
        def job(name):
            match = re.search(r'(?ms)^  ' + re.escape(name) + r':\n.*?(?=^  [a-z0-9-]+:|\Z)', text)
            self.assertIsNotNone(match, name)
            return match.group(0)
        for name in ('lint', 'runner-tests', 'golden-tests'):
            self.assertNotRegex(job(name), r'(?m)^    if:', name)
        for name in ('version-metadata', 'build-and-test'):
            self.assertIn('if: ${{ !inputs.runner_only }}', job(name))
        for name, command in [('lint', 'actionlint-action@'),
                              ('runner-tests', 'python3 -m unittest discover -s .zipper-runner/tests')]:
            steps = re.split(r'(?m)(?=^      - )', job(name))
            selected = [step for step in steps if command in step]
            self.assertEqual(len(selected), 1)
            self.assertNotRegex(selected[0], r'(?m)^(?:      - |        )if:')
        option = re.search(r'(?ms)^      runner_only:\n(.*?)(?=^      [a-z_]+:)', text)
        self.assertIsNotNone(option)
        self.assertIn('type: boolean', option.group(1))
        self.assertIn('default: false', option.group(1))
        pr = (ROOT / '.github/workflows/pr.yml').read_text()
        self.assertIn("runner_only: ${{ needs.docs-only.outputs.only_runner == 'true' }}", pr)
