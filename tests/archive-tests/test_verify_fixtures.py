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


def entry_record(ordinal, name, content):
    return {
        "ordinal": ordinal,
        "kind": "file",
        "localNameRaw": name.encode("utf-8").hex(),
        "centralNameRaw": name.encode("utf-8").hex(),
        "readableName": name,
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
     "invariants": ["payload-bytes-unchanged"]},
    {"operation": "read-entry", "profile": "strict",
     "allowedOutcomes": ["read-entry-content-matches", "read-entry-fails"],
     "invariants": ["payload-bytes-unchanged"]},
    {"operation": "integrity-check", "profile": "strict-integrity-v1",
     "allowedOutcomes": ["crc-mismatch-rejected", "crc-mismatch-unchecked"],
     "invariants": ["payload-bytes-unchanged"], "capability": "crc32"},
    {"operation": "extract", "profile": "strict",
     "allowedOutcomes": ["extract-succeeds", "extract-fails"],
     "invariants": ["payload-bytes-unchanged"]},
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
            "atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950",
            vf.compute_fixture_id(
                "1", "valid-empty", 1, 1, 42,
                "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
