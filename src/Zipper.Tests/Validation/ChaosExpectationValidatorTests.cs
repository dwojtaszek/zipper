using System.Text.Json;
using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

public class ChaosExpectationValidatorTests : TempDirectoryTestBase
{
    private const char DatColumnDelimiter = '\u0014';

    private FileGenerationRequest CreateDatRequest() => new()
    {
        LoadFile = new LoadFileConfig { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat }, Encoding = "UTF-8", IsEncodingExplicit = true },
        Chaos = new ChaosConfig { ChaosMode = true, ChaosAmount = "5%" },
    };

    private static string DatHeader => $"DOCID{DatColumnDelimiter}AUTHOR";

    private static string DatRow(string id, string author) => $"{id}{DatColumnDelimiter}{author}";

    private string WriteLoadFile(params string[] lines) =>
        this.WriteFile("loadfile.dat", string.Join(Environment.NewLine, lines) + Environment.NewLine);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(this.TempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string SidecarJson(
        int expectedDataRecords,
        string loadFile = "loadfile.dat",
        int schemaVersion = 1,
        string format = "dat",
        params object[] anomalies)
    {
        var document = new
        {
            schemaVersion,
            loadFile,
            loadFileFormat = format,
            totalAnomalies = anomalies.Length,
            invariants = new
            {
                expectedDataRecords,
                remainingRecordsParseCleanly = true,
            },
            anomalies,
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static object ValueAnomaly(string lineNumber, string recordId = "DOC00000001", string type = "path-pointer") => new
    {
        errorType = type,
        outcomeClass = ChaosOutcomeClasses.ParseSucceedsWithLoss,
        lineNumber,
        recordID = recordId,
        columns = new[] { "FILEPATH" },
    };

    private (string LoadFile, string Sidecar) WriteRun(string sidecarJson, params string[] rows)
    {
        var loadFile = this.WriteLoadFile([DatHeader, .. rows]);
        var sidecar = this.WriteFile("loadfile_chaos_expectations.json", sidecarJson);
        return (loadFile, sidecar);
    }

    [Fact]
    public void Validate_MatchingSidecarAndProducedOutput_ReturnsNoFindings()
    {
        // Arrange: one value-level anomaly on a record that still parses cleanly.
        var (loadFile, sidecar) = this.WriteRun(
            SidecarJson(2, anomalies: [ValueAnomaly("2")]),
            DatRow("DOC00000001", "Alice"),
            DatRow("DOC00000002", "Bob"));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.False(result.HasErrors, result.GetSummary());
    }

    [Fact]
    public void Validate_UnexpectedRecordCount_ReportsInvariantError()
    {
        // Arrange: sidecar claims three data records but the produced file has two.
        var (loadFile, sidecar) = this.WriteRun(
            SidecarJson(3, anomalies: [ValueAnomaly("2")]),
            DatRow("DOC00000001", "Alice"),
            DatRow("DOC00000002", "Bob"));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Category == "ChaosExpectation" && finding.Message.Contains("record count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UndamagedRecordDoesNotParseCleanly_ReportsInvariantError()
    {
        // Arrange: line 3 has an extra column but the sidecar only marks line 2 damaged.
        var (loadFile, sidecar) = this.WriteRun(
            SidecarJson(3, anomalies: [ValueAnomaly("2")]),
            DatRow("DOC00000001", "Alice"),
            DatRow("DOC00000002", "EXTRA"),
            "DOC00000003\u0014Bob\u0014Surprise");

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Category == "ChaosExpectation" && finding.Message.Contains("does not parse cleanly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ParseFailsAnomalyWhoseRecordStillParses_ReportsOutcomeMismatch()
    {
        // Arrange: the sidecar declares a structural parse failure but the record parses cleanly.
        var (loadFile, sidecar) = this.WriteRun(
            SidecarJson(
                2,
                anomalies: [new
                {
                    errorType = "mixed-delimiters",
                    outcomeClass = ChaosOutcomeClasses.ParseFails,
                    lineNumber = "2",
                    recordID = "DOC00000001",
                    columns = new[] { "N/A" },
                }]),
            DatRow("DOC00000001", "Alice"),
            DatRow("DOC00000002", "Bob"));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Category == "ChaosExpectation" && finding.Message.Contains("parse-fails", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SchemaVersionMismatch_ReportsSchemaError()
    {
        var (loadFile, sidecar) = this.WriteRun(SidecarJson(1, schemaVersion: 2), DatRow("DOC00000001", "Alice"));

        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Category == "ChaosExpectation" && finding.Message.Contains("schemaVersion", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SidecarNamesDifferentLoadFile_ReportsError()
    {
        var (loadFile, sidecar) = this.WriteRun(SidecarJson(1, loadFile: "other.dat"), DatRow("DOC00000001", "Alice"));

        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Category == "ChaosExpectation" && finding.Message.Contains("loadFile", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MalformedSidecarJson_ReportsError()
    {
        var (loadFile, sidecar) = this.WriteRun("{not json", DatRow("DOC00000001", "Alice"));

        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Category == "ChaosExpectation");
    }
}
