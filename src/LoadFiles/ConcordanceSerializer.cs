namespace Zipper.LoadFiles;

/// <summary>
/// Renders <see cref="LoadFileRecord"/> instances to Concordance DAT: every field (header and
/// value alike) is wrapped in the quote delimiter (ASCII 254) and joined by the configured
/// column delimiter (ASCII 20 by default). Values have the quote character doubled when
/// present; embedded newlines are left intact (Concordance does not sanitize them).
/// </summary>
internal sealed class ConcordanceSerializer : ILoadFileSerializer
{
    private const char QuoteDelim = 'þ'; // ASCII 254 — Concordance standard quote character

    private readonly char fieldDelim;

    public ConcordanceSerializer(FileGenerationRequest request)
    {
        this.fieldDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter)
            ? request.Delimiters.ColumnDelimiter[0]
            : '\u0014';
    }

    public string FormatName => "CONCORDANCE";

    public string FileExtension => ".dat";

    public string RenderHeader(IReadOnlyList<string> columns) =>
        string.Join(this.fieldDelim, columns.Select(c => $"{QuoteDelim}{c}{QuoteDelim}"));

    public string RenderRecord(LoadFileRecord record) =>
        string.Join(
            this.fieldDelim,
            record.Columns.Select(c => $"{QuoteDelim}{Escape(record.Values.TryGetValue(c, out var v) ? v : string.Empty)}{QuoteDelim}"));

    private static string Escape(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        return field.Contains(QuoteDelim, StringComparison.Ordinal)
            ? field.Replace(QuoteDelim.ToString(), new string(QuoteDelim, 2), StringComparison.Ordinal)
            : field;
    }
}
