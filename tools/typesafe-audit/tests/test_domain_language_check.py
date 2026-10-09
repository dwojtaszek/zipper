"""Unit tests for the semantic domain language lint check.

Naming follows the repo convention: {Method}_{Scenario}_{Expected}.
"""

import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

CHECK_DIR = Path(__file__).resolve().parents[1] / "checks" / "domain_language"
TOOL_DIR = Path(__file__).resolve().parents[1]

sys.path.insert(0, str(TOOL_DIR))

spec = importlib.util.spec_from_file_location("tsa_dl_check", CHECK_DIR / "run_check.py")
dl_check = importlib.util.module_from_spec(spec)
sys.modules["tsa_dl_check"] = dl_check
spec.loader.exec_module(dl_check)


class AliasDetectionTests(unittest.TestCase):
    def test_detect_known_aliases_withProhibitedTerms_ExpectedDetected(self):
        text = "This uses rolling sets, target size, redacted mode, and an audit log."
        detected = dl_check.detect_known_aliases(text)
        self.assertIn("rolling sets", detected)
        self.assertIn("target size", detected)
        self.assertIn("redacted mode", detected)
        self.assertIn("audit log", detected)

    def test_detect_known_aliases_withCanonicalTerms_ExpectedEmpty(self):
        text = "This uses Rolling Production Set, Target Zip Size, Redacted Production, and Audit File."
        self.assertEqual(dl_check.detect_known_aliases(text), [])

    def test_detect_known_aliases_withCanonicalTargetZipSize_ExpectedEmpty(self):
        for variant in ("Target Zip Size", "target zip size", "TARGET ZIP SIZE"):
            with self.subTest(variant=variant):
                self.assertEqual(
                    dl_check.detect_known_aliases(variant),
                    [],
                    f"Canonical '{variant}' must never be detected as prohibited alias",
                )

    def test_detect_known_aliases_withCanonicalTermsIndividually_ExpectedEmpty(self):
        canonical_terms = [
            "Target Zip Size",
            "Target Zip Sizes",
            "Rolling Production Set",
            "Redacted Production",
            "Audit File",
            "Volume",
            "Volume Size",
            "Archive",
            "Native File",
            "Load File",
            "Production Set",
            "Production Manifest",
            "Bates Number",
            "Chaos Engine",
        ]
        for term in canonical_terms:
            for variant in (term, term.lower(), term.upper(), term.title()):
                with self.subTest(term=term, variant=variant):
                    self.assertEqual(
                        dl_check.detect_known_aliases(variant),
                        [],
                        f"Canonical term '{variant}' was detected as prohibited alias",
                    )

    def test_detect_known_aliases_withCoOccurringCanonicalAndAlias_ExpectedOnlyAliasDetected(self):
        text = "The system uses Target Zip Size instead of target size or goal size."
        detected = dl_check.detect_known_aliases(text)
        self.assertEqual(detected, ["goal size", "target size"])


    def test_detect_known_aliases_withTrueAliasesIndependently_ExpectedDetected(self):
        true_aliases = [
            ("target size", "target size"),
            ("TARGET SIZE", "target size"),
            ("target sizes", "target sizes"),
            ("goal size", "goal size"),
            ("GOAL SIZE", "goal size"),
            ("goal sizes", "goal sizes"),
            ("rolling set", "rolling set"),
            ("rolling sets", "rolling sets"),
            ("redacted mode", "redacted mode"),
            ("REDACTED MODE", "redacted mode"),
            ("audit log", "audit log"),
            ("audit logs", "audit logs"),
        ]
        for text, expected in true_aliases:
            with self.subTest(text=text, expected=expected):
                detected = dl_check.detect_known_aliases(text)
                self.assertIn(
                    expected,
                    detected,
                    f"True alias '{text}' was not detected as '{expected}'",
                )




class ChoiceStatusTests(unittest.TestCase):
    def test_status_for_choice_withAlias_ExpectedFinding(self):
        policy = {"confidence_threshold": 0.7, "finding_choices": {"canonical_terminology": ["non_canonical_alias"]}}
        status = dl_check.status_for_choice("non_canonical_alias", 0.95, policy)
        self.assertEqual(status, "finding")

    def test_status_for_choice_withCanonical_ExpectedOk(self):
        policy = {"confidence_threshold": 0.7, "finding_choices": {"canonical_terminology": ["non_canonical_alias"]}}
        status = dl_check.status_for_choice("canonical", 0.95, policy)
        self.assertEqual(status, "ok")

    def test_status_for_choice_withLowConfidence_ExpectedNeedsHumanReview(self):
        policy = {"confidence_threshold": 0.7, "finding_choices": {"canonical_terminology": ["non_canonical_alias"]}}
        status = dl_check.status_for_choice("canonical", 0.40, policy)
        self.assertEqual(status, "needs-human-review")

    def test_status_for_choice_withAmbiguous_ExpectedNeedsHumanReview(self):
        policy = {"confidence_threshold": 0.7, "finding_choices": {"canonical_terminology": ["non_canonical_alias"]}}
        status = dl_check.status_for_choice("ambiguous", 0.95, policy)
        self.assertEqual(status, "needs-human-review")


class CorpusEvaluationTests(unittest.TestCase):
    def test_corpus_labels_reproduced_with_recorded_responses(self):
        corpus_dir = Path(__file__).resolve().parents[3] / "tests" / "typesafe-audit-fixtures" / "domain-language"
        recorder_script = CHECK_DIR / "record_corpus_fixtures.py"
        with tempfile.TemporaryDirectory() as fixture_tmp, tempfile.TemporaryDirectory() as out_tmp:
            fixture_dir = Path(fixture_tmp)
            subprocess.run([sys.executable, str(recorder_script), str(fixture_dir)], check=True)

            json_out = Path(out_tmp) / "report.json"
            md_out = Path(out_tmp) / "report.md"
            code = dl_check.main([
                "--corpus", str(corpus_dir),
                "--mode", "fixture",
                "--fixture-dir", str(fixture_dir),
                "--json-out", str(json_out),
                "--md-out", str(md_out),
            ])
            self.assertEqual(code, dl_check.EXIT_OK)

            report = json.loads(json_out.read_text(encoding="utf-8"))
            expected_labels = json.loads((corpus_dir / "expected_labels.json").read_text(encoding="utf-8"))
            expected_by_case = {ex["case"]: ex["expected"] for ex in expected_labels["examples"]}

            for r in report["results"]:
                cname = r["name"]
                expected = expected_by_case[cname]
                self.assertEqual(r["status"], expected["status"], f"Status mismatch for {cname}")
                self.assertEqual(r["choice"], expected["choice"], f"Choice mismatch for {cname}")
                if cname == "canonical":
                    self.assertEqual(r["detected_aliases"], [])
                elif cname == "alias":
                    self.assertIn("target size", r["detected_aliases"])

    def test_report_canonical_input_leaves_detected_aliases_empty(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            canonical_file = tmp / "canonical_input.txt"
            canonical_file.write_text(
                "The archive uses Target Zip Size to regulate archive padding without aliases.",
                encoding="utf-8",
            )
            json_out = tmp / "report.json"
            md_out = tmp / "report.md"

            fixture_answer = {
                "answers": {
                    "canonical_terminology": {
                        "choice": "canonical",
                        "confidence": 0.95,
                        "probabilities": {"canonical": 0.95, "non_canonical_alias": 0.05},
                    }
                }
            }

            with mock.patch.object(dl_check.runner, "run_fixture", return_value=fixture_answer):
                code = dl_check.main([
                    "--text-file", str(canonical_file),
                    "--mode", "fixture",
                    "--json-out", str(json_out),
                    "--md-out", str(md_out),
                ])
                self.assertEqual(code, dl_check.EXIT_OK)

            report = json.loads(json_out.read_text(encoding="utf-8"))
            self.assertEqual(len(report["results"]), 1)
            result = report["results"][0]
            self.assertEqual(result["status"], "ok")
            self.assertEqual(result["choice"], "canonical")
            self.assertEqual(result["detected_aliases"], [])

    def test_report_alias_input_populates_detected_aliases(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            alias_file = tmp / "alias_input.txt"
            alias_file.write_text(
                "When setting target size and goal size, rolling sets are created with an audit log in redacted mode.",
                encoding="utf-8",
            )
            json_out = tmp / "report.json"
            md_out = tmp / "report.md"

            fixture_answer = {
                "answers": {
                    "canonical_terminology": {
                        "choice": "non_canonical_alias",
                        "confidence": 0.95,
                        "probabilities": {"canonical": 0.05, "non_canonical_alias": 0.95},
                    }
                }
            }

            with mock.patch.object(dl_check.runner, "run_fixture", return_value=fixture_answer):
                code = dl_check.main([
                    "--text-file", str(alias_file),
                    "--mode", "fixture",
                    "--json-out", str(json_out),
                    "--md-out", str(md_out),
                ])
                self.assertEqual(code, dl_check.EXIT_OK)

            report = json.loads(json_out.read_text(encoding="utf-8"))
            self.assertEqual(len(report["results"]), 1)
            result = report["results"][0]
            self.assertEqual(result["status"], "finding")
            self.assertEqual(result["choice"], "non_canonical_alias")
            self.assertIn("target size", result["detected_aliases"])
            self.assertIn("goal size", result["detected_aliases"])

    def test_report_json_is_stable_byte_for_byte(self):
        corpus_dir = Path(__file__).resolve().parents[3] / "tests" / "typesafe-audit-fixtures" / "domain-language"
        recorder_script = CHECK_DIR / "record_corpus_fixtures.py"
        with tempfile.TemporaryDirectory() as fixture_tmp, tempfile.TemporaryDirectory() as out_tmp:
            fixture_dir = Path(fixture_tmp)
            subprocess.run([sys.executable, str(recorder_script), str(fixture_dir)], check=True)

            json_1 = Path(out_tmp) / "report1.json"
            md_1 = Path(out_tmp) / "report1.md"
            json_2 = Path(out_tmp) / "report2.json"
            md_2 = Path(out_tmp) / "report2.md"
            dl_check.main(["--corpus", str(corpus_dir), "--mode", "fixture", "--fixture-dir", str(fixture_dir), "--json-out", str(json_1), "--md-out", str(md_1)])
            dl_check.main(["--corpus", str(corpus_dir), "--mode", "fixture", "--fixture-dir", str(fixture_dir), "--json-out", str(json_2), "--md-out", str(md_2)])

            self.assertEqual(json_1.read_bytes(), json_2.read_bytes())
            self.assertEqual(md_1.read_bytes(), md_2.read_bytes())

    def test_missing_or_malformed_choice_returns_input_error(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            text_file = tmp / "sample.txt"
            text_file.write_text("sample content", encoding="utf-8")
            json_out = tmp / "r.json"
            md_out = tmp / "r.md"

            with mock.patch.object(dl_check.runner, "run_fixture", return_value={"answers": {}}):
                code = dl_check.main([
                    "--text-file", str(text_file),
                    "--mode", "fixture",
                    "--json-out", str(json_out),
                    "--md-out", str(md_out),
                ])
                self.assertEqual(code, dl_check.EXIT_INPUT_ERROR)

            with mock.patch.object(dl_check.runner, "run_fixture", return_value={"answers": {"canonical_terminology": {"confidence": 0.9}}}):
                code = dl_check.main([
                    "--text-file", str(text_file),
                    "--mode", "fixture",
                    "--json-out", str(json_out),
                    "--md-out", str(md_out),
                ])
                self.assertEqual(code, dl_check.EXIT_INPUT_ERROR)


if __name__ == "__main__":
    unittest.main()
