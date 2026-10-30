using System.Reflection;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;
using Zipper.SourceInput;
using Zipper.Validation;

namespace Zipper.Tests;

public class ChaosEngineBuilderTests
{
    private static object? GetPrivateField(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(obj);
    }

    [Fact]
    public void Build_WhenChaosModeIsDisabled_ReturnsNull()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with { ChaosMode = false };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.Null(engine);
    }

    [Fact]
    public void Build_WhenChaosModeIsEnabled_ReturnsEngineWithSpecifiedSettings()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosAmount = "5%",
            ChaosTypes = "mixed-delimiters,quotes"
        };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "|",
            QuoteDelimiter = "\"",
            EndOfLine = "CRLF"
        };
        request.Metadata = request.Metadata with { Seed = 123 };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);

        var applier = GetPrivateField(engine, "applier");
        Assert.NotNull(applier);
        Assert.IsType<DatAnomalyApplier>(applier);

        var colDelim = GetPrivateField(applier, "columnDelimiter") as string;
        var quoteDelim = GetPrivateField(applier, "quoteDelimiter") as string;
        var eol = GetPrivateField(applier, "eol") as string;

        var enabledTypes = GetPrivateField(engine, "enabledTypes") as HashSet<string>;

        Assert.Equal("|", colDelim);
        Assert.Equal("\"", quoteDelim);
        Assert.Equal("\r\n", eol);

        Assert.NotNull(enabledTypes);
        Assert.Contains("mixed-delimiters", enabledTypes);
        Assert.Contains("quotes", enabledTypes);
        Assert.Equal(2, enabledTypes.Count);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("datetime")]
    [InlineData("legacydatesent")]
    [InlineData("legacydatecreated")]
    [InlineData("emailsentdate")]
    [InlineData("syntheticemailsentdate")]
    public void Build_DateFormat_UsesProfileTypesWithoutNameHeuristics(string dateType)
    {
        var request = new FileGenerationRequest { LoadfileOnly = true };
        request.Metadata = request.Metadata with
        {
            Seed = 42,
            ColumnProfile = new ColumnProfile
            {
                Columns =
                [
                    new ColumnDefinition { Name = "Updated By", Type = "text" },
                    new ColumnDefinition { Name = "ReviewStamp", Type = dateType },
                ],
            },
        };
        request.Delimiters = request.Delimiters with { ColumnDelimiter = "|", QuoteDelimiter = "\"", EndOfLine = "CRLF" };
        request.Chaos = request.Chaos with { ChaosMode = true, ChaosAmount = "1", ChaosTypes = "date-format" };
        var record = new LoadFileRecord
        {
            Columns = ["Updated By", "ReviewStamp"],
            Values = ["jdoe", "2024-01-15"],
            RecordId = "DOC00000001",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);
        var engine = ChaosEngineBuilder.Build(request, 2, LoadFileFormat.Dat);
        Assert.NotNull(engine);
        Assert.True(engine.ShouldIntercept(2));

        string result = engine.Intercept(2, line, record.RecordId, record, serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Equal("jdoe", fields[0]);
        Assert.Contains(fields[1], new[] { "00/00/0000", "2018-01-15 a.m.", "2018-044-09" });
        Assert.Equal("ReviewStamp", Assert.Single(engine.Anomalies).Column);
    }

    [Fact]
    public void Build_DateFormat_DoesNotTargetTextTypedBuiltInColumn()
    {
        var request = new FileGenerationRequest
        {
            LoadfileOnly = true,
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
        };
        request.Metadata = request.Metadata with
        {
            Seed = 42,
            ColumnProfile = new ColumnProfile
            {
                Columns = [new ColumnDefinition { Name = "Sent Date", Type = "text" }],
            },
        };
        request.Chaos = request.Chaos with { ChaosMode = true, ChaosAmount = "1", ChaosTypes = "date-format" };

        var record = new LoadFileRecord
        {
            Columns = ["Sent Date"],
            Values = ["Jane Doe"],
            RecordId = "DOC00000001",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);
        var engine = ChaosEngineBuilder.Build(request, 2, LoadFileFormat.Dat);
        Assert.NotNull(engine);
        Assert.True(engine.ShouldIntercept(2));

        string result = engine.Intercept(2, line, record.RecordId, record, serializer);

        Assert.Equal(line, result);
        Assert.Empty(engine.Anomalies);
    }

    [Fact]
    public void Build_DateFormat_UsesProfileDateColumnByIndex_WhenNamesNormalizeTheSame()
    {
        var profile = new ColumnProfile
        {
            Name = "punctuation-distinct-columns",
            Columns =
            [
                new ColumnDefinition { Name = "Control Number", Type = "identifier" },
                new ColumnDefinition { Name = "Review-Stamp", Type = "text" },
                new ColumnDefinition { Name = "Review Stamp", Type = "date" },
            ],
        };
        ColumnProfileLoader.Validate(profile);

        var request = new FileGenerationRequest { LoadfileOnly = true };
        request.Metadata = request.Metadata with { Seed = 42, ColumnProfile = profile };
        request.Chaos = request.Chaos with { ChaosMode = true, ChaosAmount = "1", ChaosTypes = "date-format" };
        var record = new LoadFileRecord
        {
            Columns = ["Control Number", "Review-Stamp", "Review Stamp"],
            Values = ["DOC00000001", "review value", "2024-01-15"],
            RecordId = "DOC00000001",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);
        var engine = ChaosEngineBuilder.Build(request, 2, LoadFileFormat.Dat);
        Assert.NotNull(engine);
        Assert.True(engine.ShouldIntercept(2));

        string result = engine.Intercept(2, line, record.RecordId, record, serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Equal("review value", fields[1]);
        Assert.Contains(fields[2], new[] { "00/00/0000", "2018-01-15 a.m.", "2018-044-09" });
        Assert.Equal("Review Stamp", Assert.Single(engine.Anomalies).Column);
    }

    [Fact]
    public void Build_DateFormat_UsesLoadfileOnlyDefaultDateColumn_WhenProfileIsAbsent()
    {
        var request = new FileGenerationRequest { LoadfileOnly = true };
        request.Metadata = request.Metadata with { Seed = 42 };
        request.Chaos = request.Chaos with { ChaosMode = true, ChaosAmount = "1", ChaosTypes = "date-format" };
        var record = new LoadFileRecord
        {
            Columns = ["Date Sent", "Author"],
            Values = ["2024-01-15", "Jane Doe"],
            RecordId = "DOC00000001",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);
        var engine = ChaosEngineBuilder.Build(request, 2, LoadFileFormat.Dat);
        Assert.NotNull(engine);
        Assert.True(engine.ShouldIntercept(2));

        string result = engine.Intercept(2, line, record.RecordId, record, serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Contains(fields[0], new[] { "00/00/0000", "2018-01-15 a.m.", "2018-044-09" });
        Assert.Equal("Jane Doe", fields[1]);
        Assert.Equal("Date Sent", Assert.Single(engine.Anomalies).Column);
    }

    [Fact]
    public void Build_PathPointer_AvoidsExistingSourcePaths()
    {
        var sourceRecords = new[]
        {
            new SourceRecord { RelativePath = "folder/doc.pdf", FileType = "pdf" },
            new SourceRecord { RelativePath = "folder/doc_MISSING.docx", FileType = "docx" },
        };
        var request = new FileGenerationRequest
        {
            LoadfileOnly = true,
            SourceRecords = sourceRecords,
            SourcePaths = new HashSet<string>(
                sourceRecords.Select(sourceRecord => sourceRecord.RelativePath),
                StringComparer.OrdinalIgnoreCase),
        };
        request.Chaos = request.Chaos with { ChaosMode = true, ChaosAmount = "2", ChaosTypes = "path-pointer" };
        request.Delimiters = request.Delimiters with { ColumnDelimiter = "|", QuoteDelimiter = "\"", EndOfLine = "CRLF" };
        request.Metadata = request.Metadata with { Seed = 42 };

        var engine = ChaosEngineBuilder.Build(request, 3, LoadFileFormat.Dat);
        Assert.NotNull(engine);
        Assert.False(engine.ShouldIntercept(1));
        Assert.True(engine.ShouldIntercept(2));

        var record = new LoadFileRecord
        {
            Columns = ["File Path"],
            Values = [sourceRecords[0].RelativePath],
            RecordId = "DOC00000001",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);

        string result = engine.Intercept(2, line, record.RecordId, record, serializer);
        string mutatedPath = DatLineParser.Parse(result, '|', '"')[0];

        Assert.Equal("folder/doc_MISSING_1.pdf", mutatedPath);
        Assert.DoesNotContain(sourceRecords, sourceRecord =>
            string.Equals(sourceRecord.RelativePath, mutatedPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("path-pointer", Assert.Single(engine.Anomalies).ErrorType);
    }

    [Fact]
    public void Build_WithScenario_ResolvesTypesAndAmountFromScenario()
    {
        // Arrange
        var scenarioName = "structured-import-failures";
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = scenarioName,
            ChaosAmount = null // Fall back to scenario default
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var enabledTypes = GetPrivateField(engine, "enabledTypes") as HashSet<string>;
        var sampler = GetPrivateField(engine, "sampler");
        Assert.NotNull(sampler);
        var targetLines = GetPrivateField(sampler, "targetLines") as HashSet<long>;

        var scenario = ChaosScenarios.GetByName(scenarioName);
        Assert.NotNull(scenario);

        var expectedTypes = new HashSet<string>(
            scenario.ChaosTypes.Split(',').Select(t => t.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        Assert.NotNull(enabledTypes);
        Assert.Equal(expectedTypes, enabledTypes);

        var pct = double.Parse(scenario.DefaultAmount.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
        var expectedCount = Math.Max(1, (int)(100 * pct / 100.0));

        Assert.NotNull(targetLines);
        Assert.Equal(expectedCount, targetLines.Count);
    }

    [Fact]
    public void Build_WithScenarioAndExplicitAmount_ScenarioResolvesTypesButKeepsExplicitAmount()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = "structured-import-failures",
            ChaosAmount = "20" // Explicit exact count
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var sampler = GetPrivateField(engine, "sampler");
        Assert.NotNull(sampler);
        var targetLines = GetPrivateField(sampler, "targetLines") as HashSet<long>;

        Assert.NotNull(targetLines);
        Assert.Equal(20, targetLines.Count);
    }

    [Fact]
    public void Build_WithAdversarialValuesScenario_DoesNotCreateCorruptionEngine()
    {
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = "adversarial-values",
        };

        var engine = ChaosEngineBuilder.Build(request, 7, LoadFileFormat.Dat);

        Assert.Null(engine);
    }

    [Fact]
    public void Build_WithFullChaosScenario_IncludesOptEncoding()
    {
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = "full-chaos",
        };

        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Opt);

        Assert.NotNull(engine);
        var enabledTypes = GetPrivateField(engine, "enabledTypes") as HashSet<string>;
        Assert.NotNull(enabledTypes);
        Assert.Contains("opt-encoding", enabledTypes);
    }

    [Fact]
    public void Build_WithInvalidScenario_IgnoresScenarioAndUsesRequestSettings()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = "nonexistent-scenario",
            ChaosAmount = "15",
            ChaosTypes = "mixed-delimiters"
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var enabledTypes = GetPrivateField(engine, "enabledTypes") as HashSet<string>;
        var sampler = GetPrivateField(engine, "sampler");
        Assert.NotNull(sampler);
        var targetLines = GetPrivateField(sampler, "targetLines") as HashSet<long>;

        Assert.NotNull(enabledTypes);
        Assert.Contains("mixed-delimiters", enabledTypes);
        Assert.Single(enabledTypes);

        Assert.NotNull(targetLines);
        Assert.Equal(15, targetLines.Count);
    }

    [Fact]
    public void Build_FormatOpt_ForcesOptDelimiters()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with { ChaosMode = true };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "|",
            QuoteDelimiter = "\""
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Opt);

        // Assert
        Assert.NotNull(engine);
        var applier = GetPrivateField(engine, "applier");

        Assert.NotNull(applier);
        Assert.IsType<OptAnomalyApplier>(applier);
    }

    [Fact]
    public void Build_FormatDat_NullRequestDelimiters_FallbackToDefaultDelimiters()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with { ChaosMode = true };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = null!, // Should default to thorn or similar
            QuoteDelimiter = null!
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var applier = GetPrivateField(engine, "applier");
        Assert.NotNull(applier);
        Assert.IsType<DatAnomalyApplier>(applier);

        var colDelim = GetPrivateField(applier, "columnDelimiter") as string;
        var quoteDelim = GetPrivateField(applier, "quoteDelimiter") as string;

        Assert.Equal("\u0014", colDelim); // Defaults to Device Control 4 (Thorn-like delimiter in Concordance)
        Assert.Equal("\u00fe", quoteDelim); // Defaults to thorn quote
    }
}
