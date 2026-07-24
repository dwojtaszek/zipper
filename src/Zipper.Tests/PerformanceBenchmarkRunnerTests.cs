using Xunit;

namespace Zipper.Tests;

public class PerformanceBenchmarkRunnerTests
{
    [Fact]
    public async Task RunBenchmarks_CompletesWithoutThrowingAndReturnsReport()
    {
        using var writer = new StringWriter();
        var report = await PerformanceBenchmarkRunner.RunBenchmarksAsync(writer);

        Assert.NotNull(report);
        Assert.Equal(4, report.Metrics.Count);

        var output = writer.ToString();
        Assert.Contains("=== Performance Benchmark Suite ===", output, StringComparison.Ordinal);
        Assert.Contains("=== Overall Benchmark Status:", output, StringComparison.Ordinal);
        Assert.Contains("=== Benchmark Suite Complete ===", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunBenchmarks_EmitsPassFailVerdictsForEveryReq104Metric()
    {
        using var writer = new StringWriter();
        var report = await PerformanceBenchmarkRunner.RunBenchmarksAsync(writer);

        var expectedMetricNames = new[]
        {
            "Parallel vs Sequential Generation Throughput",
            "Memory Pool Effectiveness",
            "Scalability Across File Counts",
            "Allocation Overhead",
        };

        foreach (var name in expectedMetricNames)
        {
            var metric = Assert.Single(report.Metrics, m => m.MetricName == name);
            Assert.False(string.IsNullOrWhiteSpace(metric.Details));
        }

        var output = writer.ToString();
        Assert.Contains("Status:", output, StringComparison.Ordinal);
        Assert.Contains("PASS", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateParallelVsSequential_WithValidSpeedup_ShouldReturnPass()
    {
        var verdict = PerformanceBenchmarkRunner.EvaluateParallelVsSequential(100, 50);

        Assert.Equal("Parallel vs Sequential Generation Throughput", verdict.MetricName);
        Assert.True(verdict.Passed);
        Assert.Contains("Speedup: 2.00x", verdict.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateParallelVsSequential_WithLowSpeedup_ShouldReturnFail()
    {
        var verdict = PerformanceBenchmarkRunner.EvaluateParallelVsSequential(10, 100);

        Assert.False(verdict.Passed);
        Assert.Contains("Speedup: 0.10x", verdict.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateMemoryPooling_WithPositiveReduction_ShouldReturnPass()
    {
        var verdict = PerformanceBenchmarkRunner.EvaluateMemoryPooling(100, 1000, 80, 500);

        Assert.Equal("Memory Pool Effectiveness", verdict.MetricName);
        Assert.True(verdict.Passed);
        Assert.Contains("Memory Reduction: 50.0%", verdict.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateMemoryPooling_WithNegativeReductionAndLowSpeedup_ShouldReturnFail()
    {
        var verdict = PerformanceBenchmarkRunner.EvaluateMemoryPooling(10, 100, 100, 200);

        Assert.False(verdict.Passed);
        Assert.Contains("Time Speedup: 0.10x", verdict.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateScalability_WithValidSteps_ShouldReturnPass()
    {
        var steps = new[]
        {
            new PerformanceBenchmarkRunner.ScalabilityStepResult(100, 10, 10000.0, 0.1),
            new PerformanceBenchmarkRunner.ScalabilityStepResult(500, 40, 12500.0, 0.08),
        };

        var verdict = PerformanceBenchmarkRunner.EvaluateScalability(steps);

        Assert.Equal("Scalability Across File Counts", verdict.MetricName);
        Assert.True(verdict.Passed);
    }

    [Fact]
    public void EvaluateScalability_WithEmptySteps_ShouldReturnFail()
    {
        var verdict = PerformanceBenchmarkRunner.EvaluateScalability(Array.Empty<PerformanceBenchmarkRunner.ScalabilityStepResult>());

        Assert.False(verdict.Passed);
    }

    [Fact]
    public void EvaluateScalability_WithNullSteps_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PerformanceBenchmarkRunner.EvaluateScalability(null!));
    }

    [Fact]
    public void EvaluateAllocation_WithValidAllocations_ShouldReturnPass()
    {
        var verdict = PerformanceBenchmarkRunner.EvaluateAllocation(1000, 50000);

        Assert.Equal("Allocation Overhead", verdict.MetricName);
        Assert.True(verdict.Passed);
        Assert.Contains("50 bytes/file", verdict.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateAllocation_WithZeroAllocations_ShouldReturnFail()
    {
        var verdict = PerformanceBenchmarkRunner.EvaluateAllocation(1000, 0);

        Assert.False(verdict.Passed);
    }
}
