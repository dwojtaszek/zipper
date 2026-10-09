"""Tests for the TypeSafe quality prioritization check (#959)."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOL_DIR = REPO_ROOT / "tools" / "typesafe-audit"
QUALITY_DIR = TOOL_DIR / "quality"
CORPUS = REPO_ROOT / "tests" / "typesafe-audit-fixtures" / "quality"


def load_module(name: str, path: Path):
    # Explicit-path loading avoids module-name collisions between checks.
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class CoverageGapTests(unittest.TestCase):
    """Deterministic Cobertura parsing, exclusions, and REQ mapping."""

    @classmethod
    def setUpClass(cls):
        cls.gaps = load_module("quality_coverage_gaps", QUALITY_DIR / "coverage_gaps.py")

    def test_uncovered_lines_mapped_to_methods(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        by_method = {g["method"]: g for g in gaps}
        self.assertIn("ValidateDestination", by_method)
        self.assertTrue(by_method["ValidateDestination"]["uncovered_lines"])
        self.assertIn("ValidateDestination", by_method["ValidateDestination"]["method"])

    def test_branch_coverage_is_parsed(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        by_method = {g["method"]: g for g in gaps}
        self.assertEqual(by_method["ValidateDestination"]["uncovered_branches"], 1)

    def test_generated_and_trivial_accessors_excluded(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        methods = [g["method"] for g in gaps]
        self.assertNotIn("get_Priority", methods)  # trivial accessor
        self.assertFalse(any("Designer" in m for m in methods))

    def test_req_ids_from_method_text_and_traceability_rows(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS)
        by_method = {g["method"]: g for g in gaps}
        # REQ-171 comes from both the method source text and the TSV rows; the
        # traceability rows also provide owning test names.
        self.assertIn("REQ-171", by_method["ValidateDestination"]["req_ids"])
        self.assertIn("ProductionSetPostValidatorTests.ValidateDestination_MissingDirectory_ShouldThrow", by_method["ValidateDestination"]["tests"])

    def test_source_only_req_evidence_without_tsv_fallback(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src" / "Validation"
            src_dir.mkdir(parents=True)
            cs_source = (
                "namespace Zipper.Validation;\n"
                "public class ProductionSetPostValidator\n"
                "{\n"
                "    // Source-only requirement evidence (REQ-171)\n"
                "    public void ValidateDestination(string dest)\n"
                "    {\n"
                "        if (dest == null) throw new System.ArgumentNullException();\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "ProductionSetPostValidator.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            expected_hash = hashlib.sha256(cs_file.read_bytes()).hexdigest()

            # No TSV file exists in repo_dir/tests/req-traceability.tsv.
            # Test both source-relative and repository-relative paths, including Windows backslashes.
            shapes = [
                "Validation/ProductionSetPostValidator.cs",
                "src/Validation/ProductionSetPostValidator.cs",
                "Validation\\ProductionSetPostValidator.cs",
                "src\\Validation\\ProductionSetPostValidator.cs",
            ]
            for shape in shapes:
                cobertura_content = f"""<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.5" branch-rate="0.5" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="Zipper.Validation.ProductionSetPostValidator" filename="{shape}">
          <methods>
            <method name="ValidateDestination" signature="()">
              <lines>
                <line number="6" hits="0" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>"""
                xml_path = repo_dir / "cobertura.xml"
                xml_path.write_text(cobertura_content, encoding="utf-8")
                gaps = self.gaps.extract_gaps(xml_path, repo_dir)
                self.assertEqual(len(gaps), 1, f"Failed for shape {shape}")
                gap = gaps[0]
                self.assertEqual(gap["file"], "src/Validation/ProductionSetPostValidator.cs", f"Failed file for shape {shape}")
                self.assertEqual(gap["method"], "ValidateDestination", f"Failed method for shape {shape}")
                self.assertEqual(gap["req_ids"], ["REQ-171"], f"Failed req_ids for shape {shape}")
                self.assertEqual(gap["tests"], [], f"Failed tests for shape {shape}")
                self.assertEqual(gap["source_sha256"], expected_hash, f"Failed sha256 for shape {shape}")
                self.assertNotEqual(gap["source_sha256"], "")

    def test_tsv_only_req_evidence_without_source_mention(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src" / "Validation"
            src_dir.mkdir(parents=True)
            cs_source = (
                "namespace Zipper.Validation;\n"
                "public class ProductionSetPostValidator\n"
                "{\n"
                "    public void ValidateDestination(string dest)\n"
                "    {\n"
                "        if (dest == null) throw new System.ArgumentNullException();\n"
                "    }\n"
                "}\n"
            )
            (src_dir / "ProductionSetPostValidator.cs").write_text(cs_source, encoding="utf-8")

            tests_dir = repo_dir / "tests"
            tests_dir.mkdir(parents=True)
            tsv_content = (
                "req_id\tcoverage\treference\tnotes\n"
                "REQ-333\tunit\tProductionSetPostValidatorTests.Validate_Throws\tdestination validation\n"
            )
            (tests_dir / "req-traceability.tsv").write_text(tsv_content, encoding="utf-8")

            cobertura_content = """<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.5" branch-rate="0.5" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="Zipper.Validation.ProductionSetPostValidator" filename="src/Validation/ProductionSetPostValidator.cs">
          <methods>
            <method name="ValidateDestination" signature="()">
              <lines>
                <line number="6" hits="0" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>"""
            xml_path = repo_dir / "cobertura.xml"
            xml_path.write_text(cobertura_content, encoding="utf-8")
            gaps = self.gaps.extract_gaps(xml_path, repo_dir)
            self.assertEqual(len(gaps), 1)
            gap = gaps[0]
            self.assertEqual(gap["req_ids"], ["REQ-333"])
            self.assertEqual(gap["tests"], ["ProductionSetPostValidatorTests.Validate_Throws"])

    def test_coverage_out_of_scope_paths_not_read(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td) / "repo"
            repo_dir.mkdir(parents=True)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)

            # File actually exists on disk outside the repository scope
            outside_file = Path(td) / "outside.cs"
            outside_file.write_text("// REQ-999\npublic class Outside { public void Foo() {} }", encoding="utf-8")
            self.assertTrue(outside_file.is_file())

            cobertura_content = """<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.5" branch-rate="0.5" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="Outside" filename="../../outside.cs">
          <methods>
            <method name="Foo" signature="()">
              <lines>
                <line number="1" hits="0" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>"""
            xml_path = repo_dir / "cobertura.xml"
            xml_path.write_text(cobertura_content, encoding="utf-8")
            gaps = self.gaps.extract_gaps(xml_path, repo_dir)
            self.assertEqual(len(gaps), 1)
            gap = gaps[0]
            self.assertEqual(gap["source_sha256"], "")
            self.assertEqual(gap["req_ids"], [])

    def test_adjacent_methods_do_not_bleed_req_evidence(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)
            cs_source = (
                "public class Service\n"
                "{\n"
                "    // REQ-100\n"
                "    public void MethodA() { var a = 1; }\n"
                "\n"
                "    // REQ-120\n"
                "    public void MethodEmpty()\n"
                "    { }\n"
                "\n"
                "    // REQ-150\n"
                "    public int MethodExpr()\n"
                "        => 42;\n"
                "\n"
                "    // REQ-200\n"
                "    public void MethodB()\n"
                "    {\n"
                "        var b = 2;\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "Service.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            cobertura_content = """<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.5" branch-rate="0.5" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="Service" filename="Service.cs">
          <methods>
            <method name="MethodB" signature="()">
              <lines>
                <line number="17" hits="0" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>"""
            xml_path = repo_dir / "cobertura.xml"
            xml_path.write_text(cobertura_content, encoding="utf-8")
            gaps = self.gaps.extract_gaps(xml_path, repo_dir)
            self.assertEqual(len(gaps), 1)
            gap = gaps[0]
            self.assertEqual(gap["method"], "MethodB")
            self.assertEqual(gap["req_ids"], ["REQ-200"])
            self.assertNotIn("REQ-100", gap["req_ids"])
            self.assertNotIn("REQ-120", gap["req_ids"])
            self.assertNotIn("REQ-150", gap["req_ids"])

    def test_normalize_file_path_and_resolve_source_file_direct(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)
            cs_file = src_dir / "Foo.cs"
            cs_file.write_text("// REQ-111\npublic class Foo {}", encoding="utf-8")
            json_file = src_dir / "appsettings.json"
            json_file.write_text("{}", encoding="utf-8")

            # normalize_file_path assertions across relative, Windows, and absolute styles
            self.assertEqual(
                self.gaps.normalize_file_path("Foo.cs", repo_dir),
                ("src/Foo.cs", "Foo.cs"),
            )
            self.assertEqual(
                self.gaps.normalize_file_path("src/Foo.cs", repo_dir),
                ("src/Foo.cs", "Foo.cs"),
            )
            self.assertEqual(
                self.gaps.normalize_file_path("src\\Foo.cs", repo_dir),
                ("src/Foo.cs", "Foo.cs"),
            )
            self.assertEqual(
                self.gaps.normalize_file_path("./src/Foo.cs", repo_dir),
                ("src/Foo.cs", "Foo.cs"),
            )
            self.assertEqual(
                self.gaps.normalize_file_path("../src/Foo.cs", repo_dir),
                ("src/Foo.cs", "Foo.cs"),
            )
            self.assertEqual(
                self.gaps.normalize_file_path("C:/repo/src/Foo.cs", repo_dir),
                ("src/Foo.cs", "Foo.cs"),
            )
            self.assertEqual(
                self.gaps.normalize_file_path("C:/src/repo/src/Foo.cs", repo_dir),
                ("src/Foo.cs", "Foo.cs"),
            )
            self.assertEqual(
                self.gaps.normalize_file_path("../../outside.cs", repo_dir),
                ("../../outside.cs", "../../outside.cs"),
            )

            # resolve_source_file assertions
            self.assertEqual(self.gaps.resolve_source_file(src_dir, "Foo.cs"), cs_file.resolve())
            self.assertEqual(self.gaps.resolve_source_file(src_dir, "src/Foo.cs"), cs_file.resolve())
            self.assertEqual(self.gaps.resolve_source_file(src_dir, "src\\Foo.cs"), cs_file.resolve())
            self.assertIsNone(self.gaps.resolve_source_file(src_dir, "appsettings.json"))
            self.assertIsNone(self.gaps.resolve_source_file(src_dir, "../../outside.cs"))
            self.assertIsNone(self.gaps.resolve_source_file(src_dir, "../secret.txt"))

            # hash_source assertions
            self.assertEqual(self.gaps.hash_source(None), "")
            self.assertEqual(self.gaps.hash_source(Path(td) / "nonexistent.cs"), "")
            self.assertEqual(self.gaps.hash_source(cs_file), hashlib.sha256(cs_file.read_bytes()).hexdigest())

    def test_method_spans_with_string_and_comment_braces(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)
            cs_source = (
                "public class Service\n"
                "{\n"
                "    // REQ-300\n"
                "    public void MethodWithStringBraces()\n"
                "    {\n"
                "        string s = \"{ test }\"; // comment with }\n"
                "        string verbatim = @\" { multiline\n"
                "        } \";\n"
                "    }\n"
                "\n"
                "    // REQ-301\n"
                "    public void NextMethod()\n"
                "    {\n"
                "        int x = 1;\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "Service.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            self.assertEqual(self.gaps.method_for_line(src_dir, "Service.cs", 6), "MethodWithStringBraces")
            self.assertEqual(self.gaps.method_for_line(src_dir, "Service.cs", 13), "NextMethod")
            reqs = self.gaps.extract_method_reqs(cs_file, 6)
            self.assertEqual(reqs, {"REQ-300"})
            next_reqs = self.gaps.extract_method_reqs(cs_file, 13)
            self.assertEqual(next_reqs, {"REQ-301"})

    def test_method_spans_with_multiline_comments_and_char_braces(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)
            cs_source = (
                "public class Service\n"
                "{\n"
                "    /*\n"
                "       multiline comment with braces {\n"
                "       }\n"
                "    */\n"
                "    char open = '{';\n"
                "    char close = '}';\n"
                "\n"
                "    // REQ-350\n"
                "    public void Compute()\n"
                "    {\n"
                "        char c = '}';\n"
                "        int y = 2;\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "Service.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            self.assertEqual(self.gaps.method_for_line(src_dir, "Service.cs", 13), "Compute")
            reqs = self.gaps.extract_method_reqs(cs_file, 13)
            self.assertEqual(reqs, {"REQ-350"})

    def test_record_and_type_declarations_not_treated_as_methods(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)
            cs_source = (
                "namespace Zipper.Domain;\n"
                "public record Order(int Id)\n"
                "{\n"
                "    // REQ-400\n"
                "    public void Process()\n"
                "    {\n"
                "        int a = 1;\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "Order.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            self.assertEqual(self.gaps.method_for_line(src_dir, "Order.cs", 2), "")
            self.assertEqual(self.gaps.method_for_line(src_dir, "Order.cs", 7), "Process")
            reqs = self.gaps.extract_method_reqs(cs_file, 7)
            self.assertEqual(reqs, {"REQ-400"})

    def test_root_src_file_coverage_paths_and_hashes(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)
            cs_source = (
                "public class A\n"
                "{\n"
                "    // REQ-171 root source\n"
                "    public void Execute()\n"
                "    {\n"
                "        int x = 42;\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "A.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            expected_hash = hashlib.sha256(cs_file.read_bytes()).hexdigest()

            shapes = ["A.cs", "src/A.cs", "src\\A.cs", ".\\A.cs", "./src/A.cs"]
            for shape in shapes:
                cobertura_content = f"""<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.5" branch-rate="0.5" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="A" filename="{shape}">
          <methods>
            <method name="Execute" signature="()">
              <lines>
                <line number="6" hits="0" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>"""
                xml_path = repo_dir / "cobertura.xml"
                xml_path.write_text(cobertura_content, encoding="utf-8")
                gaps = self.gaps.extract_gaps(xml_path, repo_dir)
                self.assertEqual(len(gaps), 1, f"Failed for shape {shape}")
                gap = gaps[0]
                self.assertEqual(gap["file"], "src/A.cs", f"Failed file for shape {shape}")
                self.assertEqual(gap["method"], "Execute", f"Failed method for shape {shape}")
                self.assertEqual(gap["req_ids"], ["REQ-171"], f"Failed req_ids for shape {shape}")
                self.assertEqual(gap["source_sha256"], expected_hash, f"Failed sha256 for shape {shape}")

    def test_gap_budget_enforced(self):
        gaps = self.gaps.extract_gaps(CORPUS / "cobertura.xml", CORPUS, max_gaps=1)
        self.assertEqual(len(gaps), 1)

    def test_branch_coverage_fixtures_and_line_ranges(self):
        cobertura_content = """<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE coverage SYSTEM "https://coveralls.io/xml/coverage.dtd">
<coverage line-rate="0.75" branch-rate="0.6" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="Zipper.Validation.ProductionSetPostValidator" filename="Validation/ProductionSetPostValidator.cs">
          <methods>
            <method name="PositiveHitHalfBranch" signature="()">
              <lines>
                <line number="10" hits="5" branch="False" />
                <line number="12" hits="1" branch="True" condition-coverage="50% (1/2)" />
                <line number="14" hits="5" branch="False" />
              </lines>
            </method>
            <method name="PositiveHitThreeQuartersBranch" signature="()">
              <lines>
                <line number="20" hits="2" branch="False" />
                <line number="22" hits="3" branch="True" condition-coverage="75% (3/4)" />
                <line number="24" hits="2" branch="False" />
              </lines>
            </method>
            <method name="ZeroHitZeroTwoBranch" signature="()">
              <lines>
                <line number="30" hits="1" branch="False" />
                <line number="32" hits="0" branch="True" condition-coverage="0% (0/2)" />
                <line number="34" hits="1" branch="False" />
              </lines>
            </method>
            <method name="FullyCoveredMethod" signature="()">
              <lines>
                <line number="40" hits="3" branch="False" />
                <line number="42" hits="3" branch="True" condition-coverage="100% (2/2)" />
                <line number="44" hits="3" branch="False" />
              </lines>
            </method>
            <method name="BranchFreeFullyCovered" signature="()">
              <lines>
                <line number="50" hits="4" branch="False" />
                <line number="52" hits="4" branch="False" />
              </lines>
            </method>
            <method name="BranchFreeWithUncoveredLine" signature="()">
              <lines>
                <line number="60" hits="2" branch="False" />
                <line number="62" hits="0" branch="False" />
                <line number="64" hits="2" branch="False" />
              </lines>
            </method>
            <method name="MultiLineGapMethod" signature="()">
              <lines>
                <line number="70" hits="1" branch="True" condition-coverage="50% (1/2)" />
                <line number="72" hits="0" branch="False" />
                <line number="74" hits="0" branch="True" condition-coverage="0% (0/2)" />
                <line number="76" hits="2" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"""
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src" / "Validation"
            src_dir.mkdir(parents=True)
            cs_source = (
                "namespace Zipper.Validation;\n"
                "public class ProductionSetPostValidator\n"
                "{\n"
                + "\n" * 5
                + "    public void PositiveHitHalfBranch()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        if (x > 0) {}\n"
                + "        x++;\n"
                + "    }\n"
                + "\n" * 4
                + "    public void PositiveHitThreeQuartersBranch()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        if (x > 0) {}\n"
                + "        x++;\n"
                + "    }\n"
                + "\n" * 4
                + "    public void ZeroHitZeroTwoBranch()\n"
                + "    {\n"
                + "        int x = 0;\n"
                + "        if (x > 0) {}\n"
                + "        x++;\n"
                + "    }\n"
                + "\n" * 4
                + "    public void FullyCoveredMethod()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        if (x > 0) {}\n"
                + "        x++;\n"
                + "    }\n"
                + "\n" * 4
                + "    public void BranchFreeFullyCovered()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        x++;\n"
                + "    }\n"
                + "\n" * 5
                + "    public void BranchFreeWithUncoveredLine()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        x++;\n"
                + "        x++;\n"
                + "        x++;\n"
                + "    }\n"
                + "\n" * 3
                + "    public void MultiLineGapMethod()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        if (x > 0) {}\n"
                + "        x++;\n"
                + "        if (x > 1) {}\n"
                + "        x++;\n"
                + "        x++;\n"
                + "    }\n"
                + "}\n"
            )
            (src_dir / "ProductionSetPostValidator.cs").write_text(cs_source, encoding="utf-8")
            xml_path = repo_dir / "cobertura.xml"
            xml_path.write_text(cobertura_content, encoding="utf-8")
            gaps = self.gaps.extract_gaps(xml_path, repo_dir)
            by_method = {g["method"]: g for g in gaps}

            # Positive-hit 1/2 yields one uncovered branch and 0 uncovered lines
            self.assertIn("PositiveHitHalfBranch", by_method)
            half = by_method["PositiveHitHalfBranch"]
            self.assertEqual(half["uncovered_branches"], 1)
            self.assertEqual(half["uncovered_lines"], 0)
            self.assertEqual((half["first_line"], half["last_line"]), (12, 12))

            # Positive-hit 3/4 yields one uncovered branch and 0 uncovered lines
            self.assertIn("PositiveHitThreeQuartersBranch", by_method)
            three_quarters = by_method["PositiveHitThreeQuartersBranch"]
            self.assertEqual(three_quarters["uncovered_branches"], 1)
            self.assertEqual(three_quarters["uncovered_lines"], 0)
            self.assertEqual((three_quarters["first_line"], three_quarters["last_line"]), (22, 22))

            # 0/2 yields two uncovered branches and 1 uncovered line
            self.assertIn("ZeroHitZeroTwoBranch", by_method)
            zero_two = by_method["ZeroHitZeroTwoBranch"]
            self.assertEqual(zero_two["uncovered_branches"], 2)
            self.assertEqual(zero_two["uncovered_lines"], 1)
            self.assertEqual((zero_two["first_line"], zero_two["last_line"]), (32, 32))

            # Fully covered method remains excluded
            self.assertNotIn("FullyCoveredMethod", by_method)

            # Branch-free fully covered method remains excluded
            self.assertNotIn("BranchFreeFullyCovered", by_method)

            # Branch-free with uncovered line is retained
            self.assertIn("BranchFreeWithUncoveredLine", by_method)
            branch_free = by_method["BranchFreeWithUncoveredLine"]
            self.assertEqual(branch_free["uncovered_branches"], 0)
            self.assertEqual(branch_free["uncovered_lines"], 1)
            self.assertEqual((branch_free["first_line"], branch_free["last_line"]), (62, 62))

            # Multi-line gap method spans from first gap line to last gap line
            self.assertIn("MultiLineGapMethod", by_method)
            multi = by_method["MultiLineGapMethod"]
            self.assertEqual(multi["uncovered_branches"], 3)
            self.assertEqual(multi["uncovered_lines"], 2)
            self.assertEqual((multi["first_line"], multi["last_line"]), (70, 74))

    def test_uncovered_branches_helper_robustness(self):
        helper = self.gaps._uncovered_branches
        self.assertEqual(helper({"condition": None}), 0)
        self.assertEqual(helper({"condition": ""}), 0)
        self.assertEqual(helper({}), 0)
        self.assertEqual(helper({"condition": "50% ( 1 / 2 )"}), 1)
        self.assertEqual(helper({"condition": "75% (  3 / 4  )"}), 1)
        self.assertEqual(helper({"condition": "100% ( 2 / 2 )"}), 0)
        self.assertEqual(helper({"condition": "invalid (not a branch)"}), 0)
        self.assertEqual(helper({"condition": "0% ( 0 / 2 )"}), 2)

    def test_branch_edge_cases_and_line_zero(self):
        cobertura_content = """<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.8" branch-rate="0.5" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="Zipper.Validation.ProductionSetPostValidator" filename="Validation/ProductionSetPostValidator.cs">
          <methods>
            <method name="WhitespaceAndLowercaseBranch" signature="()">
              <lines>
                <line number="10" hits="2" branch="true" condition-coverage="50% ( 1 / 2 )" />
              </lines>
            </method>
            <method name="MultiLineBranchOnlyWithIntermediateCovered" signature="()">
              <lines>
                <line number="20" hits="1" branch="true" condition-coverage="50% (1/2)" />
                <line number="22" hits="5" branch="False" />
                <line number="24" hits="2" branch="True" condition-coverage="75% (3/4)" />
              </lines>
            </method>
            <method name="CompilerGeneratedLineZero" signature="()">
              <lines>
                <line number="0" hits="0" branch="False" />
                <line number="30" hits="0" branch="False" />
                <line number="32" hits="0" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"""
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src" / "Validation"
            src_dir.mkdir(parents=True)
            cs_source = (
                "namespace Zipper.Validation;\n"
                "public class ProductionSetPostValidator\n"
                "{\n"
                + "\n" * 6
                + "    public void WhitespaceAndLowercaseBranch()\n"
                + "    {\n"
                + "    }\n"
                + "\n" * 7
                + "    public void MultiLineBranchOnlyWithIntermediateCovered()\n"
                + "    {\n"
                + "    }\n"
                + "\n" * 7
                + "    public void CompilerGeneratedLineZero()\n"
                + "    {\n"
                + "    }\n"
                + "}\n"
            )
            (src_dir / "ProductionSetPostValidator.cs").write_text(cs_source, encoding="utf-8")
            xml_path = repo_dir / "cobertura.xml"
            xml_path.write_text(cobertura_content, encoding="utf-8")
            gaps = self.gaps.extract_gaps(xml_path, repo_dir)
            by_method = {g["method"]: g for g in gaps}

            # Lowercase branch="true" with padded whitespace in condition
            self.assertIn("WhitespaceAndLowercaseBranch", by_method)
            ws = by_method["WhitespaceAndLowercaseBranch"]
            self.assertEqual(ws["uncovered_branches"], 1)
            self.assertEqual(ws["uncovered_lines"], 0)
            self.assertEqual((ws["first_line"], ws["last_line"]), (10, 10))

            # Multi-line branch-only gap with intermediate fully covered line
            self.assertIn("MultiLineBranchOnlyWithIntermediateCovered", by_method)
            multi_branch = by_method["MultiLineBranchOnlyWithIntermediateCovered"]
            self.assertEqual(multi_branch["uncovered_branches"], 2)
            self.assertEqual(multi_branch["uncovered_lines"], 0)
            self.assertEqual((multi_branch["first_line"], multi_branch["last_line"]), (20, 24))

            # Line 0 is excluded from first_line when real lines exist
            self.assertIn("CompilerGeneratedLineZero", by_method)
            zero_line = by_method["CompilerGeneratedLineZero"]
            self.assertEqual(zero_line["first_line"], 30)
            self.assertEqual(zero_line["last_line"], 32)



class MutationTests(unittest.TestCase):
    """Deterministic Stryker report parsing and category separation."""

    @classmethod
    def setUpClass(cls):
        cls.mutation = load_module("quality_mutation", QUALITY_DIR / "mutation.py")

    def test_categories_are_separate(self):
        categories = self.mutation.parse_report(CORPUS / "stryker.json", CORPUS)
        self.assertEqual(
            sorted(categories),
            ["compile_error", "no_coverage", "survived", "timeout"],
        )

    def test_survivors_carry_location_and_mutator(self):
        categories = self.mutation.parse_report(CORPUS / "stryker.json", CORPUS)
        survivor = categories["survived"][0]
        self.assertEqual(survivor["mutator"], "Arithmetic")
        self.assertIn("Path.Combine", survivor["description"])
        self.assertEqual(survivor["method"], "ValidateDestination")
        self.assertIn("REQ-171", survivor["req_ids"])

    def test_parse_report_handles_absolute_paths_from_real_stryker(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            real_stryker_json = tmp / "mutation-report.json"
            abs_file = (CORPUS / "src" / "Validation" / "ProductionSetPostValidator.cs").resolve()
            report_data = {
                "schemaVersion": "4",
                "files": {
                    str(abs_file): {
                        "language": "csharp",
                        "mutants": [
                            {
                                "id": 1,
                                "mutatorName": "Arithmetic",
                                "description": "Path.Combine(baseDir, name) changed to Path.Combine(baseDir, name + 1)",
                                "location": {"start": {"line": 12, "column": 20}, "end": {"line": 12, "column": 44}},
                                "status": "Survived"
                            }
                        ]
                    }
                }
            }
            real_stryker_json.write_text(json.dumps(report_data), encoding="utf-8")
            categories = self.mutation.parse_report(real_stryker_json, CORPUS)
            self.assertIn("survived", categories)
            survivor = categories["survived"][0]
            self.assertEqual(survivor["file"], "src/Validation/ProductionSetPostValidator.cs")
            self.assertFalse(survivor["file"].startswith("src//"))
            self.assertEqual(survivor["method"], "ValidateDestination")

    def test_mutation_paths_normalized_across_shapes_and_separators(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src" / "Validation"
            src_dir.mkdir(parents=True)
            cs_source = (
                "namespace Zipper.Validation;\n"
                "public class ProductionSetPostValidator\n"
                "{\n"
                "    // Source-only requirement (REQ-171)\n"
                "    public void ValidateDestination(string dest)\n"
                "    {\n"
                "        var p = dest + 1;\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "ProductionSetPostValidator.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            expected_hash = hashlib.sha256(cs_file.read_bytes()).hexdigest()

            # No TSV file in tests/ - source-only REQ evidence must be retained
            shapes = [
                "Validation/ProductionSetPostValidator.cs",
                "src/Validation/ProductionSetPostValidator.cs",
                "Validation\\ProductionSetPostValidator.cs",
                "src\\Validation\\ProductionSetPostValidator.cs",
            ]
            for shape in shapes:
                stryker_json = repo_dir / "stryker.json"
                report_data = {
                    "schemaVersion": "4",
                    "files": {
                        shape: {
                            "language": "csharp",
                            "mutants": [
                                {
                                    "id": 1,
                                    "mutatorName": "Arithmetic",
                                    "description": "dest + 1 changed",
                                    "location": {"start": {"line": 7, "column": 10}, "end": {"line": 7, "column": 20}},
                                    "status": "Survived"
                                }
                            ]
                        }
                    }
                }
                stryker_json.write_text(json.dumps(report_data), encoding="utf-8")
                categories = self.mutation.parse_report(stryker_json, repo_dir)
                self.assertIn("survived", categories, f"Failed for shape {shape}")
                survivor = categories["survived"][0]
                self.assertEqual(survivor["file"], "src/Validation/ProductionSetPostValidator.cs", f"Failed file for shape {shape}")
                self.assertEqual(survivor["method"], "ValidateDestination", f"Failed method for shape {shape}")
                self.assertEqual(survivor["source_sha256"], expected_hash, f"Failed sha256 for shape {shape}")
                self.assertNotEqual(survivor["source_sha256"], "")
                self.assertEqual(survivor["req_ids"], ["REQ-171"], f"Failed req_ids for shape {shape}")

    def test_mutation_out_of_scope_paths_not_read(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td) / "repo"
            repo_dir.mkdir(parents=True)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)

            outside_file = Path(td) / "outside.cs"
            outside_file.write_text("// REQ-999\npublic class Outside { public void Foo() {} }", encoding="utf-8")
            self.assertTrue(outside_file.is_file())

            stryker_json = repo_dir / "stryker.json"
            report_data = {
                "schemaVersion": "4",
                "files": {
                    "../../outside.cs": {
                        "language": "csharp",
                        "mutants": [
                            {
                                "id": 1,
                                "mutatorName": "Arithmetic",
                                "description": "outside mutant",
                                "location": {"start": {"line": 1, "column": 1}, "end": {"line": 1, "column": 10}},
                                "status": "Survived"
                            }
                        ]
                    }
                }
            }
            stryker_json.write_text(json.dumps(report_data), encoding="utf-8")
            categories = self.mutation.parse_report(stryker_json, repo_dir)
            self.assertIn("survived", categories)
            survivor = categories["survived"][0]
            self.assertEqual(survivor["source_sha256"], "")
            self.assertEqual(survivor["req_ids"], [])

    def test_root_src_file_mutation_paths_and_hashes(self):
        with tempfile.TemporaryDirectory() as td:
            repo_dir = Path(td)
            src_dir = repo_dir / "src"
            src_dir.mkdir(parents=True)
            cs_source = (
                "public class A\n"
                "{\n"
                "    // REQ-171 root source\n"
                "    public void Execute()\n"
                "    {\n"
                "        int x = 42;\n"
                "    }\n"
                "}\n"
            )
            cs_file = src_dir / "A.cs"
            cs_file.write_text(cs_source, encoding="utf-8")
            expected_hash = hashlib.sha256(cs_file.read_bytes()).hexdigest()

            shapes = ["A.cs", "src/A.cs", "src\\A.cs", ".\\A.cs", "./src/A.cs"]
            for shape in shapes:
                stryker_json = repo_dir / "stryker.json"
                report_data = {
                    "schemaVersion": "4",
                    "files": {
                        shape: {
                            "language": "csharp",
                            "mutants": [
                                {
                                    "id": 1,
                                    "mutatorName": "Arithmetic",
                                    "description": "x = 42 changed",
                                    "location": {"start": {"line": 6, "column": 9}, "end": {"line": 6, "column": 19}},
                                    "status": "Survived"
                                }
                            ]
                        }
                    }
                }
                stryker_json.write_text(json.dumps(report_data), encoding="utf-8")
                categories = self.mutation.parse_report(stryker_json, repo_dir)
                self.assertIn("survived", categories, f"Failed for shape {shape}")
                survivor = categories["survived"][0]
                self.assertEqual(survivor["file"], "src/A.cs", f"Failed file for shape {shape}")
                self.assertEqual(survivor["method"], "Execute", f"Failed method for shape {shape}")
                self.assertEqual(survivor["source_sha256"], expected_hash, f"Failed sha256 for shape {shape}")
                self.assertEqual(survivor["req_ids"], ["REQ-171"], f"Failed req_ids for shape {shape}")



class RunCheckTests(unittest.TestCase):
    """Fixture-mode ranking run, composition, byte stability."""

    @classmethod
    def setUpClass(cls):
        cls.run_check = load_module("quality_run_check", QUALITY_DIR / "run_check.py")

    def _record(self, fixture_dir: Path):
        subprocess.run(
            [sys.executable, str(QUALITY_DIR / "record_corpus_fixtures.py"), str(fixture_dir)],
            check=True,
        )

    def test_ranking_reproduces_expected_order(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            self.run_check.main([
                "--coverage", str(CORPUS / "cobertura.xml"),
                "--mutation", str(CORPUS / "stryker.json"),
                "--repo-root", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            report = json.loads((tmp / "r.json").read_text())
            candidates = report["ranked"]
            self.assertGreater(len(candidates), 0)
            scores = [c["priority"] for c in candidates]
            self.assertEqual(scores, sorted(scores, reverse=True))
            # High-risk output-validation candidates rank above trivial accessors.
            self.assertNotIn("get_Priority", [c["method"] for c in candidates])
            # Components and weights are never collapsed away.
            top = candidates[0]
            self.assertIn("components", top)
            self.assertIn("weights", top)
            self.assertIn("source_sha256", top)

    def test_report_is_byte_stable(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            args = [
                "--coverage", str(CORPUS / "cobertura.xml"),
                "--mutation", str(CORPUS / "stryker.json"),
                "--repo-root", str(CORPUS),
                "--mode", "fixture", "--fixture-dir", str(fixtures),
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ]
            self.run_check.main(args)
            first = (tmp / "r.json").read_bytes()
            (tmp / "r.json").unlink()
            self.run_check.main(args)
            self.assertEqual(first, (tmp / "r.json").read_bytes())

    def test_missing_inputs_is_input_error(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            code = self.run_check.main([
                "--repo-root", str(CORPUS),
                "--mode", "fixture",
                "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
            ])
            self.assertEqual(code, self.run_check.EXIT_INPUT_ERROR)

    def test_live_mode_reaches_runner_with_wellformed_request(self):
        # Regression (review B1): live mode must actually invoke run_live with
        # a well-formed request, not crash on a missing config local.
        import os
        from unittest import mock

        captured = {}

        def fake_run_live(request, config, api_key, secrets):
            captured["request"] = request
            captured["api_key"] = api_key
            return {"answers": {key: {"score": 0.5, "confidence": 0.9} for key in request["questions"]}}

        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}):
                with mock.patch.object(self.run_check.runner, "run_live", side_effect=fake_run_live):
                    code = self.run_check.main([
                        "--coverage", str(CORPUS / "cobertura.xml"),
                        "--repo-root", str(CORPUS),
                        "--mode", "live",
                        "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
                    ])
            self.assertEqual(code, self.run_check.EXIT_OK)
            request = captured["request"]
            self.assertIn("model", request)
            self.assertIn("questions", request)
            self.assertTrue(all("#" in key for key in request["questions"]))
            self.assertEqual(captured["api_key"], "test-key")

    def test_ranking_sorted_across_batch_boundaries(self):
        # Regression (review M1): with more candidates than batch_size, the
        # final ranking must still be globally priority-ordered.
        from unittest import mock

        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fixtures = Path(tempfile.mkdtemp())
            self._record(fixtures)
            synthetic = [
                {"origin": "coverage", "file": f"src/Synthetic{i}.cs", "method": f"M{i}", "first_line": 1,
                 "last_line": 2, "uncovered_lines": 1, "uncovered_branches": 0, "req_ids": [], "tests": [],
                 "source_sha256": f"{i:064x}"}
                for i in range(25)
            ]
            with mock.patch.object(self.run_check.coverage_gaps, "extract_gaps", return_value=synthetic):
                with mock.patch.object(
                    self.run_check.runner, "run_fixture",
                    side_effect=lambda request, fixture_dir: {
                        "answers": {
                            key: {"score": int(key.split("#")[-2]) / 30.0, "confidence": 0.9}
                            for key in request["questions"]
                        }
                    },
                ):
                    self.run_check.main([
                        "--coverage", str(CORPUS / "cobertura.xml"),
                        "--repo-root", str(CORPUS),
                        "--mode", "fixture", "--fixture-dir", str(fixtures),
                        "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
                    ])
            report = json.loads((tmp / "r.json").read_text())
            priorities = [c["priority"] for c in report["ranked"]]
            self.assertEqual(priorities, sorted(priorities, reverse=True))
            self.assertEqual(len(priorities), 25)

    def test_changed_files_filter_restricts_candidates(self):
        from unittest import mock
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            fake_scored = [{"origin": "coverage", "category": "coverage_gap", "file": "src/Validation/ProductionSetPostValidator.cs", "method": "Validate", "lines": "1-10", "req_ids": [], "priority": 0.8, "scores": {}}]
            with mock.patch.object(self.run_check, "score_batch", return_value=fake_scored):
                args = [
                    "--coverage", str(CORPUS / "cobertura.xml"),
                    "--repo-root", str(CORPUS),
                    "--mode", "fixture",
                    "--changed-files", "src/Validation/ProductionSetPostValidator.cs",
                    "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
                ]
                code = self.run_check.main(args)
                self.assertEqual(code, self.run_check.EXIT_OK)
                report = json.loads((tmp / "r.json").read_text(encoding="utf-8"))
                self.assertEqual(len(report["ranked"]), 1)

            args[7] = "src/Unrelated/OtherFile.cs"
            code = self.run_check.main(args)
            self.assertEqual(code, self.run_check.EXIT_OK)
            report2 = json.loads((tmp / "r.json").read_text(encoding="utf-8"))
            self.assertEqual(len(report2["ranked"]), 0)

    def test_path_matches_respects_boundary(self):
        pm = self.run_check._path_matches
        self.assertTrue(pm("src/Foo.cs", {"src/Foo.cs"}))
        self.assertTrue(pm("src/Foo.cs", {"Foo.cs"}))
        self.assertTrue(pm("/repo/src/Foo.cs", {"src/Foo.cs"}))
        self.assertFalse(pm("src/BarFoo.cs", {"Foo.cs"}))
        self.assertFalse(pm("src/Foo.cs", {"BarFoo.cs"}))

    def test_branch_only_candidates_included_in_ranking_input(self):
        from unittest import mock
        cobertura_content = """<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE coverage SYSTEM "https://coveralls.io/xml/coverage.dtd">
<coverage line-rate="0.75" branch-rate="0.6" version="1.9">
  <packages>
    <package name="Zipper">
      <classes>
        <class name="Zipper.Validation.ProductionSetPostValidator" filename="Validation/ProductionSetPostValidator.cs">
          <methods>
            <method name="ValidateDestination" signature="()">
              <lines>
                <line number="10" hits="5" branch="False" />
                <line number="12" hits="1" branch="True" condition-coverage="50% (1/2)" />
                <line number="14" hits="5" branch="False" />
              </lines>
            </method>
            <method name="FullyCoveredMethod" signature="()">
              <lines>
                <line number="40" hits="3" branch="False" />
                <line number="42" hits="3" branch="True" condition-coverage="100% (2/2)" />
                <line number="44" hits="3" branch="False" />
              </lines>
            </method>
          </methods>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"""
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            xml_path = tmp / "cobertura.xml"
            xml_path.write_text(cobertura_content, encoding="utf-8")
            with mock.patch.object(
                self.run_check.runner, "run_fixture",
                side_effect=lambda request, fixture_dir: {
                    "answers": {
                        key: {"score": 0.8, "confidence": 0.9}
                        for key in request["questions"]
                    }
                },
            ):
                args = [
                    "--coverage", str(xml_path),
                    "--repo-root", str(CORPUS),
                    "--mode", "fixture",
                    "--json-out", str(tmp / "r.json"), "--md-out", str(tmp / "r.md"),
                ]
                code = self.run_check.main(args)
                self.assertEqual(code, self.run_check.EXIT_OK)
                report = json.loads((tmp / "r.json").read_text(encoding="utf-8"))
                self.assertEqual(report["coverage_candidates"], 1)
                ranked_methods = [c["method"] for c in report["ranked"]]
                self.assertIn("ValidateDestination", ranked_methods)
                self.assertNotIn("FullyCoveredMethod", ranked_methods)
                candidate = report["ranked"][0]
                self.assertEqual(candidate["lines"], "12-12")
                self.assertEqual(candidate["origin"], "coverage")



class WorkflowWiringTests(unittest.TestCase):
    """Scheduled/manual quality workflow with least privilege and budgets."""

    @classmethod
    def setUpClass(cls):
        cls.wf = (REPO_ROOT / ".github" / "workflows" / "typesafe-quality-audit.yml").read_text()

    def test_weekly_schedule_and_dispatch(self):
        self.assertIn("cron:", self.wf)
        self.assertIn("workflow_dispatch:", self.wf)
        self.assertIn("scope:", self.wf)
        self.assertIn("shard:", self.wf)

    def test_read_only_permissions(self):
        self.assertIn("contents: read", self.wf)
        self.assertNotIn("contents: write", self.wf)

    def test_coverage_run_uses_cobertura(self):
        self.assertIn("XPlat Code Coverage", self.wf)

    def test_mutation_tool_is_pinned_in_manifest(self):
        manifest = json.loads((REPO_ROOT / ".config" / "dotnet-tools.json").read_text())
        self.assertIn("dotnet-stryker", manifest["tools"])
        self.assertTrue(manifest["tools"]["dotnet-stryker"]["version"])

    def test_workflow_uses_pinned_concurrency_option(self):
        self.assertIn("--concurrency 4", self.wf)
        self.assertNotIn("--max-concurrent-test-runs", self.wf)

    def test_workflow_scope_relative_to_project_root(self):
        self.assertIn('default: "LoadFiles/**', self.wf)
        self.assertNotIn('default: "src/LoadFiles/**', self.wf)

    def test_workflow_wires_shard_timeout(self):
        self.assertIn("timeout-minutes: ${{ fromJSON(github.event.inputs.shard || '45') }}", self.wf)

    def test_workflow_upload_runs_on_failure_or_timeout(self):
        self.assertIn("always() && steps.secret.outputs.available == 'true'", self.wf)

    def test_workflow_does_not_silently_drop_mutation_input(self):
        self.assertNotIn("MUTATION_ARG=()", self.wf)
        self.assertIn("--mutation results/mutation/mutation-report.json", self.wf)

    def test_workflow_locates_and_normalizes_mutation_report(self):
        self.assertIn("normalize_report.py", self.wf)
        self.assertIn('--output "../results/mutation"', self.wf)


class MutationNormalizeTests(unittest.TestCase):
    """Report location, mutant counting, normalization, and validation."""

    @classmethod
    def setUpClass(cls):
        cls.mutation = load_module("quality_mutation", QUALITY_DIR / "mutation.py")

    def test_locate_report_finds_nested_file(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            nested = tmp / "reports"
            nested.mkdir()
            report_file = nested / "mutation-report.json"
            report_file.write_text("{}", encoding="utf-8")
            located = self.mutation.locate_report(tmp)
            self.assertEqual(located, report_file)

    def test_locate_report_missing_file_raises(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            with self.assertRaises(FileNotFoundError):
                self.mutation.locate_report(tmp)

    def test_count_selected_mutants_excludes_ignored(self):
        report = {
            "files": {
                "Foo.cs": {
                    "mutants": [
                        {"status": "Ignored"},
                        {"status": "Killed"},
                        {"status": "Survived"},
                    ]
                }
            }
        }
        self.assertEqual(self.mutation.count_selected_mutants(report), 2)

    def test_process_report_zero_mutants_raises(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            raw = tmp / "raw.json"
            raw.write_text(json.dumps({
                "files": {
                    "Foo.cs": {"mutants": [{"status": "Ignored"}]}
                }
            }), encoding="utf-8")
            out = tmp / "out.json"
            with self.assertRaises(ValueError) as ctx:
                self.mutation.process_report(raw, out, REPO_ROOT, scope="Foo/**")
            self.assertIn("0 selected mutants", str(ctx.exception))

    def test_process_report_normalizes_and_writes_output(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            raw = tmp / "raw.json"
            abs_path = str((REPO_ROOT / "src" / "ContentTypeHelper.cs").resolve())
            raw.write_text(json.dumps({
                "files": {
                    abs_path: {"mutants": [{"status": "Killed"}]}
                }
            }), encoding="utf-8")
            out = tmp / "out.json"
            norm = self.mutation.process_report(raw, out, REPO_ROOT, scope="ContentTypeHelper.cs")
            self.assertTrue(out.is_file())
            self.assertIn("files", norm)
            self.assertIn("ContentTypeHelper.cs", norm["files"])
            self.assertNotIn(abs_path, norm["files"])


if __name__ == "__main__":
    unittest.main()
