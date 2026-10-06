#!/usr/bin/env python3
import sys
import os
import glob
import argparse
import xml.etree.ElementTree as ET

def parse_args():
    parser = argparse.ArgumentParser(description="Check per-file line coverage against threshold.")
    parser.add_argument("--reports", default="TestResults/*/coverage.cobertura.xml", help="Glob pattern for Cobertura XML files")
    parser.add_argument("--min-coverage", type=float, default=50.0, help="Minimum per-file line coverage percentage (default: 50.0)")
    parser.add_argument("--min-lines", type=int, default=20, help="Minimum valid executable lines per file to enforce gate (default: 20)")
    parser.add_argument("--allowlist", default=".github/coverage-file-allowlist.txt", help="Path to allowlist file for exempted files")
    return parser.parse_args()

SRC_DIR_MARKER = "/src/"

def normalize_filename(filename):
    filename = filename.strip().replace("\\", "/")
    while filename.startswith("./"):
        filename = filename[2:]
    if filename.startswith("src/"):
        filename = filename[4:]
    elif filename.startswith(SRC_DIR_MARKER):
        filename = filename[len(SRC_DIR_MARKER):]
    elif SRC_DIR_MARKER in filename:
        filename = filename.split(SRC_DIR_MARKER, 1)[1]
    return filename

def load_allowlist(allowlist_path):
    exemptions = set()
    if os.path.isfile(allowlist_path):
        with open(allowlist_path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith("#"):
                    exemptions.add(normalize_filename(line))
    return exemptions

def main():
    args = parse_args()

    report_files = []
    for pattern in args.reports.split(","):
        pattern = pattern.strip()
        if pattern:
            report_files.extend(glob.glob(pattern, recursive=True))
    report_files = sorted(set(report_files))

    if not report_files:
        print(f"[ERROR] No Cobertura XML report files found matching pattern: {args.reports}", file=sys.stderr)
        sys.exit(1)

    allowlist = load_allowlist(args.allowlist)

    file_lines = {}
    for report in report_files:
        try:
            tree = ET.parse(report)
            root = tree.getroot()
        except Exception as e:
            print(f"[ERROR] Failed to parse Cobertura report '{report}': {e}", file=sys.stderr)
            sys.exit(1)

        root_tag = root.tag.split("}")[-1] if "}" in root.tag else root.tag
        if root_tag != "coverage":
            print(f"[ERROR] Malformed Cobertura report '{report}': root element is <{root.tag}>, expected <coverage>", file=sys.stderr)
            sys.exit(1)

        for l in root.findall(".//line"):
            line_str = l.get("number")
            hits_str = l.get("hits")
            if line_str is None or hits_str is None:
                print(f"[ERROR] Malformed line element in '{report}': missing number or hits attribute", file=sys.stderr)
                sys.exit(1)
            try:
                line_num = int(line_str)
                hits = int(hits_str)
                if line_num < 0 or hits < 0:
                    raise ValueError("Line number and hits must be non-negative")
            except ValueError as ve:
                print(f"[ERROR] Invalid line/hit attribute in '{report}': {ve}", file=sys.stderr)
                sys.exit(1)

        sources = [s.text.strip().replace("\\", "/") for s in root.findall(".//sources/source") if s.text and s.text.strip()]

        for cls in root.findall(".//class"):
            raw_fname = cls.get("filename", "")
            if not raw_fname:
                continue

            clean_raw = raw_fname.strip().replace("\\", "/")
            while clean_raw.startswith("./"):
                clean_raw = clean_raw[2:]

            if sources and SRC_DIR_MARKER not in clean_raw and not clean_raw.startswith("src/") and not clean_raw.startswith(SRC_DIR_MARKER):
                matching_sources = []
                for src in sources:
                    candidate = src.rstrip("/") + "/" + clean_raw.lstrip("/")
                    if SRC_DIR_MARKER in candidate or candidate.startswith("src/"):
                        matching_sources.append(candidate)
                if len(matching_sources) > 1:
                    print(f"[ERROR] Ambiguous source resolution for class filename '{raw_fname}' in '{report}'", file=sys.stderr)
                    sys.exit(1)
                elif len(matching_sources) == 1:
                    raw_fname = matching_sources[0]

            fname = normalize_filename(raw_fname)
            if fname.startswith("Zipper.Tests/") or fname.startswith("Zipper.Analyzers") or "/obj/" in fname or fname.startswith("obj/"):
                continue

            lines = cls.findall("lines/line")
            if not lines:
                lines = cls.findall(".//line")
            if not lines:
                continue

            if fname not in file_lines:
                file_lines[fname] = {}

            for l in lines:
                line_num = int(l.get("number"))
                hits = int(l.get("hits"))
                file_lines[fname][line_num] = file_lines[fname].get(line_num, False) or (hits > 0)

    if not file_lines:
        print("[ERROR] No valid C# source files found in Cobertura XML reports.", file=sys.stderr)
        sys.exit(1)

    failed_files = []
    evaluated_count = 0

    print(f"Checking per-file line coverage (floor: {args.min_coverage:.1f}%, min lines: {args.min_lines})...")
    print(f"{'File':<60s} {'Covered':>8s} / {'Valid':<8s} {'Rate':>8s} {'Status':>8s}")
    print("-" * 90)

    for fname in sorted(file_lines.keys()):
        lines_dict = file_lines[fname]
        valid = len(lines_dict)
        covered = sum(1 for is_cov in lines_dict.values() if is_cov)
        rate = (covered / valid * 100.0) if valid > 0 else 0.0

        if valid < args.min_lines:
            status = f"SKIP (<{args.min_lines})"
        elif fname in allowlist:
            status = "ALLOWLIST"
        elif rate < args.min_coverage:
            status = "FAIL"
            failed_files.append((fname, rate, covered, valid))
            evaluated_count += 1
        else:
            status = "PASS"
            evaluated_count += 1

        print(f"{fname:<60s} {covered:8d} / {valid:<8d} {rate:7.1f}% {status:>8s}")

    print("-" * 90)
    if failed_files:
        print(f"\n[ERROR] {len(failed_files)} file(s) failed the per-file minimum coverage threshold of {args.min_coverage:.1f}%:", file=sys.stderr)
        for fname, rate, covered, valid in failed_files:
            print(f"  ::error file={fname}::{fname}: coverage {rate:.1f}% ({covered}/{valid} lines) < {args.min_coverage:.1f}% threshold", file=sys.stderr)
        sys.exit(1)
    else:
        print(f"\n[SUCCESS] All {evaluated_count} evaluated files passed the per-file coverage floor of {args.min_coverage:.1f}%!")

if __name__ == "__main__":
    main()
