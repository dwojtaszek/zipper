#!/usr/bin/env bash
# REQ-033, REQ-034: prove actual compiled metadata and CLI, not just MSBuild properties.
set -euo pipefail
cd "$(dirname "$0")/.."
sha=$(git rev-parse HEAD)
dotnet build tools/artifact-metadata -c Release --nologo
for version in 1.0.0 9.8.7-dev 9.8.7; do
  dotnet build src/Zipper.csproj -c Release "-p:Version=$version" "-p:SourceRevisionId=$sha" --nologo
  VERSION="$version" SHA="$sha" python3 - <<'PY'
import json, os, subprocess
from pathlib import Path
binary = Path("src/bin/Release/net10.0/Zipper").resolve()
assert subprocess.check_output([str(binary), "--version"], text=True).split()[1] == "v" + os.environ["VERSION"]
info = json.loads(subprocess.check_output([
    "dotnet", "tools/artifact-metadata/bin/Release/net10.0/artifact-metadata.dll",
    str(binary.with_suffix(".dll"))], text=True))
numeric = os.environ["VERSION"].split("-")[0] + ".0"
assert info == {"assemblyVersion": numeric, "fileVersion": numeric,
                "informationalVersion": os.environ["VERSION"], "sourceRevision": os.environ["SHA"]}, info
PY
done
