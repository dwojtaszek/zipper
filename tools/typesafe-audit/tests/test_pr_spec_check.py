"""Tests for the PR specification compliance side check (#958)."""

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOL_DIR = REPO_ROOT / "tools" / "typesafe-audit"
CHECK_DIR = TOOL_DIR / "checks" / "pr-spec"
CORPUS = REPO_ROOT / "tests" / "typesafe-audit-fixtures" / "pr-spec"


def load_module(name: str, path: Path):
    # Explicit-path loading avoids module-name collisions between checks.
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class CollectorTests(unittest.TestCase):
    """Pure collection functions: refs, filtering, REQ discovery, supersede order."""

    @classmethod
    def setUpClass(cls):
        cls.collect = load_module("pr_spec_collect", CHECK_DIR / "collect.py")

    def test_extract_issue_refs_explicit_keywords_only(self):
        body = "Fixes #927. Closes #930.\nBranch name feat/ISSUE-744-x must be ignored. Related: #404 is not a closure ref."
        refs = self.collect.extract_issue_refs(body)
        self.assertEqual(refs, [927, 930])

    def test_extract_issue_refs_case_insensitive_and_unique(self):
        self.assertEqual(self.collect.extract_issue_refs("resolves #12, FIXES #12, closes #13"), [12, 13])

    def test_filter_files_excludes_binaries_generated_and_secrets(self):
        files = [
            {"path": "src/Cli/Program.cs", "additions": 10},
            {"path": "src/bin/Debug/net10.0/Zipper.dll", "additions": 0},
            {"path": "results/output.zip", "additions": 0},
            {"path": "logo.png", "additions": 0},
            {"path": ".env", "additions": 3},
            {"path": "docs/advanced-guide.md", "additions": 5},
        ]
        kept = self.collect.filter_files(files)
        self.assertEqual([f["path"] for f in kept], ["src/Cli/Program.cs", "docs/advanced-guide.md"])

    def test_filter_files_enforces_size_budget(self):
        files = [{"path": f"src/File{i}.cs", "additions": 5} for i in range(10)]
        kept = self.collect.filter_files(files, max_files=3)
        self.assertEqual(len(kept), 3)

    def test_affected_req_ids_from_patches_tsv_and_refs(self):
        files = [
            {"path": "src/ManifestComparison/ProductionManifestComparer.cs", "patch": "+ REQ-178 stuff"},
            {"path": "tests/req-traceability.tsv", "patch": "+ REQ-179\tintegration\tscript.sh\tX"},
        ]
        reqs = self.collect.affected_req_ids(
            files, pr_text="Fixes #950", issue_texts={950: "REQ-179 semantics"},
            repo_root=REPO_ROOT,
        )
        self.assertIn("REQ-178", reqs)
        self.assertIn("REQ-179", reqs)

    def test_spec_evidence_chronological_with_newest_flagged(self):
        issues = {
            42: {
                "number": 42,
                "body": "original spec",
                "comments": [
                    {"order": 1, "author": "a", "body": "intermediate note"},
                    {"order": 2, "author": "maintainer", "body": "newest explicit design decision: use flag X"},
                ],
            }
        }
        evidence = self.collect.spec_evidence(issues, [42])
        self.assertEqual([e["order"] for e in evidence], [0, 1, 2])
        self.assertTrue(evidence[-1]["supersedes_older"])
        self.assertFalse(evidence[0]["supersedes_older"])


class RunCheckTests(unittest.TestCase):
    """Corpus mode end to end, recorded responses, status policy, no-spec neutrality."""

    @classmethod
    def setUpClass(cls):
        cls.run_check = load_module("pr_spec_run_check", CHECK_DIR / "run_check.py")

    def _run_corpus(self, tmp: Path, extra: list[str] | None = None):
        fixture_dir = Path(tempfile.mkdtemp())
        # Record expected responses for the corpus cases (same pure builders as
        # run_check): validates the request/fixture pipeline deterministically.
        import subprocess

        subprocess.run(
            [sys.executable, str(CHECK_DIR / "record_corpus_fixtures.py"), str(fixture_dir)],
            check=True,
        )
        self.run_check.main([
            "--corpus", str(CORPUS),
            "--mode", "fixture", "--fixture-dir", str(fixture_dir),
            "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            *(extra or []),
        ])

    def test_corpus_labels_reproduced_with_recorded_responses(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            self._run_corpus(tmp)
            report = json.loads((tmp / "r.json").read_text())
            expected = {
                e["pr"]: e["expected"] for e in json.loads((CORPUS / "expected_labels.json").read_text())["examples"]
            }
            by_pr = {r["pr"]: r for r in report["results"]}
            for pr, want in expected.items():
                for qid, status in want["statuses"].items():
                    self.assertEqual(by_pr[pr]["answers"][qid]["status"], status, f"{pr}#{qid}")

    def test_report_json_is_stable_byte_for_byte(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            self._run_corpus(tmp)
            first = (tmp / "r.json").read_bytes()
            (tmp / "r.json").unlink()
            self._run_corpus(tmp)
            self.assertEqual(first, (tmp / "r.json").read_bytes())

    def test_no_explicit_spec_is_neutral_not_failure(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixture_dir = Path(tempfile.mkdtemp())
            code = self.run_check.main([
                "--pr-json", str(CORPUS / "cases" / "no-spec.json"),
                "--mode", "fixture", "--fixture-dir", str(fixture_dir),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            self.assertEqual(code, self.run_check.EXIT_OK)
            report = json.loads((tmp / "r.json").read_text())
            # Neutral: no obligation judged, spec status reported, not a failure.
            self.assertEqual(len(report["results"]), 1)
            self.assertEqual(report["results"][0]["answers"], {})
            self.assertEqual(report["spec_status"], "no-explicit-spec")

    def test_low_confidence_routes_to_needs_human_review(self):
        status = self.run_check.status_for(
            "implementation",
            {"type": "choice", "answer": "contradicted", "confidence": 0.3},
            self.run_check.load_json(self.run_check.POLICY_PATH),
        )
        self.assertEqual(status, "needs-human-review")

    def test_high_confidence_unrelated_behavior_is_finding(self):
        status = self.run_check.status_for(
            "unrelated_behavior",
            {"type": "noul", "answer": 0.95, "confidence": 0.9},
            self.run_check.load_json(self.run_check.POLICY_PATH),
        )
        self.assertEqual(status, "finding")

    def test_summary_out_appends_markdown(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            summary = tmp / "summary.md"
            summary.write_text("existing\n")
            self._run_corpus(tmp, ["--summary-out", str(summary)])
            text = summary.read_text()
            self.assertTrue(text.startswith("existing\n"))
            self.assertIn("PR Specification Compliance", text)


class WorkflowWiringTests(unittest.TestCase):
    """The workflow carries the independent pr-spec-review job with least privilege."""

    @classmethod
    def setUpClass(cls):
        cls.wf = (REPO_ROOT / ".github" / "workflows" / "typesafe-audit.yml").read_text()

    def test_pr_spec_review_job_declared(self):
        self.assertIn("pr-spec-review:", self.wf)

    def test_least_privilege_permissions(self):
        job = self.wf.split("pr-spec-review:", 1)[1]
        self.assertIn("contents: read", job)
        self.assertIn("pull-requests: read", job)
        self.assertIn("issues: read", job)
        self.assertNotIn("pull-requests: write", job)

    def test_fork_prs_skip(self):
        job = self.wf.split("pr-spec-review:", 1)[1]
        self.assertIn("github.event.pull_request.head.repo.fork", job)

    def test_uses_recorded_fixture_corpus_offline(self):
        job = self.wf.split("pr-spec-review:", 1)[1]
        self.assertIn("--corpus", job)


if __name__ == "__main__":
    unittest.main()
