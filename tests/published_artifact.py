"""Independent acceptance of the exact native single-file executable."""
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[1]
# .NET apphost bundle signature, see dotnet/runtime's bundle format.
SIGNATURE = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")


def bundle_assembly(binary):
    data = binary.read_bytes()
    marker = data.index(SIGNATURE)
    offset = struct.unpack_from("<q", data, marker - 8)[0]
    reader = io.BytesIO(data)
    reader.seek(offset)
    major, minor, count = struct.unpack("<IIi", reader.read(12))
    if major != 6:
        raise ValueError(f"Unsupported bundle version {major}.{minor}")
    def string():
        length, shift = 0, 0
        while True:
            byte = reader.read(1)[0]
            length |= (byte & 127) << shift
            if byte < 128:
                break
            shift += 7
        return reader.read(length).decode("utf-8")
    string()  # bundle ID
    reader.read(40)  # deps/runtimeconfig locations and flags
    for _ in range(count):
        position, size, compressed = struct.unpack("<qqq", reader.read(24))
        reader.read(1)  # file type
        name = string()
        if name == "Zipper.dll":
            payload = data[position:position + (compressed or size)]
            return zlib.decompress(payload, -15) if compressed else payload
    raise ValueError("Zipper.dll missing from native bundle")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def accept():
    binary = Path(os.environ["ZIPPER_EXECUTABLE"])
    version, sha = os.environ["EXPECTED_VERSION"], os.environ["EXPECTED_SHA"]
    if not binary.is_absolute() or not binary.is_file():
        raise ValueError("An absolute shipped executable is required")
    original_hash = digest(binary)
    with tempfile.TemporaryDirectory(prefix="zipper-acceptance-") as directory:
        root = Path(directory)
        assembly = root / "Zipper.dll"
        assembly.write_bytes(bundle_assembly(binary))
        info = json.loads(subprocess.check_output([
            "dotnet", str(ROOT / "tools/artifact-metadata/bin/Release/net10.0/artifact-metadata.dll"),
            str(assembly)], text=True))
        numeric = version.split("-")[0] + ".0"
        assert info == {"assemblyVersion": numeric, "fileVersion": numeric,
                        "informationalVersion": version, "sourceRevision": sha}, info
        def run(*args, success=True):
            # No dotnet-run fallback: the target is always this native executable.
            result = subprocess.run([str(binary), *map(str, args)], cwd=root, capture_output=True, text=True, timeout=60)
            assert (result.returncode == 0) == success, result.stdout + result.stderr
            return result.stdout
        assert run("--version").split()[1] == "v" + version
        run("--not-a-zipper-option", success=False)
        standard = root / "standard"
        run("--type", "pdf", "--count", "3", "--seed", "42", "--output-path", standard,
            "--load-file-format", "csv", "--with-text")
        archives = list(standard.glob("*.zip"))
        assert len(archives) == 1
        with zipfile.ZipFile(archives[0]) as archive:
            assert archive.testzip() is None
            pdfs = [name for name in archive.namelist() if name.endswith(".pdf")]
            assert len(pdfs) == 3
            assert all(archive.read(name).startswith(b"%PDF") for name in pdfs)
            assert len([n for n in archive.namelist() if n.endswith(".txt")]) == 3
        def rows(path, dat=False):
            with path.open(encoding="utf-8-sig", newline="") as stream:
                result = list(csv.DictReader(stream, **({"delimiter": "\x14", "quotechar": "þ"} if dat else {})))
            assert len(result) == 3
            control, filepath = ("Control Number", "File Path") if dat else ("CONTROL NUMBER", "FILE PATH")
            assert len({row[control] for row in result}) == 3
            assert all(row[filepath] for row in result)
            return result
        records = rows(next(standard.glob("*.csv")))
        assert {r["FILE PATH"].replace("\\", "/") for r in records} == set(pdfs)
        loadonly = root / "loadonly"
        run("--loadfile-only", "--type", "pdf", "--count", "3", "--seed", "42",
            "--load-file-format", "dat", "--output-path", loadonly)
        rows(next(loadonly.glob("*.dat")), dat=True)
        assert not list(loadonly.rglob("*.zip")) and not list(loadonly.rglob("*.pdf"))
        audit = json.loads(next(loadonly.glob("*_properties.json")).read_text())
        assert audit["totalRecords"] == 3, audit
        production = root / "production"
        run("--production-set", "--type", "pdf", "--count", "3", "--seed", "42",
            "--bates-prefix", "ACCEPT", "--output-path", production)
        manifest_path = next(production.rglob("_manifest.json"))
        manifest = json.loads(manifest_path.read_text())
        assert manifest["nativeFileCount"] == 3
        assert manifest["batesNumberStart"] == "ACCEPT00000001"
        assert manifest["batesNumberEnd"] == "ACCEPT00000003"
        natives = list((manifest_path.parent / "NATIVES").rglob("*.pdf"))
        assert len(natives) == 3 and all(p.read_bytes().startswith(b"%PDF") for p in natives)
        data = manifest_path.parent / "DATA"
        with (data / "loadfile.dat").open(encoding="utf-8-sig", newline="") as stream:
            dat = list(csv.DictReader(stream, delimiter="\x14", quotechar="þ"))
        expected_bates = {f"ACCEPT{i:08d}" for i in range(1, 4)}
        assert len(dat) == 3 and {r["DOCID"] for r in dat} == expected_bates
        assert {r["BATES_NUMBER"] for r in dat} == expected_bates
        for row in dat:
            for column in ("NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH"):
                path = manifest_path.parent / row[column].replace("\\", "/")
                assert path.is_file() and path.stat().st_size > 0, (column, path)
        with (data / "loadfile.opt").open(encoding="utf-8-sig", newline="") as stream:
            opt = list(csv.reader(stream))
        assert len(opt) == 3 and all(len(row) == 7 for row in opt)
        assert {row[0] for row in opt} == expected_bates
        assert all(row[1] == "VOL001" and row[3] == "Y" and row[6] == "1" for row in opt)
        assert {r[2] for r in opt} == {r["IMAGE_PATH"] for r in dat}
        assert digest(binary) == original_hash, "Executable changed during acceptance"
    record = {"file": binary.name, "sha256": original_hash, "version": version,
              "source_sha": sha, "metadata": info,
              "checks": ["version", "invalid-invocation", "Standard", "Loadfile-Only", "Production Set"]}
    path = binary.parent / "acceptance.json"
    path.write_text(json.dumps(record, indent=2) + "\n")
    print(f"Accepted {binary.name}: {original_hash}")


if __name__ == "__main__":
    accept()
