"""Exercise authorization with real Git refs and subprocess CLI boundaries."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

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


if __name__ == "__main__":
    unittest.main()
