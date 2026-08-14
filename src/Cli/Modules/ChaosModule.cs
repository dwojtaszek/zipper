using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the four chaos flags: parse, validate, and build ChaosConfig.</summary>
public sealed class ChaosModule : CliModule
{
    private bool _mode;
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

    public bool TryBuild(bool loadfileOnly, LoadFileFormat currentFormat, out ChaosConfig config)
    {
        if (_mode)
        {
            if (!loadfileOnly)
            {
                Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only.");
                config = default!;
                return false;
            }

            if (currentFormat != LoadFileFormat.Dat && currentFormat != LoadFileFormat.Opt)
            {
                Console.Error.WriteLine("Error: --chaos-mode is only supported for dat and opt load file formats.");
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

            if (scenario.RequiredFormat.HasValue && scenario.RequiredFormat.Value != currentFormat)
            {
                Console.Error.WriteLine($"Error: Chaos scenario '{_scenario}' requires --loadfile-format {scenario.RequiredFormat.Value.ToString().ToLowerInvariant()} but got {currentFormat.ToString().ToLowerInvariant()}.");
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
            var validTypes = new HashSet<string>(ChaosAnomalyTypes.ForFormat(currentFormat), StringComparer.OrdinalIgnoreCase);
            foreach (var t in _types.Split(','))
            {
                if (!validTypes.Contains(t.Trim()))
                {
                    Console.Error.WriteLine($"Error: Invalid chaos type '{t.Trim()}'. Valid types for {currentFormat}: {string.Join(", ", validTypes)}");
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
