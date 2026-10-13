import os
import re
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import workflow_validator  # noqa: E402
from workflow_validator import (
    parse_workflow_yaml,
    validate_artifact_producer,
    validate_artifact_consumer,
    validate_cache_configuration,
    validate_artifact_pipeline,
    validate_artifact_mutations,
    run_artifact_validation_cli,
    WorkflowValidationError,
)

REPO_ROOT = str(Path(__file__).resolve().parents[3])
CI_WORKFLOW_PATH = os.path.join(REPO_ROOT, ".github", "workflows", "ci.yml")
RELEASE_WORKFLOW_PATH = os.path.join(REPO_ROOT, ".github", "workflows", "build-and-test.yml")
COVERAGE_ACTION_PATH = os.path.join(REPO_ROOT, ".github", "actions", "coverage-gate", "action.yml")
FIXTURES_DIR = os.path.join(REPO_ROOT, "tests", "typesafe-audit-fixtures", "artifacts", "cases")


class TestArtifactValidator(unittest.TestCase):
    def setUp(self):
        with open(CI_WORKFLOW_PATH, "r", encoding="utf-8") as f:
            self.ci_text = f.read()
        self.ci_workflow = parse_workflow_yaml(self.ci_text)

        with open(RELEASE_WORKFLOW_PATH, "r", encoding="utf-8") as f:
            self.release_text = f.read()
        self.release_workflow = parse_workflow_yaml(self.release_text)

        with open(COVERAGE_ACTION_PATH, "r", encoding="utf-8") as f:
            self.coverage_text = f.read()
        self.coverage_action = parse_workflow_yaml(self.coverage_text)

    def test_real_workflows_pass_pipeline_validation(self):
        errors = validate_artifact_pipeline(
            CI_WORKFLOW_PATH, RELEASE_WORKFLOW_PATH, COVERAGE_ACTION_PATH
        )
        self.assertEqual(errors, [], f"Real workflow validation failed: {errors}")

    def test_comment_only_workflow_fails(self):
        comment_only = "\n".join(f"# {line}" for line in self.ci_text.splitlines())
        parsed = parse_workflow_yaml(comment_only)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed)
        err = str(ctx.exception).lower()
        self.assertTrue("job" in err or "empty" in err or "missing" in err)

    def test_producer_missing_upload_step_fails(self):
        mutated = re.sub(
            r"uses:\s*actions/upload-artifact@[^\n]+",
            "# uses: actions/upload-artifact@mutated",
            self.ci_text,
        )
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed)
        self.assertIn("upload-artifact", str(ctx.exception).lower())

    def test_upload_in_wrong_job_fails(self):
        # Match upload step generically
        match = re.search(r"(\s+-\s+name:\s+Upload self-contained binaries[\s\S]+?retention-days:\s*\d+)", self.ci_text)
        self.assertIsNotNone(match)
        upload_block = match.group(1)
        mutated = self.ci_text.replace(upload_block, "\n        # removed upload from producer\n")
        mutated = mutated.replace("    steps:\n", f"    steps:\n{upload_block}\n", 1)
        parsed = parse_workflow_yaml(mutated)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed)
        self.assertIn("build-and-test", str(ctx.exception))
        self.assertIn("upload-artifact", str(ctx.exception).lower())

    def test_mismatched_upload_name_fails(self):
        mutated_ci = self.ci_text.replace(
            "name: zipper-${{ matrix.platform }}",
            "name: zipper-pkg-${{ matrix.platform }}",
        )
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("name", str(ctx.exception).lower())

    def test_missing_retention_days_fails(self):
        mutated_ci = re.sub(r"retention-days: \d+", "retention-days: 0", self.ci_text)
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("retention-days", str(ctx.exception).lower())

    def test_boolean_retention_days_fails(self):
        mutated_ci = re.sub(r"retention-days: \d+", "retention-days: true", self.ci_text)
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("retention-days", str(ctx.exception).lower())

    def test_missing_self_contained_publish_fails(self):
        mutated_ci = self.ci_text.replace("--self-contained true", "")
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("self-contained", str(ctx.exception).lower())

    def test_commented_shell_command_in_run_fails(self):
        mutated_ci = self.ci_text.replace(
            "rm -f publish/${{ matrix.platform }}/*.pdb",
            "# rm -f publish/${{ matrix.platform }}/*.pdb",
        )
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("pdb", str(ctx.exception).lower())

    def test_omitted_platform_fails(self):
        mutated_ci = self.ci_text.replace(
            "- os: macos-latest\n            platform: osx-arm64\n            coverage: false",
            "# omitted osx-arm64",
        )
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("osx-arm64", str(ctx.exception))

    def test_matrix_flow_style_platforms_pass(self):
        flow_yaml = """name: Flow Matrix CI
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        platform: [win-x64, linux-x64, osx-arm64]
    steps:
      - name: Publish
        run: |
          dotnet publish -c Release -r ${{ matrix.platform }} --self-contained true -o publish/${{ matrix.platform }}
          rm -f publish/${{ matrix.platform }}/*.pdb
          case "${{ matrix.platform }}" in
            win-x64)   mv publish/${{ matrix.platform }}/Zipper.exe publish/${{ matrix.platform }}/zipper-win-x64.exe ;;
            linux-x64) mv publish/${{ matrix.platform }}/Zipper     publish/${{ matrix.platform }}/zipper-linux-x64 ;;
            osx-arm64) mv publish/${{ matrix.platform }}/Zipper     publish/${{ matrix.platform }}/zipper-osx-arm64 ;;
          esac
      - name: Upload
        uses: actions/upload-artifact@v4
        with:
          name: zipper-${{ matrix.platform }}
          path: publish/${{ matrix.platform }}
          retention-days: 7
"""
        parsed = parse_workflow_yaml(flow_yaml)
        # Should not raise
        validate_artifact_producer(parsed)

    def test_consumer_missing_needs_ci_fails(self):
        mutated_rel = self.release_text.replace("needs: [prepare, ci]", "needs: [prepare]")
        parsed_rel = parse_workflow_yaml(mutated_rel)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_consumer(parsed_rel)
        self.assertIn("needs", str(ctx.exception).lower())

    def test_consumer_download_with_name_fails(self):
        mutated_rel = re.sub(
            r"(uses:\s*actions/download-artifact@[^\n]+\n(?:\s+if:[^\n]+\n)?\s+with:\n)",
            r"\1          name: custom-name\n",
            self.release_text,
        )
        parsed_rel = parse_workflow_yaml(mutated_rel)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_consumer(parsed_rel)
        self.assertIn("name", str(ctx.exception).lower())

    def test_changed_release_paths_fails(self):
        mutated_rel = self.release_text.replace(
            "--artifacts artifacts",
            "--artifacts different",
        )
        parsed_rel = parse_workflow_yaml(mutated_rel)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_consumer(parsed_rel)
        self.assertIn("release", str(ctx.exception).lower())

    def test_commented_release_controller_fails(self):
        mutated = self.release_text.replace(
            "python3 .github/scripts/release.py publish",
            "# python3 .github/scripts/release.py publish",
        )
        with self.assertRaises(WorkflowValidationError):
            validate_artifact_consumer(parse_workflow_yaml(mutated))

    def test_release_controller_without_immutable_download_fails(self):
        mutated = self.release_text.replace(
            "artifact-ids: ${{ needs.prepare.outputs.artifact_ids }}",
            "pattern: zipper-*",
        )
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_consumer(parse_workflow_yaml(mutated))
        self.assertIn("artifact-ids", str(ctx.exception))

    def test_missing_pdb_cleanup_fails(self):
        mutated_ci = self.ci_text.replace(
            "rm -f publish/${{ matrix.platform }}/*.pdb",
            "# pdb cleanup omitted",
        )
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("pdb", str(ctx.exception).lower())

    def test_missing_rename_pattern_fails(self):
        mutated_ci = self.ci_text.replace(
            "win-x64)   mv publish/${{ matrix.platform }}/Zipper.exe publish/${{ matrix.platform }}/zipper-win-x64.exe ;;",
            "# rename omitted",
        )
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed_ci)
        self.assertIn("rename", str(ctx.exception).lower())

    def test_missing_download_step_fails(self):
        mutated_rel = re.sub(
            r"uses:\s*actions/download-artifact@[^\n]+",
            "# uses: actions/download-artifact@mutated",
            self.release_text,
        )
        parsed_rel = parse_workflow_yaml(mutated_rel)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_consumer(parsed_rel)
        self.assertIn("download-artifact", str(ctx.exception).lower())

    def test_cache_setup_dotnet_missing_cache_fails(self):
        mutated_ci = self.ci_text.replace("cache: true", "# cache: true")
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_cache_configuration(parsed_ci, self.coverage_action)
        self.assertIn("cache", str(ctx.exception).lower())

    def test_cache_setup_dotnet_missing_dependency_path_fails(self):
        mutated_ci = self.ci_text.replace(
            "cache-dependency-path: '**/packages.lock.json'",
            "# dependency path removed",
        )
        parsed_ci = parse_workflow_yaml(mutated_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_cache_configuration(parsed_ci, self.coverage_action)
        self.assertIn("cache-dependency-path", str(ctx.exception).lower())

    def test_cache_coverage_gate_missing_cache_fails(self):
        mutated_action = re.sub(
            r"uses:\s*actions/cache@[^\n]+",
            "# uses: actions/cache@mutated",
            self.coverage_text,
        )
        parsed_action = parse_workflow_yaml(mutated_action)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_cache_configuration(self.ci_workflow, parsed_action)
        self.assertIn("cache", str(ctx.exception).lower())

    def test_decoy_comments_do_not_satisfy_producer(self):
        decoy_ci = """name: Decoy CI
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        include:
          - platform: win-x64
          - platform: linux-x64
          - platform: osx-arm64
    steps:
      - name: Dummy publish
        run: |
          dotnet publish --self-contained true -o publish/${{ matrix.platform }}
          rm -f publish/${{ matrix.platform }}/*.pdb
          mv publish/Zipper zipper-win-x64.exe
          mv publish/Zipper zipper-linux-x64
          mv publish/Zipper zipper-osx-arm64
      # uses: actions/upload-artifact@v4
      # with:
      #   name: zipper-${{ matrix.platform }}
      #   path: publish/${{ matrix.platform }}
      - name: Dummy Step
        run: echo "not producing artifacts"
"""
        parsed = parse_workflow_yaml(decoy_ci)
        with self.assertRaises(WorkflowValidationError) as ctx:
            validate_artifact_producer(parsed)
        self.assertIn("upload-artifact", str(ctx.exception).lower())

    def test_mutation_fixtures(self):
        if not os.path.isdir(FIXTURES_DIR):
            self.skipTest(f"Fixtures directory {FIXTURES_DIR} not found")
        errors = validate_artifact_mutations(FIXTURES_DIR)
        self.assertEqual(errors, [], f"Mutation fixture validation failed: {errors}")

    def test_cli_unexpected_positional_arg_rejected(self):
        exit_code = run_artifact_validation_cli(["unexpected_positional_path"])
        self.assertEqual(exit_code, 1)

    def test_cli_unrecognized_option_rejected(self):
        exit_code = run_artifact_validation_cli(["--unrecognized-option"])
        self.assertEqual(exit_code, 1)

    def test_cli_missing_arg_value_rejected(self):
        exit_code = run_artifact_validation_cli(["--ci-workflow"])
        self.assertEqual(exit_code, 1)


if __name__ == "__main__":
    unittest.main()
