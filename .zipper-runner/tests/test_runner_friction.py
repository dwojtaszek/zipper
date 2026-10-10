"""Regression tests at the runner CLI, subprocess, and Git boundaries."""
import importlib.util
import contextlib
import io
import os
from pathlib import Path
import selectors
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

BASE = Path(__file__).resolve().parents[1]


class AgentOverrideTests(unittest.TestCase):
    def test_status_explicitAgentBeatsEnvDefaults_withoutChangingPreferences(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shutil.copy(BASE / "runner.py", root / "runner.py")
            (root / ".env").write_text("ACTIVE_AGENT=agy\n")
            (root / "plugins").mkdir()
            for agent in ("agy", "droid"):
                (root / "plugins" / f"{agent}.py").write_text(
                    "def check_installation(): return True\n"
                    "def check_token_health(): return True\n"
                    "def list_models(): return ['default']\n"
                    "def run_mission(*args, **kwargs): raise AssertionError('unexpected mission')\n"
                )
            prefs = "| Agent | Model | Priority |\n| agy | default | 1 |\n| droid | default | 3 |\n"
            (root / "AGENT_PREFERENCES.md").write_text(prefs)
            env = {**os.environ, "RUNNER_BASE": directory, "REPO_PATH": directory,
                   "PLUGINS_DIR": str(root / "plugins"), "ACTIVE_AGENT": "agy",
                   "LOCK_FILE_PATH": str(root / "runner.lock")}
            for arguments, preference, enabled, expected in (
                (["--agent", "droid"], "agy", True, "droid"),
                ([], "droid", True, "droid"),
                (["--agent", "droid"], "agy", False, "agy"),
            ):
                with self.subTest(arguments=arguments, enabled=enabled):
                    current_prefs = prefs if enabled else prefs.replace("| droid | default | 3 |", "| droid | default | 0 |")
                    (root / "AGENT_PREFERENCES.md").write_text(current_prefs)
                    result = subprocess.run(
                        [sys.executable, str(root / "runner.py"), "--status", "--dry-run", *arguments],
                        env={**env, "ACTIVE_AGENT": preference}, capture_output=True, text=True, timeout=5,
                    )
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertIn(f"Selected top candidate: {expected}/default", result.stdout)
                    self.assertEqual((root / "AGENT_PREFERENCES.md").read_text(), current_prefs)
            (root / "AGENT_PREFERENCES.md").write_text(prefs)
            plugin_path = root / "plugins" / "droid.py"
            plugin_path.write_text(plugin_path.read_text().replace(
                "def check_token_health(): return True", "def check_token_health(): return False"))
            result = subprocess.run(
                [sys.executable, str(root / "runner.py"), "--status", "--agent", "droid"],
                env=env, capture_output=True, text=True, timeout=5,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("Selected top candidate: agy/default", result.stdout)
            self.assertIn("using normal fallbacks", result.stdout)


class RunnerCycleTests(unittest.TestCase):
    def test_babysitOnly_resumesWipPr_withoutMergeOrNewIntake(self):
        self._check_wip_resume(exit_code=0)

    def test_babysitOnly_failedWipResume_preservesUncommittedWork(self):
        self._check_wip_resume(exit_code=1)

    def test_babysitOnly_dryRun_doesNotDispatchWipResume(self):
        self._check_wip_resume(exit_code=0, dry_run=True)

    def test_repeatedBabysit_doesNotResumeWipTwice(self):
        self._check_wip_resume(exit_code=1, repeat_scan=True)

    def test_babysitOnly_unpublishedCommit_isNotRemotePrProgress(self):
        self._check_wip_resume(exit_code=0, publish=False)

    def _check_wip_resume(self, exit_code, dry_run=False, repeat_scan=False, publish=True):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            base = root / "runner"
            (base / "plugins").mkdir(parents=True)
            shutil.copy(BASE / "runner.py", base / "runner.py")
            (base / "AGENT_PREFERENCES.md").write_text("| Agent | Model | Priority |\n| droid | default | 1 |\n")
            (base / "plugins" / "droid.py").write_text(
                "from pathlib import Path\nimport subprocess\n"
                "def check_installation(): return True\n"
                "def check_token_health(): return True\n"
                "def list_models(): return ['default']\n"
                "def run_mission(prompt, cwd, **kwargs):\n"
                "    assert kwargs['is_continue'] is True\n"
                "    assert 'Resume work on GitHub issue #42' in prompt\n"
                "    print('WIP_MISSION_CALLED')\n"
                "    Path(cwd, 'progress.txt').write_text('completed work')\n"
                f"    if {exit_code} != 0: return {exit_code}, '', 'interrupted'\n"
                "    subprocess.run(['git','add','progress.txt'],cwd=cwd,check=True)\n"
                "    subprocess.run(['git','commit','-m','fix: finish work'],cwd=cwd,check=True,stdout=subprocess.PIPE)\n"
                f"    if {publish}: subprocess.run(['git','push','origin','HEAD'],cwd=cwd,check=True,capture_output=True)\n"
                "    return 0, 'done', ''\n"
            )
            repo = root / "repo"
            def git(*args, cwd=repo):
                return subprocess.run(["git", "-C", str(cwd), *args], check=True, stdout=subprocess.PIPE, text=True).stdout.strip()
            subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, stdout=subprocess.PIPE)
            (repo / "tests").mkdir()
            (repo / "tests" / "wait-for-reviews.sh").write_text('#!/bin/sh\ntouch "$REVIEW_CALLED"\nprintf "[ OK ] No unresolved review threads\\n"\n')
            git("add", ".")
            git("commit", "-m", "test: base")
            git("clone", "--bare", str(repo), str(root / "origin"))
            git("remote", "add", "origin", str(root / "origin"))
            wt = root / "worktrees" / "issue-42"
            git("worktree", "add", "-b", "fix/ISSUE-42-smooth", str(wt), "main")
            git("commit", "--allow-empty", "-m", "wip: unfinished work", cwd=wt)
            git("push", "--set-upstream", "origin", "HEAD", cwd=wt)
            gh = root / "gh"
            gh.write_text(
                "#!/usr/bin/env python3\nimport json, os, sys, subprocess\nfrom pathlib import Path\n"
                "command=sys.argv[1:3]\n"
                "if command == ['issue','list']:\n"
                "    Path(os.environ['FORBIDDEN_COMMAND']).touch()\n"
                "    raise AssertionError('new intake')\n"
                "elif command == ['issue','view']: print(json.dumps({'title':'Smooth','body':'Finish work','comments':[]}))\n"
                "elif command == ['pr','view']:\n"
                "    fields=sys.argv[sys.argv.index('--json')+1]\n"
                "    if fields == 'commits':\n"
                "        print(subprocess.check_output(['git','--git-dir',os.environ['TEST_ORIGIN'],'log','-1','--format=%s','refs/heads/fix/ISSUE-42-smooth'],text=True).strip())\n"
                "    else:\n"
                "        head = subprocess.check_output(['git','--git-dir',os.environ['TEST_ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'],text=True).strip()\n"
                "        print(json.dumps({'number':99,'state':'OPEN','updatedAt':'2099-01-01T00:00:00Z','statusCheckRollup':[], 'headRefOid': head}))\n"
                "elif command == ['pr','merge']:\n"
                "    Path(os.environ['FORBIDDEN_COMMAND']).touch()\n"
                "    raise AssertionError('merge bypass')\n"
                "elif command == ['api','graphql']: print('0')\n"
                "else: print('[]')\n"
            )
            gh.chmod(0o755)
            mail = root / "msmtp"
            mail.write_text("#!/bin/sh\nexit 0\n")
            mail.chmod(0o755)
            invocation = [sys.executable, str(base / "runner.py")]
            if repeat_scan:
                invocation = [
                    sys.executable, "-c",
                    "import runpy,sys; sys.argv=sys.argv[1:]; "
                    "runner=runpy.run_path(sys.argv[0]); "
                    "runner['babysit_active_worktrees'](); runner['babysit_active_worktrees']()",
                    str(base / "runner.py"),
                ]
            result = subprocess.run(
                [*invocation, "--babysit-only", "--agent", "droid", *(["--dry-run"] if dry_run else [])],
                env={**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"],
                     "RUNNER_BASE": str(base), "REPO_PATH": str(repo),
                     "WORKTREES_BASE": str(root / "worktrees"), "PLUGINS_DIR": str(base / "plugins"),
                     "LOCK_FILE_PATH": str(base / "runner.lock"), "STATE_DIR": str(root / "state"),
                     "TEST_WT": str(wt), "TEST_ORIGIN": str(root / "origin"),
                     "REVIEW_CALLED": str(root / "review-called"),
                     "FORBIDDEN_COMMAND": str(root / "forbidden-command")},
                capture_output=True, text=True, timeout=15,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(result.stdout.count("WIP_MISSION_CALLED"), 0 if dry_run else 1, result.stdout)
            self.assertFalse((root / "review-called").exists(), "Progress snapshots ran the blocking merge review gate")
            self.assertFalse((root / "forbidden-command").exists(), "WIP continuation attempted intake or merge")
            self.assertEqual(git("log", "-1", "--format=%s", cwd=wt), "fix: finish work" if exit_code == 0 and not dry_run else "wip: unfinished work")
            remote_subject = git("--git-dir", str(root / "origin"), "log", "-1", "--format=%s", "refs/heads/fix/ISSUE-42-smooth")
            self.assertEqual(remote_subject, "fix: finish work" if exit_code == 0 and not dry_run and publish else "wip: unfinished work")
            if not publish:
                self.assertIn("no PR progress", result.stdout)
                self.assertNotIn("succeeded (", result.stdout)
            if not dry_run:
                self.assertEqual((wt / "progress.txt").read_text(), "completed work")
            else:
                self.assertFalse((wt / "progress.txt").exists())
            if not repeat_scan:
                self.assertIn("Babysit-only: new issue pickup disabled", result.stdout)

    def test_intake_evaluatesMergeGatesImmediately_withoutDuplicateMission(self):
        for conclusion in ("SUCCESS", "PENDING"):
            with self.subTest(conclusion=conclusion), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                base = root / "runner"
                (base / "plugins").mkdir(parents=True)
                shutil.copy(BASE / "runner.py", base / "runner.py")
                (base / "AGENT_PREFERENCES.md").write_text("| Agent | Model | Priority |\n| droid | default | 1 |\n")
                (base / "plugins" / "droid.py").write_text(
                    "import os, subprocess\nfrom pathlib import Path\n"
                    "def check_installation(): return True\n"
                    "def check_token_health(): return True\n"
                    "def list_models(): return ['default']\n"
                    "def run_mission(prompt, cwd, **kwargs):\n"
                    "    print('MISSION_CALLED')\n"
                    "    subprocess.run(['git', 'commit', '--allow-empty', '-m', 'fix: completed'], cwd=cwd, check=True, stdout=subprocess.PIPE)\n"
                    "    subprocess.run(['git', 'push', 'origin', 'HEAD'], cwd=cwd, check=True, capture_output=True)\n"
                    "    Path(os.environ['OPENED']).touch()\n"
                    "    return 0, 'done', ''\n"
                )
                repo = root / "repo"
                subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, capture_output=True)
                (repo / "tests").mkdir()
                (repo / "tests" / "wait-for-reviews.sh").write_text("#!/bin/sh\nexit 0\n")
                subprocess.run(["git", "-C", str(repo), "add", "."], check=True)
                subprocess.run(["git", "-C", str(repo), "commit", "-m", "test: initial"], check=True, stdout=subprocess.PIPE)
                subprocess.run(["git", "clone", "--bare", str(repo), str(root / "origin")], check=True, capture_output=True)
                subprocess.run(["git", "-C", str(repo), "remote", "add", "origin", str(root / "origin")], check=True)
                gh = root / "gh"
                gh.write_text(
                    "#!/usr/bin/env python3\nimport json, os, sys, subprocess\nfrom pathlib import Path\n"
                    "command = sys.argv[1:3]\n"
                    "if command == ['issue', 'list']:\n"
                    "    print(json.dumps([{'number':42,'title':'Smooth','labels':[{'name':'bug'}], 'author':{'login':'dwojtaszek'}}]))\n"
                    "elif command == ['issue', 'view']: print(json.dumps({'title':'Smooth','body':'Fix friction','comments':[]}))\n"
                    "elif command == ['pr', 'view']:\n"
                    "    if not Path(os.environ['OPENED']).exists(): sys.exit(1)\n"
                    "    fields = sys.argv[sys.argv.index('--json') + 1]\n"
                    "    if fields == 'commits': print('fix: completed')\n"
                    "    else:\n"
                    f"        print(json.dumps({{'number':99,'url':'https://example.test/99','state':'OPEN','updatedAt':'2099-01-01T00:00:00Z', 'statusCheckRollup':[{{'__typename':'CheckRun','name':'ci','status': {'COMPLETED' if conclusion == 'SUCCESS' else 'IN_PROGRESS'!r},'conclusion':{conclusion!r}}}]}}))\n"
                    "elif command == ['pr', 'merge']:\n"
                    "    sha = subprocess.check_output(['git','--git-dir',os.environ['ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'], text=True).strip()\n"
                    "    subprocess.run(['git','--git-dir',os.environ['ORIGIN'],'update-ref','refs/heads/main',sha], check=True)\n"
                    "    Path(os.environ['MERGED']).touch()\n"
                    "else: print('[]')\n"
                )
                gh.chmod(0o755)
                for name, body in (
                    ("msmtp", "exit 0"),
                    ("coderabbit", "echo '{\"type\":\"complete\",\"status\":\"review_completed\",\"findings\":0}'"),
                ):
                    path = root / name
                    path.write_text(f"#!/bin/sh\n{body}\n")
                    path.chmod(0o755)
                result = subprocess.run(
                    [sys.executable, str(base / "runner.py"), "--agent", "droid"],
                    env={**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"],
                         "RUNNER_BASE": str(base), "REPO_PATH": str(repo), "TRUSTED_AUTHORS": "dwojtaszek",
                         "WORKTREES_BASE": str(root / "worktrees"), "PLUGINS_DIR": str(base / "plugins"),
                         "LOCK_FILE_PATH": str(base / "runner.lock"), "OPENED": str(root / "opened"),
                         "MERGED": str(root / "merged"), "ORIGIN": str(root / "origin"),
                         "STATE_DIR": str(root / "state"), "MAX_ACTIVE_WORKTREES": "1"},
                    capture_output=True, text=True, timeout=15,
                )
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(result.stdout.count("MISSION_CALLED"), 1)
                self.assertIn("PR #99 is open. Evaluating checks", result.stdout)
                self.assertEqual((root / "merged").exists(), conclusion == "SUCCESS")
                if conclusion == "SUCCESS":
                    self.assertIn("Reusing successful CodeRabbit review", result.stdout)
                    self.assertIn("Main synchronized after merge", result.stdout)
                    self.assertFalse((root / "worktrees" / "issue-42").exists())
                else:
                    self.assertIn("checks are still pending", result.stdout)

    def test_babysitOnly_idleDoesNotReportCapacity_orProbeAgents(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            base = root / "runner"
            base.mkdir()
            shutil.copy(BASE / "runner.py", base / "runner.py")
            (base / "plugins").mkdir()
            (base / "plugins" / "droid.py").write_text(
                "def check_installation(): raise AssertionError('idle health probe')\n"
                "def check_token_health(): raise AssertionError('idle health probe')\n"
                "def run_mission(*args, **kwargs): raise AssertionError('idle mission')\n"
            )
            repo = root / "repo"
            subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(repo), "commit", "--allow-empty", "-m", "test: initial"], check=True, stdout=subprocess.PIPE)
            subprocess.run(["git", "clone", "--bare", str(repo), str(root / "origin")], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(repo), "remote", "add", "origin", str(root / "origin")], check=True)
            worktrees = root / "worktrees"
            worktrees.mkdir()
            (worktrees / "issue-not-a-directory").touch()
            gh = root / "gh"
            gh.write_text("#!/bin/sh\nprintf '[]\\n'\n")
            gh.chmod(0o755)
            result = subprocess.run(
                [sys.executable, str(base / "runner.py"), "--babysit-only"],
                env={**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"],
                     "RUNNER_BASE": str(base), "REPO_PATH": str(repo),
                     "WORKTREES_BASE": str(worktrees), "PLUGINS_DIR": str(base / "plugins"),
                     "LOCK_FILE_PATH": str(base / "runner.lock")},
                capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("Babysit-only: new issue pickup disabled", result.stdout)
            self.assertNotIn("At capacity", result.stdout)
            self.assertNotIn("Preferences file", result.stdout)


class MainSyncTests(unittest.TestCase):
    def test_babysit_mergedPrFastForwardsMain_withoutDiscardingDirtyFiles(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repo = root / "repo"
            def git(*args, cwd=repo):
                return subprocess.run(["git", "-C", str(cwd), *args], check=True, stdout=subprocess.PIPE, text=True).stdout.strip()
            subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, capture_output=True)
            git("commit", "--allow-empty", "-m", "test: initial")
            git("clone", "--bare", str(repo), str(root / "origin"))
            git("remote", "add", "origin", str(root / "origin"))
            git("clone", str(root / "origin"), str(root / "publisher"))
            git("commit", "--allow-empty", "-m", "test: merged change", cwd=root / "publisher")
            expected = git("rev-parse", "HEAD", cwd=root / "publisher")
            git("push", "origin", "main", cwd=root / "publisher")
            spec = importlib.util.spec_from_file_location("friction_runner", BASE / "runner.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            module.REPO_PATH = str(repo)
            module.WORKTREES_BASE = str(root / "worktrees")
            module.STATE_DIR = str(root / "state")
            module.DRY_RUN = False
            gh = root / "gh"
            gh.write_text(
                "#!/usr/bin/env python3\nimport json, sys\n"
                "print(json.dumps({'number': 1, 'state': 'MERGED'} if sys.argv[1:3] == ['pr', 'view'] else []))\n"
            )
            gh.chmod(0o755)
            mail = root / "msmtp"
            mail.write_text("#!/bin/sh\nexit 0\n")
            mail.chmod(0o755)
            old_path = os.environ["PATH"]
            os.environ["PATH"] = directory + os.pathsep + old_path
            self.addCleanup(os.environ.__setitem__, "PATH", old_path)
            worktree = root / "worktrees" / "issue-1"
            git("worktree", "add", str(worktree), "-b", "fix/issue-1")
            module.babysit_active_worktrees()
            self.assertEqual(git("rev-parse", "HEAD"), expected)
            (repo / "user-notes").write_text("Keep this.")
            git("commit", "--allow-empty", "-m", "test: another merge", cwd=root / "publisher")
            git("push", "origin", "main", cwd=root / "publisher")
            git("worktree", "add", str(worktree), "-b", "fix/issue-1")
            module.babysit_active_worktrees()
            self.assertEqual(git("rev-parse", "HEAD"), expected)
            self.assertEqual((repo / "user-notes").read_text(), "Keep this.")


class ScheduleTests(unittest.TestCase):
    def test_setupCron_migratesOnlyLegacyIntake_andIsIdempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shutil.copy(BASE / "setup-cron.sh", root / "setup-cron.sh")
            for name in ("runner.py", "cron-wrapper.sh", "gh", "opencode"):
                path = root / name
                path.write_text("#!/bin/sh\nexit 0\n")
                path.chmod(0o755)
            fixture = root / "crontab-fixture"
            fixture.write_text(f"0 */6 * * * {root}/cron-wrapper.sh\n1 2 * * * unrelated-job\n")
            fake = root / "crontab"
            fake.write_text(
                "#!/usr/bin/env python3\nimport os, sys\nfrom pathlib import Path\n"
                "p = Path(os.environ['CRON_FIXTURE'])\n"
                "if sys.argv[1:] == ['-l']: sys.stdout.write(p.read_text())\n"
                "else: p.write_text(sys.stdin.read())\n"
            )
            fake.chmod(0o755)
            env = {**os.environ, "HOME": directory, "PATH": directory + os.pathsep + os.environ["PATH"],
                   "CRON_FIXTURE": str(fixture)}
            for _ in range(2):
                result = subprocess.run(["bash", str(root / "setup-cron.sh")], env=env, capture_output=True, text=True, timeout=5)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(fixture.read_text().splitlines(), [
                    f"*/15 * * * * {root}/cron-wrapper.sh", "1 2 * * * unrelated-job",
                    f"*/5 * * * * {root}/cron-wrapper.sh --babysit-only",
                ])
            custom = f"*/20 * * * * {root}/cron-wrapper.sh --agent droid --babysit-only"
            commented = f"# */5 * * * * {root}/cron-wrapper.sh --babysit-only"
            for extra, expected in (
                (custom, custom),
                (commented, f"*/5 * * * * {root}/cron-wrapper.sh --babysit-only"),
            ):
                fixture.write_text(f"*/15 * * * * {root}/cron-wrapper.sh\n{extra}\n")
                result = subprocess.run(["bash", str(root / "setup-cron.sh")], env=env, capture_output=True, text=True, timeout=5)
                self.assertEqual(result.returncode, 0, result.stderr)
                lines = fixture.read_text().splitlines()
                active = [line for line in lines if not line.lstrip().startswith("#")]
                self.assertEqual(active, [f"*/15 * * * * {root}/cron-wrapper.sh", expected])
                self.assertIn(extra, lines)


class DroidProgressTests(unittest.TestCase):
    def test_runMission_returnsFinalText_withoutForwardingToolPayloads(self):
        spec = importlib.util.spec_from_file_location("friction_droid", BASE / "plugins" / "droid.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as directory:
            fake = Path(directory) / "droid"
            fake.write_text(
                "#!/usr/bin/env python3\nimport json\n"
                "print(json.dumps({'type':'tool_result','content':'private tool payload'}))\n"
                "print(json.dumps({'type':'completion','finalText':'done'}), end='')\n"
            )
            fake.chmod(0o755)
            old_path = os.environ["PATH"]
            os.environ["PATH"] = directory + os.pathsep + old_path
            try:
                code, out, err = module.run_mission("test", directory)
            finally:
                os.environ["PATH"] = old_path
            self.assertEqual((code, out, err), (0, "done\n", ""))

    def test_runMission_interruptedStream_returnsPartialText_andSafeHeartbeat(self):
        spec = importlib.util.spec_from_file_location("friction_droid", BASE / "plugins" / "droid.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as directory:
            fake = Path(directory) / "droid"
            fake.write_text(
                "#!/usr/bin/env python3\nimport json, time\n"
                "print(json.dumps({'type':'message','role':'assistant','text':'Working'}), flush=True)\n"
                "print(json.dumps({'type':'tool_result','content':'private payload'}), flush=True)\n"
                "time.sleep(30)\n"
            )
            fake.chmod(0o755)
            log = io.StringIO()
            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"]}), \
                 patch.object(module, "MISSION_TIMEOUT", 1.0), patch.object(module, "HEARTBEAT_SECONDS", 0.03), \
                 contextlib.redirect_stdout(log):
                code, out, err = module.run_mission("test", directory)
            self.assertEqual((code, out), (-1, "Working\n"))
            self.assertIn("timed out", err)
            self.assertIn("Mission active", log.getvalue())
            self.assertNotIn("private payload", log.getvalue() + out)

    def test_runMission_drainsBothPipes_andPreservesUnicodeOutput(self):
        spec = importlib.util.spec_from_file_location("friction_droid", BASE / "plugins" / "droid.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as directory:
            fake = Path(directory) / "droid"
            fake.write_text(
                "#!/usr/bin/env python3\nimport sys\n"
                "sys.stdout.write('é' * 100000)\n"
                "sys.stderr.write('stderr' * 20000)\n"
            )
            fake.chmod(0o755)
            old_path = os.environ["PATH"]
            os.environ["PATH"] = directory + os.pathsep + old_path
            try:
                code, out, err = module.run_mission("test", directory)
            finally:
                os.environ["PATH"] = old_path
            self.assertEqual(code, 0)
            self.assertEqual(out, "é" * 100000)
            self.assertEqual(err, "stderr" * 20000)

    def test_runMission_reportsProgressBeforeChildExit_withoutLoggingContent(self):
        with tempfile.TemporaryDirectory() as directory:
            release = Path(directory) / "release"
            fake = Path(directory) / "droid"
            fake.write_text(
                "#!/usr/bin/env python3\n"
                "import json, time\nfrom pathlib import Path\n"
                "print(json.dumps({'type': 'message', 'role': 'assistant', "
                "'text': 'private mission content'}), flush=True)\n"
                f"while not Path({str(release)!r}).exists(): time.sleep(0.01)\n"
            )
            fake.chmod(0o755)
            script = (
                f"import sys; sys.path.insert(0, {str(BASE / 'plugins')!r}); "
                f"import droid; assert droid.run_mission('test', {directory!r})[0] == 0"
            )
            process = subprocess.Popen(
                [sys.executable, "-u", "-c", script], stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                env={**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"]},
            )
            output = b""
            try:
                with selectors.DefaultSelector() as selector:
                    selector.register(process.stdout, selectors.EVENT_READ)
                    deadline = time.monotonic() + 2
                    while time.monotonic() < deadline and b"assistant progress" not in output:
                        if selector.select(0.1):
                            output += os.read(process.stdout.fileno(), 4096)
                self.assertIn(b"assistant progress", output)
                self.assertIsNone(process.poll())
            finally:
                release.touch()
                remaining, _ = process.communicate(timeout=5)
                output += remaining
            self.assertNotIn(b"private mission content", output)
            self.assertEqual(process.returncode, 0)


if __name__ == "__main__":
    unittest.main()
