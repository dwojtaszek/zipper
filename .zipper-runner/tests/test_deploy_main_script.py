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
        self.lock_path = self.root / "repo-mutation.lock"
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

    def hold_lock(self, age_seconds: int = 0, with_created: bool = True) -> None:
        """Take the shared main-checkout lock, as the runner or a peer deploy does.

        `with_created=False` models a holder that died between `mkdir` and writing
        its timestamp, which is the only case where the age has to come from the
        directory mtime.
        """
        self.lock_path.mkdir(exist_ok=True)
        if with_created:
            (self.lock_path / "created").write_text(str(int(time.time()) - age_seconds))
        (self.lock_path / "owner").write_text("someone-else")
        stamp = time.time() - age_seconds
        os.utime(self.lock_path, (stamp, stamp))

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
        env["REPO_MUTATION_LOCK_PATH"] = str(self.lock_path)
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

    def test_deploy_whenLockIsHeldByAnotherRun_shouldSkipWithoutTouchingTheCheckout(self):
        self.repo.push_upstream_commit("two\n", "c2")
        before = self.repo.head()
        self.repo.hold_lock()

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before)
        self.assertIn("locked by the runner or another deploy", self.repo.logs())
        self.assertTrue(self.repo.lock_path.exists(),
                        "a skipped deploy must leave the other holder's lock alone")

    def test_deploy_whenLockIsHeldByAStaleHolder_shouldReclaimItAndDeploy(self):
        """A crashed deploy must not wedge every later tick. The bound is longer
        than the runner's slowest mutation, so a live holder is never stolen."""
        new_upstream = self.repo.push_upstream_commit("two\n", "c2")
        self.repo.hold_lock(age_seconds=3600)

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), new_upstream, "a stale lock must not block the deploy")
        self.assertIn("stealing a stale main-checkout lock", self.repo.logs())

    def test_deploy_whenStaleHolderWroteNoTimestamp_shouldStillReclaimIt(self):
        """The runner creates the lock with mkdir; a crash before it records its
        timestamp would otherwise leave a lock the shell can never judge stale,
        wedging every later deploy while the runner is down."""
        new_upstream = self.repo.push_upstream_commit("two\n", "c2")
        self.repo.hold_lock(age_seconds=3600, with_created=False)

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), new_upstream, "the mtime fallback must reclaim it")
        self.assertIn("stealing a stale main-checkout lock", self.repo.logs())

    def test_deploy_whenTheLockIsStolenMidRun_shouldNotDeleteTheNewOwnersLock(self):
        """A steal can only happen after the stale bound, but the release must still
        verify ownership: removing a lock another process now holds would let a third
        process mutate the checkout alongside it."""
        self.repo.push_upstream_commit("two\n", "c2")

        # A git that simulates a concurrent steal on its first invocation, which is
        # after this deploy acquired the lock and before it releases it.
        fake_bin = self.repo.root / "fakebin-steal"
        fake_bin.mkdir(exist_ok=True)
        real_git = shutil.which("git")
        (fake_bin / "git").write_text(
            "#!/bin/bash\n"
            f'if [ -d "{self.repo.lock_path}" ] && [ ! -e "{self.repo.root}/stolen" ]; then\n'
            f'  : > "{self.repo.root}/stolen"\n'
            f'  echo "a-different-holder" > "{self.repo.lock_path}/owner"\n'
            "fi\n"
            f'exec "{real_git}" "$@"\n'
        )
        (fake_bin / "git").chmod(0o755)

        env = dict(os.environ)
        env["REPO_PATH"] = str(self.repo.checkout)
        env["DEPLOY_LOGS_DIR"] = str(self.repo.root / "logs")
        env["REPO_MUTATION_LOCK_PATH"] = str(self.repo.lock_path)
        env["PATH"] = f"{fake_bin}:{env['PATH']}"

        result = subprocess.run([str(self.repo.installed)], capture_output=True, text=True, env=env)

        self.assertEqual(result.returncode, 0)
        self.assertTrue(self.repo.lock_path.exists(),
                        "a lock reclaimed by another process must not be deleted")
        self.assertIn("reclaimed by another process", self.repo.logs())

    def test_deploy_whenOnlyBsdStatIsAvailable_shouldStillJudgeTheLockStale(self):
        """macOS ships BSD `stat`, which rejects `-c`. The mtime fallback must work
        there too, or a lock with no `created` file wedges every macOS deploy."""
        new_upstream = self.repo.push_upstream_commit("two\n", "c2")
        self.repo.hold_lock(age_seconds=3600, with_created=False)

        # A `stat` that rejects GNU `-c` and answers BSD `-f %m`, as macOS does.
        fake_bin = self.repo.root / "fakebin-stat"
        fake_bin.mkdir(exist_ok=True)
        (fake_bin / "stat").write_text(
            "#!/bin/bash\n"
            'if [ "$1" = "-f" ] && [ "$2" = "%m" ]; then\n'
            '  exec /usr/bin/stat -c %Y "$3"\n'
            "fi\n"
            "exit 1\n"
        )
        (fake_bin / "stat").chmod(0o755)

        env = dict(os.environ)
        env["REPO_PATH"] = str(self.repo.checkout)
        env["DEPLOY_LOGS_DIR"] = str(self.repo.root / "logs")
        env["REPO_MUTATION_LOCK_PATH"] = str(self.repo.lock_path)
        env["PATH"] = f"{fake_bin}:{env['PATH']}"

        result = subprocess.run([str(self.repo.installed)], capture_output=True, text=True, env=env)

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), new_upstream, "BSD stat must not break reclamation")
        self.assertIn("stealing a stale main-checkout lock", self.repo.logs())

    def _pathWithoutTimeoutBinaries(self, extra_bin: Path) -> str:
        """A PATH holding what the script needs but no `timeout`/`gtimeout`, as macOS.

        Linux has `/usr/bin/timeout`, so a PATH that merely prepends a fake bin
        directory still finds it and never exercises the fallback. This symlinks an
        explicit allow-list instead, so absence is real rather than assumed.
        """
        sandbox = self.repo.root / "sandbox-bin"
        sandbox.mkdir(exist_ok=True)
        for name in ("dirname", "pwd", "mkdir", "date", "cat", "stat", "rm", "sleep", "mktemp", "bash"):
            source = shutil.which(name)
            if source and not (sandbox / name).exists():
                (sandbox / name).symlink_to(source)
        for absent in ("timeout", "gtimeout"):
            self.assertIsNone(shutil.which(absent, path=f"{extra_bin}:{sandbox}"),
                              f"{absent} must not be reachable for this test to mean anything")
        return f"{extra_bin}:{sandbox}"

    def test_deploy_whenTimeoutBinaryIsMissing_shouldBoundTheFetchWithTheWatchdog(self):
        """macOS ships neither `timeout` nor `gtimeout`, so without the watchdog a
        non-HTTP remote would be unbounded while holding the lock. This is also why
        the watchdog is used instead of `perl -e 'alarm shift; exec @ARGV'`, which
        silently fails to bound a fetch once the exec'd program is a shell script."""
        self.repo.push_upstream_commit("two\n", "c2")
        before = self.repo.head()

        fake_bin = self.repo.root / "fakebin-notimeout"
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
        env["REPO_MUTATION_LOCK_PATH"] = str(self.repo.lock_path)
        env["DEPLOY_FETCH_TIMEOUT"] = "2"
        env["PATH"] = self._pathWithoutTimeoutBinaries(fake_bin)

        started = time.monotonic()
        result = subprocess.run([str(self.repo.installed)], capture_output=True, text=True,
                                env=env, timeout=25)
        elapsed = time.monotonic() - started

        self.assertEqual(result.returncode, 0)
        self.assertLess(elapsed, 20, "the watchdog must kill a hanging fetch, not stall")
        self.assertEqual(self.repo.head(), before)
        self.assertIn("fetch failed or exceeded", self.repo.logs())

    def test_deploy_whenLockIsFreshButTheRunnerIsWorking_shouldNotStealIt(self):
        """The stale bound must exceed the runner's slowest mutation, or a slow
        fetch would be mistaken for a crashed holder and the lock stolen mid-write."""
        self.repo.push_upstream_commit("two\n", "c2")
        before = self.repo.head()
        self.repo.hold_lock(age_seconds=600)

        result = self.repo.run_script()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.repo.head(), before, "a live holder must not be stolen from")
        self.assertIn("locked by the runner or another deploy", self.repo.logs())
        self.assertTrue(self.repo.lock_path.exists(),
                        "a skipped deploy must leave the other holder's lock alone")

    def test_deploy_whenRepoPathIsNotACheckout_shouldNotDeleteAnotherHoldersLock(self):
        """The EXIT trap runs on every exit path, including the ones that give up
        before the lock is even attempted. An unguarded release there would delete
        the runner's lock and hand it to the next tick mid-mutation."""
        self.repo.hold_lock()
        not_a_repo = self.repo.root / "not-a-repo"
        not_a_repo.mkdir()
        env = dict(os.environ)
        env["REPO_PATH"] = str(not_a_repo)
        env["DEPLOY_LOGS_DIR"] = str(self.repo.root / "logs")
        env["REPO_MUTATION_LOCK_PATH"] = str(self.repo.lock_path)

        result = subprocess.run([str(self.repo.installed)], capture_output=True, text=True, env=env)

        self.assertEqual(result.returncode, 0)
        self.assertIn("not a git checkout", self.repo.logs())
        self.assertTrue(self.repo.lock_path.exists(),
                        "an early exit must not delete another holder's lock")

    def test_deploy_whenItFinishes_shouldReleaseTheSharedLock(self):
        """The runner waits for this lock. Leaving it behind would make every later
        run skip its mutations until the stale bound expired."""
        self.repo.push_upstream_commit("two\n", "c2")

        self.repo.run_script()

        self.assertFalse(self.repo.lock_path.exists(), "the lock must be released on exit")

    def test_deploy_whenFetching_shouldAlwaysBoundTheStallInPortableGitConfig(self):
        """`timeout`/`gtimeout` is absent on macOS, where CI runs this suite, so the
        git-level low-speed abort is the bound that always applies."""
        self.repo.push_upstream_commit("two\n", "c2")

        # A git that records its own arguments, fails only the fetch, and is the
        # real git otherwise, so the pre-fetch state checks still behave.
        recorder = self.repo.root / "recorder"
        recorder.mkdir(exist_ok=True)
        calls = recorder / "calls.txt"
        fake_bin = self.repo.root / "fakebin-args"
        fake_bin.mkdir(exist_ok=True)
        real_git = shutil.which("git")
        (fake_bin / "git").write_text(
            "#!/bin/bash\n"
            f'printf "%s\\n" "$*" >> "{calls}"\n'
            "for a in \"$@\"; do [ \"$a\" = fetch ] && exit 1; done\n"
            f'exec "{real_git}" "$@"\n'
        )
        (fake_bin / "git").chmod(0o755)

        env = dict(os.environ)
        env["REPO_PATH"] = str(self.repo.checkout)
        env["DEPLOY_LOGS_DIR"] = str(self.repo.root / "logs")
        env["REPO_MUTATION_LOCK_PATH"] = str(self.repo.lock_path)
        env["DEPLOY_FETCH_TIMEOUT"] = "7"
        env["PATH"] = f"{fake_bin}:{env['PATH']}"

        subprocess.run([str(self.repo.installed)], capture_output=True, text=True, env=env)

        fetch_calls = [line for line in calls.read_text().splitlines() if "fetch" in line]
        self.assertEqual(len(fetch_calls), 1, "the fetch must be the recorded call")
        self.assertIn("http.lowSpeedLimit=1", fetch_calls[0])
        self.assertIn("http.lowSpeedTime=7", fetch_calls[0])

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
        env["REPO_MUTATION_LOCK_PATH"] = str(self.repo.lock_path)
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


if __name__ == "__main__":
    unittest.main()
