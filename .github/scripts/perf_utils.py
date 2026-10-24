"""Shared performance utilities for PR guard and baseline refresh."""

import json
import os
import platform
import subprocess
import sys


def median(values):
    s = sorted(values)
    n = len(s)
    return s[n // 2] if n % 2 else (s[n // 2 - 1] + s[n // 2]) / 2


def load_runs(results_dir, scenarios):
    runs = []
    for n in range(1, 6):
        run_file = os.path.join(results_dir, f"run_{n}.json")
        with open(run_file) as f:
            runs.append(json.load(f))
    for sc in scenarios:
        if any(r[sc]["rss_kb"] <= 0 or r[sc]["wall_s"] < 0 for r in runs):
            raise ValueError(f"invalid measurement for {sc}: RSS must be positive and wall time non-negative")
    return runs


def compute_variability(runs, scenarios):
    variability = {}
    for sc in scenarios:
        walls = [r[sc]["wall_s"] for r in runs]
        rsss = [r[sc]["rss_kb"] for r in runs]
        med_w = median(walls)
        med_r = median(rsss)
        min_w, max_w = min(walls), max(walls)
        min_r, max_r = min(rsss), max(rsss)
        spread_w = round(max_w - min_w, 2)
        spread_r = max_r - min_r
        pct_w = round((spread_w / med_w * 100), 1) if med_w > 0 else 0.0
        pct_r = round((spread_r / med_r * 100), 1) if med_r > 0 else 0.0
        variability[sc] = {
            "wall_s": {
                "median": med_w,
                "min": min_w,
                "max": max_w,
                "spread": spread_w,
                "spread_pct": pct_w,
            },
            "rss_kb": {
                "median": med_r,
                "min": min_r,
                "max": max_r,
                "spread": spread_r,
                "spread_pct": pct_r,
            },
        }
    return variability


def get_dotnet_version():
    try:
        res = subprocess.run(["dotnet", "--version"], capture_output=True, text=True, check=False)
        if res.returncode == 0:
            return res.stdout.strip()
    except Exception:
        pass
    return os.environ.get("DOTNET_VERSION", "unknown")


def get_runtime_context(seed=None):
    if seed is None:
        try:
            seed = int(os.environ.get("PERF_SEED", 42))
        except (ValueError, TypeError):
            seed = 42
    runner_os = os.environ.get("RUNNER_OS") or platform.system()
    img_os = os.environ.get("ImageOS")
    img_ver = os.environ.get("ImageVersion")
    if img_os and img_ver:
        runner_image = f"{img_os} ({img_ver})"
    elif img_ver:
        runner_image = f"ubuntu ({img_ver})"
    else:
        runner_image = os.environ.get("RUNNER_IMAGE", platform.platform())
    return {
        "runner_os": runner_os,
        "runner_image": runner_image,
        "dotnet_sdk": get_dotnet_version(),
        "seed": seed,
    }


def format_variability_table(variability, scenarios):
    lines = [
        "### Run-to-Run Variability (5 runs)",
        "",
        "| Scenario | Metric | Min | Max | Spread | Relative Spread |",
        "|----------|--------|-----|-----|--------|-----------------|",
    ]
    for sc in scenarios:
        vw = variability[sc]["wall_s"]
        vr = variability[sc]["rss_kb"]
        lines.append(f"| {sc} | wall_s | {vw['min']:.2f}s | {vw['max']:.2f}s | {vw['spread']:.2f}s | {vw['spread_pct']:.1f}% |")
        lines.append(f"| {sc} | rss_kb | {vr['min']} | {vr['max']} | {vr['spread']} | {vr['spread_pct']:.1f}% |")
    return "\n".join(lines)


def format_context_section(context):
    lines = [
        "### Runtime Context",
        "",
        f"- **Runner OS**: {context.get('runner_os', 'unknown')}",
        f"- **Runner Image**: {context.get('runner_image', 'unknown')}",
        f"- **.NET SDK**: {context.get('dotnet_sdk', 'unknown')}",
        f"- **Seed**: {context.get('seed', 42)}",
    ]
    return "\n".join(lines)
