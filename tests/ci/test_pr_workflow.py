"""Regressions for PR workflow requirement traceability gating (#1215)."""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class PrWorkflowTraceabilityTests(unittest.TestCase):
    def setUp(self):
        self.pr_workflow_path = ROOT / ".github/workflows/pr.yml"
        self.workflow_text = self.pr_workflow_path.read_text(encoding="utf-8")

    def test_docs_only_job_runs_deterministic_traceability_gate(self):
        """Docs-only PRs must run validate-req-traceability.sh --strict so REQ immutability is enforced."""
        try:
            import yaml
            parsed = yaml.safe_load(self.workflow_text)
        except ImportError:
            parsed = None

        if parsed:
            docs_only_job = parsed.get("jobs", {}).get("docs-only", {})
            steps = docs_only_job.get("steps", [])
            traceability_steps = [
                s for s in steps
                if "validate-req-traceability.sh --strict" in s.get("run", "")
            ]
            self.assertEqual(
                len(traceability_steps),
                1,
                "docs-only job must have exactly one step running validate-req-traceability.sh --strict",
            )
            step = traceability_steps[0]
            condition = step.get("if", "")
            self.assertIn(
                "steps.check.outputs.only_docs == 'true'",
                condition,
                "Traceability step in docs-only job must be conditioned on only_docs == 'true'",
            )
        else:
            self.assertIn("validate-req-traceability.sh --strict", self.workflow_text)
            self.assertIn("steps.check.outputs.only_docs == 'true'", self.workflow_text)

    def test_docs_only_job_runs_docs_drift_tests(self):
        """Docs-only PRs must run DocsDriftTests so terminology/README/flag drift is caught before merge."""
        try:
            import yaml
            parsed = yaml.safe_load(self.workflow_text)
        except ImportError:
            parsed = None

        if parsed:
            docs_only_job = parsed.get("jobs", {}).get("docs-only", {})
            steps = docs_only_job.get("steps", [])
            drift_steps = [
                s for s in steps
                if "DocsDriftTests" in s.get("run", "")
            ]
            self.assertEqual(
                len(drift_steps),
                1,
                "docs-only job must have a step running DocsDriftTests",
            )
            step = drift_steps[0]
            condition = step.get("if", "")
            self.assertIn(
                "steps.check.outputs.only_docs == 'true'",
                condition,
                "DocsDriftTests step in docs-only job must be conditioned on only_docs == 'true'",
            )
        else:
            self.assertIn("DocsDriftTests", self.workflow_text)

    def test_ci_gate_depends_on_docs_only_job(self):
        """ci-gate must depend on docs-only job so a failure in docs-only blocks merge."""
        try:
            import yaml
            parsed = yaml.safe_load(self.workflow_text)
        except ImportError:
            parsed = None

        if parsed:
            ci_gate = parsed.get("jobs", {}).get("ci-gate", {})
            needs = ci_gate.get("needs", [])
            self.assertIn("docs-only", needs, "ci-gate must list docs-only in its needs dependencies")
        else:
            self.assertTrue(
                re.search(r"ci-gate:.*?needs:\s*\[[^\]]*docs-only", self.workflow_text, re.DOTALL),
                "ci-gate must list docs-only in its needs dependencies",
            )

    @unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "POSIX bash execution boundary")
    def test_traceability_script_detects_stale_tsv_row_on_deleted_req(self):
        """If a REQ is deleted/renumbered in Requirements.md but remains in TSV, gate must fail strict."""
        with tempfile.TemporaryDirectory() as tmp:
            tmp_root = Path(tmp)
            reqs = tmp_root / "Requirements.md"
            reqs.write_text("- **REQ-001**: Active requirement.\n", encoding="utf-8")

            tsv = tmp_root / "req-traceability.tsv"
            tsv.write_text(
                "req_id\tcoverage\treference\tnotes\n"
                "REQ-001\texemption\t-\tValid rationale\n"
                "REQ-999\texemption\t-\tStale deleted requirement\n",
                encoding="utf-8",
            )

            tests_dir = tmp_root / "tests"
            tests_dir.mkdir()

            script_path = ROOT / "tests/validate-req-traceability.sh"
            result = subprocess.run(
                [
                    "bash",
                    str(script_path),
                    "--strict",
                    "--requirements",
                    str(reqs),
                    "--tsv",
                    str(tsv),
                    "--repo-root",
                    str(tmp_root),
                    "--tests-root",
                    str(tmp_root),
                ],
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertEqual(1, result.returncode, f"Expected exit code 1, got {result.returncode}. Output:\n{result.stdout}\n{result.stderr}")
            self.assertIn("unknown manifest requirement references found", result.stderr)
            self.assertIn("REQ-999", result.stdout)


class CiGateContractTests(unittest.TestCase):
    """The required `ci-gate` check must fail closed on anything it cannot prove.

    PR #1377 exposed the hole. The gate blocklisted the literal strings "failure"
    and "cancelled", so when the caller's reusable `ci` workflow was torn down
    mid-run and reported `abandoned` -- a value outside the documented
    success/failure/cancelled/skipped set -- the grep matched nothing and the gate
    went green. The run's own conclusion was `failure` and its macOS leg never
    executed a single step, yet the required check reported success. `ci-gate` is
    the only status context branch protection requires, so a green gate is what
    lets `gh pr merge` land a PR whose CI never ran. (The runner's own
    `_ci_status_from_rollup` reads the whole rollup as an allowlist and did see the
    cancelled leg, so it would have refused this merge -- the required check, and
    anything trusting it, is what was wrong.)

    These tests execute the gate step's real script, extracted from pr.yml, under
    the env contract that same step declares, so the behavior under test is the
    behavior CI runs.
    """

    STEP_NAME = "- name: Evaluate required jobs"

    def setUp(self):
        self.workflow_text = (ROOT / ".github/workflows/pr.yml").read_text(encoding="utf-8")

    def _step_lines(self):
        """The step's own lines, bounded by the next step at the same indentation."""
        lines = self.workflow_text.splitlines()
        start = next((i for i, line in enumerate(lines) if line.strip() == self.STEP_NAME), None)
        self.assertIsNotNone(start, "pr.yml must define the 'Evaluate required jobs' step")
        step_indent = len(lines[start]) - len(lines[start].lstrip())
        end = len(lines)
        for i in range(start + 1, len(lines)):
            if lines[i].strip() and (len(lines[i]) - len(lines[i].lstrip())) <= step_indent:
                end = i
                break
        return lines[start:end]

    def _gate_step(self):
        """The `run` block of the ci-gate evaluation step, dedented as YAML leaves it."""
        lines = self._step_lines()
        run_at = next((i for i, line in enumerate(lines) if line.strip() == "run: |"), None)
        self.assertIsNotNone(run_at, "the gate step must have a literal 'run: |' block")

        body, indent = [], None
        for line in lines[run_at + 1:]:
            if line.strip() and indent is None:
                indent = len(line) - len(line.lstrip())
            if line.strip() and (len(line) - len(line.lstrip())) < indent:
                break
            body.append(line[indent:] if line.strip() else "")
        script = "\n".join(body).rstrip("\n")
        self.assertTrue(script.strip(), "the gate step's run block must not be empty")
        return script

    def _payload_env_key(self):
        """The env var the step actually passes the payload in.

        Read from the YAML rather than hardcoded, so a rename in pr.yml makes the
        gate fail under test instead of silently blocking every merge in the repo.
        """
        lines = self._step_lines()
        for line in lines:
            match = re.match(r"\s*([A-Z_][A-Z0-9_]*):\s*(\$\{\{\s*toJSON\(needs\)\s*\}\})\s*$", line)
            if match:
                return match.group(1)
        self.fail("the gate step must pass toJSON(needs) through an env var")

    def _run_gate(self, payload, *, raw=None, drop_payload=False):
        env = dict(os.environ)
        key = self._payload_env_key()
        env.pop(key, None)
        if not drop_payload:
            env[key] = raw if raw is not None else json.dumps(payload)
        return subprocess.run(["bash", "-c", self._gate_step()],
                              capture_output=True, text=True, check=False, env=env)

    def _assert_blocks(self, payload, *, names=(), raw=None):
        result = self._run_gate(payload, raw=raw)
        self.assertEqual(1, result.returncode,
                         f"gate must block {payload!r}; stdout={result.stdout!r} stderr={result.stderr!r}")
        self.assertIn("merge blocked", result.stdout + result.stderr)
        for name in names:
            self.assertIn(name, result.stdout + result.stderr,
                          f"the failure must name the offending job {name!r}")

    def _assert_passes(self, payload):
        result = self._run_gate(payload)
        self.assertEqual(0, result.returncode,
                         f"gate must pass {payload!r}; stdout={result.stdout!r} stderr={result.stderr!r}")

    @staticmethod
    def _needs(results, *, only_docs=None):
        """A `toJSON(needs)`-shaped payload: each result nested under its job."""
        payload = {name: {"result": result, "outputs": {}} for name, result in results.items()}
        if only_docs is not None:
            payload["docs-only"]["outputs"]["only_docs"] = only_docs
        return payload

    # --- the regression itself -------------------------------------------------

    def test_abandoned_ci_job_should_block_the_merge(self):
        """The exact #1377 shape: the caller's reusable workflow was abandoned."""
        self._assert_blocks(self._needs(
            {"docs-only": "success", "ci": "abandoned", "goldens": "skipped"}, only_docs="false"),
            names=("ci",))

    def test_abandoned_ci_job_should_not_be_excused_by_a_skipped_sibling(self):
        """In #1377 `goldens` skipped only because its abandoned dependency skipped
        it. A bare 'skipped is fine' would have waved that through."""
        self._assert_blocks(self._needs(
            {"docs-only": "success", "ci": "abandoned", "goldens": "skipped"}, only_docs="false"),
            names=("ci", "goldens"))

    # --- allowlist totality ----------------------------------------------------

    def test_cancelled_job_should_block_the_merge(self):
        self._assert_blocks(self._needs(
            {"docs-only": "success", "ci": "cancelled", "goldens": "success"}), names=("ci",))

    def test_failed_job_should_block_the_merge(self):
        self._assert_blocks(self._needs(
            {"docs-only": "success", "ci": "failure", "goldens": "success"}), names=("ci",))

    def test_unrecognised_result_should_block_the_merge(self):
        """Allowlist by construction: a result nobody anticipated still blocks."""
        for result in ("timed_out", "startup_failure", "stale", "some_future_state"):
            with self.subTest(result=result):
                self._assert_blocks(self._needs(
                    {"docs-only": "success", "ci": result, "goldens": "success"}), names=("ci",))

    def test_null_result_should_block_the_merge(self):
        """A job that never reported a result proves nothing."""
        self._assert_blocks(self._needs(
            {"docs-only": "success", "ci": None, "goldens": "success"}), names=("ci",))

    def test_missing_job_should_block_the_merge(self):
        """A payload that omits a needed job must not pass by omission."""
        self._assert_blocks({"docs-only": {"result": "success", "outputs": {"only_docs": "true"}}},
                            names=("ci", "goldens"))

    def test_unknown_job_should_block_the_merge(self):
        """A new entry in `needs` must be judged explicitly, not ignored."""
        payload = self._needs({"docs-only": "success", "ci": "success", "goldens": "success"})
        payload["security"] = {"result": "success", "outputs": {}}
        self._assert_blocks(payload, names=("security",))

    # --- skips must be justified ----------------------------------------------

    def test_skipped_docs_only_should_block_the_merge(self):
        """`docs-only` has no `if:`, so a skip means the run never executed."""
        self._assert_blocks(self._needs(
            {"docs-only": "skipped", "ci": "skipped", "goldens": "skipped"}), names=("docs-only",))

    def test_skipped_heavy_jobs_without_the_fast_path_should_block_the_merge(self):
        self._assert_blocks(self._needs(
            {"docs-only": "success", "ci": "skipped", "goldens": "skipped"}, only_docs="false"),
            names=("ci", "goldens"))

    def test_skipped_heavy_jobs_without_any_only_docs_output_should_block_the_merge(self):
        self._assert_blocks(self._needs(
            {"docs-only": "success", "ci": "skipped", "goldens": "skipped"}), names=("ci", "goldens"))

    # --- legitimate passes ----------------------------------------------------

    def test_all_success_should_pass(self):
        self._assert_passes(self._needs(
            {"docs-only": "success", "ci": "success", "goldens": "success"}, only_docs="false"))

    def test_docs_only_fast_path_should_pass(self):
        """On a docs-only PR the heavy jobs are deliberately skipped, not failed."""
        self._assert_passes(self._needs(
            {"docs-only": "success", "ci": "skipped", "goldens": "skipped"}, only_docs="true"))

    def test_bare_string_results_should_be_understood(self):
        """`needs.*.result` can arrive without the nesting; both shapes must work."""
        self._assert_passes({"docs-only": "success", "ci": "success", "goldens": "success"})
        self._assert_blocks({"docs-only": "success", "ci": "abandoned", "goldens": "success"},
                            names=("ci",))

    # --- fail closed ----------------------------------------------------------

    def test_empty_results_should_block_the_merge(self):
        result = self._run_gate({})
        self.assertEqual(1, result.returncode)
        self.assertIn("No required job results", result.stdout)
        self.assertNotIn("Traceback", result.stdout + result.stderr)

    def test_absent_results_should_be_reported_cleanly_not_crash(self):
        """A payload that is valid JSON but not an object must block with a
        message, not a traceback: the gate log is the incident record."""
        for payload in ([], "success", 0):
            with self.subTest(payload=payload):
                result = self._run_gate(payload)
                self.assertEqual(1, result.returncode,
                                 f"gate must block {payload!r}; stdout={result.stdout!r}")
                self.assertIn("No required job results", result.stdout)
                self.assertNotIn("Traceback", result.stdout + result.stderr)

    def test_malformed_json_should_block_the_merge(self):
        """Fail closed: an unreadable payload must never read as a pass."""
        self._assert_blocks(None, raw="not json at all")

    def test_absent_payload_should_block_the_merge(self):
        result = self._run_gate(None, drop_payload=True)
        self.assertEqual(1, result.returncode,
                         f"an absent payload must block; stdout={result.stdout!r}")

    # --- the wiring and the guard-the-guard -----------------------------------

    def test_the_step_should_pass_the_whole_needs_object(self):
        """The gate needs per-job names and `docs-only`'s output, not just results."""
        self.assertEqual("REQUIRED_JOBS_JSON", self._payload_env_key())

    def test_the_gate_should_report_what_it_saw(self):
        """The payload echo is what made #1377 diagnosable at all."""
        result = self._run_gate(self._needs(
            {"docs-only": "success", "ci": "success", "goldens": "success"}, only_docs="false"))
        self.assertIn("Required job results:", result.stdout)
        self.assertIn('"ci"', result.stdout)

    def test_the_gate_should_decide_by_allowlist_not_by_name(self):
        """Guard the guard: the pre-#1377 gate grepped for result names.

        The behavioural tests above already prove the allowlist is total, but a
        future edit that reintroduced a name blocklist *and* adjusted the tests
        would be invisible here without pinning the shape.
        """
        script = self._gate_step()
        # The comments explain the old blocklist by name; only the code decides,
        # so a mention in prose must not trip the check.
        code = "\n".join(line for line in script.splitlines()
                         if not line.lstrip().startswith("#"))
        self.assertNotIn("grep", code,
                         "the gate must decide by allowlist, not by matching result names")
        self.assertIn('"docs-only": {"success"}', code,
                      "docs-only must be allowed only as `success`")

    def test_the_gate_should_receive_every_declared_needed_job(self):
        """The payload must carry every `needs` entry, or one could go unevaluated."""
        self.assertIn("toJSON(needs)", self.workflow_text)
        ci_gate = self.workflow_text.split("ci-gate:", 1)[1]
        needs_line = re.search(r"needs:\s*\[([^\]]*)\]", ci_gate)
        self.assertIsNotNone(needs_line, "ci-gate must declare its needs")
        declared = {part.strip() for part in needs_line.group(1).split(",") if part.strip()}
        self.assertEqual({"docs-only", "ci", "goldens"}, declared)
        for job in declared:
            self.assertIn(f'"{job}":', self._gate_step(),
                          f"{job} is in `needs` but the gate has no rule for it")


if __name__ == "__main__":
    unittest.main()
