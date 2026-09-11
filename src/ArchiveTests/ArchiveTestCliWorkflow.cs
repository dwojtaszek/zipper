using Zipper.Cli.Modules;

namespace Zipper.ArchiveTests;

/// <summary>
/// The Archive Test CLI workflow (REQ-208/215/216, ADR-0008): a Program short-circuit,
/// not a generation mode. Builds the typed <see cref="ArchiveTestRequest"/> from raw
/// module values — never OutputModule.TryBuild or MetadataModule.TryBuild, which
/// impose Native File requirements — enforces the closed flag set, and publishes the
/// suite folder. Exit codes: 0 published, 1 validation or generation failure (no
/// partial publication), 130 cancellation.
/// </summary>
internal static class ArchiveTestCliWorkflow
{
    /// <summary>The closed flag set of this workflow (ADR-0008): nothing else may be
    /// combined with --archive-test-suite, even a flag explicitly set to its default.</summary>
    private static readonly string[] AllowedFlags =
    [
        "--archive-test-suite", "--archive-test-cases", "--seed", "--output-path",
    ];

    public static async Task<int> RunAsync(CliModuleSet modules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var disallowed = modules.ConsumedFlags
            .Where(flag => !AllowedFlags.Contains(flag, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (disallowed.Count > 0)
        {
            Console.Error.WriteLine(
                $"Error: --archive-test-suite accepts only --archive-test-suite, --archive-test-cases, --seed, and --output-path. Disallowed flag{(disallowed.Count == 1 ? string.Empty : "s")}: {string.Join(", ", disallowed)}.");
            return 1;
        }

        if (modules.ArchiveTest.RawSuite is null)
        {
            Console.Error.WriteLine("Error: --archive-test-cases requires --archive-test-suite.");
            return 1;
        }

        var suite = ResolveSuite(modules.ArchiveTest.RawSuite);
        if (suite is null)
        {
            Console.Error.WriteLine(
                $"Error: Invalid --archive-test-suite '{modules.ArchiveTest.RawSuite}'. Supported suites: smoke, compatibility, malformed, security, all.");
            return 1;
        }

        var suiteKeys = ArchiveTestCatalog.ListSuite(suite).Select(definition => definition.CaseKey).ToList();
        List<string> caseKeys;
        if (modules.ArchiveTest.RawCases is null)
        {
            // No selection: every member of the suite, in ordinal Case Key order.
            caseKeys = suiteKeys;
        }
        else
        {
            caseKeys = [];
            foreach (var token in modules.ArchiveTest.RawCases.Split(','))
            {
                var key = token.Trim();
                if (key.Length == 0)
                {
                    Console.Error.WriteLine("Error: --archive-test-cases must be a comma-separated list of non-empty Case Keys.");
                    return 1;
                }

                if (caseKeys.Contains(key, StringComparer.Ordinal))
                {
                    Console.Error.WriteLine($"Error: Duplicate Case Key '{key}' in --archive-test-cases.");
                    return 1;
                }

                if (!suiteKeys.Contains(key, StringComparer.Ordinal))
                {
                    Console.Error.WriteLine(
                        $"Error: Case Key '{key}' is not a member of suite '{suite}'. Members: {string.Join(", ", suiteKeys)}.");
                    return 1;
                }

                caseKeys.Add(key);
            }

            // Generation order is the ordinal Case Key order, never the typed order and
            // never random sampling.
            caseKeys.Sort(StringComparer.Ordinal);
        }

        var outputPath = modules.Output.RawOutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Error: --output-path is required for --archive-test-suite.");
            return 1;
        }

        var seed = modules.Metadata.Seed ?? ArchiveTestRequest.DefaultSeed;

        ArchiveTestRequest request;
        try
        {
            // Create re-validates the Case Keys and the output target: a new directory
            // inside the working directory, exactly like the OutputModule path rules.
            request = ArchiveTestRequest.Create(caseKeys, seed, outputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ArchiveTestSuiteGenerator.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(
                $"Published {result.FixtureIds.Count} Archive Test fixture pair{(result.FixtureIds.Count == 1 ? string.Empty : "s")} to {result.PublishedDirectory}.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync("\nOperation cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"\nAn error occurred: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>Suite names are case-insensitive; the canonical name is returned.</summary>
    private static string? ResolveSuite(string raw) => raw.ToLowerInvariant() switch
    {
        "smoke" => ArchiveTestCatalog.SmokeSuite,
        "compatibility" => ArchiveTestCatalog.CompatibilitySuite,
        "malformed" => ArchiveTestCatalog.MalformedSuite,
        "security" => ArchiveTestCatalog.SecuritySuite,
        "all" => ArchiveTestCatalog.AllSuites,
        _ => null,
    };
}
