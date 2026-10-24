"""Contract and behavioral tests for truthful git hooks (#1219):
1. Missing dotnet toolchain exits non-zero (exit 1) in pre-commit and pre-push
2. Windows .bat is invoked via cmd.exe //c on MINGW/MSYS/CYGWIN with injected-failure handling
3. Pre-push reads stdin refs:
   - Non-current branch push cannot borrow current-HEAD pre-commit.ok validation
   - Push with outgoing tree differing from current checkout is rejected
   - Remote branch deletion skips checks and succeeds
   - Pushing current HEAD with matching fresh pre-commit.ok skips unit tests
   - Pushing different commit with same tree does not borrow pre-commit.ok
   - Injected failure in Windows cmd.exe runner halts push
"""
from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class GitHooksContractTests(unittest.TestCase):
    def setUp(self):
        self.pre_commit_path = ROOT / ".github/hooks/pre-commit"
        self.pre_push_path = ROOT / ".github/hooks/pre-push"
        self.pre_commit_text = self.pre_commit_path.read_text(encoding="utf-8")
        self.pre_push_text = self.pre_push_path.read_text(encoding="utf-8")

    def test_pre_commit_missing_dotnet_exits_non_zero(self):
        """pre-commit must exit non-zero (exit 1) when dotnet is not found."""
        self.assertNotIn("dotnet not found; skipping pre-commit checks.\n    exit 0", self.pre_commit_text)
        self.assertIn("exit 1", self.pre_commit_text)

    def test_pre_push_missing_dotnet_exits_non_zero(self):
        """pre-push must exit non-zero (exit 1) when dotnet is not found."""
        self.assertNotIn("dotnet not found; skipping pre-push checks.\n    exit 0", self.pre_push_text)
        self.assertIn("exit 1", self.pre_push_text)

    def test_pre_push_windows_invokes_cmd_exe(self):
        """pre-push on MINGW/MSYS/CYGWIN must invoke cmd.exe //c tests\\run-e2e-basic.bat."""
        self.assertIn('cmd.exe //c "tests\\\\run-e2e-basic.bat"', self.pre_push_text)
        self.assertNotIn("./tests/run-e2e-basic.bat", self.pre_push_text)

    def test_pre_push_reads_stdin_refs(self):
        """pre-push must read refs from standard input rather than assuming HEAD."""
        self.assertIn("read -r local_ref local_sha remote_ref remote_sha", self.pre_push_text)


@unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
class GitHooksBehavioralTests(unittest.TestCase):
    def setUp(self):
        self.tmpdir = tempfile.TemporaryDirectory()
        self.repo_dir = Path(self.tmpdir.name)
        # Initialize test repo
        self._git("init", "-b", "main")
        self._git("config", "user.email", "test@example.com")
        self._git("config", "user.name", "Test User")

        # Copy hook scripts into the repo
        self.hooks_dir = self.repo_dir / ".github/hooks"
        self.hooks_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / ".github/hooks/pre-commit", self.hooks_dir / "pre-commit")
        shutil.copy2(ROOT / ".github/hooks/pre-push", self.hooks_dir / "pre-push")
        shutil.copy2(ROOT / ".github/hooks/post-commit", self.hooks_dir / "post-commit")

        # Shared mock dotnet so behavioral tests do not depend on host toolchain
        self.bin_dir = self.repo_dir / "mock-bin"
        self.bin_dir.mkdir(parents=True, exist_ok=True)
        mock_dotnet = self.bin_dir / "dotnet"
        mock_dotnet.write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        mock_dotnet.chmod(0o755)
        self.env = {k: v for k, v in os.environ.items() if k not in ("DOTNET_ROOT",)}
        self.env["PATH"] = f"{self.bin_dir}:{self.env.get('PATH', '')}"

    def tearDown(self):
        self.tmpdir.cleanup()

    def _git(self, *args: str) -> str:
        res = subprocess.run(
            ["git", *args],
            cwd=self.repo_dir,
            capture_output=True,
            text=True,
            check=True,
        )
        return res.stdout.strip()

    def _env_without_dotnet(self) -> dict[str, str]:
        bin_dir = self.repo_dir / "isolated-bin"
        bin_dir.mkdir(exist_ok=True)
        for tool in (
            "bash", "git", "dirname", "basename", "cat", "grep", "sed",
            "sort", "head", "tail", "tr", "cut", "date", "mktemp", "rm", "uname",
        ):
            found = shutil.which(tool)
            if found and not (bin_dir / tool).exists():
                (bin_dir / tool).symlink_to(found)
        env = {k: v for k, v in os.environ.items() if k not in ("PATH", "DOTNET_ROOT")}
        env["PATH"] = str(bin_dir)
        return env

    def test_missing_dotnet_fails_pre_commit(self):
        """Pre-commit must fail (exit 1) if dotnet is missing when changes are staged."""
        (self.repo_dir / "README.md").write_text("Hello", encoding="utf-8")
        self._git("add", "README.md")
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-commit")],
            cwd=self.repo_dir,
            env=self._env_without_dotnet(),
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(
            res.returncode,
            1,
            f"Expected pre-commit to exit 1 when dotnet is missing. Output: {res.stdout}\n{res.stderr}",
        )
        self.assertIn("dotnet not found", res.stderr)

    def test_missing_dotnet_fails_pre_push(self):
        """Pre-push must fail (exit 1) if dotnet is missing."""
        (self.repo_dir / "README.md").write_text("Hello", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "init")

        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push")],
            cwd=self.repo_dir,
            env=self._env_without_dotnet(),
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(
            res.returncode,
            1,
            f"Expected pre-push to exit 1 when dotnet is missing. Output: {res.stdout}\n{res.stderr}",
        )
        self.assertIn("dotnet not found", res.stderr)

    def test_pre_push_deleting_remote_ref_skips_checks(self):
        """Pre-push must exit 0 immediately when deleting a remote ref."""
        (self.repo_dir / "README.md").write_text("Hello", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "init")

        stdin_input = "(delete) 0000000000000000000000000000000000000000 refs/heads/feature 1111111111111111111111111111111111111111\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(res.returncode, 0, f"Expected 0 on ref deletion. Output: {res.stdout}\n{res.stderr}")
        self.assertIn("Deleting remote ref", res.stdout)

    def test_non_current_branch_cannot_borrow_head_pre_commit_ok(self):
        """Pushing a non-current ref whose tree differs from HEAD cannot borrow pre-commit.ok and must abort."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "initial")
        head_commit = self._git("rev-parse", "HEAD")

        # Create branch feature with different content
        self._git("checkout", "-b", "feature")
        (self.repo_dir / "README.md").write_text("Feature content", encoding="utf-8")
        self._git("commit", "-am", "feature commit")
        feature_commit = self._git("rev-parse", "HEAD")

        # Switch back to main
        self._git("checkout", "main")

        # Create a fake fresh pre-commit.ok for main's HEAD
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        now = int(time.time())
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={head_commit}\n", encoding="utf-8")

        # Now simulate git push origin feature while checked out on main:
        # feature has a different tree than main!
        stdin_input = f"refs/heads/feature {feature_commit} refs/heads/feature 0000000000000000000000000000000000000000\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotEqual(
            res.returncode,
            0,
            f"Pushing a non-current branch whose tree differs from checkout must not succeed. Output: {res.stdout}\n{res.stderr}",
        )
        self.assertNotIn("Unit tests already passed in pre-commit", res.stdout)
        self.assertIn("current checkout tree differs from outgoing tree", res.stderr)

    def test_matching_head_borrows_fresh_pre_commit_ok(self):
        """Pushing current HEAD with fresh pre-commit.ok skips unit tests."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "initial")
        head_commit = self._git("rev-parse", "HEAD")

        # Create fresh pre-commit.ok for HEAD
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        now = int(time.time())
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={head_commit}\n", encoding="utf-8")

        # Mock mutation smoke and e2e smoke so they pass
        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        smoke_script = tests_dir / "run-mutation-smoke.sh"
        smoke_script.write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        smoke_script.chmod(0o755)

        stdin_input = f"refs/heads/main {head_commit} refs/heads/main 0000000000000000000000000000000000000000\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(res.returncode, 0, f"Expected 0 exit code. Output: {res.stdout}\n{res.stderr}")
        self.assertIn("Unit tests already passed in pre-commit", res.stdout)

    def test_different_commit_with_same_tree_does_not_borrow_pre_commit_ok(self):
        """A different commit object with the same tree cannot borrow pre-commit.ok."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "commit 1")
        commit1 = self._git("rev-parse", "HEAD")

        # Create commit 2 with same tree (empty commit)
        self._git("commit", "--allow-empty", "-m", "commit 2")
        commit2 = self._git("rev-parse", "HEAD")
        self.assertNotEqual(commit1, commit2)

        # Marker was recorded only for commit 1
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        now = int(time.time())
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={commit1}\n", encoding="utf-8")

        # Set up a mock dotnet that logs invocations
        mock_bin_dir = self.repo_dir / "bin"
        mock_bin_dir.mkdir(parents=True, exist_ok=True)
        mock_dotnet = mock_bin_dir / "dotnet"
        mock_dotnet.write_text("#!/bin/bash\necho \"MOCK_DOTNET_CALLED: $@\"\nexit 0\n", encoding="utf-8")
        mock_dotnet.chmod(0o755)

        # Mock mutation smoke
        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        (tests_dir / "run-mutation-smoke.sh").write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        (tests_dir / "run-mutation-smoke.sh").chmod(0o755)

        env = {k: v for k, v in os.environ.items() if k not in ("DOTNET_ROOT",)}
        env["PATH"] = f"{mock_bin_dir}:{env.get('PATH', '')}"

        stdin_input = f"refs/heads/main {commit2} refs/heads/main 0000000000000000000000000000000000000000\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotIn("Unit tests already passed in pre-commit", res.stdout)
        self.assertIn("MOCK_DOTNET_CALLED", res.stdout)

    def test_windows_injected_cmd_failure_aborts_push(self):
        """Simulated Windows failure in cmd.exe //c halts push with exit 1."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "commit 1")
        commit = self._git("rev-parse", "HEAD")

        # Mock pre-commit.ok so unit tests skip
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        now = int(time.time())
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={commit}\n", encoding="utf-8")

        # Mock mutation smoke
        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        (tests_dir / "run-mutation-smoke.sh").write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        (tests_dir / "run-mutation-smoke.sh").chmod(0o755)

        # Create run-e2e-basic.bat
        (tests_dir / "run-e2e-basic.bat").write_text("@echo off\nexit /b 1\n", encoding="utf-8")

        # Set origin/main to initial commit so diff against merge-base detects changes
        self._git("update-ref", "refs/remotes/origin/main", commit)

        # Force RUN_E2E=true by creating a change in src/
        (self.repo_dir / "src").mkdir(parents=True, exist_ok=True)
        (self.repo_dir / "src/File.cs").write_text("// change", encoding="utf-8")
        self._git("add", "src/File.cs")
        self._git("commit", "-m", "add src")
        new_commit = self._git("rev-parse", "HEAD")
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={new_commit}\n", encoding="utf-8")

        # Create mock uname returning MINGW64_NT and mock cmd.exe returning exit 1
        mock_bin_dir = self.repo_dir / "bin"
        mock_bin_dir.mkdir(parents=True, exist_ok=True)
        mock_uname = mock_bin_dir / "uname"
        mock_uname.write_text("#!/bin/bash\necho \"MINGW64_NT-10.0\"\n", encoding="utf-8")
        mock_uname.chmod(0o755)

        mock_cmd = mock_bin_dir / "cmd.exe"
        mock_cmd.write_text("#!/bin/bash\necho \"Simulated cmd.exe failure\" >&2\nexit 1\n", encoding="utf-8")
        mock_cmd.chmod(0o755)

        env = dict(os.environ)
        env["PATH"] = f"{mock_bin_dir}:{env.get('PATH', '')}"

        stdin_input = f"refs/heads/main {new_commit} refs/heads/main 0000000000000000000000000000000000000000\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(res.returncode, 1)
        self.assertIn("Basic E2E smoke tests failed", res.stdout)

    def test_pre_push_empty_stdin_manual_invocation_validates_head(self):
        """pre-push without args and empty stdin validates HEAD instead of falsely claiming deletion."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "commit 1")
        head_commit = self._git("rev-parse", "HEAD")

        # Set up a fresh pre-commit.ok marker
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        now = int(time.time())
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={head_commit}\n", encoding="utf-8")

        # Mock mutation smoke
        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        (tests_dir / "run-mutation-smoke.sh").write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        (tests_dir / "run-mutation-smoke.sh").chmod(0o755)

        # Run pre-push with empty stdin and no arguments (manual invocation)
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push")],
            cwd=self.repo_dir,
            input="",
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(res.returncode, 0)
        self.assertNotIn("Deleting remote ref", res.stdout)
        self.assertIn("Unit tests already passed in pre-commit", res.stdout)

    def test_pre_push_up_to_date_skips_checks(self):
        """When git push runs with no outgoing refs, pre-push skips checks and reports up-to-date."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "commit 1")

        # Git calls pre-push <remote> <url> with empty stdin when up-to-date
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input="",
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(res.returncode, 0)
        self.assertNotIn("Deleting remote ref", res.stdout)
        self.assertIn("Everything up-to-date (no outgoing refs to push); skipping pre-push checks.", res.stdout)

    def test_annotated_tag_pointing_to_head_borrows_pre_commit_ok(self):
        """Pushing an annotated tag pointing to a verified HEAD peels to commit and borrows pre-commit.ok."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "commit 1")
        head_commit = self._git("rev-parse", "HEAD")

        # Create annotated tag
        self._git("tag", "-a", "v1.0.0", "-m", "release v1.0.0")
        tag_sha = self._git("rev-parse", "refs/tags/v1.0.0")
        self.assertNotEqual(head_commit, tag_sha)

        # Create pre-commit.ok marker for HEAD
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        now = int(time.time())
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={head_commit}\n", encoding="utf-8")

        # Mock mutation smoke
        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        (tests_dir / "run-mutation-smoke.sh").write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        (tests_dir / "run-mutation-smoke.sh").chmod(0o755)

        # Stdin gives tag_sha for the tag ref
        stdin_input = f"refs/tags/v1.0.0 {tag_sha} refs/tags/v1.0.0 0000000000000000000000000000000000000000\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(res.returncode, 0, f"Output: {res.stdout}\n{res.stderr}")
        self.assertIn("Unit tests already passed in pre-commit", res.stdout)

    def test_expired_pre_commit_ok_runs_unit_tests(self):
        """pre-commit.ok older than 600s does not skip unit tests."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "commit 1")
        head_commit = self._git("rev-parse", "HEAD")

        # Marker from 601 seconds ago
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        stale_ts = int(time.time()) - 601
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={stale_ts}\nhead={head_commit}\n", encoding="utf-8")

        mock_bin_dir = self.repo_dir / "bin"
        mock_bin_dir.mkdir(parents=True, exist_ok=True)
        mock_dotnet = mock_bin_dir / "dotnet"
        mock_dotnet.write_text("#!/bin/bash\necho \"MOCK_DOTNET_CALLED: $@\"\nexit 0\n", encoding="utf-8")
        mock_dotnet.chmod(0o755)

        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        (tests_dir / "run-mutation-smoke.sh").write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        (tests_dir / "run-mutation-smoke.sh").chmod(0o755)

        env = {k: v for k, v in os.environ.items() if k not in ("DOTNET_ROOT",)}
        env["PATH"] = f"{mock_bin_dir}:{env.get('PATH', '')}"

        stdin_input = f"refs/heads/main {head_commit} refs/heads/main 0000000000000000000000000000000000000000\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotIn("Unit tests already passed in pre-commit", res.stdout)
        self.assertIn("MOCK_DOTNET_CALLED", res.stdout)

    def test_dirty_working_tree_invalidates_pre_commit_ok(self):
        """Unstaged modifications invalidate pre-commit.ok skip."""
        (self.repo_dir / "README.md").write_text("Initial", encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", "commit 1")
        head_commit = self._git("rev-parse", "HEAD")

        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        now = int(time.time())
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={now}\nhead={head_commit}\n", encoding="utf-8")

        # Dirty working tree (unstaged edit)
        (self.repo_dir / "README.md").write_text("Modified unstaged", encoding="utf-8")

        mock_bin_dir = self.repo_dir / "bin"
        mock_bin_dir.mkdir(parents=True, exist_ok=True)
        mock_dotnet = mock_bin_dir / "dotnet"
        mock_dotnet.write_text("#!/bin/bash\necho \"MOCK_DOTNET_CALLED: $@\"\nexit 0\n", encoding="utf-8")
        mock_dotnet.chmod(0o755)

        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        (tests_dir / "run-mutation-smoke.sh").write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        (tests_dir / "run-mutation-smoke.sh").chmod(0o755)

        env = {k: v for k, v in os.environ.items() if k not in ("DOTNET_ROOT",)}
        env["PATH"] = f"{mock_bin_dir}:{env.get('PATH', '')}"

        stdin_input = f"refs/heads/main {head_commit} refs/heads/main 0000000000000000000000000000000000000000\n"
        res = subprocess.run(
            ["bash", str(self.hooks_dir / "pre-push"), "origin", "git@github.com:example/repo.git"],
            cwd=self.repo_dir,
            input=stdin_input,
            env=env,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotIn("Unit tests already passed in pre-commit", res.stdout)
        self.assertIn("MOCK_DOTNET_CALLED", res.stdout)


if __name__ == "__main__":
    unittest.main()
