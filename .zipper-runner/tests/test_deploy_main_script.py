"""Tests for deploy-main.sh, the decoupled fast-forward of the runner checkout.

The script exists because the runner only syncs its checkout while it holds a
lock it also holds across long agent missions, so merged changes waited tens of
minutes to go live. The value of the script is entirely in what it refuses to do:
it must never discard work, and it must never fight a git operation already in
progress. These tests build a real origin and a real clone and assert on real git
state, because the safety property is about what git does to the filesystem.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import os
import shutil
import subprocess
import tempfile
import time
import unittest
from pathlib import Path

_SCRIPT = Path(__file__).resolve().parents[1] / "deploy-main.sh"


class _Repo:
    """An origin bare repo plus a checkout, with the script installed outside it."""

    def __init__(self, root: str):
        self.root = Path(root)
        self.origin = self.root / "origin.git"
        self.checkout = self.root / "checkout"
        self.installed = self.root / "bin" / "deploy-main.sh"
        self.installed.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(_SCRIPT, self.installed)
        self.installed.chmod(0o755)
        self._git("init", "--quiet", "--bare", "--initial-branch=main",
                  str(self.origin), cwd=self.root)
        self._git("clone", "--quiet", str(self.origin), str(self.checkout), cwd=self.root)
        self._git("config", "user.email", "t@example.invalid")
        self._git("config", "user.name", "Test")
        (self.checkout / "f.txt").write_text("one\n")
        (self.checkout / ".gitignore").write_text("secret.txt\n")
        self._git("add", "-A")
        self._git("commit", "--quiet", "-m", "c1")
        self._git("branch", "-M", "main")
        self._git("push", "--quiet", "-u", "origin", "main")

    def _push_upstream(self, pusher: Path, add_args: list[str]) -> str:
        self._git("clone", "--quiet", str(self.origin), str(pusher), cwd=self.root)
        subprocess.run(["git", "config", "user.email", "t@example.invalid"], cwd=pusher, check=True)
        subprocess.run(["git", "config", "user.name", "Test"], cwd=pusher, check=True)
        subprocess.run(["git", "add", *add_args], cwd=pusher, check=True)
        subprocess.run(["git", "commit", "--quiet", "-m", "upstream"], cwd=pusher, check=True)
        subprocess.run(["git", "push", "--quiet", "origin", "main"], cwd=pusher, check=True)
        return subprocess.run(["git", "rev-parse", "HEAD"], cwd=pusher,
                              capture_output=True, text=True).stdout.strip()

    def _git(self, *args, cwd=None):
        return subprocess.run(["git", *args], cwd=str(cwd or self.checkout),
                              capture_output=True, text=True, check=False)

    def head(self) -> str:
        return self._git("rev-parse", "HEAD").stdout.strip()

    def push_upstream_commit(self, content: str, message: str) -> str:
        pusher = self.root / f"pusher-{message}"
        self._git("clone", "--quiet", str(self.origin), str(pusher), cwd=self.root)
        subprocess.run(["git", "config", "user.email", "t@example.invalid"], cwd=pusher, check=True)
        subprocess.run(["git", "config", "user.name", "Test"], cwd=pusher, check=True)
        with open(pusher / "f.txt", "a") as handle:
            handle.write(content)
        subprocess.run(["git", "commit", "--quiet", "-am", message], cwd=pusher, check=True)
        subprocess.run(["git", "push", "--quiet", "origin", "main"], cwd=pusher, check=True)
        return subprocess.run(["git", "rev-parse", "HEAD"], cwd=pusher,
                              capture_output=True, text=True).stdout.strip()

    def push_upstream_ignoredPathNowTracked(self, path: str, content: str) -> str:
        """Push a commit that starts tracking a path the checkout ignores."""
        pusher = self.root / "pusher-ignored"
        self._git("clone", "--quiet", str(self.origin), str(pusher), cwd=self.root)
        subprocess.run(["git", "config", "user.email", "t@example.invalid"], cwd=pusher, check=True)
        subprocess.run(["git", "config", "user.name", "Test"], cwd=pusher, check=True)
        (pusher / path).write_text(content)
        subprocess.run(["git", "add", "-f", path], cwd=pusher, check=True)
        subprocess.run(["git", "commit", "--quiet", "-m", "track-ignored"], cwd=pusher, check=True)
        subprocess.run(["git", "push", "--quiet", "origin", "main"], cwd=pusher, check=True)
        return subprocess.run(["git", "rev-parse", "HEAD"], cwd=pusher,
                              capture_output=True, text=True).stdout.strip()

    def run_script(self) -> subprocess.CompletedProcess:
        env = dict(os.environ)
        env["REPO_PATH"] = str(self.checkout)
        env["DEPLOY_LOGS_DIR"] = str(self.root / "logs")
        env["DEPLOY_LOCK_PATH"] = str(self.root / "deploy.lock")
        return subprocess.run([str(self.installed)], capture_output=True, text=True, env=env)

    def logs(self) -> str:
        log_dir = self.root / "logs"
        if not log_dir.exists():
            return ""
        return "\n".join(path.read_text() for path in sorted(log_dir.glob("*.log")))


class DeployMainScriptTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.mkdtemp(prefix="zipper-deploy-test-")
        self.addCleanup(shutil.rmtree, self._tmp, ignore_errors=True)
        self.repo = _Repo(self._tmp)

    def test_deploy_whenBehind_shouldFastForwardAndLogTheMove(self):
        target = self.repo.push_upstream_commit("two\n", "c2")

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), target)
        self.assertIn("DEPLOYED", self.repo.logs())

    def test_deploy_whenAlreadyCurrent_shouldBeSilentAndDoNothing(self):
        before = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)
        self.assertNotIn("DEPLOYED", self.repo.logs())
        self.assertNotIn("SKIP", self.repo.logs())

    def test_deploy_whenTrackedFileModified_shouldSkipAndPreserveTheEdit(self):
        self.repo.push_upstream_commit("two\n", "c2")
        with open(self.repo.checkout / "f.txt", "a") as handle:
            handle.write("local work\n")
        before = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before, "a dirty checkout must not move")
        self.assertIn("uncommitted changes", self.repo.logs())
        self.assertIn("local work", (self.repo.checkout / "f.txt").read_text())

    def test_deploy_whenOnAnotherBranch_shouldSkip(self):
        self.repo.push_upstream_commit("two\n", "c2")
        self.repo._git("checkout", "--quiet", "-b", "feature")
        before = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)
        self.assertIn("not main", self.repo.logs())

    def test_deploy_whenRebaseInProgress_shouldSkip(self):
        self.repo.push_upstream_commit("two\n", "c2")
        git_dir = Path(self.repo._git("rev-parse", "--git-dir").stdout.strip())
        if not git_dir.is_absolute():
            git_dir = self.repo.checkout / git_dir
        (git_dir / "rebase-merge").mkdir()
        before = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)
        self.assertIn("in progress", self.repo.logs())

    def test_deploy_whenIndexLockHeld_shouldSkip(self):
        self.repo.push_upstream_commit("two\n", "c2")
        git_dir = Path(self.repo._git("rev-parse", "--git-dir").stdout.strip())
        if not git_dir.is_absolute():
            git_dir = self.repo.checkout / git_dir
        (git_dir / "index.lock").touch()
        before = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)
        self.assertIn("index.lock", self.repo.logs())

    def test_deploy_whenUntrackedFileWouldBeOverwritten_shouldSkipAndKeepTheFile(self):
        pusher = self.repo.root / "pusher-clash"
        self.repo._git("clone", "--quiet", str(self.repo.origin), str(pusher), cwd=self.repo.root)
        subprocess.run(["git", "config", "user.email", "t@example.invalid"], cwd=pusher, check=True)
        subprocess.run(["git", "config", "user.name", "Test"], cwd=pusher, check=True)
        (pusher / "clash.txt").write_text("upstream\n")
        subprocess.run(["git", "add", "-A"], cwd=pusher, check=True)
        subprocess.run(["git", "commit", "--quiet", "-m", "c3"], cwd=pusher, check=True)
        subprocess.run(["git", "push", "--quiet", "origin", "main"], cwd=pusher, check=True)
        (self.repo.checkout / "clash.txt").write_text("mine\n")
        before = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)
        self.assertEqual((self.repo.checkout / "clash.txt").read_text(), "mine\n",
                         "the untracked file must survive; nothing may be deleted to force the merge")
        self.assertIn("not a fast-forward", self.repo.logs())

    def test_deploy_whenUpstreamTracksAnIgnoredPath_shouldRefuseAndKeepTheLocalFile(self):
        """The tracked-file check cannot see ignored files, and git overwrites
        them during a merge by default. Measured: without `--no-overwrite-ignore`
        the incoming tracked file replaced the local ignored one. This is how a
        deploy could destroy the runner's own ignored runner.lock or logs."""
        self.repo.push_upstream_ignoredPathNowTracked("secret.txt", "upstream-secret\n")
        (self.repo.checkout / "secret.txt").write_text("LOCAL PRECIOUS\n")
        before = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before, "the deploy must refuse rather than overwrite")
        self.assertEqual((self.repo.checkout / "secret.txt").read_text(), "LOCAL PRECIOUS\n")
        self.assertIn("not a fast-forward", self.repo.logs())

    def test_deploy_whenFetchHangs_shouldGiveUpWithinTheBoundAndNotStall(self):
        """A real unreachable origin stalled git for ~300s. On a one-minute timer
        that holds the unit open and delays every deploy behind it, so the fetch
        is bounded and the next tick retries."""
        self.repo.push_upstream_commit("two\n", "c2")
        before = self.repo.head()

        # A git that hangs only on `fetch`, and is the real git for everything else.
        fake_bin = self.repo.root / "fakebin"
        fake_bin.mkdir(exist_ok=True)
        real_git = shutil.which("git")
        (fake_bin / "git").write_text(
            "#!/bin/bash\n"
            "for a in \"$@\"; do [ \"$a\" = fetch ] && { sleep 30; exit 1; }; done\n"
            f'exec "{real_git}" "$@"\n'
        )
        (fake_bin / "git").chmod(0o755)

        env = dict(os.environ)
        env["REPO_PATH"] = str(self.repo.checkout)
        env["DEPLOY_LOGS_DIR"] = str(self.repo.root / "logs")
        env["DEPLOY_LOCK_PATH"] = str(self.repo.root / "deploy.lock")
        env["DEPLOY_FETCH_TIMEOUT"] = "2"
        env["PATH"] = f"{fake_bin}:{env['PATH']}"

        started = time.monotonic()
        result = subprocess.run([str(self.repo.installed)], capture_output=True, text=True,
                                env=env, timeout=25)
        elapsed = time.monotonic() - started

        self.assertEqual(result.returncode, 0)
        self.assertLess(elapsed, 20, "the bounded fetch must give up, not stall the timer")
        self.assertEqual(self.repo.head(), before)
        self.assertIn("fetch failed or exceeded", self.repo.logs())

    def test_deploy_whenRepoPathUnsetAndScriptOutsideACheckout_shouldExplainItself(self):
        env = {k: v for k, v in os.environ.items() if k != "REPO_PATH"}
        env["DEPLOY_LOGS_DIR"] = str(self.repo.root / "logs")

        result = subprocess.run([str(self.repo.installed)], capture_output=True, text=True, env=env)

        self.assertEqual(result.returncode, 0)
        self.assertIn("REPO_PATH", result.stderr + result.stdout)
        self.assertEqual(self.repo.head(), self.repo._git("rev-parse", "origin/main").stdout.strip())

    def test_deploy_whenCheckoutIsAheadOfRemote_shouldStayQuietAndChangeNothing(self):
        """Being ahead also differs from the remote, and `merge --ff-only` accepts
        it as a successful no-op. Without an ancestry test that logged a DEPLOYED
        line every minute while moving nothing."""
        self.repo._git("config", "user.email", "t@example.invalid")
        (self.repo.checkout / "local.txt").write_text("ahead\n")
        self.repo._git("add", "-A")
        self.repo._git("commit", "--quiet", "-m", "local-only")
        local_head = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), local_head)
        self.assertNotIn("DEPLOYED", self.repo.logs(),
                         "a no-op merge must not be reported as a deploy")

    def test_deploy_whenCheckoutAndRemoteHaveDiverged_shouldRefuseAndRewriteNothing(self):
        """`--ff-only` is the guard that makes this safe: a plain merge here would
        create a merge commit and rewrite the shape of the runner's main branch."""
        self.repo._git("config", "user.email", "t@example.invalid")
        (self.repo.checkout / "local.txt").write_text("ahead\n")
        self.repo._git("add", "-A")
        self.repo._git("commit", "--quiet", "-m", "local-only")
        self.repo.push_upstream_commit("two\n", "c2")
        local_head = self.repo.head()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), local_head, "a diverged checkout must never be rewritten")
        self.assertIn("not a fast-forward", self.repo.logs())
        self.assertTrue((self.repo.checkout / "local.txt").exists())
        self.assertFalse((self.repo.checkout / ".git" / "MERGE_HEAD").exists(),
                         "no merge may be left in progress")

    def test_deploy_whenLockIsHeldByAnotherRun_shouldSkipWithoutTouchingTheCheckout(self):
        self.repo.push_upstream_commit("two\n", "c2")
        before = self.repo.head()
        lock_path = self.repo.root / "deploy.lock"
        holder = subprocess.Popen(
            ["flock", "-x", str(lock_path), "sleep", "5"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
        self.addCleanup(holder.wait)
        time.sleep(0.4)  # let flock take it

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)
        self.assertIn("another deploy is still running", self.repo.logs())


if __name__ == "__main__":
    unittest.main()
