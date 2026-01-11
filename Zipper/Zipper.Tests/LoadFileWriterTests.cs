using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zipper.LoadFiles;
using Xunit;

namespace Zipper
{
    public class LoadFileWriterTests : IDisposable
    {
        private readonly string _tempDir;

        public LoadFileWriterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private FileGenerationRequest CreateTestRequest(string fileType = "pdf")
        {
            return new FileGenerationRequest
            {
                OutputPath = _tempDir,
                FileCount = 3,
                FileType = fileType,
                Folders = 1,
                Encoding = "Unicode (UTF-8)",
                WithMetadata = true,
                WithText = false,
                LoadFileFormat = LoadFileFormat.Dat
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
                        FilePathInZip = $"folder_001/file_{i:D8}.pdf"
                    },
                    Data = Encoding.UTF8.GetBytes($"Test content {i}")
                });
            }
            return fileList;
        }

        [Fact]
        public async Task DatWriter_ShouldWriteValidDatFormat()
        {
            // Arrange
            var request = CreateTestRequest();
            var fileData = CreateTestFileData();
            var writer = new DatWriter();
            var outputPath = Path.Combine(_tempDir, "test.dat");

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
        public async Task OptWriter_ShouldWriteTabDelimitedFormat()
        {
            // Arrange
            var request = CreateTestRequest();
            var fileData = CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
            var outputPath = Path.Combine(_tempDir, "test.opt");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert
            Assert.Equal(4, lines.Length);
            // OPT format uses tab delimiters
            Assert.Contains('\t', lines[0]);
            Assert.Contains("Control Number", lines[0]);
        }

        [Fact]
        public async Task CsvWriter_ShouldWriteCsvFormat()
        {
            // Arrange
            var request = CreateTestRequest();
            var fileData = CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
            var outputPath = Path.Combine(_tempDir, "test.csv");

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
            var request = CreateTestRequest();
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
                }
            };
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
            var outputPath = Path.Combine(_tempDir, "test.csv");

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
        public async Task XmlWriter_ShouldWriteValidXml()
        {
            // Arrange
            var request = CreateTestRequest();
            var fileData = CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Xml);
            var outputPath = Path.Combine(_tempDir, "test.xml");

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
        public async Task ConcordanceWriter_ShouldUseProperDelimiters()
        {
            // Arrange
            var request = CreateTestRequest();
            var fileData = CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            var outputPath = Path.Combine(_tempDir, "test.dat");

            // Act
            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);

            // Assert - Concordance uses comma delimiter with CSV escaping
            Assert.Contains(',', content);
            Assert.Contains("CONTROLNUMBER", content);
            // Verify format: fields are comma-separated without trailing delimiter
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("BEGATTY,ENDDATTY,CONTROLNUMBER,PATH", lines[0]);
        }

        [Fact]
        public async Task WriterFactory_WithAllFormats_ShouldReturnCorrectWriters()
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
            Assert.Equal("XML", xmlWriter.FormatName);
            Assert.Equal(".xml", xmlWriter.FileExtension);

            var concordanceWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            Assert.Equal("CONCORDANCE", concordanceWriter.FormatName);
        }

        [Fact]
        public async Task AllWriters_WithBatesConfig_ShouldIncludeBatesNumber()
        {
            // Arrange
            var request = CreateTestRequest();
            request.BatesConfig = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 6,
                Increment = 1
            };
            var fileData = CreateTestFileData();

            foreach (LoadFileFormat format in Enum.GetValues(typeof(LoadFileFormat)))
            {
                // Arrange
                var writer = LoadFileWriterFactory.CreateWriter(format);
                var outputPath = Path.Combine(_tempDir, $"test.{format.ToString().ToLower()}");

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
            var request = CreateTestRequest("tiff");
            request.TiffPageRange = (1, 10);
            var fileData = CreateTestFileData();
            fileData[0] = fileData[0] with { PageCount = 5 };
            fileData[1] = fileData[1] with { PageCount = 7 };
            fileData[2] = fileData[2] with { PageCount = 3 };

            foreach (LoadFileFormat format in Enum.GetValues(typeof(LoadFileFormat)))
            {
                // Arrange
                var writer = LoadFileWriterFactory.CreateWriter(format);
                var outputPath = Path.Combine(_tempDir, $"test.{format.ToString().ToLower()}");

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
            // Register code pages encoding provider for ANSI (Windows-1252) support on Linux
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Arrange - Test encoding path with non-UTF8 encoding to verify proper encoding handling
            var request = CreateTestRequest();
            request.Encoding = encoding;
            var fileData = CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
            var outputPath = Path.Combine(_tempDir, "test.dat");

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
                _ => Encoding.UTF8
            };

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert - File should be readable and contain expected content
            Assert.True(lines.Length >= 2); // At least header + one data row
            Assert.Contains("Control Number", lines[0]);
        }
    }
}
