using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class LoadFileGeneratorTests
    {
        private readonly ITestOutputHelper _output;

        public LoadFileGeneratorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task WriteLoadFileToArchiveAsync_BasicRequest_CreatesValidLoadFile()
        {
            // Arrange
            using var memoryStream = new MemoryStream();
            using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

            var loadFileName = "test_load.dat";
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 3,
                WithMetadata = false
            };

            var testFiles = CreateTestFiles(3, "pdf");

            // Act
            await LoadFileGenerator.WriteLoadFileToArchiveAsync(archive, loadFileName, request, testFiles);

            // Assert
            archive.Dispose();
            var zipBytes = memoryStream.ToArray();
            using var testArchive = new ZipArchive(new MemoryStream(zipBytes));

            var loadEntry = testArchive.GetEntry(loadFileName);
            Assert.NotNull(loadEntry);

            using var loadStream = loadEntry.Open();
            using var reader = new StreamReader(loadStream);
            var content = await reader.ReadToEndAsync();

            Assert.Contains("Control Number", content);
            Assert.Contains("File Path", content);
        }

        [Fact]
        public async Task WriteLoadFileToArchiveAsync_WithMetadata_IncludesMetadataColumns()
        {
            // Arrange
            using var memoryStream = new MemoryStream();
            using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

            var loadFileName = "test_load.dat";
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 2,
                WithMetadata = true
            };

            var testFiles = CreateTestFiles(2, "pdf");

            // Act
            await LoadFileGenerator.WriteLoadFileToArchiveAsync(archive, loadFileName, request, testFiles);

            // Assert
            archive.Dispose();
            var zipBytes = memoryStream.ToArray();
            using var testArchive = new ZipArchive(new MemoryStream(zipBytes));

            var loadEntry = testArchive.GetEntry(loadFileName);
            Assert.NotNull(loadEntry);

            using var loadStream = loadEntry.Open();
            using var reader = new StreamReader(loadStream);
            var content = await reader.ReadToEndAsync();

            Assert.Contains("Control Number", content);
            Assert.Contains("File Path", content);
            Assert.Contains("Custodian", content);
            Assert.Contains("Date Sent", content);
            Assert.Contains("Author", content);
            Assert.Contains("File Size", content);
        }

        [Fact]
        public async Task WriteLoadFileToArchiveAsync_EmlFiles_IncludesEmlColumns()
        {
            // Arrange
            using var memoryStream = new MemoryStream();
            using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

            var loadFileName = "test_load.dat";
            var request = new FileGenerationRequest
            {
                FileType = "eml",
                FileCount = 2,
                WithMetadata = false
            };

            var testFiles = CreateTestFiles(2, "eml");

            // Act
            await LoadFileGenerator.WriteLoadFileToArchiveAsync(archive, loadFileName, request, testFiles);

            // Assert
            archive.Dispose();
            var zipBytes = memoryStream.ToArray();
            using var testArchive = new ZipArchive(new MemoryStream(zipBytes));

            var loadEntry = testArchive.GetEntry(loadFileName);
            Assert.NotNull(loadEntry);

            using var loadStream = loadEntry.Open();
            using var reader = new StreamReader(loadStream);
            var content = await reader.ReadToEndAsync();

            Assert.Contains("Control Number", content);
            Assert.Contains("File Path", content);
            Assert.Contains("To", content);
            Assert.Contains("From", content);
            Assert.Contains("Subject", content);
            Assert.Contains("Sent Date", content);
            Assert.Contains("Attachment", content);
        }

        [Fact]
        public async Task WriteLoadFileToArchiveAsync_WithText_IncludesTextColumns()
        {
            // Arrange
            using var memoryStream = new MemoryStream();
            using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

            var loadFileName = "test_load.dat";
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 2,
                WithText = true
            };

            var testFiles = CreateTestFiles(2, "pdf");

            // Act
            await LoadFileGenerator.WriteLoadFileToArchiveAsync(archive, loadFileName, request, testFiles);

            // Assert
            archive.Dispose();
            var zipBytes = memoryStream.ToArray();
            using var testArchive = new ZipArchive(new MemoryStream(zipBytes));

            var loadEntry = testArchive.GetEntry(loadFileName);
            Assert.NotNull(loadEntry);

            using var loadStream = loadEntry.Open();
            using var reader = new StreamReader(loadStream);
            var content = await reader.ReadToEndAsync();

            Assert.Contains("Control Number", content);
            Assert.Contains("File Path", content);
            Assert.Contains("Extracted Text", content);
        }

        [Fact]
        public async Task WriteLoadFileToDiskAsync_CreatesValidLoadFile()
        {
            // Arrange
            var loadFilePath = Path.GetTempFileName();
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 2,
                Encoding = "UTF-8"
            };

            var testFiles = CreateTestFiles(2, "pdf");

            // Act
            await LoadFileGenerator.WriteLoadFileToDiskAsync(loadFilePath, request, testFiles);

            // Assert
            Assert.True(File.Exists(loadFilePath));

            var content = await File.ReadAllTextAsync(loadFilePath);
            Assert.Contains("Control Number", content);
            Assert.Contains("File Path", content);
        }

        [Theory]
        [InlineData("UTF-8")]
        [InlineData("ANSI")]
        [InlineData("UTF-16")]
        public async Task WriteLoadFileToDiskAsync_DifferentEncodings_UsesCorrectEncoding(string encoding)
        {
            // Arrange
            var loadFilePath = Path.GetTempFileName();
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 1,
                Encoding = encoding
            };

            var testFiles = CreateTestFiles(1, "pdf");

            // Act
            await LoadFileGenerator.WriteLoadFileToDiskAsync(loadFilePath, request, testFiles);

            // Assert
            Assert.True(File.Exists(loadFilePath));

            // Read file with specified encoding to verify it was written correctly
            var fileEncoding = LoadFileGenerator.GetEncoding(encoding);
            using var reader = new StreamReader(loadFilePath, FileEncodingDetect.GetEncoding(loadFilePath));
            var content = await reader.ReadToEndAsync();

            Assert.Contains("Control Number", content);
        }

        [Fact]
        public void GetEncoding_UTF8_ReturnsCorrectEncoding()
        {
            // Act
            var encoding = LoadFileGenerator.GetEncoding("UTF-8");

            // Assert
            Assert.Equal(System.Text.Encoding.UTF8, encoding);
        }

        [Fact]
        public void GetEncoding_ANSI_ReturnsWindows1252Encoding()
        {
            // Act
            var encoding = LoadFileGenerator.GetEncoding("ANSI");

            // Assert
            Assert.Equal(System.Text.Encoding.GetEncoding(1252), encoding);
        }

        [Fact]
        public void GetEncoding_UTF16_ReturnsUnicodeEncoding()
        {
            // Act
            var encoding = LoadFileGenerator.GetEncoding("UTF-16");

            // Assert
            Assert.Equal(System.Text.Encoding.Unicode, encoding);
        }

        [Fact]
        public void GetEncoding_UnknownEncoding_ReturnsUTF8()
        {
            // Act
            var encoding = LoadFileGenerator.GetEncoding("UNKNOWN");

            // Assert
            Assert.Equal(System.Text.Encoding.UTF8, encoding);
        }

        private List<FileData> CreateTestFiles(int count, string fileType)
        {
            var files = new List<FileData>();
            for (int i = 0; i < count; i++)
            {
                files.Add(new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = i,
                        FileName = $"test{i}.{fileType}",
                        FilePathInZip = $"folder{i % 3}/test{i}.{fileType}",
                        FolderName = $"folder{i % 3}",
                        FolderNumber = i % 3
                    },
                    Data = System.Text.Encoding.UTF8.GetBytes($"Test content {i}"),
                    Attachment = null,
                    MemoryOwner = null
                });
            }
            return files;
        }
    }
}