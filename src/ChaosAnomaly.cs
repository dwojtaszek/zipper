using System.Text.Json.Serialization;

namespace Zipper;

/// <summary>
/// Represents a peer reference to a coordinated anomaly in a companion load file.
/// </summary>
internal record ChaosAnomalyPeer
{
    /// <summary>
    /// Gets the file name where the peer anomaly was injected (e.g., "loadfile.opt").
    /// </summary>
    public string File { get; init; } = string.Empty;

    /// <summary>
    /// Gets the line number where the peer anomaly was injected.
    /// </summary>
    public string LineNumber { get; init; } = string.Empty;

    /// <summary>
    /// Gets the record ID affected in the peer file (e.g., "DOC00001054").
    /// </summary>
    public string RecordID { get; init; } = string.Empty;

    /// <summary>
    /// Gets the column name affected in the peer file, or "N/A" for structural errors.
    /// </summary>
    public string Column { get; init; } = "N/A";
}

/// <summary>
/// Represents a single anomaly injected by the Chaos Engine.
/// </summary>
internal record ChaosAnomaly
{
    /// <summary>
    /// Gets or sets the optional file name where the anomaly was injected (e.g., "loadfile.dat").
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? File { get; init; }

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

    /// <summary>
    /// Gets or sets an optional peer reference identifying the corresponding coordinated anomaly
    /// in the companion load file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChaosAnomalyPeer? Peer { get; init; }
}
