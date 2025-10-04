# Task 5: Improve Build and Test Workflow

## Description

To improve the efficiency and reliability of the CI/CD process, this task is to refactor the existing `build.yml` and `test.yml` workflows into a single, streamlined workflow. The new workflow will incorporate linting, parallel builds, and testing of the built artifacts.

## Implementation Steps

1.  **Combine Workflows:**
    *   Create a new workflow file named `build-and-test.yml` in the `.github/workflows` directory.
    *   Copy the existing build and test steps from `build.yml` and `test.yml` into the new file.
    *   Delete the old `build.yml` and `test.yml` files.

2.  **Add Linting Job:**
    *   Add a new job named `lint` to the beginning of the workflow.
    *   This job should use the `.editorconfig` file to check for code style violations.
    *   The build should fail if any linting errors are found.

3.  **Parallelize Builds:**
    *   Modify the `build` job to use a matrix strategy to run builds for Windows, Linux, and macOS in parallel.
    *   Each build in the matrix should produce a platform-specific artifact.

4.  **Add Test Job:**
    *   Add a new job named `test` that depends on the successful completion of the `build` job.
    *   The `test` job should download the build artifacts from the `build` job.
    -   The `test` job should also use a matrix strategy to run tests on all three platforms.
    *   The `test` job should run the tests against the downloaded artifacts.

5.  **Conditional Release:**
    *   Ensure the `release` job only runs if the `lint`, `build`, and `test` jobs are successful.
    *   The `release` job should download the artifacts from the `build` job.

## Acceptance Criteria

*   A single `build-and-test.yml` workflow file must be present in the `.github/workflows` directory.
*   The workflow must include a `lint` job that runs before the `build` job.
*   The `build` job must run in parallel for Windows, Linux, and macOS.
*   The `test` job must run after the `build` job and use the artifacts from the `build` job.
*   The `release` job must only run on successful completion of all previous jobs.
