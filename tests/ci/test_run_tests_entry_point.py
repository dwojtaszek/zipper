"""Regression guard for #1405: run-tests.bat must have an explicit entry point.

Without 'goto :main' before helper subroutines, batch execution starts at line 1
and falls straight through into the first helper (:print_success), which terminates
with 'goto :eof' and exits 0 in 0.02s without running any tests.
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUN_TESTS_BAT = ROOT / "tests" / "run-tests.bat"


class RunTestsBatEntryPointTests(unittest.TestCase):
    def setUp(self):
        self.bat_content = RUN_TESTS_BAT.read_text(encoding="utf-8")
        self.lines = self.bat_content.splitlines()

    def test_goto_main_precedes_first_subroutine_label(self):
        """run-tests.bat must jump to :main before falling into helper subroutines."""
        first_label_idx = None
        for idx, line in enumerate(self.lines):
            stripped = line.strip()
            if stripped.startswith(":") and not stripped.startswith("::"):
                first_label_idx = idx
                break

        self.assertIsNotNone(first_label_idx, "run-tests.bat must have labels")

        pre_label_text = "\n".join(self.lines[:first_label_idx])
        has_goto_main = bool(
            re.search(r"^\s*goto\s+:?main\b", pre_label_text, re.IGNORECASE | re.MULTILINE)
        )
        self.assertTrue(
            has_goto_main,
            f"run-tests.bat has label '{self.lines[first_label_idx].strip()}' at line {first_label_idx + 1} "
            "with no 'goto :main' before it. Execution falls straight into subroutines and exits early (#1405).",
        )

    def test_main_label_exists_before_test_cases(self):
        """:main label must exist and appear before the first test case execution."""
        main_label_idx = None
        first_case_idx = None

        for idx, line in enumerate(self.lines):
            stripped = line.strip()
            if re.match(r"^:main\b", stripped, re.IGNORECASE):
                if main_label_idx is None:
                    main_label_idx = idx
            if re.search(r'call\s+:run_test_case\s+"Test Case 1:', stripped, re.IGNORECASE):
                if first_case_idx is None:
                    first_case_idx = idx

        self.assertIsNotNone(main_label_idx, "run-tests.bat must contain a ':main' label")
        self.assertIsNotNone(first_case_idx, "run-tests.bat must contain Test Case 1")
        self.assertLess(
            main_label_idx,
            first_case_idx,
            "':main' label must appear before the first test case execution",
        )

    def test_no_subroutines_with_goto_eof_between_main_and_end_of_tests(self):
        """Subroutines ending in 'goto :eof' or 'exit /b' must not be placed between :main and :end_of_tests.

        In batch files, falling through into a subroutine that ends with 'goto :eof' or 'exit /b'
        causes the script to terminate early when not called via 'call'.
        """
        in_main = False
        seen_main = False
        seen_end = False
        subroutine_labels = []

        for idx, line in enumerate(self.lines):
            stripped = line.strip()
            if re.match(r"^:main\b", stripped, re.IGNORECASE):
                in_main = True
                seen_main = True
                continue
            if re.match(r"^:end_of_tests\b", stripped, re.IGNORECASE):
                in_main = False
                seen_end = True
                break

            if in_main and stripped.startswith(":") and not stripped.startswith("::"):
                # Check if this label block contains goto :eof or exit /b
                label = stripped
                # Look ahead for goto :eof or exit /b before next label
                has_terminator = False
                for forward_line in self.lines[idx + 1:]:
                    f_stripped = forward_line.strip()
                    if f_stripped.startswith(":") and not f_stripped.startswith("::"):
                        break
                    if re.match(r"^(goto\s*:?eof|exit\s*/b|exit\s+[0-9]+)\b", f_stripped, re.IGNORECASE):
                        has_terminator = True
                        break
                if has_terminator:
                    subroutine_labels.append(label)

        self.assertTrue(seen_main, "run-tests.bat must contain a ':main' label")
        self.assertTrue(seen_end, "run-tests.bat must contain an ':end_of_tests' label")
        self.assertEqual(
            subroutine_labels,
            [],
            f"Helper subroutines terminating early found between :main and :end_of_tests: {subroutine_labels}. "
            "These must be moved before :main so sequential execution does not fall into them and terminate early.",
        )


if __name__ == "__main__":
    unittest.main()
