using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class HashUtilityTests
{
    [Theory]
    [InlineData("MD5HASH", HashAlgorithm.MD5)]
    [InlineData("MD5_HASH", HashAlgorithm.MD5)]
    [InlineData("MD5 HASH", HashAlgorithm.MD5)]
    [InlineData("SHA1HASH", HashAlgorithm.SHA1)]
    [InlineData("SHA1_HASH", HashAlgorithm.SHA1)]
    [InlineData("SHA1 HASH", HashAlgorithm.SHA1)]
    [InlineData("SHA256HASH", HashAlgorithm.SHA256)]
    [InlineData("SHA256_HASH", HashAlgorithm.SHA256)]
    [InlineData("SHA256 HASH", HashAlgorithm.SHA256)]
    [InlineData("AUTHOR", null)]
    public void GetColumnAlgorithm_KnownAndUnknownNames_ReturnsExpectedAlgorithm(
        string upperColumnName,
        HashAlgorithm? expected)
    {
        Assert.Equal(expected, HashUtility.GetColumnAlgorithm(upperColumnName));
    }

    [Fact]
    public void ComputeSimulatedHashesIfEnabled_RespectsEnabledAndMode()
    {
        var workItem = new FileWorkItem { Index = 1 };

        var disabledRequest = new FileGenerationRequest
        {
            Metadata = new MetadataConfig { Seed = 42 },
            Hash = new HashConfig { Mode = HashMode.Simulated },
        };
        Assert.Null(HashComputer.ComputeSimulatedHashesIfEnabled(workItem, disabledRequest));

        var actualRequest = new FileGenerationRequest
        {
            Metadata = new MetadataConfig { Seed = 42 },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<HashAlgorithm> { HashAlgorithm.MD5 },
            },
        };
        Assert.Null(HashComputer.ComputeSimulatedHashesIfEnabled(workItem, actualRequest));

        var simulatedRequest = new FileGenerationRequest
        {
            Metadata = new MetadataConfig { Seed = 42 },
            Hash = new HashConfig
            {
                Mode = HashMode.Simulated,
                Algorithms = new HashSet<HashAlgorithm> { HashAlgorithm.MD5, HashAlgorithm.SHA256 },
            },
        };
        var first = HashComputer.ComputeSimulatedHashesIfEnabled(workItem, simulatedRequest);
        var second = HashComputer.ComputeSimulatedHashesIfEnabled(workItem, simulatedRequest);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first![HashAlgorithm.MD5], second![HashAlgorithm.MD5]);
        Assert.Equal(first[HashAlgorithm.SHA256], second[HashAlgorithm.SHA256]);
    }
}
