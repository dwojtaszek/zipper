using System.Text.Json;
using Xunit;

namespace Zipper.Tests;

public class LoadfileAuditWriterTests
{
    [Fact]
    public void GenerateAuditJson_OptFormat_SetsCorrectDefaultDelimitersAndEncoding()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with
        {
            LoadFileFormat = LoadFileFormat.Opt,
            Encoding = "UTF-8",
            IsEncodingExplicit = false
        };
        request.Chaos = request.Chaos with { ChaosMode = false };

        // Act
        var context = new LoadfileAuditWriter.AuditContext(100, null, LoadFileFormat.Opt);
        var json = LoadfileAuditWriter.GenerateAuditJson("test.opt", request, context);

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
    public void GenerateAuditJson_DatFormat_FormatsDelimitersAndIncludesExplicitEncoding()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with
        {
            LoadFileFormat = LoadFileFormat.Dat,
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

        // Act
        var context = new LoadfileAuditWriter.AuditContext(50, null, LoadFileFormat.Dat);
        var json = LoadfileAuditWriter.GenerateAuditJson("test.dat", request, context);

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
        request.LoadFile = request.LoadFile with { LoadFileFormat = LoadFileFormat.Dat };
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

        var engine = new ChaosEngine(201, "5%", "mixed-delimiters", LoadFileFormat.Dat, "|", "\"", "\r\n", null);
        var anomaliesField = typeof(ChaosEngine).GetField("anomalies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var internalAnomalies = (List<ChaosAnomaly>)anomaliesField!.GetValue(engine)!;
        internalAnomalies.AddRange(anomalies);

        // Act
        var context = new LoadfileAuditWriter.AuditContext(200, engine, LoadFileFormat.Dat);
        var json = LoadfileAuditWriter.GenerateAuditJson("test.dat", request, context);

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
    }

    [Fact]
    public void GenerateAuditJson_NonPrintableDelimiter_FormatsAsAsciiCode()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with { LoadFileFormat = LoadFileFormat.Dat };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "\u0014", // Device Control 4
            QuoteDelimiter = "\u00fe" // Thorn character
        };

        // Act
        var context = new LoadfileAuditWriter.AuditContext(10, null, LoadFileFormat.Dat);
        var json = LoadfileAuditWriter.GenerateAuditJson("test.dat", request, context);

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
        request.LoadFile = request.LoadFile with { LoadFileFormat = LoadFileFormat.Dat };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = string.Empty,
            QuoteDelimiter = null!,
            NewlineDelimiter = string.Empty
        };

        // Act
        var context = new LoadfileAuditWriter.AuditContext(10, null, LoadFileFormat.Dat);
        var json = LoadfileAuditWriter.GenerateAuditJson("test.dat", request, context);

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
        var tempFile = Path.GetTempFileName();
        var expectedPropertiesFile = Path.ChangeExtension(tempFile, null) + "_properties.json";

        var request = new FileGenerationRequest();
        request.LoadFile = request.LoadFile with { LoadFileFormat = LoadFileFormat.Dat };

        try
        {
            // Act
            var context = new LoadfileAuditWriter.AuditContext(42, null, LoadFileFormat.Dat);
            var path = await LoadfileAuditWriter.WriteAsync(tempFile, request, context);

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
    public void CreateContext_DatFormat_ReturnsCorrectRecordsAndLines()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Output = request.Output with { FileCount = 5 };
        var files = new List<FileData>
        {
            new FileData { WorkItem = new FileWorkItem { Index = 1 } },
            new FileData { WorkItem = new FileWorkItem { Index = 2 } }
        };

        // Act
        var context = LoadfileAuditWriter.CreateContext(request, files, LoadFileFormat.Dat);

        // Assert
        Assert.Equal(2, context.TotalRecords);
        Assert.Null(context.ChaosEngine);
        Assert.Equal(LoadFileFormat.Dat, context.Format);
    }

    [Fact]
    public void CreateContext_OptFormat_SumsPageCounts()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Output = request.Output with { FileCount = 5 };
        var files = new List<FileData>
        {
            new FileData { WorkItem = new FileWorkItem { Index = 1 }, PageCount = 3 },
            new FileData { WorkItem = new FileWorkItem { Index = 2 }, PageCount = 2 }
        };

        // Act
        var context = LoadfileAuditWriter.CreateContext(request, files, LoadFileFormat.Opt);

        // Assert
        Assert.Equal(2, context.TotalRecords);
        Assert.Equal(LoadFileFormat.Opt, context.Format);
    }
}
