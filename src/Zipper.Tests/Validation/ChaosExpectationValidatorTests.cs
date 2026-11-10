using System.Text;
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

    private string WriteBinaryFile(string name, params byte[][] segments)
    {
        var path = Path.Combine(this.TempDir, name);
        File.WriteAllBytes(path, [.. segments.SelectMany(segment => segment)]);
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
    public void Validate_EncodingAnomalyAtFinalBoundary_IgnoresTrailingFragment()
    {
        // Arrange: invalid encoding bytes land after the final EOL, so they decode as one
        // non-record fragment instead of an extra data record.
        var loadFile = this.WriteBinaryFile(
            "loadfile.dat",
            Encoding.UTF8.GetBytes($"{DatHeader}\r\n{DatRow("DOC00000001", "Alice")}\r\n{DatRow("DOC00000002", "Bob")}\r\n"),
            [0xFE, 0xFF]);
        var sidecar = this.WriteFile(
            "loadfile_chaos_expectations.json",
            SidecarJson(2, anomalies: [ValueAnomaly("Boundary 3-4", recordId: "N/A", type: "encoding")]));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert: the fragment is not a record, so the row invariants still hold.
        Assert.False(result.HasErrors, result.GetSummary());
    }

    [Fact]
    public void Validate_EncodingAnomalyBeforeFinalBoundary_CountsEveryRecord()
    {
        // Arrange: bytes injected between line 2 and line 3 prefix line 3's first field, so the
        // file still has exactly two data records.
        var loadFile = this.WriteBinaryFile(
            "loadfile.dat",
            Encoding.UTF8.GetBytes($"{DatHeader}\r\n{DatRow("DOC00000001", "Alice")}\r\n"),
            [0xFE, 0xFF],
            Encoding.UTF8.GetBytes($"{DatRow("DOC00000002", "Bob")}\r\n"));
        var sidecar = this.WriteFile(
            "loadfile_chaos_expectations.json",
            SidecarJson(2, anomalies: [ValueAnomaly("Boundary 2-3", recordId: "N/A", type: "encoding")]));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.False(result.HasErrors, result.GetSummary());
    }

    [Fact]
    public void Validate_OptEncodingAnomalyAtFinalBoundary_IgnoresTrailingFragment()
    {
        // Arrange: OPT has no header, so the final logical line is the last record; its trailing
        // BOM bytes are a fragment, not a record.
        var loadFile = this.WriteBinaryFile(
            "loadfile.opt",
            Encoding.UTF8.GetBytes("a,b,c,d,e,f,g\r\nh,i,j,k,l,m,n\r\n"),
            [0xEF, 0xBB, 0xBF]);
        var sidecar = this.WriteFile(
            "loadfile_chaos_expectations.json",
            SidecarJson(2, loadFile: "loadfile.opt", format: "opt", anomalies: [ValueAnomaly("Boundary 2-3", recordId: "N/A", type: "opt-encoding")]));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.False(result.HasErrors, result.GetSummary());
    }

    [Fact]
    public void Validate_UnrecordedTrailingFragment_StillReportsRecordCountError()
    {
        // Arrange: the same trailing bytes with no boundary anomaly in the sidecar are an extra row.
        var loadFile = this.WriteBinaryFile(
            "loadfile.dat",
            Encoding.UTF8.GetBytes($"{DatHeader}\r\n{DatRow("DOC00000001", "Alice")}\r\n"),
            [0xFE, 0xFF]);
        var sidecar = this.WriteFile("loadfile_chaos_expectations.json", SidecarJson(1));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Message.Contains("record count", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void Validate_WrongTypedSidecarField_ReportsTypeErrorInsteadOfThrowing()
    {
        // Arrange: a valid JSON sidecar whose errorType is a number instead of a string.
        var (loadFile, sidecar) = this.WriteRun(
            """
            {"schemaVersion":1,"loadFile":"loadfile.dat","loadFileFormat":"dat","totalAnomalies":1,
             "invariants":{"expectedDataRecords":2,"remainingRecordsParseCleanly":true},
             "anomalies":[{"errorType":5,"outcomeClass":"parse-succeeds-with-loss","lineNumber":"2","recordID":"DOC00000001"}]}
            """,
            DatRow("DOC00000001", "Alice"),
            DatRow("DOC00000002", "Bob"));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert: the validator must not abort; it reports a finding instead.
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Category == "ChaosExpectation" && finding.Message.Contains("unexpected JSON type", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DamagedHeaderWithWrongRecordCount_StillReportsCountError()
    {
        // Arrange: the header is damaged, the sidecar claims three data records but two exist.
        var loadFile = this.WriteLoadFile(
            "DOCID",
            DatRow("DOC00000001", "Alice"),
            DatRow("DOC00000002", "Bob"));
        var sidecar = this.WriteFile(
            "loadfile_chaos_expectations.json",
            SidecarJson(
                3,
                anomalies: [new
                {
                    errorType = "columns",
                    outcomeClass = ChaosOutcomeClasses.ParseFails,
                    lineNumber = "1",
                    recordID = "HEADER",
                    columns = new[] { "N/A" },
                }]));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert: the truncated header must not suppress the independent record-count check.
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Message.Contains("record count", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Findings, finding => finding.Message.Contains("does not parse cleanly", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DamagedHeaderWithMatchingRecordCount_ReportsNoCountFinding()
    {
        // Arrange: damaged header but the produced row count matches the sidecar.
        var loadFile = this.WriteLoadFile(
            "DOCID",
            DatRow("DOC00000001", "Alice"),
            DatRow("DOC00000002", "Bob"));
        var sidecar = this.WriteFile(
            "loadfile_chaos_expectations.json",
            SidecarJson(
                2,
                anomalies: [new
                {
                    errorType = "columns",
                    outcomeClass = ChaosOutcomeClasses.ParseFails,
                    lineNumber = "1",
                    recordID = "HEADER",
                    columns = new[] { "N/A" },
                }]));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, this.CreateDatRequest());

        // Assert
        Assert.False(result.HasErrors, result.GetSummary());
    }

    [Fact]
    public void Validate_EdrmXmlMatchingDocumentCount_ReturnsNoFindings()
    {
        // Arrange: an EDRM-XML Load File whose document count matches the sidecar.
        var loadFile = this.WriteFile(
            "loadfile.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Records><Document DocID="DOC1" /><Document DocID="DOC2" /></Records>
            """);
        var sidecar = this.WriteFile("loadfile_chaos_expectations.json", SidecarJson(2, loadFile: "loadfile.xml", format: "edrmxml", anomalies: []));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, new FileGenerationRequest());

        // Assert
        Assert.False(result.HasErrors, result.GetSummary());
    }

    [Fact]
    public void Validate_EdrmXmlMismatchedDocumentCount_ReportsError()
    {
        // Arrange: two documents produced, sidecar expects three.
        var loadFile = this.WriteFile(
            "loadfile.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Records><Document DocID="DOC1" /><Document DocID="DOC2" /></Records>
            """);
        var sidecar = this.WriteFile("loadfile_chaos_expectations.json", SidecarJson(3, loadFile: "loadfile.xml", format: "edrmxml", anomalies: []));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, new FileGenerationRequest());

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Message.Contains("document count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EdrmXmlEmptyFile_ReportsError()
    {
        // Arrange: an empty EDRM-XML Load File.
        var loadFile = this.WriteFile("loadfile.xml", string.Empty);
        var sidecar = this.WriteFile("loadfile_chaos_expectations.json", SidecarJson(1, loadFile: "loadfile.xml", format: "edrmxml", anomalies: []));

        // Act
        var result = new ChaosExpectationValidator().Validate(loadFile, sidecar, new FileGenerationRequest());

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding => finding.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }
}
