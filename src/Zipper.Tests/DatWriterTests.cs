using System.Text;
using Xunit;
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
            request.WithMetadata = true;
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
            request.FileType = "eml";
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
            request.BatesConfig = new BatesNumberConfig
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
            request.FileType = "tiff";
            request.TiffPageRange = (1, 10);
            var output = await WriteAndCaptureOutput(request, []);
            Assert.Contains("Page Count", output);
        }

        [Fact]
        public async Task WriteAsync_WithText_IncludesExtractedTextColumn()
        {
            var request = DefaultRequest();
            request.WithText = true;
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
        public async Task WriteAsync_WritesInOrderOfFileIndex()
        {
            var request = DefaultRequest();
            var files = new List<FileData> { MakeFileData(3), MakeFileData(1), MakeFileData(2) };
            var (_, lines) = await WriteAndCapture(request, files);
            Assert.Contains("DOC00000001", lines[1]);
            Assert.Contains("DOC00000002", lines[2]);
            Assert.Contains("DOC00000003", lines[3]);
        }

        [Fact]
        public async Task WriteAsync_WithMetadata_WritesMetadataValues()
        {
            var request = DefaultRequest();
            request.WithMetadata = true;
            var files = new List<FileData> { MakeFileData(1) };

            var output = await WriteAndCaptureOutput(request, files);
            Assert.Contains("Custodian 1", output);
            Assert.Contains("File Size", output);
        }

        [Fact]
        public async Task WriteAsync_WithEmailData_WritesEmlColumns()
        {
            var request = DefaultRequest();
            request.FileType = "eml";
            var template = new EmailTemplate
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
                    EmailTemplate = template,
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
            request.BatesConfig = new BatesNumberConfig
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
            request.FileType = "tiff";
            request.TiffPageRange = (1, 5);
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
            request.WithText = true;
            request.FileType = "pdf";
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
            request.FileType = "eml";
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
                    EmailTemplate = null,
                },
            };

            var output = await WriteAndCaptureOutput(request, files);
            Assert.Contains("recipient1@example.com", output);
            Assert.Contains("sender1@example.com", output);
        }

        [Fact]
        public async Task WriteAsync_DefaultDelimiters_UseNonPrintingChars()
        {
            var request = DefaultRequest();
            request.ColumnDelimiter = null!;
            request.QuoteDelimiter = null!;
            var files = new List<FileData> { MakeFileData(1) };

            var output = await WriteAndCaptureOutput(request, files);
            Assert.Contains("þ", output);
        }

        [Fact]
        public async Task WriteAsync_SanitizeField_ReplacesWindowsNewline()
        {
            var request = DefaultRequest();
            request.WithMetadata = true;
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
                OutputPath = "/tmp/test",
                FileCount = 10,
                FileType = "pdf",
                Folders = 1,
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
    }
}
