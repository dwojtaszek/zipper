namespace Zipper.LoadFiles;

/// <summary>
/// Builds a <see cref="LoadFileRecord"/> by binding an ordered value list to the header
/// columns as parallel arrays. Shared by composers so header/value alignment is enforced
/// in exactly one place. The value list is aliased, not copied — callers must not mutate
/// or reuse it after this call.
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

        return new LoadFileRecord { Columns = headerColumns, Values = orderedValues, RecordId = recordId };
    }
}
