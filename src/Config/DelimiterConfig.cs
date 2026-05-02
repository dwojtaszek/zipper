namespace Zipper.Config;

public record DelimiterConfig
{
    public string ColumnDelimiter { get; init; } = "\u0014";

    public string QuoteDelimiter { get; init; } = "\u00fe";

    public string NewlineDelimiter { get; init; } = "\u00ae";

    public string MultiValueDelimiter { get; init; } = ";";

    public string NestedValueDelimiter { get; init; } = "\\";

    public string EndOfLine { get; init; } = "CRLF";

    public char GetColumnChar() => this.ColumnDelimiter[0];

    public char GetQuoteChar() => this.QuoteDelimiter.Length > 0 ? this.QuoteDelimiter[0] : '\0';

    public bool HasQuote => !string.IsNullOrEmpty(this.QuoteDelimiter);
}
