"""Unit tests for the semantic architecture lint check.

Naming follows the repo convention: {Method}_{Scenario}_{Expected}.
Run: python3 -m unittest tools.typesafe-audit.tests... is not a package; use
    cd tools/typesafe-audit && python3 -m unittest discover -s tests
"""

import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

CHECK_DIR = Path(__file__).resolve().parents[1] / "checks" / "architecture"
TOOL_DIR = Path(__file__).resolve().parents[1]

sys.path.insert(0, str(TOOL_DIR))

spec = importlib.util.spec_from_file_location("tsa_arch_check", CHECK_DIR / "run_check.py")
arch_check = importlib.util.module_from_spec(spec)
sys.modules["tsa_arch_check"] = arch_check
spec.loader.exec_module(arch_check)


def _noul_answer(value, confidence=None):
    return {"noul": value, "confidence": confidence, "probabilities": {str(value): 1.0}}


class WatchedFilesTests(unittest.TestCase):
    def test_watched_files_withSeamPaths_ExpectedKeptSorted(self):
        changed = [
            "README.md",
            "src/LoadFiles/DatComposer.cs",
            "src/Program.cs",
            "docs/architecture.md",
            "src/LoadFiles/LoadFileEmitter.cs",
        ]
        self.assertEqual(
            arch_check.watched_files(changed),
            ["docs/architecture.md", "src/LoadFiles/DatComposer.cs", "src/LoadFiles/LoadFileEmitter.cs"],
        )

    def test_watched_files_withNoSeamPaths_ExpectedEmpty(self):
        self.assertEqual(arch_check.watched_files(["src/Program.cs", "tests/run-tests.sh"]), [])

    def test_carve_out_without_doc_update_withCarveOutOnly_ExpectedTrue(self):
        changed = ["src/LoadFiles/XmlLoadFileWriter.cs", "src/Program.cs"]
        self.assertTrue(arch_check.carve_out_without_doc_update(changed))

    def test_carve_out_without_doc_update_withDocUpdated_ExpectedFalse(self):
        changed = ["src/LoadFiles/XmlLoadFileWriter.cs", "docs/architecture.md"]
        self.assertFalse(arch_check.carve_out_without_doc_update(changed))

    def test_carve_out_without_doc_update_withOtherSeamFile_ExpectedFalse(self):
        self.assertFalse(arch_check.carve_out_without_doc_update(["src/LoadFiles/DatComposer.cs"]))


class BuildQuestionsTests(unittest.TestCase):
    def test_build_questions_withEvidenceKey_ExpectedNamespaced(self):
        questions = {"seam_bypass": {"type": "noul", "instructions": "i"}}
        namespaced = arch_check.build_questions(questions, "seam#abc")
        self.assertIn("seam#abc#seam_bypass", namespaced)


class MainNeutralSkipTests(unittest.TestCase):
    def test_main_withNoSeamFiles_ExpectedNeutralSkipWithoutModelCall(self):
        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.object(arch_check, "changed_files", return_value=[]), \
                    mock.patch.object(arch_check.runner, "run_live") as run_live, \
                    mock.patch.object(arch_check.runner, "run_fixture") as run_fixture:
                code = arch_check.main(["--base", "main", "--json-out", str(json_out), "--md-out", str(md_out)])
            self.assertEqual(code, arch_check.EXIT_OK)
            run_live.assert_not_called()
            run_fixture.assert_not_called()
            report = json.loads(json_out.read_text(encoding="utf-8"))
            self.assertEqual(report["mode"], "neutral")
            self.assertIn("no load-file seam files", md_out.read_text(encoding="utf-8").lower())


class MainLiveTests(unittest.TestCase):
    def test_main_withSeamDiffAndBypassAnswer_ExpectedFindingRow(self):
        watched = ["src/LoadFiles/DatComposer.cs", "src/LoadFiles/LoadFileEmitter.cs"]
        key = arch_check.evidence_key_for(watched)
        answers = {
            f"{key}#seam_bypass": _noul_answer(0.95),
            f"{key}#diagram_stale": _noul_answer(0.01),
        }

        def fake_run_live(request, config, api_key, secrets):
            self.assertEqual(api_key, "test-key")
            return {"answers": answers, "model": "jev-test"}

        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}, clear=True), \
                    mock.patch.object(arch_check, "changed_files", return_value=watched), \
                    mock.patch.object(arch_check, "file_diff", return_value="+++ b/src/LoadFiles/DatComposer.cs"), \
                    mock.patch.object(arch_check, "ARCHITECTURE_DOC") as doc, \
                    mock.patch.object(arch_check.runner, "run_live", side_effect=fake_run_live):
                doc.exists.return_value = True
                doc.read_text.return_value = "# Architecture"
                code = arch_check.main(["--base", "main", "--mode", "live", "--json-out", str(json_out), "--md-out", str(md_out)])
            self.assertEqual(code, arch_check.EXIT_OK)
            report = json.loads(json_out.read_text(encoding="utf-8"))
            by_q = {r["question"]: r for r in report["answers"]}
            self.assertEqual(by_q["seam_bypass"]["status"], "finding")
            self.assertEqual(by_q["diagram_stale"]["status"], "ok")
            self.assertFalse(report["carve_out_needs_review"])

    def test_main_withCarveOutOnlyAndNoDocUpdate_ExpectedDeterministicReviewWithoutModelCall(self):
        watched = ["src/LoadFiles/XmlLoadFileWriter.cs"]
        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}, clear=True), \
                    mock.patch.object(arch_check, "changed_files", return_value=watched), \
                    mock.patch.object(arch_check.runner, "run_live") as run_live, \
                    mock.patch.object(arch_check.runner, "run_fixture") as run_fixture:
                code = arch_check.main(["--base", "main", "--json-out", str(json_out), "--md-out", str(md_out)])
            self.assertEqual(code, arch_check.EXIT_OK)
            run_live.assert_not_called()
            run_fixture.assert_not_called()
            report = json.loads(json_out.read_text(encoding="utf-8"))
            self.assertEqual(report["mode"], "carve-out-review")
            self.assertTrue(report["carve_out_needs_review"])
            self.assertIn("carve_out_update", md_out.read_text(encoding="utf-8"))

    def test_main_withOversizedDiffEvidence_ExpectedInputErrorExitTwo(self):
        watched = ["src/LoadFiles/DatComposer.cs"]
        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}, clear=True), \
                    mock.patch.object(arch_check, "changed_files", return_value=watched), \
                    mock.patch.object(arch_check, "file_diff", return_value="x" * 70000), \
                    mock.patch.object(arch_check, "ARCHITECTURE_DOC") as doc:
                doc.exists.return_value = True
                doc.read_text.return_value = "# Architecture"
                with self.assertRaises(SystemExit) as ctx:
                    arch_check.main(["--base", "main", "--mode", "live", "--json-out", str(json_out), "--md-out", str(md_out)])
                self.assertEqual(ctx.exception.code, 2)

    def test_main_withMissingAnswer_ExpectedInputErrorReturnTwo(self):
        watched = ["src/LoadFiles/DatComposer.cs"]
        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}, clear=True), \
                    mock.patch.object(arch_check, "changed_files", return_value=watched), \
                    mock.patch.object(arch_check, "file_diff", return_value="diff"), \
                    mock.patch.object(arch_check, "ARCHITECTURE_DOC") as doc, \
                    mock.patch.object(arch_check.runner, "run_live", return_value={"answers": {}}):
                doc.exists.return_value = True
                doc.read_text.return_value = "# Architecture"
                code = arch_check.main(["--base", "main", "--mode", "live", "--json-out", str(json_out), "--md-out", str(md_out)])
            self.assertEqual(code, arch_check.EXIT_INPUT_ERROR)

    def test_main_withMissingArchitectureDoc_ExpectedInputErrorExitTwo(self):
        watched = ["src/LoadFiles/DatComposer.cs"]
        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.object(arch_check, "changed_files", return_value=watched), \
                    mock.patch.object(arch_check, "file_diff", return_value="diff"), \
                    mock.patch.object(arch_check, "ARCHITECTURE_DOC") as doc:
                doc.exists.return_value = False
                with self.assertRaises(SystemExit) as ctx:
                    arch_check.main(["--base", "main", "--json-out", str(json_out), "--md-out", str(md_out)])
                self.assertEqual(ctx.exception.code, 2)

    def test_main_withRemoteFailure_ExpectedExitThree(self):
        watched = ["src/LoadFiles/DatComposer.cs"]
        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}, clear=True), \
                    mock.patch.object(arch_check, "changed_files", return_value=watched), \
                    mock.patch.object(arch_check, "file_diff", return_value="diff"), \
                    mock.patch.object(arch_check, "ARCHITECTURE_DOC") as doc, \
                    mock.patch.object(arch_check.runner, "run_live", side_effect=SystemExit(3)):
                doc.exists.return_value = True
                doc.read_text.return_value = "# Architecture"
                with self.assertRaises(SystemExit) as ctx:
                    arch_check.main(["--base", "main", "--mode", "live", "--json-out", str(json_out), "--md-out", str(md_out)])
                self.assertEqual(ctx.exception.code, 3)

    def test_main_withLiveModeAndNoKey_ExpectedInputError(self):
        watched = ["src/LoadFiles/DatComposer.cs"]
        with tempfile.TemporaryDirectory() as tmp:
            json_out = Path(tmp) / "arch.json"
            md_out = Path(tmp) / "arch.md"
            with mock.patch.dict(os.environ, {}, clear=True), \
                    mock.patch.object(arch_check, "changed_files", return_value=watched), \
                    mock.patch.object(arch_check, "file_diff", return_value="diff"), \
                    mock.patch.object(arch_check, "ARCHITECTURE_DOC") as doc:
                doc.exists.return_value = True
                doc.read_text.return_value = "# Architecture"
                code = arch_check.main(["--base", "main", "--mode", "live", "--json-out", str(json_out), "--md-out", str(md_out)])
            self.assertEqual(code, arch_check.EXIT_INPUT_ERROR)


if __name__ == "__main__":
    unittest.main()
