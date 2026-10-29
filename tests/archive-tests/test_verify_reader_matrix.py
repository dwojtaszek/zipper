#!/usr/bin/env python3
"""Contract tests for the optional Archive Test reader matrix."""

import contextlib
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


class ReaderMatrixTests(unittest.TestCase):
    def make_fixture(self, directory):
        fixture_id = "atc-" + "a" * 64
        archive = Path(directory) / (fixture_id + ".zip")
        content = b"reader matrix payload"
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED) as output:
            output.writestr("a.txt", content)

        case = {
            "fixtureId": fixture_id,
            "caseKey": "valid-stored",
            "classification": "valid",
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
        (Path(directory) / (fixture_id + ".json")).write_text(
            json.dumps(case), encoding="utf-8")

        python_report = {
            "adapter": "python-zipfile",
            "fixtures": [{
                "fixtureId": fixture_id,
                "caseKey": case["caseKey"],
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
                "status": "pass",
                "operations": [
                    {"operation": operation, "normalizedOutcome": outcome, "status": "pass"}
                    for operation, outcome in operations
                ],
            }],
        }

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
    child = subprocess.Popen(
        [sys.executable, "-c", "import time; time.sleep(5)"],
        stdout=sys.stdout, stderr=sys.stderr)
    with open(os.environ["FAKE_UNZIP_CHILD_PID"], "w", encoding="utf-8") as pid_file:
        pid_file.write(str(child.pid))
    time.sleep(5)
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
    with zipfile.ZipFile(args[1]) as archive:
        sys.stdout.buffer.write(archive.read(args[2]))
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

    def test_validateAdapterChecks_RejectsUndeclaredProfileOutcomes(self):
        case = {
            "caseKey": "valid-stored",
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

    def test_verifyDirectory_MissingInfoZip_RecordsDocumentedSkip(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "list",
                 "expectedOutcome": "list-succeeds"},
                {"adapter": "7zip", "action": "extract",
                 "expectedOutcome": "success"},
            ]
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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

    def test_verifyDirectory_DeclaredCheckMismatch_FailsNamedProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "infozip-unzip", "action": "integrity-check",
                 "expectedOutcome": "integrity-fails"},
            ]
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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

    def test_verifyDirectory_WrongPythonReportProfile_Fails(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            python_report["adapter"] = "dotnet-reference"

            with self.assertRaisesRegex(ValueError, "unknown Reader Adapter Profile"):
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
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")

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
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
            seven_zip_report = {
                "adapter": "7zip",
                "fixtures": [{
                    "fixtureId": case["fixtureId"],
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

    def test_verifyDirectory_SevenZipOperationMismatch_FailsProfile(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
            ]
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
            with patch.dict(os.environ, {
                "FAKE_UNZIP_NO_ARCHIVE": "1",
                "FAKE_UNZIP_NO_MATCH": "1",
            }):
                report, exit_code = MATRIX["verify_directory"](
                    Path(temp), python_report,
                    {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                    dotnet_report=self.make_dotnet_report(case))

            with patch.dict(os.environ, {"FAKE_UNZIP_READ_NO_ARCHIVE": "1"}, clear=True):
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
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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
        self.assertEqual("not-run", infozip["operations"]["read-entry"]["status"])
        self.assertEqual("not-run", infozip["operations"]["integrity-check"]["status"])
        self.assertEqual(1, report["summary"]["readerExecutionErrors"])
        self.assertEqual(0, report["summary"]["failedReaderAdapterProfiles"])
        self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])

    def test_verifyDirectory_InfoZipMemberReadTimeout_IsExecutionError(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            case["limits"]["deadlineSeconds"] = 1
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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
        self.assertEqual("not-run", infozip["operations"]["integrity-check"]["status"])
        self.assertEqual(1, report["summary"]["readerExecutionErrors"])
        self.assertEqual([], report["fixtures"][0]["agreement"]["splitOperations"])

    def test_verifyDirectory_InfoZipTimeoutKillsForkedReader(self):
        for action, expected_operation in (("-Z1", "list"), ("-p", "read-entry")):
            with self.subTest(action=action), tempfile.TemporaryDirectory() as temp:
                case, python_report = self.make_fixture(temp)
                case["limits"]["deadlineSeconds"] = 1
                (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                    json.dumps(case), encoding="utf-8")
                child_pid_path = Path(temp) / "reader-child.pid"
                start = time.monotonic()
                with patch.dict(os.environ, {
                    "FAKE_UNZIP_FORK_ACTIONS": action,
                    "FAKE_UNZIP_CHILD_PID": str(child_pid_path),
                }):
                    report, exit_code = MATRIX["verify_directory"](
                        Path(temp), python_report,
                        {"infozip-unzip": self.write_fake_unzip(temp), "7zip": None},
                        dotnet_report=self.make_dotnet_report(case))

                self.assertEqual(0, exit_code)
                self.assertLess(time.monotonic() - start, 4)
                self.assertTrue(child_pid_path.is_file())
                observed = report["fixtures"][0]["readers"]["infozip-unzip"][
                    "operations"][expected_operation]
                self.assertEqual("execution-error", observed["status"])
                self.assertEqual(1, report["summary"]["readerExecutionErrors"])

    def test_verifyDirectory_InfoZipMissingArchive_IsExecutionError(self):
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
            with patch.dict(os.environ, {"FAKE_UNZIP_REMOVE_ARCHIVE": "1"}, clear=True):
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
        with tempfile.TemporaryDirectory() as temp:
            case, python_report = self.make_fixture(temp)
            command = self.write_fake_unzip(temp)
            for line_separator in (
                "\n", "\r", "\v", "\f", "\x1c", "\x1d", "\x1e",
                "\x85", "\u2028", "\u2029",
            ):
                with self.subTest(line_separator=repr(line_separator)):
                    name = f"a{line_separator}.txt"
                    case["entries"][0]["readableName"] = name
                    (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                        json.dumps(case), encoding="utf-8")
                    with zipfile.ZipFile(
                        Path(temp) / (case["fixtureId"] + ".zip"), "w",
                        compression=zipfile.ZIP_STORED,
                    ) as output:
                        output.writestr(name, b"reader matrix payload")

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
            (Path(temp) / (case["fixtureId"] + ".json")).write_text(
                json.dumps(case), encoding="utf-8")
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
