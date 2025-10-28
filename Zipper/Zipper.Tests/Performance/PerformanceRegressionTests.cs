using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests.Performance
{
    public class PerformanceRegressionTests
    {
        private readonly ITestOutputHelper _output;

        // Baseline performance targets (these should be conservative estimates)
        private static readonly PerformanceBaseline _baseline = new()
        {
            // File distribution: should handle 10,000 calculations in < 50ms
            MaxDistributionTimeMs = 50,

            // Email generation: should handle 1,000 templates in < 200ms
            MaxEmailGenerationTimeMs = 200,

            // Memory pool: should rent/return 1,000 buffers in < 10ms
            MaxMemoryPoolTimeMs = 10,

            // Command line validation: should validate 100 argument sets in < 5ms
            MaxValidationTimeMs = 5,

            // Progress tracking: should handle 10,000 updates in < 20ms
            MaxProgressTrackingTimeMs = 20,

            // Load file generation: should generate 1,000 entries in < 100ms
            MaxLoadFileGenerationTimeMs = 100,

            // Memory allocations should be minimal
            MaxMemoryAllocationsPerOperation = 1024, // 1KB per operation max

            // GC pressure should be low
            MaxGen0CollectionsPerOperation = 0.01m, // 1 collection per 100 operations
            MaxGen1CollectionsPerOperation = 0.001m, // 1 collection per 1000 operations
            MaxGen2CollectionsPerOperation = 0.0001m // 1 collection per 10000 operations
        };

        public PerformanceRegressionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void FileDistribution_PerformanceRegression_ShouldPassBaseline()
        {
            // Arrange
            const int iterations = 10000;
            const int folderCount = 100;

            // Act
            var stopwatch = Stopwatch.StartNew();
            long memoryBefore = GC.GetTotalMemory(true);

            for (int i = 0; i < iterations; i++)
            {
                var folder1 = FileDistributionHelper.GetFolderNumber(i, folderCount, "proportional");
                var folder2 = FileDistributionHelper.GetFolderNumber(i, folderCount, "gaussian");
                var folder3 = FileDistributionHelper.GetFolderNumber(i, folderCount, "exponential");
            }

            stopwatch.Stop();
            long memoryAfter = GC.GetTotalMemory(false);
            long memoryAllocated = memoryAfter - memoryBefore;

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxDistributionTimeMs,
                $"File distribution took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxDistributionTimeMs}ms");

            Assert.True(memoryAllocated < iterations * _baseline.MaxMemoryAllocationsPerOperation,
                $"File distribution allocated {memoryAllocated} bytes, expected < {iterations * _baseline.MaxMemoryAllocationsPerOperation} bytes");

            _output.WriteLine($"File Distribution: {iterations:N0} operations in {stopwatch.ElapsedMilliseconds}ms ({(double)iterations / stopwatch.ElapsedMilliseconds * 1000:F0} ops/sec)");
            _output.WriteLine($"Memory allocated: {memoryAllocated:N0} bytes ({(double)memoryAllocated / iterations:F2} bytes/op)");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void EmailGeneration_PerformanceRegression_ShouldPassBaseline()
        {
            // Arrange
            const int iterations = 1000;

            // Act
            var stopwatch = Stopwatch.StartNew();
            long memoryBefore = GC.GetTotalMemory(true);

            for (int i = 0; i < iterations; i++)
            {
                var config = new EmlGenerationConfig
                {
                    FileIndex = i,
                    AttachmentRate = 50,
                    Category = (EmailTemplateSystem.EmailCategory)(i % 6) // Rotate through categories
                };
                var result = EmlGenerationService.GenerateEmlContent(config);
            }

            stopwatch.Stop();
            long memoryAfter = GC.GetTotalMemory(false);
            long memoryAllocated = memoryAfter - memoryBefore;

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxEmailGenerationTimeMs,
                $"Email generation took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxEmailGenerationTimeMs}ms");

            _output.WriteLine($"Email Generation: {iterations:N0} operations in {stopwatch.ElapsedMilliseconds}ms ({(double)iterations / stopwatch.ElapsedMilliseconds * 1000:F0} ops/sec)");
            _output.WriteLine($"Memory allocated: {memoryAllocated:N0} bytes ({(double)memoryAllocated / iterations:F2} bytes/op)");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void MemoryPool_PerformanceRegression_ShouldPassBaseline()
        {
            // Arrange
            const int iterations = 1000;
            const int bufferSize = 81920; // 80KB

            // Act
            var stopwatch = Stopwatch.StartNew();
            long memoryBefore = GC.GetTotalMemory(true);

            using var manager = new MemoryPoolManager();
            for (int i = 0; i < iterations; i++)
            {
                var memory = manager.Rent(bufferSize);
                memory.Dispose();
            }

            stopwatch.Stop();
            long memoryAfter = GC.GetTotalMemory(false);
            long memoryAllocated = memoryAfter - memoryBefore;

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxMemoryPoolTimeMs,
                $"Memory pool operations took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxMemoryPoolTimeMs}ms");

            _output.WriteLine($"Memory Pool: {iterations:N0} rent/return in {stopwatch.ElapsedMilliseconds}ms ({(double)iterations / stopwatch.ElapsedMilliseconds * 1000:F0} ops/sec)");
            _output.WriteLine($"Memory allocated: {memoryAllocated:N0} bytes ({(double)memoryAllocated / iterations:F2} bytes/op)");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void CommandLineValidation_PerformanceRegression_ShouldPassBaseline()
        {
            // Arrange
            const int iterations = 100;
            var testArgs = new[]
            {
                new[] { "--type", "pdf", "--count", "1000", "--output-path", "/tmp/test" },
                new[] { "--type", "eml", "--count", "500", "--output-path", "/tmp/test", "--folders", "10" },
                new[] { "--type", "jpg", "--count", "2000", "--output-path", "/tmp/test", "--with-metadata" }
            };

            // Act
            var stopwatch = Stopwatch.StartNew();
            long memoryBefore = GC.GetTotalMemory(true);

            for (int i = 0; i < iterations; i++)
            {
                var args = testArgs[i % testArgs.Length];
                var result = CommandLineValidator.ValidateAndParseArguments(args);
            }

            stopwatch.Stop();
            long memoryAfter = GC.GetTotalMemory(false);
            long memoryAllocated = memoryAfter - memoryBefore;

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxValidationTimeMs,
                $"Command line validation took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxValidationTimeMs}ms");

            _output.WriteLine($"Validation: {iterations:N0} validations in {stopwatch.ElapsedMilliseconds}ms ({(double)iterations / stopwatch.ElapsedMilliseconds * 1000:F0} ops/sec)");
            _output.WriteLine($"Memory allocated: {memoryAllocated:N0} bytes ({(double)memoryAllocated / iterations:F2} bytes/op)");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void ProgressTracking_PerformanceRegression_ShouldPassBaseline()
        {
            // Arrange
            const int iterations = 10000;

            // Act
            var stopwatch = Stopwatch.StartNew();
            long memoryBefore = GC.GetTotalMemory(true);

            ProgressTracker.Initialize(iterations);
            for (int i = 0; i < iterations; i++)
            {
                ProgressTracker.ReportProgress();
            }

            stopwatch.Stop();
            long memoryAfter = GC.GetTotalMemory(false);
            long memoryAllocated = memoryAfter - memoryBefore;

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxProgressTrackingTimeMs,
                $"Progress tracking took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxProgressTrackingTimeMs}ms");

            _output.WriteLine($"Progress Tracking: {iterations:N0} updates in {stopwatch.ElapsedMilliseconds}ms ({(double)iterations / stopwatch.ElapsedMilliseconds * 1000:F0} ops/sec)");
            _output.WriteLine($"Memory allocated: {memoryAllocated:N0} bytes ({(double)memoryAllocated / iterations:F2} bytes/op)");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void LoadFileGeneration_PerformanceRegression_ShouldPassBaseline()
        {
            // Arrange
            const int fileCount = 1000;
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var loadFilePath = Path.Combine(tempDir, "test.dat");

            try
            {
                Directory.CreateDirectory(tempDir);

                // Act
                var stopwatch = Stopwatch.StartNew();
                long memoryBefore = GC.GetTotalMemory(true);

                ProgressTracker.Initialize(fileCount);
                LoadFileGenerator.CreateLoadFile(loadFilePath, "pdf", fileCount, "UTF-8");

                stopwatch.Stop();
                long memoryAfter = GC.GetTotalMemory(false);
                long memoryAllocated = memoryAfter - memoryBefore;

                // Assert
                Assert.True(stopwatch.ElapsedMilliseconds < _baseline.MaxLoadFileGenerationTimeMs,
                    $"Load file generation took {stopwatch.ElapsedMilliseconds}ms, expected < {_baseline.MaxLoadFileGenerationTimeMs}ms");

                Assert.True(File.Exists(loadFilePath), "Load file should exist");
                Assert.True(new FileInfo(loadFilePath).Length > 0, "Load file should have content");

                _output.WriteLine($"Load File Generation: {fileCount:N0} entries in {stopwatch.ElapsedMilliseconds}ms ({(double)fileCount / stopwatch.ElapsedMilliseconds * 1000:F0} ops/sec)");
                _output.WriteLine($"Memory allocated: {memoryAllocated:N0} bytes ({(double)memoryAllocated / fileCount:F2} bytes/op)");
                _output.WriteLine($"File size: {new FileInfo(loadFilePath).Length:N0} bytes");
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
        public void ComprehensivePerformanceTest_AllComponents_ShouldPassBaseline()
        {
            // Arrange
            const int iterations = 1000;

            // Act - Run all components together
            var stopwatch = Stopwatch.StartNew();
            long gen0Before = GC.CollectionCount(0);
            long gen1Before = GC.CollectionCount(1);
            long gen2Before = GC.CollectionCount(2);
            long memoryBefore = GC.GetTotalMemory(true);

            // File distribution
            for (int i = 0; i < iterations; i++)
            {
                FileDistributionHelper.GetFolderNumber(i, 100, "proportional");
            }

            // Email generation
            for (int i = 0; i < iterations / 10; i++)
            {
                var config = new EmlGenerationConfig
                {
                    FileIndex = i,
                    AttachmentRate = 50,
                    Category = EmailTemplateSystem.EmailCategory.Business
                };
                EmlGenerationService.GenerateEmlContent(config);
            }

            // Memory pool operations
            using var manager = new MemoryPoolManager();
            for (int i = 0; i < iterations; i++)
            {
                var memory = manager.Rent(1024);
                memory.Dispose();
            }

            // Progress tracking
            ProgressTracker.Initialize(iterations);
            for (int i = 0; i < iterations; i++)
            {
                ProgressTracker.ReportProgress();
            }

            stopwatch.Stop();
            long gen0After = GC.CollectionCount(0);
            long gen1After = GC.CollectionCount(1);
            long gen2After = GC.CollectionCount(2);
            long memoryAfter = GC.GetTotalMemory(false);

            // Calculate GC pressure
            var gen0Collections = gen0After - gen0Before;
            var gen1Collections = gen1After - gen1Before;
            var gen2Collections = gen2After - gen2Before;
            var totalOperations = iterations * 3.1; // Approximate total operations

            // Assert
            Assert.True(gen0Collections / totalOperations <= _baseline.MaxGen0CollectionsPerOperation,
                $"Gen0 collections rate too high: {gen0Collections}/{totalOperations} = {(double)gen0Collections / totalOperations:F4}");

            Assert.True(gen1Collections / totalOperations <= _baseline.MaxGen1CollectionsPerOperation,
                $"Gen1 collections rate too high: {gen1Collections}/{totalOperations} = {(double)gen1Collections / totalOperations:F4}");

            Assert.True(gen2Collections / totalOperations <= _baseline.MaxGen2CollectionsPerOperation,
                $"Gen2 collections rate too high: {gen2Collections}/{totalOperations} = {(double)gen2Collections / totalOperations:F4}");

            _output.WriteLine($"Comprehensive Performance Test Results:");
            _output.WriteLine($"Total time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"GC Collections - Gen0: {gen0Collections}, Gen1: {gen1Collections}, Gen2: {gen2Collections}");
            _output.WriteLine($"GC Rates per 1K ops - Gen0: {(double)gen0Collections / totalOperations * 1000:F2}, Gen1: {(double)gen1Collections / totalOperations * 1000:F2}, Gen2: {(double)gen2Collections / totalOperations * 1000:F2}");
            _output.WriteLine($"Memory allocated: {memoryAfter - memoryBefore:N0} bytes");
        }

        private record PerformanceBaseline
        {
            public int MaxDistributionTimeMs { get; init; }
            public int MaxEmailGenerationTimeMs { get; init; }
            public int MaxMemoryPoolTimeMs { get; init; }
            public int MaxValidationTimeMs { get; init; }
            public int MaxProgressTrackingTimeMs { get; init; }
            public int MaxLoadFileGenerationTimeMs { get; init; }
            public int MaxMemoryAllocationsPerOperation { get; init; }
            public decimal MaxGen0CollectionsPerOperation { get; init; }
            public decimal MaxGen1CollectionsPerOperation { get; init; }
            public decimal MaxGen2CollectionsPerOperation { get; init; }
        }
    }
}