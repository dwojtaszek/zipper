"""Regressions for release provenance — SHA-256 manifest, SBOM, and build attestation (#1220)."""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class ReleaseProvenanceWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.ci_workflow_path = ROOT / ".github/workflows/ci.yml"
        self.ci_text = self.ci_workflow_path.read_text(encoding="utf-8")
        self.release_workflow_path = ROOT / ".github/workflows/build-and-test.yml"
        self.release_text = self.release_workflow_path.read_text(encoding="utf-8")

        try:
            import yaml
            self.ci_yaml = yaml.safe_load(self.ci_text)
            self.release_yaml = yaml.safe_load(self.release_text)
        except ImportError:
            self.ci_yaml = None
            self.release_yaml = None

    def test_ci_upload_release_artifacts_fail_on_missing_files(self):
        """Release asset uploads in ci.yml must set if-no-files-found: error."""
        if self.ci_yaml:
            producer_steps = self.ci_yaml["jobs"]["build-and-test"]["steps"]
            upload_steps = [
                s for s in producer_steps
                if isinstance(s, dict) and str(s.get("uses", "")).startswith("actions/upload-artifact@")
            ]

            release_binaries_upload = next(
                (s for s in upload_steps if s.get("id") == "upload_release_artifact" or s.get("with", {}).get("name") == "zipper-${{ matrix.platform }}"),
                None
            )
            self.assertIsNotNone(release_binaries_upload, "Missing upload step for self-contained binaries")
            self.assertEqual(
                release_binaries_upload.get("with", {}).get("if-no-files-found"),
                "error",
                "Self-contained binaries upload must specify if-no-files-found: error"
            )

            receipt_upload = next(
                (s for s in upload_steps if s.get("with", {}).get("name") == "zipper-receipt-${{ matrix.platform }}"),
                None
            )
            self.assertIsNotNone(receipt_upload, "Missing upload step for artifact receipt")
            self.assertEqual(
                receipt_upload.get("with", {}).get("if-no-files-found"),
                "error",
                "Artifact receipt upload must specify if-no-files-found: error"
            )
        else:
            self.assertTrue(
                re.search(r"name:\s*zipper-\${{\s*matrix\.platform\s*}}.*?if-no-files-found:\s*error", self.ci_text, re.DOTALL),
                "Binary upload in ci.yml must specify if-no-files-found: error",
            )
            self.assertTrue(
                re.search(r"name:\s*zipper-receipt-\${{\s*matrix\.platform\s*}}.*?if-no-files-found:\s*error", self.ci_text, re.DOTALL),
                "Receipt upload in ci.yml must specify if-no-files-found: error",
            )

    def test_ci_failure_diagnostics_uploaded_with_extended_retention(self):
        """TestResults and diagnostics must be uploaded on failure with retention > 14 days."""
        if self.ci_yaml:
            producer_steps = self.ci_yaml["jobs"]["build-and-test"]["steps"]
            failure_uploads = [
                s for s in producer_steps
                if isinstance(s, dict)
                and str(s.get("uses", "")).startswith("actions/upload-artifact@")
                and "failure()" in str(s.get("if", ""))
            ]
            self.assertGreater(len(failure_uploads), 0, "Missing failure diagnostic upload-artifact step in build-and-test")

            diagnostic_step = failure_uploads[0]
            step_with = diagnostic_step.get("with", {})
            retention = int(step_with.get("retention-days", 0))
            self.assertGreater(retention, 14, f"Failure diagnostics retention ({retention}) must be longer than 14-day advisory artifacts")

            path_val = step_with.get("path", "")
            self.assertIn("TestResults", path_val, "Failure diagnostics must collect TestResults")
            self.assertIn("test_output", path_val, "Failure diagnostics must collect test_output")
            self.assertIn("results", path_val, "Failure diagnostics must collect Unix E2E results")
        else:
            m = re.search(r"if:\s*failure\(\).*?retention-days:\s*(\d+)", self.ci_text, re.DOTALL)
            self.assertIsNotNone(m, "Failure diagnostics must be conditioned on failure() and specify retention-days")
            retention = int(m.group(1))
            self.assertGreater(retention, 14)
            self.assertIn("TestResults", self.ci_text)
            self.assertIn("test_output", self.ci_text)
            self.assertIn("results", self.ci_text)

    def test_publication_job_has_scoped_oidc_permissions(self):
        """tag-and-release job must have scoped id-token: write and attestations: write permissions."""
        if self.release_yaml:
            tag_release_job = self.release_yaml["jobs"]["tag-and-release"]
            perms = tag_release_job.get("permissions", {})
            self.assertEqual(perms.get("id-token"), "write", "tag-and-release job missing id-token: write permission")
            self.assertEqual(perms.get("attestations"), "write", "tag-and-release job missing attestations: write permission")
        else:
            self.assertTrue(
                re.search(r"tag-and-release:.*?permissions:.*?id-token:\s*write.*?attestations:\s*write", self.release_text, re.DOTALL),
                "tag-and-release job missing id-token: write and attestations: write permissions",
            )

    def test_release_publishes_build_attestation(self):
        """tag-and-release job must generate build provenance attestation covering all binary assets."""
        if self.release_yaml:
            steps = self.release_yaml["jobs"]["tag-and-release"]["steps"]
            attest_steps = [
                s for s in steps
                if isinstance(s, dict) and "actions/attest-build-provenance@" in str(s.get("uses", ""))
            ]
            self.assertEqual(len(attest_steps), 1, "tag-and-release job must include actions/attest-build-provenance step")

            subject_paths = attest_steps[0].get("with", {}).get("subject-path", "")
            self.assertIn("zipper-linux-x64", subject_paths)
            self.assertIn("zipper-win-x64.exe", subject_paths)
            self.assertIn("zipper-osx-arm64", subject_paths)
        else:
            self.assertIn("actions/attest-build-provenance@", self.release_text)
            self.assertIn("artifacts/zipper-linux-x64/zipper-linux-x64", self.release_text)
            self.assertIn("artifacts/zipper-win-x64/zipper-win-x64.exe", self.release_text)
            self.assertIn("artifacts/zipper-osx-arm64/zipper-osx-arm64", self.release_text)

    def test_release_generates_sbom(self):
        """tag-and-release job must generate an SBOM in SPDX JSON format scoped to artifacts."""
        if self.release_yaml:
            steps = self.release_yaml["jobs"]["tag-and-release"]["steps"]
            sbom_steps = [
                s for s in steps
                if isinstance(s, dict) and ("anchore/sbom-action@" in str(s.get("uses", "")) or "sbom" in str(s.get("name", "")).lower())
            ]
            self.assertGreater(len(sbom_steps), 0, "tag-and-release job must include an SBOM generation step")
            sbom_with = sbom_steps[0].get("with", {})
            self.assertEqual(sbom_with.get("format"), "spdx-json")
            self.assertEqual(sbom_with.get("output-file"), "zipper.spdx.json")
            self.assertEqual(sbom_with.get("path"), "artifacts")
        else:
            self.assertIn("anchore/sbom-action@", self.release_text)
            self.assertTrue(re.search(r"format:\s*spdx-json", self.release_text), "SBOM format must be spdx-json")
            self.assertTrue(re.search(r"output-file:\s*zipper\.spdx\.json", self.release_text), "SBOM output-file must be zipper.spdx.json")
            self.assertTrue(re.search(r"path:\s*artifacts", self.release_text), "SBOM path must be artifacts")

    def test_release_publishes_checksum_manifest_and_provenance(self):
        """tag-and-release job must generate a SHA-256 manifest and upload provenance assets to the release."""
        if self.release_yaml:
            steps = self.release_yaml["jobs"]["tag-and-release"]["steps"]
            manifest_steps = [
                s for s in steps
                if isinstance(s, dict) and "SHA256SUMS" in str(s.get("run", "")) and "gh release upload" not in str(s.get("run", ""))
            ]
            self.assertEqual(len(manifest_steps), 1, "tag-and-release job must generate SHA256SUMS manifest")

            upload_provenance_steps = [
                s for s in steps
                if isinstance(s, dict)
                and "gh release upload" in str(s.get("run", ""))
                and "SHA256SUMS" in str(s.get("run", ""))
                and "zipper.spdx.json" in str(s.get("run", ""))
            ]
            self.assertEqual(len(upload_provenance_steps), 1, "tag-and-release job must upload SHA256SUMS and SBOM to the release")
        else:
            self.assertIn("SHA256SUMS", self.release_text)
            self.assertTrue(
                re.search(r"gh release upload.*?SHA256SUMS.*?zipper\.spdx\.json", self.release_text, re.DOTALL),
                "Provenance upload step must upload SHA256SUMS and zipper.spdx.json",
            )


if __name__ == "__main__":
    unittest.main()
