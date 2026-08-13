using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns --bates-prefix, --bates-start, --bates-digits: parse, validate, and build BatesNumberConfig.</summary>
public sealed class BatesModule : CliModule
{
    private string? _prefix;
    private long? _start;
    private int? _digits;
    private IReadOnlyList<string>? _prefixes;
    private IReadOnlyList<long>? _starts;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--bates-prefix", "--bates-start", "--bates-digits" };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--bates-prefix":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --bates-prefix requires a value.");
                    return false;
                }
                _prefix = value;
                _prefixes = value.Contains(',', StringComparison.Ordinal)
                    ? value.Split(',').Select(p => p.Trim()).ToList()
                    : new List<string> { value };
                return true;
            case "--bates-start":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --bates-start requires a value.");
                    return false;
                }
                if (value.Contains(',', StringComparison.Ordinal))
                {
                    var starts = new List<long>();
                    foreach (var part in value.Split(','))
                    {
                        if (long.TryParse(part.Trim(), CultureInfo.InvariantCulture, out var sVal))
                        {
                            starts.Add(sVal);
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --bates-start: '{value}'");
                            return false;
                        }
                    }
                    _starts = starts;
                    _start = starts[0];
                }
                else if (long.TryParse(value, CultureInfo.InvariantCulture, out var batesStart))
                {
                    _start = batesStart;
                    _starts = new List<long> { batesStart };
                }
                else
                {
                    Console.Error.WriteLine($"Error: Invalid value for --bates-start: '{value}'");
                    return false;
                }
                return true;
            case "--bates-digits":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --bates-digits requires a value.");
                    return false;
                }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var digits))
                {
                    _digits = digits;
                    return true;
                }
                Console.Error.WriteLine($"Error: Invalid value for --bates-digits: '{value}'");
                return false;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool HasBatesPrefix => !string.IsNullOrEmpty(_prefix);

    // Transitional (Phase 3): test-facing raw state so CliParserTests/RequestBuilderTests can
    // assert module ownership; ParsedArguments deletes its Bates fields and these move too.
    public string? BatesPrefix => _prefix;
    public long? BatesStart => _start;
    public int? BatesDigits => _digits;
    public IReadOnlyList<string>? BatesPrefixes => _prefixes;
    public IReadOnlyList<long>? BatesStarts => _starts;

    public bool TryBuild(ParsedArguments parsed, out BatesNumberConfig? config)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        // Transitional: rolling-count / rolling-bates-mode / count come from the still-present
        // bag (Production domain, Phase 3). Order mirrors today's pipeline: ProductionSetValidator
        // ran its bates range/overlap checks before CrossCuttingValidator.ValidateBates.
        if (parsed.ProductionSet)
        {
            if (!ValidateRollingBates(parsed))
            {
                config = null;
                return false;
            }
        }

        if (_prefix is not null || _start is not null || _digits is not null)
        {
            var built = new BatesNumberConfig
            {
                Prefix = _prefix ?? "DOC",
                Start = _start ?? 1,
                Digits = _digits ?? 8,
                Prefixes = _prefixes,
                Starts = _starts,
            };
            if (!BatesSequence.TryCreate(built, out _, out var error))
            {
                Console.Error.WriteLine($"Error: {error}");
                config = null;
                return false;
            }
            config = HasBatesPrefix ? built : null;
        }
        else
        {
            config = null;
        }

        return true;
    }

    private bool ValidateRollingBates(ParsedArguments parsed)
    {
        if (_prefixes is not null)
        {
            if (_prefixes.Count > 1 && _prefixes.Count != parsed.RollingCount)
            {
                Console.Error.WriteLine("Error: Number of bates prefixes must match rolling count.");
                return false;
            }
            if (_prefixes.Any(string.IsNullOrWhiteSpace))
            {
                Console.Error.WriteLine("Error: Bates prefix cannot be empty or whitespace.");
                return false;
            }
        }

        if (_starts is not null && _starts.Count > 1 && _starts.Count != parsed.RollingCount)
        {
            Console.Error.WriteLine("Error: Number of bates starts must match rolling count.");
            return false;
        }

        var ranges = new List<(string Prefix, long Start, long End)>();
        long currentStart = _start ?? 1;
        long fileCount = parsed.Count ?? 0;

        for (int i = 0; i < parsed.RollingCount; i++)
        {
            string prefix = _prefixes is not null && _prefixes.Count > i
                ? _prefixes[i]
                : _prefix ?? string.Empty;

            long start;
            var mode = parsed.RollingBatesMode?.ToLowerInvariant() ?? "continuous";
            if (mode == "restart")
            {
                start = _starts is not null && _starts.Count > i
                    ? _starts[i]
                    : _start ?? 1;
            }
            else // continuous
            {
                if (_starts is not null && _starts.Count > i)
                {
                    start = _starts[i];
                }
                else
                {
                    start = currentStart;
                }
                currentStart = start + fileCount;
            }

            long end = start + fileCount - 1;
            ranges.Add((prefix, start, end));
        }

        var modeStr = parsed.RollingBatesMode?.ToLowerInvariant() ?? "continuous";
        if (modeStr == "continuous")
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                for (int j = i + 1; j < ranges.Count; j++)
                {
                    if (string.Equals(ranges[i].Prefix, ranges[j].Prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        long maxStart = Math.Max(ranges[i].Start, ranges[j].Start);
                        long minEnd = Math.Min(ranges[i].End, ranges[j].End);
                        if (maxStart <= minEnd)
                        {
                            Console.Error.WriteLine(
                                $"Error: Bates ranges overlap for prefix '{ranges[i].Prefix}': " +
                                $"Set {i + 1} ({ranges[i].Start}-{ranges[i].End}) and " +
                                $"Set {j + 1} ({ranges[j].Start}-{ranges[j].End}).");
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }
}
