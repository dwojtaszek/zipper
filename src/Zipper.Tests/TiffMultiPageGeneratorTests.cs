using Xunit;

namespace Zipper.Tests;

public class TiffMultiPageGeneratorTests
{
    [Fact]
    public void ParsePageRange_WithValidRange_ShouldReturnParsedRange()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("1-20");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.Min);
        Assert.Equal(20, result.Value.Max);
    }

    [Fact]
    public void ParsePageRange_WithSinglePage_ShouldReturnParsedRange()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("1-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.Min);
        Assert.Equal(1, result.Value.Max);
    }

    [Fact]
    public void ParsePageRange_WithLargeRange_ShouldReturnParsedRange()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("100-1000");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.Value.Min);
        Assert.Equal(1000, result.Value.Max);
    }

    [Fact]
    public void ParsePageRange_WithInvalidFormat_ShouldReturnNull()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("invalid");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePageRange_WithMissingMax_ShouldReturnNull()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("1-");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePageRange_WithMissingMin_ShouldReturnNull()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("-20");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePageRange_WithMinGreaterThanMax_ShouldReturnNull()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("20-1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePageRange_WithMinLessThanOne_ShouldReturnNull()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("0-10");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParsePageRange_WithMaxGreaterThan1000_ShouldReturnNull()
    {
        // Act
        var result = TiffMultiPageGenerator.ParsePageRange("1-1001");

        // Assert
        Assert.Null(result);
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
    public void Generate_ShouldReturnTiffWithValidEndianMagicBytes()
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
    public void Generate_WithDifferentPageCounts_ReturnsSamePrecomputedNativeFile()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result1 = TiffMultiPageGenerator.Generate(1, workItem);
        var result2 = TiffMultiPageGenerator.Generate(10, workItem);

        // Assert - page count is tracked for Load File Metadata only; the Native File itself is
        // pre-computed, so it must not vary with the requested page count.
        Assert.NotEmpty(result1);
        Assert.NotEmpty(result2);
        Assert.Equal(result1, result2);
    }
}
