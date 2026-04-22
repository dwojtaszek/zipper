#!/bin/bash
# Shared helper for E2E test scripts.
# Source this file (not execute) to get a `zipper` command that prefers a
# pre-built Release binary over repeated `dotnet run` invocations.
#
# Usage (in every test-*.sh):
#     # shellcheck source=./_zipper-cli.sh
#     source "$(dirname "$0")/_zipper-cli.sh"
#     ...
#     zipper --type pdf --count 10 --output-path "$OUT"

_zipper_project="${ZIPPER_PROJECT:-src/Zipper.csproj}"

_zipper_resolve_bin() {
    # Already resolved (e.g. parent script sourced this) keep it.
    if [[ -n "${_ZIPPER_BIN:-}" && -x "${_ZIPPER_BIN}" ]]; then
        return 0
    fi

    # Build once (Release). Keep output quiet so it does not spam test logs.
    if ! dotnet build "$_zipper_project" -c Release --nologo -v quiet >/dev/null; then
        echo "[_zipper-cli] dotnet build failed; falling back to 'dotnet run' per call" >&2
        return 0
    fi

    # Discover the produced binary. Works for any net* TFM.
    # Resolve to an absolute path so the caller can cd elsewhere between
    # helper-init and command invocation without breaking the binary path.
    local build_dir
    build_dir=$(find src/bin/Release -mindepth 1 -maxdepth 1 -type d -name "net*" 2>/dev/null | head -n 1)
    [[ -z "$build_dir" ]] && build_dir="src/bin/Release/net8.0"
    if [[ -d "$build_dir" ]]; then
        build_dir=$(cd "$build_dir" && pwd)
    fi

    if [[ -f "$build_dir/Zipper" ]]; then
        export _ZIPPER_BIN="$build_dir/Zipper"
    elif [[ -f "$build_dir/Zipper.exe" ]]; then
        export _ZIPPER_BIN="$build_dir/Zipper.exe"
    fi
    return 0
}

zipper() {
    if [[ -n "${_ZIPPER_BIN:-}" && -x "${_ZIPPER_BIN}" ]]; then
        "${_ZIPPER_BIN}" "$@"
    else
        # Build failed or binary not found: fall back to `dotnet run` WITHOUT
        # --no-build so the project is compiled on demand. Using --no-build
        # here would guarantee a missing-assembly failure.
        dotnet run -c Release --project "$_zipper_project" -- "$@"
    fi
}

_zipper_resolve_bin
export -f zipper 2>/dev/null || true
