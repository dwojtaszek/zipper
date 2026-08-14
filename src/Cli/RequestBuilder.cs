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

    internal static FileGenerationRequest? Build(
        OutputConfig output,
        MetadataConfig metadata,
        LoadFileConfig loadFile,
        DelimiterConfig delimiters,
        BatesNumberConfig? bates,
        TiffConfig tiff,
        ChaosConfig chaos,
        HashConfig hash,
        ProductionConfig production,
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords,
        bool loadfileOnly,
        bool isLoadFileFormatExplicit)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(loadFile);
        ArgumentNullException.ThrowIfNull(delimiters);
        ArgumentNullException.ThrowIfNull(tiff);
        ArgumentNullException.ThrowIfNull(chaos);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(production);

        // The image-type override (image-only runs get both DAT and OPT load files) keys off
        // whether the user explicitly chose formats. hasImageType reads output.FileType /
        // output.FileTypeRatios / sourceRecords — it cannot move to LoadFileModule.
        if (!isLoadFileFormatExplicit)
        {
            var hasImageType = output.FileType is "tiff" or "jpg"
                || (output.FileTypeRatios?.Any(r => r.Type is "tiff" or "jpg") ?? false)
                || (sourceRecords?.Any(r => r.FileType is "tiff" or "jpg") ?? false);
            if (hasImageType)
            {
                loadFile = loadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };
            }
        }

        return new FileGenerationRequest
        {
            Output = output,
            Metadata = metadata,
            LoadFile = loadFile,
            Delimiters = delimiters,
            Bates = bates,
            Tiff = tiff,
            Chaos = chaos,
            Production = production,
            LoadfileOnly = loadfileOnly,
            Hash = hash,
            SourceRecords = sourceRecords,
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
}
