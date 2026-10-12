import subprocess
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[1] / "diff-loadfile.sh"


class DiffLoadfileTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

    def run_diff(self, *args):
        return subprocess.run(
            ["bash", str(SCRIPT), *map(str, args)],
            capture_output=True,
            text=True,
            check=False,
        )

    def test_no_args_reports_usage(self):
        result = self.run_diff()

        self.assertEqual(result.returncode, 2)
        self.assertIn("usage: diff-loadfile.sh", result.stderr)

    def test_missing_files_are_rejected(self):
        result = self.run_diff(self.root / "missing-a", self.root / "missing-b")

        self.assertEqual(result.returncode, 2)
        self.assertIn("not readable", result.stderr)

    def test_identical_files_exit_silently(self):
        expected = self.root / "expected.dat"
        actual = self.root / "actual.dat"
        expected.write_text("one\ntwo\nthree\n", encoding="utf-8")
        actual.write_text("one\ntwo\nthree\n", encoding="utf-8")

        result = self.run_diff(expected, actual)

        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "")
        self.assertEqual(result.stderr, "")

    def test_different_files_report_first_divergence(self):
        expected = self.root / "expected.dat"
        actual = self.root / "actual.dat"
        expected.write_text("one\ntwo\nthree\nfour\n", encoding="utf-8")
        actual.write_text("one\nTWO\nthree\nfour\n", encoding="utf-8")

        result = self.run_diff(expected, actual)
        output = result.stdout + result.stderr

        self.assertEqual(result.returncode, 1)
        self.assertIn("First differing line: 2", output)
        self.assertIn("two", output)
        self.assertIn("TWO", output)

    def test_trailing_newline_difference_is_detected(self):
        expected = self.root / "expected.dat"
        actual = self.root / "actual.dat"
        expected.write_bytes(b"a\nb\n")
        actual.write_bytes(b"a\nb")

        result = self.run_diff(expected, actual)

        self.assertEqual(result.returncode, 1)

    def test_difference_output_is_bounded(self):
        expected = self.root / "expected.dat"
        actual = self.root / "actual.dat"
        lines = [f"line{line}" for line in range(1, 5001)]
        expected.write_text("\n".join(lines) + "\n", encoding="utf-8")
        lines[9] = "DIVERGED"
        actual.write_text("\n".join(lines) + "\n", encoding="utf-8")

        result = self.run_diff(expected, actual)
        output = result.stdout + result.stderr

        self.assertEqual(result.returncode, 1)
        self.assertLess(len(output.splitlines()), 100)
        self.assertIn("First differing line: 10", output)


if __name__ == "__main__":
    unittest.main()
