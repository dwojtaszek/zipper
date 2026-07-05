namespace Zipper.Validation;

public sealed record ValidationFinding(
    ValidationSeverity Severity,
    string Category,
    string Message,
    string? FilePath = null,
    long? LineNumber = null);
