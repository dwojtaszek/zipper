using System.Text.Json;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;
using Zipper.Profiles.Generation;
using Zipper.Validation;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ChaosScenarioTests : WorkingDirectoryTestBase
{
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunWithCapture(string[] args)
    {
        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(
            () => Program.Main(args)).ConfigureAwait(false);
        return (exitCode, stdout, stderr);
    }

    // --- Scenario registry tests ---
    [Fact]
    public void GetByName_ValidName_ReturnsScenario()
    {
        var scenario = ChaosScenarios.GetByName("structured-import-failures");
        Assert.NotNull(scenario);
        Assert.Equal("structured-import-failures", scenario.Name);
        Assert.Contains("mixed-delimiters", scenario.ChaosTypes, StringComparison.Ordinal);
    }

    [Fact]
    public void GetByName_CaseInsensitive_ReturnsScenario()
    {
        var scenario = ChaosScenarios.GetByName("STRUCTURED-IMPORT-FAILURES");
        Assert.NotNull(scenario);
        Assert.Equal("structured-import-failures", scenario.Name);
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

    [Fact]
    public void GetByName_AllScenarios_MatchesRequirements()
    {
        // 1. structured-import-failures
        var structured = ChaosScenarios.GetByName("structured-import-failures");
        Assert.NotNull(structured);
        Assert.Equal("mixed-delimiters,quotes,columns", structured.ChaosTypes);
        Assert.Equal("3%", structured.DefaultAmount);
        Assert.Equal(LoadFileFormat.Dat, structured.RequiredFormat);

        // 2. encoding-nightmare
        var encoding = ChaosScenarios.GetByName("encoding-nightmare");
        Assert.NotNull(encoding);
        Assert.Equal("encoding,mixed-delimiters", encoding.ChaosTypes);
        Assert.Equal("5%", encoding.DefaultAmount);
        Assert.Equal(LoadFileFormat.Dat, encoding.RequiredFormat);

        // 3. broken-boundaries
        var broken = ChaosScenarios.GetByName("broken-boundaries");
        Assert.NotNull(broken);
        Assert.Equal("opt-boundary,opt-pagecount,opt-path", broken.ChaosTypes);
        Assert.Equal("8%", broken.DefaultAmount);
        Assert.Equal(LoadFileFormat.Opt, broken.RequiredFormat);

        // 4. field-overflow
        var overflow = ChaosScenarios.GetByName("field-overflow");
        Assert.NotNull(overflow);
        Assert.Equal("eol,columns", overflow.ChaosTypes);
        Assert.Equal("2%", overflow.DefaultAmount);
        Assert.Equal(LoadFileFormat.Dat, overflow.RequiredFormat);

        // 5. full-chaos
        var full = ChaosScenarios.GetByName("full-chaos");
        Assert.NotNull(full);
        Assert.Equal(string.Empty, full.ChaosTypes);
        Assert.Equal("10%", full.DefaultAmount);
        Assert.Null(full.RequiredFormat);

        // 6. transfer-encoding-failures
        var transfer = ChaosScenarios.GetByName("transfer-encoding-failures");
        Assert.NotNull(transfer);
        Assert.Equal("mixed-delimiters,encoding,quotes", transfer.ChaosTypes);
        Assert.Equal("4%", transfer.DefaultAmount);
        Assert.Equal(LoadFileFormat.Dat, transfer.RequiredFormat);

        // 7. realistic-import-failures
        var realistic = ChaosScenarios.GetByName("realistic-import-failures");
        Assert.NotNull(realistic);
        Assert.Equal("path-pointer,field-length,date-format,null-byte,trailing-empties", realistic.ChaosTypes);
        Assert.Equal("5%", realistic.DefaultAmount);
        Assert.Equal(LoadFileFormat.Dat, realistic.RequiredFormat);

        // 8. csv-escaping-failures
        var csv = ChaosScenarios.GetByName("csv-escaping-failures");
        Assert.NotNull(csv);
        Assert.Equal("csv-quote-escape,csv-embedded-newline,csv-bom", csv.ChaosTypes);
        Assert.Equal("5%", csv.DefaultAmount);
        Assert.Equal(LoadFileFormat.Csv, csv.RequiredFormat);

        // 9. concordance-import-failures
        var concordance = ChaosScenarios.GetByName("concordance-import-failures");
        Assert.NotNull(concordance);
        Assert.Equal("concordance-delimiter,concordance-quote-escape", concordance.ChaosTypes);
        Assert.Equal("5%", concordance.DefaultAmount);
        Assert.Equal(LoadFileFormat.Concordance, concordance.RequiredFormat);

        // 10. edrm-xml-structural-failures
        var xml = ChaosScenarios.GetByName("edrm-xml-structural-failures");
        Assert.NotNull(xml);
        Assert.Equal("xml-unclosed-tag,xml-bad-entity,xml-encoding-lie", xml.ChaosTypes);
        Assert.Equal("5%", xml.DefaultAmount);
        Assert.Equal(LoadFileFormat.EdrmXml, xml.RequiredFormat);

        // 11. adversarial-values
        var adversarial = ChaosScenarios.GetByName("adversarial-values");
        Assert.NotNull(adversarial);
        Assert.Equal("quotes,mixed-delimiters,eol", adversarial.ChaosTypes);
        Assert.Equal("100%", adversarial.DefaultAmount);
        Assert.Equal(LoadFileFormat.Dat, adversarial.RequiredFormat);
        Assert.True(adversarial.GenerateAdversarialValues);
    }

    // --- CLI integration tests ---
    [Fact]
    public async Task ChaosList_PrintsScenariosAndExitsZero()
    {
        var (exitCode, stdout, _) = await RunWithCapture(new[] { "--chaos-list" });
        Assert.Equal(0, exitCode);
        Assert.Contains("Available Chaos Scenarios", stdout, StringComparison.Ordinal);
        Assert.Contains("structured-import-failures", stdout, StringComparison.Ordinal);
        Assert.Contains("realistic-import-failures", stdout, StringComparison.Ordinal);
        Assert.Contains("adversarial-values", stdout, StringComparison.Ordinal);
        Assert.Contains("full-chaos", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChaosScenario_WithoutChaosMode_ReturnsError()
    {
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-scenario", "structured-import-failures",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("--chaos-scenario requires --chaos-mode", stderr, StringComparison.Ordinal);
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
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "structured-import-failures",
                "--chaos-types", "quotes",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("conflicts with --chaos-types", stderr, StringComparison.Ordinal);
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
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "fake-scenario",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown chaos scenario 'fake-scenario'", stderr, StringComparison.Ordinal);
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
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "100", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "broken-boundaries",
                "--loadfile-format", "dat",
            });
            Assert.Equal(1, exitCode);
            Assert.Contains("requires --loadfile-format opt", stderr, StringComparison.Ordinal);
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
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, stdout, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "1000", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "structured-import-failures", "--seed", "42",
            });
            Assert.Equal(0, exitCode);
            Assert.Contains("Chaos Scenario: structured-import-failures", stdout, StringComparison.Ordinal);

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
            var errorTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anomaly in anomalies.EnumerateArray())
            {
                errorTypes.Add(anomaly.GetProperty("errorType").GetString()!);
            }

            // Should only contain types from the structured-import-failures scenario
            Assert.Subset(
                errorTypes,
                new HashSet<string>(StringComparer.Ordinal) { "mixed-delimiters", "quotes", "columns" });
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
    public async Task ChaosScenario_RealisticImportFailures_GeneratesAllAuditedAnomalies()
    {
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        var baselinePath = Path.Combine(this.TempDir, $"zipper_baseline_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "5", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "realistic-import-failures",
                "--chaos-amount", "5", "--seed", "42",
            });
            Assert.Equal(0, exitCode);

            string propertiesPath = Assert.Single(Directory.GetFiles(tempPath, "*_properties.json"));
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(propertiesPath));
            var anomalies = doc.RootElement.GetProperty("chaosMode").GetProperty("injectedAnomalies")
                .EnumerateArray()
                .ToArray();
            var expectedTypes = new HashSet<string>(ChaosAnomalyTypes.DatRecordOnly, StringComparer.Ordinal);
            var actualTypes = anomalies.Select(anomaly => anomaly.GetProperty("errorType").GetString()!).ToHashSet(StringComparer.Ordinal);
            JsonElement pathPointer = Assert.Single(anomalies, anomaly => anomaly.GetProperty("errorType").GetString() == "path-pointer");

            Assert.Equal(expectedTypes, actualTypes);
            Assert.Contains(
                "PathReconciliation:",
                pathPointer.GetProperty("description").GetString(),
                StringComparison.Ordinal);
            Assert.All(anomalies, anomaly =>
            {
                Assert.False(string.IsNullOrWhiteSpace(anomaly.GetProperty("column").GetString()));
                Assert.NotEqual("N/A", anomaly.GetProperty("column").GetString());
            });

            var (baselineExitCode, _, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "5", "--output-path", baselinePath, "--seed", "42",
            });
            Assert.Equal(0, baselineExitCode);

            string chaosLoadFile = Assert.Single(Directory.GetFiles(tempPath, "*.dat"));
            string baselineLoadFile = Assert.Single(Directory.GetFiles(baselinePath, "*.dat"));
            var chaosLines = await File.ReadAllLinesAsync(chaosLoadFile);
            var baselineLines = await File.ReadAllLinesAsync(baselineLoadFile);
            int lineNumber = int.Parse(pathPointer.GetProperty("lineNumber").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var header = DatLineParser.Parse(chaosLines[0], '\u0014', '\u00fe');
            int pathIndex = header.FindIndex(column => string.Equals(column, "File Path", StringComparison.Ordinal));
            Assert.True(pathIndex >= 0);
            string corruptedPath = DatLineParser.Parse(chaosLines[lineNumber - 1], '\u0014', '\u00fe')[pathIndex];
            string originalPath = DatLineParser.Parse(baselineLines[lineNumber - 1], '\u0014', '\u00fe')[pathIndex];
            string archiveEntry = originalPath.Replace('\\', '/');
            var validator = new PathReconciliationValidator();
            var originalValidation = new ValidationResult();
            validator.Validate([originalPath], [archiveEntry], chaosLoadFile, originalValidation);
            Assert.Empty(originalValidation.Findings);

            var validation = new ValidationResult();
            validator.Validate([corruptedPath], [archiveEntry], chaosLoadFile, validation);
            Assert.Contains(validation.Findings, finding => finding.Category == "PathReconciliation");
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
            if (Directory.Exists(baselinePath))
            {
                Directory.Delete(baselinePath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_GeneratesDeterministicValidTailValues()
    {
        var firstOutputPath = Path.Combine(this.TempDir, $"zipper_adversarial_first_{Guid.NewGuid():N}");
        var secondOutputPath = Path.Combine(this.TempDir, $"zipper_adversarial_second_{Guid.NewGuid():N}");
        try
        {
            foreach (var outputPath in new[] { firstOutputPath, secondOutputPath })
            {
                var (exitCode, stdout, stderr) = await RunWithCapture(new[]
                {
                    "--loadfile-only", "--count", "6", "--output-path", outputPath,
                    "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
                });

                Assert.Equal(0, exitCode);
                Assert.Empty(stderr);
                Assert.Contains("Chaos Scenario: adversarial-values", stdout, StringComparison.Ordinal);
            }

            string firstLoadFile = Assert.Single(Directory.GetFiles(firstOutputPath, "*.dat"));
            string secondLoadFile = Assert.Single(Directory.GetFiles(secondOutputPath, "*.dat"));
            byte[] firstBytes = await File.ReadAllBytesAsync(firstLoadFile);
            byte[] secondBytes = await File.ReadAllBytesAsync(secondLoadFile);
            Assert.Equal(firstBytes, secondBytes);

            string content = await File.ReadAllTextAsync(firstLoadFile);
            Assert.EndsWith("\r\n\r\n\r\n", content, StringComparison.Ordinal);
            var lines = await File.ReadAllLinesAsync(firstLoadFile);
            Assert.Equal(2, lines.Count(string.IsNullOrEmpty));
            var rows = lines
                .Where(line => !string.IsNullOrEmpty(line))
                .Skip(1)
                .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe'))
                .ToArray();
            Assert.Equal(6, rows.Length);

            var values = rows.SelectMany(row => row).ToArray();
            Assert.Contains(values, value => value.Contains('\u0014') && value.Contains('\u00fe'));
            Assert.Contains(values, value => value.Contains("Ã¾", StringComparison.Ordinal));
            Assert.Contains(values, value => value.StartsWith("Quote lookalike:", StringComparison.Ordinal));
            Assert.Contains(values, value => value.StartsWith("Escaped delimiters:", StringComparison.Ordinal));
            var platformLengthValue = Assert.Single(values, value => value.Length == 32_768);
            Assert.Contains('\u0014', platformLengthValue);
            Assert.Contains('\u00fe', platformLengthValue);
            Assert.Contains("Ã¾", platformLengthValue, StringComparison.Ordinal);

            var validation = new ValidatorRunner().ValidateLoadFile(
                firstLoadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);

            string propertiesPath = Assert.Single(Directory.GetFiles(firstOutputPath, "*_properties.json"));
            using var properties = JsonDocument.Parse(await File.ReadAllTextAsync(propertiesPath));
            var chaosMode = properties.RootElement.GetProperty("chaosMode");
            Assert.True(chaosMode.GetProperty("enabled").GetBoolean());
            Assert.Equal(0, chaosMode.GetProperty("totalAnomalies").GetInt32());
        }
        finally
        {
            if (Directory.Exists(firstOutputPath))
            {
                Directory.Delete(firstOutputPath, true);
            }
            if (Directory.Exists(secondOutputPath))
            {
                Directory.Delete(secondOutputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithZeroCount_WritesHeaderOnlyAndPassesValidation()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_header_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "0", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            string content = await File.ReadAllTextAsync(loadFile);
            Assert.EndsWith("\r\n\r\n\r\n", content, StringComparison.Ordinal);
            var lines = File.ReadAllLines(loadFile);
            Assert.Single(lines, line => !string.IsNullOrEmpty(line));
            Assert.Equal(2, lines.Count(string.IsNullOrEmpty));

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public void ChaosScenario_AdversarialValues_WithExpandedRecordCountAboveIntMax_CreatesLongSafePlan()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 1_073_741_824,
                FileType = "eml",
            },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
            Chaos = new ChaosConfig
            {
                ChaosMode = true,
                ChaosAmount = "1",
                ChaosScenario = "adversarial-values",
            },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
        };
        var composer = new DatComposer(request, WriterMode.LoadfileOnly);
        Assert.Equal(request.Output.FileCount, request.SimulatedFamilyChildCount);
        Assert.Equal(AdversarialValueTailPlan.TrailingEmptyRowCount, composer.TrailingEmptyRows);
        long recordCount = request.Output.FileCount + request.SimulatedFamilyChildCount!.Value;

        var plan = Assert.IsType<AdversarialValueTailPlan>(
            AdversarialValueTailPlan.Create(request, recordCount));
        Assert.Null(plan.GetValue(0));

        string platformLengthValue = Assert.IsType<string>(plan.GetValue(recordCount));
        Assert.Equal(AdversarialValueTailPlan.PlatformFieldLength, platformLengthValue.Length);
        Assert.Contains('\u0014', platformLengthValue);
        Assert.Contains('\u00fe', platformLengthValue);
        Assert.Contains("Ã¾", platformLengthValue, StringComparison.Ordinal);
        Assert.Null(plan.GetValue(recordCount + 1));
    }

    [Fact]
    public void ChaosScenario_AdversarialValues_WithIntMaxCount_PreservesSamplerSelection()
    {
        const int recordCount = int.MaxValue;
        const int seed = 42;
        var request = new FileGenerationRequest
        {
            Chaos = new ChaosConfig
            {
                ChaosMode = true,
                ChaosAmount = "1",
                ChaosScenario = "adversarial-values",
            },
            Metadata = new MetadataConfig { Seed = seed },
        };
        var selectedRecords = ChaosSampler.SelectTargetLines(recordCount, 1, new Random(seed));
        Assert.DoesNotContain(recordCount, selectedRecords);

        var plan = Assert.IsType<AdversarialValueTailPlan>(
            AdversarialValueTailPlan.Create(request, recordCount));

        Assert.Null(plan.GetValue(recordCount));
    }

    [Fact]
    public void ChaosScenario_AdversarialValues_WithAboveIntMaxCount_UsesSeededStreamingSelection()
    {
        const long recordCount = (long)int.MaxValue + 1;
        const int sampleSize = 100;
        const int seed = 42;
        var request = new FileGenerationRequest
        {
            Chaos = new ChaosConfig
            {
                ChaosMode = true,
                ChaosAmount = "50%",
                ChaosScenario = "adversarial-values",
            },
            Metadata = new MetadataConfig { Seed = seed },
        };
        var plan = Assert.IsType<AdversarialValueTailPlan>(
            AdversarialValueTailPlan.Create(request, recordCount));
        var replay = Assert.IsType<AdversarialValueTailPlan>(
            AdversarialValueTailPlan.Create(request, recordCount));
        var selected = new bool[sampleSize];
        var replaySelected = new bool[sampleSize];

        for (int index = 1; index <= sampleSize; index++)
        {
            selected[index - 1] = plan.GetValue(index) is not null;
            replaySelected[index - 1] = replay.GetValue(index) is not null;
        }

        Assert.Equal(selected, replaySelected);
        Assert.Contains(true, selected);
        Assert.Contains(false, selected);
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithNegativeCount_ReturnsError()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_negative_{Guid.NewGuid():N}");
        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--count", "-1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("--count must be a positive number", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithMultiRatioMixAndZeroCount_ReturnsCleanCliError()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_types_zero_{Guid.NewGuid():N}");
        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--types", "pdf:50,jpg:50", "--count", "0", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: --types is not supported with --loadfile-only.", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithAmountOverride_UsesOneCombinedTailRecord()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_amount_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "6", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values",
                "--chaos-amount", "1", "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var lines = await File.ReadAllLinesAsync(loadFile);
            var rows = lines
                .Where(line => !string.IsNullOrEmpty(line))
                .Skip(1)
                .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe'))
                .ToArray();
            Assert.Equal(6, rows.Length);
            var adversarialRecords = rows.Where(row => row.Any(value =>
                value.StartsWith("Escaped delimiters:", StringComparison.Ordinal)
                || value.StartsWith("Quote lookalike:", StringComparison.Ordinal))).ToArray();

            var adversarialRecord = Assert.Single(adversarialRecords);
            var platformValue = Assert.Single(adversarialRecord, value => value.StartsWith("Escaped delimiters:", StringComparison.Ordinal));
            Assert.Contains('\u0014', platformValue);
            Assert.Contains('\u00fe', platformValue);
            Assert.Contains("Ã¾", platformValue, StringComparison.Ordinal);
            Assert.Equal(32_768, platformValue.Length);

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithPartialAmount_SelectsMultipleRecords()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_partial_amount_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "6", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values",
                "--chaos-amount", "2", "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var rows = (await File.ReadAllLinesAsync(loadFile))
                .Where(line => !string.IsNullOrEmpty(line))
                .Skip(1)
                .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe'))
                .ToArray();
            Assert.Equal(6, rows.Length);

            var adversarialRows = rows.Where(row => row.Any(value =>
                value.StartsWith("Escaped delimiters:", StringComparison.Ordinal)
                || value.StartsWith("Quote lookalike:", StringComparison.Ordinal))).ToArray();
            Assert.Equal(2, adversarialRows.Length);
            var platformLengthValue = Assert.Single(rows.SelectMany(row => row), value => value.Length == 32_768);
            Assert.Contains(adversarialRows[0], value => value == platformLengthValue);

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithEmailFamilies_SelectsEmittedRecords()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_families_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--type", "eml", "--count", "4", "--output-path", outputPath,
                "--with-families", "--attachment-rate", "100",
                "--chaos-mode", "--chaos-scenario", "adversarial-values", "--chaos-amount", "50%", "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var lines = await File.ReadAllLinesAsync(loadFile);
            var header = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
            int authorIndex = header.FindIndex(column => string.Equals(column, "Author", StringComparison.OrdinalIgnoreCase));
            Assert.True(authorIndex >= 0);

            var rows = lines
                .Where(line => !string.IsNullOrEmpty(line))
                .Skip(1)
                .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe'))
                .ToArray();
            Assert.Equal(8, rows.Length);
            string propertiesPath = Assert.Single(Directory.GetFiles(outputPath, "*_properties.json"));
            using var properties = JsonDocument.Parse(await File.ReadAllTextAsync(propertiesPath));
            Assert.Equal(rows.Length, properties.RootElement.GetProperty("totalRecords").GetInt64());

            var adversarialRows = rows.Where(row =>
                row[authorIndex].StartsWith("Escaped delimiters:", StringComparison.Ordinal)
                || row[authorIndex].StartsWith("Quote lookalike:", StringComparison.Ordinal)).ToArray();
            Assert.Equal(4, adversarialRows.Length);
            string platformLengthValue = Assert.Single(
                rows.Select(row => row[authorIndex]),
                value => value.Length == AdversarialValueTailPlan.PlatformFieldLength);
            Assert.Equal(platformLengthValue, adversarialRows[0][authorIndex]);
            Assert.Contains('\u0014', platformLengthValue);
            Assert.Contains('\u00fe', platformLengthValue);
            Assert.Contains("Ã¾", platformLengthValue, StringComparison.Ordinal);

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithAmountAboveTotal_TargetsEveryRecord()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_over_amount_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "6", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values",
                "--chaos-amount", "200%", "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var rows = (await File.ReadAllLinesAsync(loadFile))
                .Where(line => !string.IsNullOrEmpty(line))
                .Skip(1)
                .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe'))
                .ToArray();
            Assert.Equal(6, rows.Length);
            Assert.All(rows, row => Assert.Contains(row, value =>
                value.StartsWith("Escaped delimiters:", StringComparison.Ordinal)
                || value.StartsWith("Quote lookalike:", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithColumnProfile_GeneratesValidTailValues()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_profile_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "4", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values",
                "--column-profile", "standard", "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var lines = await File.ReadAllLinesAsync(loadFile);
            var header = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
            int authorIndex = header.FindIndex(column => string.Equals(column, "AUTHOR", StringComparison.Ordinal));
            Assert.True(authorIndex >= 0);

            var rows = lines
                .Where(line => !string.IsNullOrEmpty(line))
                .Skip(1)
                .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe'))
                .ToArray();
            Assert.Equal(4, rows.Length);
            var platformValue = rows[0][authorIndex];
            Assert.Equal(32_768, platformValue.Length);
            Assert.Contains('\u0014', platformValue);
            Assert.Contains('\u00fe', platformValue);
            Assert.Contains("Ã¾", platformValue, StringComparison.Ordinal);

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithCustomDelimiters_GeneratesValidTailValues()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_custom_delimiters_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "2", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values",
                "--col-delim", "char:|", "--quote-delim", "char:\"",
                "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var lines = await File.ReadAllLinesAsync(loadFile);
            var header = DatLineParser.Parse(lines[0], '|', '"');
            int authorIndex = header.FindIndex(column => string.Equals(column, "Author", StringComparison.Ordinal));
            Assert.True(authorIndex >= 0);
            var firstRow = DatLineParser.Parse(lines[1], '|', '"');
            Assert.Contains('|', firstRow[authorIndex]);
            Assert.Contains('"', firstRow[authorIndex]);
            Assert.Contains("Ã¾", firstRow[authorIndex], StringComparison.Ordinal);
            Assert.Equal(32_768, firstRow[authorIndex].Length);

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '|',
                quoteDelimiter: '"');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithUtf16Encoding_GeneratesValidTailValues()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_utf16_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "1", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values",
                "--encoding", "UTF-16", "--seed", "42",
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var encoding = EncodingHelper.GetEncoding("UTF-16");
            Assert.NotNull(encoding);
            var lines = await File.ReadAllLinesAsync(loadFile, encoding);
            var header = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
            int authorIndex = header.FindIndex(column => string.Equals(column, "Author", StringComparison.Ordinal));
            Assert.True(authorIndex >= 0);
            var row = DatLineParser.Parse(lines[1], '\u0014', '\u00fe');
            Assert.Equal(32_768, row[authorIndex].Length);
            Assert.Contains("Ã¾", row[authorIndex], StringComparison.Ordinal);

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                encoding: encoding,
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithSourceRecordsAndProfile_GeneratesValidTailValues()
    {
        string sourceCsv = Path.Combine(this.TempDir, "adversarial-source.csv");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_source_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(sourceCsv, "FilePath,FileType\nfolder/doc.pdf,pdf\n");

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--input-csv", sourceCsv, "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--column-profile", "standard", "--seed", "42",
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
        var lines = await File.ReadAllLinesAsync(loadFile);
        var header = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
        int authorIndex = header.FindIndex(column => string.Equals(column, "Author", StringComparison.OrdinalIgnoreCase));
        Assert.True(authorIndex >= 0);
        var row = DatLineParser.Parse(lines[1], '\u0014', '\u00fe');
        Assert.Equal(32_768, row[authorIndex].Length);
        Assert.Contains("Ã¾", row[authorIndex], StringComparison.Ordinal);

        var validation = new ValidatorRunner().ValidateLoadFile(
            loadFile,
            "dat",
            headerColumns: null,
            expectedEol: "\r\n",
            columnDelimiter: '\u0014',
            quoteDelimiter: '\u00fe');
        Assert.Empty(validation.Findings);
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithPlatformLengthSourceMetadata_PreservesBothValues()
    {
        string sourceCsv = Path.Combine(this.TempDir, "adversarial-source-long-metadata.csv");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_source_long_metadata_{Guid.NewGuid():N}");
        string sourceValue = new('S', AdversarialValueTailPlan.PlatformFieldLength);
        const string sourceAuthor = "Source Counsel";
        await File.WriteAllTextAsync(
            sourceCsv,
            $"FilePath,FileType,AUTHOR,REVIEWNOTES\nfolder/doc.pdf,pdf,{sourceAuthor},{sourceValue}\n");

        try
        {
            var (exitCode, _, stderr) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--input-csv", sourceCsv, "--count", "1", "--output-path", outputPath,
                "--chaos-mode", "--chaos-scenario", "adversarial-values",
                "--column-profile", "litigation", "--seed", "42",
            });

            Assert.True(exitCode == 0, stderr);
            Assert.Empty(stderr);
            string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
            var lines = await File.ReadAllLinesAsync(loadFile);
            var header = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
            int reviewNotesIndex = header.FindIndex(column => string.Equals(column, "REVIEWNOTES", StringComparison.Ordinal));
            int authorIndex = header.FindIndex(column => string.Equals(column, "AUTHOR", StringComparison.Ordinal));
            Assert.True(reviewNotesIndex >= 0);
            Assert.True(authorIndex >= 0);
            var row = DatLineParser.Parse(lines[1], '\u0014', '\u00fe');
            Assert.Equal(sourceAuthor, row[authorIndex]);
            Assert.Equal(sourceValue, row[reviewNotesIndex]);
            const string generatedPrefix = "Escaped delimiters: left\u0014middle\u00feright | Quote lookalike: Ã¾ | ";
            string generatedValue = Assert.Single(row, value => value.StartsWith(generatedPrefix, StringComparison.Ordinal));
            Assert.Equal(AdversarialValueTailPlan.PlatformFieldLength, generatedValue.Length);
            Assert.Equal(2, row.Count(value => value.Length == AdversarialValueTailPlan.PlatformFieldLength));

            var validation = new ValidatorRunner().ValidateLoadFile(
                loadFile,
                "dat",
                headerColumns: null,
                expectedEol: "\r\n",
                columnDelimiter: '\u0014',
                quoteDelimiter: '\u00fe');
            Assert.Empty(validation.Findings);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithSourceRecordsAndMetadata_GeneratesValidTailValues()
    {
        string sourceCsv = Path.Combine(this.TempDir, "adversarial-source-metadata.csv");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_source_metadata_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(sourceCsv, "FilePath,FileType,AUTHOR\nfolder/doc.pdf,pdf,Source Counsel\n");

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--input-csv", sourceCsv, "--count", "1", "--output-path", outputPath,
            "--with-metadata", "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
        var lines = await File.ReadAllLinesAsync(loadFile);
        var header = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
        int authorIndex = header.FindIndex(column => string.Equals(column, "Author", StringComparison.OrdinalIgnoreCase));
        Assert.True(authorIndex >= 0);
        var row = DatLineParser.Parse(lines[1], '\u0014', '\u00fe');
        Assert.Equal(32_768, row[authorIndex].Length);
        Assert.Contains('\u0014', row[authorIndex]);
        Assert.Contains('\u00fe', row[authorIndex]);
        Assert.Contains("Ã¾", row[authorIndex], StringComparison.Ordinal);
        Assert.DoesNotContain("Source Counsel", row);

        var validation = new ValidatorRunner().ValidateLoadFile(
            loadFile,
            "dat",
            headerColumns: null,
            expectedEol: "\r\n",
            columnDelimiter: '\u0014',
            quoteDelimiter: '\u00fe');
        Assert.Empty(validation.Findings);
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithSourceRecordsWithoutProfile_ReturnsError()
    {
        string sourceCsv = Path.Combine(this.TempDir, "adversarial-source.csv");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_source_without_profile_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(sourceCsv, "FilePath,FileType\nfolder/doc.pdf,pdf\n");

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--input-csv", sourceCsv, "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires --with-metadata or a --column-profile", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithProfileWithoutTextColumn_ReturnsError()
    {
        string profilePath = Path.Combine(this.TempDir, "identifier-only-profile.json");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_identifier_profile_{Guid.NewGuid():N}");
        var profile = new ColumnProfile
        {
            Name = "identifier-only",
            Columns = [new ColumnDefinition { Name = "DOCID", Type = "identifier", Required = true }],
        };
        await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(profile));

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--column-profile", profilePath, "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a text or long-text Column Profile column", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithFileSizeOnlyProfile_ReturnsError()
    {
        string profilePath = Path.Combine(this.TempDir, "file-size-only-profile.json");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_file_size_profile_{Guid.NewGuid():N}");
        var profile = new ColumnProfile
        {
            Name = "file-size-only",
            Columns =
            [
                new ColumnDefinition { Name = "DOCID", Type = "identifier", Required = true },
                new ColumnDefinition { Name = "FILESIZE", Type = "text", Required = true },
            ],
        };
        await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(profile));

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--column-profile", profilePath, "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a text or long-text Column Profile column", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Theory]
    [InlineData("Source Counsel")]
    [InlineData("")]
    public async Task ChaosScenario_AdversarialValues_WithEveryEligibleColumnMappedToSourceMetadata_ReturnsError(string authorValue)
    {
        string sourceCsv = Path.Combine(this.TempDir, "adversarial-source-author.csv");
        string profilePath = Path.Combine(this.TempDir, "author-only-profile.json");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_source_author_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(sourceCsv, $"FilePath,FileType,AUTHOR\nfolder/doc.pdf,pdf,{authorValue}\n");
        await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(new ColumnProfile
        {
            Name = "author-only",
            Columns =
            [
                new ColumnDefinition { Name = "DOCID", Type = "identifier", Required = true },
                new ColumnDefinition { Name = "AUTHOR", Type = "text", Required = true },
            ],
        }));

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--input-csv", sourceCsv, "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--column-profile", profilePath, "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("not mapped to Source Metadata", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithImplicitOptFormat_ReturnsError()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_implicit_opt_{Guid.NewGuid():N}");
        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--type", "tiff", "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires DAT-only output. Image file types default to DAT and OPT; pass --loadfile-format dat explicitly.", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Theory]
    [InlineData("--col-delim", "ascii:10")]
    [InlineData("--col-delim", "ascii:0")]
    [InlineData("--quote-delim", "ascii:0")]
    [InlineData("--quote-delim", "ascii:10")]
    [InlineData("--quote-delim", "none")]
    [InlineData("--newline-delim", "ascii:254")]
    [InlineData("--newline-delim", "ascii:0")]
    [InlineData("--newline-delim", "ascii:10")]
    [InlineData("--newline-delim", "ascii:13")]
    public async Task ChaosScenario_AdversarialValues_WithInvalidDelimiters_ReturnsError(
        string delimiterFlag,
        string delimiter)
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_delimiter_{Guid.NewGuid():N}");
        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            delimiterFlag, delimiter,
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires DAT-safe delimiters", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithMultilineReplacementDelimiter_ReturnsError()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_multiline_replacement_{Guid.NewGuid():N}");
        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--delimiter-newline", "\\r\\n",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires DAT-safe delimiters", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithMatchingDelimiters_ReturnsError()
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_matching_delimiters_{Guid.NewGuid():N}");
        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--col-delim", "char:|", "--quote-delim", "char:|",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires DAT-safe delimiters", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_AdversarialValues_WithUnsafeEncodingFallback_ReturnsError()
    {
        string sourceCsv = Path.Combine(this.TempDir, "adversarial-source-encoding.csv");
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_unsafe_fallback_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(
            sourceCsv,
            "FilePath,FileType,REVIEWNOTES\nfolder/doc.pdf,pdf,before 中|after\n");

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--input-csv", sourceCsv, "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--column-profile", "litigation", "--encoding", "ANSI",
            "--col-delim", "char:|", "--quote-delim", "char:?", "--seed", "42",
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("requires DAT-safe delimiters", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Theory]
    [InlineData("ASCII", null)]
    [InlineData("ANSI", "char:漢")]
    public async Task ChaosScenario_AdversarialValues_WithUnrepresentableEncoding_ReturnsError(
        string encoding,
        string? quoteDelimiter)
    {
        var outputPath = Path.Combine(this.TempDir, $"zipper_adversarial_encoding_{Guid.NewGuid():N}");
        var args = new List<string>
        {
            "--loadfile-only", "--count", "1", "--output-path", outputPath,
            "--chaos-mode", "--chaos-scenario", "adversarial-values",
            "--encoding", encoding,
        };
        if (quoteDelimiter is not null)
        {
            args.Add("--quote-delim");
            args.Add(quoteDelimiter);
        }

        var (exitCode, _, stderr) = await RunWithCapture(args.ToArray());

        Assert.Equal(1, exitCode);
        Assert.Contains("requires DAT-safe delimiters", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task ChaosScenario_SourceDrivenPathPointer_AvoidsPathAndSidecarCollisions()
    {
        string sourceCsv = Path.Combine(this.TempDir, "source-paths.csv");
        string outputPath = Path.Combine(this.TempDir, $"zipper_source_chaos_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(sourceCsv, "FilePath,FileType\nfolder\\doc.pdf,pdf\nfolder\\doc_MISSING.docx,docx\n");

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--input-csv", sourceCsv, "--count", "2", "--output-path", outputPath,
            "--chaos-mode", "--chaos-types", "path-pointer", "--chaos-amount", "2", "--seed", "42",
        });
        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);

        string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
        var lines = await File.ReadAllLinesAsync(loadFile);
        var columns = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
        int pathIndex = columns.FindIndex(column => string.Equals(column, "File Path", StringComparison.Ordinal));
        Assert.True(pathIndex >= 0);

        var mutatedPaths = lines.Skip(1)
            .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe')[pathIndex])
            .ToArray();
        Assert.Equal(
            new[] { "folder/doc_MISSING_1.pdf", "folder/doc_MISSING_MISSING.docx" },
            mutatedPaths);

        var validation = new ValidationResult();
        new PathReconciliationValidator().Validate(
            mutatedPaths,
            ["folder/doc.pdf", "folder/doc_MISSING.docx"],
            loadFile,
            validation);
        Assert.Equal(2, validation.Findings.Count);
    }

    [Fact]
    public async Task ChaosScenario_DirectoryTemplatePathPointer_AvoidsSourcePathCollisions()
    {
        string sourceDirectory = Path.Combine(this.TempDir, "source-template");
        string sourceFolder = Path.Combine(sourceDirectory, "folder");
        string outputPath = Path.Combine(this.TempDir, $"zipper_template_chaos_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceFolder);
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "doc.pdf"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "doc_MISSING.docx"), string.Empty);

        var (exitCode, _, stderr) = await RunWithCapture(new[]
        {
            "--loadfile-only", "--directory-template", sourceDirectory, "--count", "2", "--output-path", outputPath,
            "--chaos-mode", "--chaos-types", "path-pointer", "--chaos-amount", "2", "--seed", "42",
        });
        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);

        string loadFile = Assert.Single(Directory.GetFiles(outputPath, "*.dat"));
        var lines = await File.ReadAllLinesAsync(loadFile);
        var columns = DatLineParser.Parse(lines[0], '\u0014', '\u00fe');
        int pathIndex = columns.FindIndex(column => string.Equals(column, "File Path", StringComparison.Ordinal));
        Assert.True(pathIndex >= 0);

        var mutatedPaths = lines.Skip(1)
            .Select(line => DatLineParser.Parse(line, '\u0014', '\u00fe')[pathIndex])
            .ToArray();
        Assert.Equal(
            new[] { "folder/doc_MISSING_1.pdf", "folder/doc_MISSING_MISSING.docx" },
            mutatedPaths);
    }

    [Fact]
    public async Task ChaosScenario_AmountOverride_UsesCustomAmount()
    {
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "1000", "--output-path", tempPath,
                "--chaos-mode", "--chaos-scenario", "structured-import-failures",
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
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
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
        var tempPath = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        try
        {
            var (exitCode, stdout, _) = await RunWithCapture(new[]
            {
                "--loadfile-only", "--count", "500", "--output-path", tempPath,
                "--loadfile-format", "opt",
                "--chaos-mode", "--chaos-scenario", "broken-boundaries", "--seed", "42",
            });
            Assert.Equal(0, exitCode);
            Assert.Contains("Chaos Scenario: broken-boundaries", stdout, StringComparison.Ordinal);

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
