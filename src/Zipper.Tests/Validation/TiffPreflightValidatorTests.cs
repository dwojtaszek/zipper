using System.Buffers.Binary;
using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

/// <summary>
/// Checks the TIFF preflight validator against clean Zipper output, every TIFF chaos type (the chaos ↔ validator
/// dogfooding pattern), and each rule's failure path.
/// </summary>
public class TiffPreflightValidatorTests
{
    private const string FilePath = "NATIVES/001/DOC00000001.tif";
    private const string RecordId = "DOC00000001";

    private static ValidationResult Validate(byte[] content)
    {
        var result = new ValidationResult();
        new TiffPreflightValidator().Validate(content, FilePath, result);
        return result;
    }

    private static byte[] RenderTiff(string byteOrder, string compression = "group4", int pageCount = 1) =>
        TiffRenderer.RenderMultiPage(pageCount, RecordId, new TiffConfig { RealismMode = "standard", ByteOrder = byteOrder, Compression = compression });

    private static bool IsBigEndian(byte[] tiff) => tiff[0] == (byte)'M';

    private static int FirstIfdOffset(byte[] tiff) => (int)(IsBigEndian(tiff)
        ? BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(4))
        : BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(4)));

    // Position of the first IFD entry with the given tag, so a test can overwrite one header field.
    private static int EntryPosition(byte[] tiff, ushort tag)
    {
        int ifd = FirstIfdOffset(tiff);
        int count = ReadUInt16(tiff, ifd);
        for (int i = 0; i < count; i++)
        {
            int entry = ifd + 2 + (12 * i);
            if (ReadUInt16(tiff, entry) == tag)
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"Tag {tag} is not in the first IFD.");
    }

    private static ushort ReadUInt16(byte[] tiff, int offset) =>
        IsBigEndian(tiff) ? BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(offset));

    private static void WriteUInt16(byte[] tiff, int offset, ushort value)
    {
        if (IsBigEndian(tiff))
        {
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(offset), value);
        }
    }

    private static void WriteUInt32(byte[] tiff, int offset, uint value)
    {
        if (IsBigEndian(tiff))
        {
            BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(offset), value);
        }
    }

    // Removes one directory entry and shifts the rest, including the next-directory offset, left by one entry.
    private static void DropEntry(byte[] tiff, ushort tag)
    {
        int ifd = FirstIfdOffset(tiff);
        int count = ReadUInt16(tiff, ifd);
        int entry = EntryPosition(tiff, tag);
        int afterEntries = ifd + 2 + (12 * count);
        Array.Copy(tiff, entry + 12, tiff, entry, afterEntries + 4 - (entry + 12));
        WriteUInt16(tiff, ifd, (ushort)(count - 1));
    }

    [Theory]
    [InlineData("ii", "group4", 1)]
    [InlineData("ii", "uncompressed", 3)]
    [InlineData("mm", "group4", 3)]
    [InlineData("mm", "uncompressed", 2)]
    public void Validate_RenderedTiff_ReportsNoFindings(string byteOrder, string compression, int pageCount)
    {
        var result = Validate(RenderTiff(byteOrder, compression, pageCount));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Validate_Placeholder_ReportsNoFindings()
    {
        Assert.Empty(Validate(PlaceholderFiles.GetContent("tiff")).Findings);
    }

    [Theory]
    [InlineData("tiff-dimension-lie")]
    [InlineData("tiff-truncated-ifd")]
    [InlineData("tiff-tag-count-lie")]
    [InlineData("tiff-strip-offset")]
    public void Validate_EachChaosType_IsCaughtAcrossSeedsPageCountsAndByteOrders(string chaosType)
    {
        foreach (var byteOrder in new[] { "ii", "mm" })
        {
            for (int pageCount = 1; pageCount <= 2; pageCount++)
            {
                var original = RenderTiff(byteOrder, "group4", pageCount);
                for (int seed = 1; seed <= 8; seed++)
                {
                    var mutated = TiffAnomalyApplier.Apply(original, FilePath, RecordId, chaosType, new Random(seed)).Content;

                    var result = Validate(mutated);

                    Assert.True(result.HasErrors, $"{chaosType} with seed {seed}, {pageCount} page(s), byte order {byteOrder} was not caught.");
                    Assert.All(result.Findings, finding => Assert.Equal(TiffPreflightValidator.Category, finding.Category));
                    Assert.All(result.Findings, finding => Assert.Equal(FilePath, finding.FilePath));
                }
            }
        }
    }

    [Theory]
    [InlineData("tiff-dimension-lie", "pixel range")]
    [InlineData("tiff-truncated-ifd", "extends past the end of the file")]
    [InlineData("tiff-strip-offset", "Strip 1 extends past the end of the file")]
    public void Validate_ChaosType_ReportsTheFailureItInjects(string chaosType, string expectedMessage)
    {
        var mutated = TiffAnomalyApplier.Apply(RenderTiff("ii"), FilePath, RecordId, chaosType, new Random(2)).Content;

        var result = Validate(mutated);

        Assert.Contains(result.Findings, finding => finding.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShorterThanHeader_ReportsHeaderFinding()
    {
        var result = Validate(new byte[] { (byte)'I', (byte)'I', 42 });

        Assert.Single(result.Findings);
        Assert.Contains("shorter than the 8-byte TIFF header", result.Findings[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NoByteOrderMark_ReportsHeaderFinding()
    {
        var result = Validate(Encoding.ASCII.GetBytes("%PDF-1.4 not a tiff file"));

        Assert.Single(result.Findings);
        Assert.Contains("byte-order mark", result.Findings[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WrongMagicNumber_ReportsHeaderFinding()
    {
        var tiff = RenderTiff("ii");
        tiff[2] = 41;

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("magic number is not 42", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ZeroImageWidth_ReportsDimensionFinding()
    {
        var tiff = RenderTiff("mm");
        WriteUInt32(tiff, EntryPosition(tiff, 256) + 8, 0);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Equals("ImageWidth 0 is outside the 1-65535 pixel range.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_InvalidFieldType_ReportsEntryFinding()
    {
        var tiff = RenderTiff("ii");
        WriteUInt16(tiff, EntryPosition(tiff, 262) + 2, 0);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("tag 262) has an invalid field type 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EntriesOutOfTagOrder_ReportsEntryFinding()
    {
        var tiff = RenderTiff("ii");
        // Renaming tag 273 to 255 puts it before tag 256 in the directory.
        WriteUInt16(tiff, EntryPosition(tiff, 273), 255);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("not in ascending tag order", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingStripOffsets_ReportsRequiredTagFinding()
    {
        var tiff = RenderTiff("mm");
        // Renaming StripOffsets to XPosition (274) keeps the directory sorted but drops the required tag.
        WriteUInt16(tiff, EntryPosition(tiff, 273), 274);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("missing required tag StripOffsets", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(258, "BitsPerSample")]
    [InlineData(259, "Compression")]
    [InlineData(262, "PhotometricInterpretation")]
    [InlineData(278, "RowsPerStrip")]
    [InlineData(282, "XResolution")]
    [InlineData(283, "YResolution")]
    [InlineData(296, "ResolutionUnit")]
    public void Validate_MissingBaselineTag_ReportsRequiredTagFinding(ushort tag, string name)
    {
        var tiff = RenderTiff("ii");
        DropEntry(tiff, tag);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains($"missing required tag {name}", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_StripCountArrayPastEndOfFile_ReportsFinding()
    {
        var tiff = RenderTiff("ii");
        // A LONG array of two strip counts stored past the end of the file is unreadable.
        int entry = EntryPosition(tiff, 279);
        WriteUInt32(tiff, entry + 4, 2);
        WriteUInt32(tiff, entry + 8, (uint)tiff.Length);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("StripByteCounts values extend past the end of the file", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_StripCountWithUnsupportedFieldType_ReportsFinding()
    {
        var tiff = RenderTiff("ii");
        // StripByteCounts as RATIONAL is not a valid strip count field type.
        WriteUInt16(tiff, EntryPosition(tiff, 279) + 2, 5);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("StripByteCounts has an unsupported field type 5", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NextDirectoryPastEndOfFile_ReportsFinding()
    {
        var tiff = RenderTiff("ii", pageCount: 1);
        int ifd = FirstIfdOffset(tiff);
        WriteUInt32(tiff, ifd + 2 + (12 * ReadUInt16(tiff, ifd)), (uint)tiff.Length + 10);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("points to a next directory past the end of the file", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DirectoryChainLoops_ReportsFinding()
    {
        var tiff = RenderTiff("ii", pageCount: 1);
        int ifd = FirstIfdOffset(tiff);
        WriteUInt32(tiff, ifd + 2 + (12 * ReadUInt16(tiff, ifd)), (uint)ifd);

        var result = Validate(tiff);

        Assert.Contains(result.Findings, finding => finding.Message.Contains("chain loops at IFD 2", StringComparison.Ordinal));
    }
}
