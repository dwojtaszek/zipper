"""Regressions for coverage gate action branch and line coverage thresholds (#1269)."""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class CoverageGateActionTests(unittest.TestCase):
    def setUp(self):
        self.action_path = ROOT / ".github" / "actions" / "coverage-gate" / "action.yml"
        self.action_text = self.action_path.read_text(encoding="utf-8")

    def _get_coverage_step_run(self) -> str:
        """Extract the main coverage report + gate run script from action.yml."""
        lines = self.action_text.splitlines()
        in_run = False
        run_indent = None
        run_lines = []
        for line in lines:
            stripped = line.strip()
            if not in_run:
                if stripped.startswith("run: |"):
                    in_run = True
                continue
            if not stripped:
                run_lines.append("")
                continue
            indent = len(line) - len(line.lstrip())
            if run_indent is None:
                run_indent = indent
            if indent < run_indent:
                break
            run_lines.append(line[run_indent:])
        return "\n".join(run_lines)

    def test_action_yml_contains_branch_coverage_gate_check(self):
        """action.yml must extract Branch coverage and enforce an 85% floor (#1269)."""
        self.assertIn("Branch coverage:", self.action_text)
        self.assertRegex(
            self.action_text,
            r"BRANCH_COVERAGE=.*grep\s+[\"']Branch coverage:[\"']",
            "action.yml must extract BRANCH_COVERAGE from Summary.txt",
        )
        self.assertRegex(
            self.action_text,
            r"awk\s+-v\s+c=[\"']?\$BRANCH_COVERAGE[\"']?\s+'BEGIN\s*\{\s*exit\s*\(c\+0\s*<\s*85\)",
            "action.yml must enforce the calibrated 85% branch coverage floor",
        )

    @unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
    def test_coverage_gate_execution_matrix(self):
        """Execute the extracted coverage gate run script against various Summary.txt inputs."""
        script = self._get_coverage_step_run()
        self.assertTrue(len(script) > 0, "Could not extract run script from coverage-gate/action.yml")

        # Strip GitHub expression contexts e.g. ${{ github.workspace }}
        script = re.sub(r"\$\{\{[^}]+\}\}", "dummy_workspace", script)

        cases = [
            # (name, line_cov, branch_cov, py_exit, expected_exit, expected_err_pattern)
            ("Passing both gates", "96.4%", "88.9%", 0, 0, None),
            ("Passing boundary gates", "80.0%", "85.0%", 0, 0, None),
            ("Passing integer boundary gates", "80%", "85%", 0, 0, None),
            ("Passing 100% coverage", "100%", "100%", 0, 0, None),
            ("Fails line coverage floor", "79.9%", "88.9%", 0, 1, r"ERROR: total coverage 79\.9% < 80%"),
            ("Fails branch coverage floor", "96.4%", "84.9%", 0, 1, r"ERROR: total branch coverage 84\.9% < 85%"),
            ("Missing line coverage", None, "88.9%", 0, 1, r"ERROR: could not extract line coverage from Summary\.txt"),
            ("Missing branch coverage", "96.4%", None, 0, 1, r"ERROR: could not extract branch coverage from Summary\.txt"),
            ("Per-file coverage failure", "96.4%", "88.9%", 2, 2, None),
        ]

        for name, line_cov, branch_cov, py_exit, expected_exit, expected_err_pattern in cases:
            with self.subTest(case=name):
                with tempfile.TemporaryDirectory() as td:
                    bin_dir = os.path.join(td, "bin")
                    os.makedirs(bin_dir)

                    summary_dir = os.path.join(td, "TestResults", "coverage-report")
                    os.makedirs(summary_dir)

                    summary_content = ["Summary"]
                    if line_cov is not None:
                        summary_content.append(f"  Line coverage: {line_cov}")
                    if branch_cov is not None:
                        summary_content.append(f"  Branch coverage: {branch_cov} (100 of 120)")
                    with open(os.path.join(summary_dir, "Summary.txt"), "w", encoding="utf-8") as sf:
                        sf.write("\n".join(summary_content) + "\n")

                    # Mock dotnet: tool restore and reportgenerator succeed
                    mock_dotnet = os.path.join(bin_dir, "dotnet")
                    with open(mock_dotnet, "w", encoding="utf-8") as f:
                        f.write("#!/bin/sh\nexit 0\n")
                    os.chmod(mock_dotnet, 0o755)

                    # Mock python3: check-per-file-coverage.py
                    mock_py = os.path.join(bin_dir, "python3")
                    with open(mock_py, "w", encoding="utf-8") as f:
                        f.write(f"#!/bin/sh\nexit {py_exit}\n")
                    os.chmod(mock_py, 0o755)

                    # Create dummy python script target so bash doesn't complain if checked
                    os.makedirs(os.path.join(td, ".github", "actions", "coverage-gate"), exist_ok=True)
                    Path(td, ".github", "actions", "coverage-gate", "check-per-file-coverage.py").touch()

                    env = os.environ.copy()
                    env["PATH"] = f"{bin_dir}:{env.get('PATH', '')}"

                    proc = subprocess.run(
                        ["bash", "-eo", "pipefail", "-c", script],
                        cwd=td,
                        env=env,
                        capture_output=True,
                        text=True,
                    )

                    self.assertEqual(
                        proc.returncode,
                        expected_exit,
                        f"Case '{name}' expected exit code {expected_exit}, got {proc.returncode}.\nstdout: {proc.stdout}\nstderr: {proc.stderr}",
                    )

                    if expected_err_pattern:
                        combined_output = proc.stdout + "\n" + proc.stderr
                        self.assertRegex(
                            combined_output,
                            expected_err_pattern,
                            f"Case '{name}' expected error pattern '{expected_err_pattern}' not found.\nOutput: {combined_output}",
                        )


if __name__ == "__main__":
    unittest.main()
