namespace Zipper;

/// <summary>
/// Loadfile-Only generation mode — emits a load file plus its properties sidecar without producing native files.
/// </summary>
internal class LoadFileOnlyMode : IGenerationMode
{
    private readonly Func<FileGenerationRequest, CancellationToken, Task<LoadFileOnlyResult>> _generate;

    public LoadFileOnlyMode(Func<FileGenerationRequest, CancellationToken, Task<LoadFileOnlyResult>> generate)
    {
        ArgumentNullException.ThrowIfNull(generate);
        _generate = generate;
    }

    public async Task RunAsync(FileGenerationRequest request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Starting loadfile-only generation...");
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Format: {0}", string.Join(", ", request.LoadFile.Formats)));
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Count: {0:N0}", request.Output.FileCount));
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Output Path: {0}", request.Output.OutputPath));
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Encoding: {0}", request.LoadFile.Encoding));
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  EOL: {0}", request.Delimiters.EndOfLine));

        if (request.Chaos.ChaosMode)
        {
            if (!string.IsNullOrEmpty(request.Chaos.ChaosScenario))
            {
                var scenario = ChaosScenarios.GetByName(request.Chaos.ChaosScenario);
                if (scenario != null)
                {
                    if (string.IsNullOrEmpty(request.Chaos.ChaosAmount))
                    {
                        request.Chaos = request.Chaos with { ChaosAmount = scenario.DefaultAmount };
                    }

                    request.Chaos = request.Chaos with { ChaosTypes = string.IsNullOrEmpty(scenario.ChaosTypes) ? null : scenario.ChaosTypes };

                    Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Chaos Scenario: {0} ({1})", scenario.Name, scenario.Description));
                }
                else
                {
                    Console.Error.WriteLine($"  Warning: Chaos scenario '{request.Chaos.ChaosScenario}' not found; falling back to the supplied --chaos-types/--chaos-amount (if any).");
                }
            }

            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Chaos Mode: Enabled (amount: {0})", request.Chaos.ChaosAmount ?? "1%"));
            if (!string.IsNullOrEmpty(request.Chaos.ChaosTypes))
            {
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Chaos Types: {0}", request.Chaos.ChaosTypes));
            }
        }

        var result = await _generate(request, cancellationToken).ConfigureAwait(false);

        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "\n\nGeneration complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Load file: {0}", result.LoadFilePath));
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Properties: {0}", result.PropertiesFilePath));
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Records: {0:N0}", result.TotalRecords));
    }
}
