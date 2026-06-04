using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Emails;
using Zipper.LoadFiles;

namespace Zipper
{
    public class DatWriterTests
    {
        [Fact]
        public async Task WriteAsync_BasicHeader_WritesCorrectColumns()
        {
            var (_, lines) = await WriteAndCapture(DefaultRequest(), []);
            var header = lines[0];

            Assert.Contains("Control Number", header);
            Assert.Contains("File Path", header);
            Assert.DoesNotContain("Custodian", header);
        }

        [Fact]
        public async Task WriteAsync_WithMetadata_IncludesMetadataColumns()
        {
            var request = DefaultRequest();
            request.Metadata = request.Metadata with { WithMetadata = true };
            var output = await WriteAndCaptureOutput(request, []);
            Assert.Contains("Custodian", output);
            Assert.Contains("Date Sent", output);
            Assert.Contains("Author", output);
            Assert.Contains("File Size", output);
        }

        [Fact]
        public async Task WriteAsync_WithEmailType_IncludesEmlColumns()
        {
            var request = DefaultRequest();
            request.Output = request.Output with { FileType = "eml" };
            var output = await WriteAndCaptureOutput(request, []);
            Assert.Contains("To", output);
            Assert.Contains("From", output);
            Assert.Contains("Subject", output);
            Assert.Contains("Sent Date", output);
            Assert.Contains("Attachment", output);
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
            Assert.Contains("Bates Number", output);
        }

        [Fact]
        public async Task WriteAsync_WithTiffPageRange_IncludesPageCountColumn()
        {
            var request = DefaultRequest();
            request.Output = request.Output with { FileType = "tiff" };
            request.Tiff = request.Tiff with { PageRange = (1, 10) };
            var output = await WriteAndCaptureOutput(request, []);
            Assert.Contains("Page Count", output);
        }

        [Fact]
        public async Task WriteAsync_WithText_IncludesExtractedTextColumn()
        {
            var request = DefaultRequest();
            request.Output = request.Output with { WithText = true };
            var output = await WriteAndCaptureOutput(request, []);
            Assert.Contains("Extracted Text", output);
        }

        [Fact]
        public async Task WriteAsync_WithFileData_WritesDataRows()
        {
            var request = DefaultRequest();
            var files = new List<FileData> { MakeFileData(1), MakeFileData(2) };
            var (_, lines) = await WriteAndCapture(request, files);
            Assert.Equal(3, lines.Length);
            Assert.Contains("DOC00000001", lines[1]);
            Assert.Contains("DOC00000002", lines[2]);
        }

        [Fact]
        public async Task WriteAsync_WithMetadata_WritesMetadataValues()
        {
            var request = DefaultRequest();
            request.Metadata = request.Metadata with { WithMetadata = true };
            var files = new List<FileData> { MakeFileData(1) };

            var output = await WriteAndCaptureOutput(request, files);
            Assert.Contains("Custodian 1", output);
            Assert.Contains("File Size", output);
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
            Assert.Contains("test@example.com", output);
            Assert.Contains("sender@example.com", output);
            Assert.Contains("Test Subject", output);
            Assert.Contains("2026-01-15", output);
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
            Assert.Contains("DOC000100", output);
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
            Assert.EndsWith("þ3þ", dataLine);
        }

        [Fact]
        public async Task WriteAsync_WithText_WritesTextFileReference()
        {
            var request = DefaultRequest();
            request.Output = request.Output with { WithText = true };
            request.Output = request.Output with { FileType = "pdf" };
            var files = new List<FileData> { MakeFileData(1) };

            var output = await WriteAndCaptureOutput(request, files);
            Assert.Contains(".txt", output);
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
            Assert.Contains("recipient1@example.com", output);
            Assert.Contains("sender1@example.com", output);
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
            Assert.Contains("þ", output);
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
            Assert.DoesNotContain("line1\r\nline2", output);
            Assert.Contains("line1®line2", output);
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
            Assert.Contains("line1®line2", dataLine);
        }

        [Fact]
        public void Factory_ReturnsDatWriter_AsLoadFileWriterBase()
        {
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
            Assert.IsType<DatWriter>(writer);
            Assert.IsAssignableFrom<LoadFileWriterBase>(writer);
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
            Assert.DoesNotContain("þ", header);
            Assert.Contains("Control Number", header);
            Assert.Contains("File Path", header);
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
            Assert.DoesNotContain("þ", dataRow);
            Assert.Contains("DOC00000001", dataRow);
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
            Assert.DoesNotContain("þ", output);
            Assert.Contains("Custodian", output);
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
            Assert.DoesNotContain("þ", output);
            Assert.Contains(".txt", output);
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
            Assert.DoesNotContain("þ", output);
            Assert.Contains("TEST", output);
        }

        [Fact]
        public async Task WriteAsync_LeavesUnderlyingStreamOpen()
        {
            var request = DefaultRequest();
            using var stream = new MemoryStream();
            var writer = new DatWriter();
            await writer.WriteAsync(stream, request, []);
            Assert.True(stream.CanWrite);
            stream.WriteByte(0x00); // would throw if disposed
        }

        private static async Task<(string output, string[] lines)> WriteAndCapture(FileGenerationRequest request, List<FileData> files)
        {
            using var stream = new MemoryStream();
            var writer = new DatWriter();
            await writer.WriteAsync(stream, request, files);

            var output = Encoding.UTF8.GetString(stream.ToArray());
            var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            return (output, lines);
        }

        private static async Task<string> WriteAndCaptureOutput(FileGenerationRequest request, List<FileData> files)
        {
            var (output, _) = await WriteAndCapture(request, files);
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
            var baseWriter = new DatWriter();
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
            var chaosWriter = new DatWriter();
            await chaosWriter.WriteAsync(chaosStream, request, files, chaosEngine);
            var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

            Assert.NotEqual(baseOutput, chaosOutput);
            Assert.True(chaosEngine.Anomalies.Count > 0);
            Assert.All(chaosEngine.Anomalies, a => Assert.Equal("quotes", a.ErrorType));
        }

        [Fact]
        public async Task WriteAsync_StandardMode_WithChaosMixedDelimiters_OutputDiffersFromBaseline()
        {
            var request = DefaultRequest();
            request.Metadata = request.Metadata with { Seed = 42 };
            var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

            using var baseStream = new MemoryStream();
            await new DatWriter().WriteAsync(baseStream, request, files);
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
            await new DatWriter().WriteAsync(chaosStream, request, files, chaosEngine);
            var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

            Assert.NotEqual(baseOutput, chaosOutput);
            Assert.True(chaosEngine.Anomalies.Count > 0);
            Assert.All(chaosEngine.Anomalies, a => Assert.Equal("mixed-delimiters", a.ErrorType));
            Assert.True(chaosOutput.Contains(",") || chaosOutput.Contains("\t") || chaosOutput.Contains("|"), "Should contain alternative delimiters");
        }

        [Fact]
        public async Task WriteAsync_StandardMode_WithChaosColumns_OutputDiffersFromBaseline()
        {
            var request = DefaultRequest();
            request.Metadata = request.Metadata with { Seed = 42 };
            var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

            using var baseStream = new MemoryStream();
            await new DatWriter().WriteAsync(baseStream, request, files);
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
            await new DatWriter().WriteAsync(chaosStream, request, files, chaosEngine);
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
            await new DatWriter().WriteAsync(baseStream, request, files);
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
            await new DatWriter().WriteAsync(chaosStream, request, files, chaosEngine);
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
            await new DatWriter().WriteAsync(baseStream, request, files);
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
            await new DatWriter().WriteAsync(chaosStream, request, files, chaosEngine);
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
            await new DatWriter().WriteAsync(stream1, request, files, MakeEngine(files.Count));

            using var stream2 = new MemoryStream();
            await new DatWriter().WriteAsync(stream2, request, files, MakeEngine(files.Count));

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
            var baseWriter = new DatWriter(LoadFiles.WriterMode.ProductionSet);
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
            var chaosWriter = new DatWriter(LoadFiles.WriterMode.ProductionSet);
            await chaosWriter.WriteAsync(chaosStream, request, files, chaosEngine);
            var chaosOutput = Encoding.UTF8.GetString(chaosStream.ToArray());

            Assert.NotEqual(baseOutput, chaosOutput);
            Assert.True(chaosEngine.Anomalies.Count > 0);
            Assert.All(chaosEngine.Anomalies, a => Assert.Equal("quotes", a.ErrorType));
        }

        [Fact]
        public async Task WriteAsync_StandardMode_NullChaosEngine_SameOutputAsBaseline()
        {
            var request = DefaultRequest();
            request.Metadata = request.Metadata with { Seed = 42 };
            var files = Enumerable.Range(1, 10).Select(i => MakeFileData(i)).ToList();

            // Passing null explicitly should produce identical output to no-arg call
            using var stream1 = new MemoryStream();
            await new DatWriter().WriteAsync(stream1, request, files, null);

            using var stream2 = new MemoryStream();
            await new DatWriter().WriteAsync(stream2, request, files);

            Assert.Equal(Encoding.UTF8.GetString(stream1.ToArray()), Encoding.UTF8.GetString(stream2.ToArray()));
        }
    }
}
