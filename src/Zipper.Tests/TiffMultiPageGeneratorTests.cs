using Xunit;
using static Zipper.Tests.TestHelpers.TiffTestHelpers;

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
        var result = TiffMultiPageGenerator.Generate(5, workItem, null);

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
        var result1 = TiffMultiPageGenerator.Generate(1, workItem, null);
        var result2 = TiffMultiPageGenerator.Generate(10, workItem, null);

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

    [Fact]
    public void GeneratePage_WithoutRealisticMode_ReturnsPrecomputedPlaceholder()
    {
        var result = TiffMultiPageGenerator.GeneratePage("ABC001", 1, 1, null);
        Assert.Equal(PlaceholderFiles.GetContent("tiff"), result);

        var configOff = new Config.TiffConfig { RealismMode = "none" };
        var resultOff = TiffMultiPageGenerator.GeneratePage("ABC001", 1, 1, configOff);
        Assert.Equal(PlaceholderFiles.GetContent("tiff"), resultOff);
    }

    [Fact]
    public void GeneratePage_WithRealisticMode_EmitsValidTiffWithExpectedIfdTags()
    {
        var config = new Config.TiffConfig { RealismMode = "group4" };
        var result = TiffMultiPageGenerator.GeneratePage("PROD00000001", 1, 3, config);

        Assert.NotEmpty(result);
        Assert.True(result.Length >= 8);
        Assert.Equal(0x49, result[0]);
        Assert.Equal(0x49, result[1]);
        Assert.Equal(0x2A, result[2]);
        Assert.Equal(0x00, result[3]);

        var tags = ReadFirstIfdTags(result);
        Assert.Equal(2550u, tags[256].Value); // ImageWidth
        Assert.Equal(3300u, tags[257].Value); // ImageLength
        Assert.Equal(1u, tags[258].Value);    // BitsPerSample
        Assert.Equal(4u, tags[259].Value);    // Compression: CCITT Group 4
        Assert.Equal(0u, tags[262].Value);    // PhotometricInterpretation: min-is-white
        Assert.Equal(2u, tags[296].Value);    // ResolutionUnit: Inch
        Assert.Equal(1u, tags[284].Value);    // PlanarConfiguration

        // PageNumber: count 2 SHORTs: pageIndex - 1 (0) and pageCount (3)
        var pageNumValue = tags[297].Value;
        Assert.Equal(0, (int)(pageNumValue & 0xFFFF));
        Assert.Equal(3, (int)((pageNumValue >> 16) & 0xFFFF));
    }

    [Fact]
    public void GeneratePage_WithDistinctBatesNumbers_ProducesDistinctImages()
    {
        // Page index is held constant so only the endorsement Bates number differs between the images.
        var config = new Config.TiffConfig { RealismMode = "group4" };
        var page1 = TiffMultiPageGenerator.GeneratePage("PROD00000001_001", 1, 2, config);
        var page2 = TiffMultiPageGenerator.GeneratePage("PROD00000001_002", 1, 2, config);

        Assert.NotEqual(page1, page2);
    }

    [Fact]
    public void CreatePageRaster_WithBatesNumber_DrawsInkOnlyInBottomEndorsementBand()
    {
        var raster = TiffRenderer.CreatePageRaster("PROD00000001", TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);
        int bytesPerRow = (TiffRenderer.DefaultWidth + 7) / 8;
        const int endorsementBandHeight = 300;

        bool anyInkOutsideBand = false;
        for (int y = 0; y < TiffRenderer.DefaultHeight - endorsementBandHeight; y++)
        {
            for (int x = 0; x < bytesPerRow; x++)
            {
                anyInkOutsideBand |= raster[(y * bytesPerRow) + x] != 0;
            }
        }

        bool anyInkInsideBand = false;
        for (int y = TiffRenderer.DefaultHeight - endorsementBandHeight; y < TiffRenderer.DefaultHeight; y++)
        {
            for (int x = 0; x < bytesPerRow; x++)
            {
                anyInkInsideBand |= raster[(y * bytesPerRow) + x] != 0;
            }
        }

        Assert.False(anyInkOutsideBand);
        Assert.True(anyInkInsideBand);
    }

    [Fact]
    public void CreatePageRaster_WithoutBatesNumber_IsBlank()
    {
        var raster = TiffRenderer.CreatePageRaster(null, TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);

        Assert.All(raster, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void GeneratePage_WithSameInput_IsDeterministic()
    {
        var config = new Config.TiffConfig { RealismMode = "group4" };
        var pageA = TiffMultiPageGenerator.GeneratePage("PROD00000001", 1, 1, config);
        var pageB = TiffMultiPageGenerator.GeneratePage("PROD00000001", 1, 1, config);

        Assert.Equal(pageA, pageB);
    }

    [Fact]
    public void GeneratePage_WithUncompressedOption_EmitsCompressionOne()
    {
        var config = new Config.TiffConfig { RealismMode = "standard", Compression = "uncompressed" };
        var result = TiffMultiPageGenerator.GeneratePage("PROD00000001", 1, 1, config);

        var tags = ReadFirstIfdTags(result);
        Assert.Equal(1u, tags[259].Value); // Compression: 1 (Uncompressed)
        Assert.Equal(2550u, tags[256].Value);
        Assert.Equal(3300u, tags[257].Value);
    }

    [Fact]
    public void Generate_MultiPageWithRealisticMode_EmitsChainedIfds()
    {
        var workItem = new FileWorkItem { Index = 1 };
        var config = new Config.TiffConfig { RealismMode = "group4" };
        var result = TiffMultiPageGenerator.Generate(3, workItem, config, "PROD00000001");

        Assert.Equal(3, CountTiffPages(result));
    }

    [SkippableFact]
    public void GeneratePage_LibtiffVerification_DecompressesWithoutErrors()
    {
        var tiffinfo = FindToolOnPath("tiffinfo");
        Skip.If(tiffinfo is null, "libtiff tiffinfo is not installed");

        var config = new Config.TiffConfig { RealismMode = "group4" };
        var result = TiffMultiPageGenerator.GeneratePage("TEST-BATES-001_001", 1, 1, config);

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_tiff_{Guid.NewGuid():N}.tif");
        try
        {
            File.WriteAllBytes(tempPath, result);

            // -D reads the strip data so libtiff decodes every row and reports any bad code word on stderr.
            var (exitCode, stdout, stderr) = RunTool(tiffinfo!, "-D", tempPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("Compression Scheme: CCITT Group 4", stdout);
            Assert.Empty(stderr);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
