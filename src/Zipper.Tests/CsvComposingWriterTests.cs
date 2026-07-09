using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

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
    public async Task CsvWriter_WithAnsiEncoding_ProducesAnsiOutput()
    {
        var request = this.CreateTestRequest();
        request.LoadFile = request.LoadFile with { Encoding = "ANSI" };
        var fileData = this.CreateTestFileData();
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
        var outputPath = Path.Combine(this.TempDir, "test_ansi.csv");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var bytes = await File.ReadAllBytesAsync(outputPath);

        // Not a UTF-8 BOM
        if (bytes.Length >= 3)
        {
            Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }
        // Not a UTF-16 BOM
        if (bytes.Length >= 2)
        {
            Assert.False(bytes[0] == 0xFF && bytes[1] == 0xFE);
            Assert.False(bytes[0] == 0xFE && bytes[1] == 0xFF);
        }

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var encoding = System.Text.Encoding.GetEncoding(1252);
        var content = encoding.GetString(bytes);
        var roundTripBytes = encoding.GetBytes(content);
        Assert.Equal(bytes, roundTripBytes);
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
                    FilePathInZip = "folder/doc1.eml"
                },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                DataLength = 100
            }
        };

        var content = await this.CaptureCsvOutput(request, files);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Contains("BEGATTACH", lines[0], StringComparison.Ordinal);
        Assert.Contains("ENDATTACH", lines[0], StringComparison.Ordinal);
        Assert.Contains("PARENTDOCID", lines[0], StringComparison.Ordinal);

        var parentLine = lines[1];
        Assert.Contains("DOC00000001", parentLine, StringComparison.Ordinal);
        Assert.Contains("DOC00000001_A001", parentLine, StringComparison.Ordinal);

        var childLine = lines[2];
        Assert.Contains("DOC00000001_A001", childLine, StringComparison.Ordinal);
        Assert.Contains("DOC00000001", childLine, StringComparison.Ordinal);
    }
}

