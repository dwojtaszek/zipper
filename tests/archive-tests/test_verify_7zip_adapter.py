import hashlib
import json
import os
import runpy
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).with_name("verify-7zip-adapter.py")
VERIFIER = runpy.run_path(str(SCRIPT))
READER_PROCESS = runpy.run_path(str(SCRIPT.with_name("reader_process.py")))


class SevenZipAdapterTests(unittest.TestCase):
    def test_verify_seven_zip_checks_share_one_fixture_deadline(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            stem = "atc-" + "d" * 64
            (directory / f"{stem}.zip").write_bytes(b"fixture")
            (directory / f"{stem}.json").write_text(json.dumps({
                "fixtureId": stem,
                "caseKey": "multi-check-deadline",
                "adapterChecks": [
                    {"adapter": "7zip", "action": "presence", "expectedOutcome": "present"},
                    {"adapter": "7zip", "action": "presence", "expectedOutcome": "present"},
                ],
                "limits": {"deadlineSeconds": 1},
            }), encoding="utf-8")
            report_path = directory / "report.json"
            clock = type("Clock", (), {"monotonic": unittest.mock.Mock(
                side_effect=(0.0, 0.1, 1.1))})()

            with patch.dict(VERIFIER["verify"].__globals__, {"time": clock}):
                with self.assertRaisesRegex(ValueError, "fixture check deadline exceeded"):
                    VERIFIER["verify"](directory, "unused-7zip", report_path)

            report = json.loads(report_path.read_text(encoding="utf-8"))
            fixture = report["fixtures"][0]
            self.assertEqual("fail", fixture["status"])
            self.assertEqual(2, len(fixture["checks"]))
            self.assertEqual("pass", fixture["checks"][0]["status"])
            self.assertEqual("fail", fixture["checks"][1]["status"])
            self.assertIn("fixture check deadline exceeded", fixture["checks"][1]["failure"])

    def test_terminate_reader_process_windows_kills_job_wrapper(self):
        class Process:
            pid = 12345

            def __init__(self):
                self.killed = False

            def poll(self):
                return None

            def kill(self):
                self.killed = True

        process = Process()
        windows = type("WindowsOS", (), {"name": "nt"})()
        with patch.dict(
            READER_PROCESS["terminate_reader_process"].__globals__,
            {"os": windows},
        ):
            with patch.object(READER_PROCESS["subprocess"], "run") as taskkill:
                READER_PROCESS["terminate_reader_process"](process)

        taskkill.assert_not_called()
        self.assertTrue(process.killed)

    def test_terminate_reader_process_windows_does_not_kill_exited_wrapper(self):
        class Process:
            pid = 12345

            def poll(self):
                return 0

            def kill(self):
                raise AssertionError("an exited process must not be killed again")

        process = Process()
        windows = type("WindowsOS", (), {"name": "nt"})()
        with patch.dict(
            READER_PROCESS["terminate_reader_process"].__globals__,
            {"os": windows},
        ):
            with patch.object(READER_PROCESS["subprocess"], "run") as taskkill:
                READER_PROCESS["terminate_reader_process"](process)

        taskkill.assert_not_called()

    def test_check_entry_rejects_watchdog_timeout_after_matching_output(self):
        expected = b"expected content"

        class BlockingStdout:
            def __init__(self, killed):
                self.killed = killed
                self.output = expected

            def read(self, _size):
                if self.output:
                    output, self.output = self.output, b""
                    return output
                self.killed.wait(timeout=5)
                return b""

        class HangingProcess:
            def __init__(self):
                self.killed = VERIFIER["threading"].Event()
                self.stdout = BlockingStdout(self.killed)

            def __enter__(self):
                return self

            def __exit__(self, *_args):
                return None

            def poll(self):
                return 1 if self.killed.is_set() else None

            def kill(self):
                self.killed.set()

            def wait(self, timeout):
                if not self.killed.wait(timeout):
                    raise subprocess.TimeoutExpired("fake-7zip", timeout)
                return 1

            def communicate(self):
                return b"", b""

        process = HangingProcess()
        original_timer = VERIFIER["threading"].Timer

        def short_timer(_interval, function, *args, **kwargs):
            return original_timer(0.01, function, *args, **kwargs)

        entry = {
            "readableName": "a.txt",
            "contentSize": len(expected),
            "contentSha256": hashlib.sha256(expected).hexdigest(),
        }
        with patch.object(VERIFIER["threading"], "Timer", side_effect=short_timer):
            with patch.object(VERIFIER["subprocess"], "Popen", return_value=process):
                with self.assertRaisesRegex(ValueError, "7-Zip timed out extracting"):
                    VERIFIER["check_entry"]("fake-7zip", Path("archive.zip"), entry, len(expected))

    def test_run_reader_command_output_exceeding_budget_is_rejected(self):
        with patch.dict(
            READER_PROCESS["run_reader_command"].__globals__,
            {"MAX_READER_OUTPUT_BYTES": 1024},
        ):
            with self.assertRaisesRegex(OSError, "exceeded the 1024-byte output budget"):
                READER_PROCESS["run_reader_command"](
                    [sys.executable, "-c", "import sys; sys.stdout.buffer.write(b'x' * 65536)"],
                    timeout=5)

    def test_run_reader_command_JoinsOutputThreadsWithinOneAbsoluteDeadline(self):
        class Clock:
            now = 0.0

            def monotonic(self):
                return self.now

            def advance(self, duration):
                self.now += duration

        clock = Clock()
        threads = []

        class Thread:
            def __init__(self, *_args, **_kwargs):
                self.join_timeouts = []
                self.alive = True
                threads.append(self)

            def start(self):
                pass

            def join(self, timeout=None):
                self.join_timeouts.append(timeout)
                if timeout is None:
                    self.alive = False
                    return
                duration = min(0.6, timeout)
                clock.advance(duration)
                if timeout >= 0.6:
                    self.alive = False

            def is_alive(self):
                return self.alive

        class Stream:
            def read(self, _size):
                return b""

            def close(self):
                pass

        threading_stub = type("Threading", (), {
            "Event": READER_PROCESS["threading"].Event,
            "Lock": READER_PROCESS["threading"].Lock,
            "Thread": Thread,
        })()

        class Process:
            pid = 12345
            returncode = 0
            stdout = Stream()
            stderr = Stream()

            def wait(self, timeout=None):
                return 0

        process = Process()
        globals_ = READER_PROCESS["run_reader_command"].__globals__
        with patch.dict(globals_, {
            "start_reader_process": lambda *_args, **_kwargs: process,
            "terminate_reader_process": lambda _process: None,
            "threading": threading_stub,
            "time": clock,
        }):
            with self.assertRaises(subprocess.TimeoutExpired):
                READER_PROCESS["run_reader_command"](["fake-reader"], timeout=1)

        self.assertEqual(2, len(threads))
        self.assertLess(threads[1].join_timeouts[0], threads[0].join_timeouts[0])

    def test_verify_test_actionProjectsFailedIntegrityCheckInReport(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            stem = "atc-" + "c" * 64
            (directory / f"{stem}.zip").write_bytes(b"malformed archive")
            (directory / f"{stem}.json").write_text(json.dumps({
                "fixtureId": stem,
                "caseKey": "malformed-test-report",
                "entries": [],
                "limits": {"expandedBytesBudget": 0, "deadlineSeconds": 1},
                "adapterChecks": [
                    {"adapter": "7zip", "action": "test", "expectedOutcome": "failure"},
                ],
            }), encoding="utf-8")
            report_path = directory / "7zip-report.json"

            def accepted_archive(*_args, **_kwargs):
                raise ValueError("7-Zip accepted malformed Archive")

            with patch.dict(
                VERIFIER["verify"].__globals__,
                {"check_archive_rejection": accepted_archive},
            ):
                with self.assertRaisesRegex(ValueError, "accepted malformed Archive"):
                    VERIFIER["verify"](directory, "unused-7zip", report_path)

            fixture = json.loads(report_path.read_text(encoding="utf-8"))["fixtures"][0]
            self.assertEqual("fail", fixture["checks"][0]["status"])
            self.assertIn("accepted malformed Archive", fixture["checks"][0]["failure"])
            self.assertEqual(
                [{"operation": "integrity-check",
                  "normalizedOutcome": "integrity-passes", "status": "fail"}],
                fixture["operations"])

    def test_check_entry_accepts_warning_when_expected_content_matches(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            archive = directory / "archive.zip"
            archive.write_bytes(b"archive")
            script = directory / "fake_runner.py"
            if sys.platform == "win32":
                adapter = directory / "fake_adapter.cmd"
                adapter.write_text(f'@"{sys.executable}" "{script}" %*\n', encoding="utf-8")
            else:
                adapter = directory / "fake_adapter"
                adapter.write_text(
                    f'#!/bin/sh\nexec {shlex.quote(sys.executable)} {shlex.quote(str(script))} "$@"\n',
                    encoding="utf-8",
                )
                adapter.chmod(0o755)

            expected = b"expected content"

            def write_fake_output(content, exit_code):
                script.write_text(
                    f"import sys\nsys.stdout.buffer.write({content!r})\nsys.exit({exit_code})\n",
                    encoding="utf-8",
                )

            entry = {
                "readableName": "a.txt",
                "contentSize": len(expected),
                "contentSha256": hashlib.sha256(expected).hexdigest(),
            }
            write_fake_output(expected, 1)
            VERIFIER["check_entry"](str(adapter), archive, entry, len(expected))

            write_fake_output(b"x" * len(expected), 1)
            with self.assertRaisesRegex(ValueError, "content hash mismatch"):
                VERIFIER["check_entry"](str(adapter), archive, entry, len(expected))

            write_fake_output(expected, 2)
            with self.assertRaisesRegex(ValueError, "7-Zip extraction failed"):
                VERIFIER["check_entry"](str(adapter), archive, entry, len(expected))

    def test_check_archive_rejection_timeout_reports_classified_failure(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            archive = directory / "archive.zip"
            archive.write_bytes(b"corrupted")
            (directory / "t").write_text("import time; time.sleep(10)\n", encoding="utf-8")

            original_run = VERIFIER["run_reader_command"]

            def run_with_short_timeout(command, timeout):
                return original_run(command, timeout=0.1)

            original_cwd = Path.cwd()
            try:
                os.chdir(directory)
                with patch.dict(
                    VERIFIER["check_archive_rejection"].__globals__,
                    {"run_reader_command": run_with_short_timeout},
                ):
                    with self.assertRaisesRegex(ValueError, "7-Zip timed out testing timeout-case"):
                        VERIFIER["check_archive_rejection"](sys.executable, archive, "timeout-case")
            finally:
                os.chdir(original_cwd)

    def test_verify_declared_actions_checks_real_extraction_and_hash(self):
        seven = shutil.which("7zz") or shutil.which("7z")
        if seven is None:
            self.skipTest("7-Zip is unavailable")

        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            stem = "atc-" + "a" * 64
            archive = directory / f"{stem}.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("a.txt", b"actual content")
                output.writestr("b.txt", b"unselected content")
            original_archive_bytes = archive.read_bytes()
            sidecar = directory / f"{stem}.json"
            report = directory / "7zip-report.json"
            adapter_checks = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
                {"adapter": "7zip", "action": "extract-entries",
                 "expectedOutcome": "success", "entryNames": ["a.txt"]},
                {"adapter": "7zip", "action": "presence", "expectedOutcome": "present"},
            ]
            data = {
                "caseKey": "control",
                "entries": [
                    {
                        "kind": "file",
                        "readableName": "a.txt",
                        "contentSha256": hashlib.sha256(b"actual content").hexdigest(),
                        "contentSize": len(b"actual content"),
                    },
                    {
                        "kind": "file",
                        "readableName": "b.txt",
                        "contentSha256": hashlib.sha256(b"unselected content").hexdigest(),
                        "contentSize": len(b"unselected content"),
                    },
                ],
                "limits": {"expandedBytesBudget": len(b"actual content") + len(b"unselected content")},
                "adapterChecks": adapter_checks,
            }
            sidecar.write_text(json.dumps(data))

            def run():
                return subprocess.run(
                    [sys.executable, str(SCRIPT), str(directory), seven,
                     "--report", str(report)],
                    capture_output=True, text=True, check=False,
                )

            res = run()
            self.assertEqual(0, res.returncode)
            saved_report = json.loads(report.read_text(encoding="utf-8"))
            self.assertEqual("7zip", saved_report["adapter"])
            self.assertEqual(1, len(saved_report["fixtures"]))
            self.assertEqual("pass", saved_report["fixtures"][0]["status"])
            self.assertEqual(
                hashlib.sha256(sidecar.read_bytes()).hexdigest(),
                saved_report["fixtures"][0]["expectationFileSha256"])
            self.assertEqual(
                hashlib.sha256(archive.read_bytes()).hexdigest(),
                saved_report["fixtures"][0]["archiveSha256"])
            self.assertEqual(
                {"extract", "extract-entries", "presence"},
                {check["action"] for check in saved_report["fixtures"][0]["checks"]})
            self.assertEqual(
                {"extract"},
                {operation["operation"]
                 for operation in saved_report["fixtures"][0]["operations"]})
            self.assertEqual("success", saved_report["fixtures"][0]["checks"][0]["observedOutcome"])
            self.assertEqual("pass", saved_report["fixtures"][0]["checks"][0]["status"])
            self.assertEqual(
                "extract-succeeds",
                saved_report["fixtures"][0]["operations"][0]["normalizedOutcome"])

            data["entries"] = [{
                "kind": "file",
                "readableName": "empty.txt",
                "contentSha256": hashlib.sha256(b"").hexdigest(),
                "contentSize": 0,
            }]
            data["limits"]["expandedBytesBudget"] = 0
            data["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
            ]
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("empty.txt", b"")
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(0, res.returncode, res.stderr)

            data["entries"][0]["readableName"] = "missing.txt"
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("missing.txt", res.stderr)
            self.assertIn("not present as declared", res.stderr)

            archive.write_bytes(original_archive_bytes)
            data["entries"] = [
                {
                    "kind": "file",
                    "readableName": "a.txt",
                    "contentSha256": hashlib.sha256(b"actual content").hexdigest(),
                    "contentSize": len(b"actual content"),
                },
                {
                    "kind": "file",
                    "readableName": "b.txt",
                    "contentSha256": hashlib.sha256(b"unselected content").hexdigest(),
                    "contentSize": len(b"unselected content"),
                },
            ]
            data["limits"]["expandedBytesBudget"] = (
                len(b"actual content") + len(b"unselected content"))
            data["adapterChecks"] = [{
                "adapter": "7zip",
                "action": "extract-entries",
                "expectedOutcome": "success",
                "entryNames": ["a.txt"],
            }]
            data["entries"][1]["contentSha256"] = "0" * 64
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(0, res.returncode, res.stderr)
            data["entries"][1]["contentSha256"] = hashlib.sha256(
                b"unselected content").hexdigest()
            data["adapterChecks"] = adapter_checks

            data["entries"][0]["contentSha256"] = "0" * 64
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("content hash mismatch", res.stderr)

            data["entries"][0]["contentSha256"] = hashlib.sha256(b"actual content").hexdigest()
            data["limits"]["expandedBytesBudget"] = 1
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("expanded bytes budget exceeded", res.stderr)

            data["limits"]["expandedBytesBudget"] = (
                len(b"actual content") + len(b"unselected content"))
            data["entries"][0]["readableName"] = "-y"
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("unsafe entry name", res.stderr)

            data["entries"][0]["readableName"] = "@files.txt"
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("unsafe entry name", res.stderr)

            data["adapterChecks"] = []
            sidecar.write_text(json.dumps(data))
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("no 7-Zip adapter checks", res.stderr)

            # Ticket #1131: Real malformed Archive negative control alongside valid control
            data["entries"][0]["readableName"] = "a.txt"
            data["adapterChecks"] = adapter_checks
            sidecar.write_text(json.dumps(data))

            stem_malformed = "atc-" + "b" * 64
            archive_malformed = directory / f"{stem_malformed}.zip"
            archive_malformed.write_bytes(b"PK\x03\x04corrupted archive payload")
            sidecar_malformed = directory / f"{stem_malformed}.json"
            data_malformed = {
                "caseKey": "control-malformed",
                "entries": [],
                "limits": {"expandedBytesBudget": 0},
                "adapterChecks": [
                    {"adapter": "7zip", "action": "test", "expectedOutcome": "failure"},
                ],
            }
            sidecar_malformed.write_text(json.dumps(data_malformed))

            res = run()
            self.assertEqual(0, res.returncode)
            self.assertIn("7-Zip verified 4 declared adapter checks.", res.stdout)

            # If real malformed check is given a valid archive, verifier fails closed
            with zipfile.ZipFile(archive_malformed, "w") as valid_output:
                valid_output.writestr("b.txt", b"valid content")
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("expected rejection", res.stderr)

    def test_verify_rejectsInvalidExpandedBytesBudgetBeforeExtracting(self):
        invalid_budgets = (
            (False, "invalid expanded bytes budget"),
            (True, "invalid expanded bytes budget"),
            (-1, "invalid expanded bytes budget"),
            ("invalid", "invalid expanded bytes budget"),
            (VERIFIER["MAX_EXPANDED_BYTES_BUDGET"] + 1, "expanded bytes budget exceeds hard limit"),
        )
        for budget, message in invalid_budgets:
            with self.subTest(budget=budget), tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                stem = "atc-" + "a" * 64
                (directory / f"{stem}.zip").write_bytes(b"archive")
                (directory / f"{stem}.json").write_text(json.dumps({
                    "caseKey": "invalid-expansion-budget",
                    "entries": [{
                        "kind": "file",
                        "readableName": "a.txt",
                        "contentSize": 1,
                        "contentSha256": "0" * 64,
                    }],
                    "limits": {"expandedBytesBudget": budget},
                    "adapterChecks": [
                        {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
                    ],
                }), encoding="utf-8")

                with self.assertRaisesRegex(ValueError, message):
                    VERIFIER["verify"](directory, sys.executable)

    def test_verify_distinguishes_content_rejection_from_infrastructure_failures(self):
        # Ticket #1131: Fake adapter cases for exit0, genuine rejection, command-line
        # failure, insufficient memory, interruption, and signal termination.
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            stem = "atc-" + "c" * 64
            archive = directory / f"{stem}.zip"
            archive.write_bytes(b"PK\x03\x04corrupted")
            sidecar = directory / f"{stem}.json"
            report_path = directory / "7zip-report.json"
            data = {
                "caseKey": "malformed-case",
                "entries": [],
                "limits": {"expandedBytesBudget": 0},
                "adapterChecks": [
                    {"adapter": "7zip", "action": "test", "expectedOutcome": "failure"},
                ],
            }
            sidecar.write_text(json.dumps(data))

            def make_fake_adapter(code_body):
                script = directory / "fake_runner.py"
                script.write_text(f"import sys, os, signal\n{code_body}\n", encoding="utf-8")
                if sys.platform == "win32":
                    adapter = directory / "fake_adapter.cmd"
                    adapter.write_text(f'@"{sys.executable}" "{script}" %*\n', encoding="utf-8")
                else:
                    adapter = directory / "fake_adapter"
                    quoted_py = shlex.quote(sys.executable)
                    quoted_script = shlex.quote(str(script))
                    adapter.write_text(f'#!/bin/sh\nexec {quoted_py} {quoted_script} "$@"\n', encoding="utf-8")
                    adapter.chmod(0o755)
                return str(adapter)

            def run_adapter(adapter_path):
                return subprocess.run(
                    [sys.executable, str(SCRIPT), str(directory), adapter_path,
                     "--report", str(report_path)],
                    capture_output=True, text=True, check=False,
                )

            # 1. Exit 0 success (false-green: archive accepted when failure expected)
            adapter = make_fake_adapter("sys.exit(0)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("expected rejection", res.stderr)
            self.assertNotIn("verified", res.stdout)

            # 2. Genuine content rejection (exit 2 with diagnostic)
            adapter = make_fake_adapter("sys.stderr.write('ERROR: Data Error : doc.bin\\n')\nsys.exit(2)")
            res = run_adapter(adapter)
            self.assertEqual(0, res.returncode)
            self.assertIn("7-Zip verified 1 declared adapter checks.", res.stdout)

            # 3. Command-line failure (exit 7)
            adapter = make_fake_adapter("sys.stderr.write('Command Line Error:\\nUnknown switch\\n')\nsys.exit(7)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("command-line error", res.stderr)
            self.assertNotIn("verified", res.stdout)
            self.assertIn(
                "command-line error",
                json.loads(report_path.read_text(encoding="utf-8"))[
                    "fixtures"][0]["checks"][0]["failure"])

            # 4. Insufficient memory (exit 8)
            adapter = make_fake_adapter("sys.stderr.write('Not enough memory for operation\\n')\nsys.exit(8)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("insufficient memory", res.stderr)
            self.assertNotIn("verified", res.stdout)

            # 5. Interruption (exit 255)
            adapter = make_fake_adapter("sys.exit(255)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("interrupted", res.stderr)
            self.assertNotIn("verified", res.stdout)

            # 6. Signal termination
            if sys.platform == "win32":
                adapter = make_fake_adapter("sys.exit(137)")
                res = run_adapter(adapter)
                self.assertEqual(1, res.returncode)
                self.assertIn("exit code 137", res.stderr)
                self.assertNotIn("verified", res.stdout)
            else:
                adapter = make_fake_adapter("os.kill(os.getpid(), signal.SIGKILL)")
                res = run_adapter(adapter)
                self.assertEqual(1, res.returncode)
                self.assertIn("signal", res.stderr)
                self.assertNotIn("verified", res.stdout)

            # 7. System error (exit 2 with System ERROR diagnostic)
            adapter = make_fake_adapter("sys.stderr.write('System ERROR:\\nerrno=2 : No such file or directory\\n')\nsys.exit(2)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("system error", res.stderr)
            self.assertNotIn("verified", res.stdout)

            # 8. Unclassified exit code
            adapter = make_fake_adapter("sys.exit(4)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("exit code 4", res.stderr)
            self.assertNotIn("verified", res.stdout)

            # 9. Generic I/O / permission failure (exit 2 with generic error, not archive-specific)
            adapter = make_fake_adapter("sys.stderr.write('ERROR: Permission denied\\n')\nsys.exit(2)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("system error", res.stderr)
            self.assertNotIn("verified", res.stdout)

            # 10. Generic "archives with errors" summary without archive-specific diagnostics
            adapter = make_fake_adapter("sys.stderr.write('Archives with Errors: 1\\n')\nsys.exit(2)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("without diagnostic content error", res.stderr)
            self.assertNotIn("verified", res.stdout)

            # 11. Generic "sub items errors" summary without archive-specific diagnostics
            adapter = make_fake_adapter("sys.stderr.write('Sub items Errors: 1\\n')\nsys.exit(2)")
            res = run_adapter(adapter)
            self.assertEqual(1, res.returncode)
            self.assertIn("without diagnostic content error", res.stderr)
            self.assertNotIn("verified", res.stdout)

    def test_verify_IgnoresChecksForOtherReaderProfiles(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            stem = "atc-" + "a" * 64
            (directory / f"{stem}.zip").write_bytes(b"not-read-by-presence-check")
            (directory / f"{stem}.json").write_text(json.dumps({
                "caseKey": "reader-profile-checks",
                "adapterChecks": [
                    {"adapter": "python-zipfile", "action": "list",
                     "expectedOutcome": "list-succeeds"},
                    {"adapter": "7zip", "action": "presence",
                     "expectedOutcome": "present"},
                ],
            }), encoding="utf-8")
            report_path = directory / "report.json"

            checked = VERIFIER["verify"](
                directory, "unused-seven-zip", report_path)

            report = json.loads(report_path.read_text(encoding="utf-8"))

        self.assertEqual(1, checked)
        self.assertEqual(
            [("presence", "present")],
            [(check["action"], check["observedOutcome"])
             for check in report["fixtures"][0]["checks"]])


if __name__ == "__main__":
    unittest.main()
