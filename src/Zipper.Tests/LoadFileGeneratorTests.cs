using System.Text;
using Xunit;

namespace Zipper
{
    public class LoadFileGeneratorTests
    {
        [Fact]
        public void GetEncoding_ReturnsExpectedEncoding()
        {
            var encoding = LoadFileGenerator.GetEncoding("UTF-8");
            Assert.Equal(Encoding.UTF8, encoding);
        }

        [Fact]
        public void GetEncoding_WithNull_ReturnsDefaultEncoding()
        {
            var encoding = LoadFileGenerator.GetEncoding(null!);
            Assert.Equal(Encoding.UTF8, encoding);
        }

        [Fact]
        public void GetEncoding_WithUnknownName_ReturnsDefaultEncoding()
        {
            var encoding = LoadFileGenerator.GetEncoding("not-a-real-encoding");
            Assert.Equal(Encoding.UTF8, encoding);
        }

        [Fact]
        public async Task WriteLoadFileContent_BasicHeader_WritesCorrectColumns()
        {
            var (output, lines) = await WriteAndCapture(DefaultRequest(), []);
            var header = lines[0];

            Assert.Contains("Control Number", header);
            Assert.Contains("File Path", header);
            Assert.DoesNotContain("Custodian", header);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithMetadata_IncludesMetadataColumns()
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
        public async Task WriteLoadFileContent_WithEmailType_IncludesEmlColumns()
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
        public async Task WriteLoadFileContent_WithBatesConfig_IncludesBatesNumberColumn()
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
        public async Task WriteLoadFileContent_WithTiffPageRange_IncludesPageCountColumn()
        {
            var request = DefaultRequest();
            request.FileType = "tiff";
            request.TiffPageRange = (1, 10);
            var output = await WriteAndCaptureOutput(request, []);
            Assert.Contains("Page Count", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithText_IncludesExtractedTextColumn()
        {
            var request = DefaultRequest();
            request.WithText = true;
            var output = await WriteAndCaptureOutput(request, []);
            Assert.Contains("Extracted Text", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithFileData_WritesDataRows()
        {
            var request = DefaultRequest();
            var files = new List<FileData> { MakeFileData(1), MakeFileData(2) };
            var (output, lines) = await WriteAndCapture(request, files);
            Assert.Equal(3, lines.Length);
            Assert.Contains("DOC00000001", lines[1]);
            Assert.Contains("DOC00000002", lines[2]);
        }

        [Fact]
        public async Task WriteLoadFileContent_WritesInOrderOfFileIndex()
        {
            var request = DefaultRequest();
            var files = new List<FileData> { MakeFileData(3), MakeFileData(1), MakeFileData(2) };
            var (output, lines) = await WriteAndCapture(request, files);
            Assert.Contains("DOC00000001", lines[1]);
            Assert.Contains("DOC00000002", lines[2]);
            Assert.Contains("DOC00000003", lines[3]);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithMetadata_WritesMetadataValues()
        {
            var request = DefaultRequest();
            request.WithMetadata = true;
            var files = new List<FileData> { MakeFileData(1) };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains("Custodian 1", output);
            Assert.Contains("File Size", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithEmailData_WritesEmlColumns()
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
                }
            };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains("test@example.com", output);
            Assert.Contains("sender@example.com", output);
            Assert.Contains("Test Subject", output);
            Assert.Contains("2026-01-15", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithBates_WritesBatesNumber()
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

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains("DOC000100", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithTiff_WritesPageCount()
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
                }
            };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            var dataLine = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last();
            Assert.EndsWith("þ3þ", dataLine);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithText_WritesTextFileReference()
        {
            var request = DefaultRequest();
            request.WithText = true;
            request.FileType = "pdf";
            var files = new List<FileData> { MakeFileData(1) };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains(".txt", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_BufferedWrite_FlushesAtBatchSize()
        {
            var request = DefaultRequest();
            var files = new List<FileData>();
            for (long i = 1; i <= 1500; i++)
            {
                files.Add(MakeFileData(i));
            }

            var (output, lines) = await WriteAndCapture(request, files);
            Assert.Equal(1501, lines.Length);
        }

        [Fact]
        public async Task WriteLoadFileContent_EmptyFileList_WritesOnlyHeader()
        {
            var request = DefaultRequest();
            var (output, lines) = await WriteAndCapture(request, []);
            Assert.Single(lines);
        }

        [Fact]
        public async Task WriteLoadFileContent_WithEmlWithoutTemplate_WritesFallbackEmlValues()
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
                }
            };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains("recipient1@example.com", output);
            Assert.Contains("sender1@example.com", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_DefaultDelimiters_UseNonPrintingChars()
        {
            var request = DefaultRequest();
            request.ColumnDelimiter = null!;
            request.QuoteDelimiter = null!;
            var files = new List<FileData> { MakeFileData(1) };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains("þ", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_SanitizeField_ReplacesWindowsNewline()
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
                }
            };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            Assert.DoesNotContain("\r\n", output);
            Assert.Contains("line1®line2", output);
        }

        [Fact]
        public async Task WriteLoadFileContent_SanitizeField_ReplacesUnixNewline()
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
                }
            };

            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

            var output = Encoding.UTF8.GetString(stream.ToArray());
            var dataLine = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last();
            Assert.Contains("line1®line2", dataLine);
        }

        private static async Task<(string output, string[] lines)> WriteAndCapture(FileGenerationRequest request, List<FileData> files)
        {
            using var stream = new MemoryStream();
            using var streamWriter = new StreamWriter(stream);

            await LoadFileGenerator.WriteLoadFileContent(streamWriter, request, files);
            await streamWriter.FlushAsync();

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
