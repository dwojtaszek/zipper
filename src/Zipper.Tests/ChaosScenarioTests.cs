using System.Text.Json;
using Xunit;

namespace Zipper;

[Collection("ConsoleTests")]
public class ChaosScenarioTests
{
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunWithCapture(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            int exitCode = await Program.Main(args);
            return (exitCode, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    // --- Scenario registry tests ---
    [Fact]
    public void GetByName_ValidName_ReturnsScenario()
    {
        var scenario = ChaosScenarios.GetByName("relativity-import");
        Assert.NotNull(scenario);
        Assert.Equal("relativity-import", scenario.Name);
        Assert.Contains("mixed-delimiters", scenario.ChaosTypes);
    }

    [Fact]
    public void GetByName_CaseInsensitive_ReturnsScenario()
    {
        var scenario = ChaosScenarios.GetByName("RELATIVITY-IMPORT");
        Assert.NotNull(scenario);
        Assert.Equal("relativity-import", scenario.Name);
    }

    [Fact]
    public void GetByName_InvalidName_ReturnsNull()
    {
        var scenario = ChaosScenarios.GetByName("nonexistent-scenario");
        Assert.Null(scenario);
    }

    [Fact]
    public void AllScenarios_HaveRequiredFields()
    {
        foreach (var scenario in ChaosScenarios.All)
        {
            Assert.False(string.IsNullOrEmpty(scenario.Name), "Scenario name must not be empty");
            Assert.False(string.IsNullOrEmpty(scenario.Description), "Scenario description must not be empty");
            Assert.False(string.IsNullOrEmpty(scenario.DefaultAmount), "Scenario default amount must not be empty");
        }
    }

    [Fact]
    public void ScenarioNames_AreUnique()
    {
        var names = ChaosScenarios.All.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FullChaos_HasEmptyChaosTypes()
    {
        var scenario = ChaosScenarios.GetByName("full-chaos");
        Assert.NotNull(scenario);
        Assert.True(string.IsNullOrEmpty(scenario.ChaosTypes), "full-chaos should have empty ChaosTypes to enable all types");
        Assert.Null(scenario.RequiredFormat);
    }

    [Fact]
    public void BrokenBoundaries_RequiresOptFormat()
    {
        var scenario = ChaosScenarios.GetByName("broken-boundaries");
        Assert.NotNull(scenario);
        Assert.Equal(LoadFileFormat.Opt, scenario.RequiredFormat);
    }

    // --- CLI integration tests ---
    [Fact]
    public async Task ChaosList_PrintsScenariosAndExitsZero()
    {
        var (exitCode, stdout, _) = await RunWithCapture(new[] { "--chaos-list" });
        Assert.Equal(0, exitCode);
        Assert.Contains("Available Chaos Scenarios", stdout);
        Assert.Contains("relativity-import", stdout);
        Assert.Contains("full-chaos", stdout);
    }

    [Fact]
    public async Task ChaosScenario_WithoutChaosMode_ReturnsError()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-scenario", "relativity-import",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("--chaos-scenario requires --chaos-mode", stderr);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_WithChaosTypes_ReturnsConflictError()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "relativity-import",
                "--chaos-types", "quotes",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("conflicts with --chaos-types", stderr);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_InvalidName_ReturnsError()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "fake-scenario",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown chaos scenario 'fake-scenario'", stderr);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_FormatMismatch_ReturnsError()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "broken-boundaries",
                "--loadfile-format", "dat",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("requires --loadfile-format opt", stderr);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_ValidRun_GeneratesLoadFileWithAnomalies()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, stdout, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "1000", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "relativity-import", "--seed", "42",
            });
            Assert.Equal(0, exitCode);
            Assert.Contains("Chaos Scenario: relativity-import", stdout);

            // Verify load file was created
            var datFiles = Directory.GetFiles(tempPath, "*.dat");
            Assert.Single(datFiles);

            // Verify properties JSON was created with anomalies
            var jsonFiles = Directory.GetFiles(tempPath, "*_properties.json");
            Assert.Single(jsonFiles);

            var json = await File.ReadAllTextAsync(jsonFiles[0]);
            using var doc = JsonDocument.Parse(json);
            var chaosMode = doc.RootElement.GetProperty("chaosMode");
            Assert.True(chaosMode.GetProperty("enabled").GetBoolean());
            Assert.True(chaosMode.GetProperty("totalAnomalies").GetInt32() > 0);

            // Verify anomaly types match scenario (mixed-delimiters, quotes, columns)
            var anomalies = chaosMode.GetProperty("injectedAnomalies");
            var errorTypes = new HashSet<string>();
            foreach (var anomaly in anomalies.EnumerateArray())
            {
                errorTypes.Add(anomaly.GetProperty("errorType").GetString()!);
            }

            // Should only contain types from the relativity-import scenario
            Assert.Subset(
                new HashSet<string> { "mixed-delimiters", "quotes", "columns" },
                errorTypes);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AmountOverride_UsesCustomAmount()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, _, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "1000", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "relativity-import",
                "--chaos-amount", "20%", "--seed", "42",
            });
            Assert.Equal(0, exitCode);

            // Verify properties JSON shows the anomaly count reflecting ~20%
            var jsonFiles = Directory.GetFiles(tempPath, "*_properties.json");
            Assert.Single(jsonFiles);

            var json = await File.ReadAllTextAsync(jsonFiles[0]);
            using var doc = JsonDocument.Parse(json);
            var totalAnomalies = doc.RootElement.GetProperty("chaosMode").GetProperty("totalAnomalies").GetInt32();

            // 20% of 1001 lines (1000 records + 1 header) ≈ 200
            Assert.True(totalAnomalies >= 100, $"Expected at least 100 anomalies with 20% amount, got {totalAnomalies}");
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_FullChaos_EnablesAllTypes()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, _, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "500", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "full-chaos", "--seed", "42",
            });
            Assert.Equal(0, exitCode);

            var jsonFiles = Directory.GetFiles(tempPath, "*_properties.json");
            Assert.Single(jsonFiles);

            var json = await File.ReadAllTextAsync(jsonFiles[0]);
            using var doc = JsonDocument.Parse(json);
            var totalAnomalies = doc.RootElement.GetProperty("chaosMode").GetProperty("totalAnomalies").GetInt32();

            // full-chaos at 10% of ~501 lines ≈ 50 anomalies
            Assert.True(totalAnomalies > 0, "full-chaos should inject anomalies");
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_BrokenBoundaries_WorksWithOpt()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var (exitCode, stdout, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "500", "--output-path", tempPath,
                "--loadfile-format", "opt",
                "--chaos-mode", "--chaos-scenario", "broken-boundaries", "--seed", "42",
            });
            Assert.Equal(0, exitCode);
            Assert.Contains("Chaos Scenario: broken-boundaries", stdout);

            var optFiles = Directory.GetFiles(tempPath, "*.opt");
            Assert.Single(optFiles);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }
}
