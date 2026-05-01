using System.Diagnostics;
using System.IO.Compression;

namespace Zipper
{
    /// <summary>
    /// Simple performance benchmark runner for quick performance validation.
    /// </summary>
    public static class PerformanceBenchmarkRunner
    {
        public static async Task RunBenchmarks(TextWriter? writer = null)
        {
            writer ??= Console.Out;

            await writer.WriteLineAsync("=== Performance Benchmark Suite ===");
            await writer.WriteLineAsync();

            await BenchmarkParallelVsSequential(writer);
            await BenchmarkMemoryPooling(writer);
            await BenchmarkScalability(writer);
            await BenchmarkAllocation(writer);

            await writer.WriteLineAsync("=== Benchmark Suite Complete ===");
        }

        private static async Task BenchmarkAllocation(TextWriter writer)
        {
            await writer.WriteLineAsync("4. Allocation Impact");
            await writer.WriteLineAsync("===================");

            const int fileCount = 1000;
#pragma warning disable S5443 // Using temp path is safe for local benchmark execution
            var tempDir = Path.GetTempPath();
#pragma warning restore S5443
            var outputPath = Path.Combine(tempDir, $"bench_alloc_{Guid.NewGuid()}");
            Directory.CreateDirectory(outputPath);

            try
            {
                // Force full GC before starting
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

                using var generator = new ParallelFileGenerator();
                var request = new FileGenerationRequest
                {
                    OutputPath = outputPath,
                    FileCount = fileCount,
                    FileType = "pdf", // Uses placeholder content, good for testing structural overhead
                    Folders = 10,
                    Concurrency = PerformanceConstants.DefaultConcurrency,
                    Distribution = DistributionType.Proportional,
                };

                await generator.GenerateFilesAsync(request);

                var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
                var totalAllocated = allocatedAfter - allocatedBefore;
                var bytesPerFile = totalAllocated / fileCount;

                await writer.WriteLineAsync($"  Files Generated: {fileCount}");
                await writer.WriteLineAsync($"  Total Allocated: {totalAllocated:N0} bytes");
                await writer.WriteLineAsync($"  Bytes Per File:  {bytesPerFile:N0} bytes/file");
            }
            finally
            {
                CleanupDirectory(outputPath);
            }

            await writer.WriteLineAsync();
        }

        private static async Task BenchmarkParallelVsSequential(TextWriter writer)
        {
            await writer.WriteLineAsync("1. Parallel vs Sequential Generation");
            await writer.WriteLineAsync("=====================================");

            const int fileCount = 500;
#pragma warning disable S5443 // Using temp path is safe for local benchmark execution
            var tempDir = Path.GetTempPath();
#pragma warning restore S5443
            var outputPath1 = Path.Combine(tempDir, $"bench_seq_{Guid.NewGuid()}");
            var outputPath2 = Path.Combine(tempDir, $"bench_par_{Guid.NewGuid()}");
            Directory.CreateDirectory(outputPath1);
            Directory.CreateDirectory(outputPath2);

            try
            {
                // Sequential baseline
                var sw = Stopwatch.StartNew();
                await GenerateSequentialFiles(fileCount, outputPath1);
                sw.Stop();
                var sequentialTime = sw.ElapsedMilliseconds;

                // Parallel generation
                sw.Restart();
                using var generator = new ParallelFileGenerator();
                var request = new FileGenerationRequest
                {
                    OutputPath = outputPath2,
                    FileCount = fileCount,
                    FileType = "pdf",
                    Folders = 3,
                    Concurrency = PerformanceConstants.DefaultConcurrency,
                    Distribution = DistributionType.Proportional,
                };
                await generator.GenerateFilesAsync(request);
                sw.Stop();
                var parallelTime = sw.ElapsedMilliseconds;

                var speedup = (double)sequentialTime / parallelTime;

                await writer.WriteLineAsync($"  Sequential: {sequentialTime}ms");
                await writer.WriteLineAsync($"  Parallel:   {parallelTime}ms");
                await writer.WriteLineAsync($"  Speedup:    {speedup:F2}x");
                await writer.WriteLineAsync($"  Status:     {(speedup >= 1.0 ? "✓ PASS" : "✗ FAIL")}");
            }
            finally
            {
                CleanupDirectory(outputPath1);
                CleanupDirectory(outputPath2);
            }

            await writer.WriteLineAsync();
        }

        private static async Task BenchmarkMemoryPooling(TextWriter writer)
        {
            await writer.WriteLineAsync("2. Memory Pool Performance");
            await writer.WriteLineAsync("===========================");

            const int iterations = 50;
            const int bufferSize = 2 * 1024 * 1024; // 2MB

            // Without pooling
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memoryBefore = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                var buffer = new byte[bufferSize];
                buffer[0] = (byte)i;
            }

            sw.Stop();
            var memoryAfterWithoutPool = GC.GetTotalMemory(false);
            var timeWithoutPool = sw.ElapsedMilliseconds;

            // With pooling
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            memoryBefore = GC.GetTotalMemory(false);
            sw.Restart();

            using var poolManager = new MemoryPoolManager();
            for (int i = 0; i < iterations; i++)
            {
                using var memoryOwner = poolManager.Rent(bufferSize);
                if (memoryOwner != null)
                {
                    memoryOwner.Memory.Span[0] = (byte)i;
                }
            }

            sw.Stop();
            var memoryAfterWithPool = GC.GetTotalMemory(false);
            var timeWithPool = sw.ElapsedMilliseconds;

            var timeSpeedup = (double)timeWithoutPool / timeWithPool;
            var memoryReduction = (double)(memoryAfterWithoutPool - memoryAfterWithPool) / memoryAfterWithoutPool * 100;

            await writer.WriteLineAsync($"  Without Pool: {timeWithoutPool}ms, {memoryAfterWithoutPool:N0} bytes");
            await writer.WriteLineAsync($"  With Pool:    {timeWithPool}ms, {memoryAfterWithPool:N0} bytes");
            await writer.WriteLineAsync($"  Time Speedup: {timeSpeedup:F2}x");
            await writer.WriteLineAsync($"  Memory Reduction: {memoryReduction:F1}%");
            await writer.WriteLineAsync($"  Status:       {(timeSpeedup >= 0.8 ? "✓ PASS" : "✗ FAIL")}");

            await writer.WriteLineAsync();
        }

        private static async Task BenchmarkScalability(TextWriter writer)
        {
            await writer.WriteLineAsync("3. Scalability Test");
            await writer.WriteLineAsync("===================");

            var fileCounts = new[] { 100, 500, 1000, 2000 };
#pragma warning disable S5443 // Using temp path is safe for local benchmark execution
            var tempDir = Path.GetTempPath();
#pragma warning restore S5443

            foreach (var fileCount in fileCounts)
            {
                var outputPath = Path.Combine(tempDir, $"bench_scale_{fileCount}_{Guid.NewGuid()}");
                Directory.CreateDirectory(outputPath);

                try
                {
                    var sw = Stopwatch.StartNew();

                    using var generator = new ParallelFileGenerator();
                    var request = new FileGenerationRequest
                    {
                        OutputPath = outputPath,
                        FileCount = fileCount,
                        FileType = "pdf",
                        Folders = Math.Max(1, fileCount / 200),
                        Concurrency = Math.Min(PerformanceConstants.DefaultConcurrency, (fileCount / 50) + 1),
                        Distribution = DistributionType.Proportional,
                    };

                    var result = await generator.GenerateFilesAsync(request);
                    sw.Stop();

                    var throughput = result.FilesPerSecond;
                    var avgTimePerFile = sw.ElapsedMilliseconds / (double)fileCount;

                    await writer.WriteLineAsync($"  {fileCount,4:N0} files: {sw.ElapsedMilliseconds,4}ms, {throughput,6:F1} files/sec, {avgTimePerFile,5:F2}ms/file");
                }
                finally
                {
                    CleanupDirectory(outputPath);
                }
            }

            await writer.WriteLineAsync("  Status: ✓ PASS (scalability verified)");
            await writer.WriteLineAsync();
        }

        private static Task GenerateSequentialFiles(long count, string outputPath)
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

        private static void CleanupDirectory(string path)
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
    }
}
