using System.Text.Json;
using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class LoadFileAuditWriterTests : TempDirectoryTestBase
{
    [Fact]
    public void GenerateAuditJson_OptFormat_SetsCorrectDefaultDelimitersAndEncoding()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with
        {
            Formats = new List<LoadFileFormat> { LoadFileFormat.Opt },
            Encoding = "UTF-8",
            IsEncodingExplicit = false
        };
        request.Chaos = request.Chaos with { ChaosMode = false };
        request.Output = request.Output with { FileCount = 100 };
        request.Tiff = request.Tiff with { PageRange = (1, 1) };

        // Act
        var json = LoadFileAuditWriter.GenerateAuditJson("test.opt", request, Array.Empty<FileData>(), null);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("test.opt", root.GetProperty("fileName").GetString());
        Assert.Equal("OPT (Image)", root.GetProperty("format").GetString());
        Assert.Equal(100, root.GetProperty("totalRecords").GetInt64());

        var properties = root.GetProperty("properties");
        Assert.Equal("ANSI", properties.GetProperty("encoding").GetString());

        var delimiters = properties.GetProperty("delimiters");
        Assert.Equal("char:,", delimiters.GetProperty("column").GetString());
        Assert.Equal("none", delimiters.GetProperty("quote").GetString());
        Assert.Equal("none", delimiters.GetProperty("newline").GetString());
        Assert.Equal("none", delimiters.GetProperty("multiValue").GetString());
        Assert.Equal("none", delimiters.GetProperty("nestedValue").GetString());

        var chaosMode = root.GetProperty("chaosMode");
        Assert.False(chaosMode.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, chaosMode.GetProperty("totalAnomalies").GetInt32());
        Assert.False(chaosMode.TryGetProperty("injectedAnomalies", out _));
    }

    [Fact]
    public void GenerateAuditJson_WithFamilies_IncludesChildRecords()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with
        {
            Formats = new List<LoadFileFormat> { LoadFileFormat.Dat },
            AttachmentRate = 100
        };
        request.Metadata = request.Metadata with { WithFamilies = true, Seed = 42 };
        request.Output = request.Output with { FileCount = 10, FileType = "eml" };

        // Act
        var json = LoadFileAuditWriter.GenerateAuditJson("test.dat", request, Array.Empty<FileData>(), null);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        long childCount = Zipper.LoadFiles.FamilyPlan.GetSimulatedChildCount(request);
        Assert.Equal(10 + childCount, root.GetProperty("totalRecords").GetInt64());
    }

    [Fact]
    public void GenerateAuditJson_WithMultipleAttachments_CountsEachFamilyChild()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 1, FileType = "eml" },
            Metadata = new MetadataConfig { WithFamilies = true },
        };
        var fileData = new[]
        {
            new FileData
            {
                WorkItem = new FileWorkItem { Index = 1, FileType = "eml" },
                Attachment = ("attachment.jpg", new byte[] { 1, 2, 3 }),
                AttachmentMetadata =
                [
                    new FileAttachmentMetadata("attachment.jpg", 3),
                    new FileAttachmentMetadata("attachment.pdf", 4),
                ],
            },
        };

        var datJson = LoadFileAuditWriter.GenerateAuditJson(
            "test.dat", request, fileData, format: LoadFileFormat.Dat);
        var optJson = LoadFileAuditWriter.GenerateAuditJson(
            "test.opt", request, fileData, format: LoadFileFormat.Opt);

        using var datDoc = JsonDocument.Parse(datJson);
        using var optDoc = JsonDocument.Parse(optJson);
        Assert.Equal(3, datDoc.RootElement.GetProperty("totalRecords").GetInt64());
        Assert.Equal(3, optDoc.RootElement.GetProperty("totalRecords").GetInt64());
    }

    [Fact]
    public void GenerateAuditJson_DatFormat_FormatsDelimitersAndIncludesExplicitEncoding()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with
        {
            Formats = new List<LoadFileFormat> { LoadFileFormat.Dat },
            Encoding = "UTF-8",
            IsEncodingExplicit = true
        };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "|",
            QuoteDelimiter = "\"",
            NewlineDelimiter = "\n",
            MultiValueDelimiter = ";",
            NestedValueDelimiter = ":",
            EndOfLine = "CRLF"
        };
        request.Output = request.Output with { FileCount = 50 };

        // Act
        var json = LoadFileAuditWriter.GenerateAuditJson("test.dat", request, Array.Empty<FileData>(), null);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("test.dat", root.GetProperty("fileName").GetString());
        Assert.Equal("DAT (Metadata)", root.GetProperty("format").GetString());
        Assert.Equal(50, root.GetProperty("totalRecords").GetInt64());

        var properties = root.GetProperty("properties");
        Assert.Equal("UTF-8", properties.GetProperty("encoding").GetString());
        Assert.Equal("CRLF", properties.GetProperty("lineEnding").GetString());

        var delimiters = properties.GetProperty("delimiters");
        Assert.Equal("char:|", delimiters.GetProperty("column").GetString());
        Assert.Equal("char:\"", delimiters.GetProperty("quote").GetString());
        Assert.Equal("ascii:10", delimiters.GetProperty("newline").GetString());
        Assert.Equal("char:;", delimiters.GetProperty("multiValue").GetString());
        Assert.Equal("char::", delimiters.GetProperty("nestedValue").GetString());
    }

    [Fact]
    public void GenerateAuditJson_WithAnomalies_SerializesAnomaliesAndEnablesChaosMode()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat } };
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosAmount = "5%"
        };

        var anomalies = new List<ChaosAnomaly>
        {
            new()
            {
                LineNumber = "12",
                RecordID = "DOC001",
                Column = "Author",
                ErrorType = "mixed-delimiters",
                Description = "Mixed delimiters injected"
            }
        };
        request.Output = request.Output with { FileCount = 200 };

        // Act
        var json = LoadFileAuditWriter.GenerateAuditJson("test.dat", request, Array.Empty<FileData>(), anomalies);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var chaosMode = root.GetProperty("chaosMode");
        Assert.True(chaosMode.GetProperty("enabled").GetBoolean());
        Assert.Equal("5%", chaosMode.GetProperty("targetAmount").GetString());
        Assert.Equal(1, chaosMode.GetProperty("totalAnomalies").GetInt32());

        var injected = chaosMode.GetProperty("injectedAnomalies");
        Assert.Equal(JsonValueKind.Array, injected.ValueKind);
        Assert.Equal(1, injected.GetArrayLength());

        var anomaly = injected[0];
        Assert.Equal("12", anomaly.GetProperty("lineNumber").GetString());
        Assert.Equal("DOC001", anomaly.GetProperty("recordID").GetString());
        Assert.Equal("Author", anomaly.GetProperty("column").GetString());
        Assert.Equal("mixed-delimiters", anomaly.GetProperty("errorType").GetString());
        Assert.Equal("Mixed delimiters injected", anomaly.GetProperty("description").GetString());
        Assert.False(anomaly.TryGetProperty("file", out _));
        Assert.False(anomaly.TryGetProperty("peer", out _));
    }

    [Theory]
    [InlineData(LoadFileFormat.Csv, "CSV (Metadata)")]
    [InlineData(LoadFileFormat.EdrmXml, "EDRM-XML (Metadata)")]
    [InlineData(LoadFileFormat.Concordance, "Concordance (Metadata)")]
    public void GenerateAuditJson_NonDatNonOptFormat_ReportsFormatSpecificLabel(LoadFileFormat format, string expectedLabel)
    {
        // Arrange — regression guard for #686: the format field must reflect the
        // actual Load File Format, not fall back to "DAT (Metadata)" for every
        // non-OPT format.
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with
        {
            Formats = new List<LoadFileFormat> { format },
            Encoding = "UTF-8",
            IsEncodingExplicit = true
        };
        request.Output = request.Output with { FileCount = 10 };

        // Act
        var json = LoadFileAuditWriter.GenerateAuditJson("test.out", request, Array.Empty<FileData>(), null, format);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expectedLabel, doc.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public void GenerateAuditJson_NonPrintableDelimiter_FormatsAsAsciiCode()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat } };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "\u0014", // Device Control 4
            QuoteDelimiter = "\u00fe" // Thorn character
        };
        request.Output = request.Output with { FileCount = 10 };

        // Act
        var json = LoadFileAuditWriter.GenerateAuditJson("test.dat", request, Array.Empty<FileData>(), null);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var delimiters = root.GetProperty("properties").GetProperty("delimiters");

        Assert.Equal("ascii:20", delimiters.GetProperty("column").GetString());

        // Thorn is code 254, which is > 126, so it should also format as ascii:254
        Assert.Equal("ascii:254", delimiters.GetProperty("quote").GetString());
    }

    [Fact]
    public void GenerateAuditJson_EmptyDelimiters_FormatsAsNone()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat } };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = string.Empty,
            QuoteDelimiter = null!,
            NewlineDelimiter = string.Empty
        };
        request.Output = request.Output with { FileCount = 10 };

        // Act
        var json = LoadFileAuditWriter.GenerateAuditJson("test.dat", request, Array.Empty<FileData>(), null);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var delimiters = root.GetProperty("properties").GetProperty("delimiters");

        Assert.Equal("none", delimiters.GetProperty("column").GetString());
        Assert.Equal("none", delimiters.GetProperty("quote").GetString());
        Assert.Equal("none", delimiters.GetProperty("newline").GetString());
    }

    [Fact]
    public async Task WriteAsync_WritesJsonFileToDisk()
    {
        // Arrange
        var tempFile = this.GetTempFilePath();
        var expectedPropertiesFile = Path.ChangeExtension(tempFile, null) + "_properties.json";

        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat } };

        try
        {
            request.Output = request.Output with { FileCount = 42 };
            // Act
            var path = await LoadFileAuditWriter.WriteAsync(tempFile, request, Array.Empty<FileData>(), null);

            // Assert
            Assert.Equal(expectedPropertiesFile, path);
            Assert.True(File.Exists(expectedPropertiesFile));

            var jsonContent = await File.ReadAllTextAsync(expectedPropertiesFile);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            Assert.Equal(42, root.GetProperty("totalRecords").GetInt64());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            if (File.Exists(expectedPropertiesFile))
            {
                File.Delete(expectedPropertiesFile);
            }
        }
    }

    [Fact]
    public void GenerateAuditJson_WithTotalRecordsOverrideAndPeerAnomaly_SerializesCorrectly()
    {
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with { ChaosMode = true, ChaosScenario = "cross-format-desync" };
        var anomalies = new List<ChaosAnomaly>
        {
            new ChaosAnomaly
            {
                File = "loadfile.dat",
                LineNumber = "2",
                RecordID = "DOC001",
                Peer = new ChaosAnomalyPeer { File = "loadfile.opt", LineNumber = "1", RecordID = "DOC001" }
            }
        };
        var json = LoadFileAuditWriter.GenerateAuditJson("loadfile.dat", request, Array.Empty<FileData>(), anomalies, LoadFileFormat.Dat, totalRecordsOverride: 42L);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(42L, doc.RootElement.GetProperty("totalRecords").GetInt64());
        Assert.True(doc.RootElement.GetProperty("chaosMode").GetProperty("injectedAnomalies")[0].TryGetProperty("peer", out _));
    }

    // ---- Chaos expectation sidecar tests ----

    private static FileGenerationRequest CreateChaosRequest(int fileCount = 50) => new()
    {
        Output = new OutputConfig { FileCount = fileCount },
        LoadFile = new LoadFileConfig { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat } },
        Chaos = new ChaosConfig { ChaosMode = true, ChaosAmount = "5%" },
        Metadata = new MetadataConfig { Seed = 42 },
    };

    [Fact]
    public void GenerateExpectationSidecarJson_WithAnomalies_RecordsOutcomeClassPerAnomaly()
    {
        // Arrange
        var request = CreateChaosRequest();
        var anomalies = new List<ChaosAnomaly>
        {
            new() { LineNumber = "12", RecordID = "DOC001", Column = "FILEPATH", ErrorType = "path-pointer", Description = "missing path" },
            new() { LineNumber = "13", RecordID = "DOC002", Column = "N/A", ErrorType = "columns", Description = "column shift" },
        };

        // Act
        var json = LoadFileAuditWriter.GenerateExpectationSidecarJson("test.dat", request, Array.Empty<FileData>(), anomalies);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("test.dat", root.GetProperty("loadFile").GetString());
        Assert.Equal("dat", root.GetProperty("loadFileFormat").GetString());
        Assert.Equal(2, root.GetProperty("totalAnomalies").GetInt32());
        Assert.Equal(50, root.GetProperty("invariants").GetProperty("expectedDataRecords").GetInt64());
        Assert.True(root.GetProperty("invariants").GetProperty("remainingRecordsParseCleanly").GetBoolean());

        var entries = root.GetProperty("anomalies");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal(ChaosOutcomeClasses.ParseSucceedsWithLoss, entries[0].GetProperty("outcomeClass").GetString());
        Assert.Equal("path-pointer", entries[0].GetProperty("errorType").GetString());
        Assert.Equal("12", entries[0].GetProperty("lineNumber").GetString());
        Assert.Equal("DOC001", entries[0].GetProperty("recordID").GetString());
        Assert.Equal("FILEPATH", entries[0].GetProperty("columns")[0].GetString());
        Assert.Equal(ChaosOutcomeClasses.ParseFails, entries[1].GetProperty("outcomeClass").GetString());
        Assert.Equal(0, entries[1].GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public void GenerateExpectationSidecarJson_WithoutAnomalies_EmitsEmptyOracle()
    {
        var request = CreateChaosRequest(10);

        var json = LoadFileAuditWriter.GenerateExpectationSidecarJson("test.opt", request, Array.Empty<FileData>(), [], LoadFileFormat.Opt);
        long expectedRecords = JsonDocument.Parse(
            LoadFileAuditWriter.GenerateAuditJson("test.opt", request, Array.Empty<FileData>(), [], LoadFileFormat.Opt))
            .RootElement.GetProperty("totalRecords").GetInt64();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("opt", root.GetProperty("loadFileFormat").GetString());
        Assert.Equal(0, root.GetProperty("totalAnomalies").GetInt32());
        Assert.Equal(0, root.GetProperty("anomalies").GetArrayLength());
        Assert.Equal(expectedRecords, root.GetProperty("invariants").GetProperty("expectedDataRecords").GetInt64());
    }

    [Fact]
    public async Task WriteAsync_ChaosRun_WritesExpectationSidecarNextToAudit()
    {
        // Arrange
        var loadFile = Path.Combine(this.TempDir, "loadfile.dat");
        var request = CreateChaosRequest();
        var anomalies = new List<ChaosAnomaly>
        {
            new() { LineNumber = "2", RecordID = "DOC001", Column = "FILEPATH", ErrorType = "path-pointer", Description = "missing path" },
        };

        // Act
        var propertiesPath = await LoadFileAuditWriter.WriteAsync(loadFile, request, Array.Empty<FileData>(), anomalies);
        var sidecarPath = LoadFileAuditWriter.ExpectationSidecarPath(propertiesPath);

        // Assert
        Assert.True(File.Exists(sidecarPath), $"Expected sidecar at {sidecarPath}");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(sidecarPath));
        Assert.Equal(Path.GetFileName(loadFile), doc.RootElement.GetProperty("loadFile").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("totalAnomalies").GetInt32());
    }

    [Fact]
    public async Task WriteAsync_WithoutChaos_DoesNotWriteExpectationSidecar()
    {
        // Arrange
        var loadFile = Path.Combine(this.TempDir, "loadfile.dat");
        var request = CreateChaosRequest();
        request.Chaos = request.Chaos with { ChaosMode = false };

        // Act
        var propertiesPath = await LoadFileAuditWriter.WriteAsync(loadFile, request, Array.Empty<FileData>(), null);

        // Assert
        Assert.True(File.Exists(propertiesPath));
        Assert.False(File.Exists(LoadFileAuditWriter.ExpectationSidecarPath(propertiesPath)));
    }

    [Fact]
    public async Task WriteAsync_AdversarialValuesRun_DoesNotWriteExpectationSidecar()
    {
        // Arrange: adversarial-values generates producer-valid values, not anomalies.
        var loadFile = Path.Combine(this.TempDir, "loadfile.dat");
        var request = CreateChaosRequest();
        request.Chaos = request.Chaos with { ChaosScenario = "adversarial-values" };
        var anomalies = new List<ChaosAnomaly>
        {
            new() { LineNumber = "2", RecordID = "DOC001", Column = "Enjoyment", ErrorType = "path-pointer", Description = "missing path" },
        };

        // Act
        var propertiesPath = await LoadFileAuditWriter.WriteAsync(loadFile, request, Array.Empty<FileData>(), anomalies);

        // Assert
        Assert.True(File.Exists(propertiesPath));
        Assert.False(File.Exists(LoadFileAuditWriter.ExpectationSidecarPath(propertiesPath)));
    }
}
