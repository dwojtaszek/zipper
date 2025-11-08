using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class ZipArchiveServiceTests
    {
        private readonly ITestOutputHelper _output;

        public ZipArchiveServiceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task CreateArchiveAsync_WithBasicFiles_CreatesValidArchive()
        {
            // Arrange
            var zipPath = Path.GetTempFileName();
            var loadPath = Path.GetTempFileName();
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 5,
                Concurrency = 1,
                IncludeLoadFile = false
            };

            var testFiles = new List<FileData>();
            for (int i = 0; i < 5; i++)
            {
                testFiles.Add(CreateTestFileData(i));
            }

            var channel = Channel.CreateUnbounded<FileData>();
            var writer = channel.Writer;
            foreach (var file in testFiles)
            {
                await writer.WriteAsync(file);
            }
            writer.Complete();

            // Act
            await ZipArchiveService.CreateArchiveAsync(zipPath, "load.dat", loadPath, request, channel.Reader);

            // Assert
            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Equal(5, archive.Entries.Count(e => e.FullName.EndsWith(".pdf")));
        }

        [Fact]
        public async Task CreateArchiveAsync_WithLoadFileIncluded_IncludesLoadFileInArchive()
        {
            // Arrange
            var zipPath = Path.GetTempFileName();
            var loadPath = Path.GetTempFileName();
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 3,
                Concurrency = 1,
                IncludeLoadFile = true
            };

            var testFiles = new List<FileData>();
            for (int i = 0; i < 3; i++)
            {
                testFiles.Add(CreateTestFileData(i));
            }

            var channel = Channel.CreateUnbounded<FileData>();
            var writer = channel.Writer;
            foreach (var file in testFiles)
            {
                await writer.WriteAsync(file);
            }
            writer.Complete();

            // Act
            await ZipArchiveService.CreateArchiveAsync(zipPath, "load.dat", loadPath, request, channel.Reader);

            // Assert
            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Equal(4, archive.Entries.Count); // 3 PDF files + 1 load file

            var loadEntry = archive.GetEntry("load.dat");
            Assert.NotNull(loadEntry);
        }

        [Fact]
        public async Task CreateArchiveAsync_WithTextFiles_CreatesTextFilesAlongsideMainFiles()
        {
            // Arrange
            var zipPath = Path.GetTempFileName();
            var loadPath = Path.GetTempFileName();
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 3,
                Concurrency = 1,
                WithText = true
            };

            var testFiles = new List<FileData>();
            for (int i = 0; i < 3; i++)
            {
                testFiles.Add(CreateTestFileData(i));
            }

            var channel = Channel.CreateUnbounded<FileData>();
            var writer = channel.Writer;
            foreach (var file in testFiles)
            {
                await writer.WriteAsync(file);
            }
            writer.Complete();

            // Act
            await ZipArchiveService.CreateArchiveAsync(zipPath, "load.dat", loadPath, request, channel.Reader);

            // Assert
            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Equal(6, archive.Entries.Count); // 3 PDF files + 3 text files

            // Verify text files exist
            var textEntries = archive.Entries.Where(e => e.FullName.EndsWith(".txt")).ToList();
            Assert.Equal(3, textEntries.Count);
        }

        [Fact]
        public async Task CreateArchiveAsync_WithEmlFilesAndAttachments_HandlesAttachmentsCorrectly()
        {
            // Arrange
            var zipPath = Path.GetTempFileName();
            var loadPath = Path.GetTempFileName();
            var request = new FileGenerationRequest
            {
                FileType = "eml",
                FileCount = 2,
                Concurrency = 1,
                WithText = false
            };

            var testFiles = new List<FileData>();
            for (int i = 0; i < 2; i++)
            {
                testFiles.Add(CreateTestEmlFileData(i));
            }

            var channel = Channel.CreateUnbounded<FileData>();
            var writer = channel.Writer;
            foreach (var file in testFiles)
            {
                await writer.WriteAsync(file);
            }
            writer.Complete();

            // Act
            await ZipArchiveService.CreateArchiveAsync(zipPath, "load.dat", loadPath, request, channel.Reader);

            // Assert
            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);

            // Should have 2 EML files and 2 attachments
            var emlEntries = archive.Entries.Where(e => e.FullName.EndsWith(".eml")).ToList();
            var attachmentEntries = archive.Entries.Where(e => e.FullName.Contains("attachment.") && e.FullName.EndsWith(".txt")).ToList();

            Assert.Equal(2, emlEntries.Count);
            Assert.Equal(2, attachmentEntries.Count);
        }

        [Fact]
        public async Task CreateArchiveAsync_WithMixedOptions_HandlesAllCombinations()
        {
            // Arrange
            var zipPath = Path.GetTempFileName();
            var loadPath = Path.GetTempFileName();
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 2,
                Concurrency = 1,
                WithText = true,
                IncludeLoadFile = true
            };

            var testFiles = new List<FileData>();
            for (int i = 0; i < 2; i++)
            {
                testFiles.Add(CreateTestFileData(i));
            }

            var channel = Channel.CreateUnbounded<FileData>();
            var writer = channel.Writer;
            foreach (var file in testFiles)
            {
                await writer.WriteAsync(file);
            }
            writer.Complete();

            // Act
            await ZipArchiveService.CreateArchiveAsync(zipPath, "load.dat", loadPath, request, channel.Reader);

            // Assert
            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);

            // Should have: 2 PDF files + 2 text files + 1 load file = 5 total files
            Assert.Equal(5, archive.Entries.Count);

            var pdfEntries = archive.Entries.Where(e => e.FullName.EndsWith(".pdf")).ToList();
            var textEntries = archive.Entries.Where(e => e.FullName.EndsWith(".txt")).ToList();
            var loadEntry = archive.GetEntry("load.dat");

            Assert.Equal(2, pdfEntries.Count);
            Assert.Equal(2, textEntries.Count);
            Assert.NotNull(loadEntry);
        }

        private FileData CreateTestFileData(int index)
        {
            return new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = index,
                    FileName = $"test{index}.pdf",
                    FilePathInZip = $"folder{index % 5}/test{index}.pdf",
                    FolderName = $"folder{index % 5}",
                    FolderNumber = index % 5
                },
                Data = System.Text.Encoding.UTF8.GetBytes($"Test content {index}"),
                MemoryOwner = null
            };
        }

        private FileData CreateTestEmlFileData(int index)
        {
            return new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = index,
                    FileName = $"test{index}.eml",
                    FilePathInZip = $"folder{index % 3}/test{index}.eml",
                    FolderName = $"folder{index % 3}",
                    FolderNumber = index % 3
                },
                Data = System.Text.Encoding.UTF8.GetBytes($"Test EML content {index}"),
                MemoryOwner = null,
                Attachment = ($"attachment{index}.txt", System.Text.Encoding.UTF8.GetBytes($"Attachment content {index}"))
            };
        }
    }
}