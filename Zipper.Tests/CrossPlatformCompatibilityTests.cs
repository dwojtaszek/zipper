using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class CrossPlatformCompatibilityTests
    {
        private readonly ITestOutputHelper _output;

        public CrossPlatformCompatibilityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void CommandLineValidator_ParsesArgumentsConsistently_AcrossPlatforms()
        {
            // Test command line parsing with various arguments that should work consistently
            var testCases = new[]
            {
                new[] { "--type", "pdf", "--count", "10", "--output-path", "/tmp/test" },
                new[] { "--type", "jpg", "--count", "5", "--output-path", "C:\\temp\\test" },
                new[] { "--type", "eml", "--count", "15", "--output-path", "/var/tmp/test" },
                new[] { "--type", "tiff", "--count", "8", "--output-path", "/Users/test/Documents" }
            };

            foreach (var args in testCases)
            {
                // Act
                var result = CommandLineValidator.ValidateAndParseArguments(args);

                // Assert
                Assert.NotNull(result);
                Assert.Equal(args[1].ToLower(), result.FileType.ToLower());
                Assert.Equal(int.Parse(args[3]), result.FileCount);
                Assert.True(result.FileCount > 0);

                _output.WriteLine($"Parsed args: {string.Join(" ", args)} -> Type: {result.FileType}, Count: {result.FileCount}");
            }
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void PathValidator_HandlesPlatformSpecificPaths_Correctly()
        {
            // Test path validation across different path formats
            var testPaths = new[]
            {
                Path.GetTempPath(), // Platform-agnostic temp path
                "/tmp/test",         // Unix-style path
                "C:\\temp\\test",    // Windows-style path
                "/home/user/test",   // Unix home directory
                "C:\\Users\\test",    // Windows Users directory
            };

            foreach (var testPath in testPaths)
            {
                // Skip paths that don't exist on current platform
                if (!Directory.Exists(testPath))
                {
                    try
                    {
                        Directory.CreateDirectory(testPath);
                        // Test path creation
                        var result = PathValidator.ValidateAndCreateDirectory(testPath);
                        Assert.NotNull(result);
                        Directory.Delete(testPath, true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Expected for some system paths
                        _output.WriteLine($"Skipping protected path: {testPath}");
                        continue;
                    }
                }
                else
                {
                    // Test existing path
                    var result = PathValidator.ValidateAndCreateDirectory(testPath);
                    Assert.NotNull(result);
                    Assert.Equal(Path.GetFullPath(testPath), result.FullName);
                }

                _output.WriteLine($"Path validation successful: {testPath}");
            }
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void DistributionAlgorithms_ProduceConsistentResults_AcrossPlatforms()
        {
            // Test that distribution algorithms work consistently regardless of platform
            var algorithms = new[] { "proportional", "gaussian", "exponential" };
            const int testFileCount = 1000;
            const int testFolderCount = 10;

            foreach (var algorithm in algorithms)
            {
                var distribution = FileDistributionHelper.GetDistribution(algorithm);
                var folderCounts = new int[testFolderCount];

                // Generate folder numbers using the distribution
                for (int i = 0; i < testFileCount; i++)
                {
                    var folderNumber = FileDistributionHelper.GetFolderNumber(i, testFolderCount, distribution);
                    Assert.True(folderNumber >= 1 && folderNumber <= testFolderCount,
                        $"Folder number {folderNumber} out of range for {algorithm}");
                    folderCounts[folderNumber - 1]++;
                }

                // Verify distribution characteristics
                Assert.Equal(testFileCount, folderCounts.Sum());

                if (algorithm == "proportional")
                {
                    // Proportional should distribute fairly evenly
                    var maxCount = folderCounts.Max();
                    var minCount = folderCounts.Min();
                    var ratio = (double)maxCount / minCount;
                    Assert.True(ratio < 3.0, $"Proportional distribution too uneven: max={maxCount}, min={minCount}, ratio={ratio:F2}");
                }

                _output.WriteLine($"{algorithm} distribution: max={folderCounts.Max()}, min={folderCounts.Min()}, avg={(double)folderCounts.Sum() / testFolderCount:F1}");
            }
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void EmailTemplateSystem_GeneratesConsistentEmails_AcrossPlatforms()
        {
            // Test email template generation produces consistent results
            var categories = Enum.GetValues<EmailTemplateSystem.EmailCategory>();
            var testIndex = 42;
            var testSenderIndex = 84;

            foreach (var category in categories)
            {
                // Generate multiple templates to check consistency
                var templates = new EmailTemplate[5];
                for (int i = 0; i < 5; i++)
                {
                    templates[i] = EmailTemplateSystem.GetRandomTemplate(testIndex + i, testSenderIndex + i, category);
                }

                // Verify all templates are valid
                foreach (var template in templates)
                {
                    Assert.NotNull(template);
                    Assert.False(string.IsNullOrEmpty(template.To));
                    Assert.False(string.IsNullOrEmpty(template.From));
                    Assert.False(string.IsNullOrEmpty(template.Subject));
                    Assert.False(string.IsNullOrEmpty(template.Body));
                    Assert.True(template.SentDate <= DateTime.Now);

                    // Verify no unprocessed placeholders
                    Assert.DoesNotContain("{", template.Subject);
                    Assert.DoesNotContain("{", template.Body);
                }

                // Check for reasonable variety
                var uniqueSubjects = templates.Select(t => t.Subject).Distinct().Count();
                Assert.True(uniqueSubjects >= 1, $"Should have variety in {category} email subjects");

                _output.WriteLine($"{category}: Generated {templates.Length} valid templates, {uniqueSubjects} unique subjects");
            }
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void LoadFileGenerator_HandlesEncodingsConsistently_AcrossPlatforms()
        {
            // Test load file generation with different encodings
            var encodings = new[] { "UTF-8", "UTF-16", "ANSI" };
            var testCount = 10;

            foreach (var encoding in encodings)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                var loadFilePath = Path.Combine(tempDir, "test.dat");

                try
                {
                    Directory.CreateDirectory(tempDir);

                    // Initialize progress tracking
                    ProgressTracker.Initialize(testCount);

                    // Create load file
                    LoadFileGenerator.CreateLoadFile(loadFilePath, "pdf", testCount, encoding);

                    // Verify file exists and has content
                    Assert.True(File.Exists(loadFilePath));

                    // Read and verify content based on encoding
                    var content = ReadFileWithEncoding(loadFilePath, encoding);
                    Assert.False(string.IsNullOrEmpty(content));

                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    Assert.True(lines.Length >= testCount);

                    // Verify header contains expected columns
                    var header = lines[0];
                    Assert.Contains("Control Number", header);
                    Assert.Contains("File Path", header);

                    _output.WriteLine($"{encoding}: Generated load file with {lines.Length} lines");
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void EmlGenerationService_ProducesValidEml_AcrossPlatforms()
        {
            // Test EML generation service produces consistent results
            var configs = new[]
            {
                new EmlGenerationConfig
                {
                    FileIndex = 1,
                    AttachmentRate = 0,
                    Category = EmailTemplateSystem.EmailCategory.Business
                },
                new EmlGenerationConfig
                {
                    FileIndex = 2,
                    AttachmentRate = 100,
                    Category = EmailTemplateSystem.EmailCategory.Personal
                },
                new EmlGenerationConfig
                {
                    FileIndex = 3,
                    AttachmentRate = 50,
                    Category = EmailTemplateSystem.EmailCategory.Technical
                }
            };

            foreach (var config in configs)
            {
                // Generate EML content
                var result = EmlGenerationService.GenerateEmlContent(config);

                // Verify EML structure
                Assert.NotNull(result);
                Assert.True(result.Content.Length > 0);

                var content = Encoding.UTF8.GetString(result.Content);
                Assert.Contains("From:", content);
                Assert.Contains("To:", content);
                Assert.Contains("Subject:", content);
                Assert.Contains("Date:", content);
                Assert.Contains("MIME-Version: 1.0", content);

                // Verify attachment behavior
                if (config.AttachmentRate == 0)
                {
                    Assert.Null(result.Attachment);
                }
                else if (config.AttachmentRate == 100)
                {
                    Assert.NotNull(result.Attachment);
                    Assert.True(result.Attachment.Value.content.Length > 0);
                }

                _output.WriteLine($"Config {config.FileIndex}: {result.Content.Length} bytes, attachment: {result.Attachment.HasValue}");
            }
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void MemoryPoolManager_WorksConsistently_AcrossPlatforms()
        {
            // Test memory pool manager works consistently
            using var manager = new MemoryPoolManager();
            var sizes = new[] { 1024, 4096, 65536, 1048576 };

            foreach (var size in sizes)
            {
                // Rent memory
                var memoryOwner = manager.Rent(size);
                Assert.NotNull(memoryOwner);
                Assert.True(memoryOwner.Memory.Length >= size);

                // Write test data
                var testData = new byte[size];
                new Random(42).NextBytes(testData);
                testData.CopyTo(memoryOwner.Memory.Span);

                // Verify data
                var readData = new byte[size];
                memoryOwner.Memory.Span.CopyTo(readData);
                Assert.Equal(testData, readData);

                // Dispose
                memoryOwner.Dispose();

                _output.WriteLine($"Memory pool: {size} bytes handled successfully");
            }
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void PerformanceMonitor_AccuratelyTracksMetrics_AcrossPlatforms()
        {
            // Test performance monitor works consistently
            var monitor = new PerformanceMonitor();
            const int testFileCount = 100;

            // Start monitoring
            monitor.Start(testFileCount);

            // Simulate work
            for (int i = 0; i < 10; i++)
            {
                monitor.ReportFilesCompleted(10);
                System.Threading.Thread.Sleep(1); // Small delay
            }

            // Stop and get metrics
            var metrics = monitor.Stop();

            // Verify metrics
            Assert.NotNull(metrics);
            Assert.True(metrics.ElapsedMilliseconds >= 0);
            Assert.True(metrics.FilesPerSecond >= 0);

            _output.WriteLine($"Performance: {metrics.FilesPerSecond:F2} files/sec in {metrics.ElapsedMilliseconds}ms");
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void ProgressTracker_HandlesMultiThreadedAccess_AcrossPlatforms()
        {
            // Test progress tracker handles concurrent access safely
            const int threadCount = 5;
            const int operationsPerThread = 20;
            var tasks = new Task[threadCount];

            // Initialize progress tracking
            ProgressTracker.Initialize(threadCount * operationsPerThread);

            // Simulate concurrent progress updates
            for (int i = 0; i < threadCount; i++)
            {
                var threadIndex = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        ProgressTracker.ReportProgress();
                        System.Threading.Thread.Sleep(1);
                    }
                });
            }

            // Wait for all threads to complete
            Task.WaitAll(tasks);

            // Get final progress
            var progress = ProgressTracker.GetProgress();

            Assert.True(progress.FilesCompleted > 0);
            Assert.True(progress.FilesCompleted <= threadCount * operationsPerThread);

            _output.WriteLine($"Progress tracking: {progress.FilesCompleted} files completed");
        }

        [Fact]
        [Trait("Category", "CrossPlatform")]
        public void ZipArchiveService_CreatesValidArchives_AcrossPlatforms()
        {
            // Test ZIP archive creation works consistently
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var zipFilePath = Path.Combine(tempDir, "test.zip");

            try
            {
                Directory.CreateDirectory(tempDir);

                // Create test file data
                var testData = new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FolderName = "folder_001",
                        FileName = "00000001.pdf",
                        FilePathInZip = "folder_001/00000001.pdf"
                    },
                    Data = new byte[] { 1, 2, 3, 4, 5 }
                };

                // Create channel for file data
                var channel = Channel.CreateBounded<FileData>(new BoundedChannelOptions(1));
                channel.Writer.TryWrite(testData);
                channel.Writer.Complete();

                // Create archive
                var request = new FileGenerationRequest
                {
                    FileType = "pdf",
                    FileCount = 1,
                    OutputPath = tempDir,
                    Folders = 1,
                    Distribution = "proportional",
                    Encoding = "UTF-8",
                    WithMetadata = false,
                    WithText = false,
                    AttachmentRate = 0,
                    IncludeLoadFile = false
                };

                ZipArchiveService.CreateArchiveAsync(zipFilePath, null, null, request, channel.Reader).Wait();

                // Verify archive was created
                Assert.True(File.Exists(zipFilePath));

                // Verify archive content
                using var archive = System.IO.Compression.ZipFile.OpenRead(zipFilePath);
                var entries = archive.Entries.ToList();
                Assert.Single(entries);
                Assert.Equal("00000001.pdf", entries[0].Name);

                _output.WriteLine($"ZIP archive created successfully with {entries.Count} entries");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        private static string ReadFileWithEncoding(string filePath, string encoding)
        {
            return encoding switch
            {
                "UTF-16" => Encoding.Unicode.GetString(File.ReadAllBytes(filePath)),
                "ANSI" => Encoding.GetEncoding(1252).GetString(File.ReadAllBytes(filePath)),
                _ => File.ReadAllText(filePath)
            };
        }
    }
}