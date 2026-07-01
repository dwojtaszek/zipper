namespace Zipper;

/// <summary>
/// Canonical catalog of Chaos anomaly type names.
/// Single source of truth consumed by both CliValidator and ChaosEngine.
/// </summary>
internal static class ChaosAnomalyTypes
{
    public static readonly IReadOnlyList<string> Dat = new[]
    {
        "mixed-delimiters", "quotes", "columns", "eol", "encoding",
    };

    public static readonly IReadOnlyList<string> Opt = new[]
    {
        "opt-boundary", "opt-columns", "opt-pagecount", "opt-path", "opt-batesnumber",
    };

    /// <summary>
    /// Returns the valid anomaly types for the given format.
    /// </summary>
    public static IReadOnlyList<string> ForFormat(LoadFileFormat format) =>
        format switch
        {
            LoadFileFormat.Opt => Opt,
            _ => Dat,
        };
}
