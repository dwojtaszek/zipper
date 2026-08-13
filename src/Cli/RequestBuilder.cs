using System.Globalization;
using System.Text;
using Zipper.Config;

namespace Zipper.Cli;

public static class RequestBuilder
{
    private static readonly Dictionary<string, long> SizeMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KB"] = 1024,
        ["MB"] = 1024 * 1024,
        ["GB"] = 1024 * 1024 * 1024,
    };

    public static FileGenerationRequest? Build(
        ParsedArguments parsed,
        DelimiterConfig delimiters,
        TiffConfig tiff,
        ChaosConfig chaos,
        HashConfig hash,
        BatesNumberConfig? bates,
        MetadataConfig metadata,
        LoadFileConfig loadFile,
        bool loadfileOnly,
        bool isLoadFileFormatExplicit)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(delimiters);
        ArgumentNullException.ThrowIfNull(tiff);
        ArgumentNullException.ThrowIfNull(chaos);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(loadFile);

        var resolved = PathValidator.ResolveSecurePath(
            parsed.OutputPathStr,
            Directory.GetCurrentDirectory());
        if (resolved is null)
            return null;

        var fileType = (parsed.FileType ?? "pdf").ToLowerInvariant();
        IReadOnlyList<FileTypeRatio>? fileTypeRatios = null;
        FileTypePlan? fileTypePlan = null;
        if (parsed.FileTypes is not null)
        {
            if (!FileTypeRatioParser.TryParse(parsed.FileTypes, out var parsedRatios, out var ratioError))
            {
                Console.Error.WriteLine($"Error: {ratioError}");
                return null;
            }

            if (parsedRatios.Count == 1)
            {
                fileType = parsedRatios[0].Type;
            }
            else
            {
                fileTypeRatios = parsedRatios;
                fileTypePlan = new FileTypePlan(parsedRatios, parsed.Count!.Value);
                fileType = parsedRatios[0].Type;
            }
        }

        // Source-Driven Generation: read rows now so bad input fails before any generation.
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords = null;
        if (!string.IsNullOrEmpty(parsed.InputCsv) || !string.IsNullOrEmpty(parsed.DirectoryTemplate))
        {
            var readOk = !string.IsNullOrEmpty(parsed.InputCsv)
                ? SourceInput.SourceCsvReader.TryRead(parsed.InputCsv!, out var rows, out var readError)
                : SourceInput.DirectoryTemplateReader.TryRead(parsed.DirectoryTemplate!, out rows, out readError);
            if (!readOk)
            {
                Console.Error.WriteLine($"Error: {readError}");
                return null;
            }

            if (parsed.Count.HasValue && parsed.Count.Value != rows.Count)
            {
                Console.Error.WriteLine($"Error: --count ({parsed.Count.Value}) does not match the Source Record count ({rows.Count}). Align --count with the source input or omit it.");
                return null;
            }

            if (rows.Any(r => r.BatesNumber is not null) && bates is null)
            {
                Console.Error.WriteLine("Error: the source 'BatesNumber' column requires --bates-prefix so the Bates column is emitted.");
                return null;
            }

            if (parsed.ProductionSet && rows.Any(r => r.BatesNumber is not null))
            {
                Console.Error.WriteLine("Error: the source 'BatesNumber' column cannot be used with --production-set. Production Set Bates Numbers come from the configured Bates sequence so Volume ranges in the Production Manifest stay exact.");
                return null;
            }

            // Explicit identity overrides must stay clear of sequence-generated values: an
            // override equal to a generated fallback produces duplicate Load File identities
            // and OPT image paths.
            var identityCollision = FindGeneratedIdentityCollision(rows, bates);
            if (identityCollision is not null)
            {
                Console.Error.WriteLine($"Error: {identityCollision}");
                return null;
            }

            sourceRecords = rows;
        }

        // The image-type override (image-only runs get both DAT and OPT load files) keys off
        // whether the user explicitly chose formats. hasImageType reads fileType / fileTypeRatios
        // / sourceRecords computed above — it cannot move to LoadFileModule.
        if (!isLoadFileFormatExplicit)
        {
            var hasImageType = fileType is "tiff" or "jpg"
                || (fileTypeRatios?.Any(r => r.Type is "tiff" or "jpg") ?? false)
                || (sourceRecords?.Any(r => r.FileType is "tiff" or "jpg") ?? false);
            if (hasImageType)
            {
                loadFile = loadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };
            }
        }

        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = resolved.FullName,
                FileCount = sourceRecords is not null ? sourceRecords.Count : parsed.Count!.Value,
                FileType = sourceRecords is not null ? sourceRecords[0].FileType : fileType,
                FileTypeRatios = fileTypeRatios,
                FileTypePlan = fileTypePlan,
                SourceFileTypes = sourceRecords is not null
                    ? sourceRecords.Select(r => r.FileType).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList()
                    : null,
                Folders = parsed.Folders,
                Concurrency = PerformanceConstants.DefaultConcurrency,
                WithText = parsed.WithText,
                TargetZipSize = !string.IsNullOrEmpty(parsed.TargetZipSize) ? ParseSize(parsed.TargetZipSize!) : null,
                IncludeLoadFile = parsed.IncludeLoadFile,
            },
            Metadata = metadata,
            LoadFile = loadFile,
            Delimiters = delimiters,
            Bates = bates,
            Tiff = tiff,
            Chaos = chaos,
            Production = new ProductionConfig
            {
                ProductionSet = parsed.ProductionSet,
                ProductionZip = parsed.ProductionZip,
                VolumeSize = parsed.VolumeSize ?? 5000,
                SupplementalProduction = parsed.SupplementalProduction,
                PriorManifests = !string.IsNullOrEmpty(parsed.PriorManifests)
                    ? parsed.PriorManifests.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>(),
                SupplementalGapPolicy = parsed.SupplementalGapPolicy ?? "reject",
                ProductionId = parsed.ProductionId,
                RollingCount = parsed.RollingCount,
                RollingBatesMode = (parsed.RollingBatesMode?.ToLowerInvariant()) switch
                {
                    "restart" => RollingBatesMode.Restart,
                    _ => RollingBatesMode.Continuous,
                },
                RedactedProduction = parsed.RedactedProduction,
                WithheldNativePolicy = parsed.WithheldNativePolicy?.ToLowerInvariant() ?? "keep-native",
                SourcePathMode = (parsed.SourcePathMode?.ToLowerInvariant()) switch
                {
                    "preserve" => SourcePathMode.PreserveSubdirs,
                    "originals" => SourcePathMode.Originals,
                    _ => SourcePathMode.Bates,
                },
            },
            LoadfileOnly = loadfileOnly,
            Hash = hash,
            SourceRecords = sourceRecords,
        };
    }

    // An explicit override collides with a generated identity only when it equals a value the
    // run would actually generate, compared case-insensitively (consistent with duplicate
    // detection and Windows path semantics): DOC{index:D8} for Control Numbers and the
    // configured Bates sequence values for Bates Numbers.
    private static string? FindGeneratedIdentityCollision(IReadOnlyList<SourceInput.SourceRecord> rows, BatesNumberConfig? bates)
    {
        foreach (var row in rows)
        {
            var control = row.ControlNumber;
            if (control is not null
                && control.Length == 11
                && control.StartsWith("DOC", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(control.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var controlIndex)
                && controlIndex >= 1
                && controlIndex <= rows.Count
                && string.Equals(control, "DOC" + controlIndex.ToString("D8", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                return $"source ControlNumber '{control}' collides with the generated Control Number for row {controlIndex}. Choose an override outside the generated identity space.";
            }
        }

        if (bates is not null)
        {
            var prefix = bates.Prefix;
            var start = bates.Start;
            var digits = bates.Digits;
            foreach (var row in rows)
            {
                var batesValue = row.BatesNumber;
                if (batesValue is null || !batesValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var numericPart = batesValue.AsSpan(prefix.Length);
                if (!long.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    continue;
                }

                var sequenceIndex = number - start;
                if (sequenceIndex < 0 || sequenceIndex >= rows.Count)
                {
                    continue;
                }

                var generated = prefix + number.ToString($"D{digits}", CultureInfo.InvariantCulture);
                if (string.Equals(batesValue, generated, StringComparison.OrdinalIgnoreCase))
                {
                    return $"source BatesNumber '{batesValue}' collides with the generated Bates sequence value for row {sequenceIndex + 1}. Choose an override outside the generated identity space.";
                }
            }
        }

        return null;
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
}
