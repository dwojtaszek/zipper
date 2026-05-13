namespace Zipper.Cli;

/// <summary>
/// Centralized catalog of CLI option metadata.
/// Single source of truth for flag names and whether a flag takes a value.
/// </summary>
internal static class CliOptions
{
    /// <summary>
    /// All flags that accept no following value.
    /// Used by <see cref="CliParser"/> to prevent the next argument being consumed
    /// as a value when it is itself a flag.
    /// </summary>
    public static readonly IReadOnlySet<string> ParameterlessFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "--benchmark",
        "--chaos-list",
        "--chaos-mode",
        "--include-load-file",
        "--loadfile-only",
        "--production-set",
        "--production-zip",
        "--with-families",
        "--with-metadata",
        "--with-text",
    };
}
