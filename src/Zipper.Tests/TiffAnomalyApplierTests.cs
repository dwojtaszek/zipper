using System.Buffers.Binary;
using System.Globalization;
using Xunit;
using Zipper.Config;
using Zipper.Tests.TestHelpers;

namespace Zipper.Tests;

/// <summary>
/// Checks that each TIFF anomaly type changes the first Image File Directory in the way its audit entry describes,
/// and that a seed always yields the same bytes, for both byte orders.
/// </summary>
public class TiffAnomalyApplierTests
{
    private const string FileName = "DOC00000001.tiff";
    private const string RecordId = "DOC00000001";

    private static byte[] RealisticTiff(string byteOrder, int pageCount = 1) =>
        TiffRenderer.RenderMultiPage(pageCount, RecordId, new TiffConfig { RealismMode = "standard", ByteOrder = byteOrder, Compression = "group4" });

    private static TiffAnomalyResult Apply(byte[] tiff, string type, int seed = 1) =>
        TiffAnomalyApplier.Apply(tiff, FileName, RecordId, type, new Random(seed));

    private static long FirstValue(Dictionary<ushort, string> fields, ushort tag) =>
        long.Parse(fields[tag].Split(':')[2].Split(',')[0], CultureInfo.InvariantCulture);

    private static ushort ReadUInt16(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadUInt32(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    [Theory]
    [InlineData("tiff-dimension-lie", "ii")]
    [InlineData("tiff-dimension-lie", "mm")]
    [InlineData("tiff-truncated-ifd", "ii")]
    [InlineData("tiff-truncated-ifd", "mm")]
    [InlineData("tiff-tag-count-lie", "ii")]
    [InlineData("tiff-tag-count-lie", "mm")]
    [InlineData("tiff-strip-offset", "ii")]
    [InlineData("tiff-strip-offset", "mm")]
    public void Apply_SameSeed_ReturnsIdenticalBytesAndAudit(string type, string byteOrder)
    {
        var original = RealisticTiff(byteOrder);

        var first = Apply(original, type, seed: 7);
        var second = Apply(original, type, seed: 7);

        Assert.Equal(first.Content, second.Content);
        Assert.Equal(first.Anomaly, second.Anomaly);
        Assert.NotEqual(original, first.Content);
    }

    [Theory]
    [InlineData("tiff-dimension-lie")]
    [InlineData("tiff-truncated-ifd")]
    [InlineData("tiff-tag-count-lie")]
    [InlineData("tiff-strip-offset")]
    public void Apply_EachType_AuditsFileRecordTypeAndDirectoryLocation(string type)
    {
        var anomaly = Apply(RealisticTiff("ii"), type, seed: 3).Anomaly;

        Assert.Equal(FileName, anomaly.File);
        Assert.Equal(RecordId, anomaly.RecordID);
        Assert.Equal(type, anomaly.ErrorType);
        Assert.False(string.IsNullOrWhiteSpace(anomaly.Column));
        Assert.StartsWith("IFD 1", anomaly.Location, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(anomaly.Description));
    }

    [Theory]
    [InlineData("ii", 1)]
    [InlineData("ii", 2)]
    [InlineData("mm", 3)]
    [InlineData("mm", 4)]
    public void Apply_DimensionLie_WritesMaximumLongValueToTheAuditedDimension(string byteOrder, int seed)
    {
        var result = Apply(RealisticTiff(byteOrder), "tiff-dimension-lie", seed);
        var fields = TiffTestHelpers.ReadFirstIfdFields(result.Content);

        Assert.Contains(result.Anomaly.Column, new[] { "ImageWidth", "ImageLength" });
        ushort lieTag = result.Anomaly.Column == "ImageWidth" ? (ushort)256 : (ushort)257;
        Assert.Equal("4:1:4294967295", fields[lieTag]);
    }

    [Theory]
    [InlineData("ii", 1)]
    [InlineData("mm", 9)]
    public void Apply_TruncatedIfd_KeepsPrefixAndEndsInsideFirstDirectory(string byteOrder, int seed)
    {
        var original = RealisticTiff(byteOrder);
        var result = Apply(original, "tiff-truncated-ifd", seed);

        bool bigEndian = byteOrder == "mm";
        int ifdOffset = (int)ReadUInt32(original, 4, bigEndian);
        int declaredEnd = ifdOffset + 2 + (12 * ReadUInt16(original, ifdOffset, bigEndian)) + 4;

        Assert.True(result.Content.Length > ifdOffset + 2, "The cut must leave the entry count in place.");
        Assert.True(result.Content.Length < declaredEnd, "The directory must run past the end of the file.");
        Assert.Equal(original.AsSpan(0, result.Content.Length).ToArray(), result.Content);
    }

    [Theory]
    [InlineData("ii", 4)]
    [InlineData("mm", 5)]
    public void Apply_TagCountLie_DeclaresMoreEntriesAndChangesNothingElse(string byteOrder, int seed)
    {
        var original = RealisticTiff(byteOrder);
        var result = Apply(original, "tiff-tag-count-lie", seed);

        bool bigEndian = byteOrder == "mm";
        int ifdOffset = (int)ReadUInt32(original, 4, bigEndian);
        int realCount = ReadUInt16(original, ifdOffset, bigEndian);
        int declaredCount = ReadUInt16(result.Content, ifdOffset, bigEndian);

        Assert.InRange(declaredCount - realCount, 1, 4);
        Assert.Equal(original.Length, result.Content.Length);
        Assert.Equal(original.AsSpan(ifdOffset + 2).ToArray(), result.Content.AsSpan(ifdOffset + 2).ToArray());
    }

    [Theory]
    [InlineData("ii", 1)]
    [InlineData("ii", 2)]
    [InlineData("mm", 3)]
    [InlineData("mm", 4)]
    public void Apply_StripLie_RunsTheStripPastTheEndOfTheFile(string byteOrder, int seed)
    {
        var result = Apply(RealisticTiff(byteOrder), "tiff-strip-offset", seed);
        var fields = TiffTestHelpers.ReadFirstIfdFields(result.Content);

        Assert.Contains(result.Anomaly.Column, new[] { "StripOffsets", "StripByteCounts" });
        Assert.True(FirstValue(fields, 273) + FirstValue(fields, 279) > result.Content.Length);
    }

    [Theory]
    [InlineData("tiff-dimension-lie")]
    [InlineData("tiff-truncated-ifd")]
    [InlineData("tiff-tag-count-lie")]
    [InlineData("tiff-strip-offset")]
    public void Apply_PlaceholderTiff_MutatesEveryType(string type)
    {
        var placeholder = PlaceholderFiles.GetContent("tiff");

        var result = Apply(placeholder, type, seed: 11);

        Assert.NotEqual(placeholder, result.Content);
    }

    [Fact]
    public void Apply_UnknownType_Throws()
    {
        Assert.Throws<ArgumentException>(() => Apply(RealisticTiff("ii"), "tiff-unknown"));
    }

    [Fact]
    public void Apply_NotTiffBytes_Throws()
    {
        Assert.Throws<ArgumentException>(() => Apply(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, "tiff-dimension-lie"));
    }
}
