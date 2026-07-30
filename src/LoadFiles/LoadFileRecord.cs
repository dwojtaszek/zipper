namespace Zipper.LoadFiles;

/// <summary>
/// A single row of load file data, independent of output format.
/// Column names are ordered; values are held raw (unescaped) in a parallel
/// array aligned with the columns by index — the serializer applies
/// format-specific escaping.
/// </summary>
internal sealed class LoadFileRecord
{
    /// <summary>
    /// Gets the ordered column names for this record set.
    /// </summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>
    /// Gets the raw (unescaped) column values, aligned with <see cref="Columns"/> by index.
    /// </summary>
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary>
    /// Gets the record identifier used for chaos auditing (e.g. control number or Bates).
    /// </summary>
    public string RecordId { get; init; } = string.Empty;
}
