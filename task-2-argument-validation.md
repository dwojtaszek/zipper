# Task 2: Refactor Command-Line Argument Parsing

## Description

The current command-line argument parsing in `Program.cs` is done manually, which is error-prone and hard to maintain. This task is to refactor the argument parsing to use the `System.CommandLine` library.

## Implementation Steps

1.  **Add the `System.CommandLine` NuGet Package:**
    *   Add the `System.CommandLine` package to the `Zipper.csproj` file.

2.  **Create a `RootCommand`:**
    *   In `Program.cs`, create a `RootCommand` to represent the `zipper` application.

3.  **Define Options:**
    *   For each command-line argument, create a corresponding `Option` object. For example:
        ```csharp
        var typeOption = new Option<string>(
            name: "--type",
            description: "The type of file to generate.");
        typeOption.IsRequired = true;
        ```

4.  **Add Validators:**
    *   Add validators to the options to enforce the acceptance criteria.
    *   For the `--type` option, add a validator to ensure the value is one of `pdf`, `jpg`, `tiff`, or `eml`.
    *   Create a command-level validator to ensure that `--attachment-rate` is only used with `--type eml`.

5.  **Create a `CommandHandler`:**
    *   Use the `CommandHandler.Create` method to create a handler that will be executed when the command is invoked.
    *   This handler will receive the parsed and validated arguments as parameters.

6.  **Invoke the Command:**
    *   In the `Main` method, use the `rootCommand.InvokeAsync(args)` method to parse the arguments and execute the handler.

## Acceptance Criteria

*   The application must use the `System.CommandLine` library to parse all command-line arguments.
*   The application must exit with a clear error message if the `--attachment-rate` argument is used with any file type other than `eml`.
*   The application must exit with a clear error message if the `--type` argument is not one of the supported values (`pdf`, `jpg`, `tiff`, `eml`).
*   All existing command-line arguments and their behaviors must be preserved.