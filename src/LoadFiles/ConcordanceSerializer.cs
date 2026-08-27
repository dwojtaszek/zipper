namespace Zipper.LoadFiles;

/// <summary>
/// Renders <see cref="LoadFileRecord"/> instances to Concordance DAT: every field (header and
/// value alike) is wrapped in the quote delimiter (ASCII 254) and joined by the configured
/// column delimiter (ASCII 20 by default). Values have the quote character doubled when
/// present; embedded newlines are left intact (Concordance does not sanitize them).
/// </summary>
internal sealed class ConcordanceSerializer : ILoadFileSerializer
{
    private readonly char fieldDelim;
    private readonly char quoteDelim;
    private readonly bool hasQuote;

    public ConcordanceSerializer(FileGenerationRequest request)
    {
        this.fieldDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter)
            ? request.Delimiters.ColumnDelimiter[0]
            : '\u0014';
        this.hasQuote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter);
        this.quoteDelim = this.hasQuote ? request.Delimiters.QuoteDelimiter[0] : 'þ';
    }

    public string FormatName => "CONCORDANCE";

    public string FileExtension => ".dat";

    public string RenderHeader(IReadOnlyList<string> columns) =>
        string.Join(this.fieldDelim, columns.Select(c => $"{this.quoteDelim}{EscapeHeader(c)}{this.quoteDelim}"));

    public string RenderRecord(LoadFileRecord record) =>
        string.Join(
            this.fieldDelim,
            record.Columns.Select((_, i) => $"{this.quoteDelim}{Escape(i < record.Values.Count ? record.Values[i] : string.Empty)}{this.quoteDelim}"));

    private string Escape(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        return this.hasQuote && field.Contains(this.quoteDelim, StringComparison.Ordinal)
            ? field.Replace(this.quoteDelim.ToString(), new string(this.quoteDelim, 2), StringComparison.Ordinal)
            : field;
    }

    private string EscapeHeader(string header)
    {
        if (string.IsNullOrEmpty(header))
        {
            return string.Empty;
        }

        if (this.hasQuote && header.Contains(this.quoteDelim, StringComparison.Ordinal))
        {
            return header.Replace(this.quoteDelim.ToString(), new string(this.quoteDelim, 2), StringComparison.Ordinal);
        }

        return header;
    }
}
