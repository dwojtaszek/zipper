using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns --hash-mode and --hash-algorithms: parse, validate, and build HashConfig.</summary>
public sealed class HashModule : CliModule
{
    private string? _mode;
    private string? _algorithms;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--hash-mode", "--hash-algorithms" };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--hash-mode": _mode = value; return true;
            case "--hash-algorithms": _algorithms = value; return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(ParsedArguments parsed, out HashConfig config)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (_mode is not null)
        {
            var mode = _mode.ToLowerInvariant();
            if (mode != "actual" && mode != "simulated" && mode != "none")
            {
                Console.Error.WriteLine($"Error: Invalid --hash-mode '{_mode}'. Supported values: actual, simulated, none.");
                config = default!;
                return false;
            }
        }

        if (_algorithms is not null)
        {
            bool isHashEnabled = _mode is not null &&
                (string.Equals(_mode, "actual", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(_mode, "simulated", StringComparison.OrdinalIgnoreCase));

            if (!isHashEnabled)
            {
                Console.Error.WriteLine("Error: --hash-algorithms requires --hash-mode to be 'actual' or 'simulated'.");
                config = default!;
                return false;
            }

            var algs = _algorithms.Split(',', StringSplitOptions.TrimEntries);
            if (algs.Length == 0 || algs.Any(string.IsNullOrEmpty))
            {
                Console.Error.WriteLine("Error: --hash-algorithms requires at least one valid algorithm (md5, sha1, sha256).");
                config = default!;
                return false;
            }

            foreach (var alg in algs)
            {
                var lowerAlg = alg.ToLowerInvariant();
                if (lowerAlg != "md5" && lowerAlg != "sha1" && lowerAlg != "sha256")
                {
                    Console.Error.WriteLine($"Error: Invalid hash algorithm '{alg}'. Supported values: md5, sha1, sha256.");
                    config = default!;
                    return false;
                }
            }
        }

        // Cross-domain (moves to CrossCuttingRules in Phase 4): reads LoadfileOnly from the
        // still-present bag because LoadFileModule (Phase 2) owns that flag.
        // Keep the LoadfileOnlyValidator bytes (capital E + period). Do not "fix" to the
        // RequestBuilder variant ("error: ... hash)" — no E2E asserts that string).
        if (parsed.LoadfileOnly && string.Equals(_mode, "actual", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --hash-mode actual is not supported with --loadfile-only (no file bytes to hash).");
            config = default!;
            return false;
        }

        config = Parse(_mode, _algorithms);
        return true;
    }

    public static HashConfig Parse(string? mode, string? algorithms)
    {
        var parsedMode = HashMode.None;
        if (!string.IsNullOrEmpty(mode))
        {
            parsedMode = mode.ToLowerInvariant() switch
            {
                "actual" => HashMode.Actual,
                "simulated" => HashMode.Simulated,
                "none" => HashMode.None,
                _ => HashMode.None,
            };
        }

        var parsedAlgorithms = new HashSet<HashAlgorithm>();
        if (!string.IsNullOrEmpty(algorithms))
        {
            foreach (var alg in algorithms.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
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
                    parsedAlgorithms.Add(parsedAlg.Value);
                }
            }
        }

        if (parsedMode != HashMode.None && parsedAlgorithms.Count == 0)
        {
            parsedAlgorithms.Add(HashAlgorithm.MD5);
        }

        return new HashConfig { Mode = parsedMode, Algorithms = parsedAlgorithms };
    }
}
