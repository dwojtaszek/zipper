#!/usr/bin/env python3
"""Offline GitHub release boundary for the real release controller."""
import json
import os
from pathlib import Path
import shutil
import sys

root = Path(os.environ["RELEASE_FIXTURE"])
store = root / "github.json"
releases = json.loads(store.read_text())
args = sys.argv[1:]
with (root / "requests.log").open("a") as stream:
    stream.write(json.dumps(args) + "\n")
if args[0] == "api":
    endpoint = args[1].split("/zipper/")[1]
    method = args[args.index("--method") + 1] if "--method" in args else "GET"
    payload = json.load(sys.stdin) if "--input" in args else {}
    if endpoint == "releases":
        if method == "POST":
            release = {"id": len(releases) + 1, "assets": [], **payload}
            releases.append(release)
            result = release
        else:
            result = [releases]
    elif endpoint.startswith("releases/assets/") and method == "DELETE":
        identity = int(endpoint.rsplit("/", 1)[1])
        for release in releases:
            release["assets"] = [a for a in release["assets"] if a["id"] != identity]
        result = {}
    else:
        release = next(r for r in releases if r["id"] == int(endpoint.split("/")[1]))
        if method == "PATCH":
            release.update(payload)
        result = release
elif args[:2] == ["release", "upload"]:
    release = next(r for r in releases if r["tag_name"] == args[2])
    if len(release["assets"]) + 1 == int(os.environ.get("FAIL_UPLOAD_AT", "0")):
        sys.exit(1)
    path = Path(args[3])
    dest = root / "remote-assets" / str(release["id"])
    dest.mkdir(parents=True, exist_ok=True)
    if len(release["assets"]) + 1 == int(os.environ.get("STARTER_UPLOAD_AT", "0")):
        (dest / path.name).write_bytes(b"")
        release["assets"].append({"name": path.name, "id": release["id"] * 10 + len(release["assets"]),
                                  "size": 0, "state": "starter"})
        store.write_text(json.dumps(releases))
        sys.exit(1)
    shutil.copyfile(path, dest / path.name)
    release["assets"].append({"name": path.name, "id": release["id"] * 10 + len(release["assets"]),
                              "size": path.stat().st_size, "state": "uploaded"})
    result = {}
elif args[:2] == ["release", "download"]:
    release = next(r for r in releases if r["tag_name"] == args[2])
    name = args[args.index("--pattern") + 1]
    dest = Path(args[args.index("--dir") + 1]) / name
    shutil.copyfile(root / "remote-assets" / str(release["id"]) / name, dest)
    result = {}
else:
    raise AssertionError(args)
store.write_text(json.dumps(releases))
print(json.dumps(result))
