using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests.Performance
{
    public class EndToEndPerformanceTests
    {
        private readonly ITestOutputHelper _output;

        // Performance targets for end-to-end scenarios
        private static readonly EndToEndBaseline _baseline = new()
        {
            // Small dataset: 100 files in < 2 seconds
            MaxSmallDatasetTimeMs = 2000,
            SmallDatasetFileCount = 100,

            // Medium dataset: 1000 files in < 10 seconds
            MaxMediumDatasetTimeMs = 10000,
            MediumDatasetFileCount = 1000,

            // Large dataset: 10000 files in < 60 seconds
            MaxLargeDatasetTimeMs = 60000,
            LargeDatasetFileCount = 10000,

            // Memory usage should be reasonable
            MaxMemoryUsageMB = 500, // Maximum memory usage during operations

            // Throughput targets
            MinFilesPerSecond = 50, // Minimum throughput
            MinMBPerSecond = 10     // Minimum data processing throughput
        };

        public EndToEndPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ParallelFileGenerator_SmallDataset_ShouldMeetPerformanceTargets()
        {
            // Arrange
            const int fileCount = _baseline.SmallDatasetFileCount;
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = fileCount,
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
                Directory.CreateDirectory(tempDir);

                // Act
                var stopwatch = Stopwatch.StartNew();
                long memoryBefore = GC.GetTotalMemory(true);

                using var generator = new ParallelFileGenerator();
                var result = await generator.GenerateFilesAsync(request);

                stopwatch.Stop();
                long memoryAfter = GC.GetTotalMemory(false);
                long memoryUsed = (memoryAfter - memoryBefore) / (1024 * 1024); // MB

                // Verify output
                Assert.NotNull(result);
                Assert.True(File.Exists(result.ZipFilePath), "ZIP file should exist");
                Assert.True(File.Exists(result.LoadFilePath), "Load file should exist");
                Assert.Equal(fileCount, result.FilesGenerated);

                // Performance assertions
                Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxSmallDatasetTimeMs,
                    $"Small dataset generation took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxSmallDatasetTimeMs}ms");

                Assert.True(memoryUsed < _baseline.MaxMemoryUsageMB,
                    $"Small dataset used {memoryUsed}MB memory, expected < {_baseline.MaxMemoryUsageMB}MB");

                var throughput = (double)fileCount / stopwatch.ElapsedMilliseconds * 1000;
                Assert.True(throughput >= _baseline.MinFilesPerSecond,
                    $"Small dataset throughput {throughput:F1} files/sec, expected >= {_baseline.MinFilesPerSecond} files/sec");

                _output.WriteLine($"Small Dataset Performance:");
                _output.WriteLine($"Files generated: {fileCount:N0}");
                _output.WriteLine($"Time taken: {stopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Throughput: {throughput:F1} files/second");
                _output.WriteLine($"Memory used: {memoryUsed}MB");
                _output.WriteLine($"Average per file: {(double)stopwatch.ElapsedMilliseconds / fileCount:F2}ms/file");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ParallelFileGenerator_MediumDataset_ShouldMeetPerformanceTargets()
        {
            // Arrange
            const int fileCount = _baseline.MediumDatasetFileCount;
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var request = new FileGenerationRequest
            {
                FileType = "pdf",
                FileCount = fileCount,
                OutputPath = tempDir,
                Folders = 10,
                Distribution = "gaussian",
                Encoding = "UTF-8",
                WithMetadata = true,
                WithText = false,
                AttachmentRate = 0,
                IncludeLoadFile = true
            };

            try
            {
                Directory.CreateDirectory(tempDir);

                // Act
                var stopwatch = Stopwatch.StartNew();
                long memoryBefore = GC.GetTotalMemory(true);

                using var generator = new ParallelFileGenerator();
                var result = await generator.GenerateFilesAsync(request);

                stopwatch.Stop();
                long memoryAfter = GC.GetTotalMemory(false);
                long memoryUsed = (memoryAfter - memoryBefore) / (1024 * 1024); // MB

                // Verify output
                Assert.NotNull(result);
                Assert.True(File.Exists(result.ZipFilePath), "ZIP file should exist");
                Assert.True(File.Exists(result.LoadFilePath), "Load file should exist");
                Assert.Equal(fileCount, result.FilesGenerated);

                // Get file sizes for throughput calculation
                var zipSize = new FileInfo(result.ZipFilePath).Length / (1024.0 * 1024.0); // MB
                var totalDataMB = zipSize;

                // Performance assertions
                Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxMediumDatasetTimeMs,
                    $"Medium dataset generation took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxMediumDatasetTimeMs}ms");

                Assert.True(memoryUsed < _baseline.MaxMemoryUsageMB,
                    $"Medium dataset used {memoryUsed}MB memory, expected < {_baseline.MaxMemoryUsageMB}MB");

                var filesPerSecond = (double)fileCount / stopwatch.ElapsedMilliseconds * 1000;
                var mbPerSecond = totalDataMB / stopwatch.ElapsedMilliseconds * 1000;

                Assert.True(filesPerSecond >= _baseline.MinFilesPerSecond,
                    $"Medium dataset throughput {filesPerSecond:F1} files/sec, expected >= {_baseline.MinFilesPerSecond} files/sec");

                _output.WriteLine($"Medium Dataset Performance:");
                _output.WriteLine($"Files generated: {fileCount:N0}");
                _output.WriteLine($"Time taken: {stopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Throughput: {filesPerSecond:F1} files/second");
                _output.WriteLine($"Data throughput: {mbPerSecond:F1} MB/second");
                _output.WriteLine($"Memory used: {memoryUsed}MB");
                _output.WriteLine($"ZIP size: {zipSize:F1}MB");
                _output.WriteLine($"Average per file: {(double)stopwatch.ElapsedMilliseconds / fileCount:F2}ms/file");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ParallelFileGenerator_EmlFiles_ShouldMeetPerformanceTargets()
        {
            // Arrange
            const int fileCount = 500; // Fewer files for EML due to complexity
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var request = new FileGenerationRequest
            {
                FileType = "eml",
                FileCount = fileCount,
                OutputPath = tempDir,
                Folders = 8,
                Distribution = "exponential",
                Encoding = "UTF-8",
                WithMetadata = true,
                WithText = true,
                AttachmentRate = 60,
                IncludeLoadFile = true
            };

            try
            {
                Directory.CreateDirectory(tempDir);

                // Act
                var stopwatch = Stopwatch.StartNew();
                long memoryBefore = GC.GetTotalMemory(true);

                using var generator = new ParallelFileGenerator();
                var result = await generator.GenerateFilesAsync(request);

                stopwatch.Stop();
                long memoryAfter = GC.GetTotalMemory(false);
                long memoryUsed = (memoryAfter - memoryBefore) / (1024 * 1024); // MB

                // Verify output
                Assert.NotNull(result);
                Assert.True(File.Exists(result.ZipFilePath), "ZIP file should exist");
                Assert.True(File.Exists(result.LoadFilePath), "Load file should exist");
                Assert.Equal(fileCount, result.FilesGenerated);

                // Get file sizes for throughput calculation
                var zipSize = new FileInfo(result.ZipFilePath).Length / (1024.0 * 1024.0); // MB

                // Performance assertions
                Assert.True(memoryUsed < _baseline.MaxMemoryUsageMB,
                    $"EML dataset used {memoryUsed}MB memory, expected < {_baseline.MaxMemoryUsageMB}MB");

                var filesPerSecond = (double)fileCount / stopwatch.ElapsedMilliseconds * 1000;
                var mbPerSecond = zipSize / stopwatch.ElapsedMilliseconds * 1000;

                _output.WriteLine($"EML Dataset Performance:");
                _output.WriteLine($"Files generated: {fileCount:N0}");
                _output.WriteLine($"Time taken: {stopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Throughput: {filesPerSecond:F1} files/second");
                _output.WriteLine($"Data throughput: {mbPerSecond:F1} MB/second");
                _output.WriteLine($"Memory used: {memoryUsed}MB");
                _output.WriteLine($"ZIP size: {zipSize:F1}MB");
                _output.WriteLine($"Average per file: {(double)stopwatch.ElapsedMilliseconds / fileCount:F2}ms/file");
                _output.WriteLine($"Attachment rate: 60%");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ParallelFileGenerator_AllFileTypes_ShouldMeetPerformanceTargets()
        {
            // Arrange
            const int filesPerType = 100;
            var fileTypes = new[] { "pdf", "jpg", "tiff" };
            var totalFiles = filesPerType * fileTypes.Length;
            var results = new System.Collections.Generic.List<(string type, long timeMs, double throughput, long memoryMB)>();

            foreach (var fileType in fileTypes)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                var request = new FileGenerationRequest
                {
                    FileType = fileType,
                    FileCount = filesPerType,
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
                    Directory.CreateDirectory(tempDir);

                    // Act
                    var stopwatch = Stopwatch.StartNew();
                    long memoryBefore = GC.GetTotalMemory(true);

                    using var generator = new ParallelFileGenerator();
                    var result = await generator.GenerateFilesAsync(request);

                    stopwatch.Stop();
                    long memoryAfter = GC.GetTotalMemory(false);
                    long memoryUsed = (memoryAfter - memoryBefore) / (1024 * 1024);
                    var throughput = (double)filesPerType / stopwatch.ElapsedMilliseconds * 1000;

                    results.Add((fileType, stopwatch.ElapsedMilliseconds, throughput, memoryUsed));

                    _output.WriteLine($"{fileType.ToUpper()} files: {filesPerType:N0} in {stopwatch.ElapsedMilliseconds}ms ({throughput:F1} files/sec, {memoryUsed}MB)");
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }

            // Assert overall performance
            var avgTime = results.Average(r => r.timeMs);
            var avgThroughput = results.Average(r => r.throughput);
            var avgMemory = results.Average(r => r.memoryMB);

            Assert.True(avgTime < _baseline.MaxSmallDatasetTimeMs,
                $"Average file type generation took {avgTime}ms, expected < {_baseline.MaxSmallDatasetTimeMs}ms");

            Assert.True(avgThroughput >= _baseline.MinFilesPerSecond,
                $"Average throughput {avgThroughput:F1} files/sec, expected >= {_baseline.MinFilesPerSecond} files/sec");

            _output.WriteLine($"All File Types Performance Summary:");
            _output.WriteLine($"Total files: {totalFiles:N0}");
            _output.WriteLine($"Average time per type: {avgTime:F1}ms");
            _output.WriteLine($"Average throughput: {avgThroughput:F1} files/second");
            _output.WriteLine($"Average memory usage: {avgMemory:F1}MB");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task MemoryPool_PerformanceUnderStress_ShouldMaintainEfficiency()
        {
            // Arrange
            const int operations = 10000;
            const int bufferSize = 81920; // 80KB typical file size

            // Act - Stress test memory pool
            var stopwatch = Stopwatch.StartNew();
            long memoryBefore = GC.GetTotalMemory(true);
            long gen0Before = GC.CollectionCount(0);
            long gen1Before = GC.CollectionCount(1);
            long gen2Before = GC.CollectionCount(2);

            using var manager = new MemoryPoolManager();
            var tasks = new Task[10]; // 10 concurrent operations

            for (int t = 0; t < tasks.Length; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < operations / tasks.Length; i++)
                    {
                        var memory = manager.Rent(bufferSize);
                        // Simulate some work with the memory
                        if (memory.Memory.Length > 0)
                        {
                            memory.Memory.Span.Fill((byte)(i % 256));
                        }
                        memory.Dispose();
                    }
                });
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            long gen0After = GC.CollectionCount(0);
            long gen1After = GC.CollectionCount(1);
            long gen2After = GC.CollectionCount(2);
            long memoryAfter = GC.GetTotalMemory(false);

            // Calculate metrics
            var gc0Rate = (double)(gen0After - gen0Before) / operations * 1000; // per 1000 ops
            var gc1Rate = (double)(gen1After - gen1Before) / operations * 1000;
            var gc2Rate = (double)(gen2After - gen2Before) / operations * 1000;
            var memoryUsed = (memoryAfter - memoryBefore) / (1024 * 1024);
            var opsPerSecond = (double)operations / stopwatch.ElapsedMilliseconds * 1000;

            // Assert
            Assert.True(gc0Rate < 10, $"Gen0 GC rate too high: {gc0Rate:F2} per 1000 ops");
            Assert.True(gc1Rate < 1, $"Gen1 GC rate too high: {gc1Rate:F2} per 1000 ops");
            Assert.True(gc2Rate < 0.1, $"Gen2 GC rate too high: {gc2Rate:F2} per 1000 ops");

            _output.WriteLine($"Memory Pool Stress Test:");
            _output.WriteLine($"Operations: {operations:N0}");
            _output.WriteLine($"Time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Throughput: {opsPerSecond:F0} ops/second");
            _output.WriteLine($"Memory used: {memoryUsed}MB");
            _output.WriteLine($"GC rates per 1000 ops - Gen0: {gc0Rate:F2}, Gen1: {gc1Rate:F2}, Gen2: {gc2Rate:F2}");
        }

        private record EndToEndBaseline
        {
            public int MaxSmallDatasetTimeMs { get; init; }
            public int SmallDatasetFileCount { get; init; }
            public int MaxMediumDatasetTimeMs { get; init; }
            public int MediumDatasetFileCount { get; init; }
            public int MaxLargeDatasetTimeMs { get; init; }
            public int LargeDatasetFileCount { get; init; }
            public int MaxMemoryUsageMB { get; init; }
            public double MinFilesPerSecond { get; init; }
            public double MinMBPerSecond { get; init; }
        }
    }
}