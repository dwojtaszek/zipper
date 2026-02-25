namespace Zipper;

/// <summary>
/// Represents a single anomaly injected by the Chaos Engine.
/// </summary>
internal record ChaosAnomaly
{
    /// <summary>
    /// Gets or sets the line number where the anomaly was injected.
    /// For encoding errors between lines, uses "Boundary N-M" format.
    /// </summary>
    public string LineNumber { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the record ID affected (e.g., "DOC00001054" or "HEADER").
    /// </summary>
    public string RecordID { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the column name affected, or "N/A" for structural errors.
    /// </summary>
    public string Column { get; init; } = "N/A";

    /// <summary>
    /// Gets or sets the error type name (e.g., "mixed-delimiters", "quotes").
    /// </summary>
    public string ErrorType { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets a human-readable description of the anomaly.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}
