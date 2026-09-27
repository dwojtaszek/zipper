using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveTestCodecTests
{
    [Theory]
    [InlineData("stored", 0, 10, "Stored")]
    [InlineData("deflate", 8, 20, "Deflate")]
    [InlineData("deflate64", 9, 21, "Deflate64")]
    [InlineData("bzip2", 12, 46, "BZip2")]
    public void WireCode_KnownPayloadCodec_MapsHeaderVersionAndName(
        string payloadCodec, ushort wireCode, ushort versionNeeded, string displayName)
    {
        Assert.Equal(wireCode, ArchiveTestCodec.WireCode(payloadCodec));
        Assert.Equal(versionNeeded, ArchiveTestCodec.VersionNeeded(wireCode));
        Assert.Equal(displayName, ArchiveTestCodec.DisplayName(wireCode));
    }

    [Fact]
    public void DisplayName_UnsupportedPpmdCode_StillNamesTheMutation()
    {
        Assert.Equal("PPMd", ArchiveTestCodec.DisplayName(ArchiveTestCodec.Ppmd));
        Assert.Throws<InvalidOperationException>(() => ArchiveTestCodec.VersionNeeded(ArchiveTestCodec.Ppmd));
    }

    [Fact]
    public void WireCode_UnknownPayloadCodec_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ArchiveTestCodec.WireCode("unrecognized"));
    }
}
