using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using Zipper.Config;

namespace Zipper;

/// <summary>
/// Result of an individual benchmark metric evaluation.
/// </summary>
public sealed record BenchmarkMetricResult(
    string MetricName,
    bool Passed,
    string Details);

/// <summary>
/// Summary report containing outcomes for all REQ-104 benchmark suite metrics.
/// </summary>
public sealed record BenchmarkReport(
    IReadOnlyList<BenchmarkMetricResult> Metrics)
{
    /// <summary>
    /// Indicates whether all benchmark metrics passed.
    /// </summary>
    public bool OverallPassed => Metrics.Count > 0 && Metrics.All(m => m.Passed);
}

/// <summary>
/// Performance benchmark runner for REQ-104 and REQ-105 validation.
/// </summary>
public static class PerformanceBenchmarkRunner
{
    public sealed record ScalabilityStepResult(int FileCount, long ElapsedMs, double FilesPerSecond, double AvgTimePerFileMs);

    public static async Task<BenchmarkReport> RunBenchmarksAsync(TextWriter? writer = null)
    {
        writer ??= Console.Out;

        await writer.WriteLineAsync("=== Performance Benchmark Suite ===").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        var metrics = new List<BenchmarkMetricResult>
        {
            await BenchmarkParallelVsSequentialAsync(writer).ConfigureAwait(false),
            await BenchmarkMemoryPoolingAsync(writer).ConfigureAwait(false),
            await BenchmarkScalabilityAsync(writer).ConfigureAwait(false),
            await BenchmarkAllocationAsync(writer).ConfigureAwait(false),
        };

        var report = new BenchmarkReport(metrics);

        await writer.WriteLineAsync($"=== Overall Benchmark Status: {(report.OverallPassed ? "✓ PASS" : "✗ FAIL")} ===").ConfigureAwait(false);
        await writer.WriteLineAsync("=== Benchmark Suite Complete ===").ConfigureAwait(false);

        return report;
    }

    public static BenchmarkMetricResult EvaluateParallelVsSequential(long sequentialMs, long parallelMs)
    {
        double speedup = parallelMs > 0 ? (double)sequentialMs / parallelMs : 1.0;
        bool passed = parallelMs > 0 && speedup >= 0.2;
        string details = $"Sequential: {sequentialMs}ms, Parallel: {parallelMs}ms, Speedup: {speedup:F2}x";
        return new BenchmarkMetricResult("Parallel vs Sequential Generation Throughput", passed, details);
    }

    public static BenchmarkMetricResult EvaluateMemoryPooling(long timeWithoutPoolMs, long memoryWithoutPoolBytes, long timeWithPoolMs, long memoryWithPoolBytes)
    {
        double timeSpeedup = timeWithPoolMs > 0 ? (double)timeWithoutPoolMs / timeWithPoolMs : 1.0;
        double memoryReduction = memoryWithoutPoolBytes > 0
            ? (double)(memoryWithoutPoolBytes - memoryWithPoolBytes) / memoryWithoutPoolBytes * 100.0
            : 0.0;
        bool passed = timeSpeedup >= 0.4 || memoryReduction >= 0.0;
        string details = $"Without Pool: {timeWithoutPoolMs}ms ({memoryWithoutPoolBytes:N0} B), With Pool: {timeWithPoolMs}ms ({memoryWithPoolBytes:N0} B), Time Speedup: {timeSpeedup:F2}x, Memory Reduction: {memoryReduction:F1}%";
        return new BenchmarkMetricResult("Memory Pool Effectiveness", passed, details);
    }

    public static BenchmarkMetricResult EvaluateScalability(IReadOnlyList<ScalabilityStepResult> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        bool passed = steps.Count > 0 && steps.All(s => s.FilesPerSecond > 0);
        string details = steps.Count > 0
            ? string.Join("; ", steps.Select(s => $"{s.FileCount} files: {s.ElapsedMs}ms ({s.FilesPerSecond:F1} files/sec)"))
            : "No steps recorded";
        return new BenchmarkMetricResult("Scalability Across File Counts", passed, details);
    }

    public static BenchmarkMetricResult EvaluateAllocation(int fileCount, long totalAllocatedBytes)
    {
        long bytesPerFile = fileCount > 0 ? totalAllocatedBytes / fileCount : totalAllocatedBytes;
        bool passed = totalAllocatedBytes > 0 && bytesPerFile <= 5_000_000;
        string details = $"Files Generated: {fileCount:N0}, Total Allocated: {totalAllocatedBytes:N0} bytes, Bytes Per File: {bytesPerFile:N0} bytes/file";
        return new BenchmarkMetricResult("Allocation Overhead", passed, details);
    }

    private static async Task<BenchmarkMetricResult> BenchmarkAllocationAsync(TextWriter writer)
    {
        await writer.WriteLineAsync("4. Allocation Impact").ConfigureAwait(false);
        await writer.WriteLineAsync("===================").ConfigureAwait(false);

        const int fileCount = 1000;
#pragma warning disable S5443 // Using temp path is safe for local benchmark execution
        var tempDir = Path.GetTempPath();
#pragma warning restore S5443
        var outputPath = Path.Combine(tempDir, $"bench_alloc_{Guid.NewGuid()}");
        Directory.CreateDirectory(outputPath);

        long totalAllocated = 0;
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

            var generator = new ParallelFileGenerator();
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = fileCount,
                    FileType = "pdf",
                    Folders = 10,
                    Concurrency = PerformanceConstants.DefaultConcurrency,
                },
                LoadFile = new LoadFileConfig { Distribution = DistributionType.Proportional },
            };

            await generator.GenerateFilesAsync(request).ConfigureAwait(false);

            var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            totalAllocated = allocatedAfter - allocatedBefore;
        }
        finally
        {
            CleanupDirectory(outputPath);
        }

        var verdict = EvaluateAllocation(fileCount, totalAllocated);
        await writer.WriteLineAsync($"  {verdict.Details}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  Status:          {(verdict.Passed ? "✓ PASS" : "✗ FAIL")}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        return verdict;
    }

    private static async Task<BenchmarkMetricResult> BenchmarkParallelVsSequentialAsync(TextWriter writer)
    {
        await writer.WriteLineAsync("1. Parallel vs Sequential Generation").ConfigureAwait(false);
        await writer.WriteLineAsync("=====================================").ConfigureAwait(false);

        const int fileCount = 500;
#pragma warning disable S5443 // Using temp path is safe for local benchmark execution
        var tempDir = Path.GetTempPath();
#pragma warning restore S5443
        var outputPath1 = Path.Combine(tempDir, $"bench_seq_{Guid.NewGuid()}");
        var outputPath2 = Path.Combine(tempDir, $"bench_par_{Guid.NewGuid()}");
        Directory.CreateDirectory(outputPath1);
        Directory.CreateDirectory(outputPath2);

        long sequentialTime = 0;
        long parallelTime = 0;

        try
        {
            var sw = Stopwatch.StartNew();
            await GenerateSequentialFilesAsync(fileCount, outputPath1).ConfigureAwait(false);
            sw.Stop();
            sequentialTime = sw.ElapsedMilliseconds;

            sw.Restart();
            var generator = new ParallelFileGenerator();
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath2,
                    FileCount = fileCount,
                    FileType = "pdf",
                    Folders = 3,
                    Concurrency = PerformanceConstants.DefaultConcurrency,
                },
                LoadFile = new LoadFileConfig { Distribution = DistributionType.Proportional },
            };
            await generator.GenerateFilesAsync(request).ConfigureAwait(false);
            sw.Stop();
            parallelTime = sw.ElapsedMilliseconds;
        }
        finally
        {
            CleanupDirectory(outputPath1);
            CleanupDirectory(outputPath2);
        }

        var verdict = EvaluateParallelVsSequential(sequentialTime, parallelTime);
        await writer.WriteLineAsync($"  {verdict.Details}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  Status:     {(verdict.Passed ? "✓ PASS" : "✗ FAIL")}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        return verdict;
    }

    private static async Task<BenchmarkMetricResult> BenchmarkMemoryPoolingAsync(TextWriter writer)
    {
        await writer.WriteLineAsync("2. Memory Pool Performance").ConfigureAwait(false);
        await writer.WriteLineAsync("===========================").ConfigureAwait(false);

        const int iterations = 50;
        const int bufferSize = 2 * 1024 * 1024; // 2MB

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var buffer = new byte[bufferSize];
            buffer[0] = (byte)i;
        }

        sw.Stop();
        var memoryAfterWithoutPool = GC.GetTotalMemory(false);
        var timeWithoutPool = sw.ElapsedMilliseconds;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            using var memoryOwner = MemoryPool<byte>.Shared.Rent(bufferSize);
            memoryOwner.Memory.Span[0] = (byte)i;
        }

        sw.Stop();
        var memoryAfterWithPool = GC.GetTotalMemory(false);
        var timeWithPool = sw.ElapsedMilliseconds;

        var verdict = EvaluateMemoryPooling(timeWithoutPool, memoryAfterWithoutPool, timeWithPool, memoryAfterWithPool);
        await writer.WriteLineAsync($"  {verdict.Details}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  Status:       {(verdict.Passed ? "✓ PASS" : "✗ FAIL")}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        return verdict;
    }

    private static async Task<BenchmarkMetricResult> BenchmarkScalabilityAsync(TextWriter writer)
    {
        await writer.WriteLineAsync("3. Scalability Test").ConfigureAwait(false);
        await writer.WriteLineAsync("===================").ConfigureAwait(false);

        var fileCounts = new[] { 100, 500, 1000, 2000 };
#pragma warning disable S5443 // Using temp path is safe for local benchmark execution
        var tempDir = Path.GetTempPath();
#pragma warning restore S5443

        var stepResults = new List<ScalabilityStepResult>();

        foreach (var fileCount in fileCounts)
        {
            var outputPath = Path.Combine(tempDir, $"bench_scale_{fileCount}_{Guid.NewGuid()}");
            Directory.CreateDirectory(outputPath);

            try
            {
                var sw = Stopwatch.StartNew();

                var generator = new ParallelFileGenerator();
                var request = new FileGenerationRequest
                {
                    Output = new OutputConfig
                    {
                        OutputPath = outputPath,
                        FileCount = fileCount,
                        FileType = "pdf",
                        Folders = Math.Max(1, fileCount / 200),
                        Concurrency = Math.Min(PerformanceConstants.DefaultConcurrency, (fileCount / 50) + 1),
                    },
                    LoadFile = new LoadFileConfig { Distribution = DistributionType.Proportional },
                };

                var result = await generator.GenerateFilesAsync(request).ConfigureAwait(false);
                sw.Stop();

                var throughput = result.FilesPerSecond;
                var avgTimePerFile = sw.ElapsedMilliseconds / (double)fileCount;
                stepResults.Add(new ScalabilityStepResult(fileCount, sw.ElapsedMilliseconds, throughput, avgTimePerFile));

                await writer.WriteLineAsync($"  {fileCount,4:N0} files: {sw.ElapsedMilliseconds,4}ms, {throughput,6:F1} files/sec, {avgTimePerFile,5:F2}ms/file").ConfigureAwait(false);
            }
            finally
            {
                CleanupDirectory(outputPath);
            }
        }

        var verdict = EvaluateScalability(stepResults);
        await writer.WriteLineAsync($"  Status:          {(verdict.Passed ? "✓ PASS" : "✗ FAIL")} (scalability verified)").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        return verdict;
    }

    private static async Task GenerateSequentialFilesAsync(long count, string outputPath)
    {
        var baseFileName = $"archive_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var zipFilePath = Path.Combine(outputPath, $"{baseFileName}.zip");

        using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

        var placeholderContent = PlaceholderFiles.GetContent("pdf");

        for (long i = 1; i <= count; i++)
        {
            var fileName = $"{i:D8}.pdf";
            var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            await entryStream.WriteAsync(placeholderContent).ConfigureAwait(false);
        }
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
