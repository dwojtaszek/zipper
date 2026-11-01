using System.Buffers.Binary;
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

    [Theory]
    [InlineData(-3, "0000010")]
    [InlineData(-2, "000010")]
    [InlineData(-1, "010")]
    [InlineData(0, "1")]
    [InlineData(1, "011")]
    [InlineData(2, "000011")]
    [InlineData(3, "0000011")]
    public void CompressGroup4_BlackRunOffsetFromReferenceLine_UsesVerticalModeCode(int offset, string verticalCode)
    {
        // Row 0 has a black run at [10, 20). Row 1 starts that run `offset` pixels away, so its first
        // changing element is coded in vertical mode against the reference line.
        const int width = 40;
        var raster = BuildSingleRowRaster(width, blackStart: 10, blackEnd: 20)
            .Concat(BuildSingleRowRaster(width, blackStart: 10 + offset, blackEnd: 20))
            .ToArray();

        var encoded = TiffRenderer.CompressGroup4(raster, width, 2);

        // Row 0: horizontal (001), white terminating 10 (00111), black terminating 10 (0000100), V0 (1).
        // Row 1: the vertical code for the offset, V0 (1) at the end of the black run, V0 (1) at the right edge.
        var expected = "001" + "00111" + "0000100" + "1" + verticalCode + "1" + "1" + EndOfFacsimileBlock;
        Assert.Equal(PackBits(expected), encoded);
    }

    [Fact]
    public void CreatePageRaster_WithFourHundredCharacterLabel_KeepsLastCharacterOnPage()
    {
        // REQ-238 documents labels up to 400 characters as unclipped. At that length the last glyph is the
        // last one that fits on the page, so changing only that character must change the raster.
        var stem = new string('A', 399);

        var underscore = TiffRenderer.CreatePageRaster(stem + "_", TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);
        var letterB = TiffRenderer.CreatePageRaster(stem + "B", TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);

        Assert.NotEqual(underscore, letterB);
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

    [Fact]
    public void CreatePageRaster_WithNonAsciiCharacter_BurnsQuestionMarkGlyph()
    {
        // REQ-238: the endorsement font covers printable ASCII only. Any other character burns as '?'.
        var questionMark = TiffRenderer.CreatePageRaster("?", TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);
        var blank = TiffRenderer.CreatePageRaster(null, TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);
        var eAcute = TiffRenderer.CreatePageRaster("\u00e9", TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);

        Assert.NotEqual(blank, questionMark);
        Assert.Equal(questionMark, eAcute);
    }

    [Fact]
    public void CreatePageRaster_WithAsciiBoundaryCharacters_BurnsFontGlyphs()
    {
        // Space (32) is printable but its glyph is empty, so it burns nothing. Tilde (126) is the last
        // printable glyph and must not fall back to '?'.
        Assert.Equal(Raster(null), Raster(" "));
        Assert.NotEqual(Raster("?"), Raster("~"));
    }

    [Fact]
    public void CreatePageRaster_WithLabelWiderThanPage_LeavesPaddingBitsWhite()
    {
        // REQ-238 documents 400 characters as the unclipped limit. One more character runs past the right edge,
        // so its pixels must be dropped rather than written into the unused low bits of each row's last byte.
        const int width = TiffRenderer.DefaultWidth;
        const int height = TiffRenderer.DefaultHeight;
        var raster = TiffRenderer.CreatePageRaster(new string('A', 401), width, height);

        int bytesPerRow = (width + 7) / 8;
        byte paddingMask = (byte)((1 << ((bytesPerRow * 8) - width)) - 1);
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, raster[((y + 1) * bytesPerRow) - 1] & paddingMask);
        }
    }

    [Fact]
    public void RenderMultiPage_SinglePageWithBates_EndorsesWithBatesNumberOnly()
    {
        // A one-page file carries the Bates number itself. Only multi-page files add the _NNN suffix.
        var tiff = TiffRenderer.RenderMultiPage(1, "PROD00000001", new TiffConfig { Compression = "uncompressed" });

        Assert.True(ContainsRaster(tiff, Raster("PROD00000001")));
        Assert.False(ContainsRaster(tiff, Raster("PROD00000001_001")));
    }

    [Fact]
    public void RenderMultiPage_MultiplePages_EndorsesEachPageWithPageSuffix()
    {
        var tiff = TiffRenderer.RenderMultiPage(2, "PROD", new TiffConfig { Compression = "uncompressed" });

        Assert.True(ContainsRaster(tiff, Raster("PROD_001")));
        Assert.True(ContainsRaster(tiff, Raster("PROD_002")));
    }

    [Fact]
    public void RenderMultiPage_WithoutBatesNumber_LeavesPagesUnendorsed()
    {
        // A blank page is all white, so a labelled page would not contain its raster.
        var tiff = TiffRenderer.RenderMultiPage(2, null, new TiffConfig { Compression = "uncompressed" });

        Assert.True(ContainsRaster(tiff, Raster(null)));
        Assert.False(ContainsRaster(tiff, Raster("_001")));
    }

    [Fact]
    public void RenderMultiPage_UncompressedStrip_StartsAtWordAlignedOffset()
    {
        // The Software string is odd-length, so the strip is padded to an even offset before its raster.
        var raster = Raster("PROD");
        var tiff = TiffRenderer.RenderMultiPage(1, "PROD", new TiffConfig { Compression = "uncompressed" });

        uint stripOffset = ReadStripOffset(tiff, ifdOffset: 8);

        Assert.Equal(0u, stripOffset % 2);
        Assert.True(tiff.AsSpan((int)stripOffset, raster.Length).SequenceEqual(raster));
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CalculateUncompressedTiffSize_MatchesRenderedOutput(int pageCount)
    {
        long expectedSize = TiffRenderer.CalculateUncompressedTiffSize(pageCount);
        var tiff = TiffRenderer.RenderMultiPage(pageCount, "PROD00000001", new TiffConfig { Compression = "uncompressed" });

        Assert.Equal(expectedSize, tiff.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalculateUncompressedTiffSize_NonPositive_ReturnsZero(int pageCount)
    {
        Assert.Equal(0, TiffRenderer.CalculateUncompressedTiffSize(pageCount));
    }

    [Theory]
    [InlineData("group4")]
    [InlineData("uncompressed")]
    public void RenderMultiPage_ToStream_MatchesByteArrayOutput(string compression)
    {
        var config = new TiffConfig { RealismMode = "standard", Compression = compression };
        var directBytes = TiffRenderer.RenderMultiPage(2, "PROD00000001", config);

        using var ms = new MemoryStream();
        TiffRenderer.RenderMultiPage(ms, 2, "PROD00000001", config);

        Assert.Equal(directBytes, ms.ToArray());
    }

    [Theory]
    [InlineData("group4")]
    [InlineData("uncompressed")]
    public void RenderPage_ToStream_MatchesByteArrayOutput(string compression)
    {
        var config = new TiffConfig { RealismMode = "standard", Compression = compression };
        var directBytes = TiffRenderer.RenderPage("PROD00000001", 1, 1, config);

        using var ms = new MemoryStream();
        TiffRenderer.RenderPage(ms, "PROD00000001", 1, 1, config);

        Assert.Equal(directBytes, ms.ToArray());
    }

    [Fact]
    public void RenderMultiPage_ExorbitantUncompressedPageCount_ThrowsInvalidOperationException()
    {
        const int tooManyPages = 3000; // 3000 * 1,052,922 > Array.MaxLength
        var config = new TiffConfig { RealismMode = "standard", Compression = "uncompressed" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TiffRenderer.RenderMultiPage(tooManyPages, "PROD", config));

        Assert.Contains("exceeds maximum supported buffer size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TiffMultiPageGenerator_GenerateToStream_MatchesByteArrayOutput(bool isRealistic)
    {
        var config = isRealistic ? new TiffConfig { RealismMode = "standard", Compression = "group4" } : null;
        var workItem = new FileWorkItem { Index = 1 };
        var directBytes = TiffMultiPageGenerator.Generate(2, workItem, config, "PROD00000001");

        using var ms = new MemoryStream();
        TiffMultiPageGenerator.Generate(ms, 2, workItem, config, "PROD00000001");

        Assert.Equal(directBytes, ms.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TiffMultiPageGenerator_GeneratePageToStream_MatchesByteArrayOutput(bool isRealistic)
    {
        var config = isRealistic ? new TiffConfig { RealismMode = "standard", Compression = "group4" } : null;
        var directBytes = TiffMultiPageGenerator.GeneratePage("PROD00000001", 1, 1, config);

        using var ms = new MemoryStream();
        TiffMultiPageGenerator.GeneratePage(ms, "PROD00000001", 1, 1, config);

        Assert.Equal(directBytes, ms.ToArray());
    }

    [Theory]
    [InlineData("group4")]
    [InlineData("uncompressed")]
    public void RenderMultiPage_ToNonSeekableStream_ProducesIdenticalOutput(string compression)
    {
        var config = new TiffConfig { RealismMode = "standard", Compression = compression };
        var directBytes = TiffRenderer.RenderMultiPage(2, "PROD00000001", config);

        using var backing = new MemoryStream();
        using (var nonSeekable = new NonSeekableStream(backing))
        {
            TiffRenderer.RenderMultiPage(nonSeekable, 2, "PROD00000001", config);
        }

        Assert.Equal(directBytes, backing.ToArray());
    }

    [Theory]
    [InlineData("group4")]
    [InlineData("uncompressed")]
    public void RenderMultiPage_ToStreamWithPriorContent_ProducesIdenticalTiffPayload(string compression)
    {
        var config = new TiffConfig { RealismMode = "standard", Compression = compression };
        var directBytes = TiffRenderer.RenderMultiPage(2, "PROD00000001", config);

        byte[] prefix = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03];
        using var ms = new MemoryStream();
        ms.Write(prefix, 0, prefix.Length);

        TiffRenderer.RenderMultiPage(ms, 2, "PROD00000001", config);

        var result = ms.ToArray();
        Assert.Equal(prefix, result.Take(prefix.Length).ToArray());
        Assert.Equal(directBytes, result.Skip(prefix.Length).ToArray());
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    private static byte[] Raster(string? label)
        => TiffRenderer.CreatePageRaster(label, TiffRenderer.DefaultWidth, TiffRenderer.DefaultHeight);

    private static bool ContainsRaster(byte[] tiff, byte[] raster) => tiff.AsSpan().IndexOf(raster) >= 0;

    /// <summary>Reads the StripOffsets (tag 273) value from the little-endian IFD at <paramref name="ifdOffset"/>.</summary>
    private static uint ReadStripOffset(byte[] tiff, int ifdOffset)
    {
        int entryCount = BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(ifdOffset));
        for (int i = 0; i < entryCount; i++)
        {
            int entry = ifdOffset + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(entry)) == 273)
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(entry + 8));
            }
        }

        throw new InvalidDataException("StripOffsets (tag 273) not found.");
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
