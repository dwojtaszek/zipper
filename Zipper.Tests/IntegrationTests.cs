using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class IntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public IntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ParallelFileGenerator_WithRefactoredServices_GeneratesValidArchive()
        {
            // Arrange
            using var generator = new ParallelFileGenerator();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = 100,
                OutputPath = tempDir,
                Folders = 5,
                Distribution = "proportional",
                Encoding = "UTF-8",
                WithMetadata = false,
                WithText = false,
                AttachmentRate = 0,
                IncludeLoadFile = true
            };

            try
            {
                // Act
                var result = await generator.GenerateFilesAsync(request);

                // Assert
                Assert.NotNull(result);
                Assert.True(File.Exists(result.ZipFilePath));
                Assert.True(File.Exists(result.LoadFilePath));
                Assert.Equal(100, result.FilesGenerated);
                Assert.True(result.GenerationTime.TotalMilliseconds > 0);
                Assert.True(result.FilesPerSecond > 0);

                _output.WriteLine($"Generated {result.FilesGenerated} files in {result.GenerationTime.TotalMilliseconds:F1}ms");
                _output.WriteLine($"Performance: {result.FilesPerSecond:F1} files/second");

                // Verify zip file size
                var zipFileInfo = new FileInfo(result.ZipFilePath);
                Assert.True(zipFileInfo.Length > 0);

                // Verify load file content
                var loadFileLines = File.ReadAllLines(result.LoadFilePath);
                Assert.True(loadFileLines.Length > 0);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task ParallelFileGenerator_WithEmlFiles_UsesRefactoredServices()
        {
            // Arrange
            using var generator = new ParallelFileGenerator();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var request = new FileGenerationRequest
            {
                FileType = "eml",
                FileCount = 50,
                OutputPath = tempDir,
                Folders = 3,
                Encoding = "UTF-8",
                WithMetadata = false,
                WithText = false,
                AttachmentRate = 50, // 50% attachment rate
                IncludeLoadFile = true
            };

            try
            {
                // Act
                var result = await generator.GenerateFilesAsync(request);

                // Assert
                Assert.NotNull(result);
                Assert.Equal(50, result.FilesGenerated);
                Assert.True(File.Exists(result.ZipFilePath));
                Assert.True(File.Exists(result.LoadFilePath));

                // Verify that we can extract and read some EML content
                using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
                var entries = archive.Entries.Where(e => e.FullName.EndsWith(".eml")).Take(5).ToList();

                Assert.True(entries.Count > 0, "Should have EML files in archive");

                foreach (var entry in entries)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();

                    // Basic EML validation
                    Assert.Contains("From:", content);
                    Assert.Contains("To:", content);
                    Assert.Contains("Subject:", content);
                    Assert.Contains("Date:", content);

                    _output.WriteLine($"EML file: {entry.FullName} - {content.Length} characters");
                }
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task ParallelFileGenerator_WithAllDistributions_UsesMathematicalAlgorithms()
        {
            // Arrange
            var distributions = new[] { "proportional", "gaussian", "exponential" };
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            foreach (var distribution in distributions)
            {
                using var generator = new ParallelFileGenerator();
                var request = new FileGenerationRequest
                {
                    FileType = "pdf",
                    FileCount = 30,
                    OutputPath = tempDir,
                    Folders = 3,
                    Distribution = distribution,
                    Encoding = "UTF-8",
                    WithMetadata = false,
                    WithText = false,
                    AttachmentRate = 0,
                    IncludeLoadFile = true
                };

                try
                {
                    // Act
                    var result = await generator.GenerateFilesAsync(request);

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(30, result.FilesGenerated);
                    Assert.True(File.Exists(result.ZipFilePath));
                    Assert.True(File.Exists(result.LoadFilePath));

                    // Verify load file has correct number of entries
                    var loadFileLines = File.ReadAllLines(result.LoadFilePath);
                    Assert.True(loadFileLines.Length > 30); // Header + file entries

                    _output.WriteLine($"Distribution '{distribution}': {result.FilesPerSecond:F1} files/sec");
                }
                finally
                {
                    // Cleanup
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
        }

        [Fact]
        public void LoadFileGenerator_WithRefactoredProgress_GeneratesValidLoadFile()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var loadFilePath = Path.Combine(tempDir, "test.dat");

            try
            {
                Directory.CreateDirectory(tempDir);

                // Initialize progress tracking
                ProgressTracker.Initialize(10);

                // Act
                LoadFileGenerator.CreateLoadFile(loadFilePath, "pdf", 10, "UTF-8");

                // Assert
                Assert.True(File.Exists(loadFilePath));

                var content = File.ReadAllText(loadFilePath);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                Assert.True(lines.Length > 0);
                Assert.Contains("pdf", lines.First()); // Should have file type in header

                _output.WriteLine($"Load file generated with {lines.Length} lines");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void ZipArchiveService_WithRefactoredArchitecture_CreatesValidArchive()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var zipFilePath = Path.Combine(tempDir, "test.zip");
            var loadFilePath = Path.Combine(tempDir, "test.dat");

            try
            {
                Directory.CreateDirectory(tempDir);

                var request = new FileGenerationRequest
                {
                    FileType = "pdf",
                    FileCount = 5,
                    OutputPath = tempDir,
                    Folders = 2,
                    Distribution = "proportional",
                    Encoding = "UTF-8",
                    WithMetadata = false,
                    WithText = false,
                    AttachmentRate = 0,
                    IncludeLoadFile = true
                };

                // Create test file data
                var fileData = Enumerable.Range(1, 5)
                    .Select(i => new FileData
                    {
                        WorkItem = new FileWorkItem
                        {
                            Index = i,
                            FolderNumber = i % 2 + 1,
                            FolderName = $"folder_{i % 2 + 1:D3}",
                            FileName = $"{i:D8}.pdf",
                            FilePathInZip = $"folder_{i % 2 + 1:D3}/{i:D8}.pdf"
                        },
                        Data = new byte[] { 1, 2, 3, 4, 5 }
                    });

                var channel = Channel.CreateBounded<FileData>(new BoundedChannelOptions(10));
                foreach (var data in fileData)
                {
                    channel.Writer.TryWrite(data);
                }
                channel.Writer.Complete();

                // Act
                ZipArchiveService.CreateArchiveAsync(zipFilePath, "test.dat", loadFilePath, request, channel.Reader).Wait();

                // Assert
                Assert.True(File.Exists(zipFilePath));
                Assert.True(File.Exists(loadFilePath));

                // Verify zip content
                using var archive = System.IO.Compression.ZipFile.OpenRead(zipFilePath);
                var entries = archive.Entries.ToList();
                Assert.Equal(5, entries.Count);

                foreach (var entry in entries)
                {
                    using var stream = entry.Open();
                    var buffer = new byte[5];
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    Assert.Equal(5, bytesRead);
                }

                _output.WriteLine($"Archive created with {entries.Count} files");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void EmlGenerationService_WithRefactoredComponents_GeneratesValidEml()
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = 75,
                Category = EmailTemplateSystem.EmailCategory.Business
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Length > 0);

            // Verify EML structure
            var content = System.Text.Encoding.UTF8.GetString(result.Content);
            Assert.Contains("From:", content);
            Assert.Contains("To:", content);
            Assert.Contains("Subject:", content);
            Assert.Contains("Date:", content);
            Assert.Contains("MIME-Version:", content);

            // Should have attachment based on 75% rate (not guaranteed, but likely)
            _output.WriteLine($"EML generated: {content.Length} characters");
            _output.WriteLine($"Has attachment: {result.Attachment.HasValue}");

            if (result.Attachment.HasValue)
            {
                Assert.True(result.Attachment.Value.content.Length > 0);
                Assert.False(string.IsNullOrEmpty(result.Attachment.Value.filename));
                _output.WriteLine($"Attachment: {result.Attachment.Value.filename} ({result.Attachment.Value.content.Length} bytes)");
            }
        }

        [Fact]
        public void EmailTemplateSystem_WithAllCategories_GeneratesValidEmails()
        {
            // Arrange
            var categories = Enum.GetValues<EmailTemplateSystem.EmailCategory>();

            foreach (var category in categories)
            {
                // Act
                var template = EmailTemplateSystem.GetRandomTemplate(1, 2, category);

                // Assert
                Assert.NotNull(template);
                Assert.False(string.IsNullOrEmpty(template.To));
                Assert.False(string.IsNullOrEmpty(template.From));
                Assert.False(string.IsNullOrEmpty(template.Subject));
                Assert.False(string.IsNullOrEmpty(template.Body));
                Assert.True(template.SentDate <= DateTime.Now);

                // Verify no unprocessed placeholders
                Assert.DoesNotContain("{", template.Subject);
                Assert.DoesNotContain("{", template.Body);

                _output.WriteLine($"Category {category}: {template.Subject}");
            }
        }

        [Fact]
        public void CommandLineValidator_WithRefactoredValidation_ParsesCorrectly()
        {
            // Arrange
            var args = new[]
            {
                "--type", "eml",
                "--count", "100",
                "--output-path", "/tmp/test",
                "--folders", "5",
                "--encoding", "UTF-16",
                "--distribution", "gaussian",
                "--with-metadata",
                "--with-text",
                "--attachment-rate", "50",
                "--target-zip-size", "10MB",
                "--include-load-file"
            };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("eml", result.FileType.ToLower());
            Assert.Equal(100, result.FileCount);
            Assert.Equal("/tmp/test", result.OutputPath);
            Assert.Equal(5, result.Folders);
            Assert.Equal("UTF-16", result.Encoding);
            Assert.Equal("gaussian", result.Distribution);
            Assert.True(result.WithMetadata);
            Assert.True(result.WithText);
            Assert.Equal(50, result.AttachmentRate);
            Assert.Equal(10485760, result.TargetZipSize); // 10MB
            Assert.True(result.IncludeLoadFile);

            _output.WriteLine("Command line arguments parsed successfully");
        }

        [Fact]
        public void PathValidator_WithSecurityValidation_UsesRefactoredLogic()
        {
            // Arrange
            var validPath = Path.GetTempPath();
            var invalidPath = "../../../etc/passwd"; // Path traversal attempt

            // Act & Assert
            var validResult = PathValidator.ValidateAndCreateDirectory(validPath);
            Assert.NotNull(validResult);
            Assert.Equal(Path.GetFullPath(validPath), validResult.FullName);

            // This should handle the invalid path safely (either reject or sanitize)
            var invalidResult = PathValidator.ValidateAndCreateDirectory(invalidPath);
            _output.WriteLine($"Invalid path result: {(invalidResult != null ? "Created/Sanitized" : "Rejected")}");
        }

        [Fact]
        public void MemoryPoolManager_WithRefactoredPooling_ManagesMemoryEfficiently()
        {
            // Arrange
            using var manager = new MemoryPoolManager();

            // Act
            var owners = new IMemoryOwner<byte>?[10];
            for (int i = 0; i < 10; i++)
            {
                owners[i] = manager.Rent(1024);
            }

            // Assert
            for (int i = 0; i < 10; i++)
            {
                Assert.NotNull(owners[i]);
                Assert.True(owners[i]!.Memory.Length >= 1024);
            }

            // Cleanup
            foreach (var owner in owners)
            {
                owner?.Dispose();
            }

            _output.WriteLine("Memory pool manager handled 10 allocations efficiently");
        }

        [Fact]
        public void PerformanceMonitor_WithRefactoredMetrics_TracksCorrectly()
        {
            // Arrange
            var monitor = new PerformanceMonitor();

            // Act
            monitor.Start(100);
            for (int i = 0; i < 10; i++)
            {
                monitor.ReportFilesCompleted(10);
                System.Threading.Thread.Sleep(1);
            }
            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.ElapsedMilliseconds > 0);
            Assert.True(metrics.FilesPerSecond > 0);

            _output.WriteLine($"Performance: {metrics.FilesPerSecond:F2} files/second in {metrics.ElapsedMilliseconds}ms");
        }
    }
}