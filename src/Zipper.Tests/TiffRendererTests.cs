using Xunit;
using Zipper.Config;
using Zipper.Tests.TestHelpers;

namespace Zipper.Tests;

/// <summary>
/// Known-answer and decoder checks for <see cref="TiffRenderer"/>: the CCITT T.4/T.6 bit patterns
/// for the run lengths the endorsement band produces, the endorsement raster for long labels,
/// and pixel-level decoding of realistic Group 4 output with an external TIFF decoder.
/// </summary>
public class TiffRendererTests
{
    private const string EndOfFacsimileBlock = "000000000001000000000001";

    [Fact]
    public void CompressGroup4_WhiteRunOf1728_UsesNineBitMakeupCode()
    {
        // 1728 white pixels followed by 16 black pixels that run to the right edge.
        const int width = 1744;
        var raster = BuildSingleRowRaster(width, blackStart: 1728, blackEnd: 1744);

        var encoded = TiffRenderer.CompressGroup4(raster, width, 1);

        // Horizontal mode (001), white make-up 1728 (010011011) + white terminating 0 (00110101),
        // black terminating 16 (0000010111).
        var expected = "001" + "010011011" + "00110101" + "0000010111" + EndOfFacsimileBlock;
        Assert.Equal(PackBits(expected), encoded);
    }

    [Fact]
    public void CompressGroup4_BlackRunOf4_UsesThreeBitTerminatingCode()
    {
        const int width = 20;
        var raster = BuildSingleRowRaster(width, blackStart: 0, blackEnd: 4);

        var encoded = TiffRenderer.CompressGroup4(raster, width, 1);

        // Horizontal mode (001), white terminating 0 (00110101), black terminating 4 (011),
        // vertical V0 (1) back to the right edge.
        var expected = "001" + "00110101" + "011" + "1" + EndOfFacsimileBlock;
        Assert.Equal(PackBits(expected), encoded);
    }

    [Fact]
    public void CompressGroup4_BlackRunOf640_UsesBlackMakeupCode()
    {
        const int width = 700;
        var raster = BuildSingleRowRaster(width, blackStart: 0, blackEnd: 640);

        var encoded = TiffRenderer.CompressGroup4(raster, width, 1);

        // Horizontal mode (001), white terminating 0 (00110101), black make-up 640 (0000001001010)
        // + black terminating 0 (0000110111), vertical V0 (1) back to the right edge.
        var expected = "001" + "00110101" + "0000001001010" + "0000110111" + "1" + EndOfFacsimileBlock;
        Assert.Equal(PackBits(expected), encoded);
    }

    [Theory]
    [InlineData(1280, "0000001010010")]
    [InlineData(1728, "0000001100101")]
    public void CompressGroup4_BlackRunAboveMakeupTable_UsesThirteenBitMakeupCode(int run, string makeupCode)
    {
        // Black make-up codes above 1216 are not covered by the other known-answer tests.
        int width = run + 16;
        var raster = BuildSingleRowRaster(width, blackStart: 0, blackEnd: run);

        var encoded = TiffRenderer.CompressGroup4(raster, width, 1);

        // Horizontal mode (001), white terminating 0 (00110101), black make-up (13 bits)
        // + black terminating 0 (0000110111), vertical V0 (1) back to the right edge.
        var expected = "001" + "00110101" + makeupCode + "0000110111" + "1" + EndOfFacsimileBlock;
        Assert.Equal(PackBits(expected), encoded);
    }

    [Fact]
    public void CreatePageRaster_WithLongBatesLabel_KeepsEveryCharacterOnPage()
    {
        // Labels wider than the endorsement band used to clip their trailing characters, so
        // consecutive page labels such as "<prefix>_001" and "<prefix>_002" rendered identical pages.
        var prefix = new string('A', 96);

        var page1 = TiffRenderer.CreatePageRaster(prefix + "_001", TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);
        var page2 = TiffRenderer.CreatePageRaster(prefix + "_002", TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);

        Assert.NotEqual(page1, page2);
    }

    [SkippableTheory]
    [InlineData(18)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(100)]
    public void GeneratePage_WithLabelLength_DecodesToSamePixelsAsUncompressed(int labelLength)
    {
        var magick = TiffTestHelpers.FindToolOnPath("magick");
        Skip.If(magick is null, "ImageMagick (magick) is not installed");

        var label = new string('A', labelLength - 4) + "_001";
        Assert.Equal(labelLength, label.Length);

        var group4 = TiffMultiPageGenerator.GeneratePage(label, 1, 1, new TiffConfig { RealismMode = "standard", Compression = "group4" });
        var uncompressed = TiffMultiPageGenerator.GeneratePage(label, 1, 1, new TiffConfig { RealismMode = "standard", Compression = "uncompressed" });

        var tempDir = Path.Combine(Path.GetTempPath(), $"tiff_decode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var group4Path = Path.Combine(tempDir, "group4.tif");
            var uncompressedPath = Path.Combine(tempDir, "uncompressed.tif");
            File.WriteAllBytes(group4Path, group4);
            File.WriteAllBytes(uncompressedPath, uncompressed);

            // ImageMagick decodes both files through libtiff and prints the pixel error count to stderr.
            // Any decoder warning (for example "Bad code word") makes stderr differ from "0 (0)".
            var result = TiffTestHelpers.RunTool(magick!, "compare", "-metric", "AE", group4Path, uncompressedPath, "null:");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("0 (0)", result.StdErr.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static byte[] BuildSingleRowRaster(int width, int blackStart, int blackEnd)
    {
        var raster = new byte[(width + 7) / 8];
        for (int x = blackStart; x < blackEnd; x++)
        {
            raster[x >> 3] |= (byte)(0x80 >> (x & 7));
        }

        return raster;
    }

    private static byte[] PackBits(string bits)
    {
        var padded = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
        var bytes = new byte[padded.Length / 8];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(padded.Substring(i * 8, 8), 2);
        }

        return bytes;
    }
}
