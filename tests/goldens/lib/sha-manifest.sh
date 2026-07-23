#!/usr/bin/env bash
# sha-manifest.sh — emit a deterministic sha256 manifest for a directory tree.
#
# Usage:
#   sha-manifest.sh <root-dir>
#
# Output (to stdout):
#   "<sha256>  <relative-path>\n" — for regular non-ZIP files, sorted by path.
#   "<sha256>  <rel-zip>::<entry-name>\n" — for uncompressed entries of .zip files.
#
# Exit codes:
#   0  success
#   1  bad arguments / root not a directory
#   2  hashing helper missing (sha256sum/shasum or python3)
#
# Notes:
#   - Paths are sorted with `LC_ALL=C sort` so output is byte-stable across
#     platforms regardless of locale.
#   - Symlinks are followed only when they resolve to a regular file inside
#     the tree; broken symlinks are skipped silently (matches `find -type f`).
#   - Output uses a single trailing newline at the end of the last line; no
#     extra blank line.

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: sha-manifest.sh <root-dir>" >&2
  exit 1
fi

root="$1"

if [[ ! -d "$root" ]]; then
  echo "sha-manifest.sh: not a directory: $root" >&2
  exit 1
fi

# Pick the available sha256 hasher. macOS ships `shasum`, most Linux distros
# ship `sha256sum`; we accept either.
hasher=""
if command -v sha256sum >/dev/null 2>&1; then
  hasher="sha256sum"
elif command -v shasum >/dev/null 2>&1; then
  hasher="shasum -a 256"
else
  echo "sha-manifest.sh: need sha256sum or shasum on PATH" >&2
  exit 2
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "sha-manifest.sh: need python3 on PATH" >&2
  exit 2
fi

python3 - "$root" << 'PYEOF'
import sys, os, zipfile, hashlib

if len(sys.argv) < 2:
    sys.exit(1)

root = sys.argv[1]

def get_file_sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            chunk = f.read(65536)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()

entries = []

for dirpath, _, filenames in os.walk(root, followlinks=True):
    for filename in filenames:
        full_path = os.path.join(dirpath, filename)
        if not os.path.isfile(full_path):
            continue
        rel_path = os.path.relpath(full_path, root).replace("\\", "/")
        if rel_path.startswith("./"):
            rel_path = rel_path[2:]

        if rel_path.endswith((".zip", ".docx", ".xlsx")):
            try:
                with zipfile.ZipFile(full_path, "r") as z:
                    for name in sorted(z.namelist()):
                        if name.endswith("/"):
                            continue
                        zh = hashlib.sha256()
                        with z.open(name) as ef:
                            while True:
                                chunk = ef.read(65536)
                                if not chunk:
                                    break
                                zh.update(chunk)
                        entry_key = f"{rel_path}::{name}"
                        entries.append((entry_key, f"{zh.hexdigest()}  {entry_key}"))
            except Exception as e:
                sys.stderr.write(f"Error reading zip {full_path}: {e}\n")
        else:
            h = get_file_sha256(full_path)
            entries.append((rel_path, f"{h}  {rel_path}"))

entries.sort(key=lambda x: x[0])
for _, line in entries:
    print(line)
PYEOF
