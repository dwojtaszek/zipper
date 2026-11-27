namespace Zipper.LoadFiles;

/// <summary>
/// Renders <see cref="LoadFileRecord"/> instances to Concordance DAT: every field (header and
/// value alike) is wrapped when a quote delimiter is enabled (ASCII 254 by default) and joined by the configured
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
        string.Join(this.fieldDelim, columns.Select(c => this.WrapField(this.EscapeValue(c))));

    public string RenderRecord(LoadFileRecord record) =>
        string.Join(
            this.fieldDelim,
            record.Columns.Select((_, i) => this.WrapField(this.EscapeValue(i < record.Values.Count ? record.Values[i] : string.Empty))));

    private string WrapField(string value) =>
        this.hasQuote ? $"{this.quoteDelim}{value}{this.quoteDelim}" : value;

    private string EscapeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return this.hasQuote && value.Contains(this.quoteDelim, StringComparison.Ordinal)
            ? value.Replace(this.quoteDelim.ToString(), new string(this.quoteDelim, 2), StringComparison.Ordinal)
            : value;
    }
}
