using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Serializes <see cref="LoadFileRecord"/> instances to standard DAT format
/// (ASCII 20 column delimiter, ASCII 254 quote delimiter).
/// </summary>
internal sealed class DatSerializer : ILoadFileSerializer
{
    private readonly char columnDelimiter;
    private readonly char quoteDelimiter;
    private readonly bool hasQuote;
    private readonly string newlineDelimiter;
    private readonly Encoding encoding;
    private StreamWriter? writer;
    private Stream? boundStream;

    public DatSerializer(char columnDelimiter = '\x14', char quoteDelimiter = '\xfe', string newlineDelimiter = "\xae")
    {
        this.columnDelimiter = columnDelimiter;
        this.quoteDelimiter = quoteDelimiter;
        this.hasQuote = quoteDelimiter != '\0';
        this.newlineDelimiter = newlineDelimiter;
        this.encoding = Encoding.UTF8;
    }

    public DatSerializer(FileGenerationRequest request)
    {
        this.columnDelimiter = request.Delimiters.GetColumnChar();
        this.quoteDelimiter = request.Delimiters.GetQuoteChar();
        this.hasQuote = this.quoteDelimiter != '\0';
        this.newlineDelimiter = request.Delimiters.NewlineDelimiter;
        this.encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
    }

    public string FormatName => "DAT";

    public string FileExtension => ".dat";

    public async Task WriteHeaderAsync(Stream stream, IReadOnlyList<string> columns)
    {
        this.EnsureWriter(stream);
        var sb = new StringBuilder();
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(this.columnDelimiter);
            }

            this.AppendField(sb, columns[i]);
        }

        await this.writer!.WriteLineAsync(sb.ToString());
    }

    public async Task WriteRecordAsync(Stream stream, LoadFileRecord record)
    {
        this.EnsureWriter(stream);
        var sb = new StringBuilder();
        for (int i = 0; i < record.Columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(this.columnDelimiter);
            }

            var value = record.Values.TryGetValue(record.Columns[i], out var v) ? v : string.Empty;
            var escaped = this.EscapeField(value);
            this.AppendField(sb, escaped);
        }

        await this.writer!.WriteLineAsync(sb.ToString());
    }

    public async Task FlushAsync(Stream stream)
    {
        if (this.writer != null)
        {
            await this.writer!.FlushAsync();
        }
    }

    private void EnsureWriter(Stream stream)
    {
        if (this.writer == null || this.boundStream != stream)
        {
            this.writer = new StreamWriter(stream, this.encoding, leaveOpen: true);
            this.boundStream = stream;
        }
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
