#!/usr/bin/env python3
"""Contract tests for the optional Archive Test reader matrix."""

import contextlib
import csv
import hashlib
import io
import json
import os
import runpy
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import zipfile
from pathlib import Path


SCRIPT = Path(__file__).with_name("verify-reader-matrix.py")
MATRIX = runpy.run_path(str(SCRIPT))
FIXTURE_VERIFIER = runpy.run_path(str(SCRIPT.with_name("verify-fixtures.py")))


class ReaderMatrixTests(unittest.TestCase):
    def make_fixture(self, directory):
        fixture_id = "atc-" + "a" * 64
        archive = Path(directory) / (fixture_id + ".zip")
        content = b"reader matrix payload"
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED) as output:
            output.writestr("a.txt", content)

        archive_bytes = archive.read_bytes()
        archive_sha256 = hashlib.sha256(archive_bytes).hexdigest()
        case = {
            "generatorContractVersion": "1",
            "generatorVersion": "test",
            "caseKey": "valid-stored",
            "caseRevision": 1,
            "expectationRevision": 1,
            "seed": 42,
            "classification": "valid",
            "archive": {
                "fileName": fixture_id + ".zip",
                "physicalSize": len(archive_bytes),
                "sha256": archive_sha256,
            },
            "entries": [{
                "ordinal": 0,
                "kind": "file",
                "readableName": "a.txt",
                "contentSize": len(content),
                "contentSha256": hashlib.sha256(content).hexdigest(),
            }],
            "limits": {"entryCount": 1, "expandedBytesBudget": len(content)},
            "expectations": [
                {"operation": "list", "profile": "strict",
                 "allowedOutcomes": ["listed-count-matches-entries"]},
                {"operation": "read-entry", "profile": "strict",
                 "allowedOutcomes": ["read-entry-content-matches"]},
                {"operation": "integrity-check", "profile": "strict",
                 "allowedOutcomes": ["integrity-passes", "integrity-fails"]},
                {"operation": "extract", "profile": "strict",
                 "allowedOutcomes": ["extract-completes"]},
            ],
        }
        case["fixtureId"] = MATRIX["_fixture_id_from_case"](case, archive_sha256)
        fixture_id = case["fixtureId"]
        case["archive"]["fileName"] = fixture_id + ".zip"
        if fixture_id != "atc-" + "a" * 64:
            archive.rename(Path(directory) / (fixture_id + ".zip"))
            archive = Path(directory) / (fixture_id + ".zip")
        (Path(directory) / (fixture_id + ".json")).write_text(
            json.dumps(case), encoding="utf-8")

        python_report = {
            "adapter": "python-zipfile",
            "directoryProblems": [],
            "fixtures": [{
                "fixtureId": fixture_id,
                "caseKey": case["caseKey"],
                "expectationFileSha256": hashlib.sha256(
                    json.dumps(case).encode("utf-8")).hexdigest(),
                "archiveSha256": archive_sha256,
                "status": "pass",
                "operations": [
                    {"operation": "list", "normalizedOutcome": "list-succeeds", "status": "pass"},
                    {"operation": "read-entry", "normalizedOutcome": "read-entry-content-matches", "status": "pass"},
                    {"operation": "integrity-check", "normalizedOutcome": "integrity-passes", "status": "pass"},
                    {"operation": "extract", "normalizedOutcome": "extract-succeeds", "status": "pass"},
                ],
            }],
        }
        return case, python_report

    def persist_case(self, directory, case, python_report=None):
        directory = Path(directory)
        archive_path = directory / (case["fixtureId"] + ".zip")
        archive_bytes = archive_path.read_bytes()
        archive_sha256 = hashlib.sha256(archive_bytes).hexdigest()
        case.setdefault("archive", {})
        case["archive"].update(
            physicalSize=len(archive_bytes),
            sha256=archive_sha256,
        )
        old_fixture_id = case["fixtureId"]
        case["fixtureId"] = MATRIX["_fixture_id_from_case"](case, archive_sha256)
        case["archive"]["fileName"] = case["fixtureId"] + ".zip"
        if old_fixture_id != case["fixtureId"]:
            archive_path.rename(directory / (case["fixtureId"] + ".zip"))
            (directory / (old_fixture_id + ".json")).unlink(missing_ok=True)
        sidecar_bytes = json.dumps(case).encode("utf-8")
        (directory / (case["fixtureId"] + ".json")).write_bytes(sidecar_bytes)
        if python_report is not None:
            fixture = next(
                fixture for fixture in python_report["fixtures"]
                if fixture["fixtureId"] == old_fixture_id)
            fixture.update(
                fixtureId=case["fixtureId"],
                caseKey=case["caseKey"],
                expectationFileSha256=hashlib.sha256(sidecar_bytes).hexdigest(),
                archiveSha256=archive_sha256,
            )

    @staticmethod
    def make_dotnet_report(case):
        operations = [
            ("list", "list-succeeds"),
            ("read-entry", "read-entry-content-matches"),
            ("integrity-check", "integrity-passes"),
            ("extract", "extract-succeeds"),
        ]
        return {
            "adapter": "dotnet-reference",
            "adapterVersion": "10.0.0",
            "fixtures": [{
                "fixtureId": case["fixtureId"],
                "caseKey": case["caseKey"],
                "expectationFileSha256": hashlib.sha256(
                    json.dumps(case).encode("utf-8")).hexdigest(),
                "archiveSha256": case["archive"]["sha256"],
                "status": "pass",
                "operations": [
                    {"operation": operation, "normalizedOutcome": outcome, "status": "pass"}
                    for operation, outcome in operations
                ],
            }],
        }

    @staticmethod
    def process_is_running(process_id):
        if os.name == "nt":
            result = subprocess.run(
                ["tasklist", "/FI", f"PID eq {process_id}", "/FO", "CSV", "/NH"],
                capture_output=True, text=True, check=False)
            return any(
                len(row) > 1 and row[1] == str(process_id)
                for row in csv.reader(result.stdout.splitlines()))

        result = subprocess.run(
            ["ps", "-p", str(process_id), "-o", "stat="],
            capture_output=True, text=True, check=False)
        state = result.stdout.strip()
        return result.returncode == 0 and bool(state) and not state.startswith("Z")

    @staticmethod
    def isolated_fake_unzip_environment(**overrides):
        environment = {
            key: value for key, value in os.environ.items()
            if not key.startswith("FAKE_UNZIP_")
        }
        environment.update(overrides)
        return patch.dict(os.environ, environment, clear=True)

    @staticmethod
    def write_fake_unzip(directory):
        script = Path(directory) / "fake_unzip.py"
        script.write_text(
            """import os
import signal
import sys
import subprocess
import time
import zipfile

args = sys.argv[1:]
if args[0] in os.environ.get("FAKE_UNZIP_SIGNAL_ACTIONS", "").split(","):
    os.kill(os.getpid(), signal.SIGTERM)
if args[0] in os.environ.get("FAKE_UNZIP_TIMEOUT_ACTIONS", "").split(","):
    time.sleep(5)
if args[0] in os.environ.get("FAKE_UNZIP_FORK_ACTIONS", "").split(","):
    child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(5)"],
                             stdout=sys.stdout, stderr=sys.stderr)
    with open(os.environ["FAKE_UNZIP_CHILD_PID"], "w", encoding="utf-8") as pid_file:
        pid_file.write(str(child.pid))
    if os.environ.get("FAKE_UNZIP_EXIT_PARENT_AFTER_FORK") == "1":
        raise SystemExit(0)
    time.sleep(5)
if args[0] == "-v":
    if os.environ.get("FAKE_UNZIP_INVALID_VERSION") == "1":
        print("not an Info-ZIP reader", file=sys.stderr)
        raise SystemExit(2)
    if os.environ.get("FAKE_UNZIP_BAD_VERSION_BANNER") == "1":
        print("not an Info-ZIP reader")
        raise SystemExit(0)
    print("UnZip 6.00 fake Info-ZIP reader")
    raise SystemExit(0)
def no_archive():
    if (os.environ.get("FAKE_UNZIP_REMOVE_ARCHIVE") == "1"
            and os.path.isfile(args[1])):
        os.unlink(args[1])
    print("End-of-central-directory signature not found", file=sys.stderr)
    raise SystemExit(9)
if args[0] == "-Z1":
    if (os.environ.get("FAKE_UNZIP_NO_ARCHIVE") == "1"
            or os.environ.get("FAKE_UNZIP_REMOVE_ARCHIVE") == "1"
            or not os.path.isfile(args[1])):
        no_archive()
    if os.environ.get("FAKE_UNZIP_LIST_LOG"):
        with open(os.environ["FAKE_UNZIP_LIST_LOG"], "a", encoding="utf-8") as log:
            log.write("list\\n")
    with zipfile.ZipFile(args[1]) as archive:
        for info in archive.infolist():
            print(info.filename)
    raise SystemExit(1 if os.environ.get("FAKE_UNZIP_LIST_WARNING") == "1" else 0)
elif args[0] == "-t":
    if (os.environ.get("FAKE_UNZIP_NO_ARCHIVE") == "1"
            or os.environ.get("FAKE_UNZIP_REMOVE_ARCHIVE") == "1"
            or not os.path.isfile(args[1])):
        no_archive()
    if os.environ.get("FAKE_UNZIP_INTEGRITY_RESOURCE_ERROR") == "1":
        print("unzip: error: not enough memory", file=sys.stderr)
        raise SystemExit(4)
    if os.environ.get("FAKE_UNZIP_OVERLAPPED_ARCHIVE") == "1":
        print("invalid zip file with overlapped components", file=sys.stderr)
        raise SystemExit(12)
    if os.environ.get("FAKE_UNZIP_REJECT_TEST") == "1":
        print("bad archive", file=sys.stderr)
        raise SystemExit(1)
    with zipfile.ZipFile(args[1]) as archive:
        raise SystemExit(1 if archive.testzip() else 0)
elif args[0] == "-p":
    if (os.environ.get("FAKE_UNZIP_READ_NO_ARCHIVE") == "1"
            or not os.path.isfile(args[1])):
        no_archive()
    if os.environ.get("FAKE_UNZIP_NO_MATCH") == "1":
        print("caution: filename not matched", file=sys.stderr)
        raise SystemExit(11)
    if os.environ.get("FAKE_UNZIP_READ_LOG"):
        with open(os.environ["FAKE_UNZIP_READ_LOG"], "a", encoding="utf-8") as log:
            log.write("read\\n")
    with zipfile.ZipFile(args[1]) as archive:
        names = args[2:]
        infos = archive.infolist()
        for info in infos:
            if not info.is_dir() and (not names or info.filename in names):
                sys.stdout.buffer.write(archive.read(info))
    raise SystemExit(1 if os.environ.get("FAKE_UNZIP_READ_WARNING") == "1" else 0)
else:
    raise SystemExit(2)
""",
            encoding="utf-8",
        )
        return [sys.executable, str(script)]

    def test_registry_ContainsRequiredAndOptionalReaderAdapterProfiles(self):
        profiles = MATRIX["READER_ADAPTER_PROFILES"]

        self.assertIn("dotnet-reference", profiles)
        self.assertIn("python-zipfile", profiles)
        self.assertIn("infozip-unzip", profiles)
        self.assertIn("7zip", profiles)
        self.assertFalse(profiles["dotnet-reference"].optional)
        self.assertFalse(profiles["python-zipfile"].optional)
        self.assertTrue(profiles["infozip-unzip"].optional)
        self.assertTrue(profiles["7zip"].optional)
        self.assertEqual(
            ("list", "read-entry", "integrity-check", "extract"),
            tuple(action for action, _ in profiles["python-zipfile"].adapter_check_outcomes))
        self.assertEqual(
            ("list", "read-entry", "integrity-check"),
            tuple(action for action, _ in profiles["infozip-unzip"].adapter_check_outcomes))
        self.assertEqual(("7zz", "7z"), profiles["7zip"].executables)
        self.assertEqual({
            "extract": ("success",),
            "test": ("failure",),
            "extract-entries": ("success",),
            "presence": ("present",),
        }, dict(profiles["7zip"].adapter_check_outcomes))

    def test_registry_MatchesFixtureSchemaAndStandaloneVerifier(self):
        schema_path = Path(__file__).parents[1] / "fixtures" / "archive-test-case.schema.json"
        schema = json.loads(schema_path.read_text(encoding="utf-8"))
        schema_profiles = {}
        for branch in schema["properties"]["adapterChecks"]["items"]["oneOf"]:
            properties = branch["properties"]
            adapter = properties["adapter"]["const"]
            action = properties["action"]["const"]
            outcome = properties["expectedOutcome"]
            outcomes = outcome.get("enum", [outcome.get("const")])
            schema_profiles.setdefault(adapter, {})[action] = set(outcomes)

        matrix_profiles = {
            adapter: {
                action: set(outcomes)
                for action, outcomes in profile.adapter_check_outcomes
            }
            for adapter, profile in MATRIX["READER_ADAPTER_PROFILES"].items()
        }
        verifier_profiles = FIXTURE_VERIFIER["ADAPTER_CHECK_OUTCOMES"]
        self.assertEqual(schema_profiles, matrix_profiles)
        self.assertEqual(schema_profiles, verifier_profiles)

    def test_runUnzipMember_ZeroByteLimit_ReadsOnlyOverflowSentinel(self):
        class Stdout:
            def __init__(self):
                self.requested_sizes = []

            def read(self, size):
                self.requested_sizes.append(size)
                return b"x" * size

            def close(self):
                pass

        class Process:
            def __init__(self):
                self.stdout = Stdout()
                self.killed = False

            def poll(self):
                return 1 if self.killed else None

            def kill(self):
                self.killed = True

            def wait(self, timeout):
                return 1

        class Timer:
            def __init__(self, *_args):
                pass

            def start(self):
                pass

            def cancel(self):
                pass

        process = Process()
        with patch.object(MATRIX["subprocess"], "Popen", return_value=process):
            with patch.object(MATRIX["threading"], "Timer", Timer):
                return_code, size, _, over_budget = MATRIX["_run_unzip_member"](
                    "unzip", Path("archive.zip"), "a.txt", 0, 1)

        self.assertEqual(1, return_code)
        self.assertEqual(1, size)
        self.assertEqual([1], process.stdout.requested_sizes)
        self.assertTrue(over_budget)

    def test_isolatedFakeUnzipEnvironment_PreservesSystemVariablesAndClearsOverrides(self):
        with patch.dict(os.environ, {
            "PATH": "fake-path",
            "SYSTEMROOT": "fake-system-root",
            "FAKE_UNZIP_STALE": "1",
        }, clear=True):
            with self.isolated_fake_unzip_environment(FAKE_UNZIP_CURRENT="1"):
                self.assertEqual("fake-path", os.environ["PATH"])
                if os.name == "nt":
                    self.assertEqual("fake-system-root", os.environ["SYSTEMROOT"])
                self.assertNotIn("FAKE_UNZIP_STALE", os.environ)
                self.assertEqual("1", os.environ["FAKE_UNZIP_CURRENT"])

    def test_unzipReadEntries_ManySmallMembers_UsesOneProcessAndChecksEachHash(self):
        with tempfile.TemporaryDirectory() as temp:
            archive_path = Path(temp) / "many-small.zip"
            entries = []
            with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_STORED) as archive:
                for index in range(1000):
                    name = f"small-{index:04d}.txt"
                    content = bytes([index % 256]) * (index % 7)
                    archive.writestr(name, content)
                    entries.append({
                        "kind": "file",
                        "ordinal": index,
                        "readableName": name,
                        "contentSize": len(content),
                        "contentSha256": hashlib.sha256(content).hexdigest(),
                        "localHeaderMethod": 0,
                        "centralDirectoryMethod": 0,
                    })
            log_path = Path(temp) / "read-calls.txt"
            case = {
                "caseKey": "many-small-entries",
                "entries": list(reversed(entries)),
                "limits": {"expandedBytesBudget": sum(entry["contentSize"] for entry in entries)},
            }
            command = self.write_fake_unzip(temp)

            with patch.dict(os.environ, {"FAKE_UNZIP_READ_LOG": str(log_path)}):
                outcome = MATRIX["_unzip_read_entries"](
                    command, archive_path, case, time.monotonic() + 5, {0, 8})
                read_calls = log_path.read_text(encoding="utf-8").splitlines()

        self.assertEqual(("read-entry-content-matches", None), outcome)
        self.assertEqual(["read"], read_calls)

    def test_unzipReadEntries_EmptyArchive_StillProbesReader(self):
        with tempfile.TemporaryDirectory() as temp:
            archive_path = Path(temp) / "empty.zip"
            with zipfile.ZipFile(archive_path, "w"):
                pass
            log_path = Path(temp) / "list-calls.txt"
            case = {
                "caseKey": "valid-empty",
                "entries": [],
                "limits": {"entryCount": 0, "expandedBytesBudget": 0},
            }
            command = self.write_fake_unzip(temp)

            with patch.dict(os.environ, {"FAKE_UNZIP_LIST_LOG": str(log_path)}):
                outcome = MATRIX["_unzip_read_entries"](
                    command, archive_path, case, time.monotonic() + 5, {0, 8})
                read_calls = (
                    log_path.read_text(encoding="utf-8").splitlines()
                    if log_path.exists() else [])

        self.assertEqual(("read-entry-content-matches", None), outcome)
        self.assertEqual(["list"], read_calls)

    def test_unzipReadEntries_EmptyArchiveRejected_ReportsReadFailure(self):
        with tempfile.TemporaryDirectory() as temp:
            archive_path = Path(temp) / "empty.zip"
            with zipfile.ZipFile(archive_path, "w"):
                pass
            case = {
                "caseKey": "malformed-empty",
                "entries": [],
                "limits": {"entryCount": 0, "expandedBytesBudget": 0},
            }
            command = self.write_fake_unzip(temp)

            with patch.dict(os.environ, {"FAKE_UNZIP_NO_ARCHIVE": "1"}):
                outcome = MATRIX["_unzip_read_entries"](
                    command, archive_path, case, time.monotonic() + 5, {0, 8})

        self.assertEqual(("read-entry-fails", None), outcome)

    def test_unzipReadEntries_EmptyArchiveExpiredDeadline_ReportsExecutionError(self):
        with tempfile.TemporaryDirectory() as temp:
            archive_path = Path(temp) / "empty.zip"
            with zipfile.ZipFile(archive_path, "w"):
                pass
            command = self.write_fake_unzip(temp)
            case = {
                "caseKey": "valid-empty",
                "entries": [],
                "limits": {"entryCount": 0, "expandedBytesBudget": 0},
            }

            with self.assertRaisesRegex(
                MATRIX["ReaderExecutionError"],
                "Info-ZIP fixture deadline exceeded reading entries",
            ):
                MATRIX["_unzip_read_entries"](
                    command, archive_path, case, time.monotonic() - 1, {0, 8})

    def test_validateAdapterChecks_RejectsUndeclaredProfileOutcomes(self):
        case = {
            "caseKey": "valid-stored",
            "classification": "valid",
            "entries": [{"kind": "file", "readableName": "a.txt"}],
            "adapterChecks": [
                {"adapter": "7zip", "action": "extract-entries",
                 "expectedOutcome": "success", "entryNames": ["a.txt"]},
            ],
        }
        MATRIX["_validate_adapter_checks"](case)

        case["adapterChecks"] = [
            {"adapter": "infozip-unzip", "action": "extract", "expectedOutcome": "success"},
        ]
        with self.assertRaisesRegex(ValueError, "invalid Adapter Check"):
            MATRIX["_validate_adapter_checks"](case)

        case["adapterChecks"] = [
            {"adapter": "python-zipfile", "action": "read-entry",
             "expectedOutcome": "read-entry-content-matches"},
            {"adapter": "dotnet-reference", "action": "integrity-check",
             "expectedOutcome": "integrity-passes"},
            {"adapter": "infozip-unzip", "action": "list",
             "expectedOutcome": "list-succeeds"},
        ]
        MATRIX["_validate_adapter_checks"](case)

    def test_validateAdapterChecks_RejectsReferenceExtractForNonValidFixtures(self):
        for classification in ("malformed", "policy-sensitive"):
            for adapter in ("dotnet-reference", "python-zipfile"):
                with self.subTest(classification=classification, adapter=adapter):
                    case = {
                        "caseKey": "fixture",
                        "classification": classification,
                        "entries": [{"kind": "file", "readableName": "a.txt"}],
                        "adapterChecks": [
                            {"adapter": adapter, "action": "extract",
                             "expectedOutcome": "extract-succeeds"},
                        ],
                    }
                    with self.assertRaisesRegex(
                        ValueError,
                        "extract Adapter Check requires a valid fixture",
                    ):
                        MATRIX["_validate_adapter_checks"](case)

    def test_verifyDirectory_MissingInfoZip_RecordsDocumentedSkip(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "list",
                 "expectedOutcome": "list-succeeds"},
                {"adapter": "7zip", "action": "extract",
                 "expectedOutcome": "success"},
            ]
            self.persist_case(temp, case, python_report)
            stdout = io.StringIO()
            with contextlib.redirect_stdout(stdout):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        self.assertEqual(0, exit_code)
        self.assertEqual("pass", report["profiles"]["dotnet-reference"]["status"])
        self.assertEqual("skipped", report["profiles"]["infozip-unzip"]["status"])
        self.assertEqual("skipped", report["profiles"]["7zip"]["status"])
        checks = report["fixtures"][0]["adapterChecks"]
        self.assertEqual(["skipped", "skipped"], [check["status"] for check in checks])
        self.assertIn("unzip", checks[0]["reason"])
        self.assertIn("7-Zip", checks[1]["reason"])
        self.assertIn("unzip", report["profiles"]["infozip-unzip"]["reason"])
        self.assertIn("7-Zip", report["profiles"]["7zip"]["reason"])
        self.assertEqual(2, report["summary"]["readerAdapterProfileSkips"])
        self.assertIn("SKIP infozip-unzip", stdout.getvalue())
        self.assertEqual(1, stdout.getvalue().count("infozip-unzip"))
        self.assertEqual(1, stdout.getvalue().count("7zip"))
        self.assertIn("agreement valid-stored (", stdout.getvalue())

    def test_verifyDirectory_InvalidInfoZipVersionProbe_DoesNotPassAdapterCheck(self):
        scenarios = (
            ("nonzero exit", {"FAKE_UNZIP_INVALID_VERSION": "1"},
             "Info-ZIP version probe exited with code 2"),
            ("unrecognized banner", {"FAKE_UNZIP_BAD_VERSION_BANNER": "1"},
             "Info-ZIP version probe did not identify"),
        )
        for scenario, environment, reason in scenarios:
            with self.subTest(scenario=scenario), tempfile.TemporaryDirectory() as temp:
                case, python_report = self.make_fixture(temp)
                case["adapterChecks"] = [
                    {"adapter": "infozip-unzip", "action": "list",
                     "expectedOutcome": "list-succeeds"},
                ]
                self.persist_case(temp, case, python_report)
                with patch.dict(os.environ, environment):
                    report, exit_code = MATRIX["verify_directory"](
                        Path(temp), python_report,
                        {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                        dotnet_report=self.make_dotnet_report(case))

                check = report["fixtures"][0]["adapterChecks"][0]
                self.assertEqual(0, exit_code)
                self.assertEqual("execution-error", check["status"])
                self.assertIn(reason, check["reason"])
                self.assertEqual("observed", report["profiles"]["infozip-unzip"]["status"])
                self.assertEqual(3, report["summary"]["readerExecutionErrors"])
                self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])

    def test_verifyDirectory_DeclaredChecks_RunAgainstNamedReaderProfiles(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "python-zipfile", "action": "read-entry",
                 "expectedOutcome": "read-entry-content-matches"},
                {"adapter": "dotnet-reference", "action": "integrity-check",
                 "expectedOutcome": "integrity-passes"},
                {"adapter": "infozip-unzip", "action": "list",
                 "expectedOutcome": "list-succeeds"},
            ]
            self.persist_case(temp, case, python_report)
            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                dotnet_report=self.make_dotnet_report(case))

        checks = report["fixtures"][0]["adapterChecks"]
        self.assertEqual(0, exit_code)
        self.assertEqual(
            ["python-zipfile", "dotnet-reference", "infozip-unzip"],
            [check["adapter"] for check in checks])
        self.assertEqual(["pass", "pass", "pass"], [check["status"] for check in checks])
        self.assertEqual(
            ["read-entry-content-matches", "integrity-passes", "list-succeeds"],
            [check["observedOutcome"] for check in checks])

    def test_verifyDirectory_PythonAdapterCheckMismatch_FailsProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "python-zipfile", "action": "list",
                 "expectedOutcome": "list-fails"},
            ]
            self.persist_case(temp, case, python_report)

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=self.make_dotnet_report(case))

        check = report["fixtures"][0]["adapterChecks"][0]
        self.assertEqual(1, exit_code)
        self.assertEqual("list-succeeds", check["observedOutcome"])
        self.assertEqual("fail", check["status"])
        self.assertEqual("fail", report["profiles"]["python-zipfile"]["status"])
        self.assertEqual(1, report["summary"]["failedReaderAdapterProfiles"])

    def test_verifyDirectory_DeclaredCheckMismatch_FailsNamedProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "integrity-check",
                 "expectedOutcome": "integrity-fails"},
            ]
            self.persist_case(temp, case, python_report)
            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                dotnet_report=self.make_dotnet_report(case))

        check = report["fixtures"][0]["adapterChecks"][0]
        self.assertEqual(1, exit_code)
        self.assertEqual("integrity-passes", check["observedOutcome"])
        self.assertEqual("fail", check["status"])
        self.assertEqual("fail", report["profiles"]["infozip-unzip"]["status"])
        self.assertEqual(1, report["summary"]["failedReaderAdapterProfiles"])

    def test_verifyDirectory_MissingDotNetReport_FailsRequiredProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            _, python_report = self.make_fixture(temp)

            with self.assertRaisesRegex(ValueError, "reference harness report is required"):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None})

    def test_verifyDirectory_PlatformScopedFixture_IsDocumentedSkip(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            for expectation in case["expectations"]:
                expectation["platform"] = "adls-gen2"
            self.persist_case(temp, case, python_report)

            not_run_operations = [
                {"operation": operation, "normalizedOutcome": "not-run",
                 "status": "not-run", "reason": "no applicable expectation"}
                for operation in MATRIX["OPERATIONS"]
            ]
            python_fixture = python_report["fixtures"][0]
            python_fixture.update(
                status="skipped",
                skipReason="no operation expectations apply on this host",
                operations=not_run_operations)
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0].update(
                status="skipped",
                skipReason="no operation expectations apply on this host",
                operations=not_run_operations)
            stdout = io.StringIO()
            with patch.dict(os.environ, {"ZIPPER_ATC_PLATFORMS": ""}):
                with contextlib.redirect_stdout(stdout):
                    report, exit_code = MATRIX["verify_directory"](
                        Path(temp), python_report,
                        {"infozip-unzip": None, "7zip": None},
                        dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual("skipped", fixture["status"])
        self.assertIn("adls-gen2", fixture["skipReason"])
        self.assertEqual("skipped", fixture["readers"]["python-zipfile"]["status"])
        self.assertEqual("skipped", fixture["readers"]["dotnet-reference"]["status"])
        self.assertEqual("skipped", report["profiles"]["python-zipfile"]["status"])
        self.assertEqual("skipped", report["profiles"]["dotnet-reference"]["status"])
        self.assertEqual(1, report["summary"]["fixturesSkipped"])
        self.assertEqual(4, report["summary"]["readerAdapterProfileSkips"])
        self.assertEqual(0, report["summary"]["failedReaderAdapterProfiles"])
        self.assertIn("SKIP fixture valid-stored", stdout.getvalue())

    def test_verifyDirectory_PlatformScopedAdapterChecks_RunIndependently(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            for expectation in case["expectations"]:
                expectation["platform"] = "adls-gen2"
            case["adapterChecks"] = [
                {"adapter": "python-zipfile", "action": "list",
                 "expectedOutcome": "list-succeeds"},
                {"adapter": "dotnet-reference", "action": "read-entry",
                 "expectedOutcome": "read-entry-content-matches"},
            ]
            self.persist_case(temp, case, python_report)

            not_run_operations = [
                {"operation": operation, "normalizedOutcome": "not-run",
                 "status": "not-run", "reason": "no applicable expectation"}
                for operation in MATRIX["OPERATIONS"]
            ]
            python_fixture = python_report["fixtures"][0]
            python_fixture.update(
                status="skipped",
                skipReason="no operation expectations apply on this host",
                operations=not_run_operations,
                adapterCheckOutcomes={"list": "list-succeeds"})
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0].update(
                status="skipped",
                skipReason="no operation expectations apply on this host",
                operations=not_run_operations,
                adapterCheckOutcomes={"read-entry": "read-entry-content-matches"})

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual("skipped", fixture["status"])
        self.assertEqual(
            ["pass", "pass"],
            [check["status"] for check in fixture["adapterChecks"]])
        self.assertEqual(
            ["list-succeeds", "read-entry-content-matches"],
            [check["observedOutcome"] for check in fixture["adapterChecks"]])

    def test_verifyDirectory_PlatformScopedAdapterCheck_RunsWhenOtherExpectationsApply(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["expectations"][-1]["platform"] = "adls-gen2"
            case["adapterChecks"] = [
                {"adapter": adapter, "action": "extract",
                 "expectedOutcome": "extract-succeeds"}
                for adapter in ("python-zipfile", "dotnet-reference")
            ]
            self.persist_case(temp, case, python_report)
            dotnet_report = self.make_dotnet_report(case)

            reports = (
                python_report["fixtures"][0],
                dotnet_report["fixtures"][0],
            )
            for reader_report in reports:
                reader_report["operations"][-1].update(
                    normalizedOutcome="not-run", status="not-run")
                reader_report["adapterCheckOutcomes"] = {
                    "extract": "extract-succeeds",
                }

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual("pass", fixture["status"])
        self.assertEqual(
            ["pass", "pass"],
            [check["status"] for check in fixture["adapterChecks"]])
        self.assertEqual(
            ["extract-succeeds", "extract-succeeds"],
            [check["observedOutcome"] for check in fixture["adapterChecks"]])

    def test_verifyDirectory_WrongPythonReportProfile_Fails(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_report["adapter"] = "dotnet-reference"

            with self.assertRaisesRegex(ValueError, "unknown Reader Adapter Profile"):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

    def test_verifyDirectory_PythonReportWithDirectoryProblems_Fails(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_report["directoryProblems"] = ["Archive has no Expectation File"]

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=self.make_dotnet_report(case))

        self.assertEqual(1, exit_code)
        self.assertIn(
            "Python verifier reported directory problem: Archive has no Expectation File",
            report["failures"])
        self.assertEqual("fail", report["profiles"]["python-zipfile"]["status"])
        self.assertEqual(1, report["summary"]["failedReaderAdapterProfiles"])

    def test_verifyDirectory_PythonReportMissingDirectoryProblems_IsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            del python_report["directoryProblems"]

            with self.assertRaisesRegex(ValueError, "directoryProblems"):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

    def test_readBoundedJson_ExceedingRemainingBudget_IsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            report_path = Path(temp) / "report.json"
            report_path.write_bytes(b"{} ")

            with self.assertRaisesRegex(ValueError, "report inputs exceed"):
                MATRIX["_read_bounded_json"](report_path, 2)

    def test_verifyDirectory_PythonReportWithWrongCaseKey_IsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_report["fixtures"][0]["caseKey"] = "different-case"

            with self.assertRaisesRegex(ValueError, "Python verifier report Case Key"):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

    def test_verifyDirectory_StaleReaderReportsAreRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["expectations"][0]["allowedOutcomes"] = ["list-fails"]
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")

            with self.assertRaisesRegex(
                ValueError,
                "Python verifier report Expectation File hash does not match",
            ):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

    def test_verifyDirectory_StaleDotNetReportIsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0]["expectationFileSha256"] = "0" * 64

            with self.assertRaisesRegex(
                ValueError,
                "reference harness report Expectation File hash does not match",
            ):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=dotnet_report)

    def test_verifyDirectory_ReaderReportWithWrongArchiveHash_IsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_report["fixtures"][0]["archiveSha256"] = "0" * 64

            with self.assertRaisesRegex(
                ValueError,
                "Python verifier report Archive hash does not match current input",
            ):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

    def test_verifyDirectory_StaleSevenZipReportIsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
            ]
            self.persist_case(temp, case, python_report)
            seven_zip_report = {
                "adapter": "7zip",
                "fixtures": [{
                    "fixtureId": case["fixtureId"],
                    "caseKey": case["caseKey"],
                    "expectationFileSha256": "0" * 64,
                    "archiveSha256": case["archive"]["sha256"],
                    "status": "pass",
                    "checks": [],
                    "operations": [],
                }],
            }

            with self.assertRaisesRegex(
                ValueError,
                "7-Zip report Expectation File hash does not match",
            ):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case),
                    seven_zip_report=seven_zip_report)

    def test_verifyDirectory_ArchiveBytesMustMatchExpectationIdentity(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            archive = Path(temp) / (case["fixtureId"] + ".zip")
            archive_bytes = archive.read_bytes()
            archive.write_bytes(archive_bytes[:-1] + bytes([archive_bytes[-1] ^ 1]))

            with self.assertRaisesRegex(
                ValueError,
                "Archive hash does not match Expectation File",
            ):
                MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

    def test_verifyDirectory_DotNetHarnessReport_RecordsRealOutcomes(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=self.make_dotnet_report(case))

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual("pass", report["profiles"]["dotnet-reference"]["status"])
        self.assertEqual("10.0.0", report["profiles"]["dotnet-reference"]["version"])
        self.assertEqual(
            "read-entry-content-matches",
            fixture["readers"]["dotnet-reference"]["operations"]["read-entry"]["normalizedOutcome"])

    def test_verifyDirectory_AdapterCheckCanonicalizesEquivalentOutcomeAlias(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "dotnet-reference", "action": "list",
                 "expectedOutcome": "list-succeeds"},
            ]
            self.persist_case(temp, case, python_report)
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0]["operations"][0][
                "normalizedOutcome"] = "listed-count-matches-entries"

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual(
            "list-succeeds",
            fixture["readers"]["dotnet-reference"]["operations"]["list"]["normalizedOutcome"])
        self.assertEqual("pass", fixture["adapterChecks"][0]["status"])
        self.assertEqual("agree", fixture["agreement"]["operations"]["list"]["status"])

    def test_verifyDirectory_DotNetAdapterCheckMismatch_FailsProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "dotnet-reference", "action": "list",
                 "expectedOutcome": "list-fails"},
            ]
            self.persist_case(temp, case, python_report)
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0]["adapterCheckOutcomes"] = {
                "list": "list-succeeds",
            }

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(1, exit_code)
        self.assertEqual("fail", fixture["adapterChecks"][0]["status"])
        self.assertEqual("list-succeeds", fixture["adapterChecks"][0]["observedOutcome"])
        self.assertEqual("fail", fixture["readers"]["dotnet-reference"]["status"])
        self.assertEqual("fail", report["profiles"]["dotnet-reference"]["status"])
        self.assertEqual("fail", fixture["status"])

    def test_verifyDirectory_DotNetOutcomeAllowedBySecondaryReaderProfile_Passes(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["expectations"].append({
                "operation": "list",
                "profile": "common",
                "allowedOutcomes": ["list-fails"],
                "invariants": [],
            })
            self.persist_case(temp, case, python_report)
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0]["operations"][0][
                "normalizedOutcome"] = "list-fails"

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        dotnet_list = fixture["readers"]["dotnet-reference"]["operations"]["list"]
        self.assertEqual(0, exit_code)
        self.assertEqual("pass", dotnet_list["status"])
        self.assertEqual("common", dotnet_list["expectationProfile"])
        self.assertEqual("split", fixture["agreement"]["operations"]["list"]["status"])

    def test_main_WritesPerFixtureAgreementReport(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_report_path = Path(temp) / "python-report.json"
            python_report_path.write_text(json.dumps(python_report), encoding="utf-8")
            dotnet_report_dir = Path(temp) / "dotnet-report"
            dotnet_report_dir.mkdir()
            dotnet_fixture = self.make_dotnet_report(case)["fixtures"][0]
            (dotnet_report_dir / f"dotnet-reference-{case['fixtureId']}.json").write_text(
                json.dumps({
                    **dotnet_fixture,
                    "adapter": "dotnet-reference",
                    "adapterVersion": "10.0.0",
                }),
                encoding="utf-8")
            report_path = Path(temp) / "reader-agreement.json"
            stdout = io.StringIO()

            with patch.object(MATRIX["shutil"], "which", return_value=None):
                with contextlib.redirect_stdout(stdout):
                    exit_code = MATRIX["main"]([
                        str(Path(temp)),
                        str(python_report_path),
                        "--dotnet-report-dir", str(dotnet_report_dir),
                        "--report", str(report_path),
                    ])

            saved_report = json.loads(report_path.read_text(encoding="utf-8"))

        self.assertEqual(0, exit_code)
        self.assertEqual(1, saved_report["summary"]["fixtures"])
        self.assertEqual(case["fixtureId"], saved_report["fixtures"][0]["fixtureId"])
        self.assertEqual(
            "agree",
            saved_report["fixtures"][0]["agreement"]["operations"]["list"]["status"])
        self.assertIn(f"agreement {case['caseKey']} ({case['fixtureId']}):", stdout.getvalue())

    def test_pythonReader_MissingApplicableOperation_FailsProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_fixture = python_report["fixtures"][0]
            python_fixture["operations"] = [
                operation for operation in python_fixture["operations"]
                if operation["operation"] != "list"
            ]

            reader = MATRIX["_reported_reader"](case, python_fixture, "Python verifier")

        self.assertEqual("fail", reader["status"])
        self.assertEqual(
            "fail",
            reader["operations"]["list"]["status"])

    def test_pythonReader_FailedOperationCannotPassOnAllowedOutcome(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_fixture = python_report["fixtures"][0]
            python_fixture["operations"][0]["status"] = "fail"

            reader = MATRIX["_reported_reader"](case, python_fixture, "Python verifier")

        self.assertEqual("fail", reader["status"])
        self.assertEqual(
            "fail",
            reader["operations"]["list"]["status"])

    def test_pythonReader_SkippedWithApplicableExpectations_FailsProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_fixture = python_report["fixtures"][0]
            python_fixture.update(
                status="skipped",
                skipReason="no operation expectations apply on this host")

            reader = MATRIX["_reported_reader"](case, python_fixture, "Python verifier")

        self.assertEqual("fail", reader["status"])

    def test_pythonReader_UnexpectedOutcome_IsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_fixture = python_report["fixtures"][0]
            python_fixture["operations"][0]["normalizedOutcome"] = "list-fails"

            reader = MATRIX["_reported_reader"](case, python_fixture, "Python verifier")

        self.assertEqual("fail", reader["status"])
        self.assertEqual("fail", reader["operations"]["list"]["status"])
        self.assertEqual("list-fails", reader["operations"]["list"]["normalizedOutcome"])

    def test_pythonReader_DuplicateOperationReport_IsRejected(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            operations = python_report["fixtures"][0]["operations"]
            operations.append({
                "operation": "list",
                "normalizedOutcome": "list-succeeds",
                "status": "pass",
            })

            with self.assertRaisesRegex(ValueError, "duplicate operation 'list'"):
                MATRIX["_reported_reader"](
                    case, python_report["fixtures"][0], "Python verifier")

    def test_recordedReader_MissingApplicableOperation_FailsProfile(self):
        case = {
            "expectations": [
                {"operation": operation, "profile": "strict",
                 "allowedOutcomes": [outcome]}
                for operation, outcome in (
                    ("list", "list-succeeds"),
                    ("read-entry", "read-entry-content-matches"),
                    ("integrity-check", "integrity-passes"),
                    ("extract", "extract-succeeds"),
                )
            ],
            "classification": "valid",
        }
        reader_fixture = {
            "status": "pass",
            "operations": [
                {"operation": "read-entry",
                 "normalizedOutcome": "read-entry-content-matches", "status": "pass"},
                {"operation": "integrity-check",
                 "normalizedOutcome": "integrity-passes", "status": "pass"},
                {"operation": "extract",
                 "normalizedOutcome": "extract-succeeds", "status": "pass"},
            ],
        }

        reader = MATRIX["_reported_reader"](case, reader_fixture, "reference harness")

        self.assertEqual("fail", reader["status"])
        self.assertEqual("fail", reader["operations"]["list"]["status"])

    def test_verifyDirectory_MissingOperation_IsExcludedFromAgreement(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0]["operations"] = [
                operation for operation in dotnet_report["fixtures"][0]["operations"]
                if operation["operation"] != "list"
            ]

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(1, exit_code)
        self.assertEqual("fail", fixture["readers"]["dotnet-reference"]["status"])
        self.assertEqual(
            "not-run",
            fixture["readers"]["dotnet-reference"]["operations"]["list"]["normalizedOutcome"])
        self.assertEqual(
            "single-reader",
            fixture["agreement"]["operations"]["list"]["status"])
        self.assertEqual(
            {"python-zipfile": "list-succeeds"},
            fixture["agreement"]["operations"]["list"]["outcomes"])

    def test_verifyDirectory_MissingSevenZipAdapterCheck_MarksProfileFailed(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
            ]
            self.persist_case(temp, case, python_report)

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=self.make_dotnet_report(case),
                seven_zip_report={"adapter": "7zip", "fixtures": []})

        self.assertEqual(1, exit_code)
        self.assertEqual("fail", report["profiles"]["7zip"]["status"])
        self.assertEqual(1, report["summary"]["failedReaderAdapterProfiles"])

    def test_verifyDirectory_FailedSevenZipAdapterCheck_FailsProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
            ]
            self.persist_case(temp, case, python_report)
            seven_zip_report = {
                "adapter": "7zip",
                "fixtures": [{
                    "fixtureId": case["fixtureId"],
                    "caseKey": case["caseKey"],
                    "expectationFileSha256": hashlib.sha256(
                        json.dumps(case).encode("utf-8")).hexdigest(),
                    "archiveSha256": case["archive"]["sha256"],
                    "status": "pass",
                    "checks": [{
                        "action": "extract",
                        "expectedOutcome": "success",
                        "observedOutcome": "failure",
                        "status": "fail",
                    }],
                    "operations": [{
                        "operation": "extract",
                        "normalizedOutcome": "extract-succeeds",
                        "status": "pass",
                    }],
                }],
            }

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=self.make_dotnet_report(case),
                seven_zip_report=seven_zip_report)

        self.assertEqual(1, exit_code)
        self.assertEqual("fail", report["profiles"]["7zip"]["status"])
        self.assertEqual("fail", report["fixtures"][0]["status"])

    def test_verifyDirectory_SevenZipTimeoutReport_FailsMatrix(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "7zip", "action": "presence", "expectedOutcome": "present"},
                {"adapter": "7zip", "action": "presence", "expectedOutcome": "present"},
            ]
            self.persist_case(temp, case, python_report)
            timeout_reason = "7-Zip fixture check deadline exceeded for valid-stored"
            seven_zip_report = {
                "adapter": "7zip",
                "adapterVersion": "7-Zip test",
                "fixtures": [{
                    "fixtureId": case["fixtureId"],
                    "caseKey": case["caseKey"],
                    "expectationFileSha256": hashlib.sha256(
                        json.dumps(case).encode("utf-8")).hexdigest(),
                    "archiveSha256": case["archive"]["sha256"],
                    "status": "fail",
                    "checks": [
                        {
                            "action": "presence",
                            "expectedOutcome": "present",
                            "observedOutcome": "present",
                            "status": "pass",
                        },
                        {
                            "action": "presence",
                            "expectedOutcome": "present",
                            "observedOutcome": None,
                            "status": "fail",
                            "failure": timeout_reason,
                        },
                    ],
                    "operations": [],
                }],
            }

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=self.make_dotnet_report(case),
                seven_zip_report=seven_zip_report)

        self.assertEqual(1, exit_code)
        self.assertEqual("fail", report["profiles"]["7zip"]["status"])
        self.assertEqual(1, report["summary"]["failedReaderAdapterProfiles"])
        self.assertEqual("pass", report["fixtures"][0]["adapterChecks"][0]["status"])
        timeout_check = report["fixtures"][0]["adapterChecks"][1]
        self.assertEqual("fail", timeout_check["status"])
        self.assertEqual(timeout_reason, timeout_check["reason"])

    def test_verifyDirectory_PassingSevenZipAdapterCheck_PassesProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
            ]
            self.persist_case(temp, case, python_report)
            seven_zip_report = {
                "adapter": "7zip",
                "adapterVersion": "7-Zip test",
                "fixtures": [{
                    "fixtureId": case["fixtureId"],
                    "caseKey": case["caseKey"],
                    "expectationFileSha256": hashlib.sha256(
                        json.dumps(case).encode("utf-8")).hexdigest(),
                    "archiveSha256": case["archive"]["sha256"],
                    "status": "pass",
                    "checks": [{
                        "action": "extract",
                        "expectedOutcome": "success",
                        "observedOutcome": "success",
                        "status": "pass",
                    }],
                    "operations": [{
                        "operation": "extract",
                        "normalizedOutcome": "extract-succeeds",
                        "status": "pass",
                    }],
                }],
            }

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": None, "7zip": None},
                dotnet_report=self.make_dotnet_report(case),
                seven_zip_report=seven_zip_report)

        self.assertEqual(0, exit_code)
        self.assertEqual("pass", report["profiles"]["7zip"]["status"])
        self.assertEqual("pass", report["fixtures"][0]["status"])
        self.assertEqual(
            "pass",
            report["fixtures"][0]["readers"]["7zip"]["status"])
        self.assertEqual(
            "pass",
            report["fixtures"][0]["adapterChecks"][0]["status"])
        self.assertEqual(0, report["summary"]["failedReaderAdapterProfiles"])

    def test_verifyDirectory_SevenZipOperationMismatch_FailsProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
            ]
            self.persist_case(temp, case, python_report)
            for operations in (
                [{
                    "operation": "extract",
                    "normalizedOutcome": "extract-fails",
                    "status": "fail",
                }],
                [],
            ):
                seven_zip_report = {
                    "adapter": "7zip",
                    "fixtures": [{
                        "fixtureId": case["fixtureId"],
                        "caseKey": case["caseKey"],
                        "expectationFileSha256": hashlib.sha256(
                            json.dumps(case).encode("utf-8")).hexdigest(),
                        "archiveSha256": case["archive"]["sha256"],
                        "status": "pass",
                        "checks": [{
                            "action": "extract",
                            "expectedOutcome": "success",
                            "observedOutcome": "success",
                            "status": "pass",
                        }],
                        "operations": operations,
                    }],
                }
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": None, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case),
                    seven_zip_report=seven_zip_report)

                self.assertEqual(1, exit_code)
                self.assertEqual("fail", report["profiles"]["7zip"]["status"])
                self.assertEqual("fail", report["fixtures"][0]["status"])

    def test_sevenZipReader_AdapterCheckOutcome_IsNotRevalidatedAsReaderExpectation(self):
        case = {
            "expectations": [{
                "operation": "extract",
                "profile": "strict",
                "allowedOutcomes": ["extract-fails"],
            }],
        }
        seven_zip_report = {
            "atc-example": {
                "status": "pass",
                "checks": [{
                    "action": "extract",
                    "expectedOutcome": "success",
                    "observedOutcome": "success",
                    "status": "pass",
                }],
                "operations": [{
                    "operation": "extract",
                    "normalizedOutcome": "extract-succeeds",
                    "status": "pass",
                }],
            },
        }

        reader = MATRIX["_seven_zip_reader"](case, "atc-example", seven_zip_report)

        self.assertEqual("pass", reader["status"])
        self.assertEqual(
            "extract-succeeds",
            reader["operations"]["extract"]["normalizedOutcome"])
        self.assertEqual(
            "fail",
            reader["operations"]["extract"]["expectationStatus"])

    def test_main_DotNetReportDirectory_IsRequired(self):
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            with self.assertRaises(SystemExit) as error:
                MATRIX["main"](["fixtures", "python-report.json"])

        self.assertEqual(2, error.exception.code)
        self.assertIn("--dotnet-report-dir", stderr.getvalue())

    def test_verifyDirectory_AllowedReaderSplit_IsReportedNotFailed(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            command = self.write_fake_unzip(temp)
            stdout = io.StringIO()
            with contextlib.redirect_stdout(stdout):
                with patch.dict(os.environ, {
                    "FAKE_UNZIP_REJECT_TEST": "1",
                    "FAKE_UNZIP_LIST_WARNING": "1",
                    "FAKE_UNZIP_READ_WARNING": "1",
                }):
                    report, exit_code = MATRIX["verify_directory"](
                        Path(temp), python_report,
                        {"infozip-unzip": command, "7zip": None},
                        dotnet_report=self.make_dotnet_report(case))

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual("observed", report["profiles"]["infozip-unzip"]["status"])
        self.assertEqual(
            "integrity-fails",
            fixture["readers"]["infozip-unzip"]["operations"]["integrity-check"]["normalizedOutcome"])
        self.assertEqual(
            ["integrity-check"],
            fixture["agreement"]["splitOperations"])
        self.assertEqual(1, report["summary"]["fixturesWithReaderSplits"])
        self.assertIn("integrity-check split", stdout.getvalue())

    def test_verifyDirectory_InfoZipExecutionErrors_AreReportedWithoutFailingGate(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "list",
                 "expectedOutcome": "list-succeeds"},
            ]
            self.persist_case(temp, case, python_report)
            missing_unzip = str(Path(temp) / "missing-unzip")
            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": missing_unzip, "7zip": None},
                dotnet_report=self.make_dotnet_report(case))

        check = report["fixtures"][0]["adapterChecks"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual("execution-error", check["status"])
        self.assertIn("could not start Info-ZIP unzip", check["reason"])
        self.assertEqual(
            "observed",
            report["fixtures"][0]["readers"]["infozip-unzip"]["status"])
        self.assertEqual("observed", report["profiles"]["infozip-unzip"]["status"])
        self.assertEqual(3, report["summary"]["readerExecutionErrors"])
        self.assertEqual(0, report["summary"]["failedReaderAdapterProfiles"])
        self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])
        for operation in ("list", "read-entry", "integrity-check"):
            observed = report["fixtures"][0]["readers"]["infozip-unzip"]["operations"][operation]
            self.assertEqual("execution-error", observed["status"])
            self.assertEqual("not-run", observed["normalizedOutcome"])

    def test_verifyDirectory_UnhandledInfoZipExecutionError_IsNonGating(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            infozip = self.write_fake_unzip(temp)

            def fail_reader(*_args):
                raise MATRIX["ReaderExecutionError"]("unexpected Info-ZIP execution error")

            with patch.dict(
                MATRIX["verify_directory"].__globals__,
                {"_infozip_reader": fail_reader},
            ):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": infozip, "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        fixture = report["fixtures"][0]
        reader = fixture["readers"]["infozip-unzip"]
        self.assertEqual(0, exit_code)
        self.assertEqual("observed", reader["status"])
        self.assertEqual("observed", report["profiles"]["infozip-unzip"]["status"])
        self.assertEqual(3, report["summary"]["readerExecutionErrors"])
        self.assertEqual(0, report["summary"]["failedReaderAdapterProfiles"])
        self.assertEqual([], fixture["agreement"]["splitOperations"])
        for operation in ("list", "read-entry", "integrity-check"):
            self.assertEqual("execution-error", reader["operations"][operation]["status"])

    def test_verifyDirectory_InfoZipIntegrityResourceError_IsNotComparedAsArchiveFailure(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            with patch.dict(os.environ, {"FAKE_UNZIP_INTEGRITY_RESOURCE_ERROR": "1"}):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        infozip = report["fixtures"][0]["readers"]["infozip-unzip"]
        observed = infozip["operations"]["integrity-check"]
        self.assertEqual(0, exit_code)
        self.assertEqual("execution-error", observed["status"])
        self.assertEqual("not-run", observed["normalizedOutcome"])
        self.assertIn("exit code 4", observed["failure"])
        self.assertEqual(1, report["summary"]["readerExecutionErrors"])
        self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])

    def test_verifyDirectory_InfoZipOverlappedArchive_IsComparedAsArchiveFailure(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            with patch.dict(os.environ, {"FAKE_UNZIP_OVERLAPPED_ARCHIVE": "1"}):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        infozip = report["fixtures"][0]["readers"]["infozip-unzip"]
        observed = infozip["operations"]["integrity-check"]
        self.assertEqual(0, exit_code)
        self.assertEqual("observed", observed["status"])
        self.assertEqual("integrity-fails", observed["normalizedOutcome"])
        self.assertEqual("pass", observed["expectationStatus"])
        self.assertEqual(0, report["summary"]["readerExecutionErrors"])
        self.assertEqual(
            "split",
            report["fixtures"][0]["agreement"]["operations"]["integrity-check"]["status"])

    def test_verifyDirectory_InfoZipArchiveErrors_AreComparedAsReaderOutcomes(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["entries"][0]["localHeaderMethod"] = 98
            case["entries"][0]["centralDirectoryMethod"] = 98
            self.persist_case(temp, case, python_report)
            with patch.dict(os.environ, {
                "FAKE_UNZIP_NO_ARCHIVE": "1",
                "FAKE_UNZIP_NO_MATCH": "1",
            }):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

            with self.isolated_fake_unzip_environment(FAKE_UNZIP_READ_NO_ARCHIVE="1"):
                missing_archive_report, missing_archive_exit = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        infozip = report["fixtures"][0]["readers"]["infozip-unzip"]
        self.assertEqual(0, exit_code)
        self.assertEqual("observed", infozip["status"])
        self.assertEqual(0, report["summary"]["readerExecutionErrors"])
        for operation, outcome, expectation_status in (
            ("list", "list-fails", "fail"),
            ("read-entry", "read-entry-fails", "fail"),
            ("integrity-check", "integrity-fails", "pass"),
        ):
            result = infozip["operations"][operation]
            self.assertEqual("observed", result["status"])
            self.assertEqual(outcome, result["normalizedOutcome"])
            self.assertEqual(expectation_status, result["expectationStatus"])
            self.assertEqual(
                "split",
                report["fixtures"][0]["agreement"]["operations"][operation]["status"])

        read_entry = missing_archive_report["fixtures"][0]["readers"][
            "infozip-unzip"]["operations"]["read-entry"]
        self.assertEqual(0, missing_archive_exit)
        self.assertEqual("observed", read_entry["status"])
        self.assertEqual("read-entry-fails", read_entry["normalizedOutcome"])
        self.assertEqual(0, missing_archive_report["summary"]["readerExecutionErrors"])

    def test_verifyDirectory_InfoZipFixtureDeadlineBoundsOperations(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["limits"]["deadlineSeconds"] = 1
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "list",
                 "expectedOutcome": "list-succeeds"},
                {"adapter": "infozip-unzip", "action": "read-entry",
                 "expectedOutcome": "read-entry-content-matches"},
                {"adapter": "infozip-unzip", "action": "integrity-check",
                 "expectedOutcome": "integrity-passes"},
            ]
            self.persist_case(temp, case, python_report)
            with patch.dict(os.environ, {
                "FAKE_UNZIP_TIMEOUT_ACTIONS": "-Z1,-p,-t",
            }):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        infozip = report["fixtures"][0]["readers"]["infozip-unzip"]
        self.assertEqual(0, exit_code)
        self.assertEqual("observed", infozip["status"])
        self.assertIn("timed out", infozip["operations"]["list"]["failure"])
        self.assertEqual("execution-error", infozip["operations"]["list"]["status"])
        for operation in ("read-entry", "integrity-check"):
            self.assertEqual(
                "execution-error", infozip["operations"][operation]["status"])
            self.assertIn(
                "deadline exhausted", infozip["operations"][operation]["failure"])
        self.assertEqual(
            ["execution-error"] * 3,
            [check["status"] for check in report["fixtures"][0]["adapterChecks"]])
        self.assertEqual(3, report["summary"]["readerExecutionErrors"])
        self.assertEqual(0, report["summary"]["failedReaderAdapterProfiles"])
        self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])

    def test_verifyDirectory_InfoZipAdapterChecks_RunWithoutApplicableExpectations(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            for expectation in case["expectations"]:
                expectation["platform"] = "adls-gen2"
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "list",
                 "expectedOutcome": "list-succeeds"},
                {"adapter": "infozip-unzip", "action": "read-entry",
                 "expectedOutcome": "read-entry-content-matches"},
                {"adapter": "infozip-unzip", "action": "integrity-check",
                 "expectedOutcome": "integrity-passes"},
            ]
            self.persist_case(temp, case, python_report)
            python_fixture = python_report["fixtures"][0]
            python_fixture["status"] = "skipped"
            for operation in python_fixture["operations"]:
                operation.update(normalizedOutcome="not-run", status="not-run")
            dotnet_report = self.make_dotnet_report(case)
            dotnet_fixture = dotnet_report["fixtures"][0]
            dotnet_fixture["status"] = "skipped"
            for operation in dotnet_fixture["operations"]:
                operation.update(normalizedOutcome="not-run", status="not-run")

            with patch.dict(os.environ, {"ZIPPER_ATC_PLATFORMS": ""}):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        for action, expected_outcome in (
            ("list", "list-succeeds"),
            ("read-entry", "read-entry-content-matches"),
            ("integrity-check", "integrity-passes"),
        ):
            self.assertEqual(
                expected_outcome,
                fixture["readers"]["infozip-unzip"]["operations"][action]["normalizedOutcome"])
        self.assertEqual(
            ["pass"] * 3,
            [check["status"] for check in fixture["adapterChecks"]])
        self.assertEqual("skipped", fixture["status"])

    def test_verifyDirectory_InfoZipAdapterCheckProbeError_WithoutApplicableExpectation_IsNonGating(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            for expectation in case["expectations"]:
                expectation["platform"] = "adls-gen2"
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "list",
                 "expectedOutcome": "list-succeeds"},
            ]
            self.persist_case(temp, case, python_report)
            python_fixture = python_report["fixtures"][0]
            python_fixture["status"] = "skipped"
            for operation in python_fixture["operations"]:
                operation.update(normalizedOutcome="not-run", status="not-run")
            dotnet_report = self.make_dotnet_report(case)
            for operation in dotnet_report["fixtures"][0]["operations"]:
                operation.update(normalizedOutcome="not-run", status="not-run")

            with patch.dict(os.environ, {
                "FAKE_UNZIP_INVALID_VERSION": "1",
                "ZIPPER_ATC_PLATFORMS": "",
            }):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=dotnet_report)

        fixture = report["fixtures"][0]
        self.assertEqual(0, exit_code)
        self.assertEqual("skipped", fixture["status"])
        self.assertEqual(
            "execution-error",
            fixture["readers"]["infozip-unzip"]["operations"]["list"]["status"])
        self.assertEqual("execution-error", fixture["adapterChecks"][0]["status"])
        self.assertEqual(1, report["summary"]["readerExecutionErrors"])
        self.assertEqual([], fixture["agreement"]["splitOperations"])

    def test_verifyDirectory_InfoZipMemberReadTimeout_IsExecutionError(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["limits"]["deadlineSeconds"] = 1
            self.persist_case(temp, case, python_report)
            with patch.dict(os.environ, {
                "FAKE_UNZIP_TIMEOUT_ACTIONS": "-p",
            }):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        infozip = report["fixtures"][0]["readers"]["infozip-unzip"]
        self.assertEqual(0, exit_code)
        self.assertEqual("list-succeeds", infozip["operations"]["list"]["normalizedOutcome"])
        self.assertIn("timed out", infozip["operations"]["read-entry"]["failure"])
        self.assertEqual("execution-error", infozip["operations"]["read-entry"]["status"])
        self.assertEqual(
            "execution-error", infozip["operations"]["integrity-check"]["status"])
        self.assertIn(
            "deadline exhausted", infozip["operations"]["integrity-check"]["failure"])
        self.assertEqual(2, report["summary"]["readerExecutionErrors"])
        self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])

    def test_verifyDirectory_InfoZipTimeoutKillsForkedReader(self):
        for action, expected_operation in (("-Z1", "list"), ("-p", "read-entry")):
            for parent_exits in (False, True):
                with self.subTest(action=action, parent_exits=parent_exits), tempfile.TemporaryDirectory() as temp:
                    case, python_report = self.make_fixture(temp)
                    case["limits"]["deadlineSeconds"] = 1
                    self.persist_case(temp, case, python_report)
                    child_pid_path = Path(temp) / "reader-child.pid"
                    environment = {
                        "FAKE_UNZIP_FORK_ACTIONS": action,
                        "FAKE_UNZIP_CHILD_PID": str(child_pid_path),
                    }
                    if parent_exits:
                        environment["FAKE_UNZIP_EXIT_PARENT_AFTER_FORK"] = "1"
                    start = time.monotonic()
                    with patch.dict(os.environ, environment):
                        report, exit_code = MATRIX["verify_directory"](
                            Path(temp), python_report,
                            {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                            dotnet_report=self.make_dotnet_report(case))

                    self.assertEqual(0, exit_code)
                    self.assertLess(time.monotonic() - start, 4)
                    self.assertTrue(child_pid_path.is_file())
                    child_pid = int(child_pid_path.read_text(encoding="utf-8"))
                    deadline = time.monotonic() + 2
                    while self.process_is_running(child_pid) and time.monotonic() < deadline:
                        time.sleep(0.05)
                    self.assertFalse(self.process_is_running(child_pid))
                    observed = report["fixtures"][0]["readers"]["infozip-unzip"][
                        "operations"][expected_operation]
                    self.assertEqual("execution-error", observed["status"])
                    expected_errors = 3 if action == "-Z1" else 2
                    self.assertEqual(
                        expected_errors, report["summary"]["readerExecutionErrors"])

    def test_verifyDirectory_InfoZipMissingArchive_IsExecutionError(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            self.persist_case(temp, case, python_report)
            with self.isolated_fake_unzip_environment(FAKE_UNZIP_REMOVE_ARCHIVE="1"):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        infozip = report["fixtures"][0]["readers"]["infozip-unzip"]
        self.assertEqual(0, exit_code)
        self.assertEqual("observed", infozip["status"])
        self.assertEqual("execution-error", infozip["operations"]["list"]["status"])
        self.assertEqual(3, report["summary"]["readerExecutionErrors"])

    @unittest.skipIf(os.name == "nt", "POSIX signal return codes are required")
    def test_verifyDirectory_InfoZipSignalTermination_IsExecutionError(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            with patch.dict(os.environ, {
                "FAKE_UNZIP_SIGNAL_ACTIONS": "-Z1,-p,-t",
            }):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

        infozip = report["fixtures"][0]["readers"]["infozip-unzip"]
        self.assertEqual(0, exit_code)
        self.assertEqual(3, report["summary"]["readerExecutionErrors"])
        self.assertEqual(0, report["summary"]["failedReaderAdapterProfiles"])
        self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])
        for operation in ("list", "read-entry", "integrity-check"):
            observed = infozip["operations"][operation]
            self.assertIn("terminated by signal", observed["failure"])
            self.assertEqual("execution-error", observed["status"])

    def test_infoZipReader_ListWithLineSeparatorMember_IsNotComparedAsSplit(self):
        with tempfile.TemporaryDirectory() as parent:
            for line_separator in (
                "\n", "\r", "\v", "\f", "\x1c", "\x1d", "\x1e",
                "\x85", "\u2028", "\u2029",
            ):
                with self.subTest(line_separator=repr(line_separator)), tempfile.TemporaryDirectory(
                    dir=parent
                ) as temp:
                    case, python_report = self.make_fixture(temp)
                    command = self.write_fake_unzip(temp)
                    name = f"a{line_separator}.txt"
                    case["entries"][0]["readableName"] = name
                    with zipfile.ZipFile(
                        Path(temp) / (case["fixtureId"] + ".zip"), "w",
                        compression=zipfile.ZIP_STORED,
                    ) as output:
                        output.writestr(name, b"reader matrix payload")
                    self.persist_case(temp, case, python_report)

                    report, exit_code = MATRIX["verify_directory"](
                        Path(temp), python_report,
                        {"infozip-unzip": command, "7zip": None},
                        dotnet_report=self.make_dotnet_report(case))

                    fixture = report["fixtures"][0]
                    list_result = fixture["readers"]["infozip-unzip"]["operations"]["list"]
                    self.assertEqual(0, exit_code)
                    self.assertEqual("not-run", list_result["status"])
                    self.assertEqual("not-run", list_result["normalizedOutcome"])
                    self.assertEqual(
                        "read-entry-content-matches",
                        fixture["readers"]["infozip-unzip"]["operations"]["read-entry"]["normalizedOutcome"])
                    self.assertIn("line separators", list_result["reason"])
                    self.assertEqual("agree", fixture["agreement"]["operations"]["list"]["status"])

    def test_verifyDirectory_InstalledInfoZip_ReportsRealOutcomes(self):
        unzip = shutil.which("unzip")
        if unzip is None:
            self.skipTest("Info-ZIP unzip is unavailable")

        with tempfile.TemporaryDirectory() as temp:
            _, python_report = self.make_fixture(temp)
            case = json.loads(
                (Path(temp) / (python_report["fixtures"][0]["fixtureId"] + ".json"))
                .read_text(encoding="utf-8"))
            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": unzip, "7zip": None},
                dotnet_report=self.make_dotnet_report(case))

        self.assertEqual(0, exit_code)
        self.assertEqual("observed", report["profiles"]["infozip-unzip"]["status"])
        self.assertIn("UnZip", report["profiles"]["infozip-unzip"]["version"])
        self.assertEqual(
            "list-succeeds",
            report["fixtures"][0]["readers"]["infozip-unzip"]["operations"]["list"]["normalizedOutcome"])
        self.assertEqual(
            "read-entry-content-matches",
            report["fixtures"][0]["readers"]["infozip-unzip"]["operations"]["read-entry"]["normalizedOutcome"])
        self.assertEqual(
            "integrity-passes",
            report["fixtures"][0]["readers"]["infozip-unzip"]["operations"]["integrity-check"]["normalizedOutcome"])

    def test_verifyDirectory_OutcomeOutsideExpectation_IsReportedForObserver(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["expectations"][0]["allowedOutcomes"] = ["list-fails"]
            self.persist_case(temp, case, python_report)
            python_report["fixtures"][0]["operations"][0]["normalizedOutcome"] = "list-fails"
            dotnet_report = self.make_dotnet_report(case)
            dotnet_report["fixtures"][0]["operations"][0]["normalizedOutcome"] = "list-fails"
            command = self.write_fake_unzip(temp)

            report, exit_code = MATRIX["verify_directory"](
                Path(temp), python_report,
                {"infozip-unzip": command, "7zip": None},
                dotnet_report=dotnet_report)

        self.assertEqual(0, exit_code)
        self.assertEqual("observed", report["profiles"]["infozip-unzip"]["status"])
        self.assertEqual(
            "observed",
            report["fixtures"][0]["readers"]["infozip-unzip"]["operations"]["list"]["status"])
        self.assertEqual(
            "fail",
            report["fixtures"][0]["readers"]["infozip-unzip"]["operations"]["list"]["expectationStatus"])
        self.assertEqual(
            "list-succeeds",
            report["fixtures"][0]["readers"]["infozip-unzip"]["operations"]["list"]["normalizedOutcome"])
        self.assertEqual(0, report["summary"]["readerExecutionErrors"])


if __name__ == "__main__":
    unittest.main()
