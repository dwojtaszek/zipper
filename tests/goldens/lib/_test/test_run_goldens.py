import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


GOLDENS_DIR = Path(__file__).resolve().parents[2]
LIB_DIR = GOLDENS_DIR / "lib"
RUNNER = GOLDENS_DIR / "run-goldens.sh"
HEADER = "scenario_name | cli_args | seed | description\n"


class RunGoldensTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)
        self.goldens_dir = self.root / "goldens"
        self.fixture_dir = self.goldens_dir / "fixtures"
        self.lib_dir = self.goldens_dir / "lib"
        self.fixture_dir.mkdir(parents=True)
        self.lib_dir.mkdir()
        shutil.copy2(RUNNER, self.goldens_dir / "run-goldens.sh")
        for name in ("sha-manifest.sh", "diff-loadfile.sh"):
            shutil.copy2(LIB_DIR / name, self.lib_dir / name)

        self.scenarios_file = self.goldens_dir / "scenarios.tsv"
        self.cli_calls = self.root / "cli-calls.txt"
        self.cli = self.root / "fake-zipper"
        self.cli.write_text(
            f"#!{sys.executable}\n"
            "import os, sys\n"
            "from pathlib import Path\n"
            "calls = os.environ.get('CLI_CALLS')\n"
            "if calls:\n"
            "    with open(calls, 'a', encoding='utf-8') as stream:\n"
            "        stream.write('called\\n')\n"
            "if '--fail' in sys.argv[1:]:\n"
            "    print('deliberate CLI failure', file=sys.stderr)\n"
            "    raise SystemExit(7)\n"
            "if not os.environ.get('CLI_SKIP_OUTPUT'):\n"
            "    Path('loadfile.dat').write_bytes("
            "os.environ.get('CLI_OUTPUT', 'expected\\\\n').encode('utf-8'))\n",
            encoding="utf-8",
        )
        self.cli.chmod(0o755)
        self.write_scenarios([("sample", "", "Sample")])

    def write_scenarios(self, rows):
        lines = [HEADER]
        lines.extend(
            f"{name} | {args} | 42 | {description}\n"
            for name, args, description in rows
        )
        self.scenarios_file.write_text("".join(lines), encoding="utf-8")

    def add_fixture(self, name, content=b"expected\n"):
        fixture = self.fixture_dir / name
        fixture.mkdir(parents=True)
        (fixture / "tree.txt").write_text("./loadfile.dat\n", encoding="utf-8")
        digest = hashlib.sha256(content).hexdigest()
        (fixture / "sha-manifest.txt").write_text(
            f"{digest}  loadfile.dat\n", encoding="utf-8"
        )
        (fixture / "loadfile.dat").write_bytes(content)

    def run_runner(self, *args, zipper_cli=None, cli_output="expected\n", skip_output=False):
        env = os.environ.copy()
        env["ZIPPER_CLI"] = str(self.cli if zipper_cli is None else zipper_cli)
        env["CLI_CALLS"] = str(self.cli_calls)
        env["CLI_OUTPUT"] = cli_output
        env["CLI_SKIP_OUTPUT"] = "1" if skip_output else ""
        return subprocess.run(
            ["bash", str(self.goldens_dir / "run-goldens.sh"), *args],
            cwd=self.root,
            env=env,
            capture_output=True,
            text=True,
            check=False,
        )

    def combined_output(self, result):
        return result.stdout + result.stderr

    def test_unknown_scenario_is_rejected_without_running_cli(self):
        self.write_scenarios([("known", "", "Known")])

        result = self.run_runner("--scenario", "misspelled")

        self.assertEqual(result.returncode, 2)
        self.assertIn("unknown scenario", result.stderr.lower())
        self.assertIn("misspelled", result.stderr)
        self.assertFalse(self.cli_calls.exists())

    def test_unknown_argument_is_rejected_without_running_cli(self):
        result = self.run_runner("--unknown")

        self.assertEqual(result.returncode, 2)
        self.assertIn("unknown argument: --unknown", result.stderr)
        self.assertFalse(self.cli_calls.exists())

    def test_missing_or_empty_scenario_selector_is_rejected(self):
        for args in (("--scenario",), ("--scenario", ""), ("--scenario", "--capture")):
            with self.subTest(args=args):
                result = self.run_runner(*args)

                self.assertEqual(result.returncode, 2)
                self.assertIn("requires a non-empty name", result.stderr)

    def test_empty_and_header_only_tables_are_rejected(self):
        for content in ("", "\n# no rows\n", HEADER):
            with self.subTest(content=content):
                self.scenarios_file.write_text(content, encoding="utf-8")

                result = self.run_runner()

                self.assertEqual(result.returncode, 2)
                self.assertIn("no scenario rows", result.stderr.lower())
                self.assertFalse(self.cli_calls.exists())

    def test_invalid_header_and_empty_scenario_name_are_rejected(self):
        cases = (
            (
                "wrong_name | cli_args | seed | description\nsample | | 42 | Sample\n",
                "invalid header",
            ),
            (HEADER + " | --count 1 | 42 | unnamed scenario\n", "empty scenario name"),
        )

        for content, diagnostic in cases:
            with self.subTest(diagnostic=diagnostic):
                self.scenarios_file.write_text(content, encoding="utf-8")

                result = self.run_runner()

                self.assertEqual(result.returncode, 2)
                self.assertIn(diagnostic, result.stderr.lower())
                self.assertFalse(self.cli_calls.exists())

    def test_missing_scenario_table_is_rejected(self):
        self.scenarios_file.unlink()

        result = self.run_runner()

        self.assertEqual(result.returncode, 2)
        self.assertIn("scenarios.tsv not found", result.stderr)

    def test_unrunnable_cli_is_rejected(self):
        self.write_scenarios([("sample", "", "Sample")])
        self.add_fixture("sample")

        result = self.run_runner(zipper_cli=self.root / "missing-cli")

        self.assertEqual(result.returncode, 2)
        self.assertIn("not an executable file", result.stderr.lower())

    def test_empty_cli_env_is_rejected(self):
        self.write_scenarios([("sample", "", "Sample")])
        self.add_fixture("sample")

        result = self.run_runner(zipper_cli="")

        self.assertEqual(result.returncode, 2)
        self.assertIn("must point at the zipper executable", result.stderr.lower())

    def test_non_executable_cli_file_is_rejected(self):
        self.write_scenarios([("sample", "", "Sample")])
        self.add_fixture("sample")
        non_exec = self.root / "non-exec-cli"
        non_exec.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        non_exec.chmod(0o644)

        result = self.run_runner(zipper_cli=non_exec)

        self.assertEqual(result.returncode, 2)
        self.assertIn("not an executable file", result.stderr.lower())

    def test_relative_cli_path_is_supported(self):
        self.write_scenarios([("sample", "", "Sample")])
        self.add_fixture("sample")

        result = self.run_runner(zipper_cli="fake-zipper")

        self.assertEqual(result.returncode, 0, self.combined_output(result))
        self.assertIn("ran=1", result.stdout)
        self.assertIn("failed=0", result.stdout)

    def test_nonzero_cli_exit_fails_scenario_with_summary(self):
        self.write_scenarios([("failure", "--fail", "Failure")])

        result = self.run_runner()
        output = self.combined_output(result)

        self.assertEqual(result.returncode, 1)
        self.assertIn("CLI crashed with non-zero exit code", output)
        self.assertIn("ran=1", output)
        self.assertIn("failed=1", output)
        self.assertIn("total=1", output)

    def test_missing_fixture_fails_scenario_with_summary(self):
        self.write_scenarios([("missing-fixture", "", "Missing fixture")])

        result = self.run_runner()
        output = self.combined_output(result)

        self.assertEqual(result.returncode, 1)
        self.assertIn("MISSING fixture dir", output)
        self.assertIn("ran=1", output)
        self.assertIn("failed=1", output)

    def test_mutated_output_fails_with_actionable_difference(self):
        self.write_scenarios([("sample", "", "Sample")])
        self.add_fixture("sample")

        result = self.run_runner(cli_output="mutated\n")
        output = self.combined_output(result)

        self.assertEqual(result.returncode, 1)
        self.assertIn("SHA manifest mismatch", output)
        self.assertIn("First differing line: 1", output)
        self.assertIn("ran=1", output)
        self.assertIn("failed=1", output)

    def test_missing_generated_file_fails_with_actionable_diagnostic(self):
        self.add_fixture("sample")

        result = self.run_runner(skip_output=True)
        output = self.combined_output(result)

        self.assertEqual(result.returncode, 1)
        self.assertIn("MISSING in actual: loadfile.dat", output)
        self.assertIn("ran=1", output)
        self.assertIn("failed=1", output)

    def test_valid_selected_scenario_runs_once(self):
        self.write_scenarios([("first", "", "First"), ("second", "", "Second")])
        self.add_fixture("first")
        self.add_fixture("second")

        result = self.run_runner("--scenario", "first")

        self.assertEqual(result.returncode, 0, self.combined_output(result))
        self.assertIn("ran=1", result.stdout)
        self.assertIn("failed=0", result.stdout)
        self.assertIn("total=2", result.stdout)
        self.assertEqual(self.cli_calls.read_text(encoding="utf-8").splitlines(), ["called"])

    def test_full_run_executes_every_scenario_successfully(self):
        self.write_scenarios([("first", "", "First"), ("second", "", "Second")])
        self.add_fixture("first")
        self.add_fixture("second")

        result = self.run_runner()

        self.assertEqual(result.returncode, 0, self.combined_output(result))
        self.assertIn("ran=2", result.stdout)
        self.assertIn("failed=0", result.stdout)
        self.assertIn("total=2", result.stdout)
        self.assertEqual(
            self.cli_calls.read_text(encoding="utf-8").splitlines(),
            ["called", "called"],
        )

    def test_capture_mode_populates_fixture_directory(self):
        self.write_scenarios([("sample", "", "Sample")])

        result = self.run_runner("--capture")

        self.assertEqual(result.returncode, 0, self.combined_output(result))
        self.assertIn("captured ->", result.stdout)
        self.assertTrue((self.fixture_dir / "sample" / "sha-manifest.txt").exists())
        self.assertTrue((self.fixture_dir / "sample" / "tree.txt").exists())


if __name__ == "__main__":
    unittest.main()
