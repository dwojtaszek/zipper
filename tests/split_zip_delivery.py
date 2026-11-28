#!/usr/bin/env python3
"""Generate and verify two small Info-ZIP multipart deliveries."""

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

ARCHIVE = "delivery.zip"
MANIFEST = "delivery-manifest.json"
MISSING_ORDINAL = 2
PART_BYTES = 64 * 1024
ZIP_EPOCH = 315532800
MEMBERS = {
    "payload.bin": b"".join(bytes([value]) * PART_BYTES for value in (0x11, 0x22, 0x33)),
    "marker.txt": b"Standard split ZIP\n",
}


def part_filename(ordinal, total):
    return ARCHIVE if ordinal == total else f"delivery.z{ordinal:02d}"


def fingerprint(path):
    with path.open("rb") as stream:
        return {"bytes": path.stat().st_size, "sha256": hashlib.file_digest(stream, "sha256").hexdigest()}


def verify_missing_ordinals(total, missing_ordinals):
    if not total:
        raise ValueError("A delivery must declare its Archive parts")
    if any(type(index) is not int or not 1 <= index <= total for index in missing_ordinals):
        raise ValueError("Missing part ordinals must reference declared parts")
    if len(set(missing_ordinals)) != len(missing_ordinals):
        raise ValueError("Missing part ordinals must be unique")


def verify_parts(directory, parts, missing_ordinals):
    verify_missing_ordinals(len(parts), missing_ordinals)
    for ordinal, part in enumerate(parts, 1):
        index = part.get("ordinal")
        if type(index) is not int or index != ordinal or part["filename"] != part_filename(ordinal, len(parts)):
            raise ValueError("Part ordinals and filenames must be consecutive in delivery order")
        size = part.get("bytes")
        digest = part.get("sha256")
        if type(size) is not int or size <= 0 or not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError(f"Invalid fingerprint for declared part {part['filename']}")
        path = directory / part["filename"]
        if ordinal in missing_ordinals:
            if path.exists():
                raise ValueError(f"Part {part['filename']} was expected to be absent")
        elif fingerprint(path) != {"bytes": size, "sha256": digest}:
            raise ValueError(f"Part {part['filename']} did not match its manifest")


def verify_complete(directory, manifest, reader):
    path = str(directory / ARCHIVE)
    listing = subprocess.check_output([reader, "l", "-slt", "-ba", path], text=True)
    entries = [
        dict(line.split(" = ", 1) for line in block.splitlines() if " = " in line)
        for block in listing.strip().split("\n\n")
    ]
    expected = manifest["members"]
    if [entry["Path"] for entry in entries] != [member["filename"] for member in expected]:
        raise ValueError("Independent reader inventory did not match the delivery manifest")

    payload = entries[0]
    first_part = manifest["parts"][0]
    if int(payload["Volume Index"]) != 0 or int(payload["Packed Size"]) <= first_part["bytes"] - int(payload["Offset"]):
        raise ValueError("Payload did not span multiple parts")

    for entry, member in zip(entries, expected, strict=True):
        content = subprocess.check_output([reader, "x", "-so", "-bd", path, entry["Path"]])
        if len(content) != member["bytes"] or hashlib.sha256(content).hexdigest() != member["sha256"]:
            raise ValueError(f"Independent member verification failed for {entry['Path']}")


def verify_missing(directory, reader):
    result = subprocess.run(
        [reader, "t", "-bd", str(directory / ARCHIVE)], text=True,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False,
    )
    if result.returncode != 2:
        raise ValueError(f"Expected the independent reader to reject the missing part: {result.stdout}")
    print(f"Missing-middle delivery rejected by {Path(reader).name} with exit {result.returncode}")


def write_manifest(directory, manifest):
    (directory / MANIFEST).write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    # A delivery may have unrelated files beside the explicitly listed Archive parts.
    (directory / "notes.txt").write_text("Unrelated delivery note\n", encoding="utf-8")
    verify_parts(directory, manifest["parts"], manifest["missing_part_ordinals"])


def create_split_archive(workspace, directory, producer):
    sources = workspace / "sources"
    sources.mkdir()
    for filename, content in MEMBERS.items():
        path = sources / filename
        path.write_bytes(content)
        path.chmod(0o644)
        os.utime(path, (ZIP_EPOCH, ZIP_EPOCH))

    subprocess.run(
        [producer, "-q", "-X", "-0", "-s", "64k", str(directory / ARCHIVE), *MEMBERS],
        cwd=sources, env=os.environ | {"TZ": "UTC", "LC_ALL": "C", "ZIPOPT": ""}, check=True,
    )
    total = 1 + len(list(directory.glob("delivery.z[0-9][0-9]")))
    if total != 4:
        raise ValueError(f"Expected four Archive parts, received {total}")
    parts = []
    for ordinal in range(1, total + 1):
        filename = part_filename(ordinal, total)
        parts.append({"ordinal": ordinal, "filename": filename, **fingerprint(directory / filename)})
    if any(part["bytes"] != PART_BYTES for part in parts[:-1]):
        raise ValueError("Expected 64 KiB non-final Archive parts")
    return parts


def generate_delivery(destination, producer, reader):
    with tempfile.TemporaryDirectory(prefix=".split-zip-", dir=destination.parent) as staging:
        workspace = Path(staging)
        complete = workspace / "complete"
        missing = workspace / "missing-middle"
        complete.mkdir()
        missing.mkdir()

        parts = create_split_archive(workspace, complete, producer)
        manifest = {
            "schema_version": 1,
            "format": "info-zip-split-zip",
            "producer": {"name": "Info-ZIP", "version": "3.0"},
            "split_size_bytes": PART_BYTES,
            "parts": parts,
            "members": [
                {"filename": name, "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()}
                for name, content in MEMBERS.items()
            ],
            "variant": "complete",
            "missing_part_ordinals": [],
        }
        write_manifest(complete, manifest)
        verify_complete(complete, manifest, reader)

        for part in parts:
            if part["ordinal"] != MISSING_ORDINAL:
                shutil.copyfile(complete / part["filename"], missing / part["filename"])
        missing_manifest = manifest | {"variant": "missing-middle", "missing_part_ordinals": [MISSING_ORDINAL]}
        write_manifest(missing, missing_manifest)
        verify_missing(missing, reader)

        destination.mkdir()
        complete.rename(destination / complete.name)
        missing.rename(destination / missing.name)
    print(f"Verified split ZIP deliveries at {destination}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path, help="New directory for the two verified deliveries")
    parser.add_argument("--producer", default="zip", help="Path to Info-ZIP 3.0")
    args = parser.parse_args()
    producer = shutil.which(args.producer)
    reader = shutil.which("7zz") or shutil.which("7z")
    if not producer or not reader:
        parser.error("Info-ZIP 3.0 and 7-Zip (7zz or 7z) are required")
    version = subprocess.check_output([producer, "-v"], text=True)
    if not re.search(r"^This is Zip 3\.0 \(.*by Info-ZIP", version, re.MULTILINE):
        parser.error("The split ZIP recipe requires Info-ZIP 3.0")
    generate_delivery(args.destination.absolute(), str(Path(producer).resolve()), reader)


if __name__ == "__main__":
    main()
