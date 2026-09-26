#!/usr/bin/env python3
"""Independent verifier for published Archive Test Fixture pairs (ticket #845).

Consumes the Expectation File (<fixtureId>.json) beside each Archive — never
inside it — and re-derives every structural claim with an implementation that
shares no code with the C# generator:

  1. flat pair enumeration (exact match; orphans, duplicates, extra files rejected)
  2. authoritative JSON Schema (draft-07) validation via the pinned Ajv CLI
  3. independent Archive length/SHA-256 and canonical Fixture ID recompute
  4. mutation record linkage/offset arithmetic and targeted raw byte comparison
     (control reconstruction by inverting in-place patches when inline hex is complete)
  5. entry reads by ordinal (ZipInfo objects, never name-keyed lookups),
     streamed and bounded by the declared expansion budget
  6. real extraction for valid controls into a fresh owned directory
  7. bounded list/read/integrity operations for malformed fixtures
  8. no extraction for policy-sensitive fixtures (extract stays not-run)

Report contract: one JSON report outside the fixture directory recording
fixtureId, Case Key, adapter/runtime version, operation, normalized outcome,
failure stage, asserted invariants, and pass/fail/not-run per fixture.

Exit status: 0 only when every fixture passes; nonzero for required failures,
malformed oracles, verification timeouts or resource exhaustion, or missing
prerequisites. Unlike tests/goldens/lib/validate-properties-json.sh there is no
silent-skip fallback: a missing prerequisite is a failure, never a pass.

Domain terms follow UBIQUITOUS_LANGUAGE.md: Archive Test Fixture, Case Key,
Seed, Expectation File, Fixture ID.
"""

import argparse
import hashlib
import io
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import time
import zipfile

DESCRIPTOR_MAGIC = "zipper-archive-test"
DESCRIPTOR_VERSION = "1"
FIXTURE_ID_PREFIX = "atc-"

PAIR_NAME_RE = re.compile(r"^atc-[0-9a-f]{64}\.(zip|json)$")

DEFAULT_SCHEMA = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                              "..", "fixtures", "archive-test-case.schema.json")
# Pinned local Ajv CLI (ticket #932): node runs the restored package directly —
# never npx, never the network. Restore once with `npm ci` in this directory
# (pinned by package-lock.json); verification itself downloads nothing.
DEFAULT_AJV_SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                  "node_modules", "ajv-cli", "dist", "index.js")
DEFAULT_AJV = "node " + DEFAULT_AJV_SCRIPT
AJV_TIMEOUT_SECONDS = 120
READ_CHUNK = 64 * 1024

MAX_ARCHIVE_PHYSICAL_BYTES = 16 * 1024 * 1024
MAX_EXPANDED_BYTES_BUDGET = 32 * 1024 * 1024
MAX_ENTRIES = 1_000
MAX_JSON_BYTES = 1024 * 1024
MAX_DEADLINE_SECONDS = 10
MAX_FOLDER_TOTAL_BYTES = 256 * 1024 * 1024

OPERATIONS = ("list", "read-entry", "integrity-check", "extract")
FAILURE_TOKENS = {
    "list": "list-fails",
    "read-entry": "read-entry-fails",
    "integrity-check": "integrity-fails",
    "extract": "extract-fails",
}
# Catalog success vocabulary -> the verifier's canonical success token per operation.
SUCCESS_ALIASES = {
    "listed-count-matches-entries": "list-succeeds",
    "empty-list": "list-succeeds",
    "read-entry-content-matches": "read-entry-content-matches",
    "integrity-passes": "integrity-passes",
    "extract-completes": "extract-succeeds",
    "extract-completes-empty": "extract-succeeds",
}

# The verifier's canonical success token per operation, used to require full
# evidence when an invariant is declared (REQ-212, ticket #1025). Keep in
# lockstep with SUCCESS_ALIASES above: operation outcomes are normalized to
# these canonical tokens before _record_operation, so the invariant handlers and
# the outcome gate compare against the same spelling.
OP_SUCCESS_TOKENS = {
    "list": "list-succeeds",
    "read-entry": "read-entry-content-matches",
    "integrity-check": "integrity-passes",
    "extract": "extract-succeeds",
}

# Invariant vocabulary the verifier can evaluate. An invariant outside this
# vocabulary fails closed as 'unverified-invariant': the vocabulary grows only
# by shipping the matching assertion, never by relabeling a collected token.
LISTED_COUNT_PREDICATE = re.compile(r"^listed-count == (entry-count|\d+)$")
# Structural claims re-derived by the structure audits that run before any
# operation (verify_structure raises and fails the fixture when they break):
# token -> audit check name.
STRUCTURAL_INVARIANT_CHECKS = {
    "two-structurally-plausible-eocd-records": "eocd-ambiguity",
    "shadow-eocd-selects-b-bin": "eocd-ambiguity",
    "declared-offsets-are-lies": "mutations",
    "declared-sizes-are-lies": "mutations",
    "declared-counts-disagree": "mutations",
    "metadata-sources-disagree": "mutations",
    "one-member-fails": "mutations",
    "one-member-unsupported": "mutations",
    "spanning-declared-single-file": "mutations",
}
# Policy/containment invariants ride the extract expectation of policy-sensitive
# fixtures, which ordinary hosts never extract (extract stays not-run): nothing
# was written outside the root, silently overwritten, materialized as a link, or
# followed. Extraction exercised on such a fixture cannot be proven contained.
POLICY_EXTRACT_INVARIANTS = frozenset((
    "no-writes-outside-root",
    "containment-required",
    "no-silent-overwrite",
    "distinct-content-hashes-observable",
    "links-never-materialized-as-os-links",
    "escape-targets-never-followed",
))
# The verifier reports failures while reading entries as stage 'per-entry'; the
# declared vocabulary names that stage 'read-entry' (the operation it happens in).
CANONICAL_FAILURE_STAGES = {"per-entry": "read-entry"}

# Outcomes that record a failure stage and are therefore subject to failure-stage
# enforcement when an expectation declares allowed stages (REQ-212, #1025): the
# operation's failure token plus the codec-rejection outcomes the generator pairs
# with declared failure stages. The 'unchecked' outcome variants record no stage at
# all (see op_integrity_check) and are never staged.
STAGE_CHECKED_OUTCOMES = {
    "list": ("list-fails",),
    "read-entry": ("read-entry-fails", "unsupported-method-rejected"),
    "integrity-check": ("integrity-fails", "crc-mismatch-rejected", "unsupported-method-rejected"),
    "extract": ("extract-fails", "unsupported-method-rejected"),
}


def expectation_items(expectations, key):
    """Sorted, deduplicated values declared for a key across expectations."""
    return sorted({item for expectation in expectations
                   for item in expectation.get(key, [])})


class VerificationError(Exception):
    """A required verification failure for one fixture (exit code becomes 1)."""

    def __init__(self, check, message, stage="structure"):
        super().__init__(message)
        self.check = check
        self.stage = stage


def sha256_hex(data):
    return hashlib.sha256(data).hexdigest()


def canonical_descriptor(generator_contract_version, case_key, case_revision,
                         expectation_revision, seed, archive_sha256):
    """Independent re-implementation of the canonical LF-terminated descriptor."""
    return ("\n".join([
        DESCRIPTOR_MAGIC,
        DESCRIPTOR_VERSION,
        str(generator_contract_version),
        str(case_key),
        str(case_revision),
        str(expectation_revision),
        str(seed),
        str(archive_sha256),
    ]) + "\n").encode("utf-8")


def compute_fixture_id(generator_contract_version, case_key, case_revision,
                       expectation_revision, seed, archive_sha256):
    descriptor = canonical_descriptor(generator_contract_version, case_key,
                                      case_revision, expectation_revision,
                                      seed, archive_sha256)
    return FIXTURE_ID_PREFIX + sha256_hex(descriptor)


def parse_json_strict(text):
    """JSON parse that rejects duplicate object keys (a silent oracle corruption)."""
    def no_duplicates(pairs):
        seen = {}
        for key, value in pairs:
            if key in seen:
                raise ValueError("duplicate JSON key '%s'" % key)
            seen[key] = value
        return seen

    return json.loads(text, object_pairs_hook=no_duplicates)


def run_checked(argv, timeout):
    """Runs a subprocess with a deadline; on timeout kills that child tree.

    Returns (returncode, stdout, stderr, timed_out). The parent is never
    terminated; a timeout is reported as a verification failure. The child is
    started in its own session/process group so the kill reaches the whole
    child tree, not just the launcher.
    """
    popen_kwargs = {}
    if os.name == "posix":
        popen_kwargs["start_new_session"] = True
    try:
        proc = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                **popen_kwargs)
    except FileNotFoundError:
        return None, b"", "command not found: %s" % argv[0], False
    except OSError as exc:
        return None, b"", str(exc), False
    try:
        out, err = proc.communicate(timeout=timeout)
        return proc.returncode, out or b"", err or b"", False
    except subprocess.TimeoutExpired:
        _kill_tree(proc)
        proc.communicate()
        return None, b"", "timeout", True


def _kill_tree(proc):
    """Kills the child and its descendants; only that tree, never the parent."""
    if os.name == "posix":
        import signal
        try:
            os.killpg(proc.pid, signal.SIGKILL)
        except (ProcessLookupError, PermissionError):
            proc.kill()
    else:
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        proc.kill()


class AjvValidator:
    """Authoritative draft-07 validation through the pinned local Ajv CLI (node)."""

    def __init__(self, schema_path, command):
        self.schema_path = os.path.abspath(schema_path)
        # posix=False on Windows: backslashes in the absolute script path are
        # separators, not escapes (shlex would eat them with posix=True).
        self.command = shlex.split(command, posix=os.name != "nt")
        self.resolved = self.command.copy()
        # Resolve the launcher via PATH (node, or npx.cmd for custom commands).
        self.resolved[0] = shutil.which(self.command[0]) or self.command[0]

    def check_prerequisite(self):
        if not os.path.isfile(self.schema_path):
            return "missing prerequisite: schema file %s" % self.schema_path
        if shutil.which(self.command[0]) is None and not os.path.isfile(self.command[0]):
            return ("missing prerequisite: Ajv launcher '%s' not found on PATH "
                    "(expected node for the pinned Ajv CLI; a missing tool is a failure, "
                    "not a skipped check)" % self.command[0])
        if os.path.basename(self.command[0]).lower().startswith("node") and len(self.command) > 1 \
                and not os.path.isfile(self.command[1]):
            return ("missing prerequisite: pinned Ajv CLI not restored at %s "
                    "(run `npm ci --ignore-scripts --prefix tests/archive-tests` once; verification never downloads, so a missing "
                    "restore is a failure, not a skipped check)" % (self.command[1],))
        return None

    def validate(self, json_path):
        argv = self.resolved + ["test", "-s", self.schema_path, "-d",
                                os.path.abspath(json_path), "--valid", "--spec=draft7"]
        code, out, err, timed_out = run_checked(argv, AJV_TIMEOUT_SECONDS)
        if timed_out:
            raise VerificationError("schema", "Ajv schema validation timed out for %s"
                                    % os.path.basename(json_path), stage="prerequisite")
        if code is None:
            raise VerificationError("schema", "Ajv could not be executed: %s" % err,
                                    stage="prerequisite")
        if code != 0:
            detail = (err or out).decode("utf-8", "replace").strip()
            raise VerificationError("schema", "Expectation File %s failed draft-07 "
                                    "schema validation: %s"
                                    % (os.path.basename(json_path), detail), stage="schema")
        return True


class FixtureVerifier:
    """Verifies one published Archive Test Fixture pair against its Expectation File."""

    def __init__(self, json_path, zip_path, ajv, platforms):
        self.json_path = json_path
        self.zip_path = zip_path
        self.ajv = ajv
        self.platforms = platforms
        self.bytes_read = 0
        self.subprocess_count = 0
        self.started = None

    # ---- shared helpers ----

    def _check_deadline(self, deadline_seconds):
        if self.started is not None and time.monotonic() - self.started > deadline_seconds:
            raise VerificationError("deadline", "per-fixture deadline of %d seconds "
                                    "exceeded" % deadline_seconds, stage="verification")

    def _read_stream(self, stream, cap, counter):
        """Streams in chunks, bounded per operation by the expansion budget.
        The counter is updated in a finally block so a budget violation still
        reports the bytes actually consumed."""
        chunks = []
        total = 0
        try:
            while True:
                chunk = stream.read(READ_CHUNK)
                if not chunk:
                    break
                total += len(chunk)
                if total > cap - counter["read"]:
                    raise VerificationError("bounded-read",
                                            "entry read exceeded the declared expansion "
                                            "budget (resource exhaustion)", stage="read-entry")
                chunks.append(chunk)
        finally:
            counter["read"] += total
            self.bytes_read += total
        return b"".join(chunks)

    def _open(self, zip_bytes):
        return zipfile.ZipFile(io.BytesIO(zip_bytes))

    def _validate_case_budgets(self, case):
        """Validates REQ-213 fixed metadata budgets before reading fixture bytes."""
        archive = case.get("archive")
        if isinstance(archive, dict):
            physical_size = archive.get("physicalSize")
            if isinstance(physical_size, int) and physical_size > MAX_ARCHIVE_PHYSICAL_BYTES:
                raise VerificationError(
                    "archive-size-sha",
                    "archive.physicalSize %d exceeds the 16 MiB physical budget" % physical_size)

        limits = case.get("limits")
        if isinstance(limits, dict):
            json_budget = limits.get("jsonBytesBudget")
            if isinstance(json_budget, int) and json_budget > MAX_JSON_BYTES:
                raise VerificationError(
                    "json-bytes-budget",
                    "limits.jsonBytesBudget %d exceeds the 1 MiB budget" % json_budget)

            entry_count = limits.get("entryCount")
            if isinstance(entry_count, int) and entry_count > MAX_ENTRIES:
                raise VerificationError(
                    "entries-ordinals",
                    "limits.entryCount %d exceeds the %d entry budget" % (entry_count, MAX_ENTRIES))

            expanded_budget = limits.get("expandedBytesBudget")
            if isinstance(expanded_budget, int) and expanded_budget > MAX_EXPANDED_BYTES_BUDGET:
                raise VerificationError(
                    "expanded-bytes-budget",
                    "limits.expandedBytesBudget %d exceeds the 32 MiB budget" % expanded_budget)

            deadline = limits.get("deadlineSeconds")
            if isinstance(deadline, int) and deadline > MAX_DEADLINE_SECONDS:
                raise VerificationError(
                    "deadline",
                    "limits.deadlineSeconds %d exceeds the 10-second budget" % deadline)

        entries = case.get("entries")
        if isinstance(entries, list) and len(entries) > MAX_ENTRIES:
            raise VerificationError(
                "entries-ordinals",
                "entries count %d exceeds the %d entry budget" % (len(entries), MAX_ENTRIES))

    # ---- structure checks (required for every fixture) ----

    def verify_structure(self, case, zip_bytes):
        checks = []

        # REQ-213 fixed maximum budget checks before Ajv:
        self._validate_case_budgets(case)

        json_size = os.path.getsize(self.json_path)
        if json_size > MAX_JSON_BYTES or (isinstance(case.get("limits"), dict)
                                          and json_size > case["limits"].get("jsonBytesBudget", MAX_JSON_BYTES)):
            declared = case.get("limits", {}).get("jsonBytesBudget", MAX_JSON_BYTES)
            raise VerificationError("json-bytes-budget",
                                    "Expectation File is %d bytes, over the declared "
                                    "%d-byte budget" % (json_size, declared))
        checks.append({"check": "json-bytes-budget", "status": "pass"})

        if len(zip_bytes) > MAX_ARCHIVE_PHYSICAL_BYTES:
            raise VerificationError("archive-size-sha",
                                    "published Archive is %d bytes, over the 16 MiB physical budget"
                                    % len(zip_bytes))
        checks.append({"check": "expanded-bytes-budget", "status": "pass"})

        # Ajv authoritative schema validation (subprocess, deadline-bounded).
        self.subprocess_count += 1
        self.ajv.validate(self.json_path)
        checks.append({"check": "schema", "status": "pass"})

        # Independent Archive length and SHA-256 recompute.
        if case["archive"]["physicalSize"] != len(zip_bytes):
            raise VerificationError("archive-size-sha",
                                    "archive.physicalSize is %d but the published "
                                    "Archive is %d bytes"
                                    % (case["archive"]["physicalSize"], len(zip_bytes)))
        actual_sha = sha256_hex(zip_bytes)
        if case["archive"]["sha256"] != actual_sha:
            raise VerificationError("archive-size-sha",
                                    "archive.sha256 does not match the SHA-256 of the "
                                    "published Archive bytes")
        checks.append({"check": "archive-size-sha", "status": "pass"})

        # Independent canonical Fixture ID recompute.
        recomputed = compute_fixture_id(
            case["generatorContractVersion"], case["caseKey"], case["caseRevision"],
            case["expectationRevision"], case["seed"], actual_sha)
        if case["fixtureId"] != recomputed:
            raise VerificationError("fixture-id",
                                    "fixtureId %s does not match the independently "
                                    "recomputed %s" % (case["fixtureId"], recomputed))
        if os.path.basename(self.json_path) != case["fixtureId"] + ".json":
            raise VerificationError("fixture-id",
                                    "Expectation File basename does not carry the "
                                    "declared Fixture ID")
        if case["archive"]["fileName"] != case["fixtureId"] + ".zip":
            raise VerificationError("fixture-id",
                                    "archive.fileName must equal fixtureId + \".zip\"")
        if os.path.basename(self.zip_path) != case["fixtureId"] + ".zip":
            raise VerificationError("fixture-id",
                                    "Archive basename does not carry the declared "
                                    "Fixture ID")
        checks.append({"check": "fixture-id", "status": "pass"})

        # Entry ordinals unique and consistent with the declared entry count.
        ordinals = [entry["ordinal"] for entry in case["entries"]]
        if len(set(ordinals)) != len(ordinals):
            raise VerificationError("entries-ordinals", "duplicate entry ordinal")
        if case["limits"]["entryCount"] != len(case["entries"]):
            raise VerificationError("entries-ordinals",
                                    "limits.entryCount %d does not match the %d entry "
                                    "records" % (case["limits"]["entryCount"],
                                                 len(case["entries"])))
        checks.append({"check": "entries-ordinals", "status": "pass"})

        # Independent compression method audit (ticket #929).
        method_note = verify_compression_methods(case, zip_bytes)
        checks.append({"check": "compression-methods", "status": "pass", "detail": method_note})

        # Mutation chain linkage, offset arithmetic, and raw byte comparison.
        mutation_note = verify_mutations(case, zip_bytes, actual_sha)
        checks.append({"check": "mutations", "status": "pass", "detail": mutation_note})

        # Orphan hidden entry (ticket #869): the hidden LFH at offset 0 has no CDH;
        # CD-driven readers list only visibles while a forward LFH scan sees more.
        if case.get("caseKey") == "orphan-local-header":
            orphan_note = verify_orphan_hidden(case, zip_bytes)
            checks.append({"check": "orphan-hidden", "status": "pass", "detail": orphan_note})

        # EOCD ambiguity (ticket #870): two complete central-directory/EOCD pairs
        # select disjoint entries. Python's backward scan must select the trailing
        # shadow pair while the authentic EOCD's declared comment spans to EOF.
        if case.get("caseKey") == "eocdr-ambiguity-comment":
            eocd_note = verify_eocd_ambiguity(case, zip_bytes)
            checks.append({"check": "eocd-ambiguity", "status": "pass", "detail": eocd_note})

        # Unicode Path extra field (ticket #871): a valid 0x7075 subfield in both
        # headers whose CRC matches the standard name; readers ignore the field.
        if case.get("caseKey") == "unicode-path-extra-mismatch":
            unicode_note = verify_unicode_path_extra(case, zip_bytes)
            checks.append({"check": "unicode-path", "status": "pass", "detail": unicode_note})

        # ADLS Gen2 segment depth (ticket #878): the single member's
        # container-relative path pins the hierarchical-namespace boundary
        # (63 segments, legal) or the first violation (64).
        if case.get("caseKey") in ("path-adls-segments-boundary", "path-adls-segments-exceeded"):
            adls_note = verify_adls_segments(case)
            checks.append({"check": "adls-segments", "status": "pass", "detail": adls_note})

        # Legacy-codec trail-byte names (ticket #887): the named CP932/Big5/GBK
        # consumer decodes the raw name bytes and asserts no spurious split.
        if case.get("caseKey") in ENCODING_TRAIL_BYTE_CASES:
            encoding_note = verify_encoding_trail_bytes(case, zip_bytes)
            checks.append({"check": "encoding-trail-byte", "status": "pass", "detail": encoding_note})

        # Prefixed cases (ticket #873): a 64-byte signature-free stub precedes the
        # Archive; the rebased twin shifts every offset past it, the unrebased twin
        # leaves stale offsets behind.
        if case.get("caseKey") in ("prefix-rebased", "prefix-unrebased"):
            prefix_note = verify_prefix_model(case, zip_bytes)
            checks.append({"check": "prefix-model", "status": "pass", "detail": prefix_note})

        # Coded valid controls (tickets #897/#898): the sidecar carries no method
        # field, so the method codes are audited here from both headers instead.
        if case.get("caseKey") == "valid-bzip2":
            coded_note = verify_coded_method(case, zip_bytes, 12)
            checks.append({"check": "coded-method", "status": "pass", "detail": coded_note})
        # Shared-range resource case (ticket #872): every central header points at
        # the single local header; the declared expansion is honestly structured.
        if case.get("caseKey") == "zip-bomb-overlapping-deflate":
            shared_note = verify_shared_payload_range(case, zip_bytes)
            checks.append({"check": "shared-range", "status": "pass", "detail": shared_note})

        # Coded valid controls (ticket #898; #897 adds BZip2; #931 adds the
        # Deflate64-specific control): the sidecar carries no method field,
        # so the method codes are audited here from both headers.
        if case.get("caseKey") in ("valid-deflate64", "valid-deflate64-long-match"):
            coded_note = verify_coded_method(case, zip_bytes, 9)
            checks.append({"check": "coded-method", "status": "pass", "detail": coded_note})

        # Hostile filename bytes (ticket #876): the sidecar raw hex must equal
        # the exact hostile bytes in both headers; reads stay ordinal-based so
        # a Python NUL truncation never becomes a name-keyed lookup.
        if case.get("caseKey") in ("filename-null-byte", "filename-c0-control"):
            hostile_note = verify_hostile_name(case, zip_bytes)
            checks.append({"check": "hostile-name", "status": "pass", "detail": hostile_note})

        return checks

    # ---- reader operations (normalized outcomes, never exception wording) ----

    def op_list(self, zip_bytes, case):
        """'listed-count-matches-entries' is a claim, so the count is verified:
        listing succeeds only when the reader's entry count equals the declared
        limits.entryCount."""
        try:
            with self._open(zip_bytes) as zf:
                entry_count = len(zf.infolist())
        except NotImplementedError:
            return "list-fails", {}, "open"
        except Exception:
            return "list-fails", {}, "open"
        details = {"entryCount": entry_count, "declaredEntryCount": case["limits"]["entryCount"]}
        if entry_count != case["limits"]["entryCount"]:
            return "list-fails", details, "list"
        return "list-succeeds", details, None

    def _read_all_entries(self, zf, case, infos, budget, crc_token):
        """Reads every file entry with known content by ordinal; returns the
        aggregate normalized read outcome. Shared by the read-entry and
        integrity-check operations so every byte is streamed and bounded.
        crc_token is the normalized token a CRC-32 rejection maps to for the
        calling operation ('read-entry-fails' vs 'crc-mismatch-rejected')."""
        counter = {"read": 0}
        details = {"entriesRead": 0, "hashMatches": 0}
        saw_mismatch = False
        saw_unsupported = False
        saw_crc_reject = False
        saw_failure = False
        for entry in case["entries"]:
            if entry["kind"] != "file" or entry.get("contentSha256") is None:
                continue
            if entry["ordinal"] >= len(infos):
                saw_failure = True
                continue
            info = infos[entry["ordinal"]]
            try:
                with zf.open(info) as stream:
                    data = self._read_stream(stream, budget, counter)
            except NotImplementedError:
                saw_unsupported = True
                continue
            except zipfile.BadZipFile as exc:
                if "CRC" in str(exc):
                    saw_crc_reject = True
                else:
                    saw_failure = True
                continue
            except Exception:
                saw_failure = True
                continue
            details["entriesRead"] += 1
            if entry.get("contentSize") is not None and len(data) != entry["contentSize"]:
                saw_mismatch = True
            elif sha256_hex(data) == entry["contentSha256"]:
                details["hashMatches"] += 1
            else:
                saw_mismatch = True
        details["bytesRead"] = counter["read"]
        if saw_unsupported:
            return "unsupported-method-rejected", details, "per-entry"
        if saw_crc_reject:
            return crc_token, details, "per-entry"
        if saw_failure:
            return "read-entry-fails", details, "per-entry"
        if saw_mismatch:
            return "read-entry-returns-unverified-bytes", details, None
        return "read-entry-content-matches", details, None

    def op_read_entry(self, zip_bytes, case, budget):
        try:
            with self._open(zip_bytes) as zf:
                infos = zf.infolist()
                return self._read_all_entries(zf, case, infos, budget,
                                              crc_token="read-entry-fails")
        except VerificationError:
            raise
        except NotImplementedError:
            return "read-entry-fails", {}, "open"
        except Exception:
            return "read-entry-fails", {}, "open"

    def op_integrity_check(self, zip_bytes, case, budget):
        """Integrity via bounded streams: Python's reader verifies CRC-32 while
        streaming, so a CRC lie surfaces as a rejection, not a silent pass.
        When the fixture declares a CRC mutation and the reader streams every
        entry without rejecting it, the honest normalized outcome is that the
        CRC lie went undetected ('crc-mismatch-unchecked'), not a clean pass:
        Python verifies against the central-directory value and never consults
        a patched local-header CRC."""
        try:
            with self._open(zip_bytes) as zf:
                infos = zf.infolist()
                outcome, details, stage = self._read_all_entries(
                    zf, case, infos, budget, crc_token="crc-mismatch-rejected")
        except VerificationError:
            raise
        except NotImplementedError:
            return "unsupported-method-rejected", {}, "open"
        except Exception:
            return "integrity-fails", {}, "open"
        if outcome in ("read-entry-fails", "read-entry-returns-unverified-bytes"):
            return "integrity-fails", details, "per-entry"
        if outcome == "read-entry-content-matches":
            if any("crc" in mutation["code"] for mutation in case["mutations"]):
                return "crc-mismatch-unchecked", details, None
            return "integrity-passes", details, None
        return outcome, details, stage

    @staticmethod
    def _unsafe_extract_names(infos):
        """Independent name-safety scan of the Archive itself (never the JSON
        metadata): absolute paths, parent traversal, NUL bytes, backslashes,
        and Windows drive letters make extraction unsafe on ordinary hosts."""
        for info in infos:
            name = info.filename
            if (name.startswith("/") or name.startswith("\\")
                    or "\x00" in name
                    or re.search(r"(^|[\\/])\.\.([\\/]|$)", name)
                    or re.match(r"^[A-Za-z]:", name)):
                return name
        return None

    def op_extract(self, zip_bytes, case, budget):
        """Real extraction for safe valid controls only, into a fresh owned
        directory. The classification field alone is never trusted: extraction
        additionally requires an empty mutation list, independently safe entry
        names, and a declared expansion within budget. Every byte is written
        through the bounded reader, so no zip bomb can expand unbounded."""
        counter = {"read": 0}
        try:
            with self._open(zip_bytes) as zf:
                infos = zf.infolist()

                # Independent safety derivation (the JSON is not hash-bound).
                if case["mutations"]:
                    # Divergence from the 7-Zip adapter, deliberately. This reader
                    # extracts into a real directory on the host, so a fixture
                    # carrying a declared mutation is refused outright: a control
                    # is only extracted when nothing about it is adversarial.
                    # The 7-Zip E2E extracts the same mutated cases because it
                    # extracts to a scratch tree it owns and treats the adapter's
                    # own policy output as the oracle rather than re-deriving
                    # safety. So one case can be extracted by one adapter and
                    # skipped by the other, and that is expected: coverage of a
                    # mutated case comes from the adapter that owns its policy,
                    # not from this reader. What must never happen is the weaker
                    # outcome — a mutated case reported as 'extract-succeeds'.
                    return ("not-run", {"reason": "fixture declares mutations; "
                                       "extraction is for safe controls only"}, None)
                unsafe = self._unsafe_extract_names(infos)
                if unsafe is not None:
                    return ("not-run", {"reason": "unsafe entry name '%s'" % unsafe,
                                       "declaredSafe": False}, None)
                declared_total = sum(info.file_size for info in infos if not info.is_dir())
                if declared_total > budget:
                    return ("not-run", {"reason": "declared expansion %d exceeds the "
                                       "budget %d" % (declared_total, budget)}, None)

                extracted_root = os.path.realpath(tempfile.mkdtemp(prefix="atc-extract-"))
                entries_by_ordinal = {entry["ordinal"]: entry for entry in case["entries"]}
                try:
                    for ordinal, info in enumerate(infos):
                        target = os.path.join(extracted_root, *info.filename.split("/"))
                        if os.path.commonpath([os.path.realpath(target), extracted_root]) != extracted_root:
                            raise VerificationError(
                                "extract-containment",
                                "extraction target '%s' escapes the owned root"
                                % info.filename)
                        if info.is_dir():
                            os.makedirs(target, exist_ok=True)
                            continue
                        parent = os.path.dirname(target)
                        if parent:
                            os.makedirs(parent, exist_ok=True)
                        try:
                            with zf.open(info) as stream:
                                data = self._read_stream(stream, budget, counter)
                        except NotImplementedError:
                            # A codec this reader cannot implement is rejected
                            # outright (#930), never a generic extraction failure.
                            return "unsupported-method-rejected", {"ordinal": ordinal}, "extract"
                        entry = entries_by_ordinal.get(ordinal)
                        if entry is not None and entry.get("contentSha256") is not None \
                                and sha256_hex(data) != entry["contentSha256"]:
                            return "extract-fails", {"ordinal": ordinal}, "extract"
                        with open(target, "wb") as handle:
                            handle.write(data)
                finally:
                    shutil.rmtree(extracted_root, ignore_errors=True)

                file_entries = [entry for entry in case["entries"] if entry["kind"] == "file"]
                file_infos = [info for info in infos if not info.is_dir()]
                if len(file_infos) != len(file_entries):
                    return "extract-fails", {"extractedFiles": len(file_infos)}, "extract"
                return "extract-succeeds", {"extractedFiles": len(file_infos),
                                            "bytesRead": counter["read"]}, None
        except VerificationError:
            raise
        except Exception:
            return "extract-fails", {}, "extract"

    # ---- expectation matching ----

    @staticmethod
    def _require_declared_content_hashes(case):
        """Every file entry must declare a content hash.

        The schema makes ``contentSha256`` optional, so a publisher can omit it.
        The operations that claim to have verified content then verify nothing:
        ``op_read_entry`` skips the entry entirely (and would report
        ``read-entry-content-matches`` having read zero entries), and
        ``op_extract`` writes the bytes and reports ``extract-succeeds``. Both are
        silent passes, so the Expectation File is refused up front rather than
        the outcome being quietly weakened. Directory entries legitimately carry
        no content and are unaffected. REQ-212: an unverifiable outcome is not a
        passing verification."""
        for entry in case["entries"]:
            if entry["kind"] != "file":
                continue
            if entry.get("contentSha256") is None:
                raise VerificationError(
                    "expectation-content",
                    "Expectation File for Case Key '%s' omits contentSha256 for file "
                    "entry at ordinal %d, so its content cannot be verified; the "
                    "Expectation File must declare a content hash for every file entry"
                    % (case["caseKey"], entry["ordinal"]),
                    stage="expectation")

    def applicable_expectations(self, case):
        """Validate complete operation coverage, then select platform-applicable
        expectations for this host (or those with no platform mark)."""
        self._require_declared_content_hashes(case)
        declared_operations = {expectation["operation"]
                               for expectation in case["expectations"]}
        missing_operations = [operation for operation in OPERATIONS
                              if operation not in declared_operations]
        if missing_operations:
            raise VerificationError(
                "expectation",
                "Expectation File for Case Key '%s' is missing operation record(s): %s"
                % (case["caseKey"], ", ".join(missing_operations)),
                stage="expectation")

        applicable = {}
        inapplicable = {}
        for expectation in case["expectations"]:
            platform = expectation.get("platform")
            operation = expectation["operation"]
            if platform is not None and platform not in self.platforms:
                inapplicable.setdefault(operation, set()).add(platform)
                continue
            applicable.setdefault(operation, []).append(expectation)

        # A platform-scoped Case Key (e.g. a Windows-only path case) legitimately has every
        # record marked for a platform this host is not, and verifying it off-platform is a
        # skip, not a pass. What must never happen is a Case Key with no platform scope at
        # all being marked inapplicable wholesale: that is a fixture claiming to be verified
        # while every operation reports "not-run". So a record only counts as a hole when it
        # has no platform mark of its own and another record for the same operation carries
        # the only applicable mark.
        unscoped = any(expectation.get("platform") is None
                       for expectation in case["expectations"])
        if unscoped:
            shadowed = [operation for operation in OPERATIONS
                        if operation not in applicable and operation in inapplicable]
            if shadowed:
                detail = ", ".join(
                    "%s (only records are marked for %s)"
                    % (operation, "/".join(sorted(inapplicable[operation])))
                    for operation in shadowed)
                raise VerificationError(
                    "expectation",
                    "Expectation File for Case Key '%s' has an unscoped record but leaves "
                    "operation(s) with no applicable expectation on this host, so they could "
                    "only be reported 'not-run': %s" % (case["caseKey"], detail),
                    stage="expectation")
        return applicable

    @staticmethod
    def outcome_allowed(observed, operation, expectations):
        allowed = set()
        for expectation in expectations:
            allowed.update(expectation["allowedOutcomes"])
        candidates = {observed}
        if observed == FAILURE_TOKENS[operation]:
            candidates.add("operation-fails")
        candidates.update(token for token, canonical in SUCCESS_ALIASES.items()
                          if canonical == observed)
        return bool(candidates & allowed), sorted(allowed)

    @staticmethod
    def _not_run(operation, reason, invariants=None):
        return {"operation": operation, "normalizedOutcome": "not-run",
                "failureStage": None, "details": {}, "status": "not-run",
                "reason": reason, "invariantsChecked": invariants or []}

    # ---- driver ----

    def verify(self):
        self.started = time.monotonic()
        result = {
            "fixtureId": None,
            "caseKey": None,
            "adapter": "python-zipfile",
            "adapterVersion": "%s %s" % (sys.implementation.name, sys.version.split()[0]),
            "operations": [],
            "structureChecks": [],
            "status": "fail",
            "failure": None,
        }
        try:
            # Fixed on-disk budget pre-checks (REQ-213) BEFORE reading fixture bytes:
            # 1. Archive on-disk size: enforce fixed 16 MiB ceiling. A forged
            # archive.physicalSize in metadata cannot increase this hard limit.
            zip_size = os.path.getsize(self.zip_path)
            if zip_size > MAX_ARCHIVE_PHYSICAL_BYTES:
                raise VerificationError(
                    "archive-size-sha",
                    "published Archive is %d bytes on disk, over the 16 MiB physical "
                    "budget" % zip_size)

            # 2. Expectation File on-disk size (REQ-213: <= 1 MiB) before parsing.
            json_size = os.path.getsize(self.json_path)
            if json_size > MAX_JSON_BYTES:
                raise VerificationError(
                    "json-bytes-budget",
                    "Expectation File is %d bytes on disk, over the 1 MiB budget"
                    % json_size)

            with open(self.json_path, "rb") as handle:
                raw_json = handle.read(MAX_JSON_BYTES + 1)
            if len(raw_json) > MAX_JSON_BYTES:
                raise VerificationError(
                    "json-bytes-budget",
                    "Expectation File exceeds the 1 MiB budget")

            case = parse_json_strict(raw_json.decode("utf-8"))
            result["fixtureId"] = case.get("fixtureId")
            result["caseKey"] = case.get("caseKey")

            # Validate metadata budgets BEFORE opening/reading Archive bytes!
            self._validate_case_budgets(case)

            with open(self.zip_path, "rb") as handle:
                zip_bytes = handle.read(MAX_ARCHIVE_PHYSICAL_BYTES + 1)
            if len(zip_bytes) > MAX_ARCHIVE_PHYSICAL_BYTES:
                raise VerificationError(
                    "archive-size-sha",
                    "published Archive exceeds the 16 MiB physical budget")

            raw_deadline = case.get("limits", {}).get("deadlineSeconds", MAX_DEADLINE_SECONDS)
            deadline = min(raw_deadline if isinstance(raw_deadline, int) and raw_deadline > 0 else MAX_DEADLINE_SECONDS,
                           MAX_DEADLINE_SECONDS)
            self._check_deadline(deadline)
            result["structureChecks"] = self.verify_structure(case, zip_bytes)

            classification = case.get("classification")
            raw_budget = case.get("limits", {}).get("expandedBytesBudget", MAX_EXPANDED_BYTES_BUDGET)
            budget = min(raw_budget if isinstance(raw_budget, int) and raw_budget >= 0 else MAX_EXPANDED_BYTES_BUDGET,
                         MAX_EXPANDED_BYTES_BUDGET)
            expectations = self.applicable_expectations(case)
            ops = []

            runners = {
                "list": lambda: self.op_list(zip_bytes, case),
                "read-entry": lambda: self.op_read_entry(zip_bytes, case, budget),
                "integrity-check": lambda: self.op_integrity_check(zip_bytes, case, budget),
            }
            for operation, run in runners.items():
                if operation not in expectations:
                    # A profile that lacks an operation records that fact rather
                    # than claiming success (REQ-212: not-run is not a pass).
                    ops.append(self._not_run(operation, "no expectation record"))
                    continue
                observed, details, stage = run()
                self._record_operation(ops, operation, observed, details, stage,
                                       expectations[operation], case,
                                       result["structureChecks"])
                self._check_deadline(deadline)

            if classification == "valid":
                if "extract" in expectations:
                    observed, details, stage = self.op_extract(zip_bytes, case, budget)
                    self._record_operation(ops, "extract", observed, details, stage,
                                           expectations["extract"], case,
                                           result["structureChecks"])
                else:
                    ops.append(self._not_run("extract", "no expectation record"))
            else:
                # Ticket #845 step 8: policy-sensitive and malformed fixtures are
                # never extracted on ordinary hosts; extract stays not-run and no
                # containment claim is made from metadata inspection.
                extract_expectations = expectations.get("extract")
                extract_invariants = []
                if extract_expectations:
                    extract_invariants = expectation_items(extract_expectations,
                                                           "invariants")
                    self._assert_invariants(extract_invariants, "extract", "not-run",
                                            {}, case, result["structureChecks"])
                ops.append(self._not_run("extract", "extraction not exercised for "
                                         "%s fixtures on ordinary hosts" % classification,
                                         extract_invariants))
            self._check_deadline(deadline)

            result["operations"] = ops
            result["bytesRead"] = self.bytes_read
            result["subprocessCount"] = self.subprocess_count
            result["status"] = "fail" if any(op["status"] == "fail" for op in ops) else "pass"
        except VerificationError as exc:
            result["failure"] = {"check": exc.check, "stage": exc.stage, "message": str(exc)}
            result["bytesRead"] = self.bytes_read
            result["subprocessCount"] = self.subprocess_count
            result["status"] = "fail"
        except Exception as exc:  # malformed oracle: the verifier must fail loudly
            result["failure"] = {"check": "oracle", "stage": "structure",
                                 "message": "unverifiable Expectation File: %s" % exc}
            result["bytesRead"] = self.bytes_read
            result["subprocessCount"] = self.subprocess_count
            result["status"] = "fail"
        return result

    def _record_operation(self, ops, operation, observed, details, stage, expectations,
                          case, structure_checks):
        invariants = expectation_items(expectations, "invariants")
        self._assert_invariants(invariants, operation, observed, details, case,
                                structure_checks)
        allowed, vocabulary = self.outcome_allowed(observed, operation, expectations)
        if allowed:
            self._assert_failure_stage(operation, observed, stage, expectations)
        record = {
            "operation": operation,
            "normalizedOutcome": observed,
            "failureStage": stage,
            "details": details,
            "allowedOutcomes": vocabulary,
            "invariantsChecked": invariants,
            "status": "pass" if allowed else "fail",
        }
        if not allowed:
            record["reason"] = ("observed outcome '%s' is outside the allowed outcomes"
                                % observed)
        ops.append(record)

    # ---- invariant and failure-stage enforcement (REQ-212, ticket #1025) ----

    def _assert_invariants(self, invariants, operation, observed, details, case,
                           structure_checks):
        """Every collected invariant is asserted against observed evidence and
        the structure audits that already ran; an unsatisfied or unverifiable
        invariant fails the fixture with the invariant named — never a
        decorative label (REQ-212, ticket #1025)."""
        if not invariants:
            return
        file_count = len([entry for entry in case.get("entries", [])
                          if entry["kind"] == "file"])
        budget = case.get("limits", {}).get("expandedBytesBudget")
        mutations = case.get("mutations") or []
        for token in invariants:
            self._assert_one_invariant(token, operation, observed, details,
                                       file_count, budget, mutations,
                                       structure_checks)

    def _assert_one_invariant(self, token, operation, observed, details, file_count,
                              budget, mutations, structure_checks):
        predicate = LISTED_COUNT_PREDICATE.match(token)
        if predicate:
            self._assert_listed_count(token, details, predicate.group(1))
            return
        if token in ("no-partial-writes", "extracted-bytes-match-content-hashes",
                     "payload-bytes-unchanged"):
            self._assert_write_completeness(token, operation, observed, details,
                                            file_count)
            return
        if token == "no-allocation-from-declared-sizes":
            # Ops record bytesRead only when bytes were streamed; a list
            # operation records none and the claim is vacuously satisfied
            # (the generator emits this token on list expectations too).
            read = details.get("bytesRead")
            if budget is not None and read is not None and read > budget:
                self._invariant_failure(token, "reader streamed %d bytes over the "
                                        "declared %d-byte budget" % (read, budget))
            return
        if token == "no-files-created":
            # Extract-only vocabulary: on other operations the claim names no
            # observable output and is vacuously satisfied, so it asserts the
            # exact evidence the extract operation records.
            if operation == "extract":
                extracted = details.get("extractedFiles")
                if extracted is not None and extracted != 0:
                    self._invariant_failure(token, "extraction created %d output files"
                                            % extracted)
            return
        audit = STRUCTURAL_INVARIANT_CHECKS.get(token)
        if audit is not None:
            checks = {check["check"] for check in structure_checks}
            if audit not in checks:
                self._invariant_failure(token, "structural audit '%s' did not run "
                                        "and pass" % audit)
            if audit == "mutations" and not mutations:
                # The token asserts a mutation property of the Archive; a case
                # with no mutation records cannot satisfy it. Presence alone is
                # not evidence (REQ-212, ticket #1025).
                self._invariant_failure(token, "structural audit 'mutations' found "
                                        "no mutation evidence to assert against")
            return
        if token in POLICY_EXTRACT_INVARIANTS:
            if operation != "extract":
                # Extract-only vocabulary; on another operation the claim names
                # nothing the verifier observed and never passes silently.
                self._invariant_failure(token, "extraction-time invariant declared "
                                        "on the '%s' operation" % operation)
            if observed != "not-run":
                self._invariant_failure(token, "extraction was exercised; "
                                        "containment cannot be asserted")
            return
        raise VerificationError(
            "unverified-invariant",
            "invariant '%s' is not part of the verifier vocabulary; it can never "
            "pass silently (grow the vocabulary with an assertion)" % token,
            stage="operation")

    def _assert_listed_count(self, token, details, expected):
        entry_count = details.get("entryCount")
        if entry_count is None:
            # The list operation produced no count; a declared count claim can
            # never pass without evidence (REQ-212, #1025).
            self._invariant_failure(token, "no list evidence was collected")
            return
        if expected == "entry-count":
            declared = details.get("declaredEntryCount")
            if declared is not None and entry_count != declared:
                self._invariant_failure(token, "listed %d entries, declared %d"
                                        % (entry_count, declared))
        else:
            try:
                expected_count = int(expected)
            except ValueError:
                # The predicate's \d+ branch already guarantees digits; guard a
                # pathological token that sneaks past the regex.
                self._invariant_failure(token, "'%s' is not a bounded entry count"
                                        % expected)
            if entry_count != expected_count:
                self._invariant_failure(token, "listed %d entries, expected %s"
                                        % (entry_count, expected))

    def _assert_write_completeness(self, token, operation, observed, details, file_count):
        """Assert a write-completeness claim against the success evidence. The
        ops only return their canonical success token after proving these
        properties (op_extract requires extractedFiles == declared file entries;
        op_read_entry only succeeds on full content-hash matches), so the checks
        below are the backstop that fires if that guarantee regresses. These
        tokens also ride list expectations, on which no entry-read evidence is
        recorded: nothing was written there, so they are vacuously satisfied."""
        success = OP_SUCCESS_TOKENS[operation]
        if observed != success:
            return
        if operation == "extract":
            extracted = details.get("extractedFiles")
            if extracted is not None and extracted != file_count:
                self._invariant_failure(token, "extracted %d of %d declared file entries"
                                        % (extracted, file_count))
            return
        entries_read = details.get("entriesRead")
        if not entries_read:
            return
        matched = details.get("hashMatches", 0)
        if matched != entries_read:
            self._invariant_failure(
                token, "only %d of %d read entries matched their content hashes"
                % (matched, entries_read))

    @staticmethod
    def _invariant_failure(token, detail):
        raise VerificationError("invariant",
                                "invariant '%s' not satisfied: %s" % (token, detail),
                                stage="operation")

    @staticmethod
    def _assert_failure_stage(operation, observed, stage, expectations):
        """When the observed outcome is a staged failure (the operation's
        failure token or a codec rejection the generator pairs with stages), its
        expectations must declare failure stages containing the observed stage;
        an absent declaration or prohibited stage fails the fixture with the
        expectation quoted (REQ-212, ticket #1025).

        Stages bind per profile, not across the whole operation. A multi-profile
        Expectation File (e.g. an archive whose members use different codecs) has
        one record per profile, and only the records that actually allow
        `observed` govern it — unioning stages over every profile would let a
        sibling profile's declaration mask a record that declares nothing.
        """
        if observed not in STAGE_CHECKED_OUTCOMES[operation]:
            return

        governing = [expectation for expectation in expectations
                     if observed in (expectation.get("allowedOutcomes") or [])]
        if not governing:
            governing = list(expectations)

        for expectation in governing:
            profile = expectation.get("profile", "?")
            declared = sorted(expectation.get("failureStages") or [])
            if not declared:
                raise VerificationError(
                    "failure-stage",
                    "observed failure '%s' for operation '%s' has no declared failure "
                    "stages in expectation %s[%s]"
                    % (observed, operation, operation, profile),
                    stage="operation")
            normalized = CANONICAL_FAILURE_STAGES.get(stage, stage)
            if stage not in declared and normalized not in declared:
                raise VerificationError(
                    "failure-stage",
                    "observed failure stage '%s' is outside the allowed stages %s for "
                    "expectation %s[%s]" % (stage, declared, operation, profile),
                    stage="operation")


def verify_orphan_hidden(case, zip_bytes):
    """Orphan hidden LFH audit (ticket #869, ZipDiff c1): a valid stored LFH for
    hidden.sh at offset 0 with no CDH; the central directory still indexes only
    the visible entries at shifted offsets. Returns a detail note on success."""
    import binascii
    import struct

    def need(length, what):
        if len(zip_bytes) < length:
            raise VerificationError("orphan-hidden", "archive too small for %s" % what)

    need(30, "a hidden LFH")
    if zip_bytes[0:4] != b"PK\x03\x04":
        raise VerificationError("orphan-hidden", "offset 0 is not a local file header")
    flags = struct.unpack_from("<H", zip_bytes, 6)[0]
    method = struct.unpack_from("<H", zip_bytes, 8)[0]
    crc = struct.unpack_from("<I", zip_bytes, 14)[0]
    name_len = struct.unpack_from("<H", zip_bytes, 26)[0]
    extra_len = struct.unpack_from("<H", zip_bytes, 28)[0]
    if flags != 0:
        raise VerificationError("orphan-hidden", "hidden entry flags are 0x%04x, expected 0" % flags)
    if method != 0:
        raise VerificationError("orphan-hidden", "hidden entry method is %d, expected stored (0)" % method)
    if extra_len != 0:
        raise VerificationError("orphan-hidden", "hidden entry extra length is %d, expected 0" % extra_len)
    need(30 + name_len, "the hidden name")
    name_bytes = zip_bytes[30:30 + name_len]
    if name_bytes != b"hidden.sh":
        raise VerificationError("orphan-hidden", "hidden name is %r, expected b'hidden.sh'" % name_bytes)
    comp_size, uncomp_size = struct.unpack_from("<II", zip_bytes, 18)
    if comp_size != uncomp_size:
        raise VerificationError("orphan-hidden", "hidden stored sizes disagree: %d vs %d" % (comp_size, uncomp_size))
    hidden_length = 30 + name_len + comp_size
    need(hidden_length, "the hidden payload")
    payload = zip_bytes[30 + name_len:30 + name_len + comp_size]
    if len(payload) != comp_size:
        raise VerificationError("orphan-hidden", "hidden payload truncated")
    if (binascii.crc32(payload) & 0xFFFFFFFF) != crc:
        raise VerificationError("orphan-hidden", "hidden CRC32 0x%08x does not match the payload" % crc)

    mutations = case.get("mutations") or []
    declared = mutations[0].get("declaredValue") if mutations else None
    if not declared or "hidden-lfh-offset=0" not in declared:
        raise VerificationError("orphan-hidden", "mutation record must declare hidden-lfh-offset=0")
    if ("hidden-name-hex=%s" % name_bytes.hex()) not in declared:
        raise VerificationError("orphan-hidden", "mutation record must declare the hidden raw name bytes")
    if ("hidden-payload-sha256=%s" % sha256_hex(payload)) not in declared:
        raise VerificationError("orphan-hidden", "mutation record must declare the hidden payload hash")

    # Pinned to the comment-free valid-deflate control: the EOCD is the final 22
    # bytes, located by length, never by signature search (signature-like payload
    # bytes are data). A future control with a comment must move this case to a
    # length-declared EOCD lookup instead of changing this offset.
    need(22, "an EOCD record")
    if zip_bytes[-22:-18] != b"PK\x05\x06":
        raise VerificationError("orphan-hidden", "EOCD signature missing at the expected offset")
    this_count, total_count, cd_size, cd_offset = struct.unpack_from("<HHII", zip_bytes, len(zip_bytes) - 22 + 8)
    if this_count != total_count or total_count != len(case["entries"]):
        raise VerificationError("orphan-hidden",
                                "EOCD counts %d/%d do not match the %d visible entries"
                                % (this_count, total_count, len(case["entries"])))
    if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
        raise VerificationError("orphan-hidden", "central directory does not start at the declared offset %d" % cd_offset)
    # Every CD-declared visible must point at a genuine LFH; the hidden offset
    # must not be indexed (that non-membership is the orphan). A naive full
    # signature scan is deliberately avoided: stored payloads may legally contain
    # signature-like bytes that are data, not headers.
    cursor = cd_offset
    seen_local_offsets = set()
    for _ in case["entries"]:
        if cursor + 46 > len(zip_bytes) or zip_bytes[cursor:cursor + 4] != b"PK\x01\x02":
            raise VerificationError("orphan-hidden", "central directory entry missing at offset %d" % cursor)
        name_length, extra_length, comment_length = struct.unpack_from("<HHH", zip_bytes, cursor + 28)
        local_offset = struct.unpack_from("<I", zip_bytes, cursor + 42)[0]
        if local_offset + 4 > len(zip_bytes) or zip_bytes[local_offset:local_offset + 4] != b"PK\x03\x04":
            raise VerificationError("orphan-hidden", "visible LFH missing at the declared offset %d" % local_offset)
        seen_local_offsets.add(local_offset)
        cursor += 46 + name_length + extra_length + comment_length
    if 0 in seen_local_offsets:
        raise VerificationError("orphan-hidden", "offset 0 is indexed by the central directory")
    if hidden_length not in seen_local_offsets:
        raise VerificationError("orphan-hidden",
                                "no visible LFH at the hidden length %d; shifts not applied"
                                % hidden_length)
    # CD-driven readers list only the visible entries; the hidden name is absent.
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as zf:
        names = [info.filename for info in zf.infolist()]
    if "hidden.sh" in names:
        raise VerificationError("orphan-hidden", "hidden.sh is indexed by the central directory")
    if len(names) != len(case["entries"]):
        raise VerificationError("orphan-hidden",
                                "central directory lists %d entries, expected %d visible entries"
                                % (len(names), len(case["entries"])))

    return "hidden=%d-bytes streaming-visible-only" % hidden_length


def verify_eocd_ambiguity(case, zip_bytes):
    """EOCD-selection audit (ticket #870): exactly two structurally complete
    central-directory/EOCD pairs select a.txt and b.bin respectively. The first
    EOCD's declared comment reaches EOF; Python's backward scan selects the
    trailing zero-comment shadow EOCD and reads b.bin."""
    import struct

    offsets = []
    cursor = 0
    while True:
        cursor = zip_bytes.find(b"PK\x05\x06", cursor)
        if cursor < 0:
            break
        offsets.append(cursor)
        cursor += 4
        if len(offsets) > 2:
            raise VerificationError("eocd-ambiguity",
                                    "expected exactly two EOCD signatures, found more")
    if len(offsets) != 2:
        raise VerificationError("eocd-ambiguity",
                                "expected exactly two EOCD signatures, found %d" % len(offsets))

    authentic_eocd, shadow_eocd = offsets
    if authentic_eocd >= shadow_eocd:
        raise VerificationError("eocd-ambiguity",
                                "the authentic EOCD must precede the shadow EOCD")

    def parse_pair(eocd_offset, label):
        if eocd_offset + 22 > len(zip_bytes):
            raise VerificationError("eocd-ambiguity", "%s EOCD is truncated" % label)
        disk, cd_disk, disk_entries, total_entries, cd_size, cd_offset, comment_length = \
            struct.unpack_from("<HHHHIIH", zip_bytes, eocd_offset + 4)
        if (disk, cd_disk, disk_entries, total_entries) != (0, 0, 1, 1):
            raise VerificationError("eocd-ambiguity",
                                    "%s EOCD fields are not single-disk one-entry values" % label)
        if cd_offset + cd_size != eocd_offset:
            raise VerificationError("eocd-ambiguity",
                                    "%s central directory does not end at its EOCD" % label)
        if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
            raise VerificationError("eocd-ambiguity",
                                    "%s central header missing at offset %d" % (label, cd_offset))
        name_length, extra_length, entry_comment_length = struct.unpack_from("<HHH", zip_bytes, cd_offset + 28)
        if 46 + name_length + extra_length + entry_comment_length != cd_size:
            raise VerificationError("eocd-ambiguity",
                                    "%s central-directory size does not match its one header" % label)
        central_name = zip_bytes[cd_offset + 46:cd_offset + 46 + name_length]

        # The pair is only complete when its central entry references a genuine
        # local header carrying the same name and the same method/CRC/sizes.
        local_offset = struct.unpack_from("<I", zip_bytes, cd_offset + 42)[0]
        if local_offset + 30 > len(zip_bytes) or zip_bytes[local_offset:local_offset + 4] != b"PK\x03\x04":
            raise VerificationError("eocd-ambiguity",
                                    "%s central entry references no local header at offset %d"
                                    % (label, local_offset))
        local_name_length, local_extra_length = struct.unpack_from("<HH", zip_bytes, local_offset + 26)
        if zip_bytes[local_offset + 30:local_offset + 30 + local_name_length] != central_name:
            raise VerificationError("eocd-ambiguity",
                                    "%s local and central names disagree" % label)
        method = struct.unpack_from("<H", zip_bytes, local_offset + 8)[0]
        central_method = struct.unpack_from("<H", zip_bytes, cd_offset + 10)[0]
        crc, compressed_size = struct.unpack_from("<II", zip_bytes, local_offset + 14)
        central_crc, central_compressed = struct.unpack_from("<II", zip_bytes, cd_offset + 16)
        if (method, crc, compressed_size) != (central_method, central_crc, central_compressed):
            raise VerificationError("eocd-ambiguity",
                                    "%s local header disagrees with its central header" % label)
        data_offset = local_offset + 30 + local_name_length + local_extra_length
        if data_offset + compressed_size > len(zip_bytes):
            raise VerificationError("eocd-ambiguity",
                                    "%s payload runs past the end of the Archive" % label)
        try:
            name = central_name.decode("utf-8")
        except UnicodeDecodeError:
            raise VerificationError("eocd-ambiguity",
                                    "%s central name is not valid UTF-8" % label) from None
        if method == 0:
            # Stored payload: bind the pair to the sidecar's content hash so a
            # retargeted central header cannot masquerade as the named entry.
            # (Deflated controls never reach this pinned stored case.)
            payload = zip_bytes[data_offset:data_offset + compressed_size]
            sidecar = next((e for e in case["entries"] if e["readableName"] == name), None)
            if sidecar is None:
                raise VerificationError("eocd-ambiguity",
                                        "%s pair names entry %r missing from the sidecar"
                                        % (label, name))
            if sha256_hex(payload) != sidecar.get("contentSha256"):
                raise VerificationError("eocd-ambiguity",
                                        "%s pair payload does not match sidecar %r" % (label, name))
        return cd_offset, comment_length, name

    authentic_cd, authentic_comment, authentic_name = parse_pair(authentic_eocd, "authentic")
    shadow_cd, shadow_comment, shadow_name = parse_pair(shadow_eocd, "shadow")
    if authentic_eocd + 22 + authentic_comment != len(zip_bytes):
        raise VerificationError("eocd-ambiguity",
                                "authentic EOCD comment does not end at EOF")
    if shadow_comment != 0 or shadow_eocd + 22 != len(zip_bytes):
        raise VerificationError("eocd-ambiguity",
                                "shadow EOCD must have zero comment and end at EOF")
    if (authentic_name, shadow_name) != ("a.txt", "b.bin"):
        raise VerificationError("eocd-ambiguity",
                                "EOCD pairs select %r/%r, expected a.txt/b.bin"
                                % (authentic_name, shadow_name))

    mutations = [m for m in case.get("mutations", [])
                 if m.get("code") == "eocdr-ambiguity-comment"]
    if len(mutations) != 1:
        raise VerificationError("eocd-ambiguity",
                                "expected one eocdr-ambiguity-comment mutation record")
    declared = {}
    for token in mutations[0].get("declaredValue", "").split():
        if "=" in token:
            key, value = token.split("=", 1)
            declared[key] = value
    expected_offsets = {
        "authentic-eocd-offset": authentic_eocd,
        "authentic-comment-length": authentic_comment,
        "shadow-central-directory-offset": shadow_cd,
        "shadow-eocd-offset": shadow_eocd,
    }
    for key, value in expected_offsets.items():
        if declared.get(key) != str(value):
            raise VerificationError("eocd-ambiguity",
                                    "mutation %s is %r, expected %d"
                                    % (key, declared.get(key), value))

    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as zf:
        infos = zf.infolist()
        if len(infos) != 1 or infos[0].filename != "b.bin":
            raise VerificationError("eocd-ambiguity",
                                    "Python zipfile must select only shadow entry b.bin")
        budget = case.get("limits", {}).get("expandedBytesBudget", MAX_EXPANDED_BYTES_BUDGET)
        chunks = []
        total = 0
        with zf.open(infos[0]) as stream:
            while True:
                chunk = stream.read(65536)
                if not chunk:
                    break
                total += len(chunk)
                if total > budget:
                    raise VerificationError("eocd-ambiguity",
                                            "shadow payload exceeds the expanded-bytes budget")
                chunks.append(chunk)
        shadow_bytes = b"".join(chunks)
    shadow_entry = next((entry for entry in case["entries"]
                         if entry["readableName"] == "b.bin"), None)
    if shadow_entry is None or sha256_hex(shadow_bytes) != shadow_entry.get("contentSha256"):
        raise VerificationError("eocd-ambiguity",
                                "Python zipfile shadow payload does not match b.bin")

    return ("authentic-eocd=%d authentic-cd=%d shadow-eocd=%d shadow-cd=%d "
            "python-selects=b.bin" %
            (authentic_eocd, authentic_cd, shadow_eocd, shadow_cd))


def verify_adls_segments(case):
    """ADLS Gen2 segment-depth audit (ticket #878): the single member's
    container-relative path carries exactly 63 segments (the legal
    hierarchical-namespace boundary) or 64 (the first violation); every
    segment is non-empty and free of relative markers. ADLS Gen2 counts the
    account and container on the wire, so account-relative depth is +2."""
    expected = {"path-adls-segments-boundary": 63,
                "path-adls-segments-exceeded": 64}.get(case["caseKey"])
    if expected is None:
        raise VerificationError("adls-segments",
                                "unexpected case key %r" % case["caseKey"])
    entries = case["entries"]
    if len(entries) != 1:
        raise VerificationError("adls-segments",
                                "expected exactly one entry, found %d" % len(entries))
    name = entries[0]["readableName"]
    segments = name.strip("/").split("/")
    if len(segments) != expected:
        raise VerificationError("adls-segments",
                                "entry name carries %d segments, expected %d"
                                % (len(segments), expected))
    for segment in segments:
        if not segment or segment in (".", ".."):
            raise VerificationError("adls-segments",
                                    "entry name has an empty or relative segment %r"
                                    % segment)
    return ("segments=%d container-relative (account-relative=%d on ADLS Gen2)"
            % (expected, expected + 2))


# Legacy-codec trail-byte cases (ticket #887): the exact raw name bytes, the
# named codec that decodes them, and the expected decoded name. The named
# encoding consumer is this verifier: it decodes the raw bytes with the codec
# and asserts separator handling only after decoding — the maintainer triage
# contract for keeping the suite out of the default compatibility set.
ENCODING_TRAIL_BYTE_CASES = {
    "encoding-cp932-trail-backslash": ("cp932", "955c2e747874", "表.txt"),
    "encoding-big5-trail-backslash": ("big5", "b35c2e747874", "許.txt"),
    "encoding-gbk-trail-backslash": ("gbk", "815c2e747874", "乗.txt"),
}


def verify_encoding_trail_bytes(case, zip_bytes):
    """Legacy-codec 0x5C trail-byte audit (ticket #887): the single member's
    raw name bytes carry 0x5C only as the named codec character's trail byte,
    bit 11 stays clear, and decoding the raw bytes with the named codec
    reproduces the expected name with no spurious directory split — separator
    handling is asserted after decoding, never on the raw bytes."""
    import codecs
    import struct

    spec = ENCODING_TRAIL_BYTE_CASES.get(case["caseKey"])
    if spec is None:
        raise VerificationError("encoding-trail-byte",
                                "unexpected case key %r" % case["caseKey"])
    codec, expected_hex, expected_name = spec
    entries = case["entries"]
    if len(entries) != 1:
        raise VerificationError("encoding-trail-byte",
                                "expected exactly one entry, found %d" % len(entries))
    record = entries[0]
    if record["localNameRaw"] != expected_hex or record["centralNameRaw"] != expected_hex:
        raise VerificationError("encoding-trail-byte",
                                "sidecar raw hex does not carry the %s trail-byte name" % codec)
    raw = bytes.fromhex(expected_hex)
    if raw[-4:] != b".txt" or raw[1:2] != b"\x5c":
        raise VerificationError("encoding-trail-byte",
                                "raw name bytes do not carry the 0x5C trail-byte shape")

    # Both headers carry the exact raw bytes (single-entry control: local at
    # offset 0, central located from the comment-free EOCD).
    if len(zip_bytes) < 30 or zip_bytes[0:4] != b"PK\x03\x04":
        raise VerificationError("encoding-trail-byte", "offset 0 is not a local file header")
    local_name_len = struct.unpack_from("<H", zip_bytes, 26)[0]
    if zip_bytes[30:30 + local_name_len] != raw:
        raise VerificationError("encoding-trail-byte", "raw local name is not the codec bytes")
    local_general_purpose_flag = struct.unpack_from("<H", zip_bytes, 6)[0]
    if local_general_purpose_flag & 0x0800:
        raise VerificationError("encoding-trail-byte", "bit 11 must stay clear in the local header")
    if len(zip_bytes) < 22 or zip_bytes[-22:-18] != b"PK\x05\x06":
        raise VerificationError("encoding-trail-byte", "EOCD signature missing at the expected offset")
    cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]
    if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
        raise VerificationError("encoding-trail-byte",
                                "central directory missing at offset %d" % cd_offset)
    general_purpose_flag = struct.unpack_from("<H", zip_bytes, cd_offset + 8)[0]
    if general_purpose_flag & 0x0800:
        raise VerificationError("encoding-trail-byte", "bit 11 must stay clear for the legacy-codec name")
    central_name_len = struct.unpack_from("<H", zip_bytes, cd_offset + 28)[0]
    if zip_bytes[cd_offset + 46:cd_offset + 46 + central_name_len] != raw:
        raise VerificationError("encoding-trail-byte", "raw central name is not the codec bytes")

    # Named-consumer oracle: decode the raw bytes with the codec, then assert
    # separator handling on the DECODED name (ticket #887 triage contract).
    try:
        decoded = codecs.decode(raw, codec)
    except (UnicodeDecodeError, LookupError) as error:
        raise VerificationError("encoding-trail-byte",
                                "the named %s consumer failed to decode the raw name: %s"
                                % (codec, error)) from error
    if decoded != expected_name:
        raise VerificationError("encoding-trail-byte",
                                "%s decoding produced %r, expected %r"
                                % (codec, decoded, expected_name))
    if "\\" in decoded:
        raise VerificationError("encoding-trail-byte",
                                "decoded name %r splits on the 0x5C trail byte" % decoded)
    if record["readableName"] != decoded:
        raise VerificationError("encoding-trail-byte",
                                "sidecar readableName %r does not equal the decoded %s name"
                                % (record["readableName"], codec))
    return ("%s decoded=%s no-spurious-split" % (codec, expected_name))


def verify_unicode_path_extra(case, zip_bytes):
    """Unicode Path audit (ticket #871): entry 0 carries a well-formed 0x7075
    subfield in both headers (version 1, CRC-32 of the standard header name),
    while the raw central-directory name stays the benign 'safe.txt'. The
    verified differential: readers that honor 0x7075 (Python zipfile) resolve
    the traversal name, readers that ignore it (.NET ZipArchive, covered by the
    C# suite) list the benign name."""
    import binascii
    import struct

    # Local header is first (offset 0 for this single-entry control).
    if len(zip_bytes) < 30:
        raise VerificationError("unicode-path", "archive too small for a local file header")
    if zip_bytes[0:4] != b"PK\x03\x04":
        raise VerificationError("unicode-path", "offset 0 is not a local file header")
    local_name_len = struct.unpack_from("<H", zip_bytes, 26)[0]
    if zip_bytes[30:30 + local_name_len] != b"safe.txt":
        raise VerificationError("unicode-path", "raw local name is not the benign 'safe.txt'")
    local_extra_len = struct.unpack_from("<H", zip_bytes, 28)[0]
    local_extra = zip_bytes[30 + local_name_len:30 + local_name_len + local_extra_len]
    # Central directory location from the EOCD (comment-free control).
    if len(zip_bytes) < 22 or zip_bytes[-22:-18] != b"PK\x05\x06":
        raise VerificationError("unicode-path", "EOCD signature missing at the expected offset")
    cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]
    if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
        raise VerificationError("unicode-path", "central directory missing at offset %d" % cd_offset)
    central_name_len = struct.unpack_from("<H", zip_bytes, cd_offset + 28)[0]
    central_extra_len = struct.unpack_from("<H", zip_bytes, cd_offset + 30)[0]
    raw_central_name = zip_bytes[cd_offset + 46:cd_offset + 46 + central_name_len]
    if raw_central_name != b"safe.txt":
        raise VerificationError("unicode-path", "raw central name is %r, expected b'safe.txt'" % raw_central_name)
    central_extra = zip_bytes[cd_offset + 46 + central_name_len:cd_offset + 46 + central_name_len + central_extra_len]

    for label, extra in (("local", local_extra), ("central", central_extra)):
        if len(extra) < 4 or extra[0:2] != b"\x75\x70":
            raise VerificationError("unicode-path", "%s header lacks the 0x7075 subfield" % label)
        size = struct.unpack("<H", extra[2:4])[0]
        if size + 4 != len(extra):
            raise VerificationError("unicode-path",
                                    "%s 0x7075 subfield is not the whole %d-byte extra area" % (label, len(extra)))
        if extra[4] != 1:
            raise VerificationError("unicode-path", "%s 0x7075 version is %d, expected 1" % (label, extra[4]))
        if struct.unpack("<I", extra[5:9])[0] != (binascii.crc32(b"safe.txt") & 0xFFFFFFFF):
            raise VerificationError("unicode-path", "%s 0x7075 CRC does not match the standard name" % label)
        if extra[9:] != b"../../escaped.txt":
            raise VerificationError("unicode-path", "%s 0x7075 Unicode name is %r" % (label, extra[9:]))

    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as zf:
        infos = zf.infolist()
    if len(infos) != 1:
        raise VerificationError("unicode-path", "central directory lists %d entries, expected 1" % len(infos))
    if infos[0].filename != "../../escaped.txt":
        raise VerificationError("unicode-path",
                                "a 0x7075-honoring reader must resolve '../../escaped.txt', got %r"
                                % infos[0].filename)

    return "0x7075-valid differential-honored-vs-ignored"


def verify_prefix_model(case, zip_bytes):
    """Prefix audit (ticket #873): the first 64 bytes carry no ZIP structural
    signature. The rebased case addresses its local header past the stub; the
    unrebased case still declares offset 0 inside the stub."""
    import struct

    stub = zip_bytes[:64]
    if len(stub) != 64:
        raise VerificationError("prefix-model", "archive smaller than the 64-byte stub")
    for signature in (b"PK\x03\x04", b"PK\x01\x02", b"PK\x05\x06", b"PK\x07\x08"):
        if signature in stub:
            raise VerificationError("prefix-model", "stub contains a structural signature")

    # Pinned to the comment-free deflate control: the EOCD is the final 22
    # bytes, located by length, never by signature search. A future control
    # with a comment must move these cases to a length-declared EOCD lookup.
    if len(zip_bytes) < 22 or zip_bytes[-22:-18] != b"PK\x05\x06":
        raise VerificationError("prefix-model", "EOCD signature missing at the expected offset")
    cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]

    if case["caseKey"] == "prefix-rebased":
        if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
            raise VerificationError("prefix-model", "central directory missing at offset %d" % cd_offset)
        local_offset = struct.unpack_from("<I", zip_bytes, cd_offset + 42)[0]
        if local_offset != 64:
            raise VerificationError("prefix-model",
                                    "rebased local header offset is %d, expected 64" % local_offset)
        if zip_bytes[64:68] != b"PK\x03\x04":
            raise VerificationError("prefix-model", "no local header at the rebased offset 64")
        return "prefix=64 rebased"
    if cd_offset < 0 or cd_offset > len(zip_bytes):
        raise VerificationError("prefix-model", "stale central directory offset %d outside the archive" % cd_offset)
    if cd_offset + 4 <= len(zip_bytes) and zip_bytes[cd_offset:cd_offset + 4] == b"PK\x01\x02":
        raise VerificationError("prefix-model", "stale central directory offset still parses at %d" % cd_offset)
    # The true central directory sits one stub length later and still declares
    # local header 0 (now inside the stub) — the desynchronization this case
    # exists to pin.
    true_cd = cd_offset + 64
    if true_cd + 46 > len(zip_bytes) or zip_bytes[true_cd:true_cd + 4] != b"PK\x01\x02":
        raise VerificationError("prefix-model", "shifted central directory missing at offset %d" % true_cd)
    stale_local = struct.unpack_from("<I", zip_bytes, true_cd + 42)[0]
    if stale_local != 0:
        raise VerificationError("prefix-model",
                                "stale local header offset is %d, expected 0" % stale_local)
    mutations = case.get("mutations") or []
    declared = mutations[0].get("declaredValue") if mutations else None
    if not declared or "prefix-length=64" not in declared or "rebased=false" not in declared:
        raise VerificationError("prefix-model", "mutation record must declare prefix-length=64 rebased=false")
    return "prefix=64 stale-offsets"


def verify_shared_payload_range(case, zip_bytes):
    """Shared-range audit (ticket #872): every central header claims local header
    offset 0, so N entries share one physical compressed range. The declared
    expansion (entries x content size) must stay within the stated budget."""
    import struct

    if len(zip_bytes) < 22 or zip_bytes[-22:-18] != b"PK\x05\x06":
        raise VerificationError("shared-range", "EOCD signature missing at the expected offset")
    this_count, total_count, cd_size, cd_offset = struct.unpack_from(
        "<HHII", zip_bytes, len(zip_bytes) - 22 + 8)
    if this_count != total_count or total_count != len(case["entries"]):
        raise VerificationError("shared-range",
                                "EOCD counts %d/%d do not match the %d declared entries"
                                % (this_count, total_count, len(case["entries"])))
    eocd_offset = len(zip_bytes) - 22
    if cd_offset + cd_size != eocd_offset:
        raise VerificationError("shared-range",
                                "central directory span %d+%d does not end at the EOCD offset %d"
                                % (cd_offset, cd_size, eocd_offset))
    if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
        raise VerificationError("shared-range", "central directory missing at offset %d" % cd_offset)
    if zip_bytes[0:4] != b"PK\x03\x04":
        raise VerificationError("shared-range", "offset 0 is not a local file header")
    local_comp_size, local_uncomp_size = struct.unpack_from("<II", zip_bytes, 18)
    local_name_len, local_extra_len = struct.unpack_from("<HH", zip_bytes, 26)

    cursor = cd_offset
    for ordinal in range(len(case["entries"])):
        if cursor + 46 > len(zip_bytes) or zip_bytes[cursor:cursor + 4] != b"PK\x01\x02":
            raise VerificationError("shared-range", "central entry missing at offset %d" % cursor)
        local_offset = struct.unpack_from("<I", zip_bytes, cursor + 42)[0]
        if local_offset != 0:
            raise VerificationError("shared-range",
                                    "entry %d claims local header at %d, expected the shared offset 0"
                                    % (ordinal, local_offset))
        comp_size, uncomp_size = struct.unpack_from("<II", zip_bytes, cursor + 20)
        if comp_size != local_comp_size or uncomp_size != local_uncomp_size:
            raise VerificationError("shared-range",
                                    "entry %d sizes %d/%d disagree with the shared local header %d/%d"
                                    % (ordinal, comp_size, uncomp_size, local_comp_size, local_uncomp_size))
        name_len, extra_len, comment_len = struct.unpack_from("<HHH", zip_bytes, cursor + 28)
        cursor += 46 + name_len + extra_len + comment_len
    if cursor != cd_offset + cd_size:
        raise VerificationError("shared-range",
                                "central directory walk ends at %d, expected %d (cd_offset+cd_size)"
                                % (cursor, cd_offset + cd_size))

    declared = sum(entry.get("contentSize", 0) for entry in case["entries"])
    budget = case["limits"]["expandedBytesBudget"]
    if declared > budget:
        raise VerificationError("shared-range",
                                "declared expansion %d exceeds the budget %d" % (declared, budget))
    factor = (declared // max(1, len(zip_bytes))) if zip_bytes else 0

    return "%d-entries-share-offset-0 amplification=%dx" % (len(case["entries"]), factor)


def verify_hostile_name(case, zip_bytes):
    """Hostile-name audit (ticket #876): one stored entry whose local and
    central raw name bytes equal the exact hostile hex recorded in the
    Expectation File. NUL truncates in Python list views; C0 bytes survive."""
    import struct

    expected = {
        "filename-null-byte": "7265706f72742e706466002e657865",
        "filename-c0-control": "0102036f72742e706466582e657865",
    }[case["caseKey"]]
    if len(case["entries"]) != 1:
        raise VerificationError("hostile-name", "expected exactly one entry record")
    record = case["entries"][0]
    if record["localNameRaw"] != expected or record["centralNameRaw"] != expected:
        raise VerificationError("hostile-name", "sidecar raw hex does not carry the hostile bytes")
    raw = bytes.fromhex(expected)
    if len(zip_bytes) < 30 or zip_bytes[0:4] != b"PK\x03\x04":
        raise VerificationError("hostile-name", "offset 0 is not a local file header")
    local_name_len = struct.unpack_from("<H", zip_bytes, 26)[0]
    if zip_bytes[30:30 + local_name_len] != raw:
        raise VerificationError("hostile-name", "raw local name is not the hostile bytes")
    if len(zip_bytes) < 22 or zip_bytes[-22:-18] != b"PK\x05\x06":
        raise VerificationError("hostile-name", "EOCD signature missing at the expected offset")
    cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]
    if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
        raise VerificationError("hostile-name", "central directory missing at offset %d" % cd_offset)
    central_name_len = struct.unpack_from("<H", zip_bytes, cd_offset + 28)[0]
    if zip_bytes[cd_offset + 46:cd_offset + 46 + central_name_len] != raw:
        raise VerificationError("hostile-name", "raw central name is not the hostile bytes")
    return "hostile=%s local-central-equal" % case["caseKey"]


def verify_coded_method(case, zip_bytes, expected):
    """Method-code audit for single-entry coded valid controls (tickets #897/#898):
    the one entry declares the expected code and version in both headers."""
    import struct

    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as zf:
        infos = zf.infolist()
    if len(infos) != len(case["entries"]):
        raise VerificationError("coded-method",
                                "central directory lists %d entries, expected %d"
                                % (len(infos), len(case["entries"])))
    if len(infos) != 1:
        raise VerificationError("coded-method",
                                "multi-entry coded audit needs a central-directory walk, found %d" % len(infos))
    if infos[0].compress_type != expected:
        raise VerificationError("coded-method", "zipfile lists method %d, expected %d" % (infos[0].compress_type, expected))
    if len(zip_bytes) < 10 or zip_bytes[0:4] != b"PK\x03\x04":
        raise VerificationError("coded-method", "offset 0 is not a local file header")
    if struct.unpack_from("<H", zip_bytes, 8)[0] != expected:
        raise VerificationError("coded-method", "local header method is not %d" % expected)
    expected_version = {9: 21, 12: 46}.get(expected, None)
    if expected_version is not None and struct.unpack_from("<H", zip_bytes, 4)[0] != expected_version:
        raise VerificationError("coded-method", "local header version is not %d" % expected_version)
    if len(zip_bytes) < 22 or zip_bytes[-22:-18] != b"PK\x05\x06":
        raise VerificationError("coded-method", "EOCD signature missing at the expected offset")
    cd_offset = struct.unpack_from("<I", zip_bytes, len(zip_bytes) - 22 + 16)[0]
    if cd_offset + 46 > len(zip_bytes) or zip_bytes[cd_offset:cd_offset + 4] != b"PK\x01\x02":
        raise VerificationError("coded-method", "central directory missing at offset %d" % cd_offset)
    if struct.unpack_from("<H", zip_bytes, cd_offset + 10)[0] != expected:
        raise VerificationError("coded-method", "central header method is not %d" % expected)
    if expected_version is not None and struct.unpack_from("<H", zip_bytes, cd_offset + 6)[0] != expected_version:
        raise VerificationError("coded-method", "central header version is not %d" % expected_version)

    return "method=%d both-headers" % expected


def verify_compression_methods(case, zip_bytes):
    """Audits wire compression method codes and generator payloadCodec (ticket #929).
    Asserts localHeaderMethod and centralDirectoryMethod match wire bytes when
    headers exist, and checks method/codec contracts for coded controls and mutations."""
    import struct

    # Scan central directory headers if EOCD is intact:
    cd_entries = []
    eocd_pos = zip_bytes.rfind(b"PK\x05\x06") if len(zip_bytes) >= 22 else -1
    if eocd_pos != -1 and eocd_pos + 22 <= len(zip_bytes):
        cd_start = struct.unpack_from("<I", zip_bytes, eocd_pos + 16)[0]
        curr = cd_start
        while curr + 46 <= len(zip_bytes) and zip_bytes[curr:curr + 4] == b"PK\x01\x02":
            cd_method = struct.unpack_from("<H", zip_bytes, curr + 10)[0]
            name_len = struct.unpack_from("<H", zip_bytes, curr + 28)[0]
            extra_len = struct.unpack_from("<H", zip_bytes, curr + 30)[0]
            comm_len = struct.unpack_from("<H", zip_bytes, curr + 32)[0]
            lh_offset = struct.unpack_from("<I", zip_bytes, curr + 42)[0]
            lh_method = None
            if lh_offset + 10 <= len(zip_bytes) and zip_bytes[lh_offset:lh_offset + 4] == b"PK\x03\x04":
                lh_method = struct.unpack_from("<H", zip_bytes, lh_offset + 8)[0]
            cd_entries.append((cd_method, lh_method, curr, lh_offset))
            curr += 46 + name_len + extra_len + comm_len

    for entry in case.get("entries", []):
        ordinal = entry.get("ordinal", -1)
        loc_method = entry.get("localHeaderMethod")
        cd_method = entry.get("centralDirectoryMethod")
        if loc_method is None or isinstance(loc_method, bool) or not isinstance(loc_method, int) or loc_method < 0 or loc_method > 65535:
            raise VerificationError("compression-methods",
                                    "entry %d localHeaderMethod must be uint16 (0..65535)" % ordinal)
        if cd_method is None or isinstance(cd_method, bool) or not isinstance(cd_method, int) or cd_method < 0 or cd_method > 65535:
            raise VerificationError("compression-methods",
                                    "entry %d centralDirectoryMethod must be uint16 (0..65535)" % ordinal)

        codec = entry.get("payloadCodec")
        kind = entry.get("kind")
        if kind == "file":
            if codec not in ("stored", "deflate", "deflate64", "bzip2", "unknown"):
                raise VerificationError("compression-methods",
                                        "entry %d file entry has invalid payloadCodec '%s'" % (ordinal, codec))
        elif kind == "directory":
            if codec is not None:
                raise VerificationError("compression-methods",
                                        "entry %d directory entry must not declare payloadCodec" % ordinal)

        loc_offset = entry.get("localHeaderOffset")
        if loc_offset is not None and isinstance(loc_offset, int) and 0 <= loc_offset and loc_offset + 10 <= len(zip_bytes):
            if zip_bytes[loc_offset:loc_offset + 4] == b"PK\x03\x04":
                wire_loc = struct.unpack_from("<H", zip_bytes, loc_offset + 8)[0]
                if loc_method != wire_loc:
                    raise VerificationError("compression-methods",
                                            "entry %d localHeaderMethod %d does not match wire %d"
                                            % (ordinal, loc_method, wire_loc))
        elif loc_offset is None and 0 <= ordinal < len(cd_entries) and cd_entries[ordinal][1] is not None:
            wire_loc = cd_entries[ordinal][1]
            if loc_method != wire_loc:
                raise VerificationError("compression-methods",
                                        "entry %d localHeaderMethod %d does not match wire %d"
                                        % (ordinal, loc_method, wire_loc))

        cd_offset = entry.get("centralDirectoryOffset")
        if cd_offset is not None and isinstance(cd_offset, int) and 0 <= cd_offset and cd_offset + 12 <= len(zip_bytes):
            if zip_bytes[cd_offset:cd_offset + 4] == b"PK\x01\x02":
                wire_cd = struct.unpack_from("<H", zip_bytes, cd_offset + 10)[0]
                if cd_method != wire_cd:
                    raise VerificationError("compression-methods",
                                            "entry %d centralDirectoryMethod %d does not match wire %d"
                                            % (ordinal, cd_method, wire_cd))
        elif cd_offset is None and 0 <= ordinal < len(cd_entries):
            wire_cd = cd_entries[ordinal][0]
            if cd_method != wire_cd:
                raise VerificationError("compression-methods",
                                        "entry %d centralDirectoryMethod %d does not match wire %d"
                                        % (ordinal, cd_method, wire_cd))

    case_key = case.get("caseKey")
    entries = case.get("entries", [])
    if case_key == "valid-bzip2" and entries:
        if entries[0]["localHeaderMethod"] != 12 or entries[0]["centralDirectoryMethod"] != 12 or entries[0]["payloadCodec"] != "bzip2":
            raise VerificationError("compression-methods", "valid-bzip2 entry must declare method 12 and payloadCodec bzip2")
    elif case_key == "valid-deflate64" and entries:
        if entries[0]["localHeaderMethod"] != 9 or entries[0]["centralDirectoryMethod"] != 9 or entries[0]["payloadCodec"] != "deflate64":
            raise VerificationError("compression-methods", "valid-deflate64 entry must declare method 9 and payloadCodec deflate64")
    elif case_key == "valid-deflate64-long-match" and entries:
        if entries[0]["localHeaderMethod"] != 9 or entries[0]["centralDirectoryMethod"] != 9 or entries[0]["payloadCodec"] != "deflate64":
            raise VerificationError("compression-methods", "valid-deflate64-long-match entry must declare method 9 and payloadCodec deflate64")
    elif case_key == "method-cross-deflate64-deflate" and entries:
        if entries[0]["localHeaderMethod"] != 9 or entries[0]["centralDirectoryMethod"] != 8 or entries[0]["payloadCodec"] != "deflate":
            raise VerificationError("compression-methods", "method-cross-deflate64-deflate must have local 9, central 8, codec deflate")
    elif case_key == "method-cross-bzip2-stored" and entries:
        if entries[0]["localHeaderMethod"] != 12 or entries[0]["centralDirectoryMethod"] != 0 or entries[0]["payloadCodec"] != "stored":
            raise VerificationError("compression-methods", "method-cross-bzip2-stored must have local 12, central 0, codec stored")
    elif case_key == "method-data-deflate-as-bzip2" and entries:
        if entries[0]["localHeaderMethod"] != 8 or entries[0]["centralDirectoryMethod"] != 8 or entries[0]["payloadCodec"] != "bzip2":
            raise VerificationError("compression-methods", "method-data-deflate-as-bzip2 must have headers 8, codec bzip2")
    elif case_key == "method-data-bzip2-as-stored" and entries:
        if entries[0]["localHeaderMethod"] != 12 or entries[0]["centralDirectoryMethod"] != 12 or entries[0]["payloadCodec"] != "stored":
            raise VerificationError("compression-methods", "method-data-bzip2-as-stored must have headers 12, codec stored")
    elif case_key == "method-local-central-mismatch" and entries:
        if entries[0]["localHeaderMethod"] != 8 or entries[0]["centralDirectoryMethod"] != 0 or entries[0]["payloadCodec"] != "stored":
            raise VerificationError("compression-methods", "method-local-central-mismatch must have local 8, central 0, codec stored")

    return "entries=%d" % len(entries)


def verify_mutations(case, zip_bytes, actual_sha):
    """Mutation chain audit: linkage, offset arithmetic, raw bytes, reconstruction.

    Records come in two shapes: parallel views (several field rewrites of one
    control → final transition, all sharing before/after hashes) and ordered
    chains (each step consumes the previous result). In-place patches whose
    inline hex is complete are inverted to reconstruct the control and verify
    its declared hash; truncations are linkage-checked only.
    """
    mutations = case["mutations"]
    if not mutations:
        return "no mutations"

    errors = []
    for index, mutation in enumerate(mutations):
        before_size = mutation.get("beforeSize")
        after_size = mutation.get("afterSize")
        deleted = mutation.get("deletedLength") or 0
        inserted = mutation.get("insertedLength") or 0
        if before_size is not None and after_size is not None \
                and after_size != before_size - deleted + inserted:
            errors.append("mutation %d (%s): afterSize %d does not equal beforeSize "
                          "%d - deleted %d + inserted %d"
                          % (index, mutation["code"], after_size, before_size,
                             deleted, inserted))
        bounds = before_size if before_size is not None else len(zip_bytes)
        offset = mutation["offset"]
        if offset < 0 or offset > bounds:
            errors.append("mutation %d (%s): offset %d outside the before-mutation "
                          "archive (0..%d)" % (index, mutation["code"], offset, bounds))
        if deleted > bounds - offset:
            errors.append("mutation %d (%s): deletedLength %d exceeds the bytes "
                          "available at offset %d"
                          % (index, mutation["code"], deleted, offset))

    # Final transition must land exactly on the published bytes.
    last = mutations[-1]
    if last.get("afterSize") is not None and last["afterSize"] != len(zip_bytes):
        errors.append("final mutation afterSize %d does not equal the published "
                      "size %d" % (last["afterSize"], len(zip_bytes)))
    if last.get("afterSha256") is not None and last["afterSha256"] != actual_sha:
        errors.append("final mutation afterSha256 does not equal the published "
                      "Archive SHA-256")

    # Targeted raw byte comparison at the final mutation offset.
    after_hex = last.get("afterHex")
    if after_hex:
        window = bytes.fromhex(after_hex)
        offset = last["offset"]
        if offset + len(window) > len(zip_bytes) \
                or zip_bytes[offset:offset + len(window)] != window:
            errors.append("final mutation afterHex does not match the published bytes "
                          "at offset %d" % offset)

    # Chain vs parallel shape.
    pairs = {(m.get("beforeSha256"), m.get("afterSha256")) for m in mutations}
    if len(pairs) == 1:
        shape = "parallel"
    else:
        shape = "chained"
        for index in range(1, len(mutations)):
            previous = mutations[index - 1]
            current = mutations[index]
            if previous.get("afterSha256") is not None \
                    and current.get("beforeSha256") is not None \
                    and previous["afterSha256"] != current["beforeSha256"]:
                errors.append("mutation chain break at step %d: beforeSha256 does not "
                              "equal the previous afterSha256" % index)
            if previous.get("afterSize") is not None \
                    and current.get("beforeSize") is not None \
                    and previous["afterSize"] != current["beforeSize"]:
                errors.append("mutation chain break at step %d: beforeSize does not "
                              "equal the previous afterSize" % index)

    # Control reconstruction by inverting in-place patches.
    reconstructable = all(
        m.get("beforeHex") and m.get("afterHex")
        and len(m["beforeHex"]) == len(m["afterHex"])
        and m.get("beforeSize") == m.get("afterSize")
        for m in mutations
    )
    note = shape
    if reconstructable and not errors:
        buf = bytearray(zip_bytes)
        for mutation in reversed(mutations):
            offset = mutation["offset"]
            after_window = bytes.fromhex(mutation["afterHex"])
            before_window = bytes.fromhex(mutation["beforeHex"])
            if bytes(buf[offset:offset + len(after_window)]) != after_window:
                errors.append("reconstruction mismatch at mutation %s: published bytes "
                              "do not carry the declared afterHex at offset %d"
                              % (mutation["code"], offset))
                break
            buf[offset:offset + len(after_window)] = before_window
        else:
            control = bytes(buf)
            if sha256_hex(control) != mutations[0].get("beforeSha256"):
                errors.append("reconstructed control does not match the declared "
                              "beforeSha256")
            else:
                note = "%s, control-reconstructed" % shape

    if errors:
        raise VerificationError("mutations", "; ".join(errors))
    return note


def enumerate_pairs(directory):
    """Exact flat pair enumeration; rejects unsafe names, orphans, extras, and over-budget folders."""
    problems = []
    entries = sorted(os.listdir(directory))
    json_ids = {}
    zip_ids = {}
    total_folder_bytes = 0
    for name in entries:
        path = os.path.join(directory, name)
        if os.path.isdir(path):
            problems.append("unexpected subdirectory '%s' in the fixture folder" % name)
            continue
        try:
            total_folder_bytes += os.path.getsize(path)
        except OSError:
            pass
        if not PAIR_NAME_RE.match(name):
            problems.append("unsafe or unexpected file name '%s'" % name)
            continue
        fixture_id, extension = name.rsplit(".", 1)
        target = json_ids if extension == "json" else zip_ids
        if fixture_id in target:
            problems.append("duplicate %s pair member for Fixture ID %s"
                            % (extension, fixture_id))
        target[fixture_id] = path

    if total_folder_bytes > MAX_FOLDER_TOTAL_BYTES:
        problems.append("total fixture folder size is %d bytes, over the 256 MiB budget"
                        % total_folder_bytes)

    for fixture_id in sorted(set(json_ids) | set(zip_ids)):
        if fixture_id not in json_ids:
            problems.append("Archive %s.zip has no Expectation File" % fixture_id)
        if fixture_id not in zip_ids:
            problems.append("Expectation File %s.json has no Archive" % fixture_id)

    pairs = [(json_ids[fixture_id], zip_ids[fixture_id])
             for fixture_id in sorted(set(json_ids) & set(zip_ids))]
    return pairs, problems


def default_report_path(fixture_dir):
    parent = os.path.dirname(os.path.abspath(fixture_dir)) or "."
    name = os.path.basename(os.path.abspath(fixture_dir))
    return os.path.join(parent, name + "-verification.json")


def verify_directory(fixture_dir, schema_path, ajv_command, platforms):
    """Verifies every pair in a directory; returns (report, exit_code)."""
    report = {
        "fixtureDirectory": os.path.abspath(fixture_dir),
        "adapter": "python-zipfile",
        "adapterVersion": "%s %s" % (sys.implementation.name, sys.version.split()[0]),
        "schema": os.path.abspath(schema_path),
        "platforms": sorted(platforms),
        "directoryProblems": [],
        "fixtures": [],
    }

    ajv = AjvValidator(schema_path, ajv_command)
    prerequisite = ajv.check_prerequisite()
    if prerequisite is not None:
        report["directoryProblems"].append(prerequisite)
        return report, 1

    pairs, problems = enumerate_pairs(fixture_dir)
    report["directoryProblems"] = problems
    if not pairs and not problems:
        report["directoryProblems"].append("no Expectation Files found in %s"
                                           % fixture_dir)
        return report, 1
    if any("over the 256 MiB budget" in p for p in problems):
        return report, 1

    total_read = 0
    total_subprocesses = 0
    failures = list(problems)
    for json_path, zip_path in pairs:
        verifier = FixtureVerifier(json_path, zip_path, ajv, platforms)
        result = verifier.verify()
        report["fixtures"].append(result)
        total_read += result.get("bytesRead", 0)
        total_subprocesses += result.get("subprocessCount", 0)
        if result["status"] != "pass":
            failures.append("%s (%s): %s" % (
                result.get("fixtureId") or os.path.basename(json_path),
                result.get("caseKey") or "unknown",
                result.get("failure") or "operation outcome outside the allowed set"))

    report["summary"] = {
        "fixtures": len(report["fixtures"]),
        "passed": sum(1 for f in report["fixtures"] if f["status"] == "pass"),
        "failed": sum(1 for f in report["fixtures"] if f["status"] == "fail"),
        "notRunOperations": sum(1 for f in report["fixtures"]
                                for op in f.get("operations", [])
                                if op.get("status") == "not-run"),
        "bytesRead": total_read,
        "subprocessCount": total_subprocesses,
    }
    return report, (1 if failures else 0)


def host_platforms():
    platforms = set()
    if os.name == "nt":
        platforms.add("windows")
    if sys.platform.startswith("linux"):
        # Ticket #888: the emoji ZWJ NAME_MAX case is evaluated on Linux hosts
        # (ext4/XFS/btrfs enforce the 255-byte component limit), mirroring the
        # Windows auto-detect above. macOS stays opt-in via ZIPPER_ATC_PLATFORMS.
        platforms.add("linux")
    platforms.update(p.strip() for p in
                     os.environ.get("ZIPPER_ATC_PLATFORMS", "").split(",") if p.strip())
    return platforms


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Independently verify published Archive Test Fixture pairs.")
    parser.add_argument("fixture_dir",
                        help="directory holding <fixtureId>.zip/.json pairs")
    parser.add_argument("--report", default=None,
                        help="report JSON path (default: sibling of the fixture directory)")
    parser.add_argument("--schema", default=DEFAULT_SCHEMA,
                        help="authoritative draft-07 JSON Schema path")
    parser.add_argument("--ajv", default=os.environ.get("ZIPPER_ATC_AJV", DEFAULT_AJV),
                        help="Ajv CLI command (default: pinned local ajv-cli via node; "
                             "a missing tool is a failure, not a skip)")
    args = parser.parse_args(argv)

    if not os.path.isdir(args.fixture_dir):
        print("not a directory: %s" % args.fixture_dir, file=sys.stderr)
        return 2

    report, exit_code = verify_directory(args.fixture_dir, args.schema, args.ajv,
                                         host_platforms())
    report_path = args.report or default_report_path(args.fixture_dir)
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2, sort_keys=True)
        handle.write("\n")

    for fixture in report["fixtures"]:
        if fixture["status"] != "pass":
            print("FAIL %s (%s): %s" % (fixture.get("fixtureId"),
                                        fixture.get("caseKey"),
                                        fixture.get("failure") or "operation outcome "
                                        "outside the allowed set"), file=sys.stderr)
    for problem in report["directoryProblems"]:
        print("FAIL directory: %s" % problem, file=sys.stderr)
    if exit_code == 0:
        summary = report.get("summary", {})
        print("verified %d fixture pair(s): %d passed, %d failed; report: %s"
              % (summary.get("fixtures", 0), summary.get("passed", 0),
                 summary.get("failed", 0), report_path))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
