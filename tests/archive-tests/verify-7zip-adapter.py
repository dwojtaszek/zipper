#!/usr/bin/env python3
"""Run 7-Zip checks declared by Archive Test Fixture Expectation Files."""

import hashlib
import json
import subprocess
import sys
import threading
import argparse
from pathlib import Path

from reader_process import run_reader_command, start_reader_process, terminate_reader_process

MAX_EXPANDED_BYTES_BUDGET = 32 * 1024 * 1024
MAX_ARCHIVE_PHYSICAL_BYTES = 16 * 1024 * 1024
MAX_EXPECTATION_FILE_BYTES = 1024 * 1024


def list_archive_entries(seven, archive):
    try:
        result = run_reader_command(
            [seven, "l", "-slt", "-ba", "-sccUTF-8", str(archive)], timeout=30)
    except subprocess.TimeoutExpired as error:
        raise ValueError("7-Zip timed out listing Archive entries") from error
    if result.returncode not in (0, 1):
        raise ValueError("7-Zip failed to list Archive entries")

    names = {}
    for line in result.stdout.decode("utf-8", errors="replace").splitlines():
        if line.startswith("Path = "):
            name = line[len("Path = "):]
            names[name] = names.get(name, 0) + 1
    return names


def check_entry(seven, archive, entry, budget, archive_entries=None, expected_name_count=1):
    name = entry["readableName"]
    if (not name or name.startswith(("-", "@", "/", "\\")) or
            any(part in ("", ".", "..") for part in name.split("/")) or
            any(char in name for char in "\\:\x00*?")):
        raise ValueError(f"unsafe entry name: {name!r}")
    expected_size = entry["contentSize"]
    if not isinstance(expected_size, int) or expected_size < 0 or expected_size > budget:
        raise ValueError(f"invalid content size: {name!r}")
    if expected_size == 0:
        if any(char in name for char in "\r\n\v\f\x1c\x1d\x1e\x85\u2028\u2029"):
            raise ValueError(
                f"7-Zip cannot verify an empty entry with line separators in its name: {name!r}")
        if archive_entries is None:
            archive_entries = list_archive_entries(seven, archive)
        if archive_entries.get(name) != expected_name_count:
            raise ValueError(f"empty entry {name!r} is not present as declared in Archive listing")

    # ponytail: Stream selected members to stdout, not disk. Stop at the declared
    # size so a corrupt or hostile Archive cannot exhaust temporary storage.
    with start_reader_process(
        [seven, "x", "-so", str(archive), name],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
    ) as process:
        timed_out = threading.Event()

        def kill_after_timeout():
            timed_out.set()
            terminate_reader_process(process)

        timer = threading.Timer(30, kill_after_timeout)
        timer.start()
        try:
            digest = hashlib.sha256()
            size = 0
            while chunk := process.stdout.read(65536):
                size += len(chunk)
                if size > expected_size:
                    terminate_reader_process(process)
                digest.update(chunk)
            try:
                return_code = process.wait(timeout=30)
            except subprocess.TimeoutExpired as error:
                raise ValueError(f"7-Zip timed out extracting {name!r}") from error
            if timed_out.is_set():
                raise ValueError(f"7-Zip timed out extracting {name!r}")
            if size != expected_size or digest.hexdigest() != entry["contentSha256"]:
                raise ValueError(f"content hash mismatch: {name!r}")
            # 7-Zip may report a warning for a malformed header even when the
            # requested member is complete and matches the declared content.
            if return_code not in (0, 1):
                raise ValueError(f"7-Zip extraction failed for {name!r}")
        finally:
            timer.cancel()
            if process.poll() is None:
                terminate_reader_process(process)
                process.communicate()


def check_archive_rejection(seven, archive, case_key):
    try:
        result = run_reader_command([seven, "t", str(archive)], timeout=30)
    except subprocess.TimeoutExpired as error:
        raise ValueError(f"7-Zip timed out testing {case_key}") from error

    stdout = result.stdout.decode("utf-8", errors="replace") if result.stdout else ""
    stderr = result.stderr.decode("utf-8", errors="replace") if result.stderr else ""
    combined_output = f"{stdout}\n{stderr}".lower()

    if result.returncode == 0:
        raise ValueError(f"7-Zip accepted {case_key}, expected rejection")

    if result.returncode in (255, 130, -2):
        raise ValueError(f"7-Zip interrupted testing {case_key}")

    if result.returncode < 0:
        raise ValueError(f"7-Zip terminated by signal {-result.returncode} testing {case_key}")

    if sys.platform != "win32" and result.returncode > 128 and result.returncode not in (130, 255):
        raise ValueError(f"7-Zip terminated by signal {result.returncode - 128} testing {case_key}")

    if result.returncode == 8 or "not enough memory" in combined_output or "can't allocate required memory" in combined_output:
        raise ValueError(f"7-Zip insufficient memory testing {case_key}")

    if result.returncode == 7 or "command line error" in combined_output:
        raise ValueError(f"7-Zip command-line error testing {case_key}")

    system_error_markers = (
        "system error:",
        "permission denied",
        "access is denied",
        "no such file or directory",
        "i/o error",
    )
    if any(marker in combined_output for marker in system_error_markers):
        raise ValueError(f"7-Zip system error testing {case_key}")

    if result.returncode not in (1, 2):
        raise ValueError(f"7-Zip execution failed with exit code {result.returncode} testing {case_key}")

    content_markers = (
        "data error",
        "crc failed",
        "crc error",
        "headers error",
        "is not archive",
        "cannot open the file as",
        "cannot open as archive",
        "can't open as archive",
        "missing volume",
        "unavailable data",
        "unexpected end of data",
        "unsupported method",
    )
    if not any(marker in combined_output for marker in content_markers):
        raise ValueError(f"7-Zip rejected {case_key} without diagnostic content error")


def _write_report(report_path, report):
    if report_path is not None:
        Path(report_path).write_text(
            json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def verify(fixtures, seven, report_path=None):
    checked = 0
    report = {
        "adapter": "7zip",
        "adapterVersion": None,
        "fixtures": [],
    }
    for sidecar in sorted(fixtures.glob("atc-*.json")):
        with sidecar.open("rb") as stream:
            sidecar_bytes = stream.read(MAX_EXPECTATION_FILE_BYTES + 1)
        if len(sidecar_bytes) > MAX_EXPECTATION_FILE_BYTES:
            raise ValueError(f"Expectation File exceeds the 1 MiB budget: {sidecar}")
        data = json.loads(sidecar_bytes.decode("utf-8"))
        archive = sidecar.with_suffix(".zip")
        archive_sha256 = None
        if archive.is_file():
            with archive.open("rb") as stream:
                archive_bytes = stream.read(MAX_ARCHIVE_PHYSICAL_BYTES + 1)
            if len(archive_bytes) > MAX_ARCHIVE_PHYSICAL_BYTES:
                raise ValueError(f"Archive exceeds the 16 MiB physical budget: {archive}")
            archive_sha256 = hashlib.sha256(archive_bytes).hexdigest()
        fixture_report = {
            "fixtureId": data.get("fixtureId"),
            "caseKey": data.get("caseKey"),
            "expectationFileSha256": hashlib.sha256(sidecar_bytes).hexdigest(),
            "archiveSha256": archive_sha256,
            "status": "pass",
            "checks": [],
            "operations": [],
        }
        report["fixtures"].append(fixture_report)
        for directive in data.get("adapterChecks", []):
            if directive.get("adapter") != "7zip":
                continue
            action = directive.get("action")
            outcome = directive.get("expectedOutcome")
            check_report = {
                "action": action,
                "expectedOutcome": outcome,
                "status": "fail",
            }
            fixture_report["checks"].append(check_report)
            try:
                if not archive.is_file():
                    raise ValueError(f"missing Archive for {data['caseKey']}")
                if action == "presence" and outcome == "present":
                    check_report["observedOutcome"] = "present"
                elif action == "test" and outcome == "failure":
                    check_archive_rejection(seven, archive, data["caseKey"])
                    check_report["observedOutcome"] = "failure"
                    fixture_report["operations"].append({
                        "operation": "integrity-check",
                        "normalizedOutcome": "integrity-fails",
                        "status": "pass",
                    })
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
                    if isinstance(budget, bool) or not isinstance(budget, int) or budget < 0:
                        raise ValueError(f"invalid expanded bytes budget for {data['caseKey']}")
                    if budget > MAX_EXPANDED_BYTES_BUDGET:
                        raise ValueError(
                            f"expanded bytes budget exceeds hard limit for {data['caseKey']}")
                    if sum(entry["contentSize"] for entry in entries) > budget:
                        raise ValueError(f"expanded bytes budget exceeded for {data['caseKey']}")
                    expected_name_counts = {
                        entry["readableName"]: sum(
                            candidate["readableName"] == entry["readableName"]
                            for candidate in entries)
                        for entry in entries
                    }
                    archive_entries = None
                    for entry in entries:
                        if entry["contentSize"] == 0 and archive_entries is None:
                            archive_entries = list_archive_entries(seven, archive)
                        check_entry(
                            seven, archive, entry, budget, archive_entries,
                            expected_name_counts[entry["readableName"]])
                    check_report["observedOutcome"] = "success"
                    fixture_report["operations"].append({
                        "operation": "extract",
                        "normalizedOutcome": "extract-succeeds",
                        "status": "pass",
                    })
                else:
                    raise ValueError(f"unknown 7-Zip action/outcome for {data['caseKey']}: {action}/{outcome}")
                check_report["status"] = "pass"
                checked += 1
            except (KeyError, OSError, ValueError, subprocess.TimeoutExpired) as error:
                if action == "test":
                    observed = "integrity-passes" if "accepted" in str(error) else None
                    operation = "integrity-check"
                elif action in ("extract", "extract-entries"):
                    observed = "extract-fails"
                    operation = "extract"
                else:
                    observed = None
                    operation = None
                if operation is not None:
                    fixture_report["operations"].append({
                        "operation": operation,
                        "normalizedOutcome": observed,
                        "status": "fail",
                    })
                fixture_report["status"] = "fail"
                fixture_report["failure"] = str(error)
                _write_report(report_path, report)
                raise
    if checked == 0:
        raise ValueError("no 7-Zip adapter checks declared")
    _write_report(report_path, report)
    return checked


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Run 7-Zip checks declared by Archive Test Fixture Expectation Files.")
    parser.add_argument("fixture_dir")
    parser.add_argument("seven_zip")
    parser.add_argument("--report", default=None,
                        help="optional JSON report path outside the fixture directory")
    try:
        args = parser.parse_args()
        count = verify(Path(args.fixture_dir), args.seven_zip, args.report)
        print(f"7-Zip verified {count} declared adapter checks.")
    except (KeyError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"7-Zip adapter verification failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
