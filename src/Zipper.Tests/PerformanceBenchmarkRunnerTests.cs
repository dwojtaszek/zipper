using Xunit;

namespace Zipper.Tests;

public class PerformanceBenchmarkRunnerTests
{
    [Fact]
    public async Task RunBenchmarks_CompletesWithoutThrowing()
    {
        var exception = await Record.ExceptionAsync(() => PerformanceBenchmarkRunner.RunBenchmarks());
        Assert.Null(exception);
    }
}
