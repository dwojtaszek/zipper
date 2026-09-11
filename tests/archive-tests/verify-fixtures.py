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
failure stage, invariant checks, and pass/fail/not-run per fixture.

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
DEFAULT_AJV = "npx --yes ajv-cli@5.0.0"
AJV_TIMEOUT_SECONDS = 120
READ_CHUNK = 64 * 1024

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
    started in its own session/process group so the kill reaches grandchildren
    (npx spawns node), not just the launcher.
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
    """Authoritative draft-07 validation through the pinned Ajv CLI (npx)."""

    def __init__(self, schema_path, command):
        self.schema_path = os.path.abspath(schema_path)
        self.command = shlex.split(command)
        self.resolved = self.command.copy()
        # Windows cannot exec 'npx' directly (npx.cmd); resolve via PATH.
        self.resolved[0] = shutil.which(self.command[0]) or self.command[0]

    def check_prerequisite(self):
        if not os.path.isfile(self.schema_path):
            return "missing prerequisite: schema file %s" % self.schema_path
        if shutil.which(self.command[0]) is None and not os.path.isfile(self.command[0]):
            return ("missing prerequisite: Ajv launcher '%s' not found on PATH "
                    "(expected the pinned Ajv CLI; a missing tool is a failure, "
                    "not a skipped check)" % self.command[0])
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

    # ---- structure checks (required for every fixture) ----

    def verify_structure(self, case, zip_bytes):
        checks = []

        # Ajv authoritative schema validation (subprocess, deadline-bounded).
        self.subprocess_count += 1
        self.ajv.validate(self.json_path)
        checks.append({"check": "schema", "status": "pass"})

        # Expectation File size budget.
        json_size = os.path.getsize(self.json_path)
        if json_size > case["limits"]["jsonBytesBudget"]:
            raise VerificationError("json-bytes-budget",
                                    "Expectation File is %d bytes, over the declared "
                                    "%d-byte budget" % (json_size,
                                                        case["limits"]["jsonBytesBudget"]))
        checks.append({"check": "json-bytes-budget", "status": "pass"})

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

        # Mutation chain linkage, offset arithmetic, and raw byte comparison.
        mutation_note = verify_mutations(case, zip_bytes, actual_sha)
        checks.append({"check": "mutations", "status": "pass", "detail": mutation_note})

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
                        with zf.open(info) as stream:
                            data = self._read_stream(stream, budget, counter)
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

    def applicable_expectations(self, case):
        """Expectations whose platform mark applies on this host (or is unset)."""
        applicable = {}
        for expectation in case["expectations"]:
            platform = expectation.get("platform")
            if platform is not None and platform not in self.platforms:
                continue
            applicable.setdefault(expectation["operation"], []).append(expectation)
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
    def _not_run(operation, reason):
        return {"operation": operation, "normalizedOutcome": "not-run",
                "failureStage": None, "details": {}, "status": "not-run",
                "reason": reason}

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
            with open(self.json_path, "rb") as handle:
                case = parse_json_strict(handle.read().decode("utf-8"))
            result["fixtureId"] = case.get("fixtureId")
            result["caseKey"] = case.get("caseKey")

            # Physical size pre-check before slurping: the schema imposes no
            # maximum on archive.physicalSize, so a giant tampered Archive must
            # be rejected on disk size, not after reading it into memory.
            physical_cap = max(16 * 1024 * 1024, case["archive"]["physicalSize"])
            if os.path.getsize(self.zip_path) > physical_cap:
                raise VerificationError(
                    "archive-size-sha",
                    "published Archive is %d bytes on disk, over the 16 MiB physical "
                    "budget" % os.path.getsize(self.zip_path))

            with open(self.zip_path, "rb") as handle:
                zip_bytes = handle.read()

            deadline = case["limits"]["deadlineSeconds"]
            self._check_deadline(deadline)
            result["structureChecks"] = self.verify_structure(case, zip_bytes)

            classification = case["classification"]
            budget = case["limits"]["expandedBytesBudget"]
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
                                       expectations[operation])
                self._check_deadline(deadline)

            if classification == "valid":
                if "extract" in expectations:
                    observed, details, stage = self.op_extract(zip_bytes, case, budget)
                    self._record_operation(ops, "extract", observed, details, stage,
                                           expectations["extract"])
                else:
                    ops.append(self._not_run("extract", "no expectation record"))
            else:
                # Ticket #845 step 8: policy-sensitive and malformed fixtures are
                # never extracted on ordinary hosts; extract stays not-run and no
                # containment claim is made from metadata inspection.
                ops.append(self._not_run("extract", "extraction not exercised for "
                                         "%s fixtures on ordinary hosts" % classification))
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

    def _record_operation(self, ops, operation, observed, details, stage, expectations):
        allowed, vocabulary = self.outcome_allowed(observed, operation, expectations)
        invariants = sorted({item for expectation in expectations
                             for item in expectation.get("invariants", [])})
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
    """Exact flat pair enumeration; rejects unsafe names, orphans, and extras."""
    problems = []
    entries = sorted(os.listdir(directory))
    json_ids = {}
    zip_ids = {}
    for name in entries:
        if os.path.isdir(os.path.join(directory, name)):
            problems.append("unexpected subdirectory '%s' in the fixture folder" % name)
            continue
        if not PAIR_NAME_RE.match(name):
            problems.append("unsafe or unexpected file name '%s'" % name)
            continue
        fixture_id, extension = name.rsplit(".", 1)
        target = json_ids if extension == "json" else zip_ids
        if fixture_id in target:
            problems.append("duplicate %s pair member for Fixture ID %s"
                            % (extension, fixture_id))
        target[fixture_id] = os.path.join(directory, name)

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
                        help="Ajv CLI command (default: pinned npx ajv-cli; "
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
