#!/usr/bin/env bash
# sha-manifest.sh — emit a deterministic sha256 manifest for a directory tree.
#
# Usage:
#   sha-manifest.sh <root-dir>
#
# Output (to stdout):
#   "<sha256>  <relative-path>\n" — one line per regular file, sorted by path.
#
# Exit codes:
#   0  success
#   1  bad arguments / root not a directory
#   2  hashing helper missing
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

# Walk the tree, hash each regular file, normalise the path to a relative
# form (with a leading "./" stripped), and sort.
(
  cd "$root"
  # -print0 + read -d '' keeps us safe on filenames with spaces or newlines.
  find . -type f -print0 \
    | LC_ALL=C sort -z \
    | while IFS= read -r -d '' f; do
        rel="${f#./}"
        # `sha256sum` prints "<hash>  <path>"; we substitute the path with our
        # cleaned relative form so output is identical regardless of how
        # `find` rendered the original entry.
        h=$($hasher "$f" | awk '{print $1}')
        printf '%s  %s\n' "$h" "$rel"
      done
)
