using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the load-file flags (formats + loadfile-only): parse, validate, and build LoadFileConfig.</summary>
public sealed class LoadFileModule : CliModule
{
    private bool _loadfileOnly;
    private string _loadFileFormat = "dat";
    private string? _loadFileFormats;
    private bool _isLoadFileFormatExplicit;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--loadfile-only", "--load-file-format", "--load-file-formats", "--loadfile-format",
    };

    public override bool TakesValue(string flag) => flag != "--loadfile-only";

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--loadfile-only": _loadfileOnly = true; return true;
            case "--load-file-format":
            case "--loadfile-format":
                if (value is null)
                {
                    Console.Error.WriteLine($"Error: {flag} requires a value.");
                    return false;
                }
                _loadFileFormat = value;
                _isLoadFileFormatExplicit = true;
                return true;
            case "--load-file-formats":
                if (value is null)
                {
                    Console.Error.WriteLine($"Error: {flag} requires a value.");
                    return false;
                }
                _loadFileFormats = value;
                _isLoadFileFormatExplicit = true;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool LoadfileOnly => _loadfileOnly;

    public bool IsLoadFileFormatExplicit => _isLoadFileFormatExplicit;

    public LoadFileFormat CurrentFormat => RequestBuilder.GetLoadFileFormat(_loadFileFormat) ?? LoadFileFormat.Dat;

    public bool TryBuild(ParsedArguments parsed, int attachmentRate, out LoadFileConfig config)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        // Message-order invariant: today LoadfileOnlyValidator runs first but treats unknown
        // formats as Dat (GetLoadFileFormat(x) ?? Dat), so the dat/opt restriction does not
        // fire on garbage; ValidateLoadFileFormats then prints the invalid-format line.
        if (!string.IsNullOrEmpty(_loadFileFormat) && RequestBuilder.GetLoadFileFormat(_loadFileFormat) is null)
        {
            Console.Error.WriteLine("Error: Invalid load file format. Supported values are dat, opt, csv, edrm-xml, xml, concordance.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_loadFileFormats))
        {
            foreach (var fmt in _loadFileFormats.Split(','))
            {
                if (RequestBuilder.GetLoadFileFormat(fmt.Trim()) is null)
                {
                    Console.Error.WriteLine($"Error: Invalid load file format '{fmt}'. Supported: dat, opt, csv, edrm-xml, xml, concordance.");
                    config = default!;
                    return false;
                }
            }
        }

        if (_loadfileOnly)
        {
            // Transitional: TargetZipSize/IncludeLoadFile live in OutputModule (Phase 3).
            if (!string.IsNullOrEmpty(parsed.TargetZipSize))
            {
                Console.Error.WriteLine("Error: --loadfile-only conflicts with --target-zip-size.");
                config = default!;
                return false;
            }

            if (parsed.IncludeLoadFile)
            {
                Console.Error.WriteLine("Error: --loadfile-only conflicts with --include-load-file.");
                config = default!;
                return false;
            }

            var currentFormat = RequestBuilder.GetLoadFileFormat(_loadFileFormat) ?? LoadFileFormat.Dat;
            if (currentFormat != LoadFileFormat.Dat && currentFormat != LoadFileFormat.Opt)
            {
                Console.Error.WriteLine("Error: --loadfile-only mode is only supported for 'dat' and 'opt' load file formats.");
                config = default!;
                return false;
            }

            if (!string.IsNullOrEmpty(_loadFileFormats))
            {
                foreach (var fmt in _loadFileFormats.Split(','))
                {
                    var multiFormat = RequestBuilder.GetLoadFileFormat(fmt.Trim());
                    if (multiFormat.HasValue && multiFormat.Value != LoadFileFormat.Dat && multiFormat.Value != LoadFileFormat.Opt)
                    {
                        Console.Error.WriteLine("Error: --loadfile-only mode is only supported for 'dat' and 'opt' load file formats.");
                        config = default!;
                        return false;
                    }
                }
            }
        }

        IReadOnlyList<LoadFileFormat> formats;
        if (!string.IsNullOrEmpty(_loadFileFormats))
        {
            formats = _loadFileFormats.Split(',')
                .Select(f => RequestBuilder.GetLoadFileFormat(f.Trim()))
                .Where(f => f.HasValue)
                .Select(f => f!.Value)
                .ToList();
        }
        else
        {
            formats = new List<LoadFileFormat> { RequestBuilder.GetLoadFileFormat(_loadFileFormat) ?? LoadFileFormat.Dat };
        }

        // Transitional: Encoding/IsEncodingExplicit/Distribution live in OutputModule (Phase 3).
        var encoding = RequestBuilder.GetEncodingFromName(parsed.Encoding ?? "UTF-8");
        var encodingName = (encoding is not null && !string.IsNullOrEmpty(parsed.Encoding))
            ? parsed.Encoding.ToUpperInvariant()
            : "UTF-8";

        config = new LoadFileConfig
        {
            Formats = formats,
            Encoding = encodingName,
            IsEncodingExplicit = parsed.IsEncodingExplicit,
            Distribution = RequestBuilder.GetDistributionFromName(parsed.Distribution ?? "proportional") ?? DistributionType.Proportional,
            AttachmentRate = attachmentRate,
        };
        return true;
    }
}
