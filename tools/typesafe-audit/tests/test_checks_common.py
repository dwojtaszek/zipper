"""Contract tests for shared advisory-check command-line arguments."""

import argparse
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import checks_common  # noqa: E402


class CheckArgumentsTests(unittest.TestCase):
    def test_add_report_arguments_defaults_and_types(self):
        parser = argparse.ArgumentParser()
        fixture_dir = Path("fixtures")
        checks_common.add_report_arguments(parser, fixture_dir)

        args = parser.parse_args(["--json-out", "report.json", "--md-out", "report.md"])

        self.assertEqual("fixture", args.mode)
        self.assertEqual(fixture_dir, args.fixture_dir)
        self.assertEqual(Path("report.json"), args.json_out)
        self.assertEqual(Path("report.md"), args.md_out)
        self.assertIsNone(args.summary_out)
        self.assertIn("Append the Markdown report to this file (CI job", parser.format_help())

    def test_add_report_arguments_preserves_overrides_and_help(self):
        parser = argparse.ArgumentParser()
        checks_common.add_report_arguments(parser, Path("fixtures"), "Custom summary help.")

        args = parser.parse_args([
            "--mode", "live", "--fixture-dir", "other",
            "--json-out", "report.json", "--md-out", "report.md",
            "--summary-out", "summary.md",
        ])

        self.assertEqual("live", args.mode)
        self.assertEqual(Path("other"), args.fixture_dir)
        self.assertEqual(Path("summary.md"), args.summary_out)
        self.assertIn("Custom summary help.", parser.format_help())
        with self.assertRaises(SystemExit):
            parser.parse_args(["--mode", "invalid", "--json-out", "report.json", "--md-out", "report.md"])


if __name__ == "__main__":
    unittest.main()
