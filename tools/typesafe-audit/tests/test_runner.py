#!/usr/bin/env python3
"""Unit tests for the TypeSafe audit runner (stdlib only, no network)."""

from __future__ import annotations

import json
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import runner  # noqa: E402


def make_inputs(tmp: Path, files: dict[str, str]) -> Path:
    file_list = tmp / "files.list"
    file_list.write_text("\n".join(files), encoding="utf-8")
    for name, content in files.items():
        target = runner.REPO_ROOT / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
    return file_list


def isolate_repo_root(testcase: unittest.TestCase) -> None:
    """Point runner.REPO_ROOT at a temp checkout so tests never touch the real tree."""
    repo = Path(tempfile.mkdtemp()) / "repo"
    repo.mkdir()
    testcase.addCleanup(lambda: shutil.rmtree(repo.parent, ignore_errors=True))
    patcher = mock.patch.object(runner, "REPO_ROOT", repo)
    patcher.start()
    testcase.addCleanup(patcher.stop)


class ResolveInputsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        # Isolate the checkout root: scratch files live in the temp dir and the
        # escape check is exercised against a synthetic root.
        isolate_repo_root(self)

    def make_repo_file(self, name: str, content: str) -> None:
        target = runner.REPO_ROOT / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def test_resolves_files_inside_checkout_with_hash_and_content(self):
        self.make_repo_file("inside.txt", "hello")
        file_list = self.tmp / "files.list"
        file_list.write_text("inside.txt", encoding="utf-8")
        inputs = runner.resolve_inputs(file_list, 1024, 4096)
        self.assertEqual(1, len(inputs))
        self.assertEqual("inside.txt", inputs[0]["path"])
        self.assertEqual(64, len(inputs[0]["sha256"]))
        self.assertEqual("hello", inputs[0]["content"])

    def test_rejects_path_outside_checkout(self):
        outside = self.tmp / "outside.txt"
        outside.write_text("secret", encoding="utf-8")
        file_list = self.tmp / "files.list"
        file_list.write_text(str(outside), encoding="utf-8")
        with self.assertRaises(SystemExit) as ctx:
            runner.resolve_inputs(file_list, 1024, 4096)
        self.assertEqual(runner.EXIT_INPUT_ERROR, ctx.exception.code)

    def test_rejects_missing_file(self):
        file_list = self.tmp / "files.list"
        file_list.write_text("does-not-exist.txt", encoding="utf-8")
        with self.assertRaises(SystemExit) as ctx:
            runner.resolve_inputs(file_list, 1024, 4096)
        self.assertEqual(runner.EXIT_INPUT_ERROR, ctx.exception.code)

    def test_rejects_file_over_per_file_limit(self):
        self.make_repo_file("inside.txt", "x" * 100)
        file_list = self.tmp / "files.list"
        file_list.write_text("inside.txt", encoding="utf-8")
        with self.assertRaises(SystemExit):
            runner.resolve_inputs(file_list, 10, 4096)

    def test_rejects_total_over_budget(self):
        self.make_repo_file("inside.txt", "x" * 60)
        self.make_repo_file("evil.txt", "y" * 60)
        file_list = self.tmp / "files.list"
        file_list.write_text("inside.txt\nevil.txt", encoding="utf-8")
        with self.assertRaises(SystemExit):
            runner.resolve_inputs(file_list, 1024, 100)


class QuestionsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())

    def test_loads_valid_questions(self):
        path = self.tmp / "q.json"
        path.write_text(json.dumps({
            "q1": {"type": "noul", "instructions": "yes?"},
        }), encoding="utf-8")
        questions = runner.load_questions(path)
        self.assertIn("q1", questions)

    def test_rejects_question_missing_instructions(self):
        path = self.tmp / "q.json"
        path.write_text(json.dumps({"q1": {"type": "noul"}}), encoding="utf-8")
        with self.assertRaises(SystemExit) as ctx:
            runner.load_questions(path)
        self.assertEqual(runner.EXIT_INPUT_ERROR, ctx.exception.code)

    def test_rejects_unsupported_type(self):
        path = self.tmp / "q.json"
        path.write_text(json.dumps({"q1": {"type": "essay", "instructions": "x"}}), encoding="utf-8")
        with self.assertRaises(SystemExit):
            runner.load_questions(path)


class RequestTests(unittest.TestCase):
    def test_build_request_batches_all_questions_over_shared_state(self):
        inputs = [{"path": "a.md", "sha256": "h", "content": "text"}]
        questions = {"q1": {"type": "noul", "instructions": "yes?"}}
        request = runner.build_request(inputs, questions, "jev-1.13.0")
        self.assertEqual("jev-1.13.0", request["model"])
        self.assertEqual(1, len(request["state"]["files"]))
        self.assertEqual({"q1"}, set(request["questions"]))

    def test_fixture_key_is_stable_across_runs(self):
        request = {"state": {"files": [{"path": "a", "sha256": "h", "content": "t"}]}, "model": "m", "questions": {"q": {}}}
        self.assertEqual(runner.request_fixture_key(request), runner.request_fixture_key(dict(reversed(list(request.items())))))


class RedactionTests(unittest.TestCase):
    def test_scrubs_secrets_from_text(self):
        text = "HTTPError: Authorization: Bearer sk-super-secret in body"
        out = runner.redact(text, ["sk-super-secret"])
        self.assertNotIn("sk-super-secret", out)
        self.assertIn(runner.REDACTED, out)

    def test_empty_secret_is_noop(self):
        self.assertEqual("unchanged", runner.redact("unchanged", [""]))


class PolicyTests(unittest.TestCase):
    def test_low_confidence_is_finding(self):
        row = {"id": "q", "type": "choice", "answer": "a", "confidence": 0.3}
        self.assertTrue(runner.is_finding(row, {}))

    def test_high_confidence_is_not_finding(self):
        row = {"id": "q", "type": "choice", "answer": "a", "confidence": 0.95}
        self.assertFalse(runner.is_finding(row, {}))

    def test_noul_mid_range_answer_is_finding(self):
        row = {"id": "q", "type": "noul", "answer": 0.5, "confidence": None}
        self.assertTrue(runner.is_finding(row, {}))

    def test_noul_extreme_answer_is_not_finding(self):
        row = {"id": "q", "type": "noul", "answer": 0.98, "confidence": None}
        self.assertFalse(runner.is_finding(row, {}))


class SerializationTests(unittest.TestCase):
    def test_report_json_is_stable_byte_for_byte(self):
        report = {
            "runner_version": "1.0.0",
            "model": "jev-1.13.0",
            "mode": "fixture",
            "source_files": [{"path": "b.md", "sha256": "h2"}, {"path": "a.md", "sha256": "h1"}],
            "usage": {"input_tokens": 10, "output_tokens": 2},
            "answers": [{"id": "q", "type": "noul", "answer": 0.9, "confidence": None, "is_finding": False, "instructions": "i"}],
        }
        first = json.dumps(report, indent=2, sort_keys=True)
        second = json.dumps(dict(reversed(list(report.items()))), indent=2, sort_keys=True)
        self.assertEqual(first, second)


class ExitCodeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        isolate_repo_root(self)
        self.file_list = make_inputs(self.tmp, {"inside.txt": "hello"})
        self.questions = self.tmp / "q.json"
        self.questions.write_text(json.dumps({"q1": {"type": "noul", "instructions": "yes?"}}), encoding="utf-8")

    def run_runner(self, extra_args: list[str], response: dict) -> "mock.MagicMock":
        base = [
            "--files", str(self.file_list),
            "--questions", str(self.questions),
            "--json-out", str(self.tmp / "out.json"),
            "--md-out", str(self.tmp / "out.md"),
        ]
        env = {"TYPESAFE_API_KEY": "test-key"}
        with mock.patch.dict(os.environ, env, clear=True), mock.patch.object(runner, "run_live", return_value=response) as live:
            code = runner.main(base + extra_args)
            return live, code

    def test_advisory_findings_exit_zero(self):
        _, code = self.run_runner(["--mode", "live"], {"model": "m", "answers": {"q1": {"type": "noul", "noul": 0.5}}})
        self.assertEqual(runner.EXIT_OK, code)

    def test_strict_findings_exit_one(self):
        _, code = self.run_runner(["--mode", "live", "--strict"], {"model": "m", "answers": {"q1": {"type": "noul", "noul": 0.5}}})
        self.assertEqual(runner.EXIT_FINDING, code)

    def test_no_findings_exit_zero_even_strict(self):
        _, code = self.run_runner(["--mode", "live", "--strict"], {"model": "m", "answers": {"q1": {"type": "noul", "noul": 0.99}}})
        self.assertEqual(runner.EXIT_OK, code)

    def test_remote_failure_exit_three(self):
        with mock.patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}, clear=True), \
             mock.patch.object(runner, "run_live", side_effect=SystemExit(runner.EXIT_REMOTE_ERROR)):
            base = [
                "--mode", "live",
                "--files", str(self.file_list),
                "--questions", str(self.questions),
                "--json-out", str(self.tmp / "o.json"),
                "--md-out", str(self.tmp / "o.md"),
            ]
            with self.assertRaises(SystemExit) as ctx:
                runner.main(base)
            self.assertEqual(runner.EXIT_REMOTE_ERROR, ctx.exception.code)

    def test_live_without_api_key_exits_input_error(self):
        base = [
            "--mode", "live",
            "--files", str(self.file_list),
            "--questions", str(self.questions),
            "--json-out", str(self.tmp / "o.json"),
            "--md-out", str(self.tmp / "o.md"),
        ]
        with mock.patch.dict(os.environ, {}, clear=True):
            code = runner.main(base)
        self.assertEqual(runner.EXIT_INPUT_ERROR, code)


class FixtureModeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        isolate_repo_root(self)
        self.file_list = make_inputs(self.tmp, {"inside.txt": "hello"})
        self.questions = self.tmp / "q.json"
        self.questions.write_text(json.dumps({"q1": {"type": "noul", "instructions": "yes?"}}), encoding="utf-8")

    def test_missing_fixture_is_input_error(self):
        base = [
            "--mode", "fixture",
            "--fixture-dir", str(self.tmp / "none"),
            "--files", str(self.file_list),
            "--questions", str(self.questions),
            "--json-out", str(self.tmp / "o.json"),
            "--md-out", str(self.tmp / "o.md"),
        ]
        with self.assertRaises(SystemExit) as ctx:
            runner.main(base)
        self.assertEqual(runner.EXIT_INPUT_ERROR, ctx.exception.code)

    def test_recorded_fixture_replays_deterministically_without_network(self):
        inputs = runner.resolve_inputs(self.file_list, 1024, 4096)
        questions = runner.load_questions(self.questions)
        request = runner.build_request(inputs, questions, "jev-1.13.0")
        response = {"model": "jev-1.13.0", "answers": {"q1": {"type": "noul", "noul": 0.9}}}
        runner.record_fixture(request, self.tmp / "fixtures", response)

        base = [
            "--mode", "fixture",
            "--fixture-dir", str(self.tmp / "fixtures"),
            "--files", str(self.file_list),
            "--questions", str(self.questions),
            "--json-out", str(self.tmp / "o.json"),
            "--md-out", str(self.tmp / "o.md"),
        ]
        with mock.patch.object(runner.urllib.request, "urlopen") as must_not_network:
            code = runner.main(base)
        self.assertEqual(runner.EXIT_OK, code)
        must_not_network.assert_not_called()

        first = (self.tmp / "o.json").read_text(encoding="utf-8")
        runner.main(base)
        self.assertEqual(first, (self.tmp / "o.json").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
