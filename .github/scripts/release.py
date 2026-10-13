#!/usr/bin/env python3
"""Durable, SHA-bound release intent. All remote writes target its draft only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

MARKER = "<!-- zipper-release-intent:"
ASSETS = ("zipper-linux-x64", "zipper-win-x64.exe", "zipper-osx-arm64")
REPO = os.environ.get("GITHUB_REPOSITORY", "dwojtaszek/zipper")


def command(*args):
    return subprocess.check_output(args, text=True).strip()


def api(endpoint, method="GET", payload=None):
    args = ["gh", "api", f"repos/{REPO}/{endpoint}", "--method", method]
    if payload is not None:
        args += ["--input", "-"]
    response = subprocess.check_output(args, input=json.dumps(payload) if payload is not None else None, text=True)
    return json.loads(response) if response.strip() else None


def releases():
    pages = json.loads(command("gh", "api", f"repos/{REPO}/releases", "--paginate", "--slurp"))
    return [release for page in pages for release in page]


def intent(release):
    body = release.get("body") or ""
    # Notes (including AI output) are not state. Only our terminal record is.
    match = re.search(re.escape(MARKER) + r"(\{[^\n]*\}) -->\s*\Z", body)
    if not match:
        if MARKER in body:
            raise ValueError("Malformed terminal release intent")
        return None
    state = json.loads(match.group(1))
    if (state.get("schema") != 1 or not re.fullmatch(r"[0-9a-f]{40}", state.get("source_sha", ""))
            or state.get("phase") not in ("reserved", "sealed", "completed")
            or state.get("origin") not in ("main", "tag")
            or not isinstance(state.get("assets"), dict)
            or not isinstance(state.get("build_version"), str)):
        raise ValueError("Invalid release intent")
    version_tuple(state["tag"])
    version_tuple(state["previous_tag"])
    return state


def body(state, notes=""):
    return notes.strip() + "\n\n" + MARKER + json.dumps(state, sort_keys=True, separators=(",", ":")) + " -->"


def update(release, state, notes=None, **fields):
    existing_notes = (release.get("body") or "").rsplit(MARKER, 1)[0] if notes is None else notes
    # A PATCH without tag_name makes the server discard the draft's tag and assign an
    # auto-generated untagged-<hex> name, so always pin tag and source explicitly.
    return api(f"releases/{release['id']}", "PATCH", {
        "tag_name": state["tag"], "target_commitish": state["source_sha"],
        "body": body(state, existing_notes), **fields})


def version_tuple(tag):
    if not re.fullmatch(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", tag):
        raise ValueError(f"Invalid stable release tag: {tag}")
    return tuple(map(int, tag[1:].split(".")))


def output(**values):
    text = "".join(f"{key}={value}\n" for key, value in values.items())
    print(text, end="")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as stream:
            stream.write(text)


def remote_tag(tag):
    lines = command("git", "ls-remote", "--tags", "origin", f"refs/tags/{tag}", f"refs/tags/{tag}^{{}}").splitlines()
    return lines[-1].split()[0] if lines else None


def prepare(sha, ref, recover=False, intent_tag=None):
    if command("git", "rev-parse", "HEAD") != sha:
        raise ValueError("Checkout does not match release source SHA")
    all_releases = releases()
    requested = ref.removeprefix("refs/tags/") if ref.startswith("refs/tags/") else None
    if intent_tag:
        version_tuple(intent_tag)
    if requested:
        version_tuple(requested)
        if remote_tag(requested) != sha:
            raise ValueError("Manual release tag does not identify the source SHA")
    # Select by the terminal intent's tag, never the mutable release tag_name: a draft
    # whose tag drifted to an auto-generated untagged-<hex> name is still recoverable.
    matching = [r for r in all_releases if intent(r) and intent(r)["source_sha"] == sha
                and (intent(r)["tag"] == (intent_tag or requested) if (intent_tag or requested)
                     else (recover or intent(r)["origin"] == "main"))]
    if len(matching) > 1:
        raise ValueError("Ambiguous release intents for source SHA")
    if matching:
        release, state = matching[0], intent(matching[0])
    else:
        if recover or intent_tag:
            raise ValueError("No matching existing release intent to recover")
        # Do not allocate a newer version to a stale main retry.
        if not requested and command("git", "ls-remote", "origin", "refs/heads/main").split()[0] != sha:
            output(skip="true")
            return
        tags = [t for t in command("git", "tag", "-l", "v*").splitlines()
                if re.fullmatch(r"v\d+\.\d+\.\d+", t)]
        latest_tag = max(tags, key=version_tuple) if tags else "v0.0.0"
        dev_major, dev_minor, dev_patch = version_tuple(latest_tag)
        tags += [t for r in all_releases for t in [intent(r)["tag"] if intent(r) else r["tag_name"]]
                 if re.fullmatch(r"v\d+\.\d+\.\d+", t)]
        previous = max(tags, key=version_tuple) if tags else "v0.0.0"
        major, minor, patch = version_tuple(previous)
        tag = requested or f"v{major}.{minor}.{patch + 1}"
        if any(r["tag_name"] == tag or (intent(r) and intent(r)["tag"] == tag) for r in all_releases):
            raise ValueError("Tag already has a release without matching intent")
        state = {"schema": 1, "source_sha": sha, "tag": tag, "previous_tag": previous,
                 "origin": "tag" if requested else "main", "phase": "reserved", "assets": {},
                 "build_version": requested[1:] if requested else f"{dev_major}.{dev_minor}.{dev_patch + 1}-dev"}
        release = api("releases", "POST", {
            "tag_name": tag, "target_commitish": sha, "name": f"Release {tag}",
            "draft": True, "prerelease": False, "body": body(state),
        })
    Path("release-intent.json").write_text(json.dumps({"release_id": release["id"], **state}, indent=2) + "\n")
    stable = state["tag"][1:]
    output(skip=str(state["phase"] == "completed").lower(), version=state["build_version"],
           release_version=stable, tag=state["tag"], prev_tag=state["previous_tag"],
           release_id=release["id"], artifact_run=state.get("artifact_run", ""),
           artifact_ids=",".join(str(state["artifact_ids"][name]) for name in ASSETS
                                 if name in state.get("artifact_ids", {})))


def validated_assets(directory, state):
    assets = {}
    for name in ASSETS:
        paths = [p for p in directory.rglob(name) if p.is_file()]
        if len(paths) != 1:
            raise ValueError(f"Missing or ambiguous asset {name}")
        path = paths[0]
        record_path = path.parent / "acceptance.json"
        record = json.loads(record_path.read_text())
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        version = state["tag"][1:]
        expected_metadata = {"assemblyVersion": version + ".0", "fileVersion": version + ".0",
                             "informationalVersion": version, "sourceRevision": state["source_sha"]}
        if (record["file"] != name or record["sha256"] != digest
                or record["version"] != state["tag"][1:] or record["source_sha"] != state["source_sha"]
                or record.get("metadata") != expected_metadata
                or record["checks"] != ["version", "invalid-invocation", "Standard", "Loadfile-Only", "Production Set"]):
            raise ValueError(f"Asset did not pass exact-binary acceptance: {name}")
        assets[name] = {"sha256": digest, "size": path.stat().st_size, "acceptance": record}
    return assets


def publish(release_id, directory, run_id, notes):
    release = api(f"releases/{release_id}")
    state = intent(release)
    if not state:
        raise ValueError("Release intent missing or inconsistent")
    # Tolerate only the server's auto-generated untagged-<hex> drift on a sealed draft
    # still bound to the intent's source; any other tag drift fails closed.
    drifted = release["tag_name"] != state["tag"]
    if drifted and not (release["draft"] and state["phase"] == "sealed"
                        and re.fullmatch(r"untagged-[0-9a-f]+", release["tag_name"])
                        and release.get("target_commitish") == state["source_sha"]):
        raise ValueError("Release tag drifted from its intent")
    if state["phase"] == "completed":
        print("Release already completed; no-op")
        return
    assets = validated_assets(directory, state)
    if state["assets"] and state["assets"] != assets:
        raise ValueError("Retry artifacts differ from sealed release intent; download the original artifact run")
    if not state["assets"]:
        artifact_ids = {}
        for name in ASSETS:
            platform = name.removeprefix("zipper-").removesuffix(".exe")
            receipts = list(directory.rglob(f"artifact-identity-{platform}.json"))
            if len(receipts) != 1:
                raise ValueError(f"Missing immutable artifact identity for {name}")
            receipt = json.loads(receipts[0].read_text())
            identity = receipt.get("artifact_id")
            if (not isinstance(identity, int) or identity <= 0 or receipt.get("source_sha") != state["source_sha"]
                    or str(receipt.get("run_id")) != str(run_id) or receipt.get("name") != f"zipper-{platform}"):
                raise ValueError(f"Invalid immutable artifact identity for {name}")
            artifact_ids[name] = identity
        if len(set(artifact_ids.values())) != len(ASSETS):
            raise ValueError("Artifact identities are not unique")
        state.update(assets=assets, artifact_run=run_id, artifact_ids=artifact_ids, phase="sealed")
        release = update(release, state, notes)
    tag_sha = remote_tag(state["tag"])
    if tag_sha and tag_sha != state["source_sha"]:
        raise ValueError("Release tag points at a different SHA")
    if not tag_sha:
        # Lightweight tag: no author identity changes, no replacement of existing refs.
        local = subprocess.run(["git", "rev-parse", "--verify", f"{state['tag']}^{{}}"], capture_output=True, text=True)
        if local.returncode == 0 and local.stdout.strip() != state["source_sha"]:
            raise ValueError("Local tag points at a different SHA")
        if local.returncode != 0:
            command("git", "tag", state["tag"], state["source_sha"])
        command("git", "push", "origin", f"refs/tags/{state['tag']}")
    if remote_tag(state["tag"]) != state["source_sha"]:
        raise ValueError("Cannot verify published tag identity")
    if drifted:
        # Restore the drifted draft's tag and source (body and intent stay byte-identical)
        # before any upload addresses the release by tag.
        update(release, state)
        release = api(f"releases/{release_id}")
        if release["tag_name"] != state["tag"] or release.get("target_commitish") != state["source_sha"]:
            raise ValueError("Could not restore drifted release tag")
    # Never overwrite a uploaded asset, even on a partial retry.
    for name in ASSETS:
        release = api(f"releases/{release_id}")
        existing = [asset for asset in release.get("assets", []) if asset["name"] == name]
        if len(existing) > 1:
            raise ValueError(f"Duplicate remote asset {name}")
        if existing and existing[0].get("state") == "starter" and existing[0].get("size") == 0:
            if not release["draft"]:
                raise ValueError("Cannot recover an incomplete asset on a public release")
            # A documented failed-upload placeholder has no accepted content.
            # Delete only that exact empty placeholder, never an uploaded asset.
            api(f"releases/assets/{existing[0]['id']}", "DELETE")
            existing = []
        if not existing:
            path = next(p for p in directory.rglob(name) if p.is_file())
            command("gh", "release", "upload", state["tag"], str(path), "--repo", REPO)
        with tempfile.TemporaryDirectory() as temporary:
            command("gh", "release", "download", state["tag"], "--pattern", name, "--dir", temporary, "--repo", REPO)
            downloaded = Path(temporary) / name
            if (hashlib.sha256(downloaded.read_bytes()).hexdigest() != assets[name]["sha256"]
                    or downloaded.stat().st_size != assets[name]["size"]):
                raise ValueError(f"Remote asset identity mismatch: {name}")
    release = api(f"releases/{release_id}")
    if {a["name"] for a in release.get("assets", [])} != set(ASSETS):
        raise ValueError("Release does not have exactly the expected asset set")
    # Older recovery runs may complete their own release, but never become newest.
    current_main = command("git", "ls-remote", "origin", "refs/heads/main").split()[0]
    newer = any(not r["draft"] and version_tuple(r["tag_name"]) > version_tuple(state["tag"])
                for r in releases() if re.fullmatch(r"v\d+\.\d+\.\d+", r["tag_name"]))
    latest = not newer and (state["origin"] == "tag" or current_main == state["source_sha"])
    state["phase"] = "completed"
    update(release, state, draft=False, make_latest="true" if latest else "false")
    print(f"Published {state['tag']} from {state['source_sha']}, newest={latest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("prepare", "publish"))
    parser.add_argument("--sha", default=os.environ.get("GITHUB_SHA", ""))
    parser.add_argument("--ref", default=os.environ.get("GITHUB_REF", ""))
    parser.add_argument("--recover", action="store_true")
    parser.add_argument("--intent-tag")
    parser.add_argument("--release-id", type=int)
    parser.add_argument("--artifacts", type=Path, default=Path("artifacts"))
    parser.add_argument("--run-id", default=os.environ.get("GITHUB_RUN_ID", "local"))
    parser.add_argument("--notes", type=Path)
    args = parser.parse_args()
    if args.action == "prepare":
        prepare(args.sha, args.ref, args.recover, args.intent_tag)
    else:
        publish(args.release_id, args.artifacts, args.run_id, args.notes.read_text() if args.notes else "")
