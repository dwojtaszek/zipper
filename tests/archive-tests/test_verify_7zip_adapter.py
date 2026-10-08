import hashlib
import json
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPT = Path(__file__).with_name("verify-7zip-adapter.py")


class SevenZipAdapterTests(unittest.TestCase):
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
            sidecar = directory / f"{stem}.json"
            data = {
                "caseKey": "control",
                "entries": [{
                    "kind": "file",
                    "readableName": "a.txt",
                    "contentSha256": hashlib.sha256(b"actual content").hexdigest(),
                    "contentSize": len(b"actual content"),
                }],
                "limits": {"expandedBytesBudget": len(b"actual content")},
                "adapterChecks": [
                    {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
                    {"adapter": "7zip", "action": "presence", "expectedOutcome": "present"},
                ],
            }
            sidecar.write_text(json.dumps(data))

            def run():
                return subprocess.run(
                    [sys.executable, str(SCRIPT), str(directory), seven],
                    capture_output=True, text=True, check=False,
                )

            res = run()
            self.assertEqual(0, res.returncode)

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

            data["limits"]["expandedBytesBudget"] = len(b"actual content")
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
            data["adapterChecks"] = [
                {"adapter": "7zip", "action": "extract", "expectedOutcome": "success"},
                {"adapter": "7zip", "action": "presence", "expectedOutcome": "present"},
            ]
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
            self.assertIn("7-Zip verified 3 declared adapter checks.", res.stdout)

            # If real malformed check is given a valid archive, verifier fails closed
            with zipfile.ZipFile(archive_malformed, "w") as valid_output:
                valid_output.writestr("b.txt", b"valid content")
            res = run()
            self.assertEqual(1, res.returncode)
            self.assertIn("expected rejection", res.stderr)

    def test_verify_distinguishes_content_rejection_from_infrastructure_failures(self):
        # Ticket #1131: Fake adapter cases for exit0, genuine rejection, command-line
        # failure, insufficient memory, interruption, and signal termination.
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            stem = "atc-" + "c" * 64
            archive = directory / f"{stem}.zip"
            archive.write_bytes(b"PK\x03\x04corrupted")
            sidecar = directory / f"{stem}.json"
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
                    [sys.executable, str(SCRIPT), str(directory), adapter_path],
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


if __name__ == "__main__":
    unittest.main()
