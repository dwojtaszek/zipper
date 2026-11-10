namespace Zipper;

/// <summary>
/// Consumer outcome classes recorded for every injected anomaly in a Chaos expectation sidecar.
/// </summary>
internal static class ChaosOutcomeClasses
{
    /// <summary>The affected record, document, or native artifact cannot be parsed by a consumer.</summary>
    public const string ParseFails = "parse-fails";

    /// <summary>The artifact still parses, but a value, record, or relationship is wrong or missing.</summary>
    public const string ParseSucceedsWithLoss = "parse-succeeds-with-loss";

    /// <summary>Conforming consumers observe no parse error and no detectable loss.</summary>
    public const string SilentPass = "silent-pass";

    /// <summary>Every valid outcome class.</summary>
    public static readonly IReadOnlyList<string> All = [ParseFails, ParseSucceedsWithLoss, SilentPass];
}

/// <summary>
/// Recorded-outcome oracle for Chaos runs: maps every anomaly type to the outcome a consumer
/// should observe. The Archive Test Expectation Files proved this pattern for fixtures; this is
/// the Chaos-run equivalent.
/// </summary>
internal static class ChaosExpectationOracle
{
    private static readonly Dictionary<string, string> OutcomeClasses = new(StringComparer.Ordinal)
    {
        // DAT line-level anomalies.
        ["mixed-delimiters"] = ChaosOutcomeClasses.ParseFails,
        ["quotes"] = ChaosOutcomeClasses.ParseFails,
        ["columns"] = ChaosOutcomeClasses.ParseFails,
        ["eol"] = ChaosOutcomeClasses.ParseFails,
        ["encoding"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["path-pointer"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["field-length"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["date-format"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["null-byte"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["trailing-empties"] = ChaosOutcomeClasses.ParseFails,

        // OPT line-level anomalies.
        ["opt-boundary"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["opt-columns"] = ChaosOutcomeClasses.ParseFails,
        ["opt-pagecount"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["opt-path"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["opt-batesnumber"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["opt-encoding"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,

        // CSV anomalies.
        ["csv-quote-escape"] = ChaosOutcomeClasses.ParseFails,
        ["csv-embedded-newline"] = ChaosOutcomeClasses.ParseFails,
        ["csv-bom"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,

        // Concordance anomalies.
        ["concordance-delimiter"] = ChaosOutcomeClasses.ParseFails,
        ["concordance-quote-escape"] = ChaosOutcomeClasses.ParseFails,

        // EDRM-XML anomalies.
        ["xml-unclosed-tag"] = ChaosOutcomeClasses.ParseFails,
        ["xml-bad-entity"] = ChaosOutcomeClasses.ParseFails,
        ["xml-encoding-lie"] = ChaosOutcomeClasses.ParseFails,

        // Coordinated cross-format anomalies: structure holds, relationships or values do not.
        ["desync-pagecount"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["desync-order"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["desync-path"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["orphan-attachment"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["missing-child"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["broken-parent"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,

        // Email/MIME anomalies.
        ["mime-boundary"] = ChaosOutcomeClasses.ParseFails,
        ["encoded-word"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["header-fold"] = ChaosOutcomeClasses.SilentPass,
        ["rf822-depth"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,

        // TIFF anomalies.
        ["tiff-dimension-lie"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        ["tiff-truncated-ifd"] = ChaosOutcomeClasses.ParseFails,
        ["tiff-tag-count-lie"] = ChaosOutcomeClasses.ParseFails,
        ["tiff-strip-offset"] = ChaosOutcomeClasses.ParseSucceedsWithLoss,
    };

    /// <summary>
    /// Anomaly types whose affected record must still parse: the corruption changes a value but not the field structure.
    /// </summary>
    public static readonly IReadOnlySet<string> ValueLevelTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "path-pointer", "field-length", "date-format", "null-byte",
        "opt-boundary", "opt-pagecount", "opt-path",
        "desync-pagecount", "desync-order", "desync-path",
        "orphan-attachment", "missing-child", "broken-parent",
    };

    /// <summary>
    /// Anomaly types that are guaranteed to break the field count of the affected record.
    /// Quote and newline anomalies are excluded because whether they break the count depends on where the corruption lands.
    /// </summary>
    public static readonly IReadOnlySet<string> StructuralCountTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "mixed-delimiters", "trailing-empties", "opt-columns", "concordance-delimiter",
    };

    /// <summary>
    /// Anomaly types that split one record across two physical lines.
    /// </summary>
    public static readonly IReadOnlySet<string> RecordSplittingTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "eol", "csv-embedded-newline",
    };

    /// <summary>
    /// Returns the expected consumer outcome class for an anomaly type.
    /// </summary>
    /// <exception cref="ArgumentException">No outcome class is defined for the anomaly type.</exception>
    public static string ExpectedOutcomeClass(string anomalyType)
    {
        ArgumentNullException.ThrowIfNull(anomalyType);
        string canonical = ChaosAnomalyTypes.CanonicalEmailType(anomalyType);
        return OutcomeClasses.TryGetValue(canonical, out var outcomeClass)
            ? outcomeClass
            : throw new ArgumentException(
                $"No expected outcome class is defined for chaos anomaly type '{anomalyType}'.", nameof(anomalyType));
    }
}
