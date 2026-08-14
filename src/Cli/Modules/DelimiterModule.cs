using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the ten delimiter flags: parse, validate, and build DelimiterConfig.</summary>
public sealed class DelimiterModule : CliModule
{
    private string? _datDelimiters;
    private string? _delimiterColumn;
    private string? _delimiterQuote;
    private string? _delimiterNewline;
    private string? _eol;
    private string? _colDelim;
    private string? _quoteDelim;
    private string? _newlineDelim;
    private string? _multiDelim;
    private string? _nestedDelim;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--dat-delimiters", "--delimiter-column", "--delimiter-quote", "--delimiter-newline", "--eol",
        "--col-delim", "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim",
    };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--dat-delimiters": _datDelimiters = value; return true;
            case "--delimiter-column": _delimiterColumn = value; return true;
            case "--delimiter-quote": _delimiterQuote = value; return true;
            case "--delimiter-newline": _delimiterNewline = value; return true;
            case "--eol": _eol = value; return true;
            case "--col-delim": _colDelim = value; return true;
            case "--quote-delim": _quoteDelim = value; return true;
            case "--newline-delim": _newlineDelim = value; return true;
            case "--multi-delim": _multiDelim = value; return true;
            case "--nested-delim": _nestedDelim = value; return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(bool loadfileOnly, bool productionSet, out DelimiterConfig config)
    {
        // productionSet was a bag field pre-Phase-3; ProductionModule now owns it, so it is passed in.
        // Cross-domain (moves to CrossCuttingRules in Phase 4): --eol only with loadfile-only or production-set.
        if (!string.IsNullOrEmpty(_eol) && !loadfileOnly && !productionSet)
        {
            Console.Error.WriteLine("Error: --eol requires --loadfile-only or --production-set.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_eol))
        {
            var isValid = _eol!.ToUpperInvariant() switch
            {
                "CRLF" or "LF" or "CR" => true,
                _ => false,
            };
            if (!isValid)
            {
                Console.Error.WriteLine("Error: --eol must be CRLF, LF, or CR.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_datDelimiters))
        {
            var delim = _datDelimiters.ToLowerInvariant();
            if (delim != "standard" && delim != "csv")
            {
                Console.Error.WriteLine("Error: DAT delimiters must be 'standard' or 'csv'.");
                config = default!;
                return false;
            }
        }

        var sArgs = new[] { _colDelim, _newlineDelim, _multiDelim, _nestedDelim };
        var sNames = new[] { "--col-delim", "--newline-delim", "--multi-delim", "--nested-delim" };
        for (int idx = 0; idx < sArgs.Length; idx++)
        {
            if (!string.IsNullOrEmpty(sArgs[idx]) && !IsValidStrictDelimiter(sArgs[idx]!))
            {
                Console.Error.WriteLine($"Error: {sNames[idx]} must use 'ascii:<N>' or 'char:<c>' prefix.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_quoteDelim) && !_quoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) && !IsValidStrictDelimiter(_quoteDelim))
        {
            Console.Error.WriteLine("Error: --quote-delim must use 'ascii:<N>', 'char:<c>', or 'none'.");
            config = default!;
            return false;
        }

        try
        {
            if (!string.IsNullOrEmpty(_delimiterColumn)) ParseDelimiterArgument(_delimiterColumn!);
            if (!string.IsNullOrEmpty(_delimiterQuote)) ParseDelimiterArgument(_delimiterQuote!);
            if (!string.IsNullOrEmpty(_delimiterNewline)) ParseDelimiterArgument(_delimiterNewline!);
            if (!string.IsNullOrEmpty(_colDelim)) ParseStrictDelimiter(_colDelim!);
            if (!string.IsNullOrEmpty(_quoteDelim)) { if (!_quoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase)) ParseStrictDelimiter(_quoteDelim); }
            if (!string.IsNullOrEmpty(_newlineDelim)) ParseStrictDelimiter(_newlineDelim!);
            if (!string.IsNullOrEmpty(_multiDelim)) ParseStrictDelimiter(_multiDelim!);
            if (!string.IsNullOrEmpty(_nestedDelim)) ParseStrictDelimiter(_nestedDelim!);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            config = default!;
            return false;
        }

        string columnDelim = "\u0014";
        string quoteDelim = "\u00fe";
        string newlineDelim = "\u00ae";

        if (!string.IsNullOrEmpty(_datDelimiters) && _datDelimiters.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            columnDelim = ",";
            quoteDelim = "\"";
            newlineDelim = " ";
        }

        if (!string.IsNullOrEmpty(_delimiterColumn)) columnDelim = ParseDelimiterArgument(_delimiterColumn!);
        if (!string.IsNullOrEmpty(_delimiterQuote)) quoteDelim = ParseDelimiterArgument(_delimiterQuote!);
        if (!string.IsNullOrEmpty(_delimiterNewline)) newlineDelim = ParseDelimiterArgument(_delimiterNewline!);
        if (!string.IsNullOrEmpty(_colDelim)) columnDelim = ParseStrictDelimiter(_colDelim!);
        if (!string.IsNullOrEmpty(_quoteDelim)) quoteDelim = _quoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : ParseStrictDelimiter(_quoteDelim);
        if (!string.IsNullOrEmpty(_newlineDelim)) newlineDelim = ParseStrictDelimiter(_newlineDelim!);

        string multiDelim = ";";
        if (!string.IsNullOrEmpty(_multiDelim)) multiDelim = ParseStrictDelimiter(_multiDelim!);

        string nestedDelim = "\\";
        if (!string.IsNullOrEmpty(_nestedDelim)) nestedDelim = ParseStrictDelimiter(_nestedDelim!);

        config = new DelimiterConfig
        {
            ColumnDelimiter = columnDelim,
            QuoteDelimiter = quoteDelim,
            NewlineDelimiter = newlineDelim,
            MultiValueDelimiter = multiDelim,
            NestedValueDelimiter = nestedDelim,
            EndOfLine = _eol ?? "CRLF",
        };
        return true;
    }

    internal static bool IsValidStrictDelimiter(string value) => value switch
    {
        _ when value.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase) =>
            int.TryParse(value.Substring(6), CultureInfo.InvariantCulture, out var code) && code is >= 0 and <= 255,
        _ when value.StartsWith("char:", StringComparison.OrdinalIgnoreCase) => value.Length >= 6,
        _ => false,
    };

    internal static string ParseDelimiterArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg)) throw new ArgumentException("Delimiter argument cannot be empty.");
        if (string.Equals(arg, "\\t", StringComparison.Ordinal)) return "\t";
        if (string.Equals(arg, "\\n", StringComparison.Ordinal)) return "\n";
        if (string.Equals(arg, "\\r", StringComparison.Ordinal)) return "\r";
        if (string.Equals(arg, "\\r\\n", StringComparison.Ordinal)) return "\r\n";
        if (int.TryParse(arg, CultureInfo.InvariantCulture, out var asciiCode) && asciiCode >= 0 && asciiCode <= 255) return ((char)asciiCode).ToString();
        if (arg.Length > 1) Console.Error.WriteLine($"Warning: Delimiter argument '{arg}' is longer than 1 character. Using first character: '{arg[0]}'");
        return arg[0].ToString();
    }

    internal static string ParseStrictDelimiter(string arg)
    {
        if (arg.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = arg.Substring(6);
            if (int.TryParse(numPart, CultureInfo.InvariantCulture, out var code) && code >= 0 && code <= 255) return ((char)code).ToString();
            throw new ArgumentException($"Invalid ASCII code in delimiter: '{arg}'");
        }
        if (arg.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
        {
            var charPart = arg.Substring(5);
            if (charPart.Length >= 1) return charPart[0].ToString();
            throw new ArgumentException($"Missing character in delimiter: '{arg}'");
        }
        throw new ArgumentException($"Delimiter must use 'ascii:<N>' or 'char:<c>' prefix: '{arg}'");
    }
}
