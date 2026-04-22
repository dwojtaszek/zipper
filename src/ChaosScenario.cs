namespace Zipper;

/// <summary>
/// Defines a named chaos scenario with predefined anomaly types and amounts.
/// </summary>
internal record ChaosScenarioDefinition(
    string Name,
    string Description,
    string ChaosTypes,
    string DefaultAmount,
    LoadFileFormat? RequiredFormat);

/// <summary>
/// Registry of built-in chaos scenarios that bundle realistic combinations
/// of anomaly types modeled after real-world platform ingestion failures.
/// </summary>
internal static class ChaosScenarios
{
    private static readonly ChaosScenarioDefinition[] ScenariosArray =
    {
        new(
            Name: "structured-import-failures",
            Description: "Common platform ingestion failures: delimiter mismatches, unclosed quotes, column count errors",
            ChaosTypes: "mixed-delimiters,quotes,columns",
            DefaultAmount: "3%",
            RequiredFormat: LoadFileFormat.Dat),
        new(
            Name: "encoding-nightmare",
            Description: "Multi-encoding source data: invalid byte sequences and mixed delimiters",
            ChaosTypes: "encoding,mixed-delimiters",
            DefaultAmount: "5%",
            RequiredFormat: LoadFileFormat.Dat),
        new(
            Name: "broken-boundaries",
            Description: "OPT document boundary corruption: flipped break flags, invalid page counts, and invalid paths",
            ChaosTypes: "opt-boundary,opt-pagecount,opt-path",
            DefaultAmount: "8%",
            RequiredFormat: LoadFileFormat.Opt),
        new(
            Name: "field-overflow",
            Description: "Unescaped newlines and extra columns that break row parsing",
            ChaosTypes: "eol,columns",
            DefaultAmount: "2%",
            RequiredFormat: LoadFileFormat.Dat),
        new(
            Name: "full-chaos",
            Description: "All anomaly types enabled at high density for stress testing",
            ChaosTypes: string.Empty,
            DefaultAmount: "10%",
            RequiredFormat: null),
        new(
            Name: "transfer-encoding-failures",
            Description: "Cross-platform transfer errors: mixed delimiters, encoding issues, unclosed quotes",
            ChaosTypes: "mixed-delimiters,encoding,quotes",
            DefaultAmount: "4%",
            RequiredFormat: LoadFileFormat.Dat),
    };

    private static readonly IReadOnlyList<string> CachedScenarioNames =
        Array.AsReadOnly(ScenariosArray.Select(s => s.Name).ToArray());

    /// <summary>
    /// All available built-in chaos scenarios (read-only).
    /// </summary>
    public static IReadOnlyList<ChaosScenarioDefinition> All { get; } = Array.AsReadOnly(ScenariosArray);

    /// <summary>
    /// Gets all available scenario names (read-only).
    /// </summary>
    public static IReadOnlyList<string> ScenarioNames => CachedScenarioNames;

    /// <summary>
    /// Looks up a scenario by name (case-insensitive).
    /// </summary>
    /// <param name="name">Scenario name to look up.</param>
    /// <returns>Matching scenario definition, or null if not found.</returns>
    public static ChaosScenarioDefinition? GetByName(string name)
    {
        return ScenariosArray.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Prints the list of available scenarios to stdout.
    /// </summary>
    public static void PrintScenarioList()
    {
        Console.WriteLine("Available Chaos Scenarios:");
        Console.WriteLine();
        Console.WriteLine($"  {"Name",-25} {"Format",-8} {"Default",-10} Description");
        Console.WriteLine($"  {new string('-', 25)} {new string('-', 8)} {new string('-', 10)} {new string('-', 50)}");

        foreach (var scenario in ScenariosArray)
        {
            var format = scenario.RequiredFormat?.ToString() ?? "Any";
            Console.WriteLine($"  {scenario.Name,-25} {format,-8} {scenario.DefaultAmount,-10} {scenario.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("Usage: zipper --loadfile-only --count <N> --output-path <path> --chaos-mode --chaos-scenario <name>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - --chaos-scenario requires --chaos-mode and --loadfile-only");
        Console.WriteLine("  - --chaos-scenario conflicts with --chaos-types (use one or the other)");
        Console.WriteLine("  - --chaos-amount overrides the scenario's default amount");
        Console.WriteLine("  - 'full-chaos' enables all types for the selected format");
    }
}
