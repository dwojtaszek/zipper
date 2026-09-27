#!/usr/bin/env python3
"""Run 7-Zip checks declared by Archive Test Fixture Expectation Files."""

import hashlib
import json
import subprocess
import sys
import threading
from pathlib import Path


def check_entry(seven, archive, entry, budget):
    name = entry["readableName"]
    if (not name or name.startswith(("-", "@", "/", "\\")) or
            any(part in ("", ".", "..") for part in name.split("/")) or
            any(char in name for char in "\\:\x00*?")):
        raise ValueError(f"unsafe entry name: {name!r}")
    expected_size = entry["contentSize"]
    if not isinstance(expected_size, int) or expected_size < 0 or expected_size > budget:
        raise ValueError(f"invalid content size: {name!r}")

    # ponytail: Stream selected members to stdout, not disk. Stop at the declared
    # size so a corrupt or hostile Archive cannot exhaust temporary storage.
    with subprocess.Popen(
        [seven, "x", "-so", str(archive), name],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
    ) as process:
        timer = threading.Timer(30, lambda: process.kill() if process.poll() is None else None)
        timer.start()
        try:
            digest = hashlib.sha256()
            size = 0
            while chunk := process.stdout.read(65536):
                size += len(chunk)
                if size > expected_size:
                    raise ValueError(f"content size exceeded: {name!r}")
                digest.update(chunk)
            if process.wait(timeout=30) != 0:
                raise ValueError(f"7-Zip extraction failed for {name!r}")
            if size != expected_size or digest.hexdigest() != entry["contentSha256"]:
                raise ValueError(f"content hash mismatch: {name!r}")
        finally:
            timer.cancel()
            if process.poll() is None:
                process.kill()
                process.communicate()


def verify(fixtures, seven):
    checked = 0
    for sidecar in sorted(fixtures.glob("atc-*.json")):
        data = json.loads(sidecar.read_text(encoding="utf-8"))
        archive = sidecar.with_suffix(".zip")
        for directive in data.get("adapterChecks", []):
            if directive["adapter"] != "7zip":
                raise ValueError(f"unknown adapter for {data['caseKey']}")
            if not archive.is_file():
                raise ValueError(f"missing Archive for {data['caseKey']}")
            action = directive["action"]
            outcome = directive["expectedOutcome"]
            if action == "presence" and outcome == "present":
                checked += 1
                continue
            if action == "test" and outcome == "failure":
                result = subprocess.run([seven, "t", str(archive)], capture_output=True, timeout=30)
                if result.returncode == 0:
                    raise ValueError(f"7-Zip accepted {data['caseKey']}, expected rejection")
            elif action in ("extract", "extract-entries") and outcome == "success":
                entries = [entry for entry in data["entries"] if entry["kind"] == "file"]
                names = directive.get("entryNames")
                if action == "extract-entries":
                    entries = [entry for entry in entries if entry["readableName"] in names]
                    if len(entries) != len(names) or len({e["readableName"] for e in entries}) != len(names):
                        raise ValueError(f"missing or duplicate selected entries for {data['caseKey']}")
                if not entries:
                    raise ValueError(f"no entries selected for {data['caseKey']}")
                budget = data["limits"]["expandedBytesBudget"]
                if sum(entry["contentSize"] for entry in entries) > budget:
                    raise ValueError(f"expanded bytes budget exceeded for {data['caseKey']}")
                for entry in entries:
                    check_entry(seven, archive, entry, budget)
            else:
                raise ValueError(f"unknown 7-Zip action/outcome for {data['caseKey']}: {action}/{outcome}")
            checked += 1
    if checked == 0:
        raise ValueError("no 7-Zip adapter checks declared")
    return checked


if __name__ == "__main__":
    try:
        count = verify(Path(sys.argv[1]), sys.argv[2])
        print(f"7-Zip verified {count} declared adapter checks.")
    except (IndexError, KeyError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"7-Zip adapter verification failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
