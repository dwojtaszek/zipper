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

    /// <summary>
    /// Gets the chaos scenario name specified via --chaos-scenario, or null if not specified.
    /// </summary>
    public string? ChaosScenario => _scenario;

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
        DelimiterConfig? delimiters = null,
        string? encoding = null,
        bool productionSet = false,
        bool hasEml = false)
    {
        // An email run generates .eml work items in Standard or Production Set mode. Its chaos types
        // apply to those work items, not to load file lines.
        bool emailRun = hasEml && !loadfileOnly;
        if (!this.TryValidateMode(loadfileOnly, productionSet, hasEml)
            || !this.TryValidateEmailScenario(emailRun, productionSet)
            || !this.TryValidateScenario(formats, delimiters, encoding)
            || !this.TryValidateAmount()
            || !this.TryNormalizeChaosTypes(formats, emailRun, out var normalizedChaosTypes))
        {
            config = default!;
            return false;
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

    private bool TryValidateMode(bool loadfileOnly, bool productionSet, bool hasEml)
    {
        if (this._mode)
        {
            if (!loadfileOnly && !productionSet && !hasEml)
            {
                Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only, --production-set, or email (.eml) generation.");
                return false;
            }

            if (!loadfileOnly && !hasEml && !ChaosScenarios.IsCrossFormat(this._scenario))
            {
                Console.Error.WriteLine("Error: --chaos-mode with --production-set requires a cross-format chaos scenario ('cross-format-desync' or 'broken-families') or email (.eml) generation.");
                return false;
            }

            return true;
        }

        if (!string.IsNullOrEmpty(this._amount))
        {
            Console.Error.WriteLine("Error: --chaos-amount requires --chaos-mode.");
            return false;
        }

        if (!string.IsNullOrEmpty(this._types))
        {
            Console.Error.WriteLine("Error: --chaos-types requires --chaos-mode.");
            return false;
        }

        if (!string.IsNullOrEmpty(this._scenario))
        {
            Console.Error.WriteLine("Error: --chaos-scenario requires --chaos-mode.");
            return false;
        }

        return true;
    }

    private bool TryValidateEmailScenario(bool emailRun, bool productionSet)
    {
        // Production Set cross-format scenarios still drive load file chaos next to the .eml files.
        bool loadFileScenario = productionSet && ChaosScenarios.IsCrossFormat(this._scenario);
        if (emailRun && !string.IsNullOrEmpty(this._scenario) && !loadFileScenario)
        {
            Console.Error.WriteLine("Error: --chaos-scenario does not apply to email (.eml) chaos. Use --chaos-types with email types.");
            return false;
        }

        return true;
    }

    private bool TryValidateScenario(
        IReadOnlyList<LoadFileFormat> formats,
        DelimiterConfig? delimiters,
        string? encoding)
    {
        if (string.IsNullOrEmpty(this._scenario))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(this._types))
        {
            Console.Error.WriteLine("Error: --chaos-scenario conflicts with --chaos-types. Use one or the other.");
            return false;
        }

        var scenario = ChaosScenarios.GetByName(this._scenario);
        if (scenario is null)
        {
            Console.Error.WriteLine($"Error: Unknown chaos scenario '{this._scenario}'.\n       Available scenarios: {string.Join(", ", ChaosScenarios.ScenarioNames)}");
            return false;
        }

        if (scenario.IsCrossFormat)
        {
            var effectiveFormats = formats is { Count: > 0 } ? formats : new[] { LoadFileFormat.Dat, LoadFileFormat.Opt };
            if (!effectiveFormats.Contains(LoadFileFormat.Dat) || !effectiveFormats.Contains(LoadFileFormat.Opt))
            {
                Console.Error.WriteLine($"Error: Chaos scenario '{this._scenario}' requires both DAT and OPT load file formats.");
                return false;
            }
        }
        else if (scenario.RequiredFormat.HasValue)
        {
            var effectiveFormats = formats is { Count: > 0 } ? formats : new[] { LoadFileFormat.Dat };
            var mismatched = effectiveFormats.FirstOrDefault(f => f != scenario.RequiredFormat.Value, scenario.RequiredFormat.Value);
            if (mismatched != scenario.RequiredFormat.Value)
            {
                Console.Error.WriteLine($"Error: Chaos scenario '{this._scenario}' requires --loadfile-format {scenario.RequiredFormat.Value.ToString().ToLowerInvariant()} but got {mismatched.ToString().ToLowerInvariant()}.");
                return false;
            }
        }

        if (scenario.GenerateAdversarialValues
            && !AdversarialValueTailPlan.HasValidDatConfiguration(delimiters ?? new DelimiterConfig(), encoding ?? "UTF-8"))
        {
            Console.Error.WriteLine("Error: Chaos scenario 'adversarial-values' requires DAT-safe delimiters and an encoding that preserves its values.");
            return false;
        }

        return true;
    }

    private bool TryValidateAmount()
    {
        if (!string.IsNullOrEmpty(this._amount) && !IsValidChaosAmount(this._amount))
        {
            Console.Error.WriteLine("Error: --chaos-amount must be a percentage (e.g., '1%') or an exact count (e.g., '500').");
            return false;
        }

        return true;
    }

    private bool TryNormalizeChaosTypes(
        IReadOnlyList<LoadFileFormat> formats,
        bool emailRun,
        out string? normalizedChaosTypes)
    {
        // Defaults to the raw value so an unset/unparseable input is never silently widened.
        normalizedChaosTypes = this._types;
        if (string.IsNullOrEmpty(this._types))
        {
            return true;
        }

        // Build union of valid types for all formats, or only the email types for an email run.
        var validTypesUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (emailRun)
        {
            validTypesUnion.UnionWith(ChaosAnomalyTypes.Email);
        }
        else if (formats is { Count: > 0 })
        {
            foreach (var format in formats)
            {
                validTypesUnion.UnionWith(ChaosAnomalyTypes.ForFormat(format));
            }
        }
        else
        {
            validTypesUnion.UnionWith(ChaosAnomalyTypes.ForFormat(LoadFileFormat.Dat));
        }

        var normalized = new List<string>();
        foreach (var type in this._types.Split(','))
        {
            var trimmed = type.Trim();
            var effectiveType = ChaosAnomalyTypes.CanonicalEmailType(trimmed);

            if (!emailRun && ChaosAnomalyTypes.IsEmail(effectiveType))
            {
                Console.Error.WriteLine($"Error: Chaos type '{trimmed}' applies only to email (.eml) generation, not to load file output.");
                return false;
            }

            if (!validTypesUnion.Contains(effectiveType))
            {
                string scope = emailRun ? "email (.eml) generation" : "selected formats";
                Console.Error.WriteLine($"Error: Invalid chaos type '{trimmed}'. Valid types for {scope}: {string.Join(", ", validTypesUnion.OrderBy(value => value))}");
                return false;
            }

            normalized.Add(effectiveType);
        }

        // Store the trimmed names that were actually validated so downstream consumers
        // see exactly the set that passed validation.
        normalizedChaosTypes = string.Join(",", normalized);
        return true;
    }

    internal static bool IsValidChaosAmount(string value) => value switch
    {
        _ when value.EndsWith("%", StringComparison.Ordinal) => double.TryParse(value.TrimEnd('%'), CultureInfo.InvariantCulture, out var pct) && pct > 0,
        _ => int.TryParse(value, CultureInfo.InvariantCulture, out var count) && count > 0,
    };
}
