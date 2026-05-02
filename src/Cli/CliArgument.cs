namespace Zipper.Cli;

internal record CliArgument(
    string Flag,
    string? ValueName,
    string Description,
    string Category,
    bool IsRequired = false,
    string? DefaultValue = null,
    string[]? ValidValues = null,
    string? ValidationPattern = null);
