using System.Text;
using Zipper.Config;
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
        ArgumentNullException.ThrowIfNull(parsed);

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
        else if (!parsed.IsLoadFileFormatExplicit)
        {
            var fileType = (parsed.FileType ?? "pdf").ToLowerInvariant();
            if (string.Equals(fileType, "tiff", StringComparison.Ordinal) || string.Equals(fileType, "jpg", StringComparison.Ordinal))
            {
                multiFormats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt };
            }
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

        if (!string.IsNullOrEmpty(parsed.DelimiterColumn) && !string.IsNullOrEmpty(parsed.ParsedDelimiterColumn))
        {
            columnDelim = parsed.ParsedDelimiterColumn;
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterQuote) && !string.IsNullOrEmpty(parsed.ParsedDelimiterQuote))
        {
            quoteDelim = parsed.ParsedDelimiterQuote;
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterNewline) && !string.IsNullOrEmpty(parsed.ParsedDelimiterNewline))
        {
            newlineDelim = parsed.ParsedDelimiterNewline;
        }

        if (!string.IsNullOrEmpty(parsed.ColDelim) && !string.IsNullOrEmpty(parsed.ParsedColDelim))
        {
            columnDelim = parsed.ParsedColDelim;
        }

        if (!string.IsNullOrEmpty(parsed.QuoteDelim) && parsed.ParsedQuoteDelim != null)
        {
            quoteDelim = parsed.ParsedQuoteDelim;
        }

        if (!string.IsNullOrEmpty(parsed.NewlineDelim) && !string.IsNullOrEmpty(parsed.ParsedNewlineDelim))
        {
            newlineDelim = parsed.ParsedNewlineDelim;
        }

        string multiDelim = ";";
        if (!string.IsNullOrEmpty(parsed.MultiDelim) && !string.IsNullOrEmpty(parsed.ParsedMultiDelim))
        {
            multiDelim = parsed.ParsedMultiDelim;
        }

        string nestedDelim = "\\";
        if (!string.IsNullOrEmpty(parsed.NestedDelim) && !string.IsNullOrEmpty(parsed.ParsedNestedDelim))
        {
            nestedDelim = parsed.ParsedNestedDelim;
        }

        var encodingName = (encoding != null && !string.IsNullOrEmpty(parsed.Encoding))
            ? parsed.Encoding.ToUpperInvariant()
            : "UTF-8";

        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = parsed.OutputDirectory!.FullName,
                FileCount = parsed.Count!.Value,
                FileType = (parsed.FileType ?? "pdf").ToLowerInvariant(),
                Folders = parsed.Folders,
                Concurrency = PerformanceConstants.DefaultConcurrency,
                WithText = parsed.WithText,
                TargetZipSize = !string.IsNullOrEmpty(parsed.TargetZipSize) ? ParseSize(parsed.TargetZipSize!) : null,
                IncludeLoadFile = parsed.IncludeLoadFile,
            },
            Metadata = new MetadataConfig
            {
                WithMetadata = parsed.WithMetadata,
                ColumnProfile = profile,
                Seed = parsed.Seed,
                DateFormatOverride = parsed.DateFormat,
                EmptyPercentageOverride = parsed.EmptyPercentage,
                CustodianCountOverride = parsed.CustodianCount,
                WithFamilies = parsed.WithFamilies,
            },
            LoadFile = new LoadFileConfig
            {
                LoadFileFormat = GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat,
                LoadFileFormats = multiFormats,
                Encoding = encodingName,
                IsEncodingExplicit = parsed.IsEncodingExplicit,
                Distribution = GetDistributionFromName(parsed.Distribution ?? "proportional") ?? DistributionType.Proportional,
                AttachmentRate = parsed.AttachmentRate,
            },
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = columnDelim,
                QuoteDelimiter = quoteDelim,
                NewlineDelimiter = newlineDelim,
                MultiValueDelimiter = multiDelim,
                NestedValueDelimiter = nestedDelim,
                EndOfLine = parsed.Eol ?? "CRLF",
            },
            Bates = !string.IsNullOrEmpty(parsed.BatesPrefix) ? new BatesNumberConfig
            {
                Prefix = parsed.BatesPrefix,
                Start = parsed.BatesStart ?? 1,
                Digits = parsed.BatesDigits ?? 8,
            }
            : null,
            Tiff = new TiffConfig
            {
                PageRange = !string.IsNullOrEmpty(parsed.TiffPagesRange) ? TiffMultiPageGenerator.ParsePageRange(parsed.TiffPagesRange!) : null,
            },
            Chaos = new ChaosConfig
            {
                ChaosMode = parsed.ChaosMode,
                ChaosAmount = parsed.ChaosAmount,
                ChaosTypes = parsed.ChaosTypes,
                ChaosScenario = parsed.ChaosScenario,
            },
            Production = new ProductionConfig
            {
                ProductionSet = parsed.ProductionSet,
                ProductionZip = parsed.ProductionZip,
                VolumeSize = parsed.VolumeSize ?? 5000,
            },
            LoadfileOnly = parsed.LoadfileOnly,
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
                return long.TryParse(numberPart, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value * multiplier : null;
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
        return name.ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal) switch
        {
            "DAT" => LoadFileFormat.Dat,
            "OPT" => LoadFileFormat.Opt,
            "CSV" => LoadFileFormat.Csv,
            "XML" => LoadFileFormat.EdrmXml,
            "EDRMXML" => LoadFileFormat.EdrmXml,
            "CONCORDANCE" => LoadFileFormat.Concordance,
            _ => null,
        };
    }

    internal static string ParseDelimiterArgument(string arg) => Validation.CrossCuttingValidator.ParseDelimiterArgument(arg);

    internal static string ParseStrictDelimiter(string arg) => Validation.CrossCuttingValidator.ParseStrictDelimiter(arg);
}
