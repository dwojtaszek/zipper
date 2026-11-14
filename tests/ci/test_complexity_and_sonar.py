"""Tests for diff-scoped complexity guard and SonarCloud PR inspector."""
from __future__ import annotations

import http.client
import importlib.util
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def _load_script(name: str):
    path = ROOT / ".github" / "scripts" / name
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), path)
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class CheckComplexityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = _load_script("check-complexity.py")

    def test_python_complexity_catches_only_touched_complex_function(self):
        branches = "\n".join(f"    elif x == {i}:\n        return {i}" for i in range(18))
        source = (
            "def simple(x):\n    return x + 1\n\n"
            f"def legacy_complex(x):\n    if x == -1:\n        return -1\n{branches}\n    return 0\n"
        )
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / "sample.py"
            p.write_text(source, encoding="utf-8")
            self.assertEqual(self.mod.check_python_file(p, {1, 2}, 15), [])
            findings = self.mod.check_python_file(p, {6}, 15)
            self.assertEqual(len(findings), 1)
            self.assertIn("legacy_complex", findings[0])
            # Non-complexity edit inside a legacy complex function does not regress complexity.
            self.assertEqual(self.mod.check_python_source("sample.py", source, {6}, 15, base_source=source), [])

    def test_csharp_cognitive_complexity_handles_allman_braces(self):
        nested_ifs = "\n".join("        if (a > 0) {\n            if (b > 0) {\n                c++;\n            }\n        }" for _ in range(6))
        source = (
            "public class Sample\n{\n"
            "    public static int Clean(int x)\n    {\n        return x > 0 ? x : 0;\n    }\n\n"
            "    public static int TooComplex(int a, int b)\n    {\n        int c = 0;\n"
            f"{nested_ifs}\n        return c;\n    }}\n}}\n"
        )
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / "Sample.cs"
            p.write_text(source, encoding="utf-8")
            self.assertEqual(self.mod.check_csharp_file(p, {4, 5}, 15), [])
            findings = self.mod.check_csharp_file(p, {9, 10}, 15)
            self.assertEqual(len(findings), 1)
            self.assertIn("TooComplex", findings[0])

    def test_csharp_null_operators_are_excluded_from_cognitive_complexity(self):
        null_ops = "\n".join(f"        var x{i} = a?.ToString() ?? b?.ToString() ?? \"\";" for i in range(10))
        source = f"public class Sample\n{{\n    public static void NullSafe(object? a, object? b)\n    {{\n{null_ops}\n    }}\n}}\n"
        self.assertEqual(self.mod.check_csharp_source("Sample.cs", source, None, 15), [])

    def test_csharp_multiline_verbatim_string_is_excluded(self):
        if_lines = "\n".join("if (a) { b(); }" for _ in range(18))
        source = f'public class Sample\n{{\n    public static string Verbatim()\n    {{\n        return @"\n{if_lines}\n";\n    }}\n}}\n'
        self.assertEqual(self.mod.check_csharp_source("Sample.cs", source, None, 15), [])

    def test_python_match_arms_count_toward_complexity(self):
        cases = "\n".join(f"        case {i}:\n            return {i}" for i in range(16))
        source = f"def matcher(x):\n    match x:\n{cases}\n        case _:\n            return -1\n"
        findings = self.mod.check_python_source("sample.py", source, None, 15)
        self.assertEqual(len(findings), 1)
        self.assertIn("matcher", findings[0])

    def test_csharp_escaped_backslash_char_literal_and_primary_constructor(self):
        branches = "\n".join("        if (x > 0) { x--; }" for _ in range(16))
        source = (
            "public class Primary(int x)\n{\n"
            "    public string Sep() => \"\\\\\";\n"
            "    public char Brace() => '{';\n"
            "    public int Clean(int a) => a > 0 ? a : 0;\n"
            "}\n"
        )
        self.assertEqual(self.mod.check_csharp_source("Primary.cs", source, None, 15), [])
        complex_expr = (
            "public class Expr\n{\n"
            f"    public static int Deep(int x) => {' + '.join('(x > 0 ? 1 : 0)' for _ in range(16))};\n"
            "}\n"
        )
        self.assertEqual(len(self.mod.check_csharp_source("Expr.cs", complex_expr, None, 15)), 1)
        primary_with_complex = f"public abstract class Primary(int x)\n{{\n    public void Run(int x)\n    {{\n{branches}\n    }}\n}}\n"
        findings = self.mod.check_csharp_source("Primary.cs", primary_with_complex, None, 15)
        self.assertEqual(len(findings), 1)
        self.assertIn("'Run'", findings[0])
        implicit_private = f"class Helper\n{{\n    void Secret(int x)\n    {{\n{branches}\n    }}\n}}\n"
        self.assertEqual(len(self.mod.check_csharp_source("Helper.cs", implicit_private, None, 15)), 1)
        record_param = f"class Renderer\n{{\n    void Render(LoadFileRecord record) where T : class\n    {{\n{branches}\n    }}\n}}\n"
        self.assertEqual(len(self.mod.check_csharp_source("Renderer.cs", record_param, None, 15)), 1)
        renamed_param = f"class Renderer\n{{\n    void Render(LoadFileRecord rec) where T : class\n    {{\n{branches}\n    }}\n}}\n"
        self.assertEqual(self.mod.check_csharp_source("Renderer.cs", renamed_param, None, 15, base_source=record_param), [])
        nullable_and_dowhile = (
            "class Loops\n{\n"
            "    void Clean(List<int?> items)\n    {\n"
            + "\n".join("        var x = (string?)null;" for _ in range(10))
            + "\n        do { items.Clear(); } while (false);\n    }\n}\n"
        )
        self.assertEqual(self.mod.check_csharp_source("Loops.cs", nullable_and_dowhile, None, 15), [])
        try_block = "class T\n{\n    void Run(bool a)\n    {\n        try {\n" + "\n".join("            if (a) { a = false; }" for _ in range(10)) + "\n        } finally { }\n    }\n}\n"
        self.assertEqual(self.mod.check_csharp_source("T.cs", try_block, None, 15), [])
        overload_base = f"class O\n{{\n    void M(int x)\n    {{\n{branches}\n    }}\n}}\n"
        overload_new = f"class O\n{{\n    void M(string s)\n    {{\n{branches}\n    }}\n}}\n"
        self.assertEqual(len(self.mod.check_csharp_source("O.cs", overload_new, None, 15, base_source=overload_base)), 1)
        one_liner = (
            "class OneLine\n{\n"
            "    bool A(bool x) { return x && !x || x && !x || x && !x || x && !x || x && !x; }\n"
            "    bool B(bool x) { return x && !x || x && !x || x && !x || x && !x || x && !x; }\n"
            "}\n"
        )
        self.assertEqual(self.mod.check_csharp_source("OneLine.cs", one_liner, None, 15), [])
        diff_type_new = f"class O2\n{{\n    void M(int x)\n    {{\n{branches}\n    }}\n}}\n"
        self.assertEqual(len(self.mod.check_csharp_source("O.cs", diff_type_new, None, 15, base_source=overload_base)), 1)
        tuple_ret = f"class Tuples\n{{\n    (int a, string b) Resolve()\n    {{\n{branches}\n    }}\n}}\n"
        self.assertEqual(len(self.mod.check_csharp_source("Tuples.cs", tuple_ret, None, 15)), 1)
        allman_nested = (
            "class Allman\n{\n    void Check(bool a, bool b)\n    {\n"
            + "\n".join("        if (a)\n        {\n            if (b)\n            {\n                a = false;\n            }\n        }" for _ in range(6))
            + "\n    }\n}\n"
        )
        self.assertEqual(len(self.mod.check_csharp_source("Allman.cs", allman_nested, None, 15)), 1)

        nested_py = "def choose(a, b):\n    return (\n        1 if a\n        else (2 if b else 3)\n    )\n"
        self.assertEqual(len(self.mod.check_python_source("nested.py", nested_py, None, 15)), 1)
        self.assertEqual(len(self.mod.check_python_source("nested.py", nested_py, {4}, 15)), 1)
        self.assertEqual(self.mod.check_python_source("nested.py", nested_py, {1}, 15), [])
        self.assertEqual(self.mod.check_python_source("nested.py", nested_py, None, 15, base_source=nested_py), [])
        outer_with_inner = (
            "def outer(a, b):\n"
            "    def inner(x, y):\n"
            "        return 1 if x else (2 if y else 3)\n"
            "    return 1 if a else (2 if b else 3)\n"
        )
        base_only_inner = (
            "def outer(a, b):\n"
            "    def inner(x, y):\n"
            "        return 1 if x else (2 if y else 3)\n"
            "    return 1 if a else 2\n"
        )
        self.assertEqual(len(self.mod.check_python_source("oi.py", outer_with_inner, {4}, 15, base_source=base_only_inner)), 1)
        module_nested = "X = 1 if True else (2 if False else 3)\n"
        self.assertEqual(len(self.mod.check_python_source("mod.py", module_nested, {1}, 15)), 1)
        two_nested = "def dup(a, b):\n    x = 1 if a else (2 if b else 3)\n    y = 4 if a else (5 if b else 6)\n    return x + y\n"
        one_nested = "def dup(a, b):\n    x = 1 if a else (2 if b else 3)\n    return x\n"
        self.assertEqual(len(self.mod.check_python_source("dup.py", two_nested, {3}, 15, base_source=one_nested)), 1)

    def test_python_nested_helper_does_not_inflate_enclosing_score(self):
        branches = "\n".join(f"            elif y == {i}:\n                return {i}" for i in range(18))
        source = (
            "def outer(flag):\n"
            "    if flag:\n"
            f"        def inner(y):\n            if y < 0:\n                return -1\n{branches}\n            return 0\n"
            "        return inner(1)\n"
            "    return 0\n"
        )
        findings = self.mod.check_python_source("nested.py", source, None, 15)
        self.assertEqual(len(findings), 1)
        self.assertIn("outer.inner", findings[0])


class CheckSonarTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = _load_script("check-sonar.py")

    def test_clean_gate_and_coverage_only_failure_return_zero(self):
        for gate in (
            {"projectStatus": {"status": "OK", "conditions": []}},
            {
                "projectStatus": {
                    "status": "ERROR",
                    "conditions": [{"metricKey": "new_coverage", "status": "ERROR", "actualValue": "0.0"}],
                }
            },
            {
                "projectStatus": {
                    "status": "ERROR",
                    "conditions": [{"metricKey": "coverage", "status": "ERROR", "actualValue": "75.0"}],
                }
            },
        ):
            with patch.object(self.mod, "_fetch_json", side_effect=[gate, {"issues": []}]):
                code, summary = self.mod.inspect_pr(1444)
                self.assertEqual(code, 0)
                self.assertTrue(summary["ok"])

    def test_missing_issue_severity_defaults_to_major(self):
        gate = {"projectStatus": {"status": "OK", "conditions": []}}
        issues = {"issues": [{"rule": "python:S1", "component": "p:f.py", "line": 3, "message": "m"}]}
        with patch.object(self.mod, "_fetch_json", side_effect=[gate, issues]):
            code, summary = self.mod.inspect_pr(1444)
            self.assertEqual(code, 1)
            self.assertEqual(len(summary["blocking_issues"]), 1)

    def test_non_ok_gate_status_none_fails(self):
        gate = {"projectStatus": {"status": "NONE", "conditions": []}}
        with patch.object(self.mod, "_fetch_json", side_effect=[gate, {"issues": []}]):
            code, summary = self.mod.inspect_pr(1444)
            self.assertEqual(code, 1)
            self.assertFalse(summary["ok"])

    def test_blocking_issue_or_non_coverage_gate_returns_one(self):
        gate = {"projectStatus": {"status": "OK", "conditions": []}}
        issues = {
            "issues": [
                {
                    "severity": "CRITICAL",
                    "rule": "python:S3776",
                    "line": 120,
                    "component": "dwojtaszek_zipper:runner.py",
                    "message": "Refactor this function",
                }
            ]
        }
        with patch.object(self.mod, "_fetch_json", side_effect=[gate, issues]):
            code, summary = self.mod.inspect_pr(1444)
            self.assertEqual(code, 1)
            self.assertFalse(summary["ok"])
            self.assertEqual(len(summary["blocking_issues"]), 1)

    def test_http_exception_returns_exit_code_two(self):
        with (
            patch.object(self.mod, "_fetch_json", side_effect=http.client.IncompleteRead(b"")),
            patch.object(sys, "argv", ["check-sonar.py", "1444"]),
            patch("sys.stderr", new_callable=io.StringIO) as err,
        ):
            self.assertEqual(self.mod.main(), 2)
            self.assertIn("Error querying SonarCloud", err.getvalue())

    def test_paginates_issues_past_first_hundred(self):
        gate = {"projectStatus": {"status": "OK", "conditions": []}}
        page1 = {"total": 101, "issues": [{"severity": "MINOR", "rule": "r1"} for _ in range(100)]}
        page2 = {"total": 101, "issues": [{"severity": "MAJOR", "rule": "r2", "component": "p:f.py", "line": 5, "message": "fix"}]}
        with patch.object(self.mod, "_fetch_json", side_effect=[gate, page1, page2]):
            code, summary = self.mod.inspect_pr(1444)
            self.assertEqual(code, 1)
            self.assertEqual(len(summary["blocking_issues"]), 1)
            self.assertEqual(len(summary["advisory_issues"]), 100)


if __name__ == "__main__":
    unittest.main()
