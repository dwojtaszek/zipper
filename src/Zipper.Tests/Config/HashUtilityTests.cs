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
}
