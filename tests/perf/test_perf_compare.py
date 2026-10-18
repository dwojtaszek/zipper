import contextlib
import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]

# Dynamically load perf_compare for direct unit testing of boundary and scenario logic
_SPEC = importlib.util.spec_from_file_location(
    "perf_compare", ROOT / ".github/scripts/perf-compare.py"
)
assert _SPEC is not None and _SPEC.loader is not None, "Failed to load spec for perf-compare.py"
perf_compare = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(perf_compare)

# Multiplier vectors across 5 runs with shuffled outliers.
# For each metric, median strictly differs from first, last, mean, min, max.
# All 5 values in each vector are distinct.
HEALTHY_WALL_MULTS = [1.50, 0.60, 1.00, 1.20, 0.80]     # sorted: [0.60, 0.80, 1.00, 1.20, 1.50] -> median 1.00
HEALTHY_RSS_MULTS = [1.35, 0.85, 1.00, 1.15, 0.95]      # sorted: [0.85, 0.95, 1.00, 1.15, 1.35] -> median 1.00
REGRESSED_WALL_MULTS = [2.60, 1.00, 1.60, 1.90, 1.40]   # sorted: [1.00, 1.40, 1.60, 1.90, 2.60] -> median 1.60 (> 1.50)
REGRESSED_RSS_MULTS = [3.00, 1.50, 2.00, 2.40, 1.80]    # sorted: [1.50, 1.80, 2.00, 2.40, 3.00] -> median 2.00 (> 1.20)


def build_runs_from_baselines(baselines, high_rss=True):
    """Dynamically builds 5 unequal runs with shuffled outliers from baselines.json."""
    scenarios = [k for k in baselines if not k.startswith("_")]
    runs = []
    for run_idx in range(5):
        run_data = {}
        for sc in scenarios:
            bw = baselines[sc]["wall_s"]
            br = baselines[sc]["rss_kb"]
            if sc == "eml_20k":
                wm = REGRESSED_WALL_MULTS[run_idx]
                rm = HEALTHY_RSS_MULTS[run_idx]
            elif sc == "loadfile_200k" and high_rss:
                wm = HEALTHY_WALL_MULTS[run_idx]
                rm = REGRESSED_RSS_MULTS[run_idx]
            else:
                wm = HEALTHY_WALL_MULTS[run_idx]
                rm = HEALTHY_RSS_MULTS[run_idx]
            run_data[sc] = {
                "wall_s": round(bw * wm, 2),
                "rss_kb": int(round(br * rm)),
            }
        runs.append(run_data)

    expected_medians = {}
    for sc in scenarios:
        expected_medians[sc] = {
            "wall_s": perf_compare.median([r[sc]["wall_s"] for r in runs]),
            "rss_kb": perf_compare.median([r[sc]["rss_kb"] for r in runs]),
        }

    return runs, expected_medians


class PerfCompareTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.baselines = json.loads((ROOT / "tests/perf/baselines.json").read_text())
        cls.scenarios = [k for k in cls.baselines if not k.startswith("_")]
        cls.runs_high_rss, cls.expected_high_rss = build_runs_from_baselines(
            cls.baselines, high_rss=True
        )
        cls.runs_healthy_rss, cls.expected_healthy_rss = build_runs_from_baselines(
            cls.baselines, high_rss=False
        )

    def _write_runs(self, directory: Path, runs):
        """Helper to write run_1.json through run_N.json in the test directory."""
        for n, run in enumerate(runs, 1):
            (directory / f"run_{n}.json").write_text(json.dumps(run))

    def _run_compare(self, results_dir, extra_args=None, env=None):
        """Helper to invoke perf-compare.py with isolated environment and interpreter portability."""
        args = [sys.executable, str(ROOT / ".github/scripts/perf-compare.py"), str(results_dir)]
        if extra_args:
            args.extend(extra_args)

        run_env = dict(os.environ) if env is None else dict(env)
        # Isolate GITHUB_OUTPUT unless explicitly provided in env to prevent host CI pollution
        if env is None and "GITHUB_OUTPUT" in run_env:
            del run_env["GITHUB_OUTPUT"]

        return subprocess.run(
            args,
            cwd=ROOT,
            env=run_env,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_median_function_odd_and_even_lengths(self):
        """Directly tests perf_compare.median on odd, even, and single-element sequences."""
        self.assertEqual(42, perf_compare.median([42]))
        self.assertEqual(20, perf_compare.median([30, 10, 20]))
        self.assertEqual(15.0, perf_compare.median([10, 20]))
        self.assertEqual(2.5, perf_compare.median([1, 4, 2, 3]))
        self.assertEqual(3, perf_compare.median([5, 1, 3, 2, 4]))

    def test_high_rss_warns_without_failing(self):
        """Verifies 5-run median aggregation with unequal/outlier runs, controls, and advisory RSS warning."""
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory)
            self._write_runs(results, self.runs_high_rss)

            output = results / "github-output"
            table_out = results / "table.md"
            env = dict(os.environ, GITHUB_OUTPUT=str(output))
            result = self._run_compare(results, extra_args=["--table-out", str(table_out)], env=env)

            # Advisory exit 0 contract preserved per #1061
            self.assertEqual(0, result.returncode, result.stderr)

            # Exact aggregation asserted in measured.json
            measured_content = json.loads((results / "measured.json").read_text())
            self.assertEqual(self.expected_high_rss, measured_content)

            # Healthy control: pdf_50k within wall (<= 1.25) and RSS (<= 1.20)
            bw = self.baselines["pdf_50k"]["wall_s"]
            mw = self.expected_high_rss["pdf_50k"]["wall_s"]
            br = self.baselines["pdf_50k"]["rss_kb"]
            mr = self.expected_high_rss["pdf_50k"]["rss_kb"]
            self.assertIn(f"| pdf_50k | wall_s | {bw:.2f} | {mw:.2f} | {mw/bw:.2f}× | ℹ️ |", result.stdout)
            self.assertIn(f"| pdf_50k | rss_kb | {br} | {mr:.0f} | {mr/br:.2f}× | ✅ |", result.stdout)

            # Wall-only regression control: eml_20k wall exceeds 1.50x, RSS remains healthy
            bw = self.baselines["eml_20k"]["wall_s"]
            mw = self.expected_high_rss["eml_20k"]["wall_s"]
            br = self.baselines["eml_20k"]["rss_kb"]
            mr = self.expected_high_rss["eml_20k"]["rss_kb"]
            rw = mw / bw
            self.assertIn(f"| eml_20k | wall_s | {bw:.2f} | {mw:.2f} | {rw:.2f}× | ⚠️ |", result.stdout)
            self.assertIn(f"| eml_20k | rss_kb | {br} | {mr:.0f} | {mr/br:.2f}× | ✅ |", result.stdout)
            self.assertIn(
                f"::warning title=Perf Guard (wall_s)::eml_20k wall_s is {rw:.2f}× baseline ({mw:.2f}s vs {bw:.2f}s). Likely performance regression — review recommended.",
                result.stdout,
            )

            # High RSS advisory control: loadfile_200k exceeds 1.20x advisory threshold
            bw = self.baselines["loadfile_200k"]["wall_s"]
            mw = self.expected_high_rss["loadfile_200k"]["wall_s"]
            br = self.baselines["loadfile_200k"]["rss_kb"]
            mr = self.expected_high_rss["loadfile_200k"]["rss_kb"]
            self.assertIn(f"| loadfile_200k | wall_s | {bw:.2f} | {mw:.2f} | {mw/bw:.2f}× | ℹ️ |", result.stdout)
            self.assertIn(f"| loadfile_200k | rss_kb | {br} | {mr:.0f} | {mr/br:.2f}× | ⚠️ |", result.stdout)
            self.assertIn(
                "::warning title=Perf Guard (rss_kb)::Peak RSS exceeds 1.20× baseline; review recommended.",
                result.stdout,
            )

            # Table output file exists and matches stdout
            self.assertTrue(table_out.is_file())
            self.assertIn(f"| loadfile_200k | rss_kb | {br} | {mr:.0f} | {mr/br:.2f}× | ⚠️ |", table_out.read_text())

            # GitHub output file contains failed=true and table delimiter
            gh_text = output.read_text()
            self.assertIn("failed=true\n", gh_text)
            self.assertIn("table<<EOF\n", gh_text)

    def test_healthy_and_wall_only_regression_controls_emit_failed_false(self):
        """Verifies that wall regression alone does not fail the guard when RSS is healthy."""
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory)
            self._write_runs(results, self.runs_healthy_rss)

            output = results / "github-output"
            env = dict(os.environ, GITHUB_OUTPUT=str(output))
            result = self._run_compare(results, env=env)

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(self.expected_healthy_rss, json.loads((results / "measured.json").read_text()))

            # Wall warning is present, but RSS warning is absent
            self.assertIn("::warning title=Perf Guard (wall_s)::eml_20k wall_s is", result.stdout)
            self.assertNotIn("::warning title=Perf Guard (rss_kb)", result.stdout)
            self.assertIn("✅ All scenarios within thresholds.", result.stdout)

            # GitHub output reports failed=false
            self.assertIn("failed=false\n", output.read_text())

    def test_median_differs_from_first_last_mean_min_max(self):
        """Proves that for every scenario and metric, median strictly differs from first, last, mean, min, max."""
        datasets = [
            ("high_rss", self.runs_high_rss, self.expected_high_rss),
            ("healthy_rss", self.runs_healthy_rss, self.expected_healthy_rss),
        ]

        substitutions = {
            "first": lambda v: v[0],
            "last": lambda v: v[-1],
            "mean": lambda v: sum(v) / len(v),
            "min": min,
            "max": max,
        }

        for dataset_name, runs, expected_medians in datasets:
            # 1. Verify individual series
            for sc, metrics in expected_medians.items():
                for metric, expected_med in metrics.items():
                    vals = [r[sc][metric] for r in runs]
                    self.assertEqual(5, len(vals))
                    self.assertEqual(5, len(set(vals)), f"{dataset_name} {sc} {metric} has duplicates")

                    # Verify perf_compare.median matches expected
                    computed_median = perf_compare.median(vals)
                    self.assertEqual(expected_med, computed_median)

                    for sub_name, sub_fn in substitutions.items():
                        alt_val = sub_fn(vals)
                        self.assertNotEqual(
                            expected_med,
                            alt_val,
                            f"{dataset_name} {sc} {metric}: median {expected_med} matches {sub_name} {alt_val}",
                        )

            # 2. Verify substitution on full measured payload aggregation
            for sub_name, sub_fn in substitutions.items():
                substituted_aggregation = {
                    sc: {
                        "wall_s": sub_fn([r[sc]["wall_s"] for r in runs]),
                        "rss_kb": sub_fn([r[sc]["rss_kb"] for r in runs]),
                    }
                    for sc in expected_medians
                }
                self.assertNotEqual(
                    expected_medians,
                    substituted_aggregation,
                    f"Substitution {sub_name} unexpectedly matched expected medians for {dataset_name}",
                )

    def test_warning_threshold_boundaries(self):
        """Verifies exact warning threshold boundaries: 1.25 wall informational, 1.50 wall warning, 1.20 RSS advisory."""
        baseline = {"wall_s": 2.0, "rss_kb": 100000}

        # RSS threshold boundary: <= 1.20 is ✅ and not failed; > 1.20 is ⚠️ and failed
        rows_pass, failed_pass = perf_compare.scenario_rows(
            "sc", baseline, {"wall_s": 2.0, "rss_kb": 120000}
        )
        self.assertFalse(failed_pass)
        self.assertIn("1.20× | ✅", rows_pass[1])

        rows_fail, failed_fail = perf_compare.scenario_rows(
            "sc", baseline, {"wall_s": 2.0, "rss_kb": 120001}
        )
        self.assertTrue(failed_fail)
        self.assertIn("1.20× | ⚠️", rows_fail[1])

        # Wall threshold boundary: <= 1.25 is ℹ️; > 1.25 is ⚠️
        rows_wall_info, _ = perf_compare.scenario_rows(
            "sc", baseline, {"wall_s": 2.50, "rss_kb": 100000}
        )
        self.assertIn("1.25× | ℹ️", rows_wall_info[0])

        rows_wall_warn_status, _ = perf_compare.scenario_rows(
            "sc", baseline, {"wall_s": 2.52, "rss_kb": 100000}
        )
        self.assertIn("1.26× | ⚠️", rows_wall_warn_status[0])

        # Wall warning emission boundary: <= 1.50 emits no warning; > 1.50 emits warning
        captured_150 = io.StringIO()
        with contextlib.redirect_stdout(captured_150):
            perf_compare.scenario_rows("sc", baseline, {"wall_s": 3.00, "rss_kb": 100000})
        self.assertEqual("", captured_150.getvalue())

        captured_151 = io.StringIO()
        with contextlib.redirect_stdout(captured_151):
            perf_compare.scenario_rows("sc", baseline, {"wall_s": 3.02, "rss_kb": 100000})
        self.assertIn("::warning title=Perf Guard (wall_s)::sc wall_s is 1.51× baseline", captured_151.getvalue())

    def test_missing_run_file_fails(self):
        """Verifies that missing run files produce exit status 2 with clear diagnostic."""
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory)
            for n in range(1, 5):  # only runs 1..4, missing run_5
                (results / f"run_{n}.json").write_text(json.dumps(self.runs_high_rss[n - 1]))

            missing_run = self._run_compare(results)
            self.assertEqual(2, missing_run.returncode)
            self.assertIn("missing run file", missing_run.stderr)

    def test_invalid_measurement_fails(self):
        """Verifies that non-positive RSS or negative wall time produce exit status 2."""
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory)
            self._write_runs(results, self.runs_high_rss)

            cases = [
                ("zero_rss", {"rss_kb": 0}, "RSS must be positive"),
                ("negative_rss", {"rss_kb": -100}, "RSS must be positive"),
                ("negative_wall", {"wall_s": -0.5}, "wall time non-negative"),
            ]

            for case_name, override, expected_err in cases:
                with self.subTest(case=case_name):
                    (results / "run_5.json").write_text(
                        json.dumps({
                            name: {**vals, **override}
                            for name, vals in self.runs_high_rss[4].items()
                        })
                    )
                    res = self._run_compare(results)
                    self.assertEqual(2, res.returncode)
                    self.assertIn(expected_err, res.stderr)

    def test_custom_measured_out_option(self):
        """Verifies that --measured-out writes medians to the specified custom path without polluting CI output."""
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory)
            self._write_runs(results, self.runs_high_rss)

            custom_measured = results / "subdir" / "custom_measured.json"
            custom_measured.parent.mkdir(parents=True, exist_ok=True)
            result = self._run_compare(
                results,
                extra_args=["--measured-out", str(custom_measured)],
            )
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertTrue(custom_measured.is_file())
            self.assertEqual(self.expected_high_rss, json.loads(custom_measured.read_text()))

    def test_invalid_args_fails(self):
        """Verifies that missing or unknown CLI arguments produce exit status 2."""
        no_args = subprocess.run(
            [sys.executable, str(ROOT / ".github/scripts/perf-compare.py")],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(2, no_args.returncode)
        self.assertIn("usage: perf-compare.py", no_args.stderr)

        invalid_flag = subprocess.run(
            [sys.executable, str(ROOT / ".github/scripts/perf-compare.py"), "results", "--unknown"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(2, invalid_flag.returncode)
        self.assertIn("usage: perf-compare.py", invalid_flag.stderr)


if __name__ == "__main__":
    unittest.main()
