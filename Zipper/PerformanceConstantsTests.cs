using Xunit;

namespace Zipper
{
    public class PerformanceConstantsTests
    {
        [Fact]
        public void DefaultConcurrency_ShouldBePositive()
        {
            Assert.True(PerformanceConstants.DefaultConcurrency > 0);
        }

        [Fact]
        public void BufferSize_ShouldBePowerOfTwo()
        {
            var bufferSize = PerformanceConstants.DefaultBufferSize;
            Assert.True((bufferSize & (bufferSize - 1)) == 0); // Power of 2 check
        }
    }
}
