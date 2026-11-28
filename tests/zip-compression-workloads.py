#!/usr/bin/env python3
"""Compare Store and Deflate with matched expanded bytes and report JSON Lines."""

import argparse
import codecs
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

from zip_workload_metrics import measure_process

SEED = 42
PAYLOAD_BYTES = 16 << 20
PAYLOAD_SHA256 = "7d685668f2b2e43256633ced219dbdbdfb3a6af631316af09f1049b2c3db1686"
TINY_COUNT = 50_000
DAT_ROWS = 695_000
LAYOUTS = {
    "single": ((1, PAYLOAD_BYTES),),
    "even": ((256, PAYLOAD_BYTES // 256),),
    "large-plus-tiny": ((1, PAYLOAD_BYTES - TINY_COUNT), (TINY_COUNT, 1)),
}
METHODS = {"store": zipfile.ZIP_STORED, "deflate": zipfile.ZIP_DEFLATED}
LOG_STDERR = [(os.POSIX_SPAWN_DUP2, 2, 1)]


def hash_with_unzip(path, member_name):
    with subprocess.Popen(["unzip", "-p", path, member_name], stdout=subprocess.PIPE) as reader:
        digest = hashlib.file_digest(reader.stdout, "sha256").hexdigest()
        if reader.wait():
            raise ValueError(f"Independent reader failed for {member_name}")
    return digest


def verify_archive(path, source_name, groups, method):
    digest = hashlib.sha256()
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if len(entries) != sum(count for count, _ in groups):
            raise ValueError("Unexpected ZIP member count")
        expected_sizes = (size for count, size in groups for _ in range(count))
        for ordinal, (entry, expected_size) in enumerate(zip(entries, expected_sizes, strict=True), 1):
            name = source_name or f"{ordinal:08}.bin"
            if (entry.filename, entry.file_size, entry.compress_type) != (name, expected_size, METHODS[method]):
                raise ValueError(f"ZIP member {ordinal} did not match the recipe")
            if source_name:
                return hash_with_unzip(path, name)
            with archive.open(entry) as payload:
                for chunk in iter(lambda: payload.read(1 << 20), b""):
                    digest.update(chunk)
    return digest.hexdigest()


def archive_file(source, destination, name, method):
    with zipfile.ZipFile(destination, "x", compression=METHODS[method]) as archive:
        archive.write(source, name)


def measure_archive(archive, label, method, groups, command, source_name=None):
    digest_path = archive.with_suffix(".sha256")
    generation = measure_process(command, file_actions=LOG_STDERR)
    verification = measure_process([
        sys.executable, __file__, "--verify", str(archive), json.dumps(source_name),
        json.dumps(groups), method, str(digest_path),
    ])
    record = {
        "layout": label,
        "seed": SEED,
        "method": method,
        "members": sum(count for count, _ in groups),
        "expanded_bytes": sum(count * size for count, size in groups),
        "zip_bytes": archive.stat().st_size,
        "sha256": digest_path.read_text(encoding="ascii"),
        "generation": generation,
        "verification": verification,
    }
    archive.unlink()
    digest_path.unlink()
    return record


def report_matching(records, expected):
    for record in records:
        if record["sha256"] != expected:
            raise ValueError(f"Expanded content differs for {record['layout']} {record['method']}")
    for record in records:
        print(json.dumps(record), flush=True)


def benchmark_layouts(root, executable):
    records = []
    for label, groups in LAYOUTS.items():
        for method in METHODS:
            archive = root / f"{label}-{method}.zip"
            recipe = [f"{count}:{size}:binary:{method}" for count, size in groups]
            command = [executable, "--zip-workload", str(archive), str(SEED), *recipe]
            records.append(measure_archive(archive, label, method, groups, command))
    report_matching(records, PAYLOAD_SHA256)


def generate_dat(root, executable):
    subprocess.run([
        executable, "--loadfile-only", "--load-file-format", "dat", "--type", "pdf",
        "--count", str(DAT_ROWS), "--seed", str(SEED), "--output-path", ".",
    ], cwd=root, stdout=sys.stderr, check=True)
    dat, = root.glob("*.dat")
    with dat.open("rb") as stream:
        header = stream.readline().removeprefix(codecs.BOM_UTF8)
        if not header.startswith(b"\xc3\xbeControl Number\xc3\xbe\x14") or sum(1 for _ in stream) != DAT_ROWS:
            raise ValueError("Generated DAT did not match the expected row inventory")
    return dat


def generate_binary(root, executable, size):
    archive_path = root / "seed.zip"
    subprocess.run([
        executable, "--zip-workload", str(archive_path), str(SEED), f"1:{size}:binary:store",
    ], stdout=sys.stderr, check=True)
    binary = root / "payload.bin"
    with zipfile.ZipFile(archive_path) as archive, archive.open("00000001.bin") as member, binary.open("xb") as stream:
        shutil.copyfileobj(member, stream, length=1 << 20)
    archive_path.unlink()
    if binary.stat().st_size != size:
        raise ValueError("Seeded binary input size differs from the DAT")
    return binary


def benchmark_dat(root, executable):
    dat = generate_dat(root, executable)
    size = dat.stat().st_size
    binary = generate_binary(root, executable, size)
    for label, source in (("dat", dat), ("binary", binary)):
        name = f"payload{source.suffix}"
        with source.open("rb") as stream:
            expected = hashlib.file_digest(stream, "sha256").hexdigest()
        records = []
        for method in METHODS:
            archive = root / f"{label}-{method}.zip"
            command = [sys.executable, __file__, "--archive-file", str(source), str(archive), name, method]
            records.append(measure_archive(archive, label, method, ((1, size),), command, name))
        report_matching(records, expected)


def main():
    if len(sys.argv) == 7 and sys.argv[1] == "--verify":
        _, _, path, source_name, groups, method, digest_path = sys.argv
        digest = verify_archive(path, json.loads(source_name), json.loads(groups), method)
        Path(digest_path).write_text(digest, encoding="ascii")
        return
    if len(sys.argv) == 6 and sys.argv[1] == "--archive-file":
        archive_file(*sys.argv[2:])
        return

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", help="Path to the built Zipper executable")
    parser.add_argument("--with-dat", action="store_true", help="Also compare matched ~200 MiB DAT and binary inputs")
    args = parser.parse_args()
    executable = str(Path(args.executable).resolve())
    with tempfile.TemporaryDirectory(prefix="zipper-zip-compression-") as directory:
        root = Path(directory)
        benchmark_layouts(root, executable)
        if args.with_dat:
            benchmark_dat(root, executable)


if __name__ == "__main__":
    main()
