import hashlib
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[1] / "sha-manifest.sh"


class ShaManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

    def run_manifest(self, *args):
        return subprocess.run(
            ["bash", str(SCRIPT), *map(str, args)],
            capture_output=True,
            text=True,
            check=False,
        )

    def test_no_args_reports_usage(self):
        result = self.run_manifest()

        self.assertEqual(result.returncode, 1)
        self.assertIn("usage: sha-manifest.sh", result.stderr)

    def test_non_directory_is_rejected(self):
        result = self.run_manifest(self.root / "does-not-exist")

        self.assertEqual(result.returncode, 1)
        self.assertIn("not a directory", result.stderr)

    def test_empty_directory_produces_empty_output(self):
        result = self.run_manifest(self.root)

        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "")

    def test_single_file_produces_expected_manifest_line(self):
        (self.root / "a.txt").write_text("hello\n", encoding="utf-8")

        result = self.run_manifest(self.root)

        self.assertEqual(result.returncode, 0)
        self.assertEqual(
            result.stdout,
            "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03  a.txt\n",
        )

    def test_manifest_is_sorted_by_relative_path(self):
        (self.root / "sub").mkdir()
        (self.root / "zeta.txt").write_text("a\n", encoding="utf-8")
        (self.root / "alpha.txt").write_text("b\n", encoding="utf-8")
        (self.root / "sub" / "inner.txt").write_text("c\n", encoding="utf-8")

        result = self.run_manifest(self.root)

        self.assertEqual(result.returncode, 0)
        paths = [line.split("  ", 1)[1] for line in result.stdout.splitlines()]
        self.assertEqual(paths, ["alpha.txt", "sub/inner.txt", "zeta.txt"])

    def test_output_is_deterministic_across_runs(self):
        for name, value in (("a.txt", "one\n"), ("b.txt", "two\n"), ("c.txt", "three\n")):
            (self.root / name).write_text(value, encoding="utf-8")

        first = self.run_manifest(self.root)
        second = self.run_manifest(self.root)

        self.assertEqual(first.returncode, 0)
        self.assertEqual(second.returncode, 0)
        self.assertEqual(first.stdout, second.stdout)

    def test_output_has_no_trailing_blank_line(self):
        (self.root / "only.txt").write_text("x\n", encoding="utf-8")

        result = self.run_manifest(self.root)

        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout.count("\n"), 1)
        self.assertTrue(result.stdout.endswith("only.txt\n"))

    def test_nested_directories_are_included(self):
        nested = self.root / "a" / "b" / "c"
        nested.mkdir(parents=True)
        (nested / "file.txt").write_text("deep\n", encoding="utf-8")
        (self.root / "top.txt").write_text("top\n", encoding="utf-8")

        result = self.run_manifest(self.root)

        self.assertEqual(result.returncode, 0)
        paths = [line.split("  ", 1)[1] for line in result.stdout.splitlines()]
        self.assertEqual(paths, ["a/b/c/file.txt", "top.txt"])

    def test_uncompressed_zip_entry_is_hashed(self):
        with zipfile.ZipFile(self.root / "archive.zip", "w") as archive:
            archive.writestr("inner.txt", "hello inside zip\n")

        result = self.run_manifest(self.root)
        expected_hash = hashlib.sha256(b"hello inside zip\n").hexdigest()

        self.assertEqual(result.returncode, 0)
        self.assertEqual(
            result.stdout,
            f"{expected_hash}  "
            "archive.zip::inner.txt\n",
        )

    def test_corrupt_zip_archive_is_rejected(self):
        (self.root / "corrupt.zip").write_bytes(b"not a zip archive")

        result = self.run_manifest(self.root)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Error reading zip", result.stderr)
        self.assertIn("corrupt.zip", result.stderr)

    def test_changed_zip_entry_content_changes_manifest(self):
        archive_path = self.root / "archive.zip"
        with zipfile.ZipFile(archive_path, "w") as archive:
            archive.writestr("native.txt", "first native content\n")
        first = self.run_manifest(self.root)
        first_hash = hashlib.sha256(b"first native content\n").hexdigest()

        with zipfile.ZipFile(archive_path, "w") as archive:
            archive.writestr("native.txt", "changed native content\n")
        second = self.run_manifest(self.root)
        second_hash = hashlib.sha256(b"changed native content\n").hexdigest()

        self.assertEqual(first.returncode, 0)
        self.assertEqual(second.returncode, 0)
        self.assertIn(f"{first_hash}  archive.zip::native.txt", first.stdout)
        self.assertIn(f"{second_hash}  archive.zip::native.txt", second.stdout)
        self.assertNotEqual(first.stdout, second.stdout)


if __name__ == "__main__":
    unittest.main()
