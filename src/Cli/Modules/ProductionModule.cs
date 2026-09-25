using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the production flags: parse, validate, and build ProductionConfig.</summary>
public sealed class ProductionModule : CliModule
{
    private const int MaxProductionIdBytes = 250;

    private bool _productionSet;
    private bool _productionZip;
    private int? _volumeSize;
    private bool _supplementalProduction;
    private string? _priorManifests;
    private string? _supplementalGapPolicy;
    private string? _productionId;
    private int _rollingCount = 1;
    private string _rollingBatesMode = "continuous";
    private bool _redactedProduction;
    private string? _withheldNativePolicy;
    private string? _sourcePathMode;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--production-set", "--production-zip", "--volume-size", "--supplemental-production",
        "--prior-manifest", "--supplemental-gap-policy", "--production-id", "--rolling-count",
        "--rolling-bates-mode", "--redacted-production", "--withheld-native-policy", "--source-path-mode",
    };

    public override bool TakesValue(string flag) => flag is not "--production-set" and not "--production-zip" and not "--supplemental-production" and not "--redacted-production";

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--production-set": _productionSet = true; return true;
            case "--production-zip": _productionZip = true; return true;
            case "--supplemental-production": _supplementalProduction = true; return true;
            case "--redacted-production": _redactedProduction = true; return true;
            case "--volume-size":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --volume-size requires a value.");
                    return false;
                }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var volumeSize))
                {
                    _volumeSize = volumeSize;
                    return true;
                }
                Console.Error.WriteLine($"Error: Invalid value for --volume-size: '{value}'");
                return false;
            case "--rolling-count":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --rolling-count requires a value.");
                    return false;
                }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var rollingCount))
                {
                    _rollingCount = rollingCount;
                    return true;
                }
                Console.Error.WriteLine($"Error: Invalid value for --rolling-count: '{value}'");
                return false;
            case "--prior-manifest":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --prior-manifest requires a value.");
                    return false;
                }
                _priorManifests = value;
                return true;
            case "--supplemental-gap-policy":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --supplemental-gap-policy requires a value.");
                    return false;
                }
                _supplementalGapPolicy = value;
                return true;
            case "--production-id":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --production-id requires a value.");
                    return false;
                }
                _productionId = value;
                return true;
            case "--rolling-bates-mode":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --rolling-bates-mode requires a value.");
                    return false;
                }
                _rollingBatesMode = value;
                return true;
            case "--withheld-native-policy":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --withheld-native-policy requires a value.");
                    return false;
                }
                _withheldNativePolicy = value;
                return true;
            case "--source-path-mode":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --source-path-mode requires a value.");
                    return false;
                }
                _sourcePathMode = value;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    // Sibling-channel + test-facing raw state. CrossCuttingRules and other modules read these getters.
    public bool ProductionSet => _productionSet;
    public bool ProductionZip => _productionZip;
    public int? VolumeSize => _volumeSize;
    public bool SupplementalProduction => _supplementalProduction;
    public string? PriorManifests => _priorManifests;
    public string? SupplementalGapPolicy => _supplementalGapPolicy;
    public string? ProductionId => _productionId;
    public int RollingCount => _rollingCount;
    public string RollingBatesMode => _rollingBatesMode;
    public bool RedactedProduction => _redactedProduction;
    public string? WithheldNativePolicy => _withheldNativePolicy;
    public string? SourcePathMode => _sourcePathMode;

    public bool TryBuild(out ProductionConfig config)
    {
        if (_productionZip && !_productionSet)
        {
            Console.Error.WriteLine("Error: --production-zip requires --production-set.");
            config = default!;
            return false;
        }

        if (_volumeSize.HasValue && !_productionSet)
        {
            Console.Error.WriteLine("Error: --volume-size requires --production-set.");
            config = default!;
            return false;
        }

        if (_productionSet && _volumeSize is < 1)
        {
            Console.Error.WriteLine("Error: --volume-size must be at least 1.");
            config = default!;
            return false;
        }

        if (_supplementalProduction && !_productionSet)
        {
            Console.Error.WriteLine("Error: --supplemental-production requires --production-set.");
            config = default!;
            return false;
        }

        if (_supplementalProduction && string.IsNullOrEmpty(_priorManifests))
        {
            Console.Error.WriteLine("Error: --supplemental-production requires --prior-manifest.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_priorManifests) && !_supplementalProduction)
        {
            Console.Error.WriteLine("Error: --prior-manifest requires --supplemental-production.");
            config = default!;
            return false;
        }

        if (_supplementalGapPolicy is not null)
        {
            if (!_supplementalProduction)
            {
                Console.Error.WriteLine("Error: --supplemental-gap-policy requires --supplemental-production.");
                config = default!;
                return false;
            }

            if (!string.Equals(_supplementalGapPolicy, "reject", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_supplementalGapPolicy, "allow", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Error: --supplemental-gap-policy must be 'reject' or 'allow'.");
                config = default!;
                return false;
            }
        }

        if (_redactedProduction && !_productionSet)
        {
            Console.Error.WriteLine("Error: --redacted-production requires --production-set.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_withheldNativePolicy))
        {
            if (!_redactedProduction)
            {
                Console.Error.WriteLine("Error: --withheld-native-policy requires --redacted-production.");
                config = default!;
                return false;
            }

            var policy = _withheldNativePolicy.ToLowerInvariant();
            if (policy != "keep-native" && policy != "omit-native-path" && policy != "replace-with-placeholder")
            {
                Console.Error.WriteLine("Error: --withheld-native-policy must be 'keep-native', 'omit-native-path', or 'replace-with-placeholder'.");
                config = default!;
                return false;
            }
        }

        List<string>? prodIds = null;
        if (_productionId is not null)
        {
            // REQ-170: an empty value is a supplied value, not an omission. The auto-generated
            // default only applies when the flag is absent, so fail before GenerateProductionIds
            // can silently take its IsNullOrEmpty auto-ID branch.
            if (_productionId.Length == 0)
            {
                Console.Error.WriteLine("Error: --production-id cannot be empty; omit the flag to auto-generate the Production ID.");
                config = default!;
                return false;
            }

            prodIds = GenerateProductionIds(_productionId, _rollingCount);

            // The byte cap is checked first and outside the Production Set branch on purpose:
            // REQ-170 rejects oversized elements in every mode in which --production-id is
            // supplied, so the size error must keep winning over the requires-production-set
            // error below.
            if (prodIds.Any(id => System.Text.Encoding.UTF8.GetByteCount(id) > MaxProductionIdBytes))
            {
                Console.Error.WriteLine($"Error: --production-id must not exceed {MaxProductionIdBytes} UTF-8 bytes.");
                config = default!;
                return false;
            }

            if (!_productionSet)
            {
                Console.Error.WriteLine("Error: --production-id requires --production-set.");
                config = default!;
                return false;
            }

            // REQ-170: every Production ID becomes one directory-name segment beneath
            // --output-path, so each element must be a Safe Path Segment. The whitespace test is
            // load-bearing, not redundant: it routes whitespace-only elements (a bare tab) to the
            // more specific "Production ID cannot be empty." error further down instead of letting
            // them fall through to this rule.
            if (prodIds.Any(id => !string.IsNullOrWhiteSpace(id) && !IsSafePathSegment(id)))
            {
                Console.Error.WriteLine("Error: --production-id must be a Safe Path Segment: no path separators, reserved characters, trailing spaces or dots, or dot segments.");
                config = default!;
                return false;
            }
        }

        if (_productionSet)
        {
            if (_rollingCount <= 0)
            {
                Console.Error.WriteLine("Error: --rolling-count must be a positive number.");
                config = default!;
                return false;
            }

            if (!string.IsNullOrEmpty(_rollingBatesMode))
            {
                var mode = _rollingBatesMode.ToLowerInvariant();
                if (mode != "continuous" && mode != "restart")
                {
                    Console.Error.WriteLine("Error: --rolling-bates-mode must be 'continuous' or 'restart'.");
                    config = default!;
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(_sourcePathMode))
            {
                var pathMode = _sourcePathMode.ToLowerInvariant();
                if (pathMode is not ("bates" or "preserve" or "originals"))
                {
                    Console.Error.WriteLine("Error: --source-path-mode must be 'bates', 'preserve', or 'originals'.");
                    config = default!;
                    return false;
                }
            }

            prodIds ??= GenerateProductionIds(_productionId, _rollingCount);
            if (prodIds.Count != _rollingCount)
            {
                Console.Error.WriteLine("Error: Number of production IDs must match rolling count.");
                config = default!;
                return false;
            }

            if (prodIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != prodIds.Count)
            {
                Console.Error.WriteLine("Error: Duplicate production IDs are not allowed.");
                config = default!;
                return false;
            }

            if (prodIds.Any(string.IsNullOrWhiteSpace))
            {
                Console.Error.WriteLine("Error: Production ID cannot be empty.");
                config = default!;
                return false;
            }
        }

        config = new ProductionConfig
        {
            ProductionSet = _productionSet,
            ProductionZip = _productionZip,
            VolumeSize = _volumeSize ?? 5000,
            SupplementalProduction = _supplementalProduction,
            PriorManifests = !string.IsNullOrEmpty(_priorManifests)
                ? _priorManifests.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>(),
            SupplementalGapPolicy = _supplementalGapPolicy ?? "reject",
            ProductionId = _productionId,
            RollingCount = _rollingCount,
            RollingBatesMode = _rollingBatesMode.ToLowerInvariant() switch
            {
                "restart" => Zipper.Config.RollingBatesMode.Restart,
                _ => Zipper.Config.RollingBatesMode.Continuous,
            },
            RedactedProduction = _redactedProduction,
            WithheldNativePolicy = _withheldNativePolicy?.ToLowerInvariant() ?? "keep-native",
            SourcePathMode = _sourcePathMode?.ToLowerInvariant() switch
            {
                "preserve" => Zipper.Config.SourcePathMode.PreserveSubdirs,
                "originals" => Zipper.Config.SourcePathMode.Originals,
                _ => Zipper.Config.SourcePathMode.Bates,
            },
        };
        return true;
    }

    internal static List<string> GenerateProductionIds(string? baseId, int count)
    {
        if (string.IsNullOrEmpty(baseId))
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            if (count == 1)
            {
                return new List<string> { $"PRODUCTION_{timestamp}" };
            }
            var list = new List<string>();
            for (int i = 1; i <= count; i++)
            {
                list.Add($"PRODUCTION_{timestamp}_{i:D3}");
            }
            return list;
        }

        if (baseId.Contains(',', StringComparison.Ordinal))
        {
            return baseId.Split(',').Select(id => id.Trim()).ToList();
        }

        if (count == 1)
        {
            return new List<string> { baseId };
        }

        var result = new List<string> { baseId };
        int digitCount = 0;
        while (digitCount < baseId.Length && char.IsDigit(baseId[baseId.Length - 1 - digitCount]))
        {
            digitCount++;
        }

        if (digitCount > 0)
        {
            var prefix = baseId[..^digitCount];
            var numberStr = baseId[^digitCount..];
            var width = numberStr.Length;
            if (long.TryParse(numberStr, System.Globalization.CultureInfo.InvariantCulture, out var startNumber))
            {
                for (int i = 1; i < count; i++)
                {
                    var nextNum = startNumber + i;
                    result.Add($"{prefix}{nextNum.ToString($"D{width}", System.Globalization.CultureInfo.InvariantCulture)}");
                }
                return result;
            }
        }

        for (int i = 2; i <= count; i++)
        {
            result.Add($"{baseId}_{i}");
        }
        return result;
    }

    /// <summary>
    /// REQ-170: a Production ID is used verbatim as one directory-name segment beneath
    /// <c>--output-path</c>, so it must be a Safe Path Segment. The rules are fixed rather than
    /// read from <see cref="Path.GetInvalidFileNameChars"/> or a platform device list, so the
    /// same Production ID is accepted or rejected identically on every platform:
    /// <list type="bullet">
    /// <item>no path separator, control character, or character Windows reserves in file names;</item>
    /// <item>no trailing space or dot — Win32 strips those from every component, so
    /// <c>".. "</c> would resolve to the parent of <c>--output-path</c>, and <c>"PROD"</c> and
    /// <c>"PROD "</c> would name the same directory where a case-insensitive duplicate check
    /// cannot see the collision;</item>
    /// <item>not a dot segment, directly or after stripping trailing spaces and dots;</item>
    /// <item>not a reserved DOS device name, which <c>CreateDirectory</c> can never create.</item>
    /// </list>
    /// </summary>
    private static bool IsSafePathSegment(string value)
    {
        if (value.Length == 0 ||
            value[^1] is ' ' or '.' ||
            value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
        {
            return false;
        }

        // Win32 trims trailing spaces and dots from a path component before resolving it, so a
        // dot segment is judged after the same trim. A value made only of dots trims to empty.
        var trimmed = value.TrimEnd(' ', '.');
        if (trimmed.Length == 0 || trimmed == "." || trimmed == "..")
        {
            return false;
        }

        var stem = trimmed;
        var dotIndex = trimmed.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex >= 0)
        {
            stem = trimmed[..dotIndex];
        }

        return !IsReservedDeviceName(stem);
    }

    /// <summary>
    /// DOS device names Windows reserves regardless of extension, matched case-insensitively.
    /// <c>CreateDirectory</c> fails on these <em>after</em> validation, which would surface as an
    /// unhandled generation error instead of a validation error. The COM/LPT suffix set includes
    /// the superscript digits U+00B9, U+00B2, and U+00B3, which Windows also reserves.
    /// </summary>
    private static bool IsReservedDeviceName(string stem) =>
        stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
        (stem.Length == 4 &&
         (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
         stem[3] is (>= '1' and <= '9') or '\u00b9' or '\u00b2' or '\u00b3');
}
