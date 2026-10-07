#!/usr/bin/env python3
"""Compare serial and two-worker audit corpus replay, without API calls.

Run from the repository root: python3 tools/typesafe-audit/benchmark_concurrency.py
Measures local processes only, not hosted runner checkout/queue/upload overhead.
"""

import concurrent.futures
import hashlib
import json
from pathlib import Path
import statistics
import subprocess
import sys
import tempfile
import time


ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/typesafe-audit"
CORPUS = ROOT / "tests/typesafe-audit-fixtures"
CHECKS = ("foundation", "requirements", "traceability", "architecture", "domain-language")


def run(command):
    result = subprocess.run(
        [sys.executable, *map(str, command)], cwd=ROOT,
        capture_output=True, text=True,
    )
    if result.returncode:
        raise RuntimeError(f"{command}: exit {result.returncode}\n{result.stdout}{result.stderr}")


def main():
    with tempfile.TemporaryDirectory(prefix="zipper-audit-benchmark-") as tmp:
        temp = Path(tmp)
        commands = {}
        for check in CHECKS:
            fixtures = temp / check / "fixtures"
            if check == "foundation":
                run([TOOL / "record_sample_fixture.py", fixtures])
                command = [
                    TOOL / "runner.py", "--files", TOOL / "questions/files-sample.list",
                    "--questions", TOOL / "questions/example.json",
                ]
            else:
                directory = TOOL / "checks" / check.replace("-", "_")
                recorder = [directory / "record_corpus_fixtures.py", fixtures]
                command = [directory / "run_check.py"]
                if check == "traceability":
                    corpus = CORPUS / check
                    recorder += [
                        corpus / "req-traceability.tsv", corpus, corpus,
                        directory / "questions.json",
                    ]
                    command += [
                        "--tsv", corpus / "req-traceability.tsv",
                        "--tests-root", corpus, "--requirements-root", corpus, "--full",
                    ]
                else:
                    command += ["--corpus", CORPUS / check]
                run(recorder)
            commands[check] = command + ["--mode", "fixture", "--fixture-dir", fixtures]

        baseline = None
        samples = {1: [], 2: []}
        # Alternate ordering to reduce warm-cache/order bias.
        for repetition in range(7):
            for workers in ((1, 2) if repetition % 2 == 0 else (2, 1)):
                output = temp / f"run-{repetition}-{workers}"
                output.mkdir()

                def replay(check):
                    run(commands[check] + [
                        "--json-out", output / f"{check}.json",
                        "--md-out", output / f"{check}.md",
                    ])

                start = time.perf_counter()
                with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
                    list(pool.map(replay, CHECKS))
                samples[workers].append(time.perf_counter() - start)
                hashes = {
                    path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                    for path in output.iterdir()
                }
                if len(hashes) != 10:
                    raise RuntimeError("Expected five JSON and five Markdown reports")
                if baseline is None:
                    baseline = hashes
                elif hashes != baseline:
                    raise RuntimeError("Serial/parallel audit reports differ")

        serial, parallel = (statistics.median(samples[n]) for n in (1, 2))
        print(json.dumps({
            "scope": "offline corpus replay; excludes hosted runner and live API overhead",
            "samples_seconds": samples,
            "serial_median_seconds": serial,
            "parallel_median_seconds": parallel,
            "speedup": serial / parallel,
            "report_parity": "all ten reports byte-identical across fourteen runs",
        }, indent=2))


if __name__ == "__main__":
    main()
