# Task 4: Set Up a Linter

## Description

To ensure consistent code style and quality, this task is to set up a linter for the project. The linter should be configured to enforce the code style rules defined in `AGENTS.md`.

## Implementation Steps

1.  **Choose a Linter:**
    *   The recommended approach is to use a `.editorconfig` file to define and enforce code style rules. This is supported by Visual Studio and the `dotnet` command-line tool.

2.  **Create a `.editorconfig` File:**
    *   Create a new file named `.editorconfig` in the root of the repository.
    *   Add rules to the `.editorconfig` file to enforce the following code style conventions:
        *   Use `var` where the type is obvious.
        *   Prefer expression-bodied members for simple methods and properties.
        *   Use `_` to discard unused variables.
        *   Use file-scoped namespaces.

3.  **Enable Code Style Enforcement on Build:**
    *   Modify the `Zipper.csproj` file to enable code style enforcement during the build process. This can be done by adding the following property to the `PropertyGroup` section:
        ```xml
        <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
        ```

4.  **Verify the Linter:**
    *   Run a build to ensure that the linter is working correctly. Any code style violations should be reported as build warnings or errors.

## Acceptance Criteria

*   A `.editorconfig` file must be present in the root of the repository.
*   The `.editorconfig` file must contain rules to enforce the code style conventions defined in `AGENTS.md`.
*   Code style enforcement must be enabled on build.
