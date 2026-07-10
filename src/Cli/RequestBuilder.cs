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

    public static FileGenerationRequest? Build(ParsedArguments parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var resolved = PathValidator.ResolveSecurePath(
            parsed.OutputPathStr,
            Directory.GetCurrentDirectory());
        if (resolved is null)
            return null;

        var encoding = GetEncodingFromName(parsed.Encoding ?? "UTF-8");

        ColumnProfile? profile = null;
        if (!string.IsNullOrEmpty(parsed.ColumnProfile))
        {
            profile = ColumnProfileLoader.Load(parsed.ColumnProfile);
            if (profile is null)
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
            quoteDelim = parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : ParseStrictDelimiter(parsed.QuoteDelim);
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

        var encodingName = (encoding is not null && !string.IsNullOrEmpty(parsed.Encoding))
            ? parsed.Encoding.ToUpperInvariant()
            : "UTF-8";

        var formats = (multiFormats is not null && multiFormats.Count > 0)
            ? multiFormats
            : new List<LoadFileFormat> { GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat };

        var hashConfig = ParseHashConfig(parsed);
        if (parsed.LoadfileOnly && hashConfig.Mode == HashMode.Actual)
        {
            Console.Error.WriteLine("error: --hash-mode actual is not supported with --loadfile-only (no file bytes to hash)");
            return null;
        }

        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = resolved.FullName,
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
                Formats = formats,
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
                Prefixes = parsed.BatesPrefixes,
                Starts = parsed.BatesStarts,
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
                ProductionId = parsed.ProductionId,
                RollingCount = parsed.RollingCount,
                RollingBatesMode = (parsed.RollingBatesMode?.ToLowerInvariant()) switch
                {
                    "restart" => RollingBatesMode.Restart,
                    _ => RollingBatesMode.Continuous,
                },
            },
            LoadfileOnly = parsed.LoadfileOnly,
            Hash = hashConfig,
        };
    }

    internal static HashConfig ParseHashConfig(ParsedArguments parsed)
    {
        var mode = HashMode.None;
        if (!string.IsNullOrEmpty(parsed.HashMode))
        {
            mode = parsed.HashMode.ToLowerInvariant() switch
            {
                "actual" => HashMode.Actual,
                "simulated" => HashMode.Simulated,
                "none" => HashMode.None,
                _ => HashMode.None,
            };
        }

        var algorithms = new HashSet<HashAlgorithm>();
        if (!string.IsNullOrEmpty(parsed.HashAlgorithms))
        {
            foreach (var alg in parsed.HashAlgorithms.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var parsedAlg = alg.ToLowerInvariant() switch
                {
                    "md5" => HashAlgorithm.MD5,
                    "sha1" => HashAlgorithm.SHA1,
                    "sha256" => HashAlgorithm.SHA256,
                    _ => (HashAlgorithm?)null,
                };

                if (parsedAlg.HasValue)
                {
                    algorithms.Add(parsedAlg.Value);
                }
            }
        }

        return new HashConfig
        {
            Mode = mode,
            Algorithms = algorithms,
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
