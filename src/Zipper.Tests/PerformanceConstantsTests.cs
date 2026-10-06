using Xunit;

namespace Zipper.Tests;

public class PerformanceConstantsTests
{
    [Fact]
    public void DefaultConcurrency_ShouldEqualHalfTheProcessorCountFloorOne()
    {
        // Half the processors keeps one core free for the OS, but never drops below a single worker.
        Assert.Equal(Math.Max(1, Environment.ProcessorCount / 2), PerformanceConstants.DefaultConcurrency);
    }

    [Fact]
    public void DefaultBufferSize_ShouldEqualSixtyFourKilobytes()
    {
        Assert.Equal(65536, PerformanceConstants.DefaultBufferSize);
    }
}
