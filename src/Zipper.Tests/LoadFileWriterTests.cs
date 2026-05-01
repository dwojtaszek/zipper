using System.Text;
using Xunit;
using Zipper.LoadFiles;

namespace Zipper
{
    public class LoadFileWriterTests : IDisposable
    {
        private readonly string tempDir;

        public LoadFileWriterTests()
        {
            this.tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(this.tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, true);
            }
        }

        private FileGenerationRequest CreateTestRequest(string fileType = "pdf")
        {
            return new FileGenerationRequest
            {
                OutputPath = this.tempDir,
                FileCount = 3,
                FileType = fileType,
                Folders = 1,
                Encoding = "Unicode (UTF-8)",
                WithMetadata = true,
                WithText = false,
                LoadFileFormat = LoadFileFormat.Dat,
            };
        }

        private List<FileData> CreateTestFileData(int count = 3)
        {
            var fileList = new List<FileData>();
            for (int i = 1; i <= count; i++)
            {
                fileList.Add(new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = i,
                        FolderNumber = 1,
                        FilePathInZip = $"folder_001/file_{i:D8}.pdf",
                    },
                    Data = Encoding.UTF8.GetBytes($"Test content {i}"),
                });
            }

            return fileList;
        }

        [Fact]
        public async Task DatWriter_ShouldWriteValidDatFormat()
        {
            // Arrange
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = new DatWriter();
            var outputPath = Path.Combine(this.tempDir, "test.dat");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert
            Assert.Equal(4, lines.Length); // Header + 3 data lines
            Assert.Contains("Control Number", lines[0]);
            Assert.Contains("DOC00000001", lines[1]);
            Assert.Contains("DOC00000002", lines[2]);
            Assert.Contains("DOC00000003", lines[3]);
        }

        [Fact]
        public async Task OptWriter_ShouldWriteCommaDelimitedNoHeaderFormat()
        {
            // Arrange — Opticon standard: comma-separated, no header, 7-column
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
            var outputPath = Path.Combine(this.tempDir, "test.opt");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert — 3 data lines, no header
            Assert.Equal(3, lines.Length);

            // OPT format uses comma delimiters (Opticon standard)
            Assert.Contains(',', lines[0]);
            Assert.DoesNotContain('\t', lines[0]);

            // No header row — first line is data
            Assert.DoesNotContain("Control Number", lines[0]);

            // Each line should have exactly 6 commas (7 columns)
            Assert.Equal(6, lines[0].Count(c => c == ','));
        }

        [Theory]
        [InlineData("UTF-16")]
        [InlineData("ANSI")]
        public async Task OptWriter_ShouldRespectRequestEncoding(string encoding)
        {
            ArgumentNullException.ThrowIfNull(encoding);

            // Register code pages encoding provider for ANSI (Windows-1252) support on Linux
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Arrange - Test that OPT writer respects the requested encoding
            var request = this.CreateTestRequest();
            request.Encoding = encoding;
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
            var outputPath = Path.Combine(this.tempDir, "test.opt");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            // Read with the specified encoding to verify it was written correctly
            var targetEncoding = encoding.ToUpperInvariant() switch
            {
                "UTF-16" => Encoding.Unicode,
                "ANSI" => Encoding.GetEncoding("Windows-1252"),
                _ => Encoding.UTF8,
            };

            var content = await File.ReadAllTextAsync(outputPath, targetEncoding);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert — OPT format: 3 data lines, comma-separated, 7 columns each
            Assert.Equal(3, lines.Length);
            Assert.Contains(',', lines[0]);
            Assert.Equal(6, lines[0].Count(c => c == ','));
        }

        [Fact]
        public async Task CsvWriter_ShouldWriteCsvFormat()
        {
            // Arrange
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
            var outputPath = Path.Combine(this.tempDir, "test.csv");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert
            Assert.Equal(4, lines.Length);
            Assert.Contains("Control Number", lines[0]);
            Assert.Contains("DOC00000001", lines[1]);
        }

        [Fact]
        public async Task CsvWriter_WithSpecialCharacters_ShouldEscapeFields()
        {
            // Arrange
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
            var outputPath = Path.Combine(this.tempDir, "test.csv");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);

            // Assert - CSV should escape quotes by doubling them
            // RFC 4180: If field contains quotes, wrap field in quotes and double each quote
            Assert.Contains("\"\"", content); // At least one escaped quote should exist
        }

        [Fact]
        public async Task XmlLoadFileWriter_ShouldWriteValidXml()
        {
            // Arrange
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Xml);
            var outputPath = Path.Combine(this.tempDir, "test.xml");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);

            // Assert - Note: XDocument includes XML declaration, but we write directly to stream
            Assert.Contains("<documents>", content);
            Assert.Contains("<document>", content);
            Assert.Contains("<controlNumber>DOC00000001</controlNumber>", content);
        }

        [Fact]
        public async Task ConcordanceWriter_ShouldUseDatEscapingForQuoteDelimiter()
        {
            // Arrange — Concordance DAT uses ASCII 254 (þ) as quote delimiter
            var request = this.CreateTestRequest();
            var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FilePathInZip = "folder_001/file_with_\u00fe_char.pdf"
                    },
                    Data = Array.Empty<byte>()
                },
            };
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            var outputPath = Path.Combine(this.tempDir, "test.dat");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);

            // Assert — Fields containing þ should have it doubled (þ → þþ) per DAT escaping.
            // Verify in the actual PATH column, not just empty BEGATTY/ENDDATTY.
            var dataLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1]; // skip header
            var fields = dataLine.Split('\u0014'); // ASCII 20 column delimiter

            // PATH is the 4th column (index 3): BEGATTY, ENDDATTY, CONTROLNUMBER, PATH, ...
            Assert.Contains("\u00fe\u00fe", fields[3]);
        }

        [Fact]
        public async Task ConcordanceWriter_ShouldUseProperDelimiters()
        {
            // Arrange
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            var outputPath = Path.Combine(this.tempDir, "test.dat");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);

            // Assert - Concordance uses ASCII 20 column delimiter with þ quote wrapping
            var colDelim = '\u0014'; // ASCII 20
            Assert.Contains(colDelim, content);
            Assert.Contains("CONTROLNUMBER", content);

            // Verify format: fields are ASCII-20-separated with þ quote delimiter wrapping header names
            var lines_split = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("BEGATTY", lines_split[0]);
            Assert.Contains("CONTROLNUMBER", lines_split[0]);
            Assert.Contains("PATH", lines_split[0]);
        }

        [Fact]
        public void WriterFactory_WithAllFormats_ShouldReturnCorrectWriters()
        {
            // Act & Assert
            var datWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
            Assert.IsType<DatWriter>(datWriter);
            Assert.Equal("DAT", datWriter.FormatName);
            Assert.Equal(".dat", datWriter.FileExtension);

            var optWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
            Assert.Equal("OPT", optWriter.FormatName);
            Assert.Equal(".opt", optWriter.FileExtension);

            var csvWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
            Assert.Equal("CSV", csvWriter.FormatName);
            Assert.Equal(".csv", csvWriter.FileExtension);

            var xmlWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Xml);
            Assert.IsType<XmlLoadFileWriter>(xmlWriter);
            Assert.Equal("XML", xmlWriter.FormatName);
            Assert.Equal(".xml", xmlWriter.FileExtension);

            var concordanceWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            Assert.Equal("CONCORDANCE", concordanceWriter.FormatName);
        }

        [Fact]
        public async Task AllWriters_WithBatesConfig_ShouldIncludeBatesNumber()
        {
            // Arrange
            var request = this.CreateTestRequest();
            request.BatesConfig = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 6,
                Increment = 1,
            };
            var fileData = this.CreateTestFileData();

            foreach (LoadFileFormat format in Enum.GetValues(typeof(LoadFileFormat)))
            {
                // Arrange
                var writer = LoadFileWriterFactory.CreateWriter(format);
                var outputPath = Path.Combine(this.tempDir, $"test.{format.ToString().ToLower()}");

                // Act
                await using (var stream = File.OpenWrite(outputPath))
                {
                    await writer.WriteAsync(stream, request, fileData);
                }

                var content = await File.ReadAllTextAsync(outputPath);

                // Assert - All formats should include Bates number
                Assert.Contains("TEST", content);
                Assert.Contains("000001", content);
            }
        }

        [Fact]
        public async Task AllWriters_WithTiffPageRange_ShouldIncludePageCount()
        {
            // Arrange
            var request = this.CreateTestRequest("tiff");
            request.TiffPageRange = (1, 10);
            var fileData = this.CreateTestFileData();
            fileData[0] = fileData[0] with { PageCount = 5 };
            fileData[1] = fileData[1] with { PageCount = 7 };
            fileData[2] = fileData[2] with { PageCount = 3 };

            foreach (LoadFileFormat format in Enum.GetValues(typeof(LoadFileFormat)))
            {
                // Arrange
                var writer = LoadFileWriterFactory.CreateWriter(format);
                var outputPath = Path.Combine(this.tempDir, $"test.{format.ToString().ToLower()}");

                // Act
                await using (var stream = File.OpenWrite(outputPath))
                {
                    await writer.WriteAsync(stream, request, fileData);
                }

                var content = await File.ReadAllTextAsync(outputPath);

                // Assert - All formats should include page count
                Assert.Contains("5", content);
                Assert.Contains("7", content);
                Assert.Contains("3", content);
            }
        }

        [Theory]
        [InlineData("UTF-16")]
        [InlineData("ANSI")]
        public async Task DatWriter_WithDifferentEncodings_ShouldWriteCorrectly(string encoding)
        {
            ArgumentNullException.ThrowIfNull(encoding);

            // Register code pages encoding provider for ANSI (Windows-1252) support on Linux
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Arrange - Test encoding path with non-UTF8 encoding to verify proper encoding handling
            var request = this.CreateTestRequest();
            request.Encoding = encoding;
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
            var outputPath = Path.Combine(this.tempDir, "test.dat");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            // Read with the specified encoding to verify it was written correctly
            var targetEncoding = encoding.ToUpperInvariant() switch
            {
                "UTF-16" => Encoding.Unicode,
                "ANSI" => Encoding.GetEncoding("Windows-1252"),
                _ => Encoding.UTF8,
            };

            var content = await File.ReadAllTextAsync(outputPath, targetEncoding);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert - File should be readable and contain expected content
            Assert.True(lines.Length >= 2); // At least header + one data row
            Assert.Contains("Control Number", lines[0]);
        }

        [Fact]
        public async Task CsvWriter_WritesDocumentIdInDataRows()
        {
            var request = this.CreateTestRequest();
            var files = this.CreateTestFileData(2);
            var content = await this.CaptureCsvOutput(request, files);
            Assert.Contains("DOC00000001", content);
            Assert.Contains("DOC00000002", content);
        }

        [Fact]
        public async Task CsvWriter_WithText_WritesTextPath()
        {
            var request = this.CreateTestRequest();
            request.WithText = true;
            var files = this.CreateTestFileData(1);
            files[0] = files[0] with { WorkItem = files[0].WorkItem with { FilePathInZip = "folder_001/file_00000001.pdf" } };
            var content = await this.CaptureCsvOutput(request, files);
            Assert.Contains("folder_001/file_00000001.txt", content);
        }

        [Fact]
        public async Task CsvWriter_MetadataHeader_ReflectsRequestConfiguration()
        {
            var request = this.CreateTestRequest();
            request.WithMetadata = true;
            var files = this.CreateTestFileData(1);
            var contentWith = await this.CaptureCsvOutput(request, files);
            Assert.Contains("Custodian", contentWith);

            request.WithMetadata = false;
            var contentWithout = await this.CaptureCsvOutput(request, files);
            Assert.DoesNotContain("Custodian", contentWithout);

            var emlRequest = this.CreateTestRequest("eml");
            emlRequest.WithMetadata = false;
            var emlContent = await this.CaptureCsvOutput(emlRequest, files);
            Assert.Contains("Custodian", emlContent);
        }

        [Fact]
        public async Task CsvWriter_EmlColumns_OnlyForEmlFileType()
        {
            var files = this.CreateTestFileData(1);
            var emlRequest = this.CreateTestRequest("eml");
            var emlContent = await this.CaptureCsvOutput(emlRequest, files);
            Assert.Contains("To", emlContent);
            Assert.Contains("From", emlContent);

            var pdfRequest = this.CreateTestRequest("pdf");
            var pdfContent = await this.CaptureCsvOutput(pdfRequest, files);
            Assert.DoesNotContain("To", pdfContent);
        }

        [Fact]
        public async Task CsvWriter_PageCount_OnlyForTiffWithPageRange()
        {
            var request = this.CreateTestRequest("tiff");
            request.TiffPageRange = (1, 10);
            var files = this.CreateTestFileData(1);
            files[0] = files[0] with { PageCount = 5 };
            var contentWith = await this.CaptureCsvOutput(request, files);
            Assert.Contains("Page Count", contentWith);

            request.TiffPageRange = null;
            var contentWithout = await this.CaptureCsvOutput(request, files);
            Assert.DoesNotContain("Page Count", contentWithout);

            var pdfRequest = this.CreateTestRequest("pdf");
            pdfRequest.TiffPageRange = (1, 10);
            var pdfContent = await this.CaptureCsvOutput(pdfRequest, files);
            Assert.DoesNotContain("Page Count", pdfContent);
        }

        [Fact]
        public async Task CsvWriter_WithBatesConfig_WritesCorrectBatesNumber()
        {
            var request = this.CreateTestRequest();
            request.BatesConfig = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1000,
                Digits = 6,
                Increment = 1,
            };
            var files = this.CreateTestFileData(1);
            files[0] = files[0] with { WorkItem = files[0].WorkItem with { Index = 10 } };
            var content = await this.CaptureCsvOutput(request, files);
            Assert.Contains("TEST001009", content);
        }

        [Fact]
        public async Task CsvWriter_WithMetadata_ContainsCustodianAndFileSizeInDataRow()
        {
            var request = this.CreateTestRequest();
            request.WithMetadata = true;
            var files = this.CreateTestFileData(1);
            files[0] = files[0] with { DataLength = 1024, WorkItem = files[0].WorkItem with { FolderNumber = 5 } };
            var content = await this.CaptureCsvOutput(request, files);
            var dataLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1];
            Assert.Contains("Custodian 5", dataLine);
            Assert.Contains("1024", dataLine);
        }

        private async Task<string> CaptureCsvOutput(FileGenerationRequest request, List<FileData> files)
        {
            var writer = new LoadFiles.CsvWriter();
            using var stream = new MemoryStream();
            await writer.WriteAsync(stream, request, files);
            stream.Position = 0;
            return await new StreamReader(stream).ReadToEndAsync();
        }
    }
}
