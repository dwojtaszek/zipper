using System;
using Xunit;
using Zipper;

namespace Zipper.Tests
{
    public class ZipSizeVerifierTests
    {
        [Theory]
        // Exact boundaries (inclusive/exclusive)
        [InlineData(1000, 1000, true, 0.0)]
        [InlineData(1000, 1100, true, 0.10)]
        [InlineData(1000, 900, true, 0.10)]
        [InlineData(1000, 1101, false, 0.101)]
        [InlineData(1000, 899, false, 0.101)]
        // Zero target
        [InlineData(0, 1000, false, double.PositiveInfinity)]
        [InlineData(0, 0, true, 0.0)]
        // Target smaller than fixed overhead
        [InlineData(10, 1000, false, 99.0)]
        public void Verify_CalculatesToleranceCorrectly(long targetSize, long actualSize, bool expectedWithinTolerance, double expectedDeviation)
        {
            var result = ZipSizeVerifier.Verify(targetSize, actualSize);
            Assert.Equal(expectedWithinTolerance, result.IsWithinTolerance);
            Assert.Equal(expectedDeviation, result.Deviation, 5);
        }
    }
}
