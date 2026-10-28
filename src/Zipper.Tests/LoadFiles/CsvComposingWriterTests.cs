using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Emails;
using Zipper.LoadFiles;
using Zipper.Tests.TestHelpers;

namespace Zipper.Tests;

public class CsvComposingWriterTests : TempDirectoryTestBase
{
    private async Task<string> CaptureCsvOutput(FileGenerationRequest request, List<FileData> files)
    {
        var writer = new CsvComposingWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files).ConfigureAwait(false);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task CsvWriter_ShouldWriteCsvFormat()
    {
        var request = this.CreateTestRequest();
        var fileData = this.CreateTestFileData();
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
        var outputPath = Path.Combine(this.TempDir, "test.csv");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.Contains("CONTROL NUMBER", lines[0], StringComparison.Ordinal);
        Assert.Contains("DOC00000001", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithSpecialCharacters_ShouldEscapeFields()
    {
        var request = this.CreateTestRequest();
        var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FilePathInZip = "folder_001/file_with_\"quotes\".pdf"
                    },
                    Data = Array.Empty<byte>()
                },
            };
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
        var outputPath = Path.Combine(this.TempDir, "test.csv");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var content = await File.ReadAllTextAsync(outputPath);

        Assert.Contains("\"\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WritesDocumentIdInDataRows()
    {
        var request = this.CreateTestRequest();
        var files = this.CreateTestFileData(2);
        var content = await this.CaptureCsvOutput(request, files);
        Assert.Contains("DOC00000001", content, StringComparison.Ordinal);
        Assert.Contains("DOC00000002", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithText_WritesTextPath()
    {
        var request = this.CreateTestRequest();
        request.Output = request.Output with { WithText = true };
        var files = this.CreateTestFileData(1);
        files[0] = files[0] with { WorkItem = files[0].WorkItem with { FilePathInZip = "folder_001/file_00000001.pdf" } };
        var content = await this.CaptureCsvOutput(request, files);
        Assert.Contains("folder_001/file_00000001.txt", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_MetadataHeader_ReflectsRequestConfiguration()
    {
        var request = this.CreateTestRequest();
        request.Metadata = request.Metadata with { WithMetadata = true };
        var files = this.CreateTestFileData(1);
        var contentWith = await this.CaptureCsvOutput(request, files);
        Assert.Contains("CUSTODIAN", contentWith, StringComparison.Ordinal);

        request.Metadata = request.Metadata with { WithMetadata = false };
        var contentWithout = await this.CaptureCsvOutput(request, files);
        Assert.DoesNotContain("CUSTODIAN", contentWithout, StringComparison.Ordinal);

        var emlRequest = this.CreateTestRequest("eml");
        emlRequest.Metadata = emlRequest.Metadata with { WithMetadata = false };
        var emlContent = await this.CaptureCsvOutput(emlRequest, files);
        Assert.Contains("CUSTODIAN", emlContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_EmlColumns_OnlyForEmlFileType()
    {
        var files = this.CreateTestFileData(1);
        var emlRequest = this.CreateTestRequest("eml");
        var emlContent = await this.CaptureCsvOutput(emlRequest, files);

        // Assert against exact header tokens, not substrings: "TO" is a substring
        // of "CUSTODIAN", so a substring check would give a false positive/negative.
        var emlHeader = emlContent.Split('\n')[0].Trim('\r', '\n', ' ').Split(',');
        Assert.Contains("TO", emlHeader);
        Assert.Contains("FROM", emlHeader);

        var pdfRequest = this.CreateTestRequest("pdf");
        var pdfContent = await this.CaptureCsvOutput(pdfRequest, files);
        var pdfHeader = pdfContent.Split('\n')[0].Trim('\r', '\n', ' ').Split(',');
        Assert.DoesNotContain("TO", pdfHeader);
    }

    [Fact]
    public async Task CsvWriter_PageCount_OnlyForTiffWithPageRange()
    {
        var request = this.CreateTestRequest("tiff");
        request.Tiff = request.Tiff with { PageRange = (1, 10) };
        var files = this.CreateTestFileData(1);
        files[0] = files[0] with { PageCount = 5 };
        var contentWith = await this.CaptureCsvOutput(request, files);
        Assert.Contains("PAGE COUNT", contentWith, StringComparison.Ordinal);

        request.Tiff = request.Tiff with { PageRange = null };
        var contentWithout = await this.CaptureCsvOutput(request, files);
        Assert.DoesNotContain("PAGE COUNT", contentWithout, StringComparison.Ordinal);

        var pdfRequest = this.CreateTestRequest("pdf");
        pdfRequest.Tiff = pdfRequest.Tiff with { PageRange = (1, 10) };
        var pdfContent = await this.CaptureCsvOutput(pdfRequest, files);
        Assert.DoesNotContain("PAGE COUNT", pdfContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithMetadata_ContainsCompressionMethodInHeaderAndRow()
    {
        var request = this.CreateTestRequest();
        request.Metadata = request.Metadata with { WithMetadata = true };
        var files = this.CreateTestFileData(1);
        var content = await this.CaptureCsvOutput(request, files);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("COMPRESSION METHOD", lines[0], StringComparison.Ordinal);
        Assert.Contains("Deflate", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithoutMetadata_OmitsCompressionMethod()
    {
        var request = this.CreateTestRequest();
        request.Metadata = request.Metadata with { WithMetadata = false };
        var files = this.CreateTestFileData(1);
        var content = await this.CaptureCsvOutput(request, files);

        Assert.DoesNotContain("COMPRESSION METHOD", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithBatesConfig_WritesCorrectBatesNumber()
    {
        var request = this.CreateTestRequest();
        request.Bates = new BatesNumberConfig
        {
            Prefix = "TEST",
            Start = 1000,
            Digits = 6,
            Increment = 1,
        };
        var files = this.CreateTestFileData(1);
        files[0] = files[0] with { WorkItem = files[0].WorkItem with { Index = 10 } };
        var content = await this.CaptureCsvOutput(request, files);
        Assert.Contains("TEST001009", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithMetadata_ContainsCustodianAndFileSizeInDataRow()
    {
        var request = this.CreateTestRequest();
        request.Metadata = request.Metadata with { WithMetadata = true };
        var files = this.CreateTestFileData(1);
        files[0] = files[0] with { DataLength = 1024, WorkItem = files[0].WorkItem with { FolderNumber = 5 } };
        var content = await this.CaptureCsvOutput(request, files);
        var dataLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.Contains("Custodian 5", dataLine, StringComparison.Ordinal);
        Assert.Contains("1024", dataLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithUtf16Encoding_ProducesUtf16Output()
    {
        var request = this.CreateTestRequest();
        request.LoadFile = request.LoadFile with { Encoding = "UTF-16" };
        var fileData = this.CreateTestFileData();
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
        var outputPath = Path.Combine(this.TempDir, "test_utf16.csv");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var bytes = await File.ReadAllBytesAsync(outputPath);

        Assert.True(bytes.Length >= 2);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);

        var content = await File.ReadAllTextAsync(outputPath, System.Text.Encoding.Unicode);
        Assert.Contains("CONTROL NUMBER", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_AnsiWithNonAsciiMetadata_EmitsWindows1252Bytes()
    {
        var request = this.CreateTestRequest("eml");
        request.LoadFile = request.LoadFile with { Encoding = "ANSI" };
        var fileData = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FileName = "00000001.eml",
                    FilePathInZip = "folder_001/00000001.eml",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
                Email = new Email
                {
                    To = "recipient@example.com",
                    From = "sender@example.com",
                    Subject = "café €",
                    SentDate = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
                },
            },
        };
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
        var outputPath = Path.Combine(this.TempDir, "test_ansi.csv");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var bytes = await File.ReadAllBytesAsync(outputPath);

        // Absence of BOM (not UTF-8 BOM, not UTF-16 BOM)
        Assert.True(bytes.Length >= 3);
        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.False(bytes[0] == 0xFF && bytes[1] == 0xFE);
        Assert.False(bytes[0] == 0xFE && bytes[1] == 0xFF);

        // Expected byte sequence for "café €" in Windows-1252: 63 61 66 E9 20 80
        // Derived independently of the production Encoding instance.
        var expectedWindows1252Bytes = new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x20, 0x80 };
        Assert.True(bytes.AsSpan().IndexOf(expectedWindows1252Bytes) >= 0);

        // Windows-1252 must not contain multi-byte UTF-8 sequences for 'é' (C3 A9) or '€' (E2 82 AC)
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xC3, 0xA9 }) < 0);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xE2, 0x82, 0xAC }) < 0);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(1252);
        var content = encoding.GetString(bytes);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var headers = DelimitedTestParser.ParseLine(lines[0], ',', '"');
        var record = DelimitedTestParser.ParseRecord(headers, lines[1], ',', '"');

        Assert.Equal("café €", record["SUBJECT"]);
        Assert.Contains("café €", content, StringComparison.Ordinal);
        Assert.Contains("CONTROL NUMBER", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_Utf8WithNonAsciiMetadata_EmitsUtf8Bytes()
    {
        var request = this.CreateTestRequest("eml");
        request.LoadFile = request.LoadFile with { Encoding = "UTF-8" };
        var fileData = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FileName = "00000001.eml",
                    FilePathInZip = "folder_001/00000001.eml",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
                Email = new Email
                {
                    To = "recipient@example.com",
                    From = "sender@example.com",
                    Subject = "café €",
                    SentDate = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
                },
            },
        };
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
        var outputPath = Path.Combine(this.TempDir, "test_utf8.csv");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var bytes = await File.ReadAllBytesAsync(outputPath);

        // UTF-8 BOM preamble (EF BB BF)
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        // Expected byte sequence for "café €" in UTF-8: 63 61 66 C3 A9 20 E2 82 AC
        // Derived independently of the production Encoding instance.
        var expectedUtf8Bytes = new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xE2, 0x82, 0xAC };
        Assert.True(bytes.AsSpan().IndexOf(expectedUtf8Bytes) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xC3, 0xA9 }) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xE2, 0x82, 0xAC }) >= 0);

        // UTF-8 output must not contain the single-byte Windows-1252 encoding (63 61 66 E9 20 80)
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x20, 0x80 }) < 0);

        var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var headers = DelimitedTestParser.ParseLine(lines[0], ',', '"');
        var record = DelimitedTestParser.ParseRecord(headers, lines[1], ',', '"');

        Assert.Equal("café €", record["SUBJECT"]);
        Assert.Contains("café €", content, StringComparison.Ordinal);
        Assert.Contains("CONTROL NUMBER", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithFamilies_IncludesFamilyColumnsAndRows()
    {
        var request = this.CreateTestRequest("eml");
        request.Metadata = request.Metadata with { WithFamilies = true };

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FileName = "doc1.eml",
                    FilePathInZip = "folder/doc1.eml",
                },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                DataLength = 100,
            },
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 2,
                    FolderNumber = 1,
                    FileName = "doc2.eml",
                    FilePathInZip = "folder/doc2.eml",
                },
                DataLength = 100,
            },
        };

        var content = await this.CaptureCsvOutput(request, files);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Cardinality: 1 header + 2 parent records + 1 attachment record = 4 rows
        Assert.Equal(4, lines.Length);

        var headers = DelimitedTestParser.ParseLine(lines[0], ',', '"');
        Assert.Contains("CONTROL NUMBER", headers);
        Assert.Contains("BEGATTACH", headers);
        Assert.Contains("ENDATTACH", headers);
        Assert.Contains("PARENTDOCID", headers);
        Assert.DoesNotContain("BEGATTY", headers);
        Assert.DoesNotContain("ENDATTY", headers);

        var parent1 = DelimitedTestParser.ParseRecord(headers, lines[1], ',', '"');
        var child1 = DelimitedTestParser.ParseRecord(headers, lines[2], ',', '"');
        var parent2 = DelimitedTestParser.ParseRecord(headers, lines[3], ',', '"');

        // Parent 1 (with attachment)
        Assert.Equal("DOC00000001", parent1["CONTROL NUMBER"]);
        Assert.Equal("DOC00000001", parent1["BEGATTACH"]);
        Assert.Equal("DOC00000001_A001", parent1["ENDATTACH"]);
        Assert.Empty(parent1["PARENTDOCID"]);

        // Child 1 (attachment of Parent 1)
        Assert.Equal("DOC00000001_A001", child1["CONTROL NUMBER"]);
        Assert.Equal("DOC00000001", child1["BEGATTACH"]);
        Assert.Equal("DOC00000001_A001", child1["ENDATTACH"]);
        Assert.Equal("DOC00000001", child1["PARENTDOCID"]);
        // Child must not self-parent: PARENTDOCID points to parent, not child's own Control Number
        Assert.NotEqual(child1["CONTROL NUMBER"], child1["PARENTDOCID"]);

        // Parent 2 (without attachment: no child record, no spurious parent/child links)
        Assert.Equal("DOC00000002", parent2["CONTROL NUMBER"]);
        Assert.Equal("DOC00000002", parent2["BEGATTACH"]);
        Assert.Equal("DOC00000002", parent2["ENDATTACH"]);
        Assert.Empty(parent2["PARENTDOCID"]);
    }

    [Fact]
    public async Task CsvWriter_WithCollectionMetadata_IncludesFiveCollectionMetadataColumns()
    {
        // REQ-184: Standard mode CSV includes the five collection metadata columns.
        var request = this.CreateTestRequest();
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Csv } };
        request.Metadata = request.Metadata with { WithCollectionMetadata = true, Seed = 42 };
        var files = this.CreateTestFileData(2);

        var content = await this.CaptureCsvOutput(request, files);

        Assert.Contains("DATA_SOURCE", content, StringComparison.Ordinal);
        Assert.Contains("COLLECTION_DATE", content, StringComparison.Ordinal);
        Assert.Contains("DENISTED", content, StringComparison.Ordinal);
        Assert.Contains("DEDUPE_GROUP_ID", content, StringComparison.Ordinal);
        Assert.Contains("PROCESSING_STATUS", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvWriter_WithChaosEngine_InjectsCsvSpecificAnomaly()
    {
        var request = this.CreateTestRequest();
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FileName = "file_with_\"quote\".pdf",
                    FilePathInZip = "folder_001/file_with_\"quote\".pdf",
                    FileType = "pdf",
                },
                DataLength = 128,
            },
        };
        var engine = new ChaosEngine(2, "2", "csv-quote-escape", LoadFileFormat.Csv, ",", "\"", "\n", seed: 42);

        using var stream = new MemoryStream();
        await new CsvComposingWriter().WriteAsync(stream, request, files, engine);

        Assert.NotEmpty(engine.Anomalies);
        Assert.All(engine.Anomalies, anomaly =>
        {
            Assert.Equal("csv-quote-escape", anomaly.ErrorType);
            Assert.Contains(anomaly.ErrorType, ChaosAnomalyTypes.Csv);
        });
    }
}
