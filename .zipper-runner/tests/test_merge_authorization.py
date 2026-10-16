"""Exercise authorization with real Git refs and subprocess CLI boundaries."""
from contextlib import contextmanager
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import MagicMock, patch

import gitenv  # noqa: F401

BASE = Path(__file__).resolve().parents[1]


class MergeAuthorizationTests(unittest.TestCase):
    def test_merge_untrustedForkWrongBaseAndSpoofedBot_doNotMutateRefs(self):
        for mutation in ("untrusted", "fork", "base", "spoof", "spoof-id", "spoof-type", "missing", "no-state", "legacy",
                         "bot-mixed", "bot-unsigned", "bot-drift", "stale-approval", "revoked-approval", "dismissed-approval"):
            with self.subTest(mutation=mutation):
                self._exercise(mutation)

    def test_merge_runnerProvenance_mergesExactReviewedHead(self):
        self._exercise("runner", expected=True)

    def test_merge_authenticatedDependabot_mergesExactReviewedHead(self):
        self._exercise("bot", expected=True)

    def test_merge_explicitMaintainerApproval_authorizesUnknownAuthor(self):
        self._exercise("approval", expected=True)

    def test_merge_changedHeadAfterReview_preservesRefs(self):
        self._exercise("changed")

    def test_recovery_untrustedPr_doesNotCreateWorktree(self):
        self._exercise("recovery")

    def test_scanners_rejectUnauthorizedOrigins_beforeReviewMergeOrRecovery(self):
        for scanner in ("active", "orphan", "dependabot"):
            for scenario in ("untrusted", "fork", "base", "no-state", "legacy", "spoof-id", "spoof-type",
                             "bot-mixed", "bot-unsigned", "bot-drift", "stale-approval", "revoked-approval", "dismissed-approval"):
                with self.subTest(scanner=scanner, scenario=scenario):
                    self._exercise(scenario, scanner=scanner)

    def test_scanners_authorizedRunnerAndGenuineBot_mergeNormally(self):
        for scanner, scenario in (("active", "runner"), ("orphan", "runner"), ("dependabot", "bot")):
            with self.subTest(scanner=scanner):
                self._exercise(scenario, expected=True, scanner=scanner)

    def test_recovery_trustedProvenance_createsExactHeadWorktree(self):
        self._exercise("trusted-recovery")

    def test_merge_localReviewFailure_isStillAMergeBlocker(self):
        self._exercise("failed-review")

    def _exercise(self, scenario, expected=False, scanner=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repo = root / "repo"
            def git(*args):
                return subprocess.check_output(["git", "-C", str(repo), *args], text=True).strip()
            subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, capture_output=True)
            (repo / "tests").mkdir()
            (repo / "tests/wait-for-reviews.sh").write_text(
                '#!/bin/sh\ntouch "$FIXTURE/review-called"\nexit "${REVIEW_EXIT:-0}"\n')
            git("add", ".")
            git("commit", "--allow-empty", "-m", "test: initial")
            initial = git("rev-parse", "HEAD")
            git("branch", "fix/ISSUE-42-proof")
            git("checkout", "fix/ISSUE-42-proof")
            git("commit", "--allow-empty", "-m", "fix: proof")
            head = git("rev-parse", "HEAD")
            git("checkout", "main")
            git("clone", "--bare", str(repo), str(root / "origin"))
            git("remote", "add", "origin", str(root / "origin"))
            pr = {
                "number": 99, "state": "open", "draft": False,
                "user": {"login": "dwojtaszek", "type": "User", "id": 1},
                "base": {"ref": "main", "repo": {"full_name": "dwojtaszek/zipper"}},
                "head": {"ref": "fix/ISSUE-42-proof", "sha": head,
                         "repo": {"full_name": "dwojtaszek/zipper"}},
            }
            if scenario in ("untrusted", "approval", "recovery", "spoof", "stale-approval", "revoked-approval", "dismissed-approval"):
                pr["user"]["login"] = "outsider"
            if scenario == "fork":
                pr["head"]["repo"]["full_name"] = "outsider/zipper"
            if scenario == "base":
                pr["base"]["ref"] = "other"
            if scenario == "spoof":
                pr["head"]["ref"] = "dependabot/spoof"
            if scenario.startswith("bot"):
                pr["user"] = {"login": "dependabot[bot]", "type": "Bot", "id": 49699333}
            if scenario in ("spoof-id", "spoof-type"):
                pr["user"] = {"login": "dependabot[bot]", "type": "Bot", "id": 49699333}
                pr["user"]["id" if scenario == "spoof-id" else "type"] = 1 if scenario == "spoof-id" else "User"
            if scenario == "missing":
                pr.pop("head")
            (root / "pr.json").write_text(json.dumps(pr))
            commits = [{"sha": head, "author": {"login": "dependabot[bot]", "type": "Bot", "id": 49699333},
                        "committer": {"login": "web-flow", "type": "User", "id": 19864447},
                        "commit": {"verification": {"verified": True, "reason": "valid"}}}]
            if scenario == "bot-mixed":
                commits.insert(0, {**commits[0], "sha": initial, "author": {"login": "outsider", "type": "User", "id": 1}})
            if scenario == "bot-unsigned":
                commits[0]["commit"]["verification"]["verified"] = False
            if scenario == "bot-drift":
                commits[0]["sha"] = initial
            (root / "commits.json").write_text(json.dumps(commits))
            approval = {"user": {"login": "dwojtaszek"}, "state": "APPROVED", "commit_id": head}
            reviews = [approval] if scenario.endswith("approval") or scenario == "approval" else []
            if scenario == "stale-approval":
                reviews[0]["commit_id"] = initial
            if scenario in ("revoked-approval", "dismissed-approval"):
                reviews.append({**approval, "state": "CHANGES_REQUESTED" if scenario == "revoked-approval" else "DISMISSED"})
            (root / "reviews.json").write_text(json.dumps(reviews))
            gh = root / "gh"
            gh.write_text(
                "#!/usr/bin/env python3\nimport json, os, sys, subprocess\nfrom pathlib import Path\n"
                "root=Path(os.environ['FIXTURE'])\n"
                "args=sys.argv[1:]\n"
                "if args[:2] == ['pr','merge']:\n"
                "    assert '--admin' not in args\n"
                "    sha=args[args.index('--match-head-commit')+1]\n"
                "    assert sha == json.loads((root/'pr.json').read_text())['head']['sha']\n"
                "    subprocess.run(['git','update-ref','refs/heads/main',sha],check=True)\n"
                "    subprocess.run(['git','--git-dir',str(root/'origin'),'update-ref','refs/heads/main',sha],check=True)\n"
                "    (root/'merged').touch()\n"
                "elif args[0] == 'api':\n"
                "    if args[1].endswith('/reviews'):\n"
                "        print(json.dumps([json.loads((root/'reviews.json').read_text())]))\n"
                "    elif args[1].endswith('/commits'): print(json.dumps([json.loads((root/'commits.json').read_text())]))\n"
                "    else: print((root/'pr.json').read_text())\n"
                "elif args[:2] == ['pr','view']:\n"
                "    pr=json.loads((root/'pr.json').read_text())\n"
                "    if args[args.index('--json')+1] == 'commits': print('fix: proof')\n"
                "    else: print(json.dumps({'number':99,'state':'OPEN','headRefOid':pr.get('head',{}).get('sha'), 'statusCheckRollup':[{'__typename':'CheckRun','name':'ci-gate','status':'COMPLETED','conclusion':'SUCCESS'}]}))\n"
                "elif args[:2] == ['pr','list']:\n"
                "    pr=json.loads((root/'pr.json').read_text())\n"
                "    print(json.dumps([{'number':99,'title':'Fix #42','body':'Closes #42','author':{'login':pr['user']['login']},'headRefName':pr.get('head',{}).get('ref','fix/ISSUE-42-proof'),'statusCheckRollup':[{'__typename':'CheckRun','name':'ci-gate','status':'COMPLETED','conclusion':'SUCCESS'}]}]))\n"
                "else: raise AssertionError(args)\n"
            )
            gh.chmod(0o755)
            spec = importlib.util.spec_from_file_location("authorization_runner", BASE / "runner.py")
            runner = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(runner)
            runner.REPO_PATH = str(repo)
            runner.STATE_DIR = str(root / "state")
            runner.WORKTREES_BASE = str(root / "worktrees")
            runner.TRUSTED_AUTHORS = {"dwojtaszek"}
            runner.DRY_RUN = False
            (root / "state").mkdir()
            state = {
                "pr_number": "99", "branch": "fix/ISSUE-42-proof", "authorized_head": head,
            }
            if scenario == "legacy":
                state.pop("authorized_head")
            if scenario != "no-state":
                (root / "state" / "issue-42.json").write_text(json.dumps(state))
            if scenario == "failed-review":
                (root / "state" / "pr-99-coderabbit-gate.json").write_text('{"error":"review failed"}')
            for name in ("coderabbit", "msmtp"):
                executable = root / name
                executable.write_text("#!/bin/sh\necho '{\"type\":\"complete\",\"status\":\"review_completed\",\"findings\":0}'\n")
                executable.chmod(0o755)
            if scanner == "active":
                git("worktree", "add", str(root / "worktrees/issue-42"), "fix/ISSUE-42-proof")
            runner.REVIEW_GATE_SCRIPT = "tests/wait-for-reviews.sh"
            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"],
                                         "FIXTURE": directory, "SCENARIO": scenario,
                                         "REVIEW_EXIT": "1" if scenario == "trusted-recovery" else "0"}):
                if scanner:
                    {"active": runner.babysit_active_worktrees, "orphan": runner.babysit_orphaned_issue_prs,
                     "dependabot": runner.babysit_dependabot_prs}[scanner]()
                    if not expected:
                        self.assertFalse((root / "review-called").exists())
                elif scenario in ("recovery", "trusted-recovery"):
                    runner._recreate_worktree_for_pr("42", "fix/ISSUE-42-proof", 99, head)
                    worktree = root / "worktrees/issue-42"
                    self.assertEqual(worktree.exists(), scenario == "trusted-recovery")
                    if worktree.exists():
                        self.assertEqual(subprocess.check_output(["git", "-C", str(worktree), "rev-parse", "HEAD"], text=True).strip(), head)
                else:
                    reviewed = initial if scenario == "changed" else head
                    code, _, _ = runner._merge_authorized_pr(99, "42", reviewed)
                    self.assertEqual(code == 0, expected)
            self.assertEqual(git("rev-parse", "main"), head if expected else initial)
            self.assertEqual((root / "merged").exists(), expected)



class WorktreeReconciliationTests(unittest.TestCase):
    @contextmanager
    def _setup_fixture(self, coderabbit_findings=0):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repo = root / "repo"
            subprocess.run(["git", "init", "-b", "main", str(repo)], check=True, capture_output=True)
            (repo / "tests").mkdir()
            (repo / "tests/wait-for-reviews.sh").write_text('#!/bin/sh\nexit 0\n')
            (repo / "initial.txt").write_text("initial")
            subprocess.run(["git", "-C", str(repo), "add", "."], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(repo), "commit", "-m", "test: initial"], check=True, capture_output=True)

            gh = root / "gh"
            gh.write_text(
                "#!/usr/bin/env python3\nimport json, os, subprocess, sys\nfrom pathlib import Path\nroot = Path(__file__).resolve().parent\n"
                "args = sys.argv[1:]\n"
                "if args[:2] == ['api', 'graphql']:\n"
                "    print(json.dumps([0]))\n"
                "elif args[:2] == ['pr', 'merge']:\n"
                "    sha = args[args.index('--match-head-commit')+1]\n"
                "    subprocess.run(['git', '--git-dir', str(root/'origin'), 'update-ref', 'refs/heads/main', sha], check=True)\n"
                "    (root / 'merged').write_text(args[2])\n"
                "    print('merged')\n"
                "elif args[0] == 'api':\n"
                "    if args[1].endswith('/reviews'):\n"
                "        print(json.dumps([]))\n"
                "    elif args[1].endswith('/commits'): print(json.dumps([]))\n"
                "    else: print((root/'pr.json').read_text())\n"
                "elif args[:2] == ['pr','view']:\n"
                "    pr=json.loads((root/'pr.json').read_text())\n"
                "    if args[args.index('--json')+1] == 'commits': print('fix: proof')\n"
                "    else: print(json.dumps({'number':99,'state':'OPEN','headRefOid':pr.get('head',{}).get('sha'), 'statusCheckRollup':[{'__typename':'CheckRun','name':'ci-gate','status':'COMPLETED','conclusion':'SUCCESS'}]}))\n"
                "elif args[:2] == ['pr','list']:\n"
                "    pr=json.loads((root/'pr.json').read_text())\n"
                "    print(json.dumps([{'number':99,'title':'Fix #42','body':'Closes #42','author':{'login':pr['user']['login']},'headRefName':pr.get('head',{}).get('ref','fix/ISSUE-42-proof'),'statusCheckRollup':[{'__typename':'CheckRun','name':'ci-gate','status':'COMPLETED','conclusion':'SUCCESS'}]}]))\n"
                "else: raise AssertionError(args)\n"
            )
            gh.chmod(0o755)

            cr = root / "coderabbit"
            cr.write_text(
                f"#!/bin/sh\necho '{{\"type\":\"complete\",\"status\":\"review_completed\",\"findings\":{coderabbit_findings}}}'\n"
                + ("exit 1\n" if coderabbit_findings > 0 else "exit 0\n")
            )
            cr.chmod(0o755)

            msmtp = root / "msmtp"
            msmtp.write_text("#!/bin/sh\ncat > /dev/null\nexit 0\n")
            msmtp.chmod(0o755)

            spec = importlib.util.spec_from_file_location("authorization_runner", BASE / "runner.py")
            runner = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(runner)
            runner.REPO_PATH = str(repo)
            runner.STATE_DIR = str(root / "state")
            runner.WORKTREES_BASE = str(root / "worktrees")
            runner.TRUSTED_AUTHORS = {"dwojtaszek"}
            runner.DRY_RUN = False
            runner.REVIEW_GATE_SCRIPT = "tests/wait-for-reviews.sh"
            runner.wait_for_tokens = lambda: None
            runner._babysit_with_fallback = MagicMock()
            (root / "state").mkdir()

            yield root, repo, runner, directory

    def _create_tracked_pr_worktree(
        self, root, repo, wt_commit=None, remote_files=None, branch="fix/ISSUE-42-proof", pr_number=99, gate_error=None
    ):
        def git(*args):
            return subprocess.check_output(["git", "-C", str(repo), *args], text=True, stderr=subprocess.DEVNULL).strip()

        git("branch", branch)
        git("checkout", branch)
        if remote_files:
            for path, content in remote_files.items():
                (repo / path).write_text(content)
            git("add", ".")
            git("commit", "-m", "fix: remote change")
        head = git("rev-parse", "HEAD")
        git("checkout", "main")
        git("clone", "--bare", str(repo), str(root / "origin"))
        git("remote", "add", "origin", str(root / "origin"))

        wt_path = root / "worktrees/issue-42"
        target = wt_commit if wt_commit is not None else head
        subprocess.run(["git", "-C", str(repo), "worktree", "add", str(wt_path), target], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(wt_path), "checkout", "-B", branch], check=True, capture_output=True)

        pr = {
            "number": pr_number, "state": "open", "draft": False,
            "user": {"login": "dwojtaszek", "type": "User", "id": 1},
            "base": {"ref": "main", "repo": {"full_name": "dwojtaszek/zipper"}},
            "head": {"ref": branch, "sha": head, "repo": {"full_name": "dwojtaszek/zipper"}},
        }
        (root / "pr.json").write_text(json.dumps(pr))
        (root / "state" / "issue-42.json").write_text(json.dumps({
            "pr_number": str(pr_number), "branch": branch, "authorized_head": head,
        }))
        if gate_error is not None:
            (root / "state" / f"pr-{pr_number}-coderabbit-gate.json").write_text(json.dumps({
                "pr_number": pr_number, "branch": branch, "error": gate_error, "completion_ready": True
            }))
        return wt_path, head

    def test_babysit_activeWorktree_behindAuthorizedHead_fastForwardsAndMerges(self):
        with self._setup_fixture() as (root, repo, runner, directory):
            initial = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip()
            wt_path, head = self._create_tracked_pr_worktree(root, repo, wt_commit=initial, remote_files={"remote.txt": "remote"})

            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"], "FIXTURE": directory}):
                runner.babysit_active_worktrees()

            self.assertTrue((root / "merged").exists())
            main_head = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "main"], text=True).strip()
            self.assertEqual(main_head, head)

    def test_babysit_activeWorktree_divergedConflict_backsUpAndBlocksMerge(self):
        with self._setup_fixture() as (root, repo, runner, directory):
            (repo / "conflict.txt").write_text("initial")
            subprocess.run(["git", "-C", str(repo), "add", "."], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(repo), "commit", "-m", "test: base"], check=True, capture_output=True)
            initial = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip()

            wt_path, head = self._create_tracked_pr_worktree(
                root, repo, wt_commit=initial, remote_files={"conflict.txt": "remote version"}
            )
            (wt_path / "conflict.txt").write_text("local conflicting version")
            subprocess.run(["git", "-C", str(wt_path), "commit", "-am", "local diverge"], check=True, capture_output=True)
            local_diverged_sha = subprocess.check_output(["git", "-C", str(wt_path), "rev-parse", "HEAD"], text=True).strip()

            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"], "FIXTURE": directory}):
                runner.babysit_active_worktrees()

            # Merge must be blocked because rebase conflicted!
            self.assertFalse((root / "merged").exists())
            # Verify backup branch was created and points to the diverged commit
            backup_branches = [b.strip() for b in subprocess.check_output(["git", "-C", str(repo), "branch", "--list", "runner-backup-*"], text=True).strip().split()]
            self.assertTrue(len(backup_branches) >= 1)
            backup_sha = subprocess.check_output(["git", "-C", str(repo), "rev-parse", backup_branches[0]], text=True).strip()
            self.assertEqual(backup_sha, local_diverged_sha)

    def test_babysit_activeWorktree_divergedClean_rebasesAndPublishes(self):
        with self._setup_fixture() as (root, repo, runner, directory):
            initial = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip()
            wt_path, head = self._create_tracked_pr_worktree(
                root, repo, wt_commit=initial, remote_files={"remote_change.txt": "remote"}
            )
            (wt_path / "local_change.txt").write_text("local")
            subprocess.run(["git", "-C", str(wt_path), "add", "."], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(wt_path), "commit", "-m", "local non-conflicting change"], check=True, capture_output=True)

            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"], "FIXTURE": directory}):
                runner.babysit_active_worktrees()

            # Clean rebase succeeds, publishes to remote, and clears gate failure
            self.assertFalse((root / "state" / "pr-99-coderabbit-gate.json").exists())
            remote_sha = subprocess.check_output(["git", "--git-dir", str(root / "origin"), "rev-parse", "refs/heads/fix/ISSUE-42-proof"], text=True).strip()
            local_sha = subprocess.check_output(["git", "-C", str(wt_path), "rev-parse", "HEAD"], text=True).strip()
            self.assertEqual(remote_sha, local_sha)
            saved_state = json.loads((root / "state" / "issue-42.json").read_text())
            self.assertEqual(saved_state.get("authorized_head"), local_sha)

    def test_babysit_activeWorktree_dirtyWorktree_doesNotReconcileOrReset(self):
        with self._setup_fixture() as (root, repo, runner, directory):
            initial = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip()
            wt_path, head = self._create_tracked_pr_worktree(
                root, repo, wt_commit=initial, remote_files={"remote_change.txt": "remote"}
            )
            (wt_path / "uncommitted.txt").write_text("uncommitted dirty work")

            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"], "FIXTURE": directory}):
                runner.babysit_active_worktrees()

            # Merge blocked, uncommitted file preserved, worktree not reset
            self.assertFalse((root / "merged").exists())
            self.assertTrue((wt_path / "uncommitted.txt").exists())
            self.assertEqual((wt_path / "uncommitted.txt").read_text(), "uncommitted dirty work")

    def test_babysit_activeWorktree_staleCodeRabbitFailure_clearedWhenClean(self):
        with self._setup_fixture() as (root, repo, runner, directory):
            wt_path, head = self._create_tracked_pr_worktree(
                root, repo, remote_files={"proof.txt": "proof"}, gate_error="stale failure"
            )

            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"], "FIXTURE": directory}):
                runner.babysit_active_worktrees()

            self.assertFalse((root / "state" / "pr-99-coderabbit-gate.json").exists())
            self.assertTrue((root / "merged").exists())
            main_head = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "main"], text=True).strip()
            self.assertEqual(main_head, head)

    def test_babysit_activeWorktree_persistentCodeRabbitFailure_blocksMerge(self):
        with self._setup_fixture(coderabbit_findings=3) as (root, repo, runner, directory):
            wt_path, head = self._create_tracked_pr_worktree(
                root, repo, remote_files={"proof.txt": "proof"}, gate_error="prior failure"
            )

            with patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"], "FIXTURE": directory}):
                runner.babysit_active_worktrees()

            # Gate failure persists and merge is blocked!
            self.assertTrue((root / "state" / "pr-99-coderabbit-gate.json").exists())
            self.assertFalse((root / "merged").exists())
            runner._babysit_with_fallback.assert_called_once()
            self.assertIn("local CodeRabbit review gate failed for PR #99", runner._babysit_with_fallback.call_args[0][0])


if __name__ == "__main__":
    unittest.main()

