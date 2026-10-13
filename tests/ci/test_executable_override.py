"""A supplied executable must never trigger a build or fallback."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


@unittest.skipIf(os.name == "nt", "POSIX subprocess boundary")
class ExecutableOverrideTests(unittest.TestCase):
    def test_override_missingOrRelative_failsWithoutCallingDotnet(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dotnet = root / "dotnet"
            dotnet.write_text(f"#!/bin/sh\ntouch '{root / 'build-called'}'\nexit 0\n")
            dotnet.chmod(0o755)
            for supplied in (str(root / "missing"), "relative", ""):
                env = {**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"],
                       "ZIPPER_EXECUTABLE": supplied}
                for script in ("_zipper-cli.sh", "run-e2e-basic.sh", "run-e2e-loadfile.sh", "test-published-artifact.sh"):
                    if script == "_zipper-cli.sh":
                        args = ["bash", "-c", 'source "$1"; zipper --version', "fixture", str(ROOT / "tests" / script)]
                    else:
                        args = ["bash", str(ROOT / "tests" / script)]
                    result = subprocess.run(args, cwd=root, env=env, capture_output=True, text=True)
                    self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                    self.assertFalse((root / "build-called").exists())

    def test_override_pathWithSpaces_executesOnlySuppliedFile(self):
        with tempfile.TemporaryDirectory(prefix="zipper binary ") as directory:
            binary = Path(directory) / "native executable"
            binary.write_text("#!/bin/sh\nprintf 'supplied:%s\\n' \"$1\"\n")
            binary.chmod(0o755)
            result = subprocess.run(["bash", "-c", 'source "$1"; zipper --version',
                                     "fixture", str(ROOT / "tests/_zipper-cli.sh")],
                                    env={**os.environ, "ZIPPER_EXECUTABLE": str(binary)},
                                    capture_output=True, text=True)
            self.assertEqual((result.returncode, result.stdout), (0, "supplied:--version\n"))
