import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class PerfCompareTests(unittest.TestCase):
    def test_high_rss_warns_without_failing(self):
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory)
            baselines = json.loads((ROOT / "tests/perf/baselines.json").read_text())
            measured = {
                name: {"wall_s": values["wall_s"], "rss_kb": values["rss_kb"] * 2}
                for name, values in baselines.items() if not name.startswith("_")
            }
            for n in range(1, 6):
                (results / f"run_{n}.json").write_text(json.dumps(measured))

            output = results / "github-output"
            env = dict(os.environ, GITHUB_OUTPUT=str(output))
            result = subprocess.run(
                ["python3", str(ROOT / ".github/scripts/perf-compare.py"), str(results)],
                cwd=ROOT, env=env, capture_output=True, text=True, check=False,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            for name, values in measured.items():
                self.assertIn(f"| {name} | rss_kb |", result.stdout)
                self.assertIn(f"| {name} | rss_kb | {values['rss_kb'] // 2} | {values['rss_kb']:.0f} | 2.00× | ⚠️ |", result.stdout)
            self.assertEqual(measured, json.loads((results / "measured.json").read_text()))
            self.assertIn("failed=true", output.read_text())

            (results / "run_5.json").unlink()
            missing_run = subprocess.run(
                ["python3", str(ROOT / ".github/scripts/perf-compare.py"), str(results)],
                cwd=ROOT, capture_output=True, text=True, check=False,
            )
            self.assertEqual(2, missing_run.returncode)
            self.assertIn("missing run file", missing_run.stderr)

            (results / "run_5.json").write_text(json.dumps({
                name: {**values, "rss_kb": 0} for name, values in measured.items()
            }))
            invalid_rss = subprocess.run(
                ["python3", str(ROOT / ".github/scripts/perf-compare.py"), str(results)],
                cwd=ROOT, capture_output=True, text=True, check=False,
            )
            self.assertEqual(2, invalid_rss.returncode)
            self.assertIn("RSS must be positive", invalid_rss.stderr)


if __name__ == "__main__":
    unittest.main()
