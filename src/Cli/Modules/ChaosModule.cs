using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the four chaos flags: parse, validate, and build ChaosConfig.</summary>
public sealed class ChaosModule : CliModule
{
    private bool _mode;
    /// <summary>
    /// Gets whether --chaos-mode was specified.
    /// </summary>
    public bool ChaosMode => _mode;

    /// <summary>
    /// Gets the comma-separated chaos types specified via --chaos-types, or null if not specified.
    /// </summary>
    public string? ChaosTypes => _types;

    private string? _amount;
    private string? _types;
    private string? _scenario;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--chaos-mode", "--chaos-amount", "--chaos-types", "--chaos-scenario" };

    public override bool TakesValue(string flag) => flag != "--chaos-mode";

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--chaos-mode": _mode = true; return true;
            case "--chaos-amount": _amount = value; return true;
            case "--chaos-types": _types = value; return true;
            case "--chaos-scenario": _scenario = value; return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool TryBuild(bool loadfileOnly, IReadOnlyList<LoadFileFormat> formats, out ChaosConfig config)
    {
        if (_mode)
        {
            if (!loadfileOnly)
            {
                Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only.");
                config = default!;
                return false;
            }

            if (formats is { Count: > 0 })
            {
                foreach (var format in formats)
                {
                    if (format != LoadFileFormat.Dat && format != LoadFileFormat.Opt)
                    {
                        Console.Error.WriteLine("Error: --chaos-mode is only supported for dat and opt load file formats.");
                        config = default!;
                        return false;
                    }
                }
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(_amount))
            {
                Console.Error.WriteLine("Error: --chaos-amount requires --chaos-mode.");
                config = default!;
                return false;
            }
            if (!string.IsNullOrEmpty(_types))
            {
                Console.Error.WriteLine("Error: --chaos-types requires --chaos-mode.");
                config = default!;
                return false;
            }
            if (!string.IsNullOrEmpty(_scenario))
            {
                Console.Error.WriteLine("Error: --chaos-scenario requires --chaos-mode.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_scenario) && !string.IsNullOrEmpty(_types))
        {
            Console.Error.WriteLine("Error: --chaos-scenario conflicts with --chaos-types. Use one or the other.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_scenario))
        {
            var scenario = ChaosScenarios.GetByName(_scenario);
            if (scenario == null)
            {
                Console.Error.WriteLine($"Error: Unknown chaos scenario '{_scenario}'.\n       Available scenarios: {string.Join(", ", ChaosScenarios.ScenarioNames)}");
                config = default!;
                return false;
            }

            // For scenarios, validate against the first format (or all formats if needed)
            var primaryFormat = formats?.FirstOrDefault() ?? LoadFileFormat.Dat;
            if (scenario.RequiredFormat.HasValue && scenario.RequiredFormat.Value != primaryFormat)
            {
                Console.Error.WriteLine($"Error: Chaos scenario '{_scenario}' requires --loadfile-format {scenario.RequiredFormat.Value.ToString().ToLowerInvariant()} but got {primaryFormat.ToString().ToLowerInvariant()}.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_amount) && !IsValidChaosAmount(_amount))
        {
            Console.Error.WriteLine("Error: --chaos-amount must be a percentage (e.g., '1%') or an exact count (e.g., '500').");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_types))
        {
            // Build union of valid types for all formats
            var validTypesUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (formats is { Count: > 0 })
            {
                foreach (var format in formats)
                {
                    foreach (var t in ChaosAnomalyTypes.ForFormat(format))
                    {
                        validTypesUnion.Add(t);
                    }
                }
            }
            else
            {
                // Fallback to dat format if no formats specified
                foreach (var t in ChaosAnomalyTypes.ForFormat(LoadFileFormat.Dat))
                {
                    validTypesUnion.Add(t);
                }
            }

            foreach (var t in _types.Split(','))
            {
                var trimmed = t.Trim();
                if (!validTypesUnion.Contains(trimmed))
                {
                    Console.Error.WriteLine($"Error: Invalid chaos type '{trimmed}'. Valid types for selected formats: {string.Join(", ", validTypesUnion.OrderBy(x => x))}");
                    config = default!;
                    return false;
                }
            }
        }

        config = new ChaosConfig
        {
            ChaosMode = _mode,
            ChaosAmount = _amount,
            ChaosTypes = _types,
            ChaosScenario = _scenario,
        };
        return true;
    }

    internal static bool IsValidChaosAmount(string value) => value switch
    {
        _ when value.EndsWith("%", StringComparison.Ordinal) => double.TryParse(value.TrimEnd('%'), CultureInfo.InvariantCulture, out var pct) && pct > 0,
        _ => int.TryParse(value, CultureInfo.InvariantCulture, out var count) && count > 0,
    };
}
