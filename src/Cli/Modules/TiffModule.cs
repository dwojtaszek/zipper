using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns --tiff-pages: parse, validate, and build TiffConfig.</summary>
public sealed class TiffModule : CliModule
{
    private string? _pageRange;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--tiff-pages" };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--tiff-pages": _pageRange = value; return true;
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

        config = new TiffConfig
        {
            PageRange = !string.IsNullOrEmpty(_pageRange) ? TiffMultiPageGenerator.ParsePageRange(_pageRange!) : null,
        };
        return true;
    }
}
