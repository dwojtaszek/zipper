namespace Zipper.LoadFiles;

/// <summary>
/// Builds a <see cref="LoadFileRecord"/> by zipping an ordered value list onto the header
/// columns. Shared by composers so header/value alignment is enforced in exactly one place.
/// </summary>
internal static class LoadFileRecordBuilder
{
    internal static LoadFileRecord Build(IReadOnlyList<string> headerColumns, IReadOnlyList<string> orderedValues, string recordId)
    {
        if (orderedValues.Count != headerColumns.Count)
        {
            throw new InvalidOperationException(
                $"Load file value count {orderedValues.Count} does not match header column count {headerColumns.Count}.");
        }

        var values = new Dictionary<string, string>(headerColumns.Count, StringComparer.Ordinal);
        for (int i = 0; i < headerColumns.Count; i++)
        {
            values[headerColumns[i]] = orderedValues[i];
        }

        return new LoadFileRecord { Columns = headerColumns, Values = values, RecordId = recordId };
    }
}
