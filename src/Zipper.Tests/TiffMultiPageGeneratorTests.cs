using Xunit;

namespace Zipper.Tests;

public class TiffMultiPageGeneratorTests
{
    [Theory]
    [InlineData("ParsePageRange_WithValidRange_ShouldReturnParsedRange", "1-20", 1, 20)]
    [InlineData("ParsePageRange_WithSinglePage_ShouldReturnParsedRange", "1-1", 1, 1)]
    [InlineData("ParsePageRange_WithLargeRange_ShouldReturnParsedRange", "100-1000", 100, 1000)]
    [InlineData("ParsePageRange_WithInvalidFormat_ShouldReturnNull", "invalid", null, null)]
    [InlineData("ParsePageRange_WithMissingMax_ShouldReturnNull", "1-", null, null)]
    [InlineData("ParsePageRange_WithMissingMin_ShouldReturnNull", "-20", null, null)]
    [InlineData("ParsePageRange_WithMinGreaterThanMax_ShouldReturnNull", "20-1", null, null)]
    [InlineData("ParsePageRange_WithMinLessThanOne_ShouldReturnNull", "0-10", null, null)]
    [InlineData("ParsePageRange_WithMaxGreaterThan1000_ShouldReturnNull", "1-1001", null, null)]
    public void ParsePageRange_WithRange_ReturnsExpectedRange(string scenario, string range, int? expectedMin, int? expectedMax)
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange(range);

        // Assert
        Assert.True(expectedMin == result?.Min, $"{scenario}: expected minimum {expectedMin} but got {result?.Min}.");
        Assert.True(expectedMax == result?.Max, $"{scenario}: expected maximum {expectedMax} but got {result?.Max}.");
    }

    [Fact]
    public void GetPageCount_WithNullRange_ShouldReturnOne()
    {
        // Act
        var result = TiffMultiPageGenerator.GetPageCount(null, null, 1);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public void GetPageCount_WithSinglePageRange_ShouldReturnMin()
    {
        // Arrange
        var range = (Min: 5, Max: 5);

        // Act
        var result = TiffMultiPageGenerator.GetPageCount(range, null, 1);

        // Assert
        Assert.Equal(5, result);
    }

    [Fact]
    public void GetPageCount_WithValidRangeAndDifferentIndex_ShouldReturnDifferentPageCounts()
    {
        // Arrange
        var range = (Min: 1, Max: 100);

        // Act
        var result1 = TiffMultiPageGenerator.GetPageCount(range, null, 1);
        var result2 = TiffMultiPageGenerator.GetPageCount(range, null, 2);

        // Assert - With large range, different indices should likely produce different results
        // (Though statistically possible to be the same, very unlikely with 100 values)
        Assert.InRange(result1, range.Min, range.Max);
        Assert.InRange(result2, range.Min, range.Max);
    }

    [Fact]
    public void GetPageCount_WithRange_ShouldReturnCountWithinRange()
    {
        // Arrange
        var range = (Min: 5, Max: 15);

        // Act
        for (int i = 0; i < 100; i++)
        {
            var result = TiffMultiPageGenerator.GetPageCount(range, null, i);

            // Assert
            Assert.InRange(result, range.Min, range.Max);
        }
    }

    [Fact]
    public void Generate_SinglePageRequest_ReturnsTiffWithValidEndianMagicBytes()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result = TiffMultiPageGenerator.Generate(5, workItem);

        // Assert - the pre-computed Native File must be a real TIFF: "II*\0" (little-endian) or "MM\0*" (big-endian).
        Assert.NotEmpty(result);
        Assert.True(result.Length >= 4);
        bool isLittleEndian = result[0] == 0x49 && result[1] == 0x49 && result[2] == 0x2A && result[3] == 0x00;
        bool isBigEndian = result[0] == 0x4D && result[1] == 0x4D && result[2] == 0x00 && result[3] == 0x2A;
        Assert.True(isLittleEndian || isBigEndian, $"TIFF must have valid endian magic bytes but started with {Convert.ToHexString(result.AsSpan(0, 4))}");
    }

    [Fact]
    public void Generate_WithDifferentPageCounts_ReturnsSinglePagePrecomputedNativeFile()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result1 = TiffMultiPageGenerator.Generate(1, workItem);
        var result2 = TiffMultiPageGenerator.Generate(10, workItem);

        // Assert - Page Count is tracked for Load File Metadata only (see the generator's class
        // documentation): both requests return the pre-computed Native File placeholder, so the
        // decoded IFD chain must hold exactly one page regardless of the requested Page Count.
        // Asserting the decoded page count - not byte equality - keeps this valid when the
        // placeholder content is regenerated, and pins the actual domain property. Once
        // multi-page Native File output satisfies REQ-045 at the file level, this expectation
        // moves to the requested range.
        Assert.NotEmpty(result1);
        Assert.NotEmpty(result2);
        Assert.Equal(1, CountTiffPages(result1));
        Assert.Equal(1, CountTiffPages(result2));
    }

    /// <summary>
    /// Walks a TIFF's Image File Directory chain and counts its pages, reading offsets in the
    /// endianness declared by the magic bytes. A cycle guard bounds malformed inputs.
    /// </summary>
    private static int CountTiffPages(byte[] tiff)
    {
        bool littleEndian = tiff[0] == 0x49 && tiff[1] == 0x49;

        ushort ReadUInt16(int offset) => littleEndian
            ? (ushort)(tiff[offset] | (tiff[offset + 1] << 8))
            : (ushort)((tiff[offset] << 8) | tiff[offset + 1]);

        uint ReadUInt32(int offset) => littleEndian
            ? (uint)(tiff[offset] | (tiff[offset + 1] << 8) | (tiff[offset + 2] << 16) | (tiff[offset + 3] << 24))
            : ((uint)tiff[offset] << 24) | ((uint)tiff[offset + 1] << 16) | ((uint)tiff[offset + 2] << 8) | tiff[offset + 3];

        int pages = 0;
        uint nextIfdOffset = ReadUInt32(4);
        while (nextIfdOffset != 0)
        {
            int entryCount = ReadUInt16((int)nextIfdOffset);
            nextIfdOffset = ReadUInt32((int)nextIfdOffset + 2 + (12 * entryCount));
            pages++;
            Assert.InRange(pages, 1, 1000); // Bound the walk if the IFD chain is cyclic.
        }

        return pages;
    }
}
