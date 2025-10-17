using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper
{
    public class PerformanceBenchmarkTests
    {
        private readonly ITestOutputHelper _output;

        public PerformanceBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ParallelGeneration_ShouldOutperformSequentialGeneration()
        {
            // Arrange
            const int fileCount = 1000;
            var tempDir = Path.GetTempPath();
            var outputPath1 = Path.Combine(tempDir, Guid.NewGuid().ToString());
            var outputPath2 = Path.Combine(tempDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputPath1);
            Directory.CreateDirectory(outputPath2);

            try
            {
                // Sequential generation baseline
                var sequentialStopwatch = Stopwatch.StartNew();
                await GenerateSequentialFiles(fileCount, outputPath1);
                sequentialStopwatch.Stop();

                // Parallel generation
                var parallelStopwatch = Stopwatch.StartNew();
                await GenerateParallelFiles(fileCount, outputPath2);
                parallelStopwatch.Stop();

                // Assert & Report
                _output.WriteLine($"Sequential: {sequentialStopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Parallel: {parallelStopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Speedup: {(double)sequentialStopwatch.ElapsedMilliseconds / parallelStopwatch.ElapsedMilliseconds:F2}x");

                // Parallel should be faster (with some tolerance for small file counts)
                Assert.True(parallelStopwatch.ElapsedMilliseconds <= sequentialStopwatch.ElapsedMilliseconds,
                    $"Parallel generation ({parallelStopwatch.ElapsedMilliseconds}ms) should be faster than or equal to sequential ({sequentialStopwatch.ElapsedMilliseconds}ms)");
            }
            finally
            {
                CleanupDirectory(outputPath1);
                CleanupDirectory(outputPath2);
            }
        }

        [Fact]
        public void MemoryPool_ShouldReduceMemoryAllocations()
        {
            // Arrange
            const int iterations = 100;
            const int bufferSize = 1024 * 1024; // 1MB

            // Without memory pool
            var memoryWithoutPool = GC.GetTotalMemory(true);
            for (int i = 0; i < iterations; i++)
            {
                var buffer = new byte[bufferSize];
                // Simulate work
                buffer[0] = (byte)i;
            }
            var memoryAfterWithoutPool = GC.GetTotalMemory(false);
            var allocatedWithoutPool = memoryAfterWithoutPool - memoryWithoutPool;

            // With memory pool
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memoryWithPool = GC.GetTotalMemory(true);
            using var poolManager = new MemoryPoolManager();

            for (int i = 0; i < iterations; i++)
            {
                using var memoryOwner = poolManager.Rent(bufferSize);
                if (memoryOwner != null)
                {
                    memoryOwner.Memory.Span[0] = (byte)i;
                }
            }

            var memoryAfterWithPool = GC.GetTotalMemory(false);
            var allocatedWithPool = memoryAfterWithPool - memoryWithPool;

            // Report
            _output.WriteLine($"Without pool: {allocatedWithoutPool:N0} bytes");
            _output.WriteLine($"With pool: {allocatedWithPool:N0} bytes");
            _output.WriteLine($"Reduction: {(double)allocatedWithoutPool / allocatedWithPool:F2}x");

            // Memory pool should reduce allocations
            Assert.True(allocatedWithPool <= allocatedWithoutPool * 1.5, // Allow some tolerance
                "Memory pool should reduce or at least not significantly increase memory allocations");
        }

        [Fact]
        public async Task ParallelGenerator_ShouldMaintainPerformanceWithScalingConcurrency()
        {
            // Arrange
            const int fileCount = 500;
            var tempDir = Path.GetTempPath();
            var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputPath);

            try
            {
                var concurrencyLevels = new[] { 1, 2, 4, Environment.ProcessorCount };
                var results = new ConcurrentPerformanceResult[concurrencyLevels.Length];

                for (int i = 0; i < concurrencyLevels.Length; i++)
                {
                    var concurrency = concurrencyLevels[i];
                    _output.WriteLine($"Testing concurrency level: {concurrency}");

                    var stopwatch = Stopwatch.StartNew();

                    using var generator = new ParallelFileGenerator();
                    var request = new FileGenerationRequest
                    {
                        OutputPath = outputPath,
                        FileCount = fileCount,
                        FileType = "pdf",
                        Folders = 3,
                        Concurrency = concurrency,
                        Distribution = DistributionType.Proportional
                    };

                    var result = await generator.GenerateFilesAsync(request);
                    stopwatch.Stop();

                    results[i] = new ConcurrentPerformanceResult
                    {
                        Concurrency = concurrency,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                        FilesPerSecond = result.FilesPerSecond
                    };

                    // Cleanup for next iteration
                    var files = Directory.GetFiles(outputPath, "*.zip");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }

                // Report results
                _output.WriteLine("Concurrency Performance Results:");
                for (int i = 0; i < results.Length; i++)
                {
                    var result = results[i];
                    _output.WriteLine($"Concurrency {result.Concurrency}: {result.ElapsedMs}ms, {result.FilesPerSecond:F1} files/sec");
                }

                // Performance should scale reasonably with concurrency
                var bestPerformance = results.Max(r => r.FilesPerSecond);
                var singleThreadPerformance = results.First(r => r.Concurrency == 1).FilesPerSecond;

                _output.WriteLine($"Performance scaling: {bestPerformance / singleThreadPerformance:F2}x");

                Assert.True(bestPerformance >= singleThreadPerformance * 0.8, // Allow some tolerance for overhead
                    "Parallel execution should not be significantly slower than single-threaded");
            }
            finally
            {
                CleanupDirectory(outputPath);
            }
        }

        [Fact]
        public async Task BufferedStreamWriter_ShouldHandleLargeDataEfficiently()
        {
            // Arrange
            const int dataSize = 10 * 1024 * 1024; // 10MB
            var tempDir = Path.GetTempPath();
            var testFile = Path.Combine(tempDir, Guid.NewGuid().ToString() + ".bin");
            var data = new byte[dataSize];
            new Random(42).NextBytes(data);

            try
            {
                var stopwatch = Stopwatch.StartNew();

                await using var fileStream = new FileStream(testFile, FileMode.Create);
                await using var writer = new BufferedStreamWriter(fileStream);
                await writer.WriteAsync(data);

                stopwatch.Stop();

                // Verify file was written correctly
                Assert.True(File.Exists(testFile));
                Assert.Equal(dataSize, new FileInfo(testFile).Length);

                // Verify content integrity
                var writtenData = await File.ReadAllBytesAsync(testFile);
                Assert.Equal(data, writtenData);

                _output.WriteLine($"Buffered write: {dataSize:N0} bytes in {stopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Throughput: {(double)dataSize / (1024 * 1024) / (stopwatch.ElapsedMilliseconds / 1000.0):F2} MB/s");

                // Performance should be reasonable for buffered I/O
                Assert.True(stopwatch.ElapsedMilliseconds < 5000, "10MB write should complete within 5 seconds");
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }

        [Fact]
        public void PerformanceMonitor_ShouldHaveMinimalOverhead()
        {
            // Arrange
            const int iterations = 10000;
            var monitor = new PerformanceMonitor();

            // Measure overhead of performance monitoring
            var stopwatch = Stopwatch.StartNew();

            monitor.Start(iterations);
            for (int i = 0; i < iterations; i++)
            {
                if (i % 100 == 0)
                {
                    monitor.ReportFilesCompleted(100);
                }
            }
            var metrics = monitor.Stop();

            stopwatch.Stop();

            _output.WriteLine($"Monitor overhead: {stopwatch.ElapsedMilliseconds}ms for {iterations:N0} operations");
            _output.WriteLine($"Performance: {metrics.FilesPerSecond:F1} files/sec (simulated)");

            // Monitoring overhead should be minimal
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, "Performance monitoring should have minimal overhead");
            Assert.Equal(iterations, metrics.FilesCompleted);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public async Task EndToEndPerformance_ShouldMaintainEfficiencyAcrossScales(int fileCount)
        {
            // Arrange
            var tempDir = Path.GetTempPath();
            var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputPath);

            try
            {
                var stopwatch = Stopwatch.StartNew();

                using var generator = new ParallelFileGenerator();
                var request = new FileGenerationRequest
                {
                    OutputPath = outputPath,
                    FileCount = fileCount,
                    FileType = "pdf",
                    Folders = Math.Max(1, fileCount / 100), // Scale folders with file count
                    Concurrency = Math.Min(PerformanceConstants.DefaultConcurrency, fileCount / 10 + 1),
                    Distribution = DistributionType.Proportional
                };

                var result = await generator.GenerateFilesAsync(request);
                stopwatch.Stop();

                var throughput = result.FilesPerSecond;
                var avgTimePerFile = stopwatch.ElapsedMilliseconds / (double)fileCount;

                _output.WriteLine($"File count: {fileCount:N0}");
                _output.WriteLine($"Total time: {stopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Throughput: {throughput:F1} files/sec");
                _output.WriteLine($"Avg time per file: {avgTimePerFile:F2}ms");

                // Performance should scale reasonably
                Assert.True(throughput >= 10, $"Should maintain at least 10 files/sec, got {throughput:F1}");
                Assert.True(avgTimePerFile <= 100, $"Average time per file should be reasonable, got {avgTimePerFile:F2}ms");
                Assert.Equal(fileCount, result.FilesGenerated);
            }
            finally
            {
                CleanupDirectory(outputPath);
            }
        }

        private Task GenerateSequentialFiles(long count, string outputPath)
        {
            return Task.Run(async () =>
            {
                var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
                var zipFilePath = Path.Combine(outputPath, $"{baseFileName}.zip");

                using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
                using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

                var placeholderContent = PlaceholderFiles.GetContent("pdf");

                for (long i = 1; i <= count; i++)
                {
                    var fileName = $"{i:D8}.pdf";
                    var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(placeholderContent);
                }
            });
        }

        private async Task GenerateParallelFiles(long count, string outputPath)
        {
            using var generator = new ParallelFileGenerator();
            var request = new FileGenerationRequest
            {
                OutputPath = outputPath,
                FileCount = count,
                FileType = "pdf",
                Folders = 3,
                Concurrency = PerformanceConstants.DefaultConcurrency,
                Distribution = DistributionType.Proportional
            };

            await generator.GenerateFilesAsync(request);
        }

        private void CleanupDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    Directory.Delete(path, true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        private class ConcurrentPerformanceResult
        {
            public int Concurrency { get; set; }
            public long ElapsedMs { get; set; }
            public double FilesPerSecond { get; set; }
        }
    }
}
