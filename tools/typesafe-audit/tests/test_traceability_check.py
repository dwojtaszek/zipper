#!/usr/bin/env python3
"""Tests for the semantic traceability audit check (#957). No network."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[3]
CHECK_DIR = REPO_ROOT / "tools/typesafe-audit/checks/traceability"
CORPUS_DIR = REPO_ROOT / "tests/typesafe-audit-fixtures/traceability"


def _load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


# Explicit-path imports: both audit checks define parse.py/run_check.py, so
# plain `import` would collide within one test process.
trace_parse = _load_module("tsa_trace_parse", CHECK_DIR / "parse.py")
trace_run_check = _load_module("tsa_trace_run_check", CHECK_DIR / "run_check.py")
ParseError = trace_parse.ParseError
parse_tsv = trace_parse.parse_tsv
resolve_e2e_reference = trace_parse.resolve_e2e_reference
resolve_unit_reference = trace_parse.resolve_unit_reference
run_check_main = trace_run_check.main
EXIT_INPUT_ERROR = trace_run_check.EXIT_INPUT_ERROR
load_json = trace_run_check.load_json

POLICY = load_json(CHECK_DIR / "policy.json")


class TsvParserTests(unittest.TestCase):
    def test_parses_valid_rows(self):
        rows = parse_tsv(CORPUS_DIR / "req-traceability.tsv")
        self.assertEqual(8, len(rows))
        self.assertEqual("REQ-910", rows[0]["req_id"])

    def test_rejects_malformed_row(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "bad.tsv"
            bad.write_text("req_id\tcoverage\treference\tnotes\nREQ-1\tunit\n", encoding="utf-8")
            with self.assertRaises(ParseError):
                parse_tsv(bad)

    def test_rejects_exemption_without_rationale(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "bad.tsv"
            bad.write_text("req_id\tcoverage\treference\tnotes\nREQ-1\texemption\t-\t\n", encoding="utf-8")
            with self.assertRaises(ParseError):
                parse_tsv(bad)

    def test_rejects_unknown_coverage(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "bad.tsv"
            bad.write_text("req_id\tcoverage\treference\tnotes\nREQ-1\tmaybe\tX\t-\n", encoding="utf-8")
            with self.assertRaises(ParseError):
                parse_tsv(bad)


class UnitResolverTests(unittest.TestCase):
    def test_source_index_reuses_reads_and_preserves_method_evidence(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "FixtureTests.cs"
            source.write_text(
                "public partial class FixtureTests {\n"
                " public void First() { Assert.Equal(1, result); }\n"
                " public void Second() { Assert.Equal(2, result); }\n"
                "}\npublic partial class FixtureTests {}\n"
            )
            script = root / "check.sh"
            script.write_text("echo check\n")
            expected = {
                reference: resolve_unit_reference(root, reference)
                for reference in ("FixtureTests.First", "FixtureTests.Second")
            }
            expected_script = resolve_e2e_reference(root, "check.sh")
            with mock.patch.object(trace_parse, "_read", wraps=trace_parse._read) as reads:
                index = trace_parse.SourceIndex(root)
                for reference, evidence in expected.items():
                    self.assertEqual(evidence, resolve_unit_reference(root, reference, index))
                self.assertEqual(expected_script, resolve_e2e_reference(root, "check.sh", index))
                self.assertEqual(expected_script, resolve_e2e_reference(root, "check.sh", index))
                self.assertEqual([mock.call(source), mock.call(script)], reads.call_args_list)

    def test_source_index_preserves_ambiguity_and_missing_method_errors(self):
        index = trace_parse.SourceIndex(CORPUS_DIR / "src")
        for reference in ("DupTests.Dup", "FixtureTests.WasRenamedAway"):
            with self.subTest(reference=reference):
                with self.assertRaises(ParseError) as uncached:
                    resolve_unit_reference(CORPUS_DIR / "src", reference)
                with self.assertRaises(ParseError) as indexed:
                    resolve_unit_reference(CORPUS_DIR / "src", reference, index)
                self.assertEqual(str(uncached.exception), str(indexed.exception))

    def test_source_index_preserves_escaped_class_identifiers(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "EscapedTests.cs").write_text(
                "class @FixtureTests { public void Check() { Assert.Equal(1, result); } }\n"
            )
            expected = resolve_unit_reference(root, "@FixtureTests.Check")
            actual = resolve_unit_reference(root, "@FixtureTests.Check", trace_parse.SourceIndex(root))
            self.assertEqual(expected, actual)

    def test_source_index_is_fresh_for_each_preparation(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "FreshTests.cs"
            source.write_text("class FreshTests { public void Check() { Assert.True(true); } }\n")
            first = resolve_unit_reference(root, "FreshTests.Check", trace_parse.SourceIndex(root))
            source.write_text("class FreshTests { public void Check() { Assert.False(false); } }\n")
            second = resolve_unit_reference(root, "FreshTests.Check", trace_parse.SourceIndex(root))
            self.assertIn("Assert.False(false)", second["body"])
            self.assertNotEqual(first["sha256"], second["sha256"])

    def test_repository_unit_mappings_resolve_real_test_bodies(self):
        rows = parse_tsv(REPO_ROOT / "tests/req-traceability.tsv")
        unit_rows = [row for row in rows if row["coverage"] == "unit"]
        self.assertGreater(len(unit_rows), 0)
        index = trace_parse.SourceIndex(REPO_ROOT / "src/Zipper.Tests")
        for row in unit_rows:
            with self.subTest(req_id=row["req_id"], reference=row["reference"]):
                resolved = resolve_unit_reference(REPO_ROOT / "src/Zipper.Tests", row["reference"], index)
                self.assertTrue(resolved["body"].strip())
                self.assertEqual(64, len(resolved["sha256"]))

    def test_repository_non_unit_mappings_resolve_real_test_sources(self):
        rows = parse_tsv(REPO_ROOT / "tests/req-traceability.tsv")
        index = trace_parse.SourceIndex(REPO_ROOT)
        for row in (row for row in rows if row["coverage"] != "unit"):
            with self.subTest(req_id=row["req_id"], reference=row["reference"]):
                resolved = trace_parse.resolve_reference(REPO_ROOT, row, index)
                if row["coverage"] == "exemption":
                    self.assertIsNone(resolved)
                else:
                    self.assertTrue(resolved["body"].strip())

    def test_resolves_fact_method_body(self):
        resolved = resolve_unit_reference(CORPUS_DIR / "src", "FixtureTests.Full")
        self.assertIn("Assert.Equal(3, result)", resolved["body"])
        self.assertEqual(64, len(resolved["sha256"]))

    def test_resolves_theory_method_body(self):
        resolved = resolve_unit_reference(CORPUS_DIR / "src", "FixtureTests.TheoryFull")
        self.assertIn("Assert.Equal(expected, result)", resolved["body"])

    def test_resolves_method_in_nested_class(self):
        resolved = resolve_unit_reference(CORPUS_DIR / "src", "NestedFixtureTests.InnerCheck")
        self.assertIn("Assert.NotEmpty", resolved["body"])

    def test_duplicate_method_names_are_ambiguous(self):
        with self.assertRaises(ParseError) as ctx:
            resolve_unit_reference(CORPUS_DIR / "src", "DupTests.Dup")
        self.assertIn("ambiguous", str(ctx.exception))

    def test_renamed_test_is_unresolved(self):
        with self.assertRaises(ParseError):
            resolve_unit_reference(CORPUS_DIR / "src", "FixtureTests.WasRenamedAway")

    def test_malformed_reference_is_rejected(self):
        with self.assertRaises(ParseError):
            resolve_unit_reference(CORPUS_DIR / "src", "NoDot")

    def test_sibling_class_method_lookup_is_restricted_to_requested_class(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "SharedFile.cs").write_text(
                "public class ExpectedTests {\n"
                "    public void OnlyInExpected() { Assert.True(true); }\n"
                "}\n"
                "public class OtherTests {\n"
                "    public void M() { Assert.Equal(1, 2); }\n"
                "}\n",
                encoding="utf-8",
            )
            # ExpectedTests.M must fail and not resolve OtherTests.M
            with self.assertRaises(ParseError) as ctx:
                resolve_unit_reference(root, "ExpectedTests.M")
            self.assertIn("not found", str(ctx.exception))

            # OtherTests.M resolves correctly
            other = resolve_unit_reference(root, "OtherTests.M")
            self.assertIn("Assert.Equal(1, 2)", other["body"])

            # Sibling classes sharing same method name don't collide or cause false ambiguity
            (root / "SharedMethods.cs").write_text(
                "public class SiblingOne {\n"
                "    public void Common() { Assert.Equal(1, 1); }\n"
                "}\n"
                "public class SiblingTwo {\n"
                "    public void Common() { Assert.Equal(2, 2); }\n"
                "}\n",
                encoding="utf-8",
            )
            s1 = resolve_unit_reference(root, "SiblingOne.Common")
            s2 = resolve_unit_reference(root, "SiblingTwo.Common")
            self.assertIn("Assert.Equal(1, 1)", s1["body"])
            self.assertIn("Assert.Equal(2, 2)", s2["body"])

    def test_literal_and_comment_braces_preserve_complete_test_body_and_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "BraceTests.cs").write_text(
                "public class BraceTests {\n"
                "    public void WithStringBrace()\n"
                "    {\n"
                "        var s = \"}\";\n"
                "        Assert.Equal(1, 2);\n"
                "    }\n"
                "    public void WithCommentBrace()\n"
                "    {\n"
                "        // single line brace: }\n"
                "        /* block brace: } */\n"
                "        Assert.True(true);\n"
                "    }\n"
                "    public void WithRawStringBrace()\n"
                "    {\n"
                "        var json = \"\"\"\n"
                "        {\n"
                "          \"key\": \"value}\"\n"
                "        }\n"
                "        \"\"\";\n"
                "        Assert.NotNull(json);\n"
                "    }\n"
                "    public void WithInterpolatedRawBrace()\n"
                "    {\n"
                "        var val = 42;\n"
                "        var text = $$\"\"\"\n"
                "        {\n"
                "          \"k\": \"{{val}}\"\n"
                "        }\n"
                "        \"\"\";\n"
                "        Assert.Contains(\"42\", text);\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            # Test string brace preservation
            str_case = resolve_unit_reference(root, "BraceTests.WithStringBrace")
            self.assertIn("Assert.Equal(1, 2);", str_case["body"])
            expected_body = (
                "    public void WithStringBrace()\n"
                "    {\n"
                "        var s = \"}\";\n"
                "        Assert.Equal(1, 2);\n"
                "    }"
            )
            self.assertEqual(expected_body, str_case["body"])
            self.assertEqual(trace_parse.sha256_text(expected_body), str_case["sha256"])

            # Test comment brace preservation
            comment_case = resolve_unit_reference(root, "BraceTests.WithCommentBrace")
            self.assertIn("Assert.True(true);", comment_case["body"])

            # Test raw string brace preservation
            raw_case = resolve_unit_reference(root, "BraceTests.WithRawStringBrace")
            self.assertIn("Assert.NotNull(json);", raw_case["body"])

            # Test interpolated raw string brace preservation
            interp_case = resolve_unit_reference(root, "BraceTests.WithInterpolatedRawBrace")
            self.assertIn("Assert.Contains(\"42\", text);", interp_case["body"])

    def test_expression_bodied_test_methods(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "ExprTests.cs").write_text(
                "public class ExprTests {\n"
                "    public void InlineExpr() => Assert.True(true);\n"
                "    public void MultiLineExpr() =>\n"
                "        Assert.Equal(42, 40 + 2);\n"
                "    public async Task AsyncExpr() => await Task.Yield();\n"
                "    public void LambdaExpr() => Assert.Throws<InvalidOperationException>(() => {\n"
                "        throw new InvalidOperationException();\n"
                "    });\n"
                "    public void InitExpr() => Assert.NotNull(new { Foo = 1 });\n"
                "    public void SwitchExpr() => Assert.True(1 switch { 1 => true, _ => false });\n"
                "}\n",
                encoding="utf-8",
            )
            inline_case = resolve_unit_reference(root, "ExprTests.InlineExpr")
            self.assertEqual("    public void InlineExpr() => Assert.True(true);", inline_case["body"])
            self.assertEqual(trace_parse.sha256_text(inline_case["body"]), inline_case["sha256"])

            multi_case = resolve_unit_reference(root, "ExprTests.MultiLineExpr")
            expected_multi = (
                "    public void MultiLineExpr() =>\n"
                "        Assert.Equal(42, 40 + 2);"
            )
            self.assertEqual(expected_multi, multi_case["body"])
            self.assertEqual(trace_parse.sha256_text(expected_multi), multi_case["sha256"])

            async_case = resolve_unit_reference(root, "ExprTests.AsyncExpr")
            self.assertEqual("    public async Task AsyncExpr() => await Task.Yield();", async_case["body"])

            lambda_case = resolve_unit_reference(root, "ExprTests.LambdaExpr")
            self.assertIn("Assert.Throws<InvalidOperationException>", lambda_case["body"])
            self.assertIn("throw new InvalidOperationException();", lambda_case["body"])
            self.assertTrue(lambda_case["body"].endswith("});"))
            self.assertEqual(trace_parse.sha256_text(lambda_case["body"]), lambda_case["sha256"])

            init_case = resolve_unit_reference(root, "ExprTests.InitExpr")
            self.assertEqual("    public void InitExpr() => Assert.NotNull(new { Foo = 1 });", init_case["body"])

            switch_case = resolve_unit_reference(root, "ExprTests.SwitchExpr")
            self.assertEqual("    public void SwitchExpr() => Assert.True(1 switch { 1 => true, _ => false });", switch_case["body"])

    def test_generic_method_with_new_constraint(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "GenericTests.cs").write_text(
                "public class GenericTests {\n"
                "    public void TestGeneric<T>() where T : new() {\n"
                "        var item = new T();\n"
                "        Assert.NotNull(item);\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            case = resolve_unit_reference(root, "GenericTests.TestGeneric")
            self.assertIn("where T : new()", case["body"])
            self.assertIn("Assert.NotNull(item);", case["body"])

    def test_nested_and_partial_class_semantics(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "NestedTests.cs").write_text(
                "public class OuterTests {\n"
                "    public void OuterOnly() { Assert.True(true); }\n"
                "    public class InnerTests {\n"
                "        public void InnerOnly() { Assert.Equal(1, 1); }\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            # Method inside nested class resolved via outer class
            nested_via_outer = resolve_unit_reference(root, "OuterTests.InnerOnly")
            self.assertIn("Assert.Equal(1, 1)", nested_via_outer["body"])

            # Method inside nested class resolved via qualified class name
            nested_qualified = resolve_unit_reference(root, "OuterTests.InnerTests.InnerOnly")
            self.assertIn("Assert.Equal(1, 1)", nested_qualified["body"])

            # Method inside nested class resolved directly via inner class name
            nested_direct = resolve_unit_reference(root, "InnerTests.InnerOnly")
            self.assertEqual(nested_qualified["body"], nested_direct["body"])

            # When outer and inner have same method name, Outer resolves outer and Qualified resolves inner
            (root / "AmbiguousNested.cs").write_text(
                "public class CollidingOuter {\n"
                "    public void Clashing() { Assert.True(true); }\n"
                "    public class CollidingInner {\n"
                "        public void Clashing() { Assert.False(false); }\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            outer_clash = resolve_unit_reference(root, "CollidingOuter.Clashing")
            self.assertIn("Assert.True(true)", outer_clash["body"])

            qualified_inner = resolve_unit_reference(root, "CollidingOuter.CollidingInner.Clashing")
            self.assertIn("Assert.False(false)", qualified_inner["body"])

            # Record struct and record class
            (root / "RecordTests.cs").write_text(
                "public record struct RecordStructTests {\n"
                "    public void StructMethod() { Assert.True(true); }\n"
                "}\n"
                "public record class RecordClassTests {\n"
                "    public void ClassMethod() { Assert.True(true); }\n"
                "}\n",
                encoding="utf-8",
            )
            rs = resolve_unit_reference(root, "RecordStructTests.StructMethod")
            self.assertIn("Assert.True(true)", rs["body"])
            rc = resolve_unit_reference(root, "RecordClassTests.ClassMethod")
            self.assertIn("Assert.True(true)", rc["body"])

            # Partial class split across files
            (root / "Part1.cs").write_text(
                "public partial class SplitTests {\n"
                "    public void PartOneMethod() { Assert.True(true); }\n"
                "}\n",
                encoding="utf-8",
            )
            (root / "Part2.cs").write_text(
                "public partial class SplitTests {\n"
                "    public void PartTwoMethod() { Assert.False(false); }\n"
                "}\n",
                encoding="utf-8",
            )
            p1 = resolve_unit_reference(root, "SplitTests.PartOneMethod")
            p2 = resolve_unit_reference(root, "SplitTests.PartTwoMethod")
            self.assertIn("Assert.True(true)", p1["body"])
            self.assertIn("Assert.False(false)", p2["body"])

    def test_unsupported_or_malformed_syntax_raises_parse_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            bad_brace = root / "UnclosedBrace.cs"
            bad_brace.write_text("public class Broken { public void M() {", encoding="utf-8")
            with self.assertRaises(ParseError):
                resolve_unit_reference(root, "Broken.M")

            bad_string = root / "UnclosedString.cs"
            bad_string.write_text("public class BrokenStr { public void M() { var s = \"unterminated; } }", encoding="utf-8")
            with self.assertRaises(ParseError):
                resolve_unit_reference(root, "BrokenStr.M")

            bad_comment = root / "UnclosedComment.cs"
            bad_comment.write_text("public class BrokenComment { public void M() { /* unterminated } }", encoding="utf-8")
            with self.assertRaises(ParseError):
                resolve_unit_reference(root, "BrokenComment.M")


class E2eResolverTests(unittest.TestCase):
    def test_resolves_whole_script_reference(self):
        resolved = resolve_e2e_reference(CORPUS_DIR, "sample.sh")
        self.assertEqual((CORPUS_DIR / "sample.sh").read_text().strip(), resolved["body"].strip())
        self.assertEqual(1, resolved["line"])

    def test_resolves_script_under_repository_tests_directory(self):
        resolved = resolve_e2e_reference(REPO_ROOT, "test-production-sets.sh Test Case 1c")
        self.assertIn("--production-id", resolved["body"])
        self.assertIn("tests/test-production-sets.sh", resolved["source"])

    def test_resolves_scenario_block(self):
        resolved = resolve_e2e_reference(CORPUS_DIR, "sample.sh full-scenario")
        self.assertIn("assert output A", resolved["body"])
        self.assertNotIn("partial-scenario", resolved["body"])

    def test_unknown_scenario_is_unresolved(self):
        with self.assertRaises(ParseError):
            resolve_e2e_reference(CORPUS_DIR, "sample.sh missing-scenario")


class CorpusEvaluationTests(unittest.TestCase):
    def test_changed_scope_uses_bounded_batches_and_reports_every_requirement(self):
        rows = parse_tsv(CORPUS_DIR / "req-traceability.tsv")
        scope = sorted({row["req_id"] for row in rows if row["coverage"] != "exemption"})
        lines = (CORPUS_DIR / "req-traceability.tsv").read_text().splitlines()
        original_load_json = trace_run_check.load_json
        with tempfile.TemporaryDirectory() as tmp:
            fixtures = Path(tmp)
            for start in range(0, len(scope), 2):
                batch = set(scope[start:start + 2])
                tsv = fixtures / f"batch-{start}.tsv"
                # Retain original row numbers, which are part of request evidence.
                tsv.write_text("\n".join(
                    line if index == 0 or line.split("\t")[0] in batch else ""
                    for index, line in enumerate(lines)
                ) + "\n")
                recorded = subprocess.run([
                    sys.executable, str(CHECK_DIR / "record_corpus_fixtures.py"),
                    str(fixtures), str(tsv), str(CORPUS_DIR / "src"),
                    str(CORPUS_DIR), str(CHECK_DIR / "questions.json"),
                ], capture_output=True, text=True)
                self.assertEqual(0, recorded.returncode, recorded.stderr)

            def configured_json(path):
                data = original_load_json(path)
                return {**data, "batch_size": 2} if path == trace_run_check.POLICY_PATH else data

            report_path = fixtures / "report.json"
            with mock.patch.object(trace_run_check, "changed_req_ids", return_value=set(scope)), \
                    mock.patch.object(trace_run_check, "load_json", side_effect=configured_json):
                code = run_check_main([
                    "--base", "fixture-base", "--tsv", str(CORPUS_DIR / "req-traceability.tsv"),
                    "--tests-root", str(CORPUS_DIR / "src"), "--requirements-root", str(CORPUS_DIR),
                    "--mode", "fixture", "--fixture-dir", str(fixtures),
                    "--json-out", str(report_path), "--md-out", str(fixtures / "report.md"),
                ])
            self.assertEqual(0, code)
            results = json.loads(report_path.read_text())["results"]
            self.assertEqual(set(scope), {result["req_id"] for result in results})
            self.assertEqual(len(scope) * 3, len(results))

    def test_corpus_labels_reproduced_with_recorded_responses(self):
        fixtures = Path(tempfile.mkdtemp())
        recorder = subprocess.run(
            [sys.executable, str(CHECK_DIR / "record_corpus_fixtures.py"),
             str(fixtures),
             str(CORPUS_DIR / "req-traceability.tsv"),
             str(CORPUS_DIR / "src"),
             str(CORPUS_DIR),
             str(CHECK_DIR / "questions.json")],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(0, recorder.returncode, recorder.stderr)

        json_out = fixtures / "report.json"
        md_out = fixtures / "report.md"
        with mock.patch.object(
            trace_run_check._parse_mod, "_read", wraps=trace_run_check._parse_mod._read,
        ) as reads:
            code = run_check_main([
                "--full",
                "--tsv", str(CORPUS_DIR / "req-traceability.tsv"),
                "--tests-root", str(CORPUS_DIR / "src"),
                "--requirements-root", str(CORPUS_DIR),
                "--mode", "fixture",
                "--fixture-dir", str(fixtures),
                "--json-out", str(json_out),
                "--md-out", str(md_out),
            ])
        read_paths = [call.args[0] for call in reads.call_args_list]
        self.assertGreater(len(read_paths), 0)
        self.assertEqual(len(set(read_paths)), len(read_paths))
        self.assertEqual(0, code)

        report = json.loads(json_out.read_text(encoding="utf-8"))
        expected = {
            e["file"]: e["expected"]
            for e in load_json(CORPUS_DIR / "expected_labels.json")["examples"]
        }
        by_req = {}
        for result in report["results"]:
            by_req.setdefault((result["req_id"], result["question"]), result)

        for req_id, labels in expected.items():
            for question, value in labels.items():
                result = by_req[(req_id, question)]
                if question == "coverage":
                    self.assertEqual(value, result["answer"], f"{req_id}/{question}")
                else:
                    self.assertEqual(value, result["answer"] >= POLICY["noul_finding_range"][1], f"{req_id}/{question}")

        # Exemption rows are outside the audited scope.
        self.assertFalse(any(r["req_id"] == "REQ-914" for r in report["results"]))
        self.assertFalse(report["needs_human_review"])

    def test_report_json_is_stable_byte_for_byte(self):
        fixtures = Path(tempfile.mkdtemp())
        record = subprocess.run(
            [sys.executable, str(CHECK_DIR / "record_corpus_fixtures.py"),
             str(fixtures),
             str(CORPUS_DIR / "req-traceability.tsv"),
             str(CORPUS_DIR / "src"),
             str(CORPUS_DIR),
             str(CHECK_DIR / "questions.json")],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(0, record.returncode, record.stderr)
        base_args = [
            "--full",
            "--tsv", str(CORPUS_DIR / "req-traceability.tsv"),
            "--tests-root", str(CORPUS_DIR / "src"),
            "--requirements-root", str(CORPUS_DIR),
            "--mode", "fixture",
            "--fixture-dir", str(fixtures),
        ]
        first = fixtures / "a.json"
        run_check_main([*base_args, "--json-out", str(first), "--md-out", str(fixtures / "a.md")])
        second = fixtures / "b.json"
        run_check_main([*base_args, "--json-out", str(second), "--md-out", str(fixtures / "b.md")])
        self.assertEqual(first.read_text(encoding="utf-8"), second.read_text(encoding="utf-8"))

    def test_unresolved_reference_fails_deterministically_before_api(self):
        code = run_check_main([
            "--full",
            "--tsv", str(CORPUS_DIR / "req-traceability.tsv"),
            "--tests-root", str(CORPUS_DIR / "src"),
            "--requirements-root", str(CORPUS_DIR),
            "--mode", "live",
            "--json-out", str(Path(tempfile.mkdtemp()) / "o.json"),
            "--md-out", str(Path(tempfile.mkdtemp()) / "o.md"),
        ])
        self.assertEqual(EXIT_INPUT_ERROR, code)


class WorkflowWiringTests(unittest.TestCase):
    def test_workflow_runs_strict_gate_then_semantic_audit(self):
        workflow = (REPO_ROOT / ".github/workflows/typesafe-audit.yml").read_text(encoding="utf-8")
        self.assertIn("validate-req-traceability.sh --strict", workflow)
        self.assertIn("checks/traceability/run_check.py", workflow)


if __name__ == "__main__":
    unittest.main()
