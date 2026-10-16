"""Regression tests at the runner CLI, subprocess, and Git boundaries."""
import importlib.util
import contextlib
import io
import json
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

import gitenv  # noqa: F401  # Git identity for fixture repositories

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
    def test_babysitOnly_reviewsLocalWipCompletion_beforeRunnerPublication(self):
        self._check_wip_resume(exit_code=0)

    def test_babysitOnly_failedWipResume_preservesUncommittedWork(self):
        self._check_wip_resume(exit_code=1)

    def test_babysitOnly_dryRun_doesNotDispatchWipResume(self):
        self._check_wip_resume(exit_code=0, dry_run=True)

    def test_repeatedBabysit_doesNotResumeWipTwice(self):
        self._check_wip_resume(exit_code=1, repeat_scan=True)

    def test_babysitOnly_unpublishedCommit_isNotRemotePrProgress(self):
        self._check_wip_resume(exit_code=0, publish=False)

    def test_babysitOnly_rejectedPush_preservesReviewedLocalCompletion(self):
        self._check_wip_resume(exit_code=0, reject_push=True)

    def test_babysitOnly_laterCycleRetriesRetainedReviewedCompletion(self):
        self._check_wip_resume(exit_code=0, reject_push=True, retry_push=True)

    def test_babysitOnly_ordinaryRepairRenewsProvenance_andMergesNextCycle(self):
        self._check_wip_resume(exit_code=0, ordinary_repair=True)

    def test_babysitOnly_reviewedRepairClearsPriorGate_andMergesNextCycle(self):
        self._check_wip_resume(exit_code=0, ordinary_repair=True, prior_gate_failure=True)

    def test_babysitOnly_remoteHeadChangesDuringRepair_doesNotRenewProvenance(self):
        self._check_wip_resume(exit_code=0, ordinary_repair=True, remote_changed=True)

    def test_babysitOnly_remoteHeadChangesDuringReview_atomicLeasePreservesIt(self):
        self._check_wip_resume(exit_code=0, ordinary_repair=True, late_remote_change=True)

    def test_babysitOnly_agentCreatedPrWithFailedReview_remainsRepairable(self):
        self._check_wip_resume(exit_code=0, no_pr=True, publish=False)

    def test_babysitOnly_agentCreatedWipPr_remainsRepairable(self):
        self._check_wip_resume(exit_code=0, no_pr=True, keep_wip=True)

    def test_babysitOnly_initialUnpublishedCompletion_preservesRemoteProvenance(self):
        self._check_wip_resume(exit_code=0, no_pr=True, publish=False, initial_unpublished=True)

    def test_babysitOnly_interruptedCommittedRepair_resumesBeforePublication(self):
        self._check_wip_resume(exit_code=1, committed_failure=True)

    def test_babysitOnly_interruptedPublishedInitialMission_resumesIssueCompletion(self):
        self._check_wip_resume(exit_code=1, no_pr=True, committed_failure=True)

    def test_babysitOnly_interruptedCommittedAndDirtyRepair_preservesAndResumesEdits(self):
        self._check_wip_resume(exit_code=1, committed_failure=True, dirty_failure=True)

    def test_babysitOnly_approvalRevokedDuringReview_doesNotPublishOrRenewProvenance(self):
        self._check_wip_resume(exit_code=0, ordinary_repair=True, revoke_approval=True)

    def test_babysitOnly_laterPageThreadResolution_countsAsProgress(self):
        self._check_wip_resume(exit_code=0, publish=False, thread_progress=True)

    def _check_wip_resume(self, exit_code, dry_run=False, repeat_scan=False, publish=True, thread_progress=False, reject_push=False, retry_push=False, ordinary_repair=False, prior_gate_failure=False, remote_changed=False, no_pr=False, keep_wip=False, late_remote_change=False, initial_unpublished=False, committed_failure=False, dirty_failure=False, revoke_approval=False):
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
                f"    if not {ordinary_repair} and (not {no_pr} or not Path('{root / 'pr-opened'}').exists()): assert 'Resume work on GitHub issue #42' in prompt\n"
                f"    if not {no_pr}: assert 'Do NOT push' in prompt\n"
                "    print('WIP_MISSION_CALLED')\n"
                f"    if {committed_failure} and Path(cwd, 'progress.txt').exists():\n"
                "        assert 'Resume work on GitHub issue #42' in prompt\n"
                f"        if {dirty_failure}:\n"
                "            assert Path(cwd, 'unfinished.txt').read_text() == 'preserved edits'\n"
                "            subprocess.run(['git','add','unfinished.txt'],cwd=cwd,check=True)\n"
                "            subprocess.run(['git','commit','-m','fix: finish work'],cwd=cwd,check=True,stdout=subprocess.PIPE)\n"
                "        return 0, 'repair finished', ''\n"
                f"    if {no_pr} and Path('{root / 'pr-opened'}').exists(): return 0, 'repair', ''\n"
                f"    if {no_pr}: Path('{root / 'pr-opened'}').touch()\n"
                f"    if {keep_wip}: return 0, 'checkpoint PR created', ''\n"
                f"    if {remote_changed}:\n"
                "        subprocess.run(['git','commit','--allow-empty','-m','fix: outside contributor'],cwd=cwd,check=True,stdout=subprocess.PIPE)\n"
                "        subprocess.run(['git','push','origin','HEAD'],cwd=cwd,check=True,stdout=subprocess.PIPE)\n"
                f"    if {late_remote_change}: subprocess.run(['git','commit','--allow-empty','-m','fix: intermediate repair'],cwd=cwd,check=True,stdout=subprocess.PIPE)\n"
                f"    if {thread_progress}: return 0, 'threads resolved', ''\n"
                "    Path(cwd, 'progress.txt').write_text('completed work')\n"
                f"    if {exit_code} != 0 and not {committed_failure}: return {exit_code}, '', 'interrupted'\n"
                "    subprocess.run(['git','add','progress.txt'],cwd=cwd,check=True)\n"
                "    subprocess.run(['git','commit','-m','fix: finish work'],cwd=cwd,check=True,stdout=subprocess.PIPE)\n"
                f"    if {no_pr} and not {initial_unpublished}: subprocess.run(['git','push','origin','HEAD'],cwd=cwd,check=True,stdout=subprocess.PIPE)\n"
                f"    if {dirty_failure}: Path(cwd, 'unfinished.txt').write_text('preserved edits')\n"
                f"    return {exit_code}, 'done', ''\n"
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
            git("commit", "--allow-empty", "-m", "fix: unfinished work" if ordinary_repair else "wip: unfinished work", cwd=wt)
            git("push", "--set-upstream", "origin", "HEAD", cwd=wt)
            review_marker = root / "local-review-passed"
            (root / "origin" / "hooks" / "pre-receive").write_text(
                f"#!/bin/sh\ntouch '{root / 'push-attempted'}'\n"
                f"test -f '{review_marker}' || exit 1\n"
                f"exit {1 if reject_push else 0}\n"
            )
            (root / "origin" / "hooks" / "pre-receive").chmod(0o755)
            reviewer = root / "coderabbit"
            reviewer.write_text(
                "#!/usr/bin/env python3\nimport json, subprocess\nfrom pathlib import Path\n"
                "assert subprocess.check_output(['git','status','--porcelain'],text=True).strip() == ''\n"
                "assert subprocess.check_output(['git','log','-1','--format=%s'],text=True).strip() == 'fix: finish work'\n"
                f"assert subprocess.check_output(['git','--git-dir','{root / 'origin'}','log','-1','--format=%s','refs/heads/fix/ISSUE-42-smooth'],text=True).strip().startswith('fix: finish work' if Path('{root / 'review-called'}').exists() else {'fix: finish work' if no_pr and not initial_unpublished else 'fix: unfinished work' if ordinary_repair else 'wip:'!r})\n"
                f"if {publish}: Path('{review_marker}').touch()\n"
                f"if {late_remote_change}:\n"
                "    parent = subprocess.check_output(['git','rev-parse','HEAD^'],text=True).strip()\n"
                "    subprocess.run(['git','push','origin',parent+':refs/heads/fix/ISSUE-42-smooth'],check=True,stdout=subprocess.PIPE)\n"
                f"if {revoke_approval}: Path('{root / 'approval-revoked'}').touch()\n"
                f"print(json.dumps({{'type':'complete','status':'review_completed','findings':{0 if publish else 1}}}))\n"
            )
            reviewer.chmod(0o755)
            gh = root / "gh"
            gh.write_text(
                "#!/usr/bin/env python3\nimport json, os, sys, subprocess\nfrom pathlib import Path\n"
                "command=sys.argv[1:3]\n"
                "if command == ['issue','list']:\n"
                "    Path(os.environ['FORBIDDEN_COMMAND']).touch()\n"
                "    raise AssertionError('new intake')\n"
                "elif command == ['issue','view']: print(json.dumps({'title':'Smooth','body':'Finish work','comments':[]}))\n"
                "elif command == ['pr','view']:\n"
                f"    if {no_pr} and not Path('{root / 'pr-opened'}').exists(): sys.exit(1)\n"
                "    fields=sys.argv[sys.argv.index('--json')+1]\n"
                "    if fields == 'commits':\n"
                "        print(subprocess.check_output(['git','--git-dir',os.environ['TEST_ORIGIN'],'log','-1','--format=%s','refs/heads/fix/ISSUE-42-smooth'],text=True).strip())\n"
                "    else:\n"
                "        head = subprocess.check_output(['git','--git-dir',os.environ['TEST_ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'],text=True).strip()\n"
                "        subject = subprocess.check_output(['git','--git-dir',os.environ['TEST_ORIGIN'],'log','-1','--format=%s','refs/heads/fix/ISSUE-42-smooth'],text=True).strip()\n"
                f"        checks = [{{'__typename':'CheckRun','name':'ci-gate','status':'COMPLETED','conclusion':'SUCCESS' if subject == 'fix: finish work' else 'FAILURE'}}] if {ordinary_repair or (no_pr and not keep_wip)} else []\n"
                "        print(json.dumps({'number':99,'state':'OPEN','updatedAt':'2099-01-01T00:00:00Z','statusCheckRollup':checks, 'headRefOid': head}))\n"
                "elif command == ['pr','merge']:\n"
                f"    if not {ordinary_repair}:\n"
                "        Path(os.environ['FORBIDDEN_COMMAND']).touch()\n"
                "        raise AssertionError('merge bypass')\n"
                "    assert '--admin' not in sys.argv and '--match-head-commit' in sys.argv\n"
                "    head = subprocess.check_output(['git','--git-dir',os.environ['TEST_ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'],text=True).strip()\n"
                "    assert sys.argv[sys.argv.index('--match-head-commit')+1] == head\n"
                "    subprocess.run(['git','--git-dir',os.environ['TEST_ORIGIN'],'update-ref','refs/heads/main',head],check=True)\n"
                "    Path(os.environ['MERGED_MARKER']).touch()\n"
                "elif command[0] == 'api' and command[1].startswith('repos/'):\n"
                "    head = subprocess.check_output(['git','--git-dir',os.environ['TEST_ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'],text=True).strip()\n"
                "    if command[1].endswith('/reviews'):\n"
                f"        print(json.dumps([[{{'user':{{'login':'dwojtaszek'}},'state':'DISMISSED' if Path('{root / 'approval-revoked'}').exists() else 'APPROVED','commit_id':head}}]]) if {revoke_approval} else '[]')\n"
                "    else:\n"
                f"        if {late_remote_change}: head = os.environ['INITIAL_AUTH_HEAD']\n"
                "        print(json.dumps({'number':99,'state':'open','draft':False,'user':{'login':'dwojtaszek'},'base':{'ref':'main','repo':{'full_name':'dwojtaszek/zipper'}},'head':{'sha':head,'ref':'fix/ISSUE-42-smooth','repo':{'full_name':'dwojtaszek/zipper'}}}))\n"
                "elif command == ['api','graphql']:\n"
                "    if os.environ['THREAD_PROGRESS'] == '1':\n"
                "        assert '--paginate' in sys.argv\n"
                "        marker=Path(os.environ['THREAD_COUNT_MARKER'])\n"
                "        print('0\\n1' if marker.exists() else '0\\n2')\n"
                "        marker.touch()\n"
                "    else: print('0')\n"
                "else: print('[]')\n"
            )
            gh.chmod(0o755)
            mail = root / "msmtp"
            mail.write_text("#!/bin/sh\nexit 0\n")
            mail.chmod(0o755)
            if ordinary_repair or no_pr:
                (root / "origin" / "hooks" / "pre-receive").write_text("#!/bin/sh\nexit 0\n")
            (root / "state").mkdir()
            original_head = git("rev-parse", "HEAD", cwd=wt)
            if not no_pr and not revoke_approval:
                (root / "state/issue-42.json").write_text(json.dumps({
                    "pr_number": "99", "branch": "fix/ISSUE-42-smooth", "authorized_head": original_head,
                }))
            if prior_gate_failure:
                (root / "state/pr-99-coderabbit-gate.json").write_text(json.dumps({
                    "branch": "fix/ISSUE-42-smooth", "local_head": original_head, "error": "previous review failed",
                }))
            invocation = [sys.executable, str(base / "runner.py")]
            if repeat_scan:
                invocation = [
                    sys.executable, "-c",
                    "import runpy,sys; sys.argv=sys.argv[1:]; "
                    "runner=runpy.run_path(sys.argv[0]); "
                    "runner['babysit_active_worktrees'](); runner['babysit_active_worktrees']()",
                    str(base / "runner.py"),
                ]
            env = {**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"],
                     "RUNNER_BASE": str(base), "REPO_PATH": str(repo),
                     "WORKTREES_BASE": str(root / "worktrees"), "PLUGINS_DIR": str(base / "plugins"),
                     "LOCK_FILE_PATH": str(base / "runner.lock"), "STATE_DIR": str(root / "state"),
                     "TEST_WT": str(wt), "TEST_ORIGIN": str(root / "origin"),
                     "REVIEW_CALLED": str(root / "review-called"),
                     "THREAD_PROGRESS": "1" if thread_progress else "0",
                     "THREAD_COUNT_MARKER": str(root / "thread-count-marker"),
                     "FORBIDDEN_COMMAND": str(root / "forbidden-command"),
                     "MERGED_MARKER": str(root / "merged")}
            env["INITIAL_AUTH_HEAD"] = original_head
            arguments = [*invocation, "--babysit-only", "--agent", "droid", *(["--dry-run"] if dry_run else [])]
            result = subprocess.run(
                arguments, env=env,
                capture_output=True, text=True, timeout=15,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(result.stdout.count("WIP_MISSION_CALLED"), 0 if dry_run else 1, result.stdout)
            if no_pr:
                state = json.loads((root / "state/issue-42.json").read_text())
                self.assertEqual(state["authorized_head"], git("--git-dir", str(root / "origin"), "rev-parse", "refs/heads/fix/ISSUE-42-smooth"))
                self.assertTrue((root / "state/pr-99-coderabbit-gate.json").exists())
                if committed_failure:
                    gate = json.loads((root / "state/pr-99-coderabbit-gate.json").read_text())
                    self.assertFalse(gate["completion_ready"])
                    self.assertEqual(gate["local_head"], state["authorized_head"])
                    self.assertEqual(gate["local_head"], git("rev-parse", "HEAD", cwd=wt))
                    self.assertFalse((root / "forbidden-command").exists())
                    self.assertFalse((root / "merged").exists())
                again = subprocess.run(arguments, env=env, capture_output=True, text=True, timeout=15)
                self.assertEqual(again.returncode, 0, again.stdout + again.stderr)
                self.assertIn("WIP_MISSION_CALLED", again.stdout)
                self.assertNotIn("is not authorized", again.stdout)
                self.assertFalse((root / "forbidden-command").exists())
                self.assertFalse((root / "merged").exists())
                if committed_failure:
                    self.assertEqual((wt / "progress.txt").read_text(), "completed work")
                    self.assertEqual(git("rev-parse", "HEAD", cwd=wt), state["authorized_head"])
                    self.assertFalse((root / "state/pr-99-coderabbit-gate.json").exists())
                return
            if committed_failure:
                gate = json.loads((root / "state/pr-99-coderabbit-gate.json").read_text())
                self.assertFalse(gate["completion_ready"])
                self.assertEqual(gate["local_head"], git("rev-parse", "HEAD", cwd=wt))
                self.assertFalse((root / "push-attempted").exists())
                if dirty_failure:
                    self.assertEqual((wt / "unfinished.txt").read_text(), "preserved edits")
                again = subprocess.run(arguments, env=env, capture_output=True, text=True, timeout=15)
                self.assertEqual(again.returncode, 0, again.stdout + again.stderr)
                self.assertIn("WIP_MISSION_CALLED", again.stdout)
                self.assertEqual(git("--git-dir", str(root / "origin"), "rev-parse", "refs/heads/fix/ISSUE-42-smooth"), git("rev-parse", "HEAD", cwd=wt))
                self.assertFalse((root / "state/pr-99-coderabbit-gate.json").exists())
                return
            if revoke_approval:
                self.assertTrue((root / "approval-revoked").exists())
                self.assertFalse((root / "state/issue-42.json").exists())
                self.assertEqual(git("--git-dir", str(root / "origin"), "rev-parse", "refs/heads/fix/ISSUE-42-smooth"), original_head)
                self.assertNotIn("succeeded (", result.stdout)
                return
            if remote_changed or late_remote_change:
                state = json.loads((root / "state/issue-42.json").read_text())
                self.assertEqual(state["authorized_head"], original_head)
                self.assertNotEqual(state["authorized_head"], git("rev-parse", "HEAD", cwd=wt))
                self.assertEqual(git("--git-dir", str(root / "origin"), "log", "-1", "--format=%s", "refs/heads/fix/ISSUE-42-smooth"), "fix: intermediate repair" if late_remote_change else "fix: outside contributor")
                self.assertNotIn("succeeded (", result.stdout)
                self.assertFalse((root / "merged").exists())
                return
            self.assertFalse((root / "review-called").exists(), "Progress snapshots ran the blocking merge review gate")
            self.assertFalse((root / "forbidden-command").exists(), "WIP continuation attempted intake or merge")
            self.assertEqual(git("log", "-1", "--format=%s", cwd=wt), "fix: finish work" if exit_code == 0 and not dry_run and not thread_progress else "wip: unfinished work")
            remote_subject = git("--git-dir", str(root / "origin"), "log", "-1", "--format=%s", "refs/heads/fix/ISSUE-42-smooth")
            self.assertEqual(remote_subject, "fix: finish work" if exit_code == 0 and not dry_run and publish and not thread_progress and not reject_push else "wip: unfinished work")
            gate_file = root / "state" / "pr-99-coderabbit-gate.json"
            if reject_push:
                self.assertTrue((root / "push-attempted").exists())
                self.assertIn("git push failed", gate_file.read_text())
                self.assertNotIn("succeeded (", result.stdout)
            elif not publish and not thread_progress:
                self.assertFalse((root / "push-attempted").exists(), "Failed review attempted publication")
                self.assertIn("1 findings", gate_file.read_text())
            if thread_progress:
                self.assertIn("succeeded (", result.stdout)
            elif not publish:
                self.assertIn("Completion kept local", result.stdout)
                self.assertNotIn("succeeded (", result.stdout)
            if not dry_run and not thread_progress:
                self.assertEqual((wt / "progress.txt").read_text(), "completed work")
            else:
                self.assertFalse((wt / "progress.txt").exists())
            if not repeat_scan:
                self.assertIn("Babysit-only: new issue pickup disabled", result.stdout)
            if ordinary_repair:
                self.assertFalse(gate_file.exists(), "Successful reviewed publication retained a failed gate")
                state = json.loads((root / "state/issue-42.json").read_text())
                self.assertEqual(state["authorized_head"], git("rev-parse", "HEAD", cwd=wt))
                again = subprocess.run(arguments, env=env, capture_output=True, text=True, timeout=15)
                self.assertEqual(again.returncode, 0, again.stdout + again.stderr)
                self.assertTrue((root / "merged").exists(), again.stdout)
                self.assertEqual(git("rev-parse", "main"), state["authorized_head"])
            if retry_push:
                (root / "origin" / "hooks" / "pre-receive").write_text("#!/bin/sh\nexit 0\n")
                again = subprocess.run(arguments, env=env, capture_output=True, text=True, timeout=15)
                self.assertEqual(again.returncode, 0, again.stdout + again.stderr)
                self.assertIn("Retained completion published", again.stdout)
                self.assertNotIn("WIP_MISSION_CALLED", again.stdout)
                self.assertEqual(git("--git-dir", str(root / "origin"), "rev-parse", "refs/heads/fix/ISSUE-42-smooth"), git("rev-parse", "HEAD", cwd=wt))

    def test_intake_evaluatesMergeGatesImmediately_withoutDuplicateMission(self):
        for conclusion in ("SUCCESS", "PENDING", "REVIEW_FAILED"):
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
                    "        head = subprocess.check_output(['git','--git-dir',os.environ['ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'],text=True).strip()\n"
                    f"        print(json.dumps({{'number':99,'url':'https://example.test/99','state':'OPEN','headRefOid':head,'updatedAt':'2099-01-01T00:00:00Z', 'statusCheckRollup':[{{'__typename':'CheckRun','name':'ci','status': {'COMPLETED' if conclusion == 'SUCCESS' else 'IN_PROGRESS'!r},'conclusion':{conclusion!r}}}]}}))\n"
                    "elif command[0] == 'api' and command[1].startswith('repos/'):\n"
                    "    head = subprocess.check_output(['git','--git-dir',os.environ['ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'],text=True).strip()\n"
                    "    print(json.dumps({'number':99,'state':'open','draft':False,'user':{'login':'dwojtaszek'},'base':{'ref':'main','repo':{'full_name':'dwojtaszek/zipper'}},'head':{'sha':head,'ref':'fix/ISSUE-42-smooth','repo':{'full_name':'dwojtaszek/zipper'}}}))\n"
                    "elif command == ['pr', 'merge']:\n"
                    "    assert '--admin' not in sys.argv\n"
                    "    assert '--match-head-commit' in sys.argv\n"
                    "    sha = subprocess.check_output(['git','--git-dir',os.environ['ORIGIN'],'rev-parse','refs/heads/fix/ISSUE-42-smooth'], text=True).strip()\n"
                    "    subprocess.run(['git','--git-dir',os.environ['ORIGIN'],'update-ref','refs/heads/main',sha], check=True)\n"
                    "    Path(os.environ['MERGED']).touch()\n"
                    "else: print('[]')\n"
                )
                gh.chmod(0o755)
                for name, body in (
                    ("msmtp", "exit 0"),
                    ("coderabbit", f"echo '{{\"type\":\"complete\",\"status\":\"review_completed\",\"findings\":{1 if conclusion == 'REVIEW_FAILED' else 0}}}'"),
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
                if conclusion != "REVIEW_FAILED":
                    self.assertIn("PR #99 is open. Evaluating checks", result.stdout)
                self.assertEqual((root / "merged").exists(), conclusion == "SUCCESS")
                if conclusion == "SUCCESS":
                    self.assertIn("Reusing successful CodeRabbit review", result.stdout)
                    self.assertIn("Main synchronized after merge", result.stdout)
                    self.assertFalse((root / "worktrees" / "issue-42").exists())
                elif conclusion == "PENDING":
                    self.assertIn("checks are still pending", result.stdout)
                else:
                    self.assertTrue((root / "state/pr-99-coderabbit-gate.json").exists())
                    self.assertTrue((root / "worktrees/issue-42").exists())

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


class WorktreeResilienceTests(unittest.TestCase):
    @contextlib.contextmanager
    def _runner_env(self, gh_script="#!/bin/sh\nexit 1\n"):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repo = root / "repo"
            subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, capture_output=True)

            def git(*args, cwd=repo):
                return subprocess.run(["git", "-C", str(cwd), *args], check=True, stdout=subprocess.PIPE, text=True).stdout.strip()

            spec = importlib.util.spec_from_file_location("friction_runner", BASE / "runner.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            module.REPO_PATH = str(repo)
            module.WORKTREES_BASE = str(root / "worktrees")
            module.STATE_DIR = str(root / "state")
            module.DRY_RUN = False

            gh = root / "gh"
            gh.write_text(gh_script)
            gh.chmod(0o755)
            emails_file = root / "emails.txt"
            mail = root / "msmtp"
            mail.write_text(f"#!/bin/sh\ncat >> '{emails_file}'\nprintf '\\n---EMAIL-END---\\n' >> '{emails_file}'\nexit 0\n")
            mail.chmod(0o755)
            old_path = os.environ["PATH"]
            os.environ["PATH"] = directory + os.pathsep + old_path
            try:
                yield root, repo, git, module
            finally:
                os.environ["PATH"] = old_path

    def test_babysit_detachedWorktree_alreadyMergedIntoMain_isCleanedUp(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            git("commit", "--allow-empty", "-m", "test: feature commit already in main")
            head_commit = git("rev-parse", "HEAD")

            worktree = root / "worktrees" / "issue-1125"
            git("worktree", "add", "--detach", str(worktree), head_commit)

            module.babysit_active_worktrees()
            self.assertFalse(worktree.exists())

    def test_babysit_detachedWorktree_recoversBranch_ifBranchExists(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            git("checkout", "-b", "fix/ISSUE-1125-assert-quote")
            git("commit", "--allow-empty", "-m", "test: work on issue 1125")
            feature_sha = git("rev-parse", "HEAD")
            git("checkout", "main")

            worktree = root / "worktrees" / "issue-1125"
            git("worktree", "add", "--detach", str(worktree), feature_sha)

            module.babysit_active_worktrees()
            current_branch = git("branch", "--show-current", cwd=worktree)
            self.assertEqual(current_branch, "fix/ISSUE-1125-assert-quote")

    def test_babysit_stateMergedOrClosedPr_removesWorktree(self):
        gh_script = (
            "#!/usr/bin/env python3\nimport json, sys\n"
            "if sys.argv[1:4] == ['pr', 'view', '500']:\n"
            "    print(json.dumps({'number': 500, 'headRefName': 'fix/ISSUE-500-test', 'state': 'MERGED'}))\n"
            "    sys.exit(0)\n"
            "sys.exit(1)\n"
        )
        with self._runner_env(gh_script=gh_script) as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")

            worktree = root / "worktrees" / "issue-500"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-500-test")

            state_dir = root / "state"
            state_dir.mkdir()
            (state_dir / "issue-500.json").write_text(json.dumps({"pr_number": 500, "branch": "fix/ISSUE-500-test"}))
            module.STATE_DIR = str(state_dir)

            module.babysit_active_worktrees()
            self.assertFalse(worktree.exists())
            self.assertFalse((state_dir / "issue-500.json").exists())

    def test_babysit_closedIssueOnGithub_withCleanWorktree_removesWorktree(self):
        gh_script = (
            "#!/usr/bin/env python3\nimport json, sys\n"
            "if sys.argv[1:3] == ['issue', 'view']:\n"
            "    print(json.dumps({'state': 'CLOSED', 'title': 'Closed issue', 'body': 'Done'}))\n"
            "    sys.exit(0)\n"
            "if sys.argv[1:3] == ['pr', 'list']:\n"
            "    print('[]')\n"
            "    sys.exit(0)\n"
            "sys.exit(1)\n"
        )
        with self._runner_env(gh_script=gh_script) as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")

            worktree = root / "worktrees" / "issue-700"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-700-test")

            module.babysit_active_worktrees()
            self.assertFalse(worktree.exists())

    def test_babysit_detachedWorktree_squashedIntoMain_isCleanedUp(self):
        with self._runner_env() as (root, repo, git, module):
            (repo / "file.txt").write_text("initial\n")
            git("add", "file.txt")
            git("commit", "-m", "test: initial")

            git("checkout", "-b", "feat/change")
            (repo / "file.txt").write_text("change\n")
            git("commit", "-am", "test: feature commit")
            feature_sha = git("rev-parse", "HEAD")

            git("checkout", "main")
            (repo / "file.txt").write_text("change\n")
            git("commit", "-am", "test: squash merge of feature")

            worktree = root / "worktrees" / "issue-888"
            git("worktree", "add", "--detach", str(worktree), feature_sha)

            module.babysit_active_worktrees()
            self.assertFalse(worktree.exists())

    def test_babysit_stateMergedPr_dirtyWorktree_preservesWorktree(self):
        gh_script = (
            "#!/usr/bin/env python3\nimport json, sys\n"
            "if sys.argv[1:4] == ['pr', 'view', '500']:\n"
            "    print(json.dumps({'number': 500, 'headRefName': 'fix/ISSUE-500-test', 'state': 'MERGED'}))\n"
            "    sys.exit(0)\n"
            "sys.exit(1)\n"
        )
        with self._runner_env(gh_script=gh_script) as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")

            worktree = root / "worktrees" / "issue-500"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-500-test")
            (worktree / "dirty.txt").write_text("uncommitted work")

            state_dir = root / "state"
            state_dir.mkdir()
            (state_dir / "issue-500.json").write_text(json.dumps({"pr_number": 500, "branch": "fix/ISSUE-500-test"}))
            module.STATE_DIR = str(state_dir)

            module.babysit_active_worktrees()
            self.assertTrue(worktree.exists())
            self.assertTrue((state_dir / "issue-500.json").exists())

    def test_babysit_closedIssueOnGithub_unpushedBranch_retainsBranch(self):
        gh_script = (
            "#!/usr/bin/env python3\nimport json, sys\n"
            "if sys.argv[1:3] == ['issue', 'view']:\n"
            "    print(json.dumps({'state': 'CLOSED', 'title': 'Closed issue', 'body': 'Done'}))\n"
            "    sys.exit(0)\n"
            "if sys.argv[1:3] == ['pr', 'list']:\n"
            "    print('[]')\n"
            "    sys.exit(0)\n"
            "sys.exit(1)\n"
        )
        with self._runner_env(gh_script=gh_script) as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")

            worktree = root / "worktrees" / "issue-700"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-700-test")
            (worktree / "committed.txt").write_text("committed content")
            git("add", "committed.txt", cwd=worktree)
            git("commit", "-m", "test: committed work on branch", cwd=worktree)

            module.babysit_active_worktrees()
            self.assertFalse(worktree.exists())
            branches = git("branch", "--list", "fix/ISSUE-700-test")
            self.assertIn("fix/ISSUE-700-test", branches)

    def test_babysit_detachedWorktree_recoversLowercaseBranch_ifHeadContained(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            git("checkout", "-b", "fix/issue-1125-lowercase")
            git("commit", "--allow-empty", "-m", "test: work on issue 1125")
            feature_sha = git("rev-parse", "HEAD")
            git("checkout", "main")

            worktree = root / "worktrees" / "issue-1125"
            git("worktree", "add", "--detach", str(worktree), feature_sha)

            module.babysit_active_worktrees()
            current_branch = git("branch", "--show-current", cwd=worktree)
            self.assertEqual(current_branch, "fix/issue-1125-lowercase")

    def test_removeWorktree_exhaustedAgent_unpushedAndDirty_survivesCleanupAndEmitsSafetyEmail(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            origin = root / "origin"
            git("clone", "--bare", str(repo), str(origin))
            git("remote", "add", "origin", str(origin))

            worktree = root / "worktrees" / "issue-999"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-999-exhausted")
            (worktree / "committed.txt").write_text("unpushed commit content")
            git("add", "committed.txt", cwd=worktree)
            git("commit", "-m", "test: unpushed agent work", cwd=worktree)
            (worktree / "dirty.txt").write_text("uncommitted dirty work")

            removed = module._remove_worktree(str(worktree), "fix/ISSUE-999-exhausted")
            self.assertFalse(removed)
            self.assertTrue(worktree.exists())
            self.assertTrue((worktree / "dirty.txt").exists())
            self.assertEqual((worktree / "dirty.txt").read_text(), "uncommitted dirty work")
            self.assertIn("fix/ISSUE-999-exhausted", git("branch", "--list", "fix/ISSUE-999-exhausted"))

            emails = (root / "emails.txt").read_text()
            self.assertIn("Safety", emails)
            self.assertIn("fix/ISSUE-999-exhausted", emails)

    def test_removeWorktree_cleanFullyPushed_isRemoved(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            origin = root / "origin"
            git("clone", "--bare", str(repo), str(origin))
            git("remote", "add", "origin", str(origin))

            worktree = root / "worktrees" / "issue-1000"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-1000-clean")
            (worktree / "committed.txt").write_text("clean work")
            git("add", "committed.txt", cwd=worktree)
            git("commit", "-m", "test: clean pushed work", cwd=worktree)
            git("push", "origin", "fix/ISSUE-1000-clean", cwd=worktree)

            removed = module._remove_worktree(str(worktree), "fix/ISSUE-1000-clean")
            self.assertTrue(removed)
            self.assertFalse(worktree.exists())
            self.assertNotIn("fix/ISSUE-1000-clean", git("branch", "--list", "fix/ISSUE-1000-clean"))

    def test_babysit_mainBranchWorktree_dirty_stashesAndPreserves(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            git("checkout", "-b", "temp-branch")

            worktree = root / "worktrees" / "issue-998"
            git("worktree", "add", str(worktree), "main")
            (worktree / "dirty_on_main.txt").write_text("important uncommitted work")

            module.babysit_active_worktrees()
            # Stash should have captured the dirty changes
            stashes = git("stash", "list")
            self.assertIn("runner-safety-main-worktree", stashes)
            self.assertFalse(worktree.exists())
            emails = (root / "emails.txt").read_text()
            self.assertIn("Safety", emails)

    def test_removeWorktree_cleanWorktree_unpushedCommits_survivesCleanup(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            origin = root / "origin"
            git("clone", "--bare", str(repo), str(origin))
            git("remote", "add", "origin", str(origin))

            worktree = root / "worktrees" / "issue-997"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-997-unpushed")
            (worktree / "committed.txt").write_text("committed content not pushed")
            git("add", "committed.txt", cwd=worktree)
            git("commit", "-m", "test: committed work", cwd=worktree)

            removed = module._remove_worktree(str(worktree), "fix/ISSUE-997-unpushed")
            self.assertFalse(removed)
            self.assertTrue(worktree.exists())
            self.assertIn("fix/ISSUE-997-unpushed", git("branch", "--list", "fix/ISSUE-997-unpushed"))
            emails = (root / "emails.txt").read_text()
            self.assertIn("Safety", emails)
            self.assertIn("fix/ISSUE-997-unpushed", emails)

    def test_removeWorktree_squashMergedPr_isMergedTrue_removesWorktreeAndBranch(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")

            worktree = root / "worktrees" / "issue-995"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-995-squashed")
            (worktree / "committed.txt").write_text("squashed content")
            git("add", "committed.txt", cwd=worktree)
            git("commit", "-m", "test: squashed work", cwd=worktree)

            # Even though branch is not merged into main and not pushed to origin,
            # is_merged=True explicitly signals that PR was merged (e.g. squash-merged).
            removed = module._remove_worktree(str(worktree), "fix/ISSUE-995-squashed", is_merged=True)
            self.assertTrue(removed)
            self.assertFalse(worktree.exists())
            self.assertNotIn("fix/ISSUE-995-squashed", git("branch", "--list", "fix/ISSUE-995-squashed"))

    def test_removeWorktree_gitWorktreeRemoveFails_neverRmtreesDirectory(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")

            worktree = root / "worktrees" / "issue-996"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-996-fail")

            # Mock run_cmd to simulate git worktree remove failing
            orig_run_cmd = module.run_cmd
            def mock_run_cmd(cmd, cwd=None, **kwargs):
                if len(cmd) >= 3 and cmd[0] == "git" and cmd[1] == "worktree" and cmd[2] == "remove":
                    return 1, "", "fatal: failed to remove worktree"
                return orig_run_cmd(cmd, cwd=cwd, **kwargs)

            module.run_cmd = mock_run_cmd
            try:
                removed = module._remove_worktree(str(worktree), "fix/ISSUE-996-fail")
                self.assertFalse(removed)
                self.assertTrue(worktree.exists())
            finally:
                module.run_cmd = orig_run_cmd

    def test_removeWorktree_staleRemoteTrackingRef_deletedOnOrigin_preservesWorktree(self):
        with self._runner_env() as (root, repo, git, module):
            git("commit", "--allow-empty", "-m", "test: initial")
            origin = root / "origin"
            git("clone", "--bare", str(repo), str(origin))
            git("remote", "add", "origin", str(origin))

            worktree = root / "worktrees" / "issue-994"
            git("worktree", "add", str(worktree), "-b", "fix/ISSUE-994-stale")
            (worktree / "committed.txt").write_text("branch work")
            git("add", "committed.txt", cwd=worktree)
            git("commit", "-m", "test: branch work", cwd=worktree)
            git("push", "origin", "fix/ISSUE-994-stale", cwd=worktree)

            # Delete branch from remote origin without pruning local tracking refs,
            # simulating stale origin/fix/ISSUE-994-stale ref locally.
            git("push", "origin", "--delete", "fix/ISSUE-994-stale")

            # Live origin no longer has it. Cleanup must preserve the branch!
            removed = module._remove_worktree(str(worktree), "fix/ISSUE-994-stale")
            self.assertFalse(removed)
            self.assertTrue(worktree.exists())
            self.assertIn("fix/ISSUE-994-stale", git("branch", "--list", "fix/ISSUE-994-stale"))




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


class MainRepoRecoveryTests(unittest.TestCase):
    @contextlib.contextmanager
    def _recovery_env(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            remote = root / "remote.git"
            subprocess.run(["git", "init", "--bare", "-b", "main", str(remote)], check=True, capture_output=True)

            repo = root / "repo"
            subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, capture_output=True)

            def git(*args, cwd=repo):
                return subprocess.run(["git", "-C", str(cwd), *args], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True).stdout.strip()

            git("config", "user.name", "test")
            git("config", "user.email", "test@test.com")
            git("remote", "add", "origin", str(remote))
            (repo / "tracked.txt").write_text("initial content\n")
            git("add", "tracked.txt")
            git("commit", "-m", "initial commit")
            git("push", "-u", "origin", "main")

            publisher = root / "publisher"
            subprocess.run(["git", "clone", str(remote), str(publisher)], check=True, capture_output=True)
            git("config", "user.name", "test", cwd=publisher)
            git("config", "user.email", "test@test.com", cwd=publisher)

            spec = importlib.util.spec_from_file_location("friction_runner", BASE / "runner.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            module.REPO_PATH = str(repo)
            module.DRY_RUN = False
            module.MAIN_REMOTE_REF = "origin/main"
            state_dir = root / "state"
            state_dir.mkdir()
            module.STATE_DIR = str(state_dir)

            emails_file = root / "emails.txt"
            mail = root / "msmtp"
            mail.write_text(f"#!/bin/sh\ncat >> '{emails_file}'\nprintf '\\n---EMAIL-END---\\n' >> '{emails_file}'\nexit 0\n")
            mail.chmod(0o755)

            old_path = os.environ["PATH"]
            os.environ["PATH"] = directory + os.pathsep + old_path
            try:
                yield root, repo, publisher, git, module, emails_file
            finally:
                os.environ["PATH"] = old_path

    @staticmethod
    def _setup_conflicting_commit(repo, publisher, git):
        (repo / "local.txt").write_text("local commit\n")
        git("add", "local.txt")
        git("commit", "-m", "local commit")

        (publisher / "local.txt").write_text("remote conflicting commit\n")
        git("add", "local.txt", cwd=publisher)
        git("commit", "-m", "remote conflicting commit", cwd=publisher)
        git("push", "origin", "main", cwd=publisher)

    @staticmethod
    def _setup_dirty_tree(repo, publisher, git, tracked_text="local uncommitted modification\n", untracked_text="untracked work\n"):
        (publisher / "tracked.txt").write_text("remote content\n")
        git("commit", "-am", "remote update", cwd=publisher)
        git("push", "origin", "main", cwd=publisher)

        (repo / "tracked.txt").write_text(tracked_text)
        if untracked_text is not None:
            (repo / "untracked.txt").write_text(untracked_text)

    @staticmethod
    def _setup_ignored_collision(repo, publisher, git):
        (publisher / "ignored_file.txt").write_text("remote tracked content\n")
        git("add", "ignored_file.txt", cwd=publisher)
        git("commit", "-m", "remote adds ignored_file.txt", cwd=publisher)
        git("push", "origin", "main", cwd=publisher)

        (repo / ".gitignore").write_text("ignored_file.txt\n")
        git("add", ".gitignore")
        git("commit", "-m", "ignore ignored_file.txt")
        (repo / "ignored_file.txt").write_text("local user ignored content\n")

    def _assert_sync_aborts(self, module, mock_run_cmd=None):
        if mock_run_cmd is not None:
            with patch.object(module, "run_cmd", side_effect=mock_run_cmd):
                with self.assertRaises(SystemExit) as cm:
                    module.sync_main_repo()
                self.assertEqual(cm.exception.code, 1)
        else:
            with self.assertRaises(SystemExit) as cm:
                module.sync_main_repo()
            self.assertEqual(cm.exception.code, 1)

    def test_syncMainRepo_stashFails_abortsRecoveryPreservingWorkAndSendsEmail(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_dirty_tree(repo, publisher, git)

            original_run_cmd = module.run_cmd
            def mock_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "stash"] and "--include-untracked" in args:
                    return 1, "", "Cannot save the current status"
                return original_run_cmd(args, cwd=cwd, timeout=timeout)

            self._assert_sync_aborts(module, mock_run_cmd)

            self.assertEqual((repo / "tracked.txt").read_text(), "local uncommitted modification\n")
            self.assertTrue((repo / "untracked.txt").exists())
            self.assertEqual((repo / "untracked.txt").read_text(), "untracked work\n")

            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Failed to stash dirty files", emails)

    def test_syncMainRepo_stashVerificationFails_abortsRecoveryPreservingWorkAndSendsEmail(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_dirty_tree(repo, publisher, git)

            original_run_cmd = module.run_cmd
            def mock_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "stash"] and "--include-untracked" in args:
                    return 0, "Saved working directory", ""
                if args[:3] == ["git", "stash", "list"]:
                    return 0, "", ""
                return original_run_cmd(args, cwd=cwd, timeout=timeout)

            self._assert_sync_aborts(module, mock_run_cmd)

            self.assertEqual((repo / "tracked.txt").read_text(), "local uncommitted modification\n")
            self.assertTrue((repo / "untracked.txt").exists())

            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)

    def test_syncMainRepo_stashSucceedsAndVerifies_resetsAndCleansTree(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_dirty_tree(repo, publisher, git)

            module.sync_main_repo()

            self.assertEqual((repo / "tracked.txt").read_text(), "remote content\n")
            self.assertFalse((repo / "untracked.txt").exists())

            stash_list = git("stash", "list")
            self.assertIn("runner-auto-recovery-", stash_list)

            emails = emails_file.read_text()
            self.assertIn("Auto-Recovery: Stashed dirty files and reset main", emails)

    def test_syncMainRepo_cleanRepoRebaseFails_resetsToOriginMain(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_conflicting_commit(repo, publisher, git)

            module.sync_main_repo()

            self.assertEqual((repo / "local.txt").read_text(), "remote conflicting commit\n")
            branches = git("branch", "--list", "runner-backup-*")
            self.assertTrue(bool(branches.strip()))

    def test_syncMainRepo_fetchFails_abortsStartup(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            original_run_cmd = module.run_cmd
            def mock_run_cmd(args, cwd=None, timeout=None):
                if args[:3] == ["git", "fetch", "origin"]:
                    return 1, "", "fatal: could not read from remote"
                return original_run_cmd(args, cwd=cwd, timeout=timeout)

            self._assert_sync_aborts(module, mock_run_cmd)

            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Failed to fetch origin", emails)

    def test_syncMainRepo_cleanIgnoredFileCollision_abortsRecovery(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_ignored_collision(repo, publisher, git)

            self._assert_sync_aborts(module)

            self.assertEqual((repo / "ignored_file.txt").read_text(), "local user ignored content\n")
            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Ignored files collide", emails)

    def test_syncMainRepo_dirtyIgnoredFileCollision_abortsRecovery(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_ignored_collision(repo, publisher, git)
            (repo / "tracked.txt").write_text("local uncommitted\n")

            self._assert_sync_aborts(module)

            self.assertEqual((repo / "ignored_file.txt").read_text(), "local user ignored content\n")
            self.assertEqual((repo / "tracked.txt").read_text(), "local uncommitted\n")
            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Ignored files collide", emails)

    def test_syncMainRepo_localCommitDoesNotTriggerFalseCollision(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            (repo / ".gitignore").write_text("scratch.log\n")
            git("add", ".gitignore")
            git("commit", "-m", "ignore scratch.log")
            git("push", "origin", "main")

            (repo / "scratch.log").write_text("local untracked ignored content\n")
            (repo / "my_feature.txt").write_text("local feature\n")
            git("add", "my_feature.txt")
            git("commit", "-m", "local feature commit")

            # Remote added unrelated file
            git("pull", "origin", "main", cwd=publisher)
            (publisher / "unrelated.txt").write_text("unrelated\n")
            git("add", "unrelated.txt", cwd=publisher)
            git("commit", "-m", "remote adds unrelated.txt", cwd=publisher)
            git("push", "origin", "main", cwd=publisher)

            # Rebase should succeed without collision abort and preserve ignored file
            module.sync_main_repo()
            self.assertEqual((repo / "scratch.log").read_text(), "local untracked ignored content\n")
            self.assertEqual((repo / "my_feature.txt").read_text(), "local feature\n")
            self.assertEqual((repo / "unrelated.txt").read_text(), "unrelated\n")
            if emails_file.exists():
                self.assertNotIn("Ignored files collide", emails_file.read_text())

    def test_syncMainRepo_backupBranchFails_aborts(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_conflicting_commit(repo, publisher, git)

            original_run_cmd = module.run_cmd
            def mock_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "branch"] and len(args) == 4:
                    return 1, "", "fatal: cannot create branch"
                return original_run_cmd(args, cwd=cwd, timeout=timeout)

            self._assert_sync_aborts(module, mock_run_cmd)

            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Failed to back up local commits before reset", emails)

    def test_syncMainRepo_dirtyRepoWithLocalCommits_backsUpCommitsAndStashesChanges(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            (repo / "local.txt").write_text("local commit\n")
            git("add", "local.txt")
            git("commit", "-m", "local commit")

            (publisher / "tracked.txt").write_text("remote commit\n")
            git("commit", "-am", "remote commit", cwd=publisher)
            git("push", "origin", "main", cwd=publisher)

            (repo / "tracked.txt").write_text("dirty uncommitted\n")
            (repo / "dirty_untracked.txt").write_text("dirty untracked\n")

            module.sync_main_repo()

            self.assertEqual((repo / "tracked.txt").read_text(), "remote commit\n")
            self.assertFalse((repo / "dirty_untracked.txt").exists())
            branches = git("branch", "--list", "runner-backup-*")
            self.assertTrue(bool(branches.strip()))
            stash_list = git("stash", "list")
            self.assertIn("runner-auto-recovery-", stash_list)

    def test_syncMainRepo_revListFails_aborts(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            self._setup_conflicting_commit(repo, publisher, git)

            original_run_cmd = module.run_cmd
            def mock_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "rev-list"]:
                    return 1, "", "fatal: rev-list failed"
                return original_run_cmd(args, cwd=cwd, timeout=timeout)

            self._assert_sync_aborts(module, mock_run_cmd)

            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Failed to check local commits before reset", emails)

    def test_syncMainRepo_resetFails_aborts(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            (publisher / "tracked.txt").write_text("remote content\n")
            git("commit", "-am", "remote update", cwd=publisher)
            git("push", "origin", "main", cwd=publisher)

            (repo / "tracked.txt").write_text("local uncommitted modification\n")

            original_run_cmd = module.run_cmd
            def mock_run_cmd(args, cwd=None, timeout=None):
                if args[:3] == ["git", "reset", "--hard"]:
                    return 1, "", "fatal: could not reset"
                return original_run_cmd(args, cwd=cwd, timeout=timeout)

            self._assert_sync_aborts(module, mock_run_cmd)

            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Failed to reset main branch", emails)

    def test_syncMainRepo_ignoredCollisionCheckFails_abortsRecovery(self):
        with self._recovery_env() as (root, repo, publisher, git, module, emails_file):
            (publisher / "tracked.txt").write_text("remote content\n")
            git("commit", "-am", "remote update", cwd=publisher)
            git("push", "origin", "main", cwd=publisher)

            (repo / "tracked.txt").write_text("local uncommitted modification\n")

            original_run_cmd = module.run_cmd
            def mock_run_cmd(args, cwd=None, timeout=None):
                if args[:2] == ["git", "ls-files"]:
                    return 1, "", "fatal: ls-files error"
                return original_run_cmd(args, cwd=cwd, timeout=timeout)

            self._assert_sync_aborts(module, mock_run_cmd)

            emails = emails_file.read_text()
            self.assertIn("Safety Abort", emails)
            self.assertIn("Failed to check ignored files during recovery", emails)


if __name__ == "__main__":
    unittest.main()

