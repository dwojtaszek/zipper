using System.Text;
using Zipper.Profiles;

namespace Zipper.Cli;

public static class RequestBuilder
{
    private static readonly Dictionary<string, long> SizeMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KB"] = 1024,
        ["MB"] = 1024 * 1024,
        ["GB"] = 1024 * 1024 * 1024,
    };

    public static FileGenerationRequest Build(ParsedArguments parsed)
    {
        if (parsed == null)
        {
            throw new ArgumentNullException(nameof(parsed));
        }

        var encoding = GetEncodingFromName(parsed.Encoding ?? "UTF-8");

        ColumnProfile? profile = null;
        if (!string.IsNullOrEmpty(parsed.ColumnProfile))
        {
            profile = ColumnProfileLoader.Load(parsed.ColumnProfile);
            if (profile == null)
            {
                Console.Error.WriteLine($"Warning: Failed to load column profile '{parsed.ColumnProfile}'.");
            }
        }

        List<LoadFileFormat>? multiFormats = null;
        if (!string.IsNullOrEmpty(parsed.LoadFileFormats))
        {
            multiFormats = parsed.LoadFileFormats
                .Split(',')
                .Select(f => GetLoadFileFormat(f.Trim()))
                .Where(f => f.HasValue)
                .Select(f => f!.Value)
                .ToList();
        }

        string columnDelim = "\u0014";
        string quoteDelim = "\u00fe";
        string newlineDelim = "\u00ae";

        if (!string.IsNullOrEmpty(parsed.DatDelimiters))
        {
            if (parsed.DatDelimiters.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                columnDelim = ",";
                quoteDelim = "\"";
                newlineDelim = " ";
            }
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterColumn))
        {
            columnDelim = ParseDelimiterArgument(parsed.DelimiterColumn);
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterQuote))
        {
            quoteDelim = ParseDelimiterArgument(parsed.DelimiterQuote);
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterNewline))
        {
            newlineDelim = ParseDelimiterArgument(parsed.DelimiterNewline);
        }

        if (!string.IsNullOrEmpty(parsed.ColDelim))
        {
            columnDelim = ParseStrictDelimiter(parsed.ColDelim);
        }

        if (!string.IsNullOrEmpty(parsed.QuoteDelim))
        {
            quoteDelim = parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : ParseStrictDelimiter(parsed.QuoteDelim);
        }

        if (!string.IsNullOrEmpty(parsed.NewlineDelim))
        {
            newlineDelim = ParseStrictDelimiter(parsed.NewlineDelim);
        }

        string multiDelim = ";";
        if (!string.IsNullOrEmpty(parsed.MultiDelim))
        {
            multiDelim = ParseStrictDelimiter(parsed.MultiDelim);
        }

        string nestedDelim = "\\";
        if (!string.IsNullOrEmpty(parsed.NestedDelim))
        {
            nestedDelim = ParseStrictDelimiter(parsed.NestedDelim);
        }

        var encodingName = encoding?.EncodingName ?? "UTF-8";
        if (parsed.LoadfileOnly && !string.IsNullOrEmpty(parsed.Encoding))
        {
            var lfEncoding = EncodingHelper.GetEncoding(parsed.Encoding);
            if (lfEncoding != null)
            {
                encodingName = parsed.Encoding.ToUpperInvariant();
            }
        }

        return new FileGenerationRequest
        {
            OutputPath = parsed.OutputDirectory!.FullName,
            FileCount = parsed.Count!.Value,
            FileType = (parsed.FileType ?? "pdf").ToLower(),
            Folders = parsed.Folders,
            Concurrency = PerformanceConstants.DefaultConcurrency,
            WithMetadata = parsed.WithMetadata,
            WithText = parsed.WithText,
            TargetZipSize = !string.IsNullOrEmpty(parsed.TargetZipSize) ? ParseSize(parsed.TargetZipSize!) : null,
            IncludeLoadFile = parsed.IncludeLoadFile,
            Distribution = GetDistributionFromName(parsed.Distribution ?? "proportional") ?? DistributionType.Proportional,
            Encoding = encodingName,
            AttachmentRate = parsed.AttachmentRate,
            LoadFileFormat = GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat,
            LoadFileFormats = multiFormats,
            ColumnDelimiter = columnDelim,
            QuoteDelimiter = quoteDelim,
            NewlineDelimiter = newlineDelim,
            MultiValueDelimiter = multiDelim,
            NestedValueDelimiter = nestedDelim,
            BatesConfig = !string.IsNullOrEmpty(parsed.BatesPrefix) ? new BatesNumberConfig
            {
                Prefix = parsed.BatesPrefix,
                Start = parsed.BatesStart ?? 1,
                Digits = parsed.BatesDigits ?? 8,
            }
            : null,
            TiffPageRange = !string.IsNullOrEmpty(parsed.TiffPagesRange) ? TiffMultiPageGenerator.ParsePageRange(parsed.TiffPagesRange!) : null,
            ColumnProfile = profile,
            Seed = parsed.Seed,
            DateFormatOverride = parsed.DateFormat,
            EmptyPercentageOverride = parsed.EmptyPercentage,
            CustodianCountOverride = parsed.CustodianCount,
            WithFamilies = parsed.WithFamilies,
            LoadfileOnly = parsed.LoadfileOnly,
            EndOfLine = parsed.Eol ?? "CRLF",
            ChaosMode = parsed.ChaosMode,
            ChaosAmount = parsed.ChaosAmount,
            ChaosTypes = parsed.ChaosTypes,
            ChaosScenario = parsed.ChaosScenario,
            ProductionSet = parsed.ProductionSet,
            ProductionZip = parsed.ProductionZip,
            VolumeSize = parsed.VolumeSize ?? 5000,
        };
    }

    internal static long? ParseSize(string size)
    {
        size = size.Trim();

        foreach (var (suffix, multiplier) in SizeMultipliers)
        {
            if (size.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var numberPart = size.Substring(0, size.Length - suffix.Length);
                return long.TryParse(numberPart, out var value) ? value * multiplier : null;
            }
        }

        return null;
    }

    internal static DistributionType? GetDistributionFromName(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "PROPORTIONAL" => DistributionType.Proportional,
            "GAUSSIAN" => DistributionType.Gaussian,
            "EXPONENTIAL" => DistributionType.Exponential,
            _ => null,
        };
    }

    internal static Encoding? GetEncodingFromName(string name) => EncodingHelper.GetEncoding(name);

    internal static LoadFileFormat? GetLoadFileFormat(string name)
    {
        return name.ToUpperInvariant().Replace("-", string.Empty) switch
        {
            "DAT" => LoadFileFormat.Dat,
            "OPT" => LoadFileFormat.Opt,
            "CSV" => LoadFileFormat.Csv,
            "XML" => LoadFileFormat.Xml,
            "EDRMXML" => LoadFileFormat.EdrmXml,
            "CONCORDANCE" => LoadFileFormat.Concordance,
            _ => null,
        };
    }

    internal static string ParseDelimiterArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            throw new ArgumentException("Delimiter argument cannot be empty.");
        }

        if (arg == "\\t")
        {
            return "\t";
        }

        if (arg == "\\n")
        {
            return "\n";
        }

        if (arg == "\\r")
        {
            return "\r";
        }

        if (arg == "\\r\\n")
        {
            return "\r\n";
        }

        if (int.TryParse(arg, out var asciiCode) && asciiCode >= 0 && asciiCode <= 255)
        {
            return ((char)asciiCode).ToString();
        }

        if (arg.Length > 1)
        {
            Console.Error.WriteLine($"Warning: Delimiter argument '{arg}' is longer than 1 character. Using first character: '{arg[0]}'");
        }

        return arg[0].ToString();
    }

    internal static string ParseStrictDelimiter(string arg)
    {
        if (arg.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = arg.Substring(6);
            if (int.TryParse(numPart, out var code) && code >= 0 && code <= 255)
            {
                return ((char)code).ToString();
            }

            throw new ArgumentException($"Invalid ASCII code in delimiter: '{arg}'");
        }

        if (arg.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
        {
            var charPart = arg.Substring(5);
            if (charPart.Length >= 1)
            {
                return charPart[0].ToString();
            }

            throw new ArgumentException($"Missing character in delimiter: '{arg}'");
        }

        throw new ArgumentException($"Delimiter must use 'ascii:<N>' or 'char:<c>' prefix: '{arg}'");
    }
}
