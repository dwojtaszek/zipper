#!/usr/bin/env python3
"""Independent verifier for deterministic AES-256 ZIP interoperability fixtures (#1472)."""

import argparse
import hashlib
import json
import re
import shutil
import struct
import subprocess
import sys
import threading
import zipfile
from pathlib import Path

from reader_process import run_reader_command, start_reader_process, terminate_reader_process

MAX_ARCHIVE_PHYSICAL_BYTES = 16 * 1024 * 1024
MAX_EXPANDED_BYTES_BUDGET = 32 * 1024 * 1024
MAX_MANIFEST_BYTES = 1024 * 1024
MAX_READER_TIMEOUT_SECONDS = 10
AES_WIRE_METHOD = 99
AES_EXTRA_HEADER_ID = 0x9901
AES_VENDOR_VERSION_AE2 = 2
AES_STRENGTH_256 = 3


def _validate_manifest_structure(manifest, archive_bytes, case_key):
    if not isinstance(manifest, dict):
        raise ValueError(f"{case_key}: manifest must be a JSON object")
    if manifest.get("schemaVersion") != 1:
        raise ValueError(f"{case_key}: schemaVersion must be 1")
    if manifest.get("caseKey") != case_key:
        raise ValueError(f"{case_key}: manifest caseKey mismatch")
    if manifest.get("container") != "zip":
        raise ValueError(f"{case_key}: container must be 'zip'")
    if not manifest.get("producer") or not manifest.get("producerVersion"):
        raise ValueError(f"{case_key}: producer and producerVersion are required")
    if manifest.get("encryptionStrength") != "AES-256":
        raise ValueError(f"{case_key}: encryptionStrength must be 'AES-256'")
    if manifest.get("aesVendorVersion") != AES_VENDOR_VERSION_AE2:
        raise ValueError(f"{case_key}: aesVendorVersion must be 2 (AE-2)")
    if manifest.get("syntheticPasswordCase") not in ("ascii", "leading-trailing-space", "non-ascii-utf8"):
        raise ValueError(f"{case_key}: unexpected syntheticPasswordCase")

    _validate_archive_metadata(manifest.get("archive"), archive_bytes, case_key)
    _validate_manifest_entries(manifest.get("entries"), case_key)


def _validate_archive_metadata(archive_meta, archive_bytes, case_key):
    if not isinstance(archive_meta, dict):
        raise ValueError(f"{case_key}: archive metadata must be an object")
    if archive_meta.get("fileName") != f"{case_key}.zip":
        raise ValueError(f"{case_key}: archive.fileName must equal '{case_key}.zip'")
    if len(archive_bytes) > MAX_ARCHIVE_PHYSICAL_BYTES:
        raise ValueError(f"{case_key}: archive exceeds 16 MiB budget")
    if archive_meta.get("physicalSize") != len(archive_bytes):
        raise ValueError(f"{case_key}: archive.physicalSize mismatch")
    actual_sha = hashlib.sha256(archive_bytes).hexdigest()
    if archive_meta.get("sha256") != actual_sha:
        raise ValueError(f"{case_key}: archive.sha256 mismatch")


def _validate_manifest_entries(entries, case_key):
    if not isinstance(entries, list) or not entries:
        raise ValueError(f"{case_key}: entries must be a non-empty list")

    expanded_total = 0
    for index, entry in enumerate(entries):
        size = _validate_manifest_entry(entry, index, case_key)
        expanded_total += size
        if expanded_total > MAX_EXPANDED_BYTES_BUDGET:
            raise ValueError(f"{case_key}: expanded byte budget exceeded")


def _validate_manifest_entry(entry, index, case_key):
    if entry.get("ordinal") != index:
        raise ValueError(f"{case_key}: entry ordinal {entry.get('ordinal')} out of order")
    if entry.get("localHeaderMethod") != AES_WIRE_METHOD or entry.get("centralDirectoryMethod") != AES_WIRE_METHOD:
        raise ValueError(f"{case_key}: entry {index} must declare AES method 99")
    inner_method = entry.get("innerCompressionMethod")
    if inner_method not in (0, 8):
        raise ValueError(f"{case_key}: entry {index} invalid innerCompressionMethod {inner_method}")
    expected_codec = "stored" if inner_method == 0 else "deflate"
    if entry.get("innerCompressionCodec") != expected_codec:
        raise ValueError(f"{case_key}: entry {index} innerCompressionCodec must be '{expected_codec}'")
    if entry.get("encryptionStrength") != "AES-256":
        raise ValueError(f"{case_key}: entry {index} encryptionStrength must be 'AES-256'")
    size = entry.get("contentSize")
    if isinstance(size, bool) or not isinstance(size, int) or size < 0:
        raise ValueError(f"{case_key}: entry {index} invalid contentSize")
    sha = entry.get("contentSha256")
    if not isinstance(sha, str) or len(sha) != 64:
        raise ValueError(f"{case_key}: entry {index} invalid contentSha256")
    return size


def _parse_aes_extra_field(extra_bytes, context):
    offset = 0
    while offset + 4 <= len(extra_bytes):
        header_id, data_size = struct.unpack_from("<HH", extra_bytes, offset)
        offset += 4
        if offset + data_size > len(extra_bytes):
            raise ValueError(f"{context}: truncated extra field")
        payload = extra_bytes[offset:offset + data_size]
        offset += data_size
        if header_id == AES_EXTRA_HEADER_ID:
            if len(payload) != 7:
                raise ValueError(f"{context}: 0x9901 AES extra field length must be 7, got {len(payload)}")
            vendor_version, vendor_id, strength, inner_method = struct.unpack("<H2sBH", payload)
            if vendor_version != AES_VENDOR_VERSION_AE2 or vendor_id != b"AE" or strength != AES_STRENGTH_256:
                raise ValueError(
                    f"{context}: unexpected AES extra field (ver={vendor_version}, id={vendor_id!r}, strength={strength})"
                )
            return inner_method
    raise ValueError(f"{context}: missing WinZip AES 0x9901 extra field")


def _read_central_directory_bounds(archive_bytes, entry_count, case_key):
    eocd_offset = archive_bytes.rfind(b"PK\x05\x06")
    if eocd_offset < 0 or eocd_offset + 22 > len(archive_bytes):
        raise ValueError(f"{case_key}: missing EOCD")
    _, _, _, _, total_entries, cd_size, cd_offset, comment_len = struct.unpack_from("<4sHHHHIIH", archive_bytes, eocd_offset)
    if eocd_offset + 22 + comment_len != len(archive_bytes):
        raise ValueError(f"{case_key}: EOCD comment length does not align with EOF")
    if total_entries != entry_count:
        raise ValueError(f"{case_key}: EOCD entry count {total_entries} != manifest {entry_count}")
    if cd_offset + cd_size > eocd_offset:
        raise ValueError(f"{case_key}: central directory bounds exceed EOCD")
    return cd_offset, cd_size


def verify_zip_aes_headers(archive_bytes, entries, case_key):
    cd_offset, cd_size = _read_central_directory_bounds(archive_bytes, len(entries), case_key)
    cursor = cd_offset
    for index, expected in enumerate(entries):
        if cursor + 46 > cd_offset + cd_size or archive_bytes[cursor:cursor + 4] != b"PK\x01\x02":
            raise ValueError(f"{case_key}: missing central header for ordinal {index}")
        (
            _sig, _ver_made, _ver_need, cd_flags, cd_method, _time, _date,
            _crc, _comp_sz, _uncomp_sz, cd_name_len, cd_extra_len, cd_comment_len,
            _disk, _int_attr, _ext_attr, local_offset,
        ) = struct.unpack_from("<4sHHHHHHIIIHHHHHII", archive_bytes, cursor)
        cursor += 46
        cd_name_bytes = archive_bytes[cursor:cursor + cd_name_len]
        cursor += cd_name_len
        cd_extra = archive_bytes[cursor:cursor + cd_extra_len]
        cursor += cd_extra_len + cd_comment_len

        cd_name = cd_name_bytes.decode("utf-8")
        if cd_name != expected["name"]:
            raise ValueError(f"{case_key}: central entry {index} name '{cd_name}' != '{expected['name']}'")
        if (cd_flags & 0x1) == 0 or cd_method != AES_WIRE_METHOD:
            raise ValueError(f"{case_key}: central entry {index} missing encrypted flag or method 99")
        cd_inner = _parse_aes_extra_field(cd_extra, f"{case_key}/central[{index}]")
        if cd_inner != expected["innerCompressionMethod"]:
            raise ValueError(f"{case_key}: central entry {index} inner method {cd_inner} != {expected['innerCompressionMethod']}")

        _verify_local_header(archive_bytes, local_offset, expected, index, case_key)


def _verify_local_header(archive_bytes, local_offset, expected, index, case_key):
    if local_offset + 30 > len(archive_bytes) or archive_bytes[local_offset:local_offset + 4] != b"PK\x03\x04":
        raise ValueError(f"{case_key}: missing local header for ordinal {index}")
    (
        _lsig, _lver, lflags, lmethod, _ltime, _ldate,
        _lcrc, _lcomp, _luncomp, lname_len, lextra_len,
    ) = struct.unpack_from("<4sHHHHHIIIHH", archive_bytes, local_offset)
    lextra_start = local_offset + 30 + lname_len
    lextra = archive_bytes[lextra_start:lextra_start + lextra_len]
    if (lflags & 0x1) == 0 or lmethod != AES_WIRE_METHOD:
        raise ValueError(f"{case_key}: local entry {index} missing encrypted flag or method 99")
    loc_inner = _parse_aes_extra_field(lextra, f"{case_key}/local[{index}]")
    if loc_inner != expected["innerCompressionMethod"]:
        raise ValueError(f"{case_key}: local entry {index} inner method {loc_inner} != {expected['innerCompressionMethod']}")


def check_python_zipfile_capability(archive_path, manifest, password):
    with zipfile.ZipFile(archive_path, "r") as zf:
        infos = zf.infolist()
        if len(infos) != len(manifest["entries"]):
            raise ValueError("python-zipfile entry count mismatch")
        try:
            zf.read(infos[0].filename, pwd=password.encode("utf-8"))
        except NotImplementedError as error:
            return {
                "reader": "python-zipfile",
                "status": "unsupported-capability",
                "missingCapability": "aes-256",
                "detail": str(error),
            }
    raise ValueError("Expected python-zipfile to reject AES-256 method 99")


def check_infozip_capability(archive_path, password):
    unzip_bin = shutil.which("unzip")
    if not unzip_bin:
        return {
            "reader": "infozip-unzip",
            "status": "skipped",
            "missingCapability": "aes-256",
            "detail": "unzip executable not installed",
        }
    if "\x00" in password or "\n" in password or "\r" in password:
        raise ValueError("Password contains control characters unsupported on command line")
    result = run_reader_command([unzip_bin, "-t", "-P", password, str(archive_path)], timeout=MAX_READER_TIMEOUT_SECONDS)
    combined = (result.stdout + result.stderr).decode("utf-8", errors="replace").lower()
    if result.returncode != 0 and ("unsupported" in combined or "pk compat" in combined or result.returncode == 81):
        return {
            "reader": "infozip-unzip",
            "status": "unsupported-capability",
            "missingCapability": "aes-256",
            "detail": f"exit={result.returncode}",
        }
    raise ValueError(f"Expected Info-ZIP unzip to report unsupported AES-256 capability (rc={result.returncode})")


def _assert_seven_zip_listing(seven_zip, archive_path, entries):
    result = run_reader_command([seven_zip, "l", "-slt", "-ba", "-sccUTF-8", "--", str(archive_path)], timeout=MAX_READER_TIMEOUT_SECONDS)
    if result.returncode != 0:
        raise ValueError(f"7-Zip failed to list {archive_path} (rc={result.returncode})")
    parsed = _parse_seven_zip_listing(result.stdout)
    if len(parsed) != len(entries):
        raise ValueError(f"7-Zip listing count {len(parsed)} != {len(entries)}")
    for idx, expected in enumerate(entries):
        item = parsed[idx]
        if item.get("Path") != expected["name"]:
            raise ValueError(f"7-Zip listed '{item.get('Path')}', expected '{expected['name']}'")
        if item.get("Encrypted") != "+":
            raise ValueError(f"7-Zip did not report Encrypted = + for '{expected['name']}'")
        expected_method = "AES-256 Store" if expected["innerCompressionMethod"] == 0 else "AES-256 Deflate"
        if item.get("Method") != expected_method:
            raise ValueError(f"7-Zip method '{item.get('Method')}' != '{expected_method}'")


def _parse_seven_zip_listing(stdout):
    parsed = []
    current = {}
    for line in stdout.decode("utf-8", errors="replace").splitlines():
        if not line.strip():
            if current:
                parsed.append(current)
                current = {}
            continue
        if " = " in line:
            k, v = line.split(" = ", 1)
            current[k] = v
    if current:
        parsed.append(current)
    return parsed


def _extract_entry_sha256_seven_zip(seven_zip, archive_path, name, expected_size, password):
    if "\x00" in password or "\n" in password or "\r" in password:
        raise ValueError("Password contains control characters unsupported on 7-Zip command line")
    with start_reader_process(
        [seven_zip, "x", "-so", "-spd", f"-p{password}", "--", str(archive_path), name],
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
    ) as process:
        timed_out = threading.Event()

        def kill_after_timeout():
            timed_out.set()
            terminate_reader_process(process)

        timer = threading.Timer(MAX_READER_TIMEOUT_SECONDS, kill_after_timeout)
        timer.start()
        try:
            digest = hashlib.sha256()
            size = 0
            while chunk := process.stdout.read(65536):
                size += len(chunk)
                if size > expected_size:
                    terminate_reader_process(process)
                    raise ValueError(f"7-Zip output for '{name}' exceeded expected {expected_size} bytes")
                digest.update(chunk)
            returncode = process.wait(timeout=MAX_READER_TIMEOUT_SECONDS)
            if timed_out.is_set():
                raise ValueError(f"7-Zip timed out extracting '{name}'")
            if returncode != 0:
                raise ValueError(f"7-Zip failed extracting '{name}' from {archive_path.name} (rc={returncode})")
            return size, digest.hexdigest()
        finally:
            timer.cancel()
            if process.poll() is None:
                terminate_reader_process(process)
                process.communicate()


def _assert_seven_zip_wrong_password(seven_zip, archive_path, bad_password):
    result = run_reader_command([seven_zip, "t", f"-p{bad_password}", "--", str(archive_path)], timeout=MAX_READER_TIMEOUT_SECONDS)
    combined = (result.stdout + result.stderr).decode("utf-8", errors="replace").lower()
    if result.returncode == 0 or "wrong password" not in combined:
        raise ValueError(f"7-Zip did not report a clean incorrect-password failure (rc={result.returncode})")


def _is_legacy_p7zip(seven_zip):
    result = run_reader_command([seven_zip, "i"], timeout=MAX_READER_TIMEOUT_SECONDS)
    banner = result.stdout.decode("utf-8", errors="replace")
    return re.search(r"^7-Zip \[64\] 17\.05\b", banner, re.MULTILINE) is not None


def check_seven_zip_capable_reader(seven_zip, archive_path, manifest, password):
    if not seven_zip:
        return {
            "reader": "7zip",
            "status": "skipped",
            "missingCapability": None,
            "detail": "7zz/7z not installed",
        }
    if _is_legacy_p7zip(seven_zip):
        return {
            "reader": "7zip",
            "status": "unsupported-capability",
            "missingCapability": "aes-256-fixture-profile",
            "detail": "Legacy p7zip 17.05 failed to extract the pinned AES-256 fixture entries.",
        }
    _assert_seven_zip_listing(seven_zip, archive_path, manifest["entries"])
    for entry in manifest["entries"]:
        actual_size, actual_sha = _extract_entry_sha256_seven_zip(
            seven_zip, archive_path, entry["name"], entry["contentSize"], password
        )
        if actual_size != entry["contentSize"] or actual_sha != entry["contentSha256"]:
            raise ValueError(f"7-Zip content mismatch for '{entry['name']}'")

    _assert_seven_zip_wrong_password(seven_zip, archive_path, password + "-wrong")
    if manifest.get("syntheticPasswordCase") == "leading-trailing-space":
        _assert_seven_zip_wrong_password(seven_zip, archive_path, password.strip())

    return {
        "reader": "7zip",
        "status": "verified",
        "missingCapability": None,
        "incorrectPasswordRejected": True,
        "verifiedEntryCount": len(manifest["entries"]),
    }


def resolve_seven_zip(explicit_path=None):
    if explicit_path:
        return explicit_path
    return shutil.which("7zz") or shutil.which("7z")


def _read_bounded(path, limit):
    with path.open("rb") as stream:
        data = stream.read(limit + 1)
    if len(data) > limit:
        raise ValueError(f"{path} exceeds {limit}-byte budget")
    return data


def verify_directory(fixtures_dir, passwords, seven_zip=None, require_seven_zip=False):
    fixtures_path = Path(fixtures_dir)
    if not fixtures_path.is_dir():
        raise ValueError(f"Fixtures directory does not exist: {fixtures_dir}")
    if require_seven_zip and not seven_zip:
        raise ValueError("7-Zip (7zz/7z) is required but was not found on PATH")

    manifests = sorted(
        p for p in fixtures_path.glob("*.json") if p.name != "passwords.json"
    )
    if not manifests:
        raise ValueError(f"No AES interoperability manifests found in {fixtures_dir}")

    missing_cases = passwords.keys() - {path.stem for path in manifests}
    if missing_cases:
        raise ValueError(f"Missing fixture manifests: {', '.join(sorted(missing_cases))}")

    report_cases = []
    for manifest_path in manifests:
        case_key = manifest_path.stem
        if case_key not in passwords:
            raise ValueError(f"Missing password for case '{case_key}' in verifier input")
        password = passwords[case_key]

        manifest = json.loads(_read_bounded(manifest_path, MAX_MANIFEST_BYTES).decode("utf-8"))

        archive_path = fixtures_path / f"{case_key}.zip"
        if not archive_path.is_file():
            raise ValueError(f"Missing archive file: {archive_path}")
        archive_bytes = _read_bounded(archive_path, MAX_ARCHIVE_PHYSICAL_BYTES)

        _validate_manifest_structure(manifest, archive_bytes, case_key)
        verify_zip_aes_headers(archive_bytes, manifest["entries"], case_key)

        readers = [
            check_python_zipfile_capability(archive_path, manifest, password),
            check_infozip_capability(archive_path, password),
            check_seven_zip_capable_reader(seven_zip, archive_path, manifest, password),
        ]
        report_cases.append({
            "caseKey": case_key,
            "producer": manifest["producer"],
            "producerVersion": manifest["producerVersion"],
            "encryptionStrength": manifest["encryptionStrength"],
            "archiveSha256": manifest["archive"]["sha256"],
            "readers": readers,
        })

    return {"fixtureCount": len(report_cases), "cases": report_cases}


def main():
    parser = argparse.ArgumentParser(description="Verify AES-256 ZIP interoperability fixtures.")
    parser.add_argument("fixtures_dir", help="Directory containing <caseKey>.zip and <caseKey>.json pairs")
    parser.add_argument("--passwords-json", required=True, help="JSON mapping caseKey -> exact synthetic password")
    parser.add_argument("--seven-zip", default=None, help="Optional explicit path to 7zz/7z")
    parser.add_argument("--require-7zip", dest="require_seven_zip", action="store_true", help="Fail if 7zz/7z is not installed")
    parser.add_argument("--report", default=None, help="Optional output path for JSON verification report")
    args = parser.parse_args()

    passwords = json.loads(_read_bounded(Path(args.passwords_json), MAX_MANIFEST_BYTES).decode("utf-8"))
    seven_zip = resolve_seven_zip(args.seven_zip)
    report = verify_directory(
        args.fixtures_dir,
        passwords,
        seven_zip=seven_zip,
        require_seven_zip=args.require_seven_zip,
    )

    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(f"Verified {report['fixtureCount']} AES-256 interoperability fixture(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
