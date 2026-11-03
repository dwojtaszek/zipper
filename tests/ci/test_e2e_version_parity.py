"""Regression: the archive reference harness must build with the candidate version.

Main publishes a versioned build (``-p:Version=<inputs.version>``), so the
Expectation Files it generates carry that ``generatorVersion``. The reference
harness regenerates the same fixtures in-process, so it must be built with the
same version or its Expectation File hashes never match the published catalogue.
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class E2eVersionParityTests(unittest.TestCase):
    def test_e2e_steps_export_the_build_version(self):
        ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        for step in ("Run full E2E suite (Unix)", "Run full E2E suite (Windows)"):
            with self.subTest(step=step):
                match = re.search(
                    r"name:\s*" + re.escape(step) + r"\n(.*?)(?=\n\s{6}- name:|\Z)",
                    ci,
                    re.DOTALL,
                )
                self.assertIsNotNone(match, f"Could not find step '{step}'")
                self.assertIn("BUILD_VERSION: ${{ inputs.version }}", match.group(1))

    def test_archive_suite_passes_the_version_to_dotnet_test(self):
        sh = (ROOT / "tests" / "test-archive-test-suites.sh").read_text(encoding="utf-8")
        bat = (ROOT / "tests" / "test-archive-test-suites.bat").read_text(encoding="utf-8")
        for name, text, flag, version_ref in (
            ("sh", sh, "$DOTNET_VERSION_FLAG", "${BUILD_VERSION}"),
            ("bat", bat, "%DOTNET_VERSION_FLAG%", "%BUILD_VERSION%"),
        ):
            with self.subTest(script=name):
                self.assertIn(f"-p:Version={version_ref}", text)
                # Join backslash line continuations so a multi-line invocation
                # (the .sh twin) reads as one command.
                logical = re.sub(r"\\\s*\n", " ", text)
                invocation = re.search(r"dotnet test[^\n]*", logical)
                self.assertIsNotNone(invocation, f"dotnet test invocation missing in {name}")
                self.assertIn(flag, invocation.group(0))


if __name__ == "__main__":
    unittest.main()
