namespace Zipper.LoadFiles;

/// <summary>
/// Renders <see cref="LoadFileRecord"/> instances to CSV (RFC 4180): comma-separated, with a
/// raw header row and field values quoted only when they contain a comma, quote, or newline.
/// </summary>
internal sealed class CsvSerializer : ILoadFileSerializer
{
    public string FormatName => "CSV";

    public string FileExtension => ".csv";

    public string RenderHeader(IReadOnlyList<string> columns) => string.Join(",", columns);

    public string RenderRecord(LoadFileRecord record) =>
        string.Join(",", record.Columns.Select(c => EscapeCsvField(record.Values.TryGetValue(c, out var v) ? v : string.Empty)));

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
