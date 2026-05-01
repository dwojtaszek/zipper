using Xunit;

namespace Zipper.Tests;

public class PerformanceBenchmarkRunnerTests
{
    [Fact]
    public async Task RunBenchmarks_CompletesWithoutThrowing()
    {
        var exception = await Record.ExceptionAsync(() => PerformanceBenchmarkRunner.RunBenchmarks(new StringWriter()));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RunBenchmarks_WritesAllFourSections()
    {
        var output = await CaptureOutputAsync();

        Assert.Contains("Parallel vs Sequential", output);
        Assert.Contains("Memory Pool", output);
        Assert.Contains("Scalability", output);
        Assert.Contains("Allocation", output);
        Assert.Contains("Benchmark Suite Complete", output);
    }

    [Fact]
    public async Task RunBenchmarks_PrintsTimingMetrics()
    {
        var output = await CaptureOutputAsync();

        Assert.Contains("ms", output);
        Assert.Contains("files/sec", output);
        Assert.Contains("Status", output);
    }

    private static async Task<string> CaptureOutputAsync()
    {
        var writer = new StringWriter();
        await PerformanceBenchmarkRunner.RunBenchmarks(writer);
        return writer.ToString();
    }
}
