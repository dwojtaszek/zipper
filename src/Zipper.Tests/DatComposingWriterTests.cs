using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Emails;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class DatComposingWriterTests : TempDirectoryTestBase
{
    [Fact]
    public async Task WriteAsync_BasicHeader_WritesCorrectColumns()
    {
        var (_, lines) = await WriteAndCapture(DefaultRequest(), []);
        var header = lines[0];

        Assert.Contains("Control Number", header, StringComparison.Ordinal);
        Assert.Contains("File Path", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Custodian", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithMetadata_IncludesMetadataColumns()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { WithMetadata = true };
        var output = await WriteAndCaptureOutput(request, []);
        Assert.Contains("Custodian", output, StringComparison.Ordinal);
        Assert.Contains("Date Sent", output, StringComparison.Ordinal);
        Assert.Contains("Author", output, StringComparison.Ordinal);
        Assert.Contains("File Size", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithEmailType_IncludesEmlColumns()
    {
        var request = DefaultRequest();
        request.Output = request.Output with { FileType = "eml" };
        var output = await WriteAndCaptureOutput(request, []);
        Assert.Contains("To", output, StringComparison.Ordinal);
        Assert.Contains("From", output, StringComparison.Ordinal);
        Assert.Contains("Subject", output, StringComparison.Ordinal);
        Assert.Contains("Sent Date", output, StringComparison.Ordinal);
        Assert.Contains("Attachment", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_StandardMode_WithColumnProfile_UsesProfileColumns()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { ColumnProfile = Zipper.Profiles.BuiltInProfiles.Litigation };
        var files = new List<FileData> { MakeFileData(1) };

        var (_, lines) = await WriteAndCapture(request, files);
        var header = lines[0];

        Assert.Contains("BEGBATES", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PAGECOUNT", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_WithBatesConfig_IncludesBatesNumberColumn()
    {
        var request = DefaultRequest();
        request.Bates = new BatesNumberConfig
        {
            Prefix = "TEST",
            Start = 1,
            Digits = 6,
            Increment = 1,
        };
        var output = await WriteAndCaptureOutput(request, []);
        Assert.Contains("Bates Number", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithTiffPageRange_IncludesPageCountColumn()
    {
        var request = DefaultRequest();
        request.Output = request.Output with { FileType = "tiff" };
        request.Tiff = request.Tiff with { PageRange = (1, 10) };
        var output = await WriteAndCaptureOutput(request, []);
        Assert.Contains("Page Count", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithText_IncludesExtractedTextColumn()
    {
        var request = DefaultRequest();
        request.Output = request.Output with { WithText = true };
        var output = await WriteAndCaptureOutput(request, []);
        Assert.Contains("Extracted Text", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithFileData_WritesDataRows()
    {
        var request = DefaultRequest();
        var files = new List<FileData> { MakeFileData(1), MakeFileData(2) };
        var (_, lines) = await WriteAndCapture(request, files);
        Assert.Equal(3, lines.Length);
        Assert.Contains("DOC00000001", lines[1], StringComparison.Ordinal);
        Assert.Contains("DOC00000002", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithMetadata_WritesMetadataValues()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { WithMetadata = true };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.Contains("Custodian 1", output, StringComparison.Ordinal);
        Assert.Contains("File Size", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithCollectionMetadata_IncludesCollectionMetadataColumns()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { WithCollectionMetadata = true };
        var output = await WriteAndCaptureOutput(request, []);
        Assert.Contains("Data Source", output, StringComparison.Ordinal);
        Assert.Contains("Collection Date", output, StringComparison.Ordinal);
        Assert.Contains("De-Nisted", output, StringComparison.Ordinal);
        Assert.Contains("Dedupe Group ID", output, StringComparison.Ordinal);
        Assert.Contains("Processing Status", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithCollectionMetadata_WritesCollectionMetadataValues()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { WithCollectionMetadata = true, Seed = 42 };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.Contains("GRP", output, StringComparison.Ordinal);
        Assert.Contains("2024-", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithEmailData_WritesEmlColumns()
    {
        var request = DefaultRequest();
        request.Output = request.Output with { FileType = "eml" };
        var template = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Test Subject",
            SentDate = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
        };
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "folder_001",
                    FileName = "00000001.eml",
                    FilePathInZip = "folder_001/00000001.eml",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
                Email = template,
            },
        };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.Contains("test@example.com", output, StringComparison.Ordinal);
        Assert.Contains("sender@example.com", output, StringComparison.Ordinal);
        Assert.Contains("Test Subject", output, StringComparison.Ordinal);
        Assert.Contains("2026-01-15", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithBates_WritesBatesNumber()
    {
        var request = DefaultRequest();
        request.Bates = new BatesNumberConfig
        {
            Prefix = "DOC",
            Start = 100,
            Digits = 6,
            Increment = 1,
        };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.Contains("DOC000100", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithTiff_WritesPageCount()
    {
        var request = DefaultRequest();
        request.Output = request.Output with { FileType = "tiff" };
        request.Tiff = request.Tiff with { PageRange = (1, 5) };
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "folder_001",
                    FileName = "00000001.tiff",
                    FilePathInZip = "folder_001/00000001.tiff",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
                PageCount = 3,
            },
        };

        var output = await WriteAndCaptureOutput(request, files);
        var dataLine = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last();
        Assert.EndsWith("þ3þ", dataLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithText_WritesTextFileReference()
    {
        var request = DefaultRequest();
        request.Output = request.Output with { WithText = true };
        request.Output = request.Output with { FileType = "pdf" };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.Contains(".txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_BufferedWrite_FlushesAtBatchSize()
    {
        var request = DefaultRequest();
        var files = new List<FileData>();
        for (long i = 1; i <= 1500; i++)
        {
            files.Add(MakeFileData(i));
        }

        var (_, lines) = await WriteAndCapture(request, files);
        Assert.Equal(1501, lines.Length);
    }

    [Fact]
    public async Task WriteAsync_EmptyFileList_WritesOnlyHeader()
    {
        var request = DefaultRequest();
        var (_, lines) = await WriteAndCapture(request, []);
        Assert.Single(lines);
    }

    [Fact]
    public async Task WriteAsync_WithEmlWithoutTemplate_WritesFallbackEmlValues()
    {
        var request = DefaultRequest();
        request.Output = request.Output with { FileType = "eml" };
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "folder_001",
                    FileName = "00000001.eml",
                    FilePathInZip = "folder_001/00000001.eml",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
                Email = null,
            },
        };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.Contains("recipient1@example.com", output, StringComparison.Ordinal);
        Assert.Contains("sender1@example.com", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_DefaultDelimiters_UseNonPrintingChars()
    {
        // DefaultRequest() already has QuoteDelimiter = "\u00fe" (the default).
        // We force them to explicit non-printing chars to verify they appear wrapped.
        var request = DefaultRequest();
        request.Delimiters = request.Delimiters with { ColumnDelimiter = "\u0014", QuoteDelimiter = "\u00fe" };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.Contains("þ", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_SanitizeField_ReplacesWindowsNewline()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { WithMetadata = true };
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "folder_001",
                    FileName = "00000001.pdf",
                    FilePathInZip = "line1\r\nline2",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
            },
        };

        var output = await WriteAndCaptureOutput(request, files);
        Assert.DoesNotContain("line1\r\nline2", output, StringComparison.Ordinal);
        Assert.Contains("line1®line2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_SanitizeField_ReplacesUnixNewline()
    {
        var request = DefaultRequest();
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "folder_001",
                    FileName = "00000001.pdf",
                    FilePathInZip = "line1\nline2",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
            },
        };

        var output = await WriteAndCaptureOutput(request, files);
        var dataLine = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last();
        Assert.Contains("line1®line2", dataLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWriter_DatFormat_ReturnsDatComposingWriter()
    {
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
        Assert.IsType<DatComposingWriter>(writer);
        Assert.IsAssignableFrom<ILoadFileWriter>(writer);
        Assert.Equal("DAT", writer.FormatName);
        Assert.Equal(".dat", writer.FileExtension);
    }

    [Fact]
    public async Task WriteAsync_StandardMode_EmptyQuoteDelimiter_OmitsQuoteCharactersInHeader()
    {
        var request = DefaultRequest();
        request.Delimiters = request.Delimiters with { QuoteDelimiter = string.Empty };
        var files = new List<FileData> { MakeFileData(1) };

        var (_, lines) = await WriteAndCapture(request, files);
        var header = lines[0];

        // With no quote delimiter, fields must NOT be wrapped in any quote character
        Assert.DoesNotContain("þ", header, StringComparison.Ordinal);
        Assert.Contains("Control Number", header, StringComparison.Ordinal);
        Assert.Contains("File Path", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_StandardMode_EmptyQuoteDelimiter_OmitsQuoteCharactersInDataRows()
    {
        var request = DefaultRequest();
        request.Delimiters = request.Delimiters with { QuoteDelimiter = string.Empty };
        var files = new List<FileData> { MakeFileData(1) };

        var (_, lines) = await WriteAndCapture(request, files);
        var dataRow = lines[1];

        // DOC00000001 must appear unquoted — no þ wrapper
        Assert.DoesNotContain("þ", dataRow, StringComparison.Ordinal);
        Assert.Contains("DOC00000001", dataRow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_StandardMode_EmptyQuoteDelimiter_WithMetadata_OmitsQuotes()
    {
        var request = DefaultRequest();
        request.Delimiters = request.Delimiters with { QuoteDelimiter = string.Empty };
        request.Metadata = request.Metadata with { WithMetadata = true };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);

        // Metadata columns (Custodian, Date Sent, Author, File Size) must appear unquoted
        Assert.DoesNotContain("þ", output, StringComparison.Ordinal);
        Assert.Contains("Custodian", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_StandardMode_EmptyQuoteDelimiter_WithText_OmitsQuotes()
    {
        var request = DefaultRequest();
        request.Delimiters = request.Delimiters with { QuoteDelimiter = string.Empty };
        request.Output = request.Output with { WithText = true, FileType = "pdf" };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);

        // Extracted Text column must appear unquoted
        Assert.DoesNotContain("þ", output, StringComparison.Ordinal);
        Assert.Contains(".txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_StandardMode_EmptyQuoteDelimiter_WithBates_OmitsQuotes()
    {
        var request = DefaultRequest();
        request.Delimiters = request.Delimiters with { QuoteDelimiter = string.Empty };
        request.Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 6, Increment = 1 };
        var files = new List<FileData> { MakeFileData(1) };

        var output = await WriteAndCaptureOutput(request, files);

        // Bates Number column must appear unquoted
        Assert.DoesNotContain("þ", output, StringComparison.Ordinal);
        Assert.Contains("TEST", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_LeavesUnderlyingStreamOpen()
    {
        var request = DefaultRequest();
        using var stream = new MemoryStream();
        var writer = new DatComposingWriter();
        await writer.WriteAsync(stream, request, []);
        Assert.True(stream.CanWrite);
        stream.WriteByte(0x00); // would throw if disposed
    }

    private static async Task<(string output, string[] lines)> WriteAndCapture(FileGenerationRequest request, List<FileData> files)
    {
        using var stream = new MemoryStream();
        var writer = new DatComposingWriter();
        await writer.WriteAsync(stream, request, files).ConfigureAwait(false);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        return (output, lines);
    }

    private static async Task<string> WriteAndCaptureOutput(FileGenerationRequest request, List<FileData> files)
    {
        var (output, _) = await WriteAndCapture(request, files).ConfigureAwait(false);
        return output;
    }

    private static FileGenerationRequest DefaultRequest()
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "/tmp/test",
                FileCount = 10,
                FileType = "pdf",
                Folders = 1,
            },
        };
    }

    private static FileData MakeFileData(long index)
    {
        return new FileData
        {
            WorkItem = new FileWorkItem
            {
                Index = index,
                FolderNumber = 1,
                FolderName = "folder_001",
                FileName = $"{index:D8}.pdf",
                FilePathInZip = $"folder_001/{index:D8}.pdf",
            },
            Data = Encoding.UTF8.GetBytes("content"),
            DataLength = 7,
        };
    }

    // ---- Chaos engine tests ----
    [Fact]
    public async Task WriteAsync_StandardMode_WithChaosQuotes_OutputDiffersFromBaseline()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

        // Baseline (no chaos)
        using var baseStream = new MemoryStream();
        var baseWriter = new DatComposingWriter();
        await baseWriter.WriteAsync(baseStream, request, files);
        var baseOutput = Encoding.UTF8.GetString(baseStream.ToArray());

        // With chaos — target every line with quotes type
        var chaosEngine = new ChaosEngine(
            totalLines: files.Count + 1,
            chaosAmount: "100%",
            chaosTypes: "quotes",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        var chaosWriter = new DatComposingWriter();
        await chaosWriter.WriteAsync(chaosStream, request, files, chaosEngine);
        var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

        Assert.NotEqual(baseOutput, chaosOutput);
        Assert.True(chaosEngine.Anomalies.Count > 0);
        Assert.All(chaosEngine.Anomalies, a => Assert.Equal("quotes", a.ErrorType));

        var anomaly = chaosEngine.Anomalies.First();
        Assert.Contains("Omitted the closing", anomaly.Description, StringComparison.Ordinal);
        Assert.NotEqual("N/A", anomaly.Column);

        var baseQuoteCount = baseOutput.Count(c => c == '\u00fe');
        var chaosQuoteCount = chaosOutput.Count(c => c == '\u00fe');
        Assert.True(chaosQuoteCount < baseQuoteCount, "Chaos output should have fewer quote characters");
    }

    [Fact]
    public async Task WriteAsync_StandardMode_WithChaosMixedDelimiters_OutputDiffersFromBaseline()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

        using var baseStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(baseStream, request, files);
        var baseOutput = Encoding.UTF8.GetString(baseStream.ToArray());

        var chaosEngine = new ChaosEngine(
            totalLines: files.Count + 1,
            chaosAmount: "100%",
            chaosTypes: "mixed-delimiters",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(chaosStream, request, files, chaosEngine);
        var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

        Assert.NotEqual(baseOutput, chaosOutput);
        Assert.True(chaosEngine.Anomalies.Count > 0);
        Assert.All(chaosEngine.Anomalies, a => Assert.Equal("mixed-delimiters", a.ErrorType));
        Assert.True(chaosOutput.Contains(",", StringComparison.Ordinal) || chaosOutput.Contains("\t", StringComparison.Ordinal) || chaosOutput.Contains("|", StringComparison.Ordinal), "Should contain alternative delimiters");

        var anomaly = chaosEngine.Anomalies.First();
        Assert.Contains("Replaced delimiter", anomaly.Description, StringComparison.Ordinal);
        Assert.NotEqual("N/A", anomaly.Column);

        var baseDelimCount = baseOutput.Count(c => c == '\u0014');
        var chaosDelimCount = chaosOutput.Count(c => c == '\u0014');
        Assert.True(chaosDelimCount < baseDelimCount, "Chaos output should have fewer standard delimiters");
    }

    [Fact]
    public async Task WriteAsync_StandardMode_WithChaosColumns_OutputDiffersFromBaseline()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

        using var baseStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(baseStream, request, files);
        var baseOutput = Encoding.UTF8.GetString(baseStream.ToArray());

        var chaosEngine = new ChaosEngine(
            totalLines: files.Count + 1,
            chaosAmount: "100%",
            chaosTypes: "columns",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(chaosStream, request, files, chaosEngine);
        var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

        Assert.NotEqual(baseOutput, chaosOutput);
        Assert.True(chaosEngine.Anomalies.Count > 0);
        Assert.All(chaosEngine.Anomalies, a => Assert.Equal("columns", a.ErrorType));
        var baseLines = baseOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chaosLines = chaosOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        bool columnCountDiffers = false;
        for (int i = 0; i < Math.Min(baseLines.Length, chaosLines.Length); i++)
        {
            var baseDelimCount = System.Linq.Enumerable.Count(baseLines[i], c => c == '\u0014');
            var chaosDelimCount = System.Linq.Enumerable.Count(chaosLines[i], c => c == '\u0014');
            if (baseDelimCount != chaosDelimCount)
            {
                columnCountDiffers = true;
                break;
            }
        }

        Assert.True(columnCountDiffers, "At least one line must have a different column count");
    }

    [Fact]
    public async Task WriteAsync_StandardMode_WithChaosEol_OutputDiffersFromBaseline()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

        using var baseStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(baseStream, request, files);
        var baseOutput = Encoding.UTF8.GetString(baseStream.ToArray());

        var chaosEngine = new ChaosEngine(
            totalLines: files.Count + 1,
            chaosAmount: "100%",
            chaosTypes: "eol",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(chaosStream, request, files, chaosEngine);
        var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

        Assert.NotEqual(baseOutput, chaosOutput);
        Assert.True(chaosEngine.Anomalies.Count > 0);
        Assert.All(chaosEngine.Anomalies, a => Assert.Equal("eol", a.ErrorType));
        var baseLineCount = baseOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
        var chaosLineCount = chaosOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
        Assert.True(chaosLineCount > baseLineCount, "Injected EOL should increase line count");
    }

    [Fact]
    public async Task WriteAsync_StandardMode_WithChaosEncoding_InjectsInvalidBytes()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

        using var baseStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(baseStream, request, files);
        var baseBytes = baseStream.ToArray();

        var chaosEngine = new ChaosEngine(
            totalLines: files.Count + 1,
            chaosAmount: "100%",
            chaosTypes: "encoding",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        await new DatComposingWriter().WriteAsync(chaosStream, request, files, chaosEngine);
        var chaosBytes = chaosStream.ToArray();

        // Encoding anomaly injects extra bytes between lines — output must be longer
        Assert.True(chaosBytes.Length > baseBytes.Length, "Encoding chaos should inject extra bytes");
        Assert.True(chaosEngine.Anomalies.Count > 0);
        Assert.All(chaosEngine.Anomalies, a => Assert.Equal("encoding", a.ErrorType));
    }

    [Fact]
    public async Task WriteAsync_StandardMode_WithChaos_Determinism_SameSeedProducesSameOutput()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        var files = Enumerable.Range(1, 20).Select(i => MakeFileData(i)).ToList();

        static ChaosEngine MakeEngine(int count) => new ChaosEngine(
            totalLines: count + 1,
            chaosAmount: "50%",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var stream1 = new MemoryStream();
        await new DatComposingWriter().WriteAsync(stream1, request, files, MakeEngine(files.Count));

        using var stream2 = new MemoryStream();
        await new DatComposingWriter().WriteAsync(stream2, request, files, MakeEngine(files.Count));

        Assert.Equal(stream1.ToArray(), stream2.ToArray());
    }

    [Fact]
    public async Task WriteAsync_ProductionSet_WithChaosQuotes_OutputDiffersFromBaseline()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        request.Bates = new BatesNumberConfig
        {
            Prefix = "TEST",
            Start = 1,
            Digits = 6,
            Increment = 1,
        };
        var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

        using var baseStream = new MemoryStream();
        var baseWriter = new DatComposingWriter(Zipper.LoadFiles.WriterMode.ProductionSet);
        await baseWriter.WriteAsync(baseStream, request, files);
        var baseOutput = Encoding.UTF8.GetString(baseStream.ToArray());

        var chaosEngine = new ChaosEngine(
            totalLines: files.Count + 1,
            chaosAmount: "100%",
            chaosTypes: "quotes",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        var chaosWriter = new DatComposingWriter(Zipper.LoadFiles.WriterMode.ProductionSet);
        await chaosWriter.WriteAsync(chaosStream, request, files, chaosEngine);
        var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

        Assert.NotEqual(baseOutput, chaosOutput);
        Assert.True(chaosEngine.Anomalies.Count > 0);
        Assert.All(chaosEngine.Anomalies, a => Assert.Equal("quotes", a.ErrorType));

        var anomaly = chaosEngine.Anomalies.First();
        Assert.Contains("Omitted the closing", anomaly.Description, StringComparison.Ordinal);
        Assert.NotEqual("N/A", anomaly.Column);

        var baseQuoteCount = baseOutput.Count(c => c == '\u00fe');
        var chaosQuoteCount = chaosOutput.Count(c => c == '\u00fe');
        Assert.True(chaosQuoteCount < baseQuoteCount, "Chaos output should have fewer quote characters");
    }

    [Fact]
    public async Task WriteAsync_StandardMode_NullChaosEngine_SameOutputAsBaseline()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

        // Passing null explicitly should produce identical output to no-arg call
        using var stream1 = new MemoryStream();
        await new DatComposingWriter().WriteAsync(stream1, request, files, null);

        using var stream2 = new MemoryStream();
        await new DatComposingWriter().WriteAsync(stream2, request, files);

        Assert.Equal(Encoding.UTF8.GetString(stream1.ToArray()), Encoding.UTF8.GetString(stream2.ToArray()));
    }

    /// <summary>
    /// Tests that a chaos encoding anomaly successfully injects invalid bytes on both the header boundary
    /// and the final data record boundary in loadfile-only mode.
    /// </summary>
    [Fact]
    public async Task WriteAsync_LoadFileOnlyMode_WithChaosEncoding_TargetsHeaderAndLastLine_InjectsInvalidBytesAndCreatesAudit()
    {
        var request = DefaultRequest();
        request.Metadata = request.Metadata with { Seed = 42 };
        request.Output = request.Output with { FileCount = 3 }; // 1 header + 3 data lines = 4 lines

        // Generate baseline
        using var baseStream = new MemoryStream();
        await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(baseStream, request, []);
        var baseBytes = baseStream.ToArray();

        // Total lines = FileCount + 1 (header + 3 data = 4 lines)
        var chaosEngine = new ChaosEngine(
            totalLines: 4,
            chaosAmount: "100%", // target all lines, so header (line 1) and last line (line 4) are targeted
            chaosTypes: "encoding",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(chaosStream, request, [], chaosEngine);
        var chaosBytes = chaosStream.ToArray();

        // The output should have anomalies injected for line 1 and line 4 (among others).
        // This means there should be anomalies in the Audit log specifically for Boundary 1-2 and Boundary 4-5.
        var headerAnomaly = chaosEngine.Anomalies.FirstOrDefault(a => string.Equals(a.LineNumber, "Boundary 1-2", StringComparison.Ordinal));
        var lastLineAnomaly = chaosEngine.Anomalies.FirstOrDefault(a => string.Equals(a.LineNumber, "Boundary 4-5", StringComparison.Ordinal));

        Assert.NotNull(headerAnomaly);
        Assert.NotNull(lastLineAnomaly);
        Assert.True(chaosBytes.Length > baseBytes.Length, "Encoding chaos should inject extra bytes");
    }
    [Fact]
    public async Task WriteAsync_BasicScenario_WritesValidDatFormat()
    {
        var request = this.CreateTestRequest();
        var fileData = this.CreateTestFileData();
        var writer = new DatComposingWriter();
        var outputPath = Path.Combine(this.TempDir, "test.dat");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.Contains("Control Number", lines[0], StringComparison.Ordinal);
        Assert.Contains("DOC00000001", lines[1], StringComparison.Ordinal);
        Assert.Contains("DOC00000002", lines[2], StringComparison.Ordinal);
        Assert.Contains("DOC00000003", lines[3], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UTF-16")]
    [InlineData("ANSI")]
    public async Task WriteAsync_WithDifferentEncodings_WritesCorrectly(string encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var request = this.CreateTestRequest();
        request.LoadFile = request.LoadFile with { Encoding = encoding };
        var fileData = this.CreateTestFileData();
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
        var outputPath = Path.Combine(this.TempDir, "test.dat");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var targetEncoding = encoding.ToUpperInvariant() switch
        {
            "UTF-16" => Encoding.Unicode,
            "ANSI" => Encoding.GetEncoding("Windows-1252"),
            _ => Encoding.UTF8,
        };

        var content = await File.ReadAllTextAsync(outputPath, targetEncoding);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(lines.Length >= 2);
        Assert.Contains("Control Number", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatWriter_StandardRow_FieldWithQuoteDelimiter_IsDoubled()
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
                    FilePathInZip = "folderþX/file.pdf",
                },
                Data = Array.Empty<byte>(),
            },
        };
        var writer = new DatComposingWriter();
        var outputPath = Path.Combine(this.TempDir, "test_escape.dat");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var output = await File.ReadAllTextAsync(outputPath);

        Assert.Contains("folderþþX/file.pdf", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatWriter_ProductionSetRow_FieldWithQuoteDelimiter_IsDoubled()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 1,
                FileType = "pdf",
                OutputPath = this.TempDir,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Production = new ProductionConfig { VolumeSize = 5000 },
            Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
        };

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "VOL001",
                    FileName = "TEST00000001.pdf",
                    FilePathInZip = "NATIVES/VOL001/fileþX.pdf",
                },
                DataLength = 1024,
            },
        };

        var writer = new DatComposingWriter(WriterMode.ProductionSet);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        var output = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("fileþþX.pdf", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatWriter_WithFamilies_StandardMode_IncludesFamilyColumnsAndRows()
    {
        var request = this.CreateTestRequest("eml");
        request.Metadata = request.Metadata with { WithFamilies = true };

        var fileData = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FilePathInZip = "folder_001/00000001.eml"
                },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                DataLength = 100
            }
        };

        var writer = new DatComposingWriter();
        var outputPath = Path.Combine(this.TempDir, "test_families_standard.dat");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

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

    [Fact]
    public async Task DatWriter_WithFamilies_ProductionSetMode_IncludesFamilyColumnsAndRows()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 1,
                FileType = "eml",
                OutputPath = this.TempDir,
            },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Production = new ProductionConfig { VolumeSize = 5000 },
            Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" }
        };

        var fileData = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "VOL001",
                    FileName = "TEST00000001.eml",
                    FilePathInZip = "NATIVES\\VOL001\\TEST00000001.eml"
                },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                DataLength = 100
            }
        };

        var writer = new DatComposingWriter(WriterMode.ProductionSet);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, fileData);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Contains("BEGATTACH", lines[0], StringComparison.Ordinal);
        Assert.Contains("ENDATTACH", lines[0], StringComparison.Ordinal);
        Assert.Contains("PARENTDOCID", lines[0], StringComparison.Ordinal);

        Assert.Contains("TEST00000001", lines[1], StringComparison.Ordinal);
        Assert.Contains("TEST00000001_A001", lines[1], StringComparison.Ordinal);

        Assert.Contains("TEST00000001_A001", lines[2], StringComparison.Ordinal);
        Assert.Contains("TEST00000001", lines[2], StringComparison.Ordinal);
    }
}
