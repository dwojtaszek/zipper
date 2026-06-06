using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Renders <see cref="LoadFileRecord"/> instances to standard DAT (Concordance) format
/// (ASCII 20 column delimiter, ASCII 254 quote delimiter by default).
/// Field values are quote-wrapped, the quote character is doubled when it appears in a
/// value, and embedded newlines are replaced with the configured newline delimiter.
/// </summary>
internal sealed class DatSerializer : ILoadFileSerializer
{
    private readonly char columnDelimiter;
    private readonly char quoteDelimiter;
    private readonly bool hasQuote;
    private readonly string newlineDelimiter;

    public DatSerializer(char columnDelimiter = '\x14', char quoteDelimiter = '\xfe', string newlineDelimiter = "\xae")
    {
        this.columnDelimiter = columnDelimiter;
        this.quoteDelimiter = quoteDelimiter;
        this.hasQuote = quoteDelimiter != '\0';
        this.newlineDelimiter = newlineDelimiter;
    }

    public DatSerializer(FileGenerationRequest request)
    {
        // Mirror the defensive fallbacks the legacy writer used so unset delimiters
        // cannot throw or drift from historical output.
        this.columnDelimiter = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter)
            ? request.Delimiters.ColumnDelimiter[0]
            : '\x14';
        this.hasQuote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter);
        this.quoteDelimiter = this.hasQuote ? request.Delimiters.QuoteDelimiter[0] : '\xfe';
        this.newlineDelimiter = request.Delimiters.NewlineDelimiter;
    }

    public string FormatName => "DAT";

    public string FileExtension => ".dat";

    public string RenderHeader(IReadOnlyList<string> columns)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(this.columnDelimiter);
            }

            this.AppendField(sb, columns[i]);
        }

        return sb.ToString();
    }

    public string RenderRecord(LoadFileRecord record)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < record.Columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(this.columnDelimiter);
            }

            var value = record.Values.TryGetValue(record.Columns[i], out var v) ? v : string.Empty;
            this.AppendField(sb, this.EscapeField(value));
        }

        return sb.ToString();
    }

    private void AppendField(StringBuilder sb, string value)
    {
        if (this.hasQuote)
        {
            sb.Append(this.quoteDelimiter);
            sb.Append(value);
            sb.Append(this.quoteDelimiter);
        }
        else
        {
            sb.Append(value);
        }
    }

    private string EscapeField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return field;
        }

        var result = field;
        if (this.hasQuote && result.Contains(this.quoteDelimiter))
        {
            result = result.Replace(
                this.quoteDelimiter.ToString(),
                new string(this.quoteDelimiter, 2));
        }

        if (!string.IsNullOrEmpty(this.newlineDelimiter))
        {
            result = result.Replace("\r\n", this.newlineDelimiter)
                           .Replace("\n", this.newlineDelimiter)
                           .Replace("\r", this.newlineDelimiter);
        }

        return result;
    }
}
