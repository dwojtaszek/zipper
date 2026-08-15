namespace Zipper.Cli.Modules;

/// <summary>Owns the Production Manifest comparison flags (--compare-production-manifests / --comparison-mode / --comparison-output): parse, validate, and build ComparisonRequest (REQ-176–179).</summary>
public sealed class ComparisonModule : CliModule
{
    private string? _compareManifests;
    private string? _comparisonMode;
    private string? _comparisonOutput;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--compare-production-manifests", "--comparison-mode", "--comparison-output",
    };

    public bool HasComparisonRequest => !string.IsNullOrEmpty(_compareManifests);

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--compare-production-manifests":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --compare-production-manifests requires a value.");
                    return false;
                }
                _compareManifests = value;
                return true;
            case "--comparison-mode":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --comparison-mode requires a value.");
                    return false;
                }
                _comparisonMode = value;
                return true;
            case "--comparison-output":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --comparison-output requires a value.");
                    return false;
                }
                _comparisonOutput = value;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    /// <summary>
    /// Validates the comparison trio per REQ-176/177/178. Message order mirrors the old
    /// CliValidator comparison branch byte-for-byte. Returns true with a null request when
    /// no comparison flags were given. REQ-179: this short-circuits generation validation.
    /// </summary>
    public bool TryBuild(out ComparisonRequest? request)
    {
        if (!HasComparisonRequest)
        {
            if (!string.IsNullOrEmpty(_comparisonMode) || !string.IsNullOrEmpty(_comparisonOutput))
            {
                Console.Error.WriteLine("Error: --comparison-mode and --comparison-output require --compare-production-manifests to be specified.");
                request = null;
                return false;
            }
            request = null;
            return true;
        }

        if (string.IsNullOrEmpty(_comparisonMode))
        {
            Console.Error.WriteLine("Error: --comparison-mode is required when using --compare-production-manifests.");
            request = null;
            return false;
        }

        if (!string.Equals(_comparisonMode, "replacement", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_comparisonMode, "supplemental", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_comparisonMode, "reproduction", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --comparison-mode must be 'replacement', 'supplemental', or 'reproduction'.");
            request = null;
            return false;
        }

        if (string.IsNullOrEmpty(_comparisonOutput))
        {
            Console.Error.WriteLine("Error: --comparison-output is required when using --compare-production-manifests.");
            request = null;
            return false;
        }

        request = new ComparisonRequest(_compareManifests!, _comparisonMode, _comparisonOutput);
        return true;
    }
}

/// <summary>Validated Production Manifest comparison request (REQ-176/177/178).</summary>
public sealed record ComparisonRequest(string ManifestPaths, string Mode, string OutputPath);
