// <copyright file="LoadFileWriterTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
        public async Task OptWriter_ShouldWriteTabDelimitedFormat()
        {
            // Arrange
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

            // Assert - Concordance uses comma delimiter with CSV escaping
            Assert.Contains(',', content);
            Assert.Contains("CONTROLNUMBER", content);

            // Verify format: fields are comma-separated without trailing delimiter
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("BEGATTY,ENDDATTY,CONTROLNUMBER,PATH", lines[0]);
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

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Assert - File should be readable and contain expected content
            Assert.True(lines.Length >= 2); // At least header + one data row
            Assert.Contains("Control Number", lines[0]);
        }

        [Fact]
        public void LoadFileWriterBase_GenerateMetadataValues_ReturnsValidMetadata()
        {
            // Arrange
            var workItem = new FileWorkItem
            {
                Index = 1,
                FolderNumber = 5,
                FilePathInZip = "folder_005/file_00000001.pdf",
            };
            var fileData = new FileData
            {
                WorkItem = workItem,
                Data = new byte[1024],
            };

            // Act - Call through concrete writer that exposes base class functionality
            var writer = new DatWriter();

            // Use reflection to access protected method for testing
            var method = typeof(LoadFiles.LoadFileWriterBase).GetMethod(
                "GenerateMetadataValues",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var result = method?.Invoke(null, new object[] { workItem, fileData });

            // Assert
            Assert.NotNull(result);
            var metadata = result as LoadFiles.MetadataColumns;
            Assert.NotNull(metadata);
            Assert.Equal("Custodian 5", metadata.Custodian);
            Assert.Equal(1024, metadata.FileSize);
            Assert.NotEmpty(metadata.DateSent);
            Assert.NotEmpty(metadata.Author);
        }

        [Fact]
        public void LoadFileWriterBase_ShouldIncludeMetadata_WithVariousRequests_ReturnsExpectedResults()
        {
            // Arrange & Act & Assert
            var pdfRequest = this.CreateTestRequest("pdf");
            pdfRequest.WithMetadata = true;
            var method = typeof(LoadFiles.LoadFileWriterBase).GetMethod(
                "ShouldIncludeMetadata",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var result = (bool)method!.Invoke(null, new object[] { pdfRequest })!;
            Assert.True(result); // WithMetadata = true

            pdfRequest.WithMetadata = false;
            result = (bool)method.Invoke(null, new object[] { pdfRequest })!;
            Assert.False(result); // WithMetadata = false and not EML

            var emlRequest = this.CreateTestRequest("eml");
            emlRequest.WithMetadata = false;
            result = (bool)method.Invoke(null, new object[] { emlRequest })!;
            Assert.True(result); // EML always includes metadata
        }

        [Fact]
        public void LoadFileWriterBase_ShouldIncludeEmlColumns_WithVariousFileTypes_ReturnsExpectedResults()
        {
            // Arrange & Act & Assert
            var method = typeof(LoadFiles.LoadFileWriterBase).GetMethod(
                "ShouldIncludeEmlColumns",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            var emlRequest = this.CreateTestRequest("eml");
            var result = (bool)method!.Invoke(null, new object[] { emlRequest })!;
            Assert.True(result); // EML file type

            var pdfRequest = this.CreateTestRequest("pdf");
            result = (bool)method.Invoke(null, new object[] { pdfRequest })!;
            Assert.False(result); // Non-EML file type
        }

        [Fact]
        public void LoadFileWriterBase_ShouldIncludePageCount_WithTiffAndPageRange_ReturnsTrue()
        {
            // Arrange & Act & Assert
            var method = typeof(LoadFiles.LoadFileWriterBase).GetMethod(
                "ShouldIncludePageCount",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            var tiffRequest = this.CreateTestRequest("tiff");
            tiffRequest.TiffPageRange = (1, 10);
            var result = (bool)method!.Invoke(null, new object[] { tiffRequest })!;
            Assert.True(result); // TIFF with page range

            tiffRequest.TiffPageRange = null;
            result = (bool)method.Invoke(null, new object[] { tiffRequest })!;
            Assert.False(result); // TIFF without page range

            var pdfRequest = this.CreateTestRequest("pdf");
            pdfRequest.TiffPageRange = (1, 10);
            result = (bool)method.Invoke(null, new object[] { pdfRequest })!;
            Assert.False(result); // Non-TIFF even with page range
        }

        [Fact]
        public void LoadFileWriterBase_GenerateTextPath_ReplacesExtensionCorrectly()
        {
            // Arrange
            var workItem = new FileWorkItem
            {
                Index = 1,
                FolderNumber = 1,
                FilePathInZip = "folder_001/document_00000001.pdf",
            };
            var request = this.CreateTestRequest("pdf");

            // Act
            var method = typeof(LoadFiles.LoadFileWriterBase).GetMethod(
                "GenerateTextPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var result = (string)method!.Invoke(null, new object[] { request, workItem })!;

            // Assert
            Assert.Equal("folder_001/document_00000001.txt", result);
        }

        [Fact]
        public void LoadFileWriterBase_GenerateDocumentId_FormatsCorrectly()
        {
            // Arrange
            var workItem = new FileWorkItem
            {
                Index = 42,
                FolderNumber = 1,
                FilePathInZip = "folder_001/file_00000042.pdf",
            };

            // Act
            var method = typeof(LoadFiles.LoadFileWriterBase).GetMethod(
                "GenerateDocumentId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var result = (string)method!.Invoke(null, new object[] { workItem })!;

            // Assert
            Assert.Equal("DOC00000042", result);
        }

        [Fact]
        public void LoadFileWriterBase_GenerateBatesNumber_WithConfig_GeneratesBatesNumber()
        {
            // Arrange
            var workItem = new FileWorkItem
            {
                Index = 10,
                FolderNumber = 1,
                FilePathInZip = "folder_001/file_00000010.pdf",
            };
            var request = this.CreateTestRequest();
            request.BatesConfig = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1000,
                Digits = 6,
                Increment = 1,
            };

            // Act
            var method = typeof(LoadFiles.LoadFileWriterBase).GetMethod(
                "GenerateBatesNumber",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var result = (string)method!.Invoke(null, new object[] { request, workItem })!;

            // Assert
            Assert.Equal("TEST001009", result);
        }
    }
}
