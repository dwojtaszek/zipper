namespace Zipper.LoadFiles;

/// <summary>
/// A single row of load file data, independent of output format.
/// Column names are ordered; values are keyed by column name and held raw
/// (unescaped) — the serializer applies format-specific escaping.
/// </summary>
internal sealed class LoadFileRecord
{
    /// <summary>
    /// Gets the ordered column names for this record set.
    /// </summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>
    /// Gets the raw (unescaped) column values keyed by column name.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Values { get; init; }

    /// <summary>
    /// Gets the record identifier used for chaos auditing (e.g. control number or Bates).
    /// </summary>
    public string RecordId { get; init; } = string.Empty;
}
