"""Regressions for PR workflow requirement traceability gating (#1215)."""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class PrWorkflowTraceabilityTests(unittest.TestCase):
    def setUp(self):
        self.pr_workflow_path = ROOT / ".github/workflows/pr.yml"
        self.workflow_text = self.pr_workflow_path.read_text(encoding="utf-8")

    def test_docs_only_job_runs_deterministic_traceability_gate(self):
        """Docs-only PRs must run validate-req-traceability.sh --strict so REQ immutability is enforced."""
        try:
            import yaml
            parsed = yaml.safe_load(self.workflow_text)
        except ImportError:
            parsed = None

        if parsed:
            docs_only_job = parsed.get("jobs", {}).get("docs-only", {})
            steps = docs_only_job.get("steps", [])
            traceability_steps = [
                s for s in steps
                if "validate-req-traceability.sh --strict" in s.get("run", "")
            ]
            self.assertEqual(
                len(traceability_steps),
                1,
                "docs-only job must have exactly one step running validate-req-traceability.sh --strict",
            )
            step = traceability_steps[0]
            condition = step.get("if", "")
            self.assertIn(
                "steps.check.outputs.only_docs == 'true'",
                condition,
                "Traceability step in docs-only job must be conditioned on only_docs == 'true'",
            )
        else:
            self.assertIn("validate-req-traceability.sh --strict", self.workflow_text)
            self.assertIn("steps.check.outputs.only_docs == 'true'", self.workflow_text)

    def test_docs_only_job_runs_docs_drift_tests(self):
        """Docs-only PRs must run DocsDriftTests so terminology/README/flag drift is caught before merge."""
        try:
            import yaml
            parsed = yaml.safe_load(self.workflow_text)
        except ImportError:
            parsed = None

        if parsed:
            docs_only_job = parsed.get("jobs", {}).get("docs-only", {})
            steps = docs_only_job.get("steps", [])
            drift_steps = [
                s for s in steps
                if "DocsDriftTests" in s.get("run", "")
            ]
            self.assertEqual(
                len(drift_steps),
                1,
                "docs-only job must have a step running DocsDriftTests",
            )
            step = drift_steps[0]
            condition = step.get("if", "")
            self.assertIn(
                "steps.check.outputs.only_docs == 'true'",
                condition,
                "DocsDriftTests step in docs-only job must be conditioned on only_docs == 'true'",
            )
        else:
            self.assertIn("DocsDriftTests", self.workflow_text)

    def test_ci_gate_depends_on_docs_only_job(self):
        """ci-gate must depend on docs-only job so a failure in docs-only blocks merge."""
        try:
            import yaml
            parsed = yaml.safe_load(self.workflow_text)
        except ImportError:
            parsed = None

        if parsed:
            ci_gate = parsed.get("jobs", {}).get("ci-gate", {})
            needs = ci_gate.get("needs", [])
            self.assertIn("docs-only", needs, "ci-gate must list docs-only in its needs dependencies")
        else:
            self.assertTrue(
                re.search(r"ci-gate:.*?needs:\s*\[[^\]]*docs-only", self.workflow_text, re.DOTALL),
                "ci-gate must list docs-only in its needs dependencies",
            )

    @unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
    def test_traceability_script_detects_stale_tsv_row_on_deleted_req(self):
        """If a REQ is deleted/renumbered in Requirements.md but remains in TSV, gate must fail strict."""
        with tempfile.TemporaryDirectory() as tmp:
            tmp_root = Path(tmp)
            reqs = tmp_root / "Requirements.md"
            reqs.write_text("- **REQ-001**: Active requirement.\n", encoding="utf-8")

            tsv = tmp_root / "req-traceability.tsv"
            tsv.write_text(
                "req_id\tcoverage\treference\tnotes\n"
                "REQ-001\texemption\t-\tValid rationale\n"
                "REQ-999\texemption\t-\tStale deleted requirement\n",
                encoding="utf-8",
            )

            tests_dir = tmp_root / "tests"
            tests_dir.mkdir()

            script_path = ROOT / "tests/validate-req-traceability.sh"
            result = subprocess.run(
                [
                    "bash",
                    str(script_path),
                    "--strict",
                    "--requirements",
                    str(reqs),
                    "--tsv",
                    str(tsv),
                    "--repo-root",
                    str(tmp_root),
                    "--tests-root",
                    str(tmp_root),
                ],
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertEqual(1, result.returncode, f"Expected exit code 1, got {result.returncode}. Output:\n{result.stdout}\n{result.stderr}")
            self.assertIn("unknown manifest requirement references found", result.stderr)
            self.assertIn("REQ-999", result.stdout)


if __name__ == "__main__":
    unittest.main()
