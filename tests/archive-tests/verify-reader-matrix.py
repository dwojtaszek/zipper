#!/usr/bin/env python3
"""Compare Archive Test Fixture outcomes across available Reader Adapter Profiles."""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path

from reader_process import run_reader_command, start_reader_process, terminate_reader_process


@dataclass(frozen=True)
class ReaderAdapterProfile:
    optional: bool
    executables: tuple[str, ...] = ()
    adapter_check_outcomes: tuple[tuple[str, tuple[str, ...]], ...] = ()


READER_ADAPTER_PROFILES = {
    "dotnet-reference": ReaderAdapterProfile(
        optional=False,
        adapter_check_outcomes=(
            ("list", ("list-succeeds", "list-fails")),
            ("read-entry", (
                "read-entry-content-matches", "read-entry-returns-unverified-bytes",
                "read-entry-fails", "unsupported-method-rejected")),
            ("integrity-check", (
                "integrity-passes", "integrity-fails", "integrity-unchecked",
                "crc-mismatch-unchecked", "crc-mismatch-rejected", "unsupported-method-rejected")),
            ("extract", ("extract-succeeds", "extract-fails", "unsupported-method-rejected")),
        )),
    "python-zipfile": ReaderAdapterProfile(
        optional=False,
        adapter_check_outcomes=(
            ("list", ("list-succeeds", "list-fails")),
            ("read-entry", (
                "read-entry-content-matches", "read-entry-returns-unverified-bytes",
                "read-entry-fails", "unsupported-method-rejected")),
            ("integrity-check", (
                "integrity-passes", "integrity-fails", "crc-mismatch-unchecked",
                "crc-mismatch-rejected", "unsupported-method-rejected")),
            ("extract", ("extract-succeeds", "extract-fails", "unsupported-method-rejected")),
        )),
    "infozip-unzip": ReaderAdapterProfile(
        optional=True,
        executables=("unzip",),
        adapter_check_outcomes=(
            ("list", ("list-succeeds", "list-fails")),
            ("read-entry", (
                "read-entry-content-matches", "read-entry-returns-unverified-bytes",
                "read-entry-fails", "unsupported-method-rejected")),
            ("integrity-check", (
                "integrity-passes", "integrity-fails", "unsupported-method-rejected")),
        )),
    "7zip": ReaderAdapterProfile(
        optional=True,
        executables=("7zz", "7z"),
        adapter_check_outcomes=(
            ("extract", ("success",)),
            ("test", ("failure",)),
            ("extract-entries", ("success",)),
            ("presence", ("present",)),
        ),
    ),
}

OPERATIONS = ("list", "read-entry", "integrity-check", "extract")
OUTCOME_ALIASES = {
    "listed-count-matches-entries": "list-succeeds",
    "empty-list": "list-succeeds",
    "extract-completes": "extract-succeeds",
    "extract-completes-empty": "extract-succeeds",
}
FAILURE_OUTCOMES = {
    "list": "list-fails",
    "read-entry": "read-entry-fails",
    "integrity-check": "integrity-fails",
    "extract": "extract-fails",
}
MAX_READ_BYTES = 32 * 1024 * 1024
INFOZIP_LIST_LINE_BREAKS = "\r\n\v\f\x1c\x1d\x1e\x85\u2028\u2029"
INFOZIP_ARCHIVE_ERROR_CODES = (0, 1, 2, 3, 9, 11, 12, 51, 81, 82)


def _host_platforms():
    platforms = set()
    if os.name == "nt":
        platforms.add("windows")
    if sys.platform.startswith("linux"):
        platforms.add("linux")
    platforms.update(
        value.strip() for value in os.environ.get("ZIPPER_ATC_PLATFORMS", "").split(",")
        if value.strip())
    return platforms


class ReaderExecutionError(ValueError):
    """A reader could not be executed safely or within its budget."""


def _raise_infozip_execution_error(return_code, operation, archive):
    if return_code == 9 and not archive.is_file():
        raise ReaderExecutionError(
            f"Archive disappeared during Info-ZIP {operation}")
    if return_code not in INFOZIP_ARCHIVE_ERROR_CODES:
        raise ReaderExecutionError(
            f"Info-ZIP unzip failed during {operation} (exit code {return_code})")


def _canonical_outcome(outcome):
    return OUTCOME_ALIASES.get(outcome, outcome)


def _expectations_for(case, operation):
    platforms = _host_platforms()
    return [expectation for expectation in case.get("expectations", [])
            if expectation.get("operation") == operation
            and (expectation.get("platform") is None
                 or expectation.get("platform") in platforms)]


def _allowed_outcomes_by_profile(case, operation):
    by_profile = {}
    for expectation in _expectations_for(case, operation):
        profile = expectation.get("profile", "")
        by_profile.setdefault(profile, set()).update(expectation.get("allowedOutcomes", []))
    return {
        profile: sorted(outcomes)
        for profile, outcomes in sorted(by_profile.items())
    }


def _select_expectation_profile(allowed_by_profile, outcome):
    if "full-codec" in allowed_by_profile and "unsupported-reader" in allowed_by_profile:
        unsupported_outcomes = {
            _canonical_outcome(value)
            for value in allowed_by_profile["unsupported-reader"]
        }
        return "unsupported-reader" if _canonical_outcome(outcome) in unsupported_outcomes else "full-codec"
    if "strict-integrity-v1" in allowed_by_profile:
        return "strict-integrity-v1"
    if "strict" in allowed_by_profile:
        return "strict"
    if len(allowed_by_profile) == 1:
        return next(iter(allowed_by_profile))
    return None


def _outcome_is_allowed(outcome, allowed_outcomes, operation):
    allowed = {_canonical_outcome(value) for value in allowed_outcomes}
    if "operation-fails" in allowed and outcome == FAILURE_OUTCOMES[operation]:
        return True
    return _canonical_outcome(outcome) in allowed


def _operation_result(case, operation, outcome=None, reason=None):
    all_allowed_outcomes = _allowed_outcomes_by_profile(case, operation)
    if outcome is None:
        return {
            "operation": operation,
            "normalizedOutcome": "not-run",
            "allowedOutcomesByProfile": all_allowed_outcomes,
            "status": "not-run",
            "reason": reason or "operation not run",
        }

    expectation_profile = _select_expectation_profile(all_allowed_outcomes, outcome)
    allowed = expectation_profile is not None and _outcome_is_allowed(
        outcome, all_allowed_outcomes[expectation_profile], operation)
    return {
        "operation": operation,
        "normalizedOutcome": outcome,
        "expectationProfile": expectation_profile,
        "allowedOutcomesByProfile": all_allowed_outcomes,
        "status": "pass" if allowed else "fail",
    }


def _reader_operation_result(case, operation, observed, reader):
    if observed is None:
        result = _operation_result(
            case, operation, reason=f"{reader} report has no operation result")
        result["status"] = "fail"
        result["failure"] = "missing operation result"
        return result

    status = observed.get("status")
    if status == "not-run":
        result = _operation_result(
            case, operation, reason=observed.get("reason"))
        applicable = bool(_expectations_for(case, operation))
        required = applicable and (
            operation != "extract" or case.get("classification") == "valid")
        if required:
            result["status"] = "fail"
            result["failure"] = "applicable operation was not run"
        return result

    outcome = observed.get("normalizedOutcome")
    result = _operation_result(case, operation, outcome)
    if status != "pass" or outcome is None:
        result["status"] = "fail"
        result["failure"] = (
            observed.get("failure")
            or f"{reader} report did not record a passing operation")
    return result


def _observed_operation_result(case, operation, outcome=None, reason=None, failure=None):
    result = _operation_result(case, operation, outcome, reason)
    if result["status"] in ("pass", "fail"):
        result["expectationStatus"] = result["status"]
        result["status"] = "observed"
    if failure is not None:
        result["failure"] = failure
    return result


def _execution_error_operation_result(case, operation, error):
    result = _operation_result(case, operation, reason=str(error))
    result["status"] = "execution-error"
    result["failure"] = str(error)
    return result


def _safe_member_name(name):
    return (
        isinstance(name, str)
        and bool(name)
        and not name.startswith(("-", "@", "/", "\\"))
        and "\x00" not in name
        and ":" not in name
        and "\\" not in name
        and not any(character in name for character in "*?[]")
        and all(part not in ("", ".", "..") for part in name.split("/"))
    )


def _reader_command(command, *arguments):
    prefix = [command] if isinstance(command, str) else list(command)
    return [*prefix, *arguments]


def _run_reader(command, arguments, timeout, start_error="reader command failed"):
    try:
        return run_reader_command(_reader_command(command, *arguments), timeout)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ReaderExecutionError(f"{start_error}: {error}") from error


def _remaining_time(deadline):
    remaining = deadline - time.monotonic()
    return remaining if remaining > 0 else None


def _run_unzip_member(command, archive, name, byte_limit, timeout):
    try:
        process = start_reader_process(
            _reader_command(command, "-p", str(archive), name),
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
        )
    except OSError as error:
        raise ReaderExecutionError(f"could not start Info-ZIP unzip: {error}") from error

    timed_out = threading.Event()

    def kill_after_timeout():
        timed_out.set()
        terminate_reader_process(process)

    timer = threading.Timer(timeout, kill_after_timeout)
    timer.start()
    digest = hashlib.sha256()
    size = 0
    over_budget = False
    try:
        while chunk := process.stdout.read(min(65536, byte_limit - size + 1)):
            size += len(chunk)
            if size > byte_limit:
                over_budget = True
                terminate_reader_process(process)
                break
            digest.update(chunk)

        try:
            return_code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired as error:
            raise ReaderExecutionError(
                f"Info-ZIP unzip timed out reading {name!r}") from error
        if timed_out.is_set():
            raise ReaderExecutionError(f"Info-ZIP unzip timed out reading {name!r}")
        return return_code, size, digest.hexdigest(), over_budget
    finally:
        timer.cancel()
        if process.poll() is None:
            terminate_reader_process(process)
            process.communicate()
        process.stdout.close()


def _unzip_list(command, archive, case, timeout):
    result = _run_reader(command, ["-Z1", str(archive)], timeout,
                         start_error="could not start Info-ZIP unzip")
    if result.returncode < 0:
        raise ReaderExecutionError(
            f"Info-ZIP unzip was terminated by signal {-result.returncode}")
    _raise_infozip_execution_error(result.returncode, "listing", archive)
    # Info-ZIP returns 1 when it listed entries but encountered a recoverable
    # warning. Count agreement, not its diagnostic wording, is the outcome.
    names = result.stdout.decode("utf-8", errors="replace").splitlines()
    if result.returncode not in (0, 1):
        return "list-fails"
    return "list-succeeds" if len(names) == case["limits"]["entryCount"] else "list-fails"


def _unzip_read_entries(command, archive, case, deadline, supported_methods):
    if case.get("caseKey", "").startswith("encoding-"):
        return None, "legacy filename decoding is reader-specific"

    entries = [entry for entry in case.get("entries", []) if entry.get("kind") == "file"]
    if not entries:
        return "read-entry-content-matches", None
    if any(not _safe_member_name(entry.get("readableName")) for entry in entries):
        return None, "unsafe member names are not selected"
    names = [entry["readableName"] for entry in entries]
    if len(set(names)) != len(names):
        return None, "duplicate member names cannot be selected independently"

    budget = case.get("limits", {}).get("expandedBytesBudget")
    if not isinstance(budget, int) or budget < 0:
        raise ReaderExecutionError("invalid expandedBytesBudget")
    budget = min(budget, MAX_READ_BYTES)
    bytes_read = 0

    for entry in entries:
        expected_size = entry.get("contentSize")
        expected_hash = entry.get("contentSha256")
        if not isinstance(expected_size, int) or expected_size < 0 or not expected_hash:
            return None, "member content expectation is incomplete"
        if expected_size > budget - bytes_read:
            return None, "declared member sizes exceed the reader byte budget"

        timeout = _remaining_time(deadline)
        if timeout is None:
            raise ReaderExecutionError(
                f"Info-ZIP fixture deadline exceeded reading {entry['readableName']!r}")
        return_code, size, digest, over_budget = _run_unzip_member(
            command, archive, entry["readableName"], budget - bytes_read, timeout)
        bytes_read += size
        if over_budget:
            return "read-entry-returns-unverified-bytes", None
        if return_code < 0:
            raise ReaderExecutionError(
                f"Info-ZIP unzip was terminated by signal {-return_code} "
                f"reading {entry['readableName']!r}")
        _raise_infozip_execution_error(return_code, "reading", archive)
        if return_code in (9, 11):
            return "read-entry-fails", None
        if return_code not in (0, 1):
            method = entry.get("centralDirectoryMethod", entry.get("localHeaderMethod"))
            if method not in supported_methods:
                return "unsupported-method-rejected", None
            return "read-entry-fails", None
        if size != expected_size or digest != expected_hash:
            return "read-entry-returns-unverified-bytes", None

    return "read-entry-content-matches", None


def _unzip_integrity(command, archive, case, timeout):
    result = _run_reader(command, ["-t", str(archive)], timeout,
                         start_error="could not start Info-ZIP unzip")
    if result.returncode < 0:
        raise ReaderExecutionError(
            f"Info-ZIP unzip was terminated by signal {-result.returncode}")
    _raise_infozip_execution_error(result.returncode, "integrity check", archive)
    output = (result.stdout + result.stderr).decode("utf-8", errors="replace").lower()
    if result.returncode == 0:
        return "integrity-passes"
    if ("unsupported compression method" in output
            or "compression method not supported" in output
            or "compression method 98" in output):
        return "unsupported-method-rejected"
    return "integrity-fails"


def _reported_reader(case, reader_fixture, reader_name):
    operations_by_name = {}
    for operation in reader_fixture.get("operations", []):
        name = operation.get("operation")
        if name not in OPERATIONS:
            raise ValueError(f"{reader_name} report contains unknown operation {name!r}")
        if name in operations_by_name:
            raise ValueError(f"{reader_name} report contains duplicate operation {name!r}")
        operations_by_name[name] = operation
    operations = {}
    for name in OPERATIONS:
        observed = operations_by_name.get(name)
        operations[name] = _reader_operation_result(
            case, name, observed, reader_name)

    status = "fail" if reader_fixture.get("status") != "pass" or any(
        result["status"] == "fail" for result in operations.values()) else "pass"
    return {
        "status": status,
        "version": reader_fixture.get("adapterVersion"),
        "operations": operations,
    }


def _infozip_reader(command, archive, case, supported_methods):
    timeout = max(1, min(case.get("limits", {}).get("deadlineSeconds", 10), 30))
    deadline = time.monotonic() + timeout
    operations = {}
    if _expectations_for(case, "list"):
        names = [entry.get("readableName") for entry in case.get("entries", [])]
        if any(
            isinstance(name, str)
            and any(separator in name for separator in INFOZIP_LIST_LINE_BREAKS)
            for name in names
        ):
            operations["list"] = _operation_result(
                case, "list",
                reason="Info-ZIP listing cannot delimit member names containing line separators")
        else:
            operation_timeout = _remaining_time(deadline)
            if operation_timeout is None:
                operations["list"] = _operation_result(
                    case, "list", reason="Info-ZIP fixture deadline exhausted")
            else:
                try:
                    outcome = _unzip_list(command, archive, case, operation_timeout)
                    operations["list"] = _observed_operation_result(case, "list", outcome)
                except ReaderExecutionError as error:
                    operations["list"] = _execution_error_operation_result(
                        case, "list", error)
    else:
        operations["list"] = _operation_result(case, "list", reason="no applicable expectation")

    if _expectations_for(case, "read-entry"):
        if _remaining_time(deadline) is None:
            operations["read-entry"] = _operation_result(
                case, "read-entry", reason="Info-ZIP fixture deadline exhausted")
        else:
            try:
                read_outcome, read_reason = _unzip_read_entries(
                    command, archive, case, deadline, supported_methods)
                operations["read-entry"] = _observed_operation_result(
                    case, "read-entry", read_outcome, read_reason)
            except ReaderExecutionError as error:
                operations["read-entry"] = _execution_error_operation_result(
                    case, "read-entry", error)
    else:
        operations["read-entry"] = _operation_result(
            case, "read-entry", reason="no applicable expectation")

    if _expectations_for(case, "integrity-check"):
        operation_timeout = _remaining_time(deadline)
        if operation_timeout is None:
            operations["integrity-check"] = _operation_result(
                case, "integrity-check", reason="Info-ZIP fixture deadline exhausted")
        else:
            try:
                outcome = _unzip_integrity(command, archive, case, operation_timeout)
                operations["integrity-check"] = _observed_operation_result(
                    case, "integrity-check", outcome)
            except ReaderExecutionError as error:
                operations["integrity-check"] = _execution_error_operation_result(
                    case, "integrity-check", error)
    else:
        operations["integrity-check"] = _operation_result(
            case, "integrity-check", reason="no applicable expectation")

    operations["extract"] = _operation_result(
        case, "extract", reason="Info-ZIP extraction is not run by the matrix")

    return {"status": "observed", "version": None, "operations": operations}


def _seven_zip_reader(case, fixture_id, seven_zip_fixtures):
    fixture = seven_zip_fixtures.get(fixture_id)
    if fixture is None or not fixture.get("checks"):
        operations = {
            operation: _operation_result(
                case, operation, reason="no 7-Zip Adapter Check declared")
            for operation in OPERATIONS
        }
        return {"status": "not-run", "version": None, "operations": operations}

    operations = {
        operation: _operation_result(
            case, operation, reason="no 7-Zip Adapter Check for this operation")
        for operation in OPERATIONS
    }
    expected_operations = set()
    # Extract and extract-entries checks can both project to one operation.
    for check in fixture.get("checks", []):
        if check.get("action") in ("extract", "extract-entries"):
            expected_operations.add("extract")
        elif check.get("action") == "test":
            expected_operations.add("integrity-check")

    operations_consistent = True
    observed_operations = set()
    for observed in fixture.get("operations", []):
        operation = observed.get("operation")
        if operation not in expected_operations:
            operations_consistent = False
            continue
        expected_outcome = (
            "integrity-fails" if operation == "integrity-check" else "extract-succeeds")
        if (observed.get("status") != "pass"
                or observed.get("normalizedOutcome") != expected_outcome):
            operations_consistent = False
        else:
            observed_operations.add(operation)
        result = _observed_operation_result(
            case, operation, observed.get("normalizedOutcome"))
        if not operations_consistent:
            result["status"] = "fail"
        operations[operation] = result

    if observed_operations != expected_operations:
        operations_consistent = False
    status = "pass" if fixture.get("status") == "pass" and operations_consistent else "fail"
    return {
        "status": status,
        "version": fixture.get("adapterVersion"),
        "operations": operations,
    }


def _agreement(readers):
    results = {}
    splits = []
    for operation in OPERATIONS:
        outcomes = {
            name: reader["operations"][operation]["normalizedOutcome"]
            for name, reader in readers.items()
            if reader["operations"][operation]["status"] in ("observed", "pass", "fail")
            and reader["operations"][operation]["normalizedOutcome"] != "not-run"
        }
        if len(outcomes) < 2:
            status = "single-reader"
        elif len(set(outcomes.values())) == 1:
            status = "agree"
        else:
            status = "split"
            splits.append(operation)
        results[operation] = {"status": status, "outcomes": outcomes}
    return {"operations": results, "splitOperations": splits}


def _profile_report(profile, executable, reason=None):
    if executable is None:
        return {
            "status": "skipped",
            "optional": profile.optional,
            "reason": reason or f"no {profile.executables[0]} executable found on PATH",
        }
    return {
        "status": "pending",
        "optional": profile.optional,
        "executable": executable,
        "version": None,
    }


def _validate_adapter_checks(case):
    for directive in case.get("adapterChecks", []):
        adapter = directive.get("adapter")
        profile = READER_ADAPTER_PROFILES.get(adapter)
        if profile is None:
            raise ValueError(f"unknown Reader Adapter Profile '{adapter}' for {case.get('caseKey')}")
        allowed_outcomes = dict(profile.adapter_check_outcomes)
        action = directive.get("action")
        expected = directive.get("expectedOutcome")
        if expected not in allowed_outcomes.get(action, ()):
            raise ValueError(
                f"invalid Adapter Check for {case.get('caseKey')}: {adapter}/{action}/{expected}")
        names = directive.get("entryNames")
        if action == "extract-entries":
            entries = {entry.get("readableName") for entry in case.get("entries", [])
                       if entry.get("kind") == "file"}
            if (not isinstance(names, list) or not names
                    or any(not isinstance(name, str) or not name for name in names)
                    or len(names) != len(set(names))
                    or any(name not in entries for name in names)):
                raise ValueError(
                    f"invalid extract-entries Adapter Check for {case.get('caseKey')}")
        elif names is not None:
            raise ValueError(
                f"Adapter Check {adapter}/{action} must not declare entryNames")


def _adapter_check_results(case, readers, seven_zip_fixtures):
    results = []
    seven_zip_check_index = 0
    for directive in case.get("adapterChecks", []):
        adapter = directive["adapter"]
        action = directive["action"]
        expected = directive["expectedOutcome"]
        result = {
            "adapter": adapter,
            "action": action,
            "expectedOutcome": expected,
        }
        reader = readers[adapter]
        if reader["status"] == "skipped":
            result.update(status="skipped", reason=reader.get("reason", "reader unavailable"))
        elif adapter == "7zip":
            fixture = seven_zip_fixtures.get(case["fixtureId"])
            checks = fixture.get("checks", []) if fixture is not None else []
            observed_check = (
                checks[seven_zip_check_index]
                if seven_zip_check_index < len(checks) else None)
            seven_zip_check_index += 1
            if observed_check is None:
                result.update(status="fail", reason="7-Zip report is missing this Adapter Check")
            else:
                result["observedOutcome"] = observed_check.get("observedOutcome")
                result["status"] = (
                    "pass" if observed_check.get("status") == "pass"
                    and observed_check.get("expectedOutcome") == expected
                    and observed_check.get("observedOutcome") == expected
                    else "fail")
                if result["status"] == "fail":
                    result["reason"] = observed_check.get(
                        "failure", "7-Zip Adapter Check did not match its expected outcome")
        else:
            operation = reader["operations"][action]
            observed = operation.get("normalizedOutcome")
            if operation.get("status") == "execution-error":
                result.update(status="execution-error", reason=operation.get("failure"))
            elif operation.get("status") == "not-run":
                result.update(status="not-run", reason=operation.get("reason"))
            else:
                result["observedOutcome"] = observed
                result["status"] = (
                    "pass" if operation.get("status") in ("observed", "pass")
                    and observed == expected
                    else "fail")
                if result["status"] == "fail":
                    result["reason"] = (
                        f"observed outcome {observed!r} does not match {expected!r}")
        results.append(result)
    return results


def verify_directory(
    fixture_dir, python_report, executables=None, seven_zip_report=None, dotnet_report=None):
    """Return (matrix report, exit code); allowed reader splits do not fail."""
    fixture_dir = Path(fixture_dir)
    if dotnet_report is None:
        raise ValueError(".NET reference harness report is required")
    if python_report.get("adapter") != "python-zipfile":
        raise ValueError("Python verifier report has an unknown Reader Adapter Profile")

    executables = executables or {}
    sidecars = sorted(fixture_dir.glob("atc-*.json"))
    if not sidecars:
        raise ValueError(f"no Expectation Files found in {fixture_dir}")

    python_fixtures = {
        fixture.get("fixtureId"): fixture
        for fixture in python_report.get("fixtures", [])
        if fixture.get("fixtureId")
    }
    if len(python_fixtures) != len(python_report.get("fixtures", [])):
        raise ValueError("Python verifier report contains a fixture without a Fixture ID")

    dotnet_fixtures = {}
    if dotnet_report is not None:
        if dotnet_report.get("adapter") != "dotnet-reference":
            raise ValueError(".NET reference report has an unknown Reader Adapter Profile")
        dotnet_fixtures = {
            fixture.get("fixtureId"): fixture
            for fixture in dotnet_report.get("fixtures", [])
            if fixture.get("fixtureId")
        }
        if len(dotnet_fixtures) != len(dotnet_report.get("fixtures", [])):
            raise ValueError(".NET reference report contains a fixture without a Fixture ID")

    seven_zip_fixtures = {}
    if seven_zip_report is not None:
        if seven_zip_report.get("adapter") != "7zip":
            raise ValueError("7-Zip report has an unknown adapter")
        seven_zip_fixtures = {
            fixture.get("fixtureId"): fixture
            for fixture in seven_zip_report.get("fixtures", [])
            if fixture.get("fixtureId")
        }
        if len(seven_zip_fixtures) != len(seven_zip_report.get("fixtures", [])):
            raise ValueError("7-Zip report contains a fixture without a Fixture ID")

    infozip = executables.get("infozip-unzip")
    if "infozip-unzip" not in executables:
        infozip = shutil.which("unzip")
    seven = executables.get("7zip")
    if "7zip" not in executables:
        seven = shutil.which("7zz") or shutil.which("7z")

    profiles = {
        "dotnet-reference": {
            "status": "pending",
            "optional": False,
            "version": None,
        },
        "python-zipfile": {
            "status": "pending",
            "optional": False,
            "version": python_report.get("adapterVersion"),
        },
        "infozip-unzip": _profile_report(
            READER_ADAPTER_PROFILES["infozip-unzip"], infozip),
        "7zip": _profile_report(
            READER_ADAPTER_PROFILES["7zip"], seven, "7-Zip (7zz/7z) not found on PATH"),
    }

    report = {
        "fixtureDirectory": str(fixture_dir.resolve()),
        "profiles": profiles,
        "fixtures": [],
        "summary": {},
    }
    failures = []
    infozip_methods = {0, 8}

    if infozip is not None:
        profiles["infozip-unzip"]["version"] = None
        try:
            version = _run_reader(infozip, ["-v"], timeout=5)
            output = (version.stdout + version.stderr).decode("utf-8", errors="replace")
            profiles["infozip-unzip"]["version"] = output.splitlines()[0] if output else "unknown"
            if "USE_DEFLATE64" in output:
                infozip_methods.add(9)
            if "USE_BZIP2" in output:
                infozip_methods.add(12)
            profiles["infozip-unzip"]["supportedMethods"] = sorted(infozip_methods)
        except ReaderExecutionError as error:
            profiles["infozip-unzip"]["version"] = f"unknown ({error})"

    if seven is not None and seven_zip_report is None:
        profiles["7zip"]["status"] = "fail"
        profiles["7zip"]["reason"] = "7-Zip is available but its adapter report was not supplied"
        failures.append("7-Zip is available but its adapter report was not supplied")

    expected_ids = set()
    expected_seven_zip_checks = {}
    for sidecar in sidecars:
        try:
            case = json.loads(sidecar.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise ValueError(f"could not read Expectation File {sidecar}: {error}") from error

        fixture_id = case.get("fixtureId")
        if not isinstance(fixture_id, str) or sidecar.stem != fixture_id:
            raise ValueError(f"Expectation File {sidecar} has a mismatched Fixture ID")
        _validate_adapter_checks(case)
        expected_ids.add(fixture_id)
        expected_seven_zip_checks[fixture_id] = [
            (check["action"], check["expectedOutcome"])
            for check in case.get("adapterChecks", [])
            if check["adapter"] == "7zip"
        ]
        archive = sidecar.with_suffix(".zip")
        if not archive.is_file():
            raise ValueError(f"Archive is missing for {case.get('caseKey', fixture_id)}")

        python_fixture = python_fixtures.get(fixture_id)
        if python_fixture is None:
            raise ValueError(f"Python verifier report is missing Fixture ID {fixture_id}")
        readers = {
            "python-zipfile": _reported_reader(
                case, python_fixture, "Python verifier")
        }
        dotnet_fixture = dotnet_fixtures.get(fixture_id)
        if dotnet_fixture is None:
            raise ValueError(
                f".NET reference report is missing Fixture ID {fixture_id}")
        readers["dotnet-reference"] = _reported_reader(
            case, dotnet_fixture, "reference harness")
        if infozip is not None:
            try:
                readers["infozip-unzip"] = _infozip_reader(
                    infozip, archive, case, infozip_methods)
            except (KeyError, TypeError, ReaderExecutionError) as error:
                readers["infozip-unzip"] = {
                    "status": "fail",
                    "version": profiles["infozip-unzip"].get("version"),
                    "failure": str(error),
                    "operations": {
                        operation: _operation_result(
                            case, operation, reason="reader execution failed")
                        for operation in OPERATIONS
                    },
                }
        else:
            readers["infozip-unzip"] = {
                "status": "skipped",
                "version": None,
                "reason": profiles["infozip-unzip"]["reason"],
                "operations": {
                    operation: _operation_result(
                        case, operation, reason=profiles["infozip-unzip"]["reason"])
                    for operation in OPERATIONS
                },
            }

        if seven_zip_report is not None:
            readers["7zip"] = _seven_zip_reader(case, fixture_id, seven_zip_fixtures)
        else:
            readers["7zip"] = {
                "status": profiles["7zip"]["status"],
                "version": None,
                "reason": profiles["7zip"].get("reason", "no 7-Zip report supplied"),
                "operations": {
                    operation: _operation_result(
                        case, operation, reason="no 7-Zip Adapter Check was run")
                    for operation in OPERATIONS
                },
            }

        for name, reader in readers.items():
            if reader["status"] == "fail":
                failures.append(
                    f"{case.get('caseKey', fixture_id)}: {name} Reader Adapter Profile failed")

        adapter_checks = _adapter_check_results(
            case, readers, seven_zip_fixtures)
        for check in adapter_checks:
            if check["status"] not in ("pass", "skipped", "execution-error"):
                failures.append(
                    f"{case.get('caseKey', fixture_id)}: "
                    f"{check['adapter']} Adapter Check {check['action']} failed")
                readers[check["adapter"]]["status"] = "fail"

        agreement = _agreement(readers)
        fixture_status = "fail" if any(
            reader["status"] == "fail" for reader in readers.values()) or any(
                check["status"] not in ("pass", "skipped", "execution-error")
                for check in adapter_checks) else "pass"
        report["fixtures"].append({
            "fixtureId": fixture_id,
            "caseKey": case.get("caseKey"),
            "status": fixture_status,
            "readers": readers,
            "adapterChecks": adapter_checks,
            "agreement": agreement,
        })

    missing_python = expected_ids - set(python_fixtures)
    if missing_python:
        raise ValueError("Python verifier report contains no results for fixture(s): "
                         + ", ".join(sorted(missing_python)))
    unexpected_python = set(python_fixtures) - expected_ids
    if unexpected_python:
        raise ValueError("Python verifier report contains unexpected Fixture ID(s): "
                         + ", ".join(sorted(unexpected_python)))
    if dotnet_report is not None:
        missing_dotnet = expected_ids - set(dotnet_fixtures)
        if missing_dotnet:
            raise ValueError(".NET reference report contains no results for fixture(s): "
                             + ", ".join(sorted(missing_dotnet)))
        unexpected_dotnet = set(dotnet_fixtures) - expected_ids
        if unexpected_dotnet:
            raise ValueError(".NET reference report contains unexpected Fixture ID(s)")
    if seven_zip_report is not None and set(seven_zip_fixtures) - expected_ids:
        raise ValueError("7-Zip report contains unexpected Fixture ID(s)")
    seven_zip_report_failures = {}
    if seven_zip_report is not None:
        for fixture_id, expected_checks in expected_seven_zip_checks.items():
            fixture_report = seven_zip_fixtures.get(fixture_id)
            if fixture_report is None:
                if not expected_checks:
                    continue
                message = f"7-Zip report is missing declared Adapter Checks for {fixture_id}"
                failures.append(
                    message)
                seven_zip_report_failures[fixture_id] = message
                continue
            checks = fixture_report.get("checks", [])
            reported_checks = [
                (check.get("action"), check.get("expectedOutcome"))
                for check in checks
            ]
            if (reported_checks != expected_checks
                    or any(check.get("status") != "pass"
                           or check.get("observedOutcome") != check.get("expectedOutcome")
                           for check in checks)):
                message = (
                    f"7-Zip report does not match declared Adapter Checks for {fixture_id}")
                failures.append(
                    message)
                seven_zip_report_failures[fixture_id] = message

    for fixture in report["fixtures"]:
        failure = seven_zip_report_failures.get(fixture["fixtureId"])
        if failure is not None:
            fixture["status"] = "fail"
            fixture["readers"]["7zip"]["status"] = "fail"
            fixture["readers"]["7zip"]["failure"] = failure

    profiles["python-zipfile"]["status"] = (
        "fail" if any(fixture["readers"]["python-zipfile"]["status"] == "fail"
                      for fixture in report["fixtures"]) else "pass")
    if dotnet_report is not None:
        profiles["dotnet-reference"]["status"] = (
            "fail" if any(fixture["readers"]["dotnet-reference"]["status"] == "fail"
                          for fixture in report["fixtures"]) else "pass")
        profiles["dotnet-reference"]["version"] = dotnet_report.get("adapterVersion")
        profiles["dotnet-reference"].pop("reason", None)
    if infozip is not None:
        profiles["infozip-unzip"]["status"] = (
            "fail" if any(
                fixture["readers"]["infozip-unzip"]["status"] == "fail"
                for fixture in report["fixtures"]) else "observed")
        profiles["infozip-unzip"]["executionErrors"] = sum(
            "failure" in operation
            for fixture in report["fixtures"]
            for operation in fixture["readers"]["infozip-unzip"]["operations"].values())
    if seven_zip_report is not None:
        profiles["7zip"]["status"] = (
            "fail" if seven_zip_report_failures or any(
                fixture["readers"]["7zip"]["status"] == "fail"
                          for fixture in report["fixtures"]) else "pass")
        profiles["7zip"]["version"] = seven_zip_report.get("adapterVersion")

    splits = sum(bool(fixture["agreement"]["splitOperations"])
                 for fixture in report["fixtures"])
    report["summary"] = {
        "fixtures": len(report["fixtures"]),
        "fixturesWithReaderSplits": splits,
        "readerAdapterProfileSkips": sum(
            profile["status"] == "skipped" for profile in profiles.values()),
        "readerExecutionErrors": sum(
            profile.get("executionErrors", 0) for profile in profiles.values()),
        "failedReaderAdapterProfiles": sum(
            profile["status"] == "fail" for profile in profiles.values()),
    }

    for profile_name, profile in profiles.items():
        if profile["status"] == "skipped":
            print(f"SKIP {profile_name}: {profile['reason']}")
        else:
            details = []
            if profile.get("version"):
                details.append(profile["version"])
            if profile.get("executionErrors"):
                details.append(f"{profile['executionErrors']} execution errors")
            suffix = f" ({'; '.join(details)})" if details else ""
            print(f"Reader Adapter Profile {profile_name}: {profile['status']}{suffix}")

    for fixture in report["fixtures"]:
        details = []
        for operation, result in fixture["agreement"]["operations"].items():
            if not result["outcomes"]:
                continue
            outcomes = ", ".join(
                f"{reader}={outcome}" for reader, outcome in result["outcomes"].items())
            details.append(f"{operation} {result['status']}: {outcomes}")
        summary = "; ".join(details) if details else "no comparable operations"
        print(f"agreement {fixture['caseKey']} ({fixture['fixtureId']}): {summary}")

    if failures:
        report["failures"] = failures
        for failure in failures:
            print(f"FAIL reader matrix: {failure}", file=sys.stderr)
    return report, 1 if failures else 0


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Report per-fixture agreement across available Archive Test Reader Adapter Profiles.")
    parser.add_argument("fixture_dir", help="directory containing Archive Test Fixture pairs")
    parser.add_argument("python_report", help="report produced by verify-fixtures.py")
    parser.add_argument("--unzip", default=None,
                        help="Info-ZIP executable (default: discover unzip on PATH)")
    parser.add_argument("--seven-zip-report", default=None,
                        help="optional structured report from verify-7zip-adapter.py")
    parser.add_argument("--dotnet-report-dir", default=None,
                        help="directory of per-fixture reports from ArchiveReferenceReaderTests")
    parser.add_argument("--report", default=None,
                        help="JSON output path (default: sibling reader-agreement.json)")
    args = parser.parse_args(argv)

    try:
        python_report = json.loads(Path(args.python_report).read_text(encoding="utf-8"))
        seven_zip_report = (
            json.loads(Path(args.seven_zip_report).read_text(encoding="utf-8"))
            if args.seven_zip_report else None)
        dotnet_report = None
        if args.dotnet_report_dir:
            dotnet_fixtures = [
                json.loads(path.read_text(encoding="utf-8"))
                for path in sorted(
                    Path(args.dotnet_report_dir).glob("dotnet-reference-*.json"))
            ]
            if not dotnet_fixtures:
                raise ValueError(
                    f".NET reference report directory is empty: {args.dotnet_report_dir}")
            if any(fixture.get("adapter") != "dotnet-reference"
                   for fixture in dotnet_fixtures):
                raise ValueError(".NET reference report contains an unknown Reader Adapter Profile")
            versions = {fixture.get("adapterVersion") for fixture in dotnet_fixtures}
            if len(versions) != 1:
                raise ValueError(".NET reference reports disagree on runtime version")
            dotnet_report = {
                "adapter": "dotnet-reference",
                "adapterVersion": next(iter(versions)),
                "fixtures": dotnet_fixtures,
            }
        executables = {
            "infozip-unzip": (
                args.unzip if args.unzip is not None else shutil.which("unzip")),
            "7zip": shutil.which("7zz") or shutil.which("7z"),
        }
        report, exit_code = verify_directory(
            args.fixture_dir, python_report, executables, seven_zip_report, dotnet_report)
        report_path = args.report or str(
            Path(args.fixture_dir).resolve().parent / "reader-agreement.json")
        Path(report_path).write_text(
            json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(f"Reader agreement report: {report_path}")
        return exit_code
    except (KeyError, OSError, TypeError, ValueError, json.JSONDecodeError) as error:
        print(f"Reader matrix failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
