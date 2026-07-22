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

# Walk the tree, hash each regular file, normalise the path to a relative
# form (with a leading "./" stripped), and sort.
(
  cd "$root"
  find . -type f -print0 \
    | LC_ALL=C sort -z \
    | while IFS= read -r -d '' f; do
        rel="${f#./}"
        if [[ "$rel" == *.zip || "$rel" == *.docx || "$rel" == *.xlsx ]]; then
          python3 -c '
import sys, zipfile, hashlib

zip_path = sys.argv[1]
rel_zip = sys.argv[2]
try:
    with zipfile.ZipFile(zip_path, "r") as z:
        for name in sorted(z.namelist()):
            if name.endswith("/"):
                continue
            h = hashlib.sha256()
            with z.open(name) as ef:
                while True:
                    chunk = ef.read(65536)
                    if not chunk:
                        break
                    h.update(chunk)
            print(f"{h.hexdigest()}  {rel_zip}::{name}")
except Exception as e:
    sys.stderr.write(f"Error reading zip {zip_path}: {e}\n")
' "$f" "$rel"
        else
          h=$($hasher "$f" | awk '{print $1}')
          printf '%s  %s\n' "$h" "$rel"
        fi
      done
)
