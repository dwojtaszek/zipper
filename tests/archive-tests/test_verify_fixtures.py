#!/usr/bin/env python3
"""Self-test for tests/archive-tests/verify-fixtures.py (ticket #845).

Run directly:  python3 tests/archive-tests/test_verify_fixtures.py

Every tamper scenario asserts the verifier fails for the right reason, per the
ticket's test plan: an Archive byte, a JSON hash, an ID, a basename, an
ordinal, a mutation offset, or an allowed outcome must each flip the verdict.
Positive controls use the committed valid-empty test vector (renamed to its
Fixture ID, as the published contract requires) and small synthetic pairs
built here with the standard library only — no C# generator code involved.
"""

import importlib.util
import io
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.join(HERE, "..", "..")
SCHEMA = os.path.join(REPO, "tests", "fixtures", "archive-test-case.schema.json")
VECTOR_DIR = os.path.join(REPO, "tests", "fixtures", "archive-tests")

_spec = importlib.util.spec_from_file_location(
    "verify_fixtures", os.path.join(HERE, "verify-fixtures.py"))
vf = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(vf)

DEFAULT_AJV = vf.DEFAULT_AJV
# Host with no platform marks: only unmarked expectations apply.
UNMARKED_PLATFORMS = set()


def sha256_hex(data):
    import hashlib
    return hashlib.sha256(data).hexdigest()


def make_zip(entries, stored=True):
    """Builds a small Archive with fixed timestamps; entries: [(name, bytes)]."""
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_STORED if stored else zipfile.ZIP_DEFLATED) as zf:
        for name, data in entries:
            info = zipfile.ZipInfo(name, date_time=(2024, 1, 1, 0, 0, 0))
            zf.writestr(info, data)
    return buf.getvalue()

def make_eocd_ambiguity_zip():
    """Two local entries plus disjoint authentic/shadow central tails (#870)."""
    import binascii

    def local(name, data):
        raw_name = name.encode()
        crc = binascii.crc32(data) & 0xFFFFFFFF
        return struct.pack("<IHHHHHIIIHH", 0x04034B50, 20, 0, 0, 0, 0,
                           crc, len(data), len(data), len(raw_name), 0) + raw_name + data

    def central(name, data, local_offset):
        raw_name = name.encode()
        crc = binascii.crc32(data) & 0xFFFFFFFF
        return struct.pack("<IHHHHHHIIIHHHHHII", 0x02014B50, 20, 20, 0, 0, 0, 0,
                           crc, len(data), len(data), len(raw_name), 0, 0, 0, 0, 0,
                           local_offset) + raw_name

    def eocd(cd_offset, cd_size, comment_length):
        return struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, 1, 1, cd_size,
                           cd_offset, comment_length)

    a_data, b_data = b"A", b"B"
    a_local = local("a.txt", a_data)
    b_local = local("b.bin", b_data)
    authentic_cd = central("a.txt", a_data, 0)
    authentic_cd_offset = len(a_local) + len(b_local)
    authentic_eocd_offset = authentic_cd_offset + len(authentic_cd)
    shadow_cd = central("b.bin", b_data, len(a_local))
    shadow_cd_offset = authentic_eocd_offset + 22
    shadow_eocd_offset = shadow_cd_offset + len(shadow_cd)
    comment_length = len(shadow_cd) + 22
    archive = (a_local + b_local + authentic_cd
               + eocd(authentic_cd_offset, len(authentic_cd), comment_length)
               + shadow_cd + eocd(shadow_cd_offset, len(shadow_cd), 0))
    offsets = {
        "authentic-eocd-offset": authentic_eocd_offset,
        "authentic-comment-length": comment_length,
        "shadow-central-directory-offset": shadow_cd_offset,
        "shadow-eocd-offset": shadow_eocd_offset,
    }
    return archive, offsets, a_data, b_data


def entry_record(ordinal, name, content, local_method=0, central_method=0, payload_codec="stored"):
    return {
        "ordinal": ordinal,
        "kind": "file",
        "localNameRaw": name.encode("utf-8").hex(),
        "centralNameRaw": name.encode("utf-8").hex(),
        "readableName": name,
        "localHeaderMethod": local_method,
        "centralDirectoryMethod": central_method,
        "payloadCodec": payload_codec,
        "contentSha256": sha256_hex(content),
        "contentSize": len(content),
    }


def case_for(case_key, classification, zip_bytes, entries, mutations, expectations):
    archive_sha = sha256_hex(zip_bytes)
    fixture_id = vf.compute_fixture_id("1", case_key, 1, 1, 42, archive_sha)
    return {
        "schemaVersion": 1,
        "generatorContractVersion": "1",
        "generatorVersion": "0.0.0",
        "fixtureId": fixture_id,
        "caseKey": case_key,
        "caseRevision": 1,
        "expectationRevision": 1,
        "seed": 42,
        "classification": classification,
        "archive": {
            "fileName": fixture_id + ".zip",
            "physicalSize": len(zip_bytes),
            "sha256": archive_sha,
        },
        "entries": entries,
        "mutations": mutations,
        "expectations": expectations,
        "limits": {
            "entryCount": len(entries),
            "expandedBytesBudget": sum(e.get("contentSize", 0) for e in entries) or 1,
            "jsonBytesBudget": 1048576,
            "deadlineSeconds": 10,
        },
    }


VALID_EXPECTATIONS = [
    {"operation": "list", "profile": "strict",
     "allowedOutcomes": ["listed-count-matches-entries"],
     "invariants": ["no-partial-writes"]},
    {"operation": "read-entry", "profile": "strict",
     "allowedOutcomes": ["read-entry-content-matches"],
     "invariants": ["no-partial-writes"]},
    {"operation": "integrity-check", "profile": "strict",
     "allowedOutcomes": ["integrity-passes"],
     "invariants": ["no-partial-writes"]},
    {"operation": "extract", "profile": "strict",
     "allowedOutcomes": ["extract-completes"],
     "invariants": ["no-partial-writes"]},
]

CRC_EXPECTATIONS = [
    {"operation": "list", "profile": "strict",
     "allowedOutcomes": ["list-succeeds", "list-fails"],
     "invariants": ["payload-bytes-unchanged"],
     "failureStages": ["open", "list"]},
    {"operation": "read-entry", "profile": "strict",
     "allowedOutcomes": ["read-entry-content-matches", "read-entry-fails"],
     "invariants": ["payload-bytes-unchanged"],
     "failureStages": ["open", "read-entry"]},
    {"operation": "integrity-check", "profile": "strict-integrity-v1",
     "allowedOutcomes": ["crc-mismatch-rejected", "crc-mismatch-unchecked"],
     "invariants": ["payload-bytes-unchanged"], "capability": "crc32",
     "failureStages": ["open", "read-entry", "integrity-check"]},
    {"operation": "extract", "profile": "strict",
     "allowedOutcomes": ["extract-succeeds", "extract-fails"],
     "invariants": ["payload-bytes-unchanged"],
     "failureStages": ["open", "read-entry", "extract"]},
]


def write_pair(directory, case, zip_bytes):
    fixture_id = case["fixtureId"]
    with open(os.path.join(directory, fixture_id + ".json"), "w", encoding="utf-8") as handle:
        json.dump(case, handle, separators=(",", ":"))
        handle.write("\n")
    with open(os.path.join(directory, fixture_id + ".zip"), "wb") as handle:
        handle.write(zip_bytes)


def verify(directory, ajv=DEFAULT_AJV):
    return vf.verify_directory(directory, SCHEMA, ajv, UNMARKED_PLATFORMS)


class TempDirTest(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp(prefix="atc-selftest-")
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)

    def publish_valid(self):
        """One synthetic valid pair: 'a.txt' stored, honest metadata."""
        content = b"hello archive test"
        zip_bytes = make_zip([("a.txt", content)])
        case = case_for("valid-synthetic", "valid", zip_bytes,
                        [entry_record(0, "a.txt", content)], [], VALID_EXPECTATIONS)
        write_pair(self.dir, case, zip_bytes)
        return case, zip_bytes

    def publish_crc_corrupt(self):
        """A synthetic CRC-corrupt pair: both CRC fields patched, parallel
        mutation records with complete inline hex (reconstructable control)."""
        content = b"crc corrupt me"
        control = make_zip([("a.txt", content)])
        patched = bytearray(control)
        local_crc_offset = 14  # local header: sig(4) ver(2) flag(2) method(2)
        central = control.index(b"PK\x01\x02")
        central_crc_offset = central + 16  # sig(4) ver(2) vermade(2) flag(2) method(2)
        good_local = control[local_crc_offset:local_crc_offset + 4]
        good_central = control[central_crc_offset:central_crc_offset + 4]
        patched[local_crc_offset:local_crc_offset + 4] = b"\xde\xad\xbe\xef"
        patched[central_crc_offset:central_crc_offset + 4] = b"\xde\xad\xbe\xef"
        patched = bytes(patched)
        control_sha = sha256_hex(control)

        def mutation(code, offset, before, after):
            return {
                "code": code, "structure": "local-header" if offset == local_crc_offset
                else "central-header", "offsetBasis": "before-mutation",
                "offset": offset, "explanation": "synthetic CRC lie",
                "deletedLength": 4, "insertedLength": 4,
                "beforeSize": len(control), "afterSize": len(patched),
                "beforeSha256": control_sha, "afterSha256": sha256_hex(patched),
                "beforeHex": before.hex(), "afterHex": after.hex(),
            }

        mutations = [
            mutation("crc-local-mismatch", local_crc_offset, good_local, b"\xde\xad\xbe\xef"),
            mutation("crc-central-mismatch", central_crc_offset, good_central, b"\xde\xad\xbe\xef"),
        ]
        case = case_for("crc-synthetic-both", "malformed", patched,
                        [entry_record(0, "a.txt", content)], mutations, CRC_EXPECTATIONS)
        write_pair(self.dir, case, patched)
        return case, patched

    def publish_unsupported_method(self):
        """Mirrors the published unsupported-method fixture's method-98 mutation
        and operation expectations without sharing generator code."""
        content = b"unsupported method"
        control = make_zip([("a.txt", content)])
        patched = bytearray(control)
        local_method_offset = 8
        central_method_offset = control.index(b"PK\x01\x02") + 10
        for offset in (local_method_offset, central_method_offset):
            struct.pack_into("<H", patched, offset, 98)
        patched = bytes(patched)
        control_sha = sha256_hex(control)
        patched_sha = sha256_hex(patched)

        def mutation(structure, offset):
            return {
                "code": "unsupported-method", "structure": structure,
                "offsetBasis": "before-mutation", "offset": offset,
                "explanation": "synthetic unsupported method",
                "deletedLength": 2, "insertedLength": 2,
                "beforeSize": len(control), "afterSize": len(patched),
                "beforeSha256": control_sha, "afterSha256": patched_sha,
                "beforeHex": control[offset:offset + 2].hex(),
                "afterHex": patched[offset:offset + 2].hex(),
                "declaredValue": "method=98",
            }

        mutations = [
            mutation("local-header", local_method_offset),
            mutation("central-header", central_method_offset),
        ]
        expectations = [
            {"operation": "list", "profile": "strict",
             "allowedOutcomes": ["list-succeeds"], "invariants": ["payload-bytes-unchanged"],
             "failureStages": ["open", "list"]},
            {"operation": "read-entry", "profile": "strict",
             "allowedOutcomes": ["unsupported-method-rejected", "unsupported-method-unchecked"],
             "invariants": ["payload-bytes-unchanged"],
             "failureStages": ["open", "read-entry"]},
            {"operation": "integrity-check", "profile": "strict",
             "allowedOutcomes": ["unsupported-method-rejected", "integrity-unchecked"],
             "invariants": ["payload-bytes-unchanged"],
             "failureStages": ["open", "read-entry", "integrity-check"]},
            {"operation": "extract", "profile": "strict",
             "allowedOutcomes": ["extract-fails", "extract-succeeds"],
             "invariants": ["no-partial-writes"], "failureStages": ["open", "extract"]},
        ]
        case = case_for(
            "unsupported-method", "policy-sensitive", patched,
            [entry_record(0, "a.txt", content, local_method=98,
                          central_method=98)], mutations, expectations)
        write_pair(self.dir, case, patched)
        return case, patched

    def publish_duplicate_names(self):
        """Two same-name entries with distinct payloads (policy-sensitive)."""
        first, second = b"first payload", b"second payload"
        zip_bytes = make_zip([("dup.txt", first), ("dup.txt", second)])
        expectations = [
            {"operation": "list", "profile": "strict",
             "allowedOutcomes": ["listed-count-matches-entries"],
             "invariants": ["no-partial-writes"]},
            {"operation": "read-entry", "profile": "strict",
             "allowedOutcomes": ["read-entry-content-matches"],
             "invariants": ["no-partial-writes"]},
            {"operation": "integrity-check", "profile": "strict",
             "allowedOutcomes": ["integrity-passes"],
             "invariants": ["no-partial-writes"]},
            {"operation": "extract", "profile": "strict",
             "allowedOutcomes": ["entry-rejected", "entry-renamed-by-policy", "extract-fails"],
             "invariants": ["no-silent-overwrite", "distinct-content-hashes-observable"],
             "failureStages": ["extract"]},
        ]
        case = case_for("duplicate-name", "policy-sensitive", zip_bytes,
                        [entry_record(0, "dup.txt", first),
                         entry_record(1, "dup.txt", second)], [], expectations)
        write_pair(self.dir, case, zip_bytes)
        return case, zip_bytes

    def rewrite_case(self, case):
        path = os.path.join(self.dir, case["fixtureId"] + ".json")
        with open(path, "r", encoding="utf-8") as handle:
            return json.load(handle), path

    def persist(self, path, case):
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(case, handle, separators=(",", ":"))
            handle.write("\n")

    def replace_expectation(self, case, operation, expectation):
        case["expectations"] = [
            existing for existing in case["expectations"]
            if existing["operation"] != operation
        ]
        case["expectations"].append(expectation)


class EocdAmbiguityTests(TempDirTest):
    @staticmethod
    def make_case():
        zip_bytes, offsets, a_data, b_data = make_eocd_ambiguity_zip()
        declared = " ".join("%s=%d" % item for item in offsets.items())
        mutation = {
            "code": "eocdr-ambiguity-comment",
            "structure": "whole-archive",
            "offsetBasis": "before-mutation",
            "offset": 0,
            "explanation": "synthetic two-EOCD differential",
            "declaredValue": declared,
        }
        case = case_for(
            "eocdr-ambiguity-comment",
            "policy-sensitive",
            zip_bytes,
            [entry_record(0, "a.txt", a_data), entry_record(1, "b.bin", b_data)],
            [mutation],
            CRC_EXPECTATIONS)
        return case, zip_bytes, offsets

    def test_two_plausible_eocds_select_shadow_entry(self):
        case, zip_bytes, offsets = self.make_case()

        detail = vf.verify_eocd_ambiguity(case, zip_bytes)

        self.assertIn("python-selects=b.bin", detail)
        self.assertIn("authentic-eocd=%d" % offsets["authentic-eocd-offset"], detail)
        self.assertIn("shadow-eocd=%d" % offsets["shadow-eocd-offset"], detail)

    def test_shadow_eocd_with_wrong_central_offset_is_rejected(self):
        case, zip_bytes, offsets = self.make_case()
        tampered = bytearray(zip_bytes)
        struct.pack_into("<I", tampered, offsets["shadow-eocd-offset"] + 16, 0)

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_eocd_ambiguity(case, bytes(tampered))

        self.assertEqual("eocd-ambiguity", caught.exception.check)

    def test_authentic_eocd_with_wrong_comment_length_is_rejected(self):
        case, zip_bytes, offsets = self.make_case()
        tampered = bytearray(zip_bytes)
        struct.pack_into("<H", tampered, offsets["authentic-eocd-offset"] + 20, 0)

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_eocd_ambiguity(case, bytes(tampered))

        self.assertEqual("eocd-ambiguity", caught.exception.check)

    def test_wrong_declared_shadow_eocd_offset_is_rejected(self):
        case, zip_bytes, offsets = self.make_case()
        declared = " ".join(
            "%s=%d" % item
            for item in offsets.items()
            if item[0] != "shadow-eocd-offset")
        declared += " shadow-eocd-offset=%d" % (offsets["shadow-eocd-offset"] + 1)
        case["mutations"][0]["declaredValue"] = declared

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_eocd_ambiguity(case, zip_bytes)

        self.assertEqual("eocd-ambiguity", caught.exception.check)

    def test_local_central_method_disagreement_is_rejected(self):
        case, zip_bytes, offsets = self.make_case()
        tampered = bytearray(zip_bytes)
        struct.pack_into("<H", tampered, 8, 8)

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_eocd_ambiguity(case, bytes(tampered))

        self.assertEqual("eocd-ambiguity", caught.exception.check)

    def test_authentic_payload_hash_mismatch_is_rejected(self):
        case, zip_bytes, offsets = self.make_case()
        tampered = bytearray(zip_bytes)
        tampered[35] ^= 0xFF

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_eocd_ambiguity(case, bytes(tampered))

        self.assertEqual("eocd-ambiguity", caught.exception.check)

    def test_third_eocd_signature_is_rejected(self):
        case, zip_bytes, offsets = self.make_case()
        injected = offsets["shadow-eocd-offset"]
        tampered = zip_bytes[:injected] + b"PK\x05\x06" + zip_bytes[injected:]

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_eocd_ambiguity(case, tampered)

        self.assertEqual("eocd-ambiguity", caught.exception.check)

    def test_shadow_payload_over_budget_is_rejected(self):
        case, zip_bytes, offsets = self.make_case()
        case.setdefault("limits", {})["expandedBytesBudget"] = 0

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_eocd_ambiguity(case, zip_bytes)

        self.assertEqual("eocd-ambiguity", caught.exception.check)


class AdlsSegmentTests(unittest.TestCase):
    @staticmethod
    def make_case(case_key, segments):
        name = "/".join("d%02d" % index for index in range(1, segments)) + "/target.txt"
        case = {
            "caseKey": case_key,
            "entries": [entry_record(0, name, b"x")],
        }
        return case, name

    def test_boundary_carries_exactly_63_segments(self):
        case, name = self.make_case("path-adls-segments-boundary", 63)

        detail = vf.verify_adls_segments(case)

        self.assertIn("segments=63", detail)
        self.assertIn("account-relative=65", detail)

    def test_exceeded_carries_exactly_64_segments(self):
        case, name = self.make_case("path-adls-segments-exceeded", 64)

        detail = vf.verify_adls_segments(case)

        self.assertIn("segments=64", detail)
        self.assertIn("account-relative=66", detail)

    def test_wrong_segment_count_is_rejected(self):
        case, name = self.make_case("path-adls-segments-boundary", 62)

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_adls_segments(case)

        self.assertEqual("adls-segments", caught.exception.check)

    def test_relative_marker_segment_is_rejected(self):
        case, name = self.make_case("path-adls-segments-boundary", 63)
        case["entries"][0]["readableName"] = name.replace("d01", "..", 1)

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_adls_segments(case)

        self.assertEqual("adls-segments", caught.exception.check)


class EncodingTrailByteTests(unittest.TestCase):
    @staticmethod
    def make_zip(case_key):
        """A minimal single-entry stored archive with 'ab.txt' patched to the
        case's legacy-codec trail-byte name in both headers."""
        import io
        import struct
        import zipfile

        _, expected_hex, _ = vf.ENCODING_TRAIL_BYTE_CASES[case_key]
        lead = int(expected_hex[:2], 16)
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr(zipfile.ZipInfo("ab.txt"), b"x")
        zip_bytes = bytearray(buf.getvalue())
        cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]
        zip_bytes[30] = lead
        zip_bytes[31] = 0x5C
        zip_bytes[cd_offset + 46] = lead
        zip_bytes[cd_offset + 47] = 0x5C
        return bytes(zip_bytes)

    @staticmethod
    def make_case(case_key, zip_bytes=None):
        _, expected_hex, expected_name = vf.ENCODING_TRAIL_BYTE_CASES[case_key]
        return {
            "caseKey": case_key,
            "entries": [{
                "ordinal": 0,
                "kind": "file",
                "localNameRaw": expected_hex,
                "centralNameRaw": expected_hex,
                "readableName": expected_name,
            }],
        }

    def test_named_consumer_decodes_each_codec_without_split(self):
        for case_key in vf.ENCODING_TRAIL_BYTE_CASES:
            with self.subTest(caseKey=case_key):
                codec = vf.ENCODING_TRAIL_BYTE_CASES[case_key][0]
                detail = vf.verify_encoding_trail_bytes(self.make_case(case_key), self.make_zip(case_key))
                self.assertIn(codec, detail)
                self.assertIn("no-spurious-split", detail)

    def test_decode_failure_fails_closed(self):
        case = self.make_case("encoding-cp932-trail-backslash")
        zip_bytes = self.make_zip("encoding-cp932-trail-backslash")
        overrides = {
            "encoding-cp932-trail-backslash": ("cp932", "ef5c2e747874", "表.txt"),
            "encoding-big5-trail-backslash": ("not-a-codec", "b35c2e747874", "許.txt"),
        }
        original = dict(vf.ENCODING_TRAIL_BYTE_CASES)
        try:
            vf.ENCODING_TRAIL_BYTE_CASES.update(overrides)
            # Invalid CP932 lead byte: UnicodeDecodeError arm.
            case["entries"][0]["localNameRaw"] = overrides["encoding-cp932-trail-backslash"][1]
            case["entries"][0]["centralNameRaw"] = overrides["encoding-cp932-trail-backslash"][1]
            with self.assertRaises(vf.VerificationError) as caught:
                vf.verify_encoding_trail_bytes(case, self.make_zip("encoding-cp932-trail-backslash"))
            self.assertIn("failed to decode", str(caught.exception))

            # Unknown codec name: LookupError arm.
            case = self.make_case("encoding-big5-trail-backslash")
            with self.assertRaises(vf.VerificationError) as caught:
                vf.verify_encoding_trail_bytes(case, self.make_zip("encoding-big5-trail-backslash"))
            self.assertIn("failed to decode", str(caught.exception))
        finally:
            vf.ENCODING_TRAIL_BYTE_CASES.clear()
            vf.ENCODING_TRAIL_BYTE_CASES.update(original)

    def test_local_raw_name_mismatch_is_rejected(self):
        case_key = "encoding-cp932-trail-backslash"
        case = self.make_case(case_key)
        zip_bytes = bytearray(self.make_zip(case_key))
        zip_bytes[31] = 0x2E

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_encoding_trail_bytes(case, bytes(zip_bytes))

        self.assertEqual("encoding-trail-byte", caught.exception.check)

    def test_unknown_case_key_is_rejected(self):
        case = self.make_case("encoding-cp932-trail-backslash")
        case["caseKey"] = "encoding-some-other-codec"

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_encoding_trail_bytes(case, self.make_zip("encoding-cp932-trail-backslash"))

        self.assertEqual("encoding-trail-byte", caught.exception.check)

    def test_multi_entry_case_is_rejected(self):
        case = self.make_case("encoding-cp932-trail-backslash")
        case["entries"].append(dict(case["entries"][0], ordinal=1))

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_encoding_trail_bytes(case, self.make_zip("encoding-cp932-trail-backslash"))

        self.assertEqual("encoding-trail-byte", caught.exception.check)

    def test_sidecar_raw_hex_mismatch_is_rejected(self):
        case = self.make_case("encoding-cp932-trail-backslash")
        case["entries"][0]["localNameRaw"] = "ab2e747874"

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_encoding_trail_bytes(case, self.make_zip("encoding-cp932-trail-backslash"))

        self.assertEqual("encoding-trail-byte", caught.exception.check)

    def test_central_raw_name_mismatch_is_rejected(self):
        case_key = "encoding-big5-trail-backslash"
        case = self.make_case(case_key)
        zip_bytes = bytearray(self.make_zip(case_key))
        import struct
        cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]
        zip_bytes[cd_offset + 47] = 0x2E

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_encoding_trail_bytes(case, bytes(zip_bytes))

        self.assertEqual("encoding-trail-byte", caught.exception.check)

    def test_bit11_set_is_rejected(self):
        case_key = "encoding-gbk-trail-backslash"
        case = self.make_case(case_key)
        zip_bytes = bytearray(self.make_zip(case_key))
        import struct
        cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]
        struct.pack_into("<H", zip_bytes, cd_offset + 8, 0x0800)

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_encoding_trail_bytes(case, bytes(zip_bytes))

        self.assertEqual("encoding-trail-byte", caught.exception.check)

    def test_readable_name_mismatch_is_rejected(self):
        case = self.make_case("encoding-cp932-trail-backslash")
        case["entries"][0]["readableName"] = "ab.txt"

        with self.assertRaises(vf.VerificationError) as caught:
            vf.verify_encoding_trail_bytes(case, self.make_zip("encoding-cp932-trail-backslash"))

        self.assertEqual("encoding-trail-byte", caught.exception.check)


class PositiveControlTests(TempDirTest):
    def test_committed_valid_empty_vector_passes(self):
        with open(os.path.join(VECTOR_DIR, "valid-empty.json"), "r", encoding="utf-8") as handle:
            case = json.load(handle)
        fixture_id = case["fixtureId"]
        shutil.copy(os.path.join(VECTOR_DIR, "valid-empty.json"),
                    os.path.join(self.dir, fixture_id + ".json"))
        shutil.copy(os.path.join(VECTOR_DIR, "valid-empty.zip"),
                    os.path.join(self.dir, fixture_id + ".zip"))

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code, report["directoryProblems"])
        self.assertEqual("pass", report["fixtures"][0]["status"])

    def test_synthetic_valid_pair_passes_all_operations(self):
        self.publish_valid()

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("list-succeeds", operations["list"]["normalizedOutcome"])
        self.assertEqual("read-entry-content-matches",
                         operations["read-entry"]["normalizedOutcome"])
        self.assertEqual("integrity-passes", operations["integrity-check"]["normalizedOutcome"])
        self.assertEqual("extract-succeeds", operations["extract"]["normalizedOutcome"])

    def test_resource_counts_are_bounded_by_the_report(self):
        case, _ = self.publish_valid()

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        summary = report["summary"]
        self.assertEqual(1, summary["subprocessCount"])
        self.assertLessEqual(summary["bytesRead"],
                             case["limits"]["expandedBytesBudget"] * 4)


class TamperTests(TempDirTest):
    def test_tampered_archive_byte_fails_for_identity_reason(self):
        case, zip_bytes = self.publish_valid()
        zip_bytes = bytearray(zip_bytes)
        zip_bytes[-1] ^= 0xFF  # flip a byte inside the EOCD comment area
        with open(os.path.join(self.dir, case["fixtureId"] + ".zip"), "wb") as handle:
            handle.write(zip_bytes)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("archive-size-sha", failure["check"])

    def test_tampered_json_hash_fails(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["archive"]["sha256"] = "0" * 64
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        self.assertEqual("archive-size-sha", report["fixtures"][0]["failure"]["check"])

    def test_tampered_fixture_id_fails(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["fixtureId"] = "atc-" + "0" * 64
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        self.assertEqual("fixture-id", report["fixtures"][0]["failure"]["check"])

    def test_renamed_basename_fails(self):
        case, _ = self.publish_valid()
        os.rename(os.path.join(self.dir, case["fixtureId"] + ".zip"),
                  os.path.join(self.dir, "atc-" + "1" * 64 + ".zip"))

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        self.assertTrue(any("has no Expectation File" in problem
                            for problem in report["directoryProblems"]))

    def test_duplicate_entry_ordinal_fails(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["entries"].append(dict(case["entries"][0]))
        case["limits"]["entryCount"] = 2
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        self.assertEqual("entries-ordinals", report["fixtures"][0]["failure"]["check"])

    def test_tampered_mutation_offset_fails(self):
        case, patched = self.publish_crc_corrupt()
        case, path = self.rewrite_case(case)
        case["mutations"][0]["offset"] = len(patched) + 100  # outside the archive
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        self.assertEqual("mutations", report["fixtures"][0]["failure"]["check"])
        self.assertIn("outside the before-mutation archive",
                      report["fixtures"][0]["failure"]["message"])

    def test_tampered_allowed_outcome_fails_for_oracle_reason(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][0]["allowedOutcomes"] = ["list-fails"]  # reader lists fine
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("fail", operations["list"]["status"])
        self.assertIn("outside the allowed outcomes", operations["list"]["reason"])

    def test_entry_count_lie_fails_the_list_operation(self):
        """A self-consistent entries[] lie (distinct ordinals, matching
        limits.entryCount) must still fail: the reader's own entry count is
        compared against the declared count, not just taken on faith."""
        case, zip_bytes = self.publish_valid()
        case, path = self.rewrite_case(case)
        # Drop one entry record and adjust the declared count to stay consistent.
        case["entries"] = case["entries"][:-1] if case["entries"] else []
        case["limits"]["entryCount"] = len(case["entries"])
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("list-fails", operations["list"]["normalizedOutcome"])
        self.assertEqual("fail", operations["list"]["status"])

    def test_valid_declared_unsafe_name_never_extracts(self):
        """Classification is never trusted for extraction: a 'valid' fixture
        whose Archive carries a parent-traversal name records extract as
        not-run, which cannot satisfy the valid extract expectation."""
        content = b"escape payload"
        zip_bytes = make_zip([("../escape.txt", content)])
        case = case_for("valid-lie", "valid", zip_bytes,
                        [entry_record(0, "../escape.txt", content)], [],
                        VALID_EXPECTATIONS)
        write_pair(self.dir, case, zip_bytes)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("not-run", operations["extract"]["normalizedOutcome"])
        self.assertEqual("fail", operations["extract"]["status"])

    def test_missing_prerequisite_fails_without_silent_skip(self):
        self.publish_valid()

        report, exit_code = verify(self.dir, ajv="definitely-not-a-command --x")

        self.assertEqual(1, exit_code)
        self.assertTrue(any("missing prerequisite" in problem
                            for problem in report["directoryProblems"]))

    def test_missing_restore_fails_with_restore_guidance(self):
        """Ticket #932: an unrestored pinned CLI fails closed with the exact
        restore step, without attempting any download (nothing is executed)."""
        ajv = vf.AjvValidator(SCHEMA, "node /nonexistent/ajv-cli/dist/index.js")

        problem = ajv.check_prerequisite()

        self.assertIn("npm ci", problem)
        self.assertIn("never downloads", problem)

    def test_archive_over_16mib_fails_before_reading_file_contents(self):
        case, _ = self.publish_valid()
        zip_path = os.path.join(self.dir, case["fixtureId"] + ".zip")
        with open(zip_path, "wb") as handle:
            handle.truncate(16 * 1024 * 1024 + 1)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("archive-size-sha", fixture["failure"]["check"])
        self.assertIn("over the 16 MiB physical budget", fixture["failure"]["message"])
        self.assertEqual(0, fixture.get("bytesRead", 0))

    def test_forged_physical_size_cannot_increase_hard_limit(self):
        case, _ = self.publish_valid()
        over_limit = 16 * 1024 * 1024 + 1
        zip_path = os.path.join(self.dir, case["fixtureId"] + ".zip")
        with open(zip_path, "wb") as handle:
            handle.truncate(over_limit)
        case, path = self.rewrite_case(case)
        case["archive"]["physicalSize"] = over_limit
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("archive-size-sha", fixture["failure"]["check"])
        self.assertIn("over the 16 MiB physical budget", fixture["failure"]["message"])
        self.assertEqual(0, fixture.get("bytesRead", 0))

    def test_archive_exact_16mib_pre_check_allowed(self):
        case, _ = self.publish_valid()
        zip_path = os.path.join(self.dir, case["fixtureId"] + ".zip")
        with open(zip_path, "wb") as handle:
            handle.truncate(16 * 1024 * 1024)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertNotIn("over the 16 MiB physical budget",
                         str(fixture.get("failure", {}).get("message", "")))

    def test_expectation_file_over_1mib_fails_on_disk_size(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(" " * (1024 * 1024))

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("json-bytes-budget", fixture["failure"]["check"])
        self.assertIn("over the 1 MiB budget", fixture["failure"]["message"])

    def test_tampered_json_bytes_budget_over_1mib_fails(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["limits"]["jsonBytesBudget"] = 1024 * 1024 + 1
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("json-bytes-budget", fixture["failure"]["check"])
        self.assertIn("exceeds the 1 MiB budget", fixture["failure"]["message"])

    def test_tampered_expanded_bytes_budget_over_32mib_fails(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["limits"]["expandedBytesBudget"] = 32 * 1024 * 1024 + 1
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("expanded-bytes-budget", fixture["failure"]["check"])
        self.assertIn("exceeds the 32 MiB budget", fixture["failure"]["message"])

    def test_tampered_entry_count_over_1000_fails(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["limits"]["entryCount"] = 1001
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("entries-ordinals", fixture["failure"]["check"])
        self.assertIn("exceeds the 1000 entry budget", fixture["failure"]["message"])

    def test_tampered_deadline_seconds_over_10_fails(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["limits"]["deadlineSeconds"] = 11
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("deadline", fixture["failure"]["check"])
        self.assertIn("exceeds the 10-second budget", fixture["failure"]["message"])

    def test_total_folder_over_256mib_fails_as_directory_problem(self):
        case, _ = self.publish_valid()
        zip_path = os.path.join(self.dir, case["fixtureId"] + ".zip")
        with open(zip_path, "wb") as handle:
            handle.truncate(256 * 1024 * 1024 + 1)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        self.assertTrue(any("over the 256 MiB budget" in problem
                            for problem in report["directoryProblems"]))

    def test_multiple_archives_under_16mib_over_256mib_folder_fails(self):
        # 18 pairs each 15 MiB (< 16 MiB archive limit), totaling 270 MiB (> 256 MiB folder budget)
        for i in range(18):
            fid = "atc-" + f"{i:02x}" * 32
            zip_path = os.path.join(self.dir, fid + ".zip")
            json_path = os.path.join(self.dir, fid + ".json")
            with open(zip_path, "wb") as h:
                h.truncate(15 * 1024 * 1024)
            with open(json_path, "w", encoding="utf-8") as h:
                h.write("{}")

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        self.assertTrue(any("over the 256 MiB budget" in problem
                            for problem in report["directoryProblems"]))
        self.assertEqual(0, len(report["fixtures"]))

    def test_metadata_over_limit_physical_size_rejects_without_reading_zip(self):
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["archive"]["physicalSize"] = 16 * 1024 * 1024 + 1
        self.persist(path, case)
        zip_path = os.path.join(self.dir, case["fixtureId"] + ".zip")
        os.chmod(zip_path, 0o000)
        try:
            report, exit_code = verify(self.dir)
            self.assertEqual(1, exit_code)
            self.assertEqual("archive-size-sha", report["fixtures"][0]["failure"]["check"])
            self.assertIn("exceeds the 16 MiB physical budget", report["fixtures"][0]["failure"]["message"])
        finally:
            os.chmod(zip_path, 0o644)

    def test_tampered_local_header_method_fails(self):
        case, zip_bytes = self.publish_valid()
        case_data, path = self.rewrite_case(case)
        case_data["entries"][0]["localHeaderMethod"] = 8
        self.persist(path, case_data)

        report, exit_code = verify(self.dir)
        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("fail", fixture["status"])
        self.assertEqual("compression-methods", fixture["failure"]["check"])
        self.assertIn("localHeaderMethod 8 does not match wire 0", fixture["failure"]["message"])

    def test_tampered_central_directory_method_fails(self):
        case, zip_bytes = self.publish_valid()
        case_data, path = self.rewrite_case(case)
        case_data["entries"][0]["centralDirectoryMethod"] = 8
        self.persist(path, case_data)

        report, exit_code = verify(self.dir)
        self.assertEqual(1, exit_code)
        fixture = report["fixtures"][0]
        self.assertEqual("fail", fixture["status"])
        self.assertEqual("compression-methods", fixture["failure"]["check"])
        self.assertIn("centralDirectoryMethod 8 does not match wire 0", fixture["failure"]["message"])


class CrcCorruptTests(TempDirTest):
    def test_crc_corrupt_pair_passes_with_rejected_integrity(self):
        self.publish_crc_corrupt()

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        # Listing succeeded, but the integrity outcome must reflect the CRC lie.
        self.assertEqual("list-succeeds", operations["list"]["normalizedOutcome"])
        self.assertEqual("crc-mismatch-rejected",
                         operations["integrity-check"]["normalizedOutcome"])

    def test_crc_corrupt_cannot_pass_strict_integrity_because_listing_succeeded(self):
        case, _ = self.publish_crc_corrupt()
        case, path = self.rewrite_case(case)
        for expectation in case["expectations"]:
            if expectation["operation"] == "integrity-check":
                # A lying oracle: claim the only passing outcome is a clean pass.
                expectation["allowedOutcomes"] = ["integrity-passes"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("fail", operations["integrity-check"]["status"])
        self.assertEqual("crc-mismatch-rejected",
                         operations["integrity-check"]["normalizedOutcome"])

    def test_crc_control_is_reconstructed_from_inline_hex(self):
        self.publish_crc_corrupt()

        report, _ = verify(self.dir)

        mutations_check = [c for c in report["fixtures"][0]["structureChecks"]
                           if c["check"] == "mutations"][0]
        self.assertIn("control-reconstructed", mutations_check["detail"])


class DuplicateNameTests(TempDirTest):
    def test_duplicate_names_with_distinct_payloads_are_both_read_and_checked(self):
        case, _ = self.publish_duplicate_names()

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("read-entry-content-matches",
                         operations["read-entry"]["normalizedOutcome"])
        self.assertEqual(2, operations["read-entry"]["details"]["hashMatches"])

    def test_tampered_duplicate_payload_hash_fails(self):
        case, _ = self.publish_duplicate_names()
        case, path = self.rewrite_case(case)
        case["entries"][1]["contentSha256"] = "0" * 64  # lie about the second payload
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("fail", operations["read-entry"]["status"])


class PolicyExtractionTests(TempDirTest):
    def test_policy_fixture_never_extracts_and_records_not_run(self):
        self.publish_duplicate_names()

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("not-run", operations["extract"]["normalizedOutcome"])
        self.assertEqual("not-run", operations["extract"]["status"])


class SubprocessDeadlineTests(unittest.TestCase):
    def test_inert_sleeper_timeout_kills_only_the_child(self):
        argv = [sys.executable, "-c", "import time; time.sleep(30)"]

        code, _out, err, timed_out = vf.run_checked(argv, timeout=1)

        self.assertTrue(timed_out)
        self.assertIsNone(code)
        self.assertEqual("timeout", err)
        # Parent survived; a quick child proves the harness still runs subprocesses.
        code2, out2, _err2, timed_out2 = vf.run_checked(
            [sys.executable, "-c", "print('alive')"], timeout=10)
        self.assertFalse(timed_out2)
        self.assertEqual(0, code2)
        self.assertEqual(b"alive\n", out2)

    def test_missing_command_is_reported_not_raised(self):
        code, _out, err, timed_out = vf.run_checked(
            ["definitely-not-a-command-xyz"], timeout=5)
        self.assertFalse(timed_out)
        self.assertIsNone(code)
        self.assertIn("command not found", err)


class DescriptorTests(unittest.TestCase):
    def test_canonical_descriptor_is_lf_terminated_with_final_newline(self):
        descriptor = vf.canonical_descriptor("1", "valid-empty", 1, 1, 42, "a" * 64)
        self.assertTrue(descriptor.endswith(b"\n"))
        self.assertNotIn(b"\r", descriptor)
        self.assertEqual(b"zipper-archive-test\n1\n1\nvalid-empty\n1\n1\n42\n" + b"a" * 64 + b"\n",
                         descriptor)

    def test_compute_fixture_id_matches_the_frozen_vector(self):
        # Frozen test vector from REQ-210: the 22-byte empty Archive, Seed 42.
        self.assertEqual(
            "atc-4d275f1fe266174e43c71d2cbe42084da23ce42369a17af7b84a9d165b6c489f",
            vf.compute_fixture_id(
                "1", "valid-empty", 1, 2, 42,
                "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85"))


class SchemaLimitsTests(TempDirTest):
    def setUp(self):
        super().setUp()
        self.ajv = vf.AjvValidator(SCHEMA, DEFAULT_AJV)

    def _validate_case(self, case):
        path = os.path.join(self.dir, "test.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(case, handle)
        return self.ajv.validate(path)

    def test_schema_rejects_physical_size_over_16mib(self):
        case, _ = self.publish_valid()
        case["archive"]["physicalSize"] = 16 * 1024 * 1024 + 1
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_expanded_budget_over_32mib(self):
        case, _ = self.publish_valid()
        case["limits"]["expandedBytesBudget"] = 32 * 1024 * 1024 + 1
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_entry_count_over_1000(self):
        case, _ = self.publish_valid()
        case["limits"]["entryCount"] = 1001
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_json_budget_over_1mib(self):
        case, _ = self.publish_valid()
        case["limits"]["jsonBytesBudget"] = 1024 * 1024 + 1
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_deadline_over_10s(self):
        case, _ = self.publish_valid()
        case["limits"]["deadlineSeconds"] = 11
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_missing_compression_methods(self):
        case, _ = self.publish_valid()
        del case["entries"][0]["localHeaderMethod"]
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_out_of_range_method(self):
        case, _ = self.publish_valid()
        case["entries"][0]["localHeaderMethod"] = 65536
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_invalid_payload_codec(self):
        case, _ = self.publish_valid()
        case["entries"][0]["payloadCodec"] = "invalid-codec"
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_schema_rejects_directory_with_payload_codec(self):
        case, _ = self.publish_valid()
        case["entries"][0]["kind"] = "directory"
        case["entries"][0]["payloadCodec"] = "stored"
        with self.assertRaises(vf.VerificationError) as ctx:
            self._validate_case(case)
        self.assertEqual("schema", ctx.exception.check)

    def test_verifier_rejects_local_header_method_wire_mismatch(self):
        case, zip_bytes = self.publish_valid()
        case["entries"][0]["localHeaderMethod"] = 8  # wire is 0 (stored)
        with self.assertRaises(vf.VerificationError) as ctx:
            vf.verify_compression_methods(case, zip_bytes)
        self.assertEqual("compression-methods", ctx.exception.check)

    def test_verifier_rejects_central_directory_method_wire_mismatch(self):
        case, zip_bytes = self.publish_valid()
        case["entries"][0]["centralDirectoryMethod"] = 8  # wire is 0 (stored)
        with self.assertRaises(vf.VerificationError) as ctx:
            vf.verify_compression_methods(case, zip_bytes)
        self.assertEqual("compression-methods", ctx.exception.check)

    def test_verifier_rejects_boolean_method_code(self):
        case, zip_bytes = self.publish_valid()
        case["entries"][0]["localHeaderMethod"] = True
        with self.assertRaises(vf.VerificationError) as ctx:
            vf.verify_compression_methods(case, zip_bytes)
        self.assertEqual("compression-methods", ctx.exception.check)

    def test_verifier_rejects_case_contract_violation(self):
        case, zip_bytes = self.publish_valid()
        case["caseKey"] = "method-local-central-mismatch"
        # valid-stored has local 0, central 0, so contract for method-local-central-mismatch fails
        with self.assertRaises(vf.VerificationError) as ctx:
            vf.verify_compression_methods(case, zip_bytes)
        self.assertEqual("compression-methods", ctx.exception.check)


class InvariantEnforcementTests(TempDirTest):
    """REQ-212 invariant and failure-stage enforcement (ticket #1025): declared
    invariants are asserted against observed evidence, never decorative
    'checked' labels, and an accepted failure must land inside the declared
    allowed failure stages."""

    def test_list_count_disagreement_fails_through_invariant(self):
        """A reader whose listed count disagrees with the declared count must
        fail even when list-fails is an allowed outcome: the listed-count
        invariant is violated by the observed evidence."""
        zip_bytes = make_zip([("a.txt", b"first"), ("b.txt", b"second")])
        expectations = [
            {"operation": "list", "profile": "strict",
             "allowedOutcomes": ["list-succeeds", "list-fails"],
             "invariants": ["listed-count == entry-count"],
             "failureStages": ["open", "list"]},
            *VALID_EXPECTATIONS[1:],
        ]
        # Self-consistent metadata that wrongly declares a single entry: the
        # reader lists two, so the invariant evidence contradicts the claim.
        case = case_for("count-lie", "valid", zip_bytes,
                        [entry_record(0, "a.txt", b"first")], [], expectations)
        write_pair(self.dir, case, zip_bytes)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("listed-count == entry-count", failure["message"])

    def test_unknown_invariant_token_fails_verification(self):
        """An invariant the verifier cannot evaluate fails closed (REQ-212:
        unsupported is never a pass); the vocabulary grows only deliberately."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][0]["invariants"] = ["mystery-invariant"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("unverified-invariant", failure["check"])
        self.assertIn("mystery-invariant", failure["message"])

    def test_wrong_failure_stage_fails_verification(self):
        """A failure observed at a stage outside the declared allowed stages
        fails with the expectation quoted (observed stage is normalized:
        the verifier's 'per-entry' is the declared 'read-entry')."""
        case, _ = self.publish_crc_corrupt()
        case, path = self.rewrite_case(case)
        self.replace_expectation(
            case, "read-entry",
            {"operation": "read-entry", "profile": "strict",
             "allowedOutcomes": ["read-entry-fails"], "invariants": [],
             "failureStages": ["open"]})
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("read-entry[strict]", failure["message"])

    def test_allowed_failure_stage_with_normalized_stage_passes(self):
        """Positive control: the same per-entry failure is accepted when the
        declared stages name the read-entry stage, proving the stage
        normalization does not create false positives."""
        case, _ = self.publish_crc_corrupt()
        case, path = self.rewrite_case(case)
        self.replace_expectation(
            case, "read-entry",
            {"operation": "read-entry", "profile": "strict",
             "allowedOutcomes": ["read-entry-fails"], "invariants": [],
             "failureStages": ["open", "read-entry"]})
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("pass", operations["read-entry"]["status"])
        self.assertEqual("per-entry", operations["read-entry"]["failureStage"])

    def test_listed_count_without_evidence_fails_closed(self):
        """A listed-count invariant on an operation that records no list
        evidence can never be satisfied: it fails rather than passing silently,
        the same fail-closed rule that rejects unknown invariant tokens."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        self.replace_expectation(
            case, "read-entry",
            {"operation": "read-entry", "profile": "strict",
             "allowedOutcomes": ["read-entry-content-matches"],
             "invariants": ["listed-count == entry-count"]})
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("listed-count == entry-count", failure["message"])

    def test_numeric_listed_count_predicate_passes(self):
        """The numeric listed-count form ('== N') asserts the exact observed
        count; a matching archive passes with the invariant recorded."""
        zip_bytes = make_zip([("a.txt", b"first"), ("b.txt", b"second")])
        expectations = [
            {"operation": "list", "profile": "strict",
             "allowedOutcomes": ["list-succeeds", "list-fails"],
             "invariants": ["listed-count == 2"],
             "failureStages": ["open", "list"]},
            *VALID_EXPECTATIONS[1:],
        ]
        case = case_for("count-ok", "valid", zip_bytes,
                        [entry_record(0, "a.txt", b"first"),
                         entry_record(1, "b.txt", b"second")], [], expectations)
        write_pair(self.dir, case, zip_bytes)

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("pass", operations["list"]["status"])
        self.assertEqual(["listed-count == 2"],
                         operations["list"]["invariantsChecked"])

    def test_structural_invariant_audit_mismatch_fails(self):
        """A structural token whose audit did not run for this case key cannot
        be satisfied: the claim names an audit that never executed."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][0]["invariants"].append(
            "two-structurally-plausible-eocd-records")
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("eocd-ambiguity", failure["message"])

    def test_integrity_rejection_outcome_stage_mismatch_fails(self):
        """A codec/CRC rejection is a staged failure too: it is checked against
        the declared allowed stages and fails when the observed stage is outside
        them (otherwise the wrong-stage defect survives on this outcome class)."""
        case, _ = self.publish_crc_corrupt()
        case, path = self.rewrite_case(case)
        self.replace_expectation(
            case, "integrity-check",
            {"operation": "integrity-check", "profile": "strict",
             "allowedOutcomes": ["crc-mismatch-rejected"], "invariants": [],
             "failureStages": ["open"]})
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("integrity-check[strict]", failure["message"])

    def test_integrity_unsupported_method_rejection_stage_mismatch_fails(self):
        """The published unsupported-method fixture permits a codec rejection;
        narrowing its integrity oracle to that rejection at open must not accept
        the observed per-entry rejection."""
        case, _ = self.publish_unsupported_method()
        published = next(expectation for expectation in case["expectations"]
                         if expectation["operation"] == "integrity-check")
        self.assertIn("unsupported-method-rejected", published["allowedOutcomes"])
        self.assertIn("read-entry", published["failureStages"])
        case, path = self.rewrite_case(case)
        integrity = next(expectation for expectation in case["expectations"]
                         if expectation["operation"] == "integrity-check")
        integrity["allowedOutcomes"] = ["unsupported-method-rejected"]
        integrity["failureStages"] = ["open"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("observed failure stage 'per-entry'", failure["message"])
        self.assertIn("integrity-check[strict]", failure["message"])

    def test_integrity_unsupported_method_rejection_without_failure_stages_fails(self):
        case, _ = self.publish_unsupported_method()
        case, path = self.rewrite_case(case)
        integrity = next(expectation for expectation in case["expectations"]
                         if expectation["operation"] == "integrity-check")
        integrity["allowedOutcomes"] = ["unsupported-method-rejected"]
        del integrity["failureStages"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("observed failure 'unsupported-method-rejected'", failure["message"])
        self.assertIn("operation 'integrity-check'", failure["message"])
        self.assertIn("integrity-check[strict]", failure["message"])

    def test_staged_outcome_without_own_failure_stages_fails_in_multi_profile_case(self):
        """Stages bind per profile. A multi-profile Expectation File must not let a
        sibling profile's failureStages satisfy the record that actually governs the
        observed outcome, or a stripped stage set on that profile goes unnoticed."""
        case, _ = self.publish_unsupported_method()
        case, path = self.rewrite_case(case)

        governing = {"operation": "integrity-check", "profile": "unsupported-reader",
                     "allowedOutcomes": ["unsupported-method-rejected"],
                     "invariants": ["no-partial-writes"]}
        sibling = {"operation": "integrity-check", "profile": "full-codec",
                   "allowedOutcomes": ["integrity-passes"],
                   "invariants": ["no-partial-writes"],
                   "failureStages": ["open", "read-entry"]}
        case["expectations"] = [
            expectation for expectation in case["expectations"]
            if expectation["operation"] != "integrity-check"
        ] + [governing, sibling]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("has no declared failure stages", failure["message"])
        self.assertIn("integrity-check[unsupported-reader]", failure["message"])

    def test_multi_profile_case_with_governing_stages_passes(self):
        """The per-profile rule must not reject a well-formed multi-profile file: the
        profile that allows the observed outcome declares a stage set containing it."""
        case, _ = self.publish_unsupported_method()
        case, path = self.rewrite_case(case)

        governing = {"operation": "integrity-check", "profile": "unsupported-reader",
                     "allowedOutcomes": ["unsupported-method-rejected"],
                     "invariants": ["no-partial-writes"],
                     "failureStages": ["open", "read-entry"]}
        sibling = {"operation": "integrity-check", "profile": "full-codec",
                   "allowedOutcomes": ["integrity-passes"],
                   "invariants": ["no-partial-writes"],
                   "failureStages": ["open", "read-entry"]}
        case["expectations"] = [
            expectation for expectation in case["expectations"]
            if expectation["operation"] != "integrity-check"
        ] + [governing, sibling]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code, report)
        self.assertIsNone(report["fixtures"][0]["failure"])

    def test_unscoped_fixture_shadowed_by_platform_mark_fails(self):
        """An unscoped Expectation File must not be made to verify nothing. Marking
        records platform-inapplicable on this host would report every operation
        'not-run' while the fixture still counted as passed."""
        case, _ = self.publish_unsupported_method()
        case, path = self.rewrite_case(case)
        for expectation in case["expectations"]:
            if expectation["operation"] != "list":
                expectation["platform"] = "windows"
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("expectation", failure["check"])
        self.assertIn("no applicable expectation on this host", failure["message"])
        self.assertIn("could", failure["message"])

    def test_integrity_unsupported_method_rejection_with_empty_failure_stages_fails(self):
        case, _ = self.publish_unsupported_method()
        case, path = self.rewrite_case(case)
        integrity = next(expectation for expectation in case["expectations"]
                         if expectation["operation"] == "integrity-check")
        integrity["allowedOutcomes"] = ["unsupported-method-rejected"]
        integrity["failureStages"] = []
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("observed failure 'unsupported-method-rejected'", failure["message"])
        self.assertIn("operation 'integrity-check'", failure["message"])
        self.assertIn("integrity-check[strict]", failure["message"])

    def test_missing_integrity_expectation_operation_fails(self):
        case, _ = self.publish_unsupported_method()
        case, path = self.rewrite_case(case)
        case["expectations"] = [expectation for expectation in case["expectations"]
                                if expectation["operation"] != "integrity-check"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("expectation", failure["check"])
        self.assertIn("missing operation record", failure["message"])
        self.assertIn("integrity-check", failure["message"])

    def test_integrity_unsupported_method_rejection_accepts_raw_and_normalized_stages(self):
        case, _ = self.publish_unsupported_method()

        for declared_stage in ("read-entry", "per-entry"):
            with self.subTest(declared_stage=declared_stage):
                case, path = self.rewrite_case(case)
                integrity = next(expectation for expectation in case["expectations"]
                                 if expectation["operation"] == "integrity-check")
                integrity["allowedOutcomes"] = ["unsupported-method-rejected"]
                integrity["failureStages"] = [declared_stage]
                self.persist(path, case)

                report, exit_code = verify(self.dir)

                self.assertEqual(0, exit_code, report["fixtures"][0].get("failure"))
                operations = {op["operation"]: op
                              for op in report["fixtures"][0]["operations"]}
                self.assertEqual("pass", operations["integrity-check"]["status"])
                self.assertEqual("per-entry", operations["integrity-check"]["failureStage"])

    def test_rejection_outcome_accepts_raw_per_entry_stage(self):
        """Positive control: the schema documents the stage spelling
        'per-entry'; a declaration using it must match, never false-fail with a
        self-contradictory message."""
        case, _ = self.publish_crc_corrupt()
        case, path = self.rewrite_case(case)
        self.replace_expectation(
            case, "integrity-check",
            {"operation": "integrity-check", "profile": "strict",
             "allowedOutcomes": ["crc-mismatch-rejected"], "invariants": [],
             "failureStages": ["per-entry"]})
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual("pass", operations["integrity-check"]["status"])

    def test_policy_invariant_on_wrong_operation_fails_closed(self):
        """Extract-only containment tokens declared on another operation name
        nothing the verifier observed: they fail closed instead of passing."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][0]["invariants"] = ["no-silent-overwrite"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("no-silent-overwrite", failure["message"])

    def test_invariants_checked_field_mirrors_declared_set(self):
        """The report contract records the asserted invariant set per operation,
        so consumers can see exactly what was checked."""
        case, _ = self.publish_valid()

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertEqual(["no-partial-writes"], operations["list"]["invariantsChecked"])
        self.assertEqual(["no-partial-writes"],
                         operations["read-entry"]["invariantsChecked"])

    def test_structural_invariant_accepted_when_audit_ran(self):
        """Positive control: a structural token whose audit did run passes and
        is recorded, proving the token-to-audit mapping asserts rather than only
        fail-closes."""
        case, _ = self.publish_crc_corrupt()  # the mutations audit always runs
        case, path = self.rewrite_case(case)
        case["expectations"][0]["invariants"].append("declared-offsets-are-lies")
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertIn("declared-offsets-are-lies",
                      operations["list"]["invariantsChecked"])

    def test_structural_mutation_token_without_mutations_fails(self):
        """A mutation-backed structural token on a clean Archive names a
        property the Archive does not have: the claim fails even though the
        mutations audit ran, because there is no mutation evidence."""

        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][0]["invariants"].append("declared-offsets-are-lies")
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("declared-offsets-are-lies", failure["message"])
        self.assertIn("no mutation evidence", failure["message"])

    def test_policy_invariant_on_exercised_extract_fails_closed(self):
        """A policy token on an extract that actually ran cannot be proven
        contained on an ordinary host: it fails closed."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][3]["invariants"] = ["no-silent-overwrite"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("no-silent-overwrite", failure["message"])

    def test_numeric_listed_count_mismatch_fails(self):
        """The numeric listed-count form asserts the exact observed count; a
        mismatched declaration fails the invariant even though list-fails is
        allowed."""
        zip_bytes = make_zip([("a.txt", b"first"), ("b.txt", b"second")])
        expectations = [
            {"operation": "list", "profile": "strict",
             "allowedOutcomes": ["list-succeeds", "list-fails"],
             "invariants": ["listed-count == 3"],
             "failureStages": ["open", "list"]},
            *VALID_EXPECTATIONS[1:],
        ]
        case = case_for("count-lie-numeric", "valid", zip_bytes,
                        [entry_record(0, "a.txt", b"first"),
                         entry_record(1, "b.txt", b"second")], [], expectations)
        write_pair(self.dir, case, zip_bytes)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("listed-count == 3", failure["message"])

    def test_budget_invariant_vacuous_on_list_passes(self):
        """The budget token rides list expectations too, where no bytes are
        streamed: nothing was allocated, so the claim is vacuously satisfied."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][0]["invariants"].append(
            "no-allocation-from-declared-sizes")
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(0, exit_code)
        operations = {op["operation"]: op for op in report["fixtures"][0]["operations"]}
        self.assertIn("no-allocation-from-declared-sizes",
                      operations["list"]["invariantsChecked"])

    def test_extract_failure_stage_mismatch_fails(self):
        """extract-fails is a staged outcome too: an accepted extraction
        failure at a prohibited stage fails the fixture."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["entries"][0]["contentSha256"] = "0" * 64  # lie -> extract-fails
        self.replace_expectation(
            case, "extract",
            {"operation": "extract", "profile": "strict",
             "allowedOutcomes": ["extract-fails"], "invariants": [],
             "failureStages": ["open"]})
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("extract[strict]", failure["message"])

    def test_list_failure_stage_mismatch_fails(self):
        """list-fails at the 'list' stage is checked against the declared
        stages: a declaration that only allows 'open' rejects it."""
        zip_bytes = make_zip([("a.txt", b"first"), ("b.txt", b"second")])
        expectations = [
            {"operation": "list", "profile": "strict",
             "allowedOutcomes": ["list-fails"], "invariants": [],
             "failureStages": ["open"]},
            *VALID_EXPECTATIONS[1:],
        ]
        case = case_for("count-lie-stage", "valid", zip_bytes,
                        [entry_record(0, "a.txt", b"first")], [], expectations)
        write_pair(self.dir, case, zip_bytes)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("failure-stage", failure["check"])
        self.assertIn("list[strict]", failure["message"])

    def test_no_files_created_on_nonempty_extract_fails(self):
        """no-files-created can only hold on an empty archive; a non-empty
        extract violates it even though extraction itself succeeded."""
        case, _ = self.publish_valid()
        case, path = self.rewrite_case(case)
        case["expectations"][3]["invariants"] = ["no-files-created"]
        self.persist(path, case)

        report, exit_code = verify(self.dir)

        self.assertEqual(1, exit_code)
        failure = report["fixtures"][0]["failure"]
        self.assertEqual("invariant", failure["check"])
        self.assertIn("no-files-created", failure["message"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
