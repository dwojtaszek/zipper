#!/usr/bin/env python3
"""Unit tests for tests/archive-tests/verify-aes-fixtures.py."""

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

TESTS_DIR = Path(__file__).resolve().parent
if str(TESTS_DIR) not in sys.path:
    sys.path.insert(0, str(TESTS_DIR))

SPEC = importlib.util.spec_from_file_location("verify_aes_fixtures", TESTS_DIR / "verify-aes-fixtures.py")
VERIFY_AES = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(VERIFY_AES)


class VerifyAesFixturesTests(unittest.TestCase):
    def test_verify_zip_aes_headers_rejects_missing_aes_extra_field(self):
        # Minimal EOCD-less or plain ZIP without 0x9901 extra field fails cleanly.
        import zipfile
        with tempfile.TemporaryDirectory() as td:
            zpath = Path(td) / "plain.zip"
            with zipfile.ZipFile(zpath, "w") as zf:
                zf.writestr("empty.txt", b"")
            archive_bytes = zpath.read_bytes()
            with self.assertRaises(ValueError):
                VERIFY_AES.verify_zip_aes_headers(
                    archive_bytes,
                    [{"name": "empty.txt", "innerCompressionMethod": 0}],
                    "plain",
                )

    def test_verify_directory_preserves_exact_whitespace_password_without_stripping(self):
        captured_passwords = []

        def fake_listing(_seven, _archive, _entries):
            return None

        def fake_extract(_seven, _archive, name, expected_size, password):
            captured_passwords.append(("extract", name, password))
            return expected_size, "0" * 64

        def fake_wrong(_seven, _archive, bad_password):
            captured_passwords.append(("wrong", bad_password))

        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            zbytes = b"not-used-directly"
            import hashlib
            manifest = {
                "schemaVersion": 1,
                "caseKey": "aes256-space-padded",
                "container": "zip",
                "producer": "7-Zip",
                "producerVersion": "26.06 (x64)",
                "encryptionStrength": "AES-256",
                "aesVendorVersion": 2,
                "syntheticPasswordCase": "leading-trailing-space",
                "archive": {
                    "fileName": "aes256-space-padded.zip",
                    "physicalSize": len(zbytes),
                    "sha256": hashlib.sha256(zbytes).hexdigest(),
                },
                "entries": [
                    {
                        "ordinal": 0,
                        "name": "empty.txt",
                        "contentSize": 0,
                        "contentSha256": "0" * 64,
                        "localHeaderMethod": 99,
                        "centralDirectoryMethod": 99,
                        "innerCompressionMethod": 0,
                        "innerCompressionCodec": "stored",
                        "encryptionStrength": "AES-256",
                    }
                ],
            }
            (root / "aes256-space-padded.zip").write_bytes(zbytes)
            (root / "aes256-space-padded.json").write_text(json.dumps(manifest), encoding="utf-8")

            with mock.patch.object(VERIFY_AES, "verify_zip_aes_headers"), \
                 mock.patch.object(VERIFY_AES, "check_python_zipfile_capability", return_value={"reader": "python-zipfile", "status": "unsupported-capability", "missingCapability": "aes-256"}), \
                 mock.patch.object(VERIFY_AES, "check_infozip_capability", return_value={"reader": "infozip-unzip", "status": "skipped", "missingCapability": "aes-256"}), \
                 mock.patch.object(VERIFY_AES, "_assert_seven_zip_listing", side_effect=fake_listing), \
                 mock.patch.object(VERIFY_AES, "_extract_entry_sha256_seven_zip", side_effect=fake_extract), \
                 mock.patch.object(VERIFY_AES, "_assert_seven_zip_wrong_password", side_effect=fake_wrong):
                report = VERIFY_AES.verify_directory(
                    root,
                    {"aes256-space-padded": "  zipper aes pass  "},
                    seven_zip="7z",
                    require_seven_zip=True,
                )

            self.assertEqual(report["fixtureCount"], 1)
            self.assertIn(("extract", "empty.txt", "  zipper aes pass  "), captured_passwords)
            self.assertIn(("wrong", "zipper aes pass"), captured_passwords)

    def test_read_bounded_rejects_over_budget_file(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "archive.zip"
            path.write_bytes(b"1234")
            self.assertEqual(VERIFY_AES._read_bounded(path, 4), b"1234")
            with self.assertRaisesRegex(ValueError, "3-byte budget"):
                VERIFY_AES._read_bounded(path, 3)

    def test_verify_directory_rejects_missing_fixture_manifest(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / "present.json").write_text("{}", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Missing fixture manifests: absent"):
                VERIFY_AES.verify_directory(root, {"present": "x", "absent": "y"})

    def test_verify_directory_require_7zip_fails_when_missing(self):
        with tempfile.TemporaryDirectory() as td:
            with self.assertRaisesRegex(ValueError, "7-Zip"):
                VERIFY_AES.verify_directory(td, {}, seven_zip=None, require_seven_zip=True)


if __name__ == "__main__":
    unittest.main()
