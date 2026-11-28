"""Test split ZIP part-manifest validation without needing archive tools."""

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from split_zip_delivery import fingerprint, verify_parts  # noqa: E402


class SplitZipDeliveryTests(unittest.TestCase):
    def test_verify_parts_with_duplicate_ordinal_rejects_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = root / "delivery.z01"
            first.write_bytes(b"first")
            part = {"ordinal": 1, "filename": first.name, **fingerprint(first)}
            with self.assertRaisesRegex(ValueError, "ordinals"):
                verify_parts(root, [part, part], [])

    def test_verify_parts_with_unrelated_sibling_uses_manifest_order(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            parts = []
            for ordinal, name in enumerate(("delivery.z01", "delivery.z02", "delivery.zip"), 1):
                part = root / name
                part.write_bytes(name.encode("ascii"))
                parts.append({"ordinal": ordinal, "filename": name, **fingerprint(part)})
            (root / "delivery.z99").write_bytes(b"unrelated")
            verify_parts(root, parts, [])

            (root / "delivery.z02").unlink()
            verify_parts(root, parts, [2])
            with self.assertRaises(FileNotFoundError):
                verify_parts(root, parts, [])


    def test_verify_parts_with_invalid_missing_reference_rejects_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            part = {"ordinal": 1, "filename": "delivery.zip", "bytes": 1, "sha256": "0" * 64}
            with self.assertRaisesRegex(ValueError, "reference declared parts"):
                verify_parts(Path(directory), [part], [2])

    def test_verify_parts_with_absent_part_without_hash_rejects_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            part = {"ordinal": 1, "filename": "delivery.zip", "bytes": 1}
            with self.assertRaisesRegex(ValueError, "Invalid fingerprint"):
                verify_parts(Path(directory), [part], [1])


    def test_verify_parts_with_noninteger_ordinal_rejects_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            for ordinal in (True, 1.0):
                with self.subTest(ordinal=ordinal):
                    part = {"ordinal": ordinal, "filename": "delivery.zip", "bytes": 1, "sha256": "0" * 64}
                    with self.assertRaisesRegex(ValueError, "ordinals"):
                        verify_parts(Path(directory), [part], [1])
