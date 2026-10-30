using System.Globalization;
using Zipper.Config;
using Zipper.Profiles.Generation;

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

    internal bool IsAdversarialValuesScenario => ChaosScenarios.GeneratesAdversarialValues(_scenario);

    internal bool RequiresSourcePaths(IReadOnlyList<LoadFileFormat>? formats)
    {
        if (!this._mode || formats is { Count: > 0 } && !formats.Contains(LoadFileFormat.Dat))
        {
            return false;
        }

        string? chaosTypes = this._types;
        if (string.IsNullOrEmpty(chaosTypes) && !string.IsNullOrEmpty(this._scenario))
        {
            var scenario = ChaosScenarios.GetByName(this._scenario);
            if (scenario is null)
            {
                return false;
            }

            chaosTypes = string.Join(",", ChaosScenarios.ResolveTypes(scenario, LoadFileFormat.Dat));
        }
        else if (chaosTypes is null)
        {
            return ChaosAnomalyTypes.DatDefault.Contains("path-pointer", StringComparer.OrdinalIgnoreCase);
        }

        return chaosTypes is not null
            && (chaosTypes.Length == 0
                || chaosTypes.Split(',').Any(type => string.Equals(type.Trim(), "path-pointer", StringComparison.OrdinalIgnoreCase)));
    }

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

    public bool TryBuild(
        bool loadfileOnly,
        IReadOnlyList<LoadFileFormat> formats,
        out ChaosConfig config,
        DelimiterConfig? delimiters = null)
    {
        if (_mode)
        {
            if (!loadfileOnly)
            {
                Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only.");
                config = default!;
                return false;
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

            // A scenario with a required format must match every selected format (Dat when none given).
            var effectiveFormats = formats is { Count: > 0 } ? formats : new[] { LoadFileFormat.Dat };
            if (scenario.RequiredFormat.HasValue)
            {
                var mismatched = effectiveFormats.FirstOrDefault(f => f != scenario.RequiredFormat.Value, scenario.RequiredFormat.Value);
                if (mismatched != scenario.RequiredFormat.Value)
                {
                    Console.Error.WriteLine($"Error: Chaos scenario '{_scenario}' requires --loadfile-format {scenario.RequiredFormat.Value.ToString().ToLowerInvariant()} but got {mismatched.ToString().ToLowerInvariant()}.");
                    config = default!;
                    return false;
                }
            }

            if (scenario.GenerateAdversarialValues
                && !AdversarialValueTailPlan.HasValidDelimiters(delimiters ?? new DelimiterConfig()))
            {
                Console.Error.WriteLine("Error: Chaos scenario 'adversarial-values' requires distinct single-character column and quote delimiters.");
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

        // Defaults to the raw value so an unset/unparseable input is never silently widened.
        string? normalizedChaosTypes = _types;
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

            var normalized = new List<string>();
            foreach (var t in _types.Split(','))
            {
                var trimmed = t.Trim();
                if (!validTypesUnion.Contains(trimmed))
                {
                    Console.Error.WriteLine($"Error: Invalid chaos type '{trimmed}'. Valid types for selected formats: {string.Join(", ", validTypesUnion.OrderBy(x => x))}");
                    config = default!;
                    return false;
                }

                normalized.Add(trimmed);
            }

            // Store the trimmed names that were actually validated so downstream consumers
            // see exactly the set that passed validation.
            normalizedChaosTypes = string.Join(",", normalized);
        }

        config = new ChaosConfig
        {
            ChaosMode = _mode,
            ChaosAmount = _amount,
            ChaosTypes = normalizedChaosTypes,
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
