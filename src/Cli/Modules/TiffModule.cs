using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns --tiff-pages, --tiff-realism, --with-tiff-realism, --realistic-tiff, --tiff-compression: parse, validate, and build TiffConfig.</summary>
public sealed class TiffModule : CliModule
{
    private string? _pageRange;
    private string? _realismMode;
    private string? _compression;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--tiff-pages",
        "--tiff-realism",
        "--with-tiff-realism",
        "--realistic-tiff",
        "--tiff-compression",
    };

    public override bool TakesValue(string flag) => flag switch
    {
        "--with-tiff-realism" or "--realistic-tiff" => false,
        _ => true,
    };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--tiff-pages":
                _pageRange = value;
                return true;

            case "--tiff-realism":
                if (string.IsNullOrWhiteSpace(value))
                {
                    Console.Error.WriteLine("Error: --tiff-realism requires a value.");
                    return false;
                }
                var normalized = value.ToLowerInvariant();
                if (normalized is not ("none" or "standard" or "group4" or "uncompressed"))
                {
                    Console.Error.WriteLine($"Error: Invalid TIFF realism mode '{value}'. Allowed: none, standard, group4, uncompressed.");
                    return false;
                }
                _realismMode = normalized;
                return true;

            case "--with-tiff-realism":
            case "--realistic-tiff":
                // An explicit --tiff-realism wins regardless of flag order.
                _realismMode ??= "standard";
                return true;

            case "--tiff-compression":
                if (string.IsNullOrWhiteSpace(value))
                {
                    Console.Error.WriteLine("Error: --tiff-compression requires a value.");
                    return false;
                }
                var comp = value.ToLowerInvariant();
                if (comp is not ("group4" or "uncompressed"))
                {
                    Console.Error.WriteLine($"Error: Invalid TIFF compression '{value}'. Allowed: group4, uncompressed.");
                    return false;
                }
                _compression = comp;
                return true;

            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(out TiffConfig config)
    {
        if (!string.IsNullOrEmpty(_pageRange) && TiffMultiPageGenerator.ParsePageRange(_pageRange!) is null)
        {
            Console.Error.WriteLine("Error: Invalid TIFF pages range. Use format: <min>-<max> (e.g., 1-20).");
            config = default!;
            return false;
        }

        if (_compression is not null && _realismMode is (null or "none"))
        {
            Console.Error.WriteLine("Error: --tiff-compression requires --tiff-realism, --with-tiff-realism, or --realistic-tiff.");
            config = default!;
            return false;
        }

        // A group4 or uncompressed realism mode names its encoding, so a different --tiff-compression is a conflict.
        // standard names no encoding and accepts either compression.
        if (_compression is not null && _realismMode is ("group4" or "uncompressed") && _compression != _realismMode)
        {
            Console.Error.WriteLine($"Error: --tiff-realism {_realismMode} conflicts with --tiff-compression {_compression}.");
            config = default!;
            return false;
        }

        var compression = _compression ?? (_realismMode switch
        {
            "uncompressed" => "uncompressed",
            _ => "group4",
        });

        config = new TiffConfig
        {
            PageRange = !string.IsNullOrEmpty(_pageRange) ? TiffMultiPageGenerator.ParsePageRange(_pageRange!) : null,
            RealismMode = _realismMode,
            Compression = compression,
        };
        return true;
    }
}
