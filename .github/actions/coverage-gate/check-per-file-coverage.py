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

def load_allowlist(allowlist_path):
    exemptions = set()
    if os.path.isfile(allowlist_path):
        with open(allowlist_path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip().replace("\\", "/")
                if line and not line.startswith("#"):
                    if line.startswith("src/"):
                        line = line[4:]
                    exemptions.add(line)
    return exemptions

def normalize_filename(filename):
    filename = filename.replace("\\", "/")
    if "/src/" in filename:
        filename = filename.split("/src/", 1)[1]
    elif filename.startswith("src/"):
        filename = filename[4:]
    return filename

def main():
    args = parse_args()

    report_files = glob.glob(args.reports, recursive=True)
    if not report_files:
        report_files = glob.glob("**/coverage.cobertura.xml", recursive=True)

    if not report_files:
        print(f"[ERROR] No Cobertura XML report files found matching pattern: {args.reports}", file=sys.stderr)
        sys.exit(1)

    allowlist = load_allowlist(args.allowlist)

    file_stats = {}
    for report in report_files:
        try:
            tree = ET.parse(report)
            root = tree.getroot()
            for cls in root.findall(".//class"):
                raw_fname = cls.get("filename", "")
                if not raw_fname:
                    continue

                fname = normalize_filename(raw_fname)
                if fname.startswith("Zipper.Tests/") or fname.startswith("Zipper.Analyzers") or "/obj/" in fname or fname.startswith("obj/"):
                    continue

                lines = cls.findall("lines/line")
                if not lines:
                    continue

                valid = len(lines)
                covered = sum(1 for l in lines if int(l.get("hits", 0)) > 0)

                if fname not in file_stats:
                    file_stats[fname] = {"valid": 0, "covered": 0}
                file_stats[fname]["valid"] += valid
                file_stats[fname]["covered"] += covered
        except Exception as e:
            print(f"[WARNING] Failed to parse Cobertura report {report}: {e}", file=sys.stderr)

    if not file_stats:
        print("[ERROR] No valid C# source files found in Cobertura XML reports.", file=sys.stderr)
        sys.exit(1)

    failed_files = []
    evaluated_count = 0

    print(f"Checking per-file line coverage (floor: {args.min_coverage:.1f}%, min lines: {args.min_lines})...")
    print(f"{'File':<60s} {'Covered':>8s} / {'Valid':<8s} {'Rate':>8s} {'Status':>8s}")
    print("-" * 90)

    for fname in sorted(file_stats.keys()):
        stats = file_stats[fname]
        valid = stats["valid"]
        covered = stats["covered"]
        rate = (covered / valid * 100.0) if valid > 0 else 0.0

        if valid < args.min_lines:
            status = "SKIP (<20)"
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
