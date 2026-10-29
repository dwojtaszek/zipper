"""A squash merge must not hold the runner's only worktree slot forever (#1304).

A squash merge rewrites history, so the branch tip is a *descendant* of the merged
head rather than an ancestor of it: `git merge-base --is-ancestor <branch>
<merged_head>` is false even though everything the PR merged is in main.
`git cherry` cannot match the branch's commits against one squashed commit, and
`gh pr merge --delete-branch` removes the origin ref, so a single stale commit made
after the merge defeated all three deletion guards at once. On 2026-10-29 that
worktree held the only slot until a human ran `git reset --hard` by hand.

These tests drive the real `babysit_active_worktrees` entry point and the real
`_remove_worktree` against real Git refs and a stub `gh`, so the refusal and the
recovery are both exercised at the subprocess boundary rather than mocked.
"""
from contextlib import redirect_stdout
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
import unittest.mock

import gitenv  # noqa: F401

BASE = Path(__file__).resolve().parents[1]

BRANCH = "feat/ISSUE-1304-archive-tests-generalize-adapter-checks"


class _Fixture:
    """A repository with a bare origin, one worktree, and a stub `gh` on PATH."""

    def __init__(self, root: Path, pr_state: str = "MERGED"):
        self.root = root
        self.repo = root / "repo"
        subprocess.run(["git", "init", "-b", "main", str(self.repo)], check=True, capture_output=True)

        (self.repo / "base.txt").write_text("base")
        self.git("add", "base.txt")
        self.git("commit", "-m", "test: initial")

        origin = root / "origin"
        subprocess.run(["git", "clone", "--bare", str(self.repo), str(origin)], check=True, capture_output=True)
        self.git("remote", "add", "origin", str(origin))

        # The PR branch. Its head is what GitHub reports as the merged head.
        self.git("checkout", "-b", BRANCH)
        (self.repo / "work.txt").write_text("pr work")
        self.git("add", "work.txt")
        self.git("commit", "-m", "feat: the work the PR merged")
        self.merged_head = self.git("rev-parse", "HEAD")
        self.git("push", "-u", "origin", BRANCH)
        self.git("checkout", "main")

        self.worktree = root / "worktrees" / "issue-1304"
        self.git("worktree", "add", str(self.worktree), BRANCH)

        # The squash merge itself: main gains the content as one new commit, so the
        # branch's own commits are not ancestors of anything now in main.
        self.git("merge", "--squash", BRANCH)
        self.git("commit", "-m", "feat: the work the PR merged (#1380)")
        self.git("push", "origin", "main")
        # `gh pr merge --delete-branch` deleted the remote ref, which is guard three.
        self.git("push", "origin", "--delete", BRANCH)

        self.pr = {
            "number": 1380, "state": pr_state, "mergeable": "UNKNOWN",
            "statusCheckRollup": [], "reviews": [], "comments": [],
            "updatedAt": "2026-10-29T23:50:00Z",
            "headRefOid": self.merged_head, "headRefName": BRANCH,
        }
        (root / "pr.json").write_text(json.dumps(self.pr))
        self._write_stub_gh()
        self._load_module()

    def git(self, *args, cwd=None):
        return subprocess.run(["git", "-C", str(cwd or self.repo), *args], check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True).stdout.strip()

    def _write_stub_gh(self):
        gh = self.root / "gh"
        gh.write_text(
            "#!/usr/bin/env python3\n"
            "import os, sys\n"
            "from pathlib import Path\n"
            "root = Path(os.environ['FIXTURE'])\n"
            "args = sys.argv[1:]\n"
            "if args[:2] == ['pr', 'view']:\n"
            "    print((root / 'pr.json').read_text())\n"
            "elif args[:2] == ['pr', 'list']:\n"
            "    print('[]')\n"
            "elif args[:2] == ['api', 'rate_limit']:\n"
            "    print('4999')\n"
            "elif args[:2] == ['issue', 'view']:\n"
            "    print('{\"state\": \"CLOSED\", \"title\": \"t\", \"body\": \"b\"}')\n"
            "else:\n"
            "    sys.exit(1)\n"
        )
        gh.chmod(0o755)

    def _load_module(self):
        spec = importlib.util.spec_from_file_location("merge_cleanup_runner", BASE / "runner.py")
        self.runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runner)
        runner = self.runner
        runner.REPO_PATH = str(self.repo)
        runner.WORKTREES_BASE = str(self.root / "worktrees")
        runner.STATE_DIR = str(self.root / "state")
        runner.DRY_RUN = False
        runner.TRUSTED_AUTHORS = {"dwojtaszek"}
        (self.root / "state").mkdir(exist_ok=True)
        (self.root / "state" / "issue-1304.json").write_text(
            json.dumps({"pr_number": "1380", "branch": BRANCH, "authorized_head": self.merged_head}))
        self.emails = []
        runner.send_email = lambda subject, body: self.emails.append((subject, body))
        self.env = unittest.mock.patch.dict(
            os.environ,
            {"PATH": str(self.root) + os.pathsep + os.environ["PATH"], "FIXTURE": str(self.root)},
        )
        self.env.start()

    def close(self):
        self.env.stop()

    def commit_in_worktree(self, name: str, message: str) -> str:
        (self.worktree / name).write_text(message)
        self.git("add", name, cwd=self.worktree)
        self.git("commit", "-m", message, cwd=self.worktree)
        return self.git("rev-parse", "HEAD", cwd=self.worktree)

    def babysit(self) -> str:
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            self.runner.babysit_active_worktrees()
        return buffer.getvalue()

    def backup_branches(self) -> list[str]:
        out = self.git("branch", "--list", "runner-backup-*")
        return [line.strip().lstrip("*+ ") for line in out.splitlines() if line.strip()]

    def git_fails(self, *args) -> bool:
        result = subprocess.run(["git", "-C", str(self.repo), *args], capture_output=True, text=True)
        return result.returncode != 0


class SquashMergeCleanupTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    def fixture(self, **kwargs):
        fixture = _Fixture(self.root, **kwargs)
        self.addCleanup(fixture.close)
        return fixture

    def test_babysit_squashMergedPrWithPostMergeCommit_freesSlotAndPreservesCommit(self):
        """The #1304 shape, end to end: the slot frees and the commit survives."""
        fixture = self.fixture()
        extra = fixture.commit_in_worktree("post-merge.txt", "fix: post-merge mission work")
        self.assertFalse(fixture.git_fails("merge-base", "--is-ancestor", fixture.merged_head, extra),
                         "the merged head must be inside the branch's history")

        fixture.babysit()

        self.assertFalse(fixture.worktree.exists(),
                         "a squash-merged worktree must not keep the runner's only slot")
        self.assertNotIn(BRANCH, fixture.git("branch", "--list", BRANCH))
        backups = fixture.backup_branches()
        self.assertEqual(1, len(backups), f"expected exactly one pinned backup, got {backups}")
        self.assertEqual(extra, fixture.git("rev-parse", backups[0]))
        self.assertEqual("fix: post-merge mission work", fixture.git("log", "--format=%s", "-1", backups[0]))
        self.assertFalse(fixture.git_fails("cat-file", "-e", f"refs/heads/{backups[0]}:post-merge.txt"))
        self.assertTrue(fixture.git_fails("cat-file", "-e", "main:post-merge.txt"),
                        "the extra commit's content must not be in main, which is why it was pinned")
        self.assertFalse((self.root / "state" / "issue-1304.json").exists())
        subjects = [subject for subject, _ in fixture.emails]
        self.assertTrue(any(backups[0] in str(body) for _, body in fixture.emails),
                        f"the operator must be told where the commits went: {subjects}")

    def test_babysit_closedPrWithUnpushedCommits_preservesWorktreeAndBranch(self):
        """A close without a merge proves nothing, so the work is still protected."""
        fixture = self.fixture(pr_state="CLOSED")
        extra = fixture.commit_in_worktree("unmerged.txt", "fix: unmerged work")

        fixture.babysit()

        self.assertTrue(fixture.worktree.exists())
        self.assertIn(BRANCH, fixture.git("branch", "--list", BRANCH))
        self.assertEqual(extra, fixture.git("rev-parse", BRANCH))
        self.assertEqual([], fixture.backup_branches())

    def test_removeWorktree_pinFails_preservesWorktree(self):
        """Pinning is the promise that no commit is lost; failing it must fail closed."""
        fixture = self.fixture()
        fixture.commit_in_worktree("post-merge.txt", "fix: post-merge mission work")
        fixture.runner._pin_backup_branch = lambda *args, **kwargs: (None, False)

        removed = fixture.runner._remove_worktree(
            str(fixture.worktree), BRANCH, is_merged=True, merged_head=fixture.merged_head)

        self.assertFalse(removed)
        self.assertTrue(fixture.worktree.exists())
        self.assertIn(BRANCH, fixture.git("branch", "--list", BRANCH))

    def test_removeWorktree_removalKeepsFailing_reusesTheSamePin(self):
        """A pin per retry would pile up a backup branch every five minutes."""
        fixture = self.fixture()
        extra = fixture.commit_in_worktree("post-merge.txt", "fix: post-merge mission work")
        runner = fixture.runner
        original = runner.run_cmd

        def failing_worktree_remove(cmd, cwd=None, **kwargs):
            if cmd[:3] == ["git", "worktree", "remove"]:
                return 1, "", "fatal: failed to remove worktree"
            return original(cmd, cwd=cwd, **kwargs)

        runner.run_cmd = failing_worktree_remove
        try:
            self.assertFalse(runner._remove_worktree(str(fixture.worktree), BRANCH,
                                                     is_merged=True, merged_head=fixture.merged_head))
            self.assertFalse(runner._remove_worktree(str(fixture.worktree), BRANCH,
                                                     is_merged=True, merged_head=fixture.merged_head))
        finally:
            runner.run_cmd = original

        self.assertTrue(runner._remove_worktree(str(fixture.worktree), BRANCH,
                                               is_merged=True, merged_head=fixture.merged_head))
        self.assertEqual([extra], [fixture.git("rev-parse", b) for b in fixture.backup_branches()],
                         "both failed attempts must reuse the one pin at the same tip")

    def test_removeWorktree_mergedAtTheBranchTip_createsNoBackupBranch(self):
        """The ordinary squash merge has nothing to pin, so it must not mint one."""
        fixture = self.fixture()

        removed = fixture.runner._remove_worktree(
            str(fixture.worktree), BRANCH, is_merged=True, merged_head=fixture.merged_head)

        self.assertTrue(removed)
        self.assertFalse(fixture.worktree.exists())
        self.assertEqual([], fixture.backup_branches())

    def test_removeWorktree_branchRewrittenAwayFromTheMergedHead_mintsNoBackup(self):
        """A merged PR says nothing about a branch that no longer contains its head.

        Only the squash-merge shape proves the merged content is in main. Here the
        branch was rewritten off `main`, so the pin path must not fire: the branch is
        still removable through the origin check, and no backup is invented.
        """
        fixture = self.fixture()
        fixture.git("reset", "--hard", "main", cwd=fixture.worktree)
        rewritten = fixture.commit_in_worktree("rewritten.txt", "fix: unrelated work")
        fixture.git("push", "origin", BRANCH, cwd=fixture.worktree)
        self.assertTrue(fixture.git_fails("merge-base", "--is-ancestor", fixture.merged_head, rewritten))

        removed = fixture.runner._remove_worktree(
            str(fixture.worktree), BRANCH, is_merged=True, merged_head=fixture.merged_head)

        self.assertTrue(removed)
        self.assertEqual([], fixture.backup_branches())

    def test_babysit_dryRun_squashMergedWithPostMergeCommit_createsNoBackupRef(self):
        """A dry run's whole contract is "nothing is changed", and a ref is a change.

        `git branch <name> <tip>` used to be classified read-only, so this path
        really created the `runner-backup-*` ref while reporting a simulated
        cleanup. The repair is at the classification, so the pin still *reports* the
        name it would create but performs no ref write.
        """
        fixture = self.fixture()
        extra = fixture.commit_in_worktree("post-merge.txt", "fix: post-merge mission work")
        fixture.runner.DRY_RUN = True

        log = fixture.babysit()

        self.assertEqual([], fixture.backup_branches(), "a dry run must not create a ref")
        self.assertTrue(fixture.worktree.exists(), "a dry run must not remove the worktree")
        self.assertEqual(extra, fixture.git("rev-parse", BRANCH), "a dry run must not delete the branch")
        self.assertIn(f"git branch runner-backup-{BRANCH}", log,
                      "the dry run must still report the pin it would create")

    def test_dryRun_pinBackupBranch_returnsTheNameItWouldCreate_withoutCreatingIt(self):
        """The dry-run contract is an explicit choice: a usable name, no ref."""
        fixture = self.fixture()
        extra = fixture.commit_in_worktree("post-merge.txt", "fix: post-merge mission work")
        runner = fixture.runner
        runner.DRY_RUN = True

        name, created = runner._pin_backup_branch(str(fixture.worktree), BRANCH, extra)

        self.assertIsNotNone(name, "a dry-run report needs the name it would have used")
        self.assertRegex(name, rf"^runner-backup-{re.escape(BRANCH)}-\d{{8}}-\d{{6}}$")
        self.assertTrue(created, "a dry run reports the pin it would have made")
        self.assertTrue(fixture.git_fails("rev-parse", "--verify", f"refs/heads/{name}"),
                        "the reported ref must not exist after a dry run")
        self.assertEqual("", fixture.git("branch", "--list", "runner-backup-*"))

        # The same call outside a dry run really creates it at that tip.
        runner.DRY_RUN = False
        real_name, real_created = runner._pin_backup_branch(str(fixture.worktree), BRANCH, extra)
        self.assertTrue(real_created)
        self.assertEqual(extra, fixture.git("rev-parse", f"refs/heads/{real_name}"))

    def test_dryRun_gitBranchListingStillRunsForReal(self):
        """The classification must not swing the other way and bypass a read."""
        fixture = self.fixture()
        fixture.runner.DRY_RUN = True

        code, out, _ = fixture.runner.run_cmd(["git", "branch", "--list", BRANCH], cwd=str(fixture.repo))

        self.assertEqual(0, code)
        self.assertIn(BRANCH, out, "a listing in a dry run must return real output")
        self.assertNotIn("MOCK SUCCESS", out)


class GitBranchClassificationTests(unittest.TestCase):
    """Every real `git branch` form in the module, against the real classifier.

    `run_cmd` bypasses a mutating command in a dry run, so this classification is
    what decides whether a dry run writes a ref.
    """

    def setUp(self):
        spec = importlib.util.spec_from_file_location("classification_runner", BASE / "runner.py")
        self.runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runner)

    def test_isMutatingCmd_gitBranchListingForms_areReadOnly(self):
        for args in (
            ["git", "branch"],
            ["git", "branch", "--show-current"],
            ["git", "branch", "--list", "runner-backup-*", "--points-at", "a" * 40],
            ["git", "branch", "--list", "*ISSUE-1*"],
            ["git", "branch", "-r", "--list", "origin/*ISSUE-1*"],
            ["git", "branch", "-r"],
            ["git", "branch", "-vv"],
            ["git", "branch", "--contains", "a" * 40],
            ["git", "branch", "--format", "%(refname)"],
        ):
            with self.subTest(args=" ".join(args)):
                self.assertFalse(self.runner._is_mutating_cmd(args))

    def test_isMutatingCmd_gitBranchCreationAndRemoval_areMutating(self):
        for args in (
            ["git", "branch", "runner-backup-fix/x-20261030-000000", "b" * 40],
            ["git", "branch", "fix/ISSUE-1-extra", "HEAD"],
            ["git", "branch", "--force", "existing", "HEAD"],
            ["git", "branch", "-D", "fix/ISSUE-1"],
            ["git", "branch", "--delete", "fix/ISSUE-1"],
            ["git", "branch", "-m", "a", "b"],
            # These mutate the current branch and carry no positional operand, so
            # only the flag set can classify them; the positional rule cannot.
            ["git", "branch", "--unset-upstream"],
            ["git", "branch", "--edit-description"],
        ):
            with self.subTest(args=" ".join(args)):
                self.assertTrue(self.runner._is_mutating_cmd(args))

    def test_isMutatingCmd_unrecognisedFlagWithPositional_resolvesToMutating(self):
        """Fail toward bypass: a skipped listing costs a log line, a ref write does not."""
        self.assertTrue(self.runner._is_mutating_cmd(["git", "branch", "--no-such-flag", "name", "HEAD"]))


if __name__ == "__main__":
    unittest.main()
