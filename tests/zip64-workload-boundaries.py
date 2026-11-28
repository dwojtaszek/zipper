#!/usr/bin/env python3
"""Exercise the real ZIP workload CLI and verify its records with Python's independent reader."""

import argparse
import hashlib
import os
import shutil
import struct
import sys
import tempfile
import time
import zipfile
from pathlib import Path

SEED = "42"
ZIP32_SIZE_LIMIT = 1 << 32
ZIP32_COUNT_SENTINEL = (1 << 16) - 1
ZIP32_SIZE_SENTINEL = ZIP32_SIZE_LIMIT - 1
COUNTS = (9_999, 10_000, 10_001, 65_534, 65_535, 65_536)
LARGE_COUNTS = (100_000, 1_000_000)
# Seed 42 yields these bytes at the sampled ordinals, independently of Archive size.
COUNT_SAMPLES = dict(zip(
    COUNTS + LARGE_COUNTS,
    bytes.fromhex("e9 73 6d 12 20 4d 44 71"),
    strict=True,
))
SIZE_CASES = {
    "deflate-before": (ZIP32_SIZE_LIMIT - 1, "deflate"),
    "deflate-exact": (ZIP32_SIZE_LIMIT, "deflate"),
    "deflate-after": (ZIP32_SIZE_LIMIT + 1, "deflate"),
    "stored-exact": (ZIP32_SIZE_LIMIT, "store"),
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_directory(stream, expected_count):
    stream.seek(-22, os.SEEK_END)
    sig, disk, first_disk, disk_count, total_count, length, offset, comment_size = struct.unpack(
        "<4s4H2IH", stream.read(22),
    )
    require(sig == b"PK\x05\x06" and disk == first_disk == comment_size == 0, "Invalid EOCD")
    require(disk_count == total_count == min(expected_count, ZIP32_COUNT_SENTINEL), "EOCD count mismatch")

    locator_offset = stream.tell() - 42
    stream.seek(locator_offset)
    locator = stream.read(20)
    if not locator.startswith(b"PK\x06\x07"):
        require(expected_count <= ZIP32_COUNT_SENTINEL, "Missing ZIP64 locator for entry count")
        require(length + offset == locator_offset + 20, "Classic central directory span mismatch")
        return offset, "classic"

    _, locator_disk, end_offset, disks = struct.unpack("<4sIQI", locator)
    require(locator_disk == 0 and disks == 1, "Invalid ZIP64 locator")
    stream.seek(end_offset)
    sig, size, _, _, disk, first_disk, disk_count, total_count, full_length, full_offset = struct.unpack(
        "<4sQ2H2I4Q", stream.read(56),
    )
    require(sig == b"PK\x06\x06" and disk == first_disk == 0, "Invalid ZIP64 EOCD")
    require(disk_count == total_count == expected_count, "ZIP64 count mismatch")
    require(end_offset + size + 12 == locator_offset, "ZIP64 EOCD span mismatch")
    require(full_length + full_offset == end_offset, "ZIP64 central directory span mismatch")
    require(length == min(full_length, ZIP32_SIZE_SENTINEL), "Central directory length mismatch")
    require(offset == min(full_offset, ZIP32_SIZE_SENTINEL), "Central directory offset mismatch")
    return full_offset, "ZIP64"


def verify_count(path, count):
    with path.open("rb") as stream:
        _, representation = read_directory(stream, count)
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        require(len(entries) == count, "Independent inventory count mismatch")
        for ordinal, entry in enumerate(entries, 1):
            require(entry.filename == f"{ordinal:08}.bin", f"Missing, duplicate, or out-of-order entry {ordinal}")
            require(
                entry.file_size == entry.compress_size == 1 and entry.compress_type == zipfile.ZIP_STORED,
                f"Expected one Stored byte in entry {ordinal}",
            )
        for ordinal, expected in COUNT_SAMPLES.items():
            if ordinal <= count:
                digest = hashlib.sha256(archive.read(entries[ordinal - 1])).hexdigest()
                require(digest == hashlib.sha256(bytes([expected])).hexdigest(), f"Payload mismatch at {ordinal}")
                print(f"entry={ordinal} sha256={digest}")
    print(f"count={count} directory_representation={representation}")


def verify_central_entry(stream, expected_values):
    header = stream.read(46)
    require(header.startswith(b"PK\x01\x02"), "Invalid central directory entry")
    compressed, expanded, name_length, extra_length, comment_length = struct.unpack_from("<IIHHH", header, 20)
    offset, = struct.unpack_from("<I", header, 42)
    stream.seek(name_length, os.SEEK_CUR)
    extra = stream.read(extra_length)
    stream.seek(comment_length, os.SEEK_CUR)

    verify_extended_fields((expanded, compressed, offset), expected_values, extra)


def verify_extended_fields(fields, expected_values, extra):
    extended = []
    for field, expected in zip(fields, expected_values, strict=True):
        # A writer may extend both sizes when either exceeds the 32-bit range.
        if field == ZIP32_SIZE_SENTINEL and extra:
            extended.append(expected)
        else:
            require(field == expected, f"Unexpected 32-bit field {field} for {expected}")
    if extended:
        extra_id, size = struct.unpack_from("<HH", extra)
        require(extra_id == 1 and size == 8 * len(extended), "Invalid ZIP64 extended information")
        require(struct.unpack_from(f"<{len(extended)}Q", extra, 4) == tuple(extended), "ZIP64 field mismatch")



def verify_local_entry(stream, entry):
    stream.seek(entry.header_offset)
    header = stream.read(30)
    require(header.startswith(b"PK\x03\x04"), f"Invalid local header for {entry.filename}")
    flags, codec = struct.unpack_from("<HH", header, 6)
    crc, compressed, expanded, name_length, extra_length = struct.unpack_from("<IIIHH", header, 14)
    require(flags == entry.flag_bits and codec == entry.compress_type, "Local flags or method mismatch")
    stream.seek(name_length, os.SEEK_CUR)
    extra = stream.read(extra_length)
    if not flags & 8:
        require(crc == entry.CRC, "Local CRC mismatch")
        verify_extended_fields((expanded, compressed), (entry.file_size, entry.compress_size), extra)
        return

    require(crc == compressed == expanded == 0, "Unexpected local data-descriptor placeholders")
    stream.seek(entry.compress_size, os.SEEK_CUR)
    crc, = struct.unpack("<I", stream.read(4))
    if crc == 0x08074B50 and crc != entry.CRC:
        crc, = struct.unpack("<I", stream.read(4))
    width = "<2Q" if max(entry.file_size, entry.compress_size) > ZIP32_SIZE_SENTINEL else "<2I"
    sizes = struct.unpack(width, stream.read(struct.calcsize(width)))
    require(crc == entry.CRC and sizes == (entry.compress_size, entry.file_size), "Data descriptor mismatch")


def expected_payload_hash(size, value):
    digest = hashlib.sha256()
    chunk = bytes([value]) * (1 << 20)
    whole, tail = divmod(size, len(chunk))
    for _ in range(whole):
        digest.update(chunk)
    digest.update(chunk[:tail])
    return digest.hexdigest()


def size_recipe(case):
    if case == "stored-offset":
        half = ZIP32_SIZE_LIMIT // 2
        return [f"2:{half}:repeat:store", "1:1:repeat:store"], (half, half, 1), zipfile.ZIP_STORED
    size, method = SIZE_CASES[case]
    codec = zipfile.ZIP_STORED if method == "store" else zipfile.ZIP_DEFLATED
    return [f"1:{size}:repeat:{method}"], (size,), codec


def verify_size(path, case):
    _, sizes, codec = size_recipe(case)
    with path.open("rb") as stream, zipfile.ZipFile(path) as archive:
        offset, representation = read_directory(stream, len(sizes))
        stream.seek(offset)
        for ordinal, (entry, size, value) in enumerate(
            zip(archive.infolist(), sizes, (0xAB, 0x24, 0x20)[:len(sizes)], strict=True), 1,
        ):
            require(entry.filename == f"{ordinal:08}.bin" and entry.file_size == size, "Member inventory mismatch")
            require(entry.compress_type == codec, f"Unexpected compression method for {entry.filename}")
            if codec == zipfile.ZIP_STORED:
                require(entry.compress_size == size, f"Stored size mismatch for {entry.filename}")
            if case == "stored-offset" and ordinal == 3:
                require(entry.header_offset >= ZIP32_SIZE_LIMIT, "Final header did not cross 4 GiB")
            verify_central_entry(stream, (size, entry.compress_size, entry.header_offset))
            next_entry = stream.tell()
            verify_local_entry(stream, entry)
            stream.seek(next_entry)
            with archive.open(entry) as payload:
                actual = hashlib.file_digest(payload, "sha256").hexdigest()
            require(actual == expected_payload_hash(size, value), f"Payload hash mismatch for {entry.filename}")
            print(f"member={entry.filename} size={size} compressed={entry.compress_size} "
                  f"local_header_offset={entry.header_offset} sha256={actual}")
        print(f"case={case} directory_representation={representation}")


def timed_run(phase, command):
    started = time.monotonic()
    child = os.posix_spawnp(command[0], command, os.environ)
    _, status, usage = os.wait4(child, 0)
    require(os.waitstatus_to_exitcode(status) == 0, f"{phase} failed")
    print(f"{phase} seconds={time.monotonic() - started:.3f} peak_rss_kib={usage.ru_maxrss}", flush=True)


def run_cases(executable, mode):
    if mode == "size":
        cases = [(name, size_recipe(name)[0]) for name in (*SIZE_CASES, "stored-offset")]
    else:
        counts = LARGE_COUNTS if mode == "scale" else COUNTS
        cases = [(str(count), [f"{count}:1:repeat:store"]) for count in counts]

    with tempfile.TemporaryDirectory(prefix="zipper-zip64-") as directory:
        if mode == "size":
            require(shutil.disk_usage(directory).free > ZIP32_SIZE_LIMIT + (1 << 20),
                    "The physical size recipes require at least 4 GiB plus 1 MiB of free space")
        for name, groups in cases:
            archive = Path(directory) / f"{name}.zip"
            print(f"case={name} seed={SEED}", flush=True)
            timed_run("generation", [executable, "--zip-workload", str(archive), SEED, *groups])
            print(f"physical_bytes={archive.stat().st_size}", flush=True)
            timed_run("verification", [sys.executable, __file__, "--verify", mode, name, str(archive)])
            archive.unlink()


def main():
    if len(sys.argv) == 5 and sys.argv[1] == "--verify":
        _, _, mode, case, archive = sys.argv
        if mode == "size":
            verify_size(Path(archive), case)
        else:
            verify_count(Path(archive), int(case))
        return

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", help="Path to the built Zipper executable")
    parser.add_argument("mode", choices=("count", "scale", "size"))
    args = parser.parse_args()
    run_cases(str(Path(args.executable).resolve()), args.mode)


if __name__ == "__main__":
    main()
