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

    def _commit_readme(self, content: str = "Initial", msg: str = "commit") -> str:
        (self.repo_dir / "README.md").write_text(content, encoding="utf-8")
        self._git("add", "README.md")
        self._git("commit", "-m", msg)
        return self._git("rev-parse", "HEAD")

    def _write_pre_commit_ok(self, head: str, timestamp: int | None = None) -> None:
        marker_dir = self.repo_dir / ".git/zipper-hooks"
        marker_dir.mkdir(parents=True, exist_ok=True)
        ts = int(time.time()) if timestamp is None else timestamp
        (marker_dir / "pre-commit.ok").write_text(f"timestamp={ts}\nhead={head}\n", encoding="utf-8")

    def _mock_mutation_smoke(self) -> None:
        tests_dir = self.repo_dir / "tests"
        tests_dir.mkdir(parents=True, exist_ok=True)
        smoke_script = tests_dir / "run-mutation-smoke.sh"
        smoke_script.write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
        smoke_script.chmod(0o755)

    def _env_with_logging_dotnet(self) -> dict[str, str]:
        mock_bin_dir = self.repo_dir / "bin"
        mock_bin_dir.mkdir(parents=True, exist_ok=True)
        mock_dotnet = mock_bin_dir / "dotnet"
        mock_dotnet.write_text("#!/bin/bash\necho \"MOCK_DOTNET_CALLED: $@\"\nexit 0\n", encoding="utf-8")
        mock_dotnet.chmod(0o755)
        self._mock_mutation_smoke()
        env = {k: v for k, v in os.environ.items() if k not in ("DOTNET_ROOT",)}
        env["PATH"] = f"{mock_bin_dir}:{env.get('PATH', '')}"
        return env

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

    def _run_pre_push(
        self,
        stdin_input: str = "",
        env: dict[str, str] | None = None,
        args: list[str] | None = None,
    ) -> subprocess.CompletedProcess[str]:
        cmd = ["bash", str(self.hooks_dir / "pre-push")]
        if args is None:
            cmd.extend(["origin", "git@github.com:example/repo.git"])
        else:
            cmd.extend(args)
        return subprocess.run(
            cmd,
            cwd=self.repo_dir,
            input=stdin_input,
            env=self.env if env is None else env,
            capture_output=True,
            text=True,
            check=False,
        )

    def _main_ref_stdin(self, sha: str) -> str:
        return f"refs/heads/main {sha} refs/heads/main 0000000000000000000000000000000000000000\n"

    def _assert_dotnet_called(self, res: subprocess.CompletedProcess[str]) -> None:
        self.assertNotIn("Unit tests already passed in pre-commit", res.stdout)
        self.assertIn("MOCK_DOTNET_CALLED", res.stdout)

    def _assert_pre_commit_ok_borrowed(self, res: subprocess.CompletedProcess[str]) -> None:
        self.assertEqual(res.returncode, 0, f"Expected 0 exit code. Output: {res.stdout}\n{res.stderr}")
        self.assertIn("Unit tests already passed in pre-commit", res.stdout)

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
        self._commit_readme(msg="init")
        res = self._run_pre_push(env=self._env_without_dotnet(), args=[])
        self.assertEqual(
            res.returncode,
            1,
            f"Expected pre-push to exit 1 when dotnet is missing. Output: {res.stdout}\n{res.stderr}",
        )
        self.assertIn("dotnet not found", res.stderr)

    def test_pre_push_deleting_remote_ref_skips_checks(self):
        """Pre-push must exit 0 immediately when deleting a remote ref."""
        self._commit_readme(msg="init")
        stdin_input = "(delete) 0000000000000000000000000000000000000000 refs/heads/feature 1111111111111111111111111111111111111111\n"
        res = self._run_pre_push(stdin_input)
        self.assertEqual(res.returncode, 0, f"Expected 0 on ref deletion. Output: {res.stdout}\n{res.stderr}")
        self.assertIn("Deleting remote ref", res.stdout)

    def test_non_current_branch_cannot_borrow_head_pre_commit_ok(self):
        """Pushing a non-current ref whose tree differs from HEAD cannot borrow pre-commit.ok and must abort."""
        head_commit = self._commit_readme(msg="initial")

        # Create branch feature with different content
        self._git("checkout", "-b", "feature")
        (self.repo_dir / "README.md").write_text("Feature content", encoding="utf-8")
        self._git("commit", "-am", "feature commit")
        feature_commit = self._git("rev-parse", "HEAD")

        # Switch back to main
        self._git("checkout", "main")
        self._write_pre_commit_ok(head_commit)

        # Now simulate git push origin feature while checked out on main:
        # feature has a different tree than main!
        stdin_input = f"refs/heads/feature {feature_commit} refs/heads/feature 0000000000000000000000000000000000000000\n"
        res = self._run_pre_push(stdin_input)
        self.assertNotEqual(
            res.returncode,
            0,
            f"Pushing a non-current branch whose tree differs from checkout must not succeed. Output: {res.stdout}\n{res.stderr}",
        )
        self.assertNotIn("Unit tests already passed in pre-commit", res.stdout)
        self.assertIn("current checkout tree differs from outgoing tree", res.stderr)

    def test_matching_head_borrows_fresh_pre_commit_ok(self):
        """Pushing current HEAD with fresh pre-commit.ok skips unit tests."""
        head_commit = self._commit_readme(msg="initial")
        self._write_pre_commit_ok(head_commit)
        self._mock_mutation_smoke()

        res = self._run_pre_push(self._main_ref_stdin(head_commit))
        self._assert_pre_commit_ok_borrowed(res)

    def test_different_commit_with_same_tree_does_not_borrow_pre_commit_ok(self):
        """A different commit object with the same tree cannot borrow pre-commit.ok."""
        commit1 = self._commit_readme(msg="commit 1")
        self._git("commit", "--allow-empty", "-m", "commit 2")
        commit2 = self._git("rev-parse", "HEAD")
        self.assertNotEqual(commit1, commit2)

        self._write_pre_commit_ok(commit1)
        res = self._run_pre_push(self._main_ref_stdin(commit2), env=self._env_with_logging_dotnet())
        self._assert_dotnet_called(res)

    def test_windows_injected_cmd_failure_aborts_push(self):
        """Simulated Windows failure in cmd.exe //c halts push with exit 1."""
        commit = self._commit_readme(msg="commit 1")
        self._write_pre_commit_ok(commit)
        self._mock_mutation_smoke()

        # Create run-e2e-basic.bat
        (self.repo_dir / "tests/run-e2e-basic.bat").write_text("@echo off\nexit /b 1\n", encoding="utf-8")

        # Set origin/main to initial commit so diff against merge-base detects changes
        self._git("update-ref", "refs/remotes/origin/main", commit)

        # Force RUN_E2E=true by creating a change in src/
        (self.repo_dir / "src").mkdir(parents=True, exist_ok=True)
        (self.repo_dir / "src/File.cs").write_text("// change", encoding="utf-8")
        self._git("add", "src/File.cs")
        self._git("commit", "-m", "add src")
        new_commit = self._git("rev-parse", "HEAD")
        self._write_pre_commit_ok(new_commit)

        # Create mock uname returning MINGW64_NT and mock cmd.exe returning exit 1
        mock_bin_dir = self.repo_dir / "bin"
        mock_bin_dir.mkdir(parents=True, exist_ok=True)
        mock_uname = mock_bin_dir / "uname"
        mock_uname.write_text("#!/bin/bash\necho \"MINGW64_NT-10.0\"\n", encoding="utf-8")
        mock_uname.chmod(0o755)

        mock_cmd = mock_bin_dir / "cmd.exe"
        mock_cmd.write_text("#!/bin/bash\necho \"Simulated cmd.exe failure\" >&2\nexit 1\n", encoding="utf-8")
        mock_cmd.chmod(0o755)

        env = dict(self.env)
        env["PATH"] = f"{mock_bin_dir}:{env.get('PATH', '')}"

        res = self._run_pre_push(self._main_ref_stdin(new_commit), env=env)
        self.assertEqual(res.returncode, 1)
        self.assertIn("Basic E2E smoke tests failed", res.stdout)

    def test_pre_push_empty_stdin_manual_invocation_validates_head(self):
        """pre-push without args and empty stdin validates HEAD instead of falsely claiming deletion."""
        head_commit = self._commit_readme(msg="commit 1")
        self._write_pre_commit_ok(head_commit)
        self._mock_mutation_smoke()

        res = self._run_pre_push(args=[])
        self.assertNotIn("Deleting remote ref", res.stdout)
        self._assert_pre_commit_ok_borrowed(res)

    def test_pre_push_up_to_date_skips_checks(self):
        """When git push runs with no outgoing refs, pre-push skips checks and reports up-to-date."""
        self._commit_readme(msg="commit 1")

        res = self._run_pre_push()
        self.assertEqual(res.returncode, 0)
        self.assertNotIn("Deleting remote ref", res.stdout)
        self.assertIn("Everything up-to-date (no outgoing refs to push); skipping pre-push checks.", res.stdout)

    def test_annotated_tag_pointing_to_head_borrows_pre_commit_ok(self):
        """Pushing an annotated tag pointing to a verified HEAD peels to commit and borrows pre-commit.ok."""
        head_commit = self._commit_readme(msg="commit 1")

        # Create annotated tag
        self._git("tag", "-a", "v1.0.0", "-m", "release v1.0.0")
        tag_sha = self._git("rev-parse", "refs/tags/v1.0.0")
        self.assertNotEqual(head_commit, tag_sha)

        self._write_pre_commit_ok(head_commit)
        self._mock_mutation_smoke()

        stdin_input = f"refs/tags/v1.0.0 {tag_sha} refs/tags/v1.0.0 0000000000000000000000000000000000000000\n"
        res = self._run_pre_push(stdin_input)
        self._assert_pre_commit_ok_borrowed(res)

    def test_expired_pre_commit_ok_runs_unit_tests(self):
        """pre-commit.ok older than 600s does not skip unit tests."""
        head_commit = self._commit_readme(msg="commit 1")
        self._write_pre_commit_ok(head_commit, timestamp=int(time.time()) - 601)
        res = self._run_pre_push(self._main_ref_stdin(head_commit), env=self._env_with_logging_dotnet())
        self._assert_dotnet_called(res)

    def test_dirty_working_tree_invalidates_pre_commit_ok(self):
        """Unstaged modifications invalidate pre-commit.ok skip."""
        head_commit = self._commit_readme(msg="commit 1")
        self._write_pre_commit_ok(head_commit)
        (self.repo_dir / "README.md").write_text("Modified unstaged", encoding="utf-8")
        res = self._run_pre_push(self._main_ref_stdin(head_commit), env=self._env_with_logging_dotnet())
        self._assert_dotnet_called(res)

    def test_multi_ref_push_where_one_ref_unverified_runs_unit_tests(self):
        """When pushing multiple refs and one does not match pre-commit.ok, unit tests run."""
        commit1 = self._commit_readme(msg="commit 1")
        self._git("commit", "--allow-empty", "-m", "commit 2")
        commit2 = self._git("rev-parse", "HEAD")

        self._write_pre_commit_ok(commit1)
        stdin_input = (
            f"refs/heads/main {commit1} refs/heads/main 0000000000000000000000000000000000000000\n"
            f"refs/heads/other {commit2} refs/heads/other 0000000000000000000000000000000000000000\n"
        )
        res = self._run_pre_push(stdin_input, env=self._env_with_logging_dotnet())
        self._assert_dotnet_called(res)

    def test_annotated_tag_with_cli_changes_executes_e2e(self):
        """Pushing an annotated tag with CLI changes correctly identifies diff and executes E2E."""
        commit = self._commit_readme(msg="commit 1")
        self._git("update-ref", "refs/remotes/origin/main", commit)

        (self.repo_dir / "src").mkdir(parents=True, exist_ok=True)
        (self.repo_dir / "src/File.cs").write_text("// change", encoding="utf-8")
        self._git("add", "src/File.cs")
        self._git("commit", "-m", "add src")
        new_commit = self._git("rev-parse", "HEAD")
        self._write_pre_commit_ok(new_commit)
        self._mock_mutation_smoke()

        self._git("tag", "-a", "v2.0.0", "-m", "release v2.0.0", new_commit)
        tag_sha = self._git("rev-parse", "refs/tags/v2.0.0")

        (self.repo_dir / "tests/run-e2e-basic.sh").write_text("#!/bin/bash\necho \"MOCK_E2E_CALLED\"\nexit 0\n", encoding="utf-8")
        (self.repo_dir / "tests/run-e2e-basic.sh").chmod(0o755)

        stdin_input = f"refs/tags/v2.0.0 {tag_sha} refs/tags/v2.0.0 0000000000000000000000000000000000000000\n"
        res = self._run_pre_push(stdin_input)
        self.assertEqual(res.returncode, 0)
        self.assertIn("MOCK_E2E_CALLED", res.stdout)


if __name__ == "__main__":
    unittest.main()
