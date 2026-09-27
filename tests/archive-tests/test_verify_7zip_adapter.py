import hashlib
import json
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

            self.assertEqual(0, run().returncode)
            data["entries"][0]["contentSha256"] = "0" * 64
            sidecar.write_text(json.dumps(data))
            self.assertIn("content hash mismatch", run().stderr)

            data["entries"][0]["contentSha256"] = hashlib.sha256(b"actual content").hexdigest()
            data["limits"]["expandedBytesBudget"] = 1
            sidecar.write_text(json.dumps(data))
            self.assertIn("expanded bytes budget exceeded", run().stderr)

            data["limits"]["expandedBytesBudget"] = len(b"actual content")
            data["entries"][0]["readableName"] = "-y"
            sidecar.write_text(json.dumps(data))
            self.assertIn("unsafe entry name", run().stderr)

            data["entries"][0]["readableName"] = "@files.txt"
            sidecar.write_text(json.dumps(data))
            self.assertIn("unsafe entry name", run().stderr)

            data["adapterChecks"] = []
            sidecar.write_text(json.dumps(data))
            self.assertIn("no 7-Zip adapter checks", run().stderr)


if __name__ == "__main__":
    unittest.main()
