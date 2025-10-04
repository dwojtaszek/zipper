# Task 3: Improve Code Quality and Maintainability

## Description

The `Program.cs` file has some areas that could be improved for better readability and maintainability. This task is to address these issues.

## Acceptance Criteria

*   The "magic numbers" `(char)20` and `(char)254` in `Program.cs` must be replaced with named constants (e.g., `const char ColumnDelimiter = (char)20;`).
*   (Optional but recommended) Consider using a library like `System.CommandLine` or `CommandLineParser` to handle command-line argument parsing. This would simplify the code and provide more robust error handling.
