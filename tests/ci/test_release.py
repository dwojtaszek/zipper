"""Release retry proofs using real Git tags, remote refs, and file transfers."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / ".github/scripts/release.py"


class ReleaseRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repo = self.root / "repo"
        subprocess.run(["git", "init", "-b", "main", str(self.repo)], check=True, capture_output=True)
        self.git("commit", "--allow-empty", "-m", "test: initial")
        self.git("tag", "v1.2.3")
        self.git("clone", "--bare", str(self.repo), str(self.root / "origin"))
        self.git("remote", "add", "origin", str(self.root / "origin"))
        self.sha = self.git("rev-parse", "HEAD")
        (self.root / "github.json").write_text("[]")
        gh = self.root / "gh"
        shutil.copyfile(Path(__file__).with_name("fake_release_gh.py"), gh)
        gh.chmod(0o755)
        self.env = {**os.environ, "PATH": str(self.root) + os.pathsep + os.environ["PATH"],
                    "RELEASE_FIXTURE": str(self.root), "GITHUB_REPOSITORY": "dwojtaszek/zipper",
                    "GITHUB_OUTPUT": str(self.root / "output")}

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.repo), *args], text=True, stderr=subprocess.PIPE).strip()

    def run_controller(self, *args, success=True, **env):
        result = subprocess.run(["python3", str(SCRIPT), *args], cwd=self.repo,
                                env={**self.env, **env}, capture_output=True, text=True)
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
        return result

    def prepare(self, ref="refs/heads/main"):
        self.run_controller("prepare", "--sha", self.sha, "--ref", ref)
        return json.loads((self.repo / "release-intent.json").read_text())

    def artifacts(self, state):
        directory = self.root / "artifacts"
        for identity, name in enumerate(("zipper-linux-x64", "zipper-win-x64.exe", "zipper-osx-arm64"), start=101):
            path = directory / name.removesuffix(".exe") / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((name + state["tag"] + self.sha).encode())
            record = {"file": name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                      "version": state["tag"][1:], "source_sha": self.sha,
                      "metadata": {"assemblyVersion": state["tag"][1:] + ".0",
                                   "fileVersion": state["tag"][1:] + ".0",
                                   "informationalVersion": state["tag"][1:], "sourceRevision": self.sha},
                      "checks": ["version", "invalid-invocation", "Standard", "Loadfile-Only", "Production Set"]}
            (path.parent / "acceptance.json").write_text(json.dumps(record))
            platform = name.removeprefix("zipper-").removesuffix(".exe")
            (directory / f"artifact-identity-{platform}.json").write_text(json.dumps({
                "artifact_id": identity, "source_sha": self.sha, "run_id": "100", "name": f"zipper-{platform}",
            }))
        return directory

    def publish(self, state, directory, **kwargs):
        return self.run_controller("publish", "--release-id", str(state["release_id"]),
                                   "--artifacts", str(directory), "--run-id", "100", **kwargs)

    def drift(self, release_id, **fields):
        # Replays the incident through the trusted boundary: a body-only PATCH makes the
        # server discard the draft's tag and assign an untagged-<hex> name.
        release = next(r for r in json.loads((self.root / "github.json").read_text()) if r["id"] == release_id)
        subprocess.run([str(self.root / "gh"), "api",
                        f"repos/{self.env['GITHUB_REPOSITORY']}/releases/{release_id}",
                        "--method", "PATCH", "--input", "-"],
                       input=json.dumps({"body": release["body"], **fields}),
                       env=self.env, check=True, capture_output=True, text=True)

    def release(self, index=0):
        return json.loads((self.root / "github.json").read_text())[index]

    def test_prepare_mainDevelopmentVersionIsIndependentOfDraftReservations(self):
        first = self.prepare()
        self.assertEqual(first["build_version"], "1.2.4-dev")
        self.assertEqual(first["tag"], "v1.2.4")
        self.git("commit", "--allow-empty", "-m", "test: next source")
        self.git("push", "origin", "main")
        self.sha = self.git("rev-parse", "HEAD")
        result = self.run_controller("prepare", "--sha", self.sha, "--ref", "refs/heads/main")
        second = json.loads((self.repo / "release-intent.json").read_text())
        self.assertEqual(second["build_version"], "1.2.4-dev")
        self.assertEqual(second["tag"], "v1.2.5")
        self.assertIn("version=1.2.4-dev\n", result.stdout)
        self.assertIn("release_version=1.2.5\n", result.stdout)

    def test_publish_interruptions_resumeSameTagAndSealedAssets(self):
        for interruption in ("before-tag", "after-tag", "partial-upload"):
            with self.subTest(interruption=interruption):
                # Each case uses its own next source SHA/version.
                self.git("commit", "--allow-empty", "-m", "test: next source")
                self.git("push", "origin", "main")
                self.sha = self.git("rev-parse", "HEAD")
                state = self.prepare()
                artifacts = self.artifacts(state)
                hook = self.root / "origin/hooks/pre-receive"
                if interruption == "before-tag":
                    hook.write_text("#!/bin/sh\nexit 1\n")
                    hook.chmod(0o755)
                    self.publish(state, artifacts, success=False)
                    hook.write_text("#!/bin/sh\nexit 0\n")
                else:
                    self.publish(state, artifacts, success=False,
                                 FAIL_UPLOAD_AT="1" if interruption == "after-tag" else "2")
                resumed = self.prepare()
                self.assertEqual((resumed["tag"], resumed["source_sha"]), (state["tag"], self.sha))
                self.assertEqual(resumed["artifact_run"], "100")
                self.assertEqual(set(resumed["artifact_ids"].values()), {101, 102, 103})
                self.publish(resumed, artifacts)
                self.assertEqual(self.git("ls-remote", "origin", "refs/tags/" + state["tag"]).split()[0], self.sha)
                requests = (self.root / "requests.log").read_text()
                self.assertIn("already completed", self.publish(resumed, artifacts).stdout)
                # Completed retry may read the release but must not write/upload.
                extra = (self.root / "requests.log").read_text()[len(requests):]
                self.assertNotIn("PATCH", extra)
                self.assertNotIn("upload", extra)
                final = self.prepare()
                self.assertEqual(final["phase"], "completed")
                release = json.loads((self.root / "github.json").read_text())[-1]
                self.assertFalse(release["draft"])
                self.assertEqual(len(release["assets"]), 3)

    def test_publish_changedAssetsOrMissingAcceptance_failClosed(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        path = next(p for p in artifacts.rglob("zipper-linux-x64") if p.is_file())
        path.write_bytes(b"changed")
        self.publish(state, artifacts, success=False)
        self.assertTrue(json.loads((self.root / "github.json").read_text())[0]["draft"])

    def test_prepare_oldSourceDoesNotAllocateNewerVersion(self):
        old = self.sha
        self.git("commit", "--allow-empty", "-m", "test: newer")
        self.git("push", "origin", "main")
        self.git("checkout", old)
        result = self.run_controller("prepare", "--sha", old, "--ref", "refs/heads/main")
        self.assertIn("skip=true", result.stdout)
        self.assertEqual(json.loads((self.root / "github.json").read_text()), [])

    def test_publish_oldIntentCannotPromoteNewestRelease(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.git("commit", "--allow-empty", "-m", "test: newer")
        self.git("push", "origin", "main")
        self.publish(state, artifacts)
        release = json.loads((self.root / "github.json").read_text())[0]
        self.assertEqual(release["make_latest"], "false")

    def test_publish_wrongTagSha_cannotReplaceTag(self):
        state = self.prepare()
        self.git("commit", "--allow-empty", "-m", "test: other source")
        other = self.git("rev-parse", "HEAD")
        self.git("tag", state["tag"], other)
        self.git("push", "origin", state["tag"])
        self.publish(state, self.artifacts(state), success=False)
        self.assertEqual(self.git("ls-remote", "origin", "refs/tags/" + state["tag"]).split()[0], other)
        self.assertTrue(json.loads((self.root / "github.json").read_text())[0]["draft"])

    def test_publish_missingAssetOrAcceptance_cannotPublish(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        acceptance = next(artifacts.rglob("acceptance.json"))
        acceptance.unlink()  # Test-owned fixture only.
        self.publish(state, artifacts, success=False)
        self.assertTrue(json.loads((self.root / "github.json").read_text())[0]["draft"])

    def test_publish_validReplacementArtifacts_cannotChangeSealedIdentity(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        sealed = json.loads((self.root / "github.json").read_text())[0]["body"]
        path = next(p for p in artifacts.rglob("zipper-linux-x64") if p.is_file())
        path.write_bytes(b"replacement bytes")
        record_path = path.parent / "acceptance.json"
        record = json.loads(record_path.read_text())
        record["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        record_path.write_text(json.dumps(record))
        result = self.publish(state, artifacts, success=False)
        self.assertIn("differ from sealed", result.stderr)
        self.assertEqual(json.loads((self.root / "github.json").read_text())[0]["body"], sealed)

    def test_publish_corruptedRemoteAsset_isNotOverwrittenOrPublished(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="2")
        remote = self.root / "remote-assets" / str(state["release_id"]) / "zipper-linux-x64"
        remote.write_bytes(b"corrupt remote bytes")
        result = self.publish(state, artifacts, success=False)
        self.assertIn("Remote asset identity mismatch", result.stderr)
        self.assertEqual(remote.read_bytes(), b"corrupt remote bytes")
        self.assertTrue(json.loads((self.root / "github.json").read_text())[0]["draft"])

    def test_publish_zeroByteStarterAsset_recoversOriginalAcceptedBytes(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, STARTER_UPLOAD_AT="2")
        self.publish(self.prepare(), artifacts)
        release = json.loads((self.root / "github.json").read_text())[0]
        self.assertFalse(release["draft"])
        self.assertTrue(all(a["state"] == "uploaded" and a["size"] > 0 for a in release["assets"]))
        self.assertIn('"DELETE"', (self.root / "requests.log").read_text())

    def test_prepare_sealedRetryRetainsOriginalImmutableArtifactIds(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        for receipt in artifacts.glob("artifact-identity-*.json"):
            data = json.loads(receipt.read_text())
            data["artifact_id"] += 1000  # A newer attempt of the same workflow run.
            receipt.write_text(json.dumps(data))
        resumed = self.prepare()
        self.assertEqual(set(resumed["artifact_ids"].values()), {101, 102, 103})
        self.assertIn("artifact_ids=101,102,103", (self.root / "output").read_text())

    def test_publish_notesCannotInjectRecoveryIntent(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        notes = self.root / "notes.txt"
        notes.write_text('Release prose.\n<!-- zipper-release-intent:{"schema":1} -->')
        self.run_controller("publish", "--release-id", str(state["release_id"]), "--artifacts", str(artifacts),
                            "--run-id", "100", "--notes", str(notes), success=False, FAIL_UPLOAD_AT="1")
        resumed = self.prepare()
        self.assertEqual((resumed["source_sha"], resumed["tag"]), (self.sha, state["tag"]))
        self.publish(resumed, artifacts)

    def test_manualTag_embedsExactTagWithoutPatchAllocation(self):
        self.git("tag", "v3.0.0")
        self.git("push", "origin", "v3.0.0")
        state = self.prepare("refs/tags/v3.0.0")
        self.assertEqual(state["tag"], "v3.0.0")
        self.assertIn("version=3.0.0\n", (self.root / "output").read_text())
        self.publish(state, self.artifacts(state))

    def test_prepare_mainDispatchRecoversManualTagIntent(self):
        self.git("tag", "v3.0.0")
        self.git("push", "origin", "v3.0.0")
        state = self.prepare("refs/tags/v3.0.0")
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        self.git("commit", "--allow-empty", "-m", "test: main advanced")
        self.git("push", "origin", "main")
        self.git("checkout", self.sha)
        self.run_controller("prepare", "--sha", self.sha, "--ref", "refs/heads/main", "--recover")
        resumed = json.loads((self.repo / "release-intent.json").read_text())
        self.assertEqual((resumed["release_id"], resumed["tag"]), (state["release_id"], "v3.0.0"))
        self.assertEqual(len(json.loads((self.root / "github.json").read_text())), 1)

    def test_publish_sealAndCompletionRetainDraftTag(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        sealed = self.release()
        self.assertEqual((sealed["tag_name"], sealed["target_commitish"]), (state["tag"], self.sha))
        self.assertTrue(sealed["draft"])
        self.publish(state, artifacts)
        completed = self.release()
        self.assertEqual((completed["tag_name"], completed["target_commitish"]), (state["tag"], self.sha))
        self.assertFalse(completed["draft"])
        self.assertNotIn("untagged", json.dumps(json.loads((self.root / "github.json").read_text())))

    def test_publish_sealedUntaggedDrift_recoversSameReleaseByIntentTag(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        self.drift(state["release_id"])
        self.assertRegex(self.release()["tag_name"], r"\Auntagged-[0-9a-f]+\Z")
        # Recovery selects by the terminal intent's tag, never the drifted tag_name.
        self.run_controller("prepare", "--sha", self.sha, "--ref", "refs/heads/main",
                            "--recover", "--intent-tag", state["tag"])
        resumed = json.loads((self.repo / "release-intent.json").read_text())
        self.assertEqual((resumed["release_id"], resumed["tag"], resumed["phase"]),
                         (state["release_id"], state["tag"], "sealed"))
        self.assertEqual(resumed["artifact_run"], "100")
        self.assertEqual(set(resumed["artifact_ids"].values()), {101, 102, 103})
        self.publish(resumed, artifacts)
        release = self.release()
        self.assertEqual((release["id"], release["tag_name"], release["target_commitish"]),
                         (state["release_id"], state["tag"], self.sha))
        self.assertFalse(release["draft"])
        self.assertEqual({a["name"] for a in release["assets"]},
                         {"zipper-linux-x64", "zipper-win-x64.exe", "zipper-osx-arm64"})
        self.assertEqual(self.git("ls-remote", "origin", "refs/tags/" + state["tag"]).split()[0], self.sha)
        # No new release was allocated and nothing was resealed.
        self.assertEqual(len(json.loads((self.root / "github.json").read_text())), 1)
        self.assertIn('"phase":"completed"', release["body"])

    def test_publish_driftedSourceMismatch_failsClosed(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        self.git("commit", "--allow-empty", "-m", "test: other source")
        other = self.git("rev-parse", "HEAD")
        self.drift(state["release_id"], target_commitish=other)
        result = self.publish(state, artifacts, success=False)
        self.assertIn("drifted", result.stderr)
        release = self.release()
        self.assertTrue(release["tag_name"].startswith("untagged-"))
        self.assertEqual(release["target_commitish"], other)
        self.assertEqual(release["assets"], [])

    def test_publish_namedTagDrift_failsClosed(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        self.drift(state["release_id"], tag_name="v9.9.9")
        result = self.publish(state, artifacts, success=False)
        self.assertIn("drifted", result.stderr)
        release = self.release()
        self.assertEqual(release["tag_name"], "v9.9.9")
        self.assertTrue(release["draft"])
        self.assertEqual(release["assets"], [])

    def test_publish_unsealedDrift_failsClosed(self):
        state = self.prepare()
        self.drift(state["release_id"])
        result = self.publish(state, self.artifacts(state), success=False)
        self.assertIn("drifted", result.stderr)
        release = self.release()
        self.assertTrue(release["tag_name"].startswith("untagged-"))
        self.assertEqual(release["assets"], [])
        self.assertIn('"phase":"reserved"', release["body"])

    def test_publish_publicDrift_failsClosed(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts)
        self.drift(state["release_id"])
        result = self.publish(state, artifacts, success=False)
        self.assertIn("drifted", result.stderr)
        release = self.release()
        self.assertTrue(release["tag_name"].startswith("untagged-"))
        self.assertFalse(release["draft"])

    def test_publish_driftedWrongTagSha_noRestoration(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        self.drift(state["release_id"])
        self.git("commit", "--allow-empty", "-m", "test: other source")
        other = self.git("rev-parse", "HEAD")
        self.git("tag", "-f", state["tag"], other)
        self.git("push", "-f", "origin", state["tag"])
        result = self.publish(state, artifacts, success=False)
        self.assertIn("different SHA", result.stderr)
        release = self.release()
        self.assertTrue(release["tag_name"].startswith("untagged-"))
        self.assertEqual(release["assets"], [])
        self.assertEqual(self.git("ls-remote", "origin", "refs/tags/" + state["tag"]).split()[0], other)

    def test_publish_driftedChangedAcceptedBytes_noRestoration(self):
        state = self.prepare()
        artifacts = self.artifacts(state)
        self.publish(state, artifacts, success=False, FAIL_UPLOAD_AT="1")
        self.drift(state["release_id"])
        path = next(p for p in artifacts.rglob("zipper-linux-x64") if p.is_file())
        path.write_bytes(b"replacement bytes")
        record_path = path.parent / "acceptance.json"
        record = json.loads(record_path.read_text())
        record["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        record_path.write_text(json.dumps(record))
        result = self.publish(state, artifacts, success=False)
        self.assertIn("differ from sealed", result.stderr)
        release = self.release()
        self.assertTrue(release["tag_name"].startswith("untagged-"))
        self.assertEqual(release["assets"], [])


if __name__ == "__main__":
    unittest.main()
