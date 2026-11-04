using System.Buffers.Binary;
using System.Globalization;

namespace Zipper;

/// <summary>
/// The mutated TIFF bytes and the audit entry describing the anomaly applied to them.
/// </summary>
internal readonly record struct TiffAnomalyResult(byte[] Content, ChaosAnomaly Anomaly);

/// <summary>
/// Injects header and Image File Directory anomalies into rendered TIFF bytes: a dimension lie, a directory cut short,
/// an entry count that overstates the entries, and a strip that runs past the end of the file.
/// </summary>
/// <remarks>
/// Every mutation targets the first Image File Directory, the one readers display first. The fields it edits are
/// LONG values, as the renderer and the placeholder write them. Each call is pure: the output depends only on the
/// input bytes, the anomaly type, and the random sequence, so a seed always yields the same bytes.
/// </remarks>
internal static class TiffAnomalyApplier
{
    private const ushort ImageWidthTag = 256;
    private const ushort ImageLengthTag = 257;
    private const ushort StripOffsetsTag = 273;
    private const ushort StripByteCountsTag = 279;

    private const int HeaderSize = 8;
    private const int EntrySize = 12;
    private const int EntryCountSize = 2;
    private const int NextIfdOffsetSize = 4;

    /// <summary>
    /// Applies one TIFF anomaly type to the TIFF bytes. Every call changes the bytes and returns one audit entry.
    /// </summary>
    /// <param name="tiffBytes">The rendered or placeholder TIFF bytes.</param>
    /// <param name="fileName">The TIFF file name recorded in the audit entry.</param>
    /// <param name="recordId">The record identifier recorded in the audit entry.</param>
    /// <param name="chaosType">A type from <see cref="ChaosAnomalyTypes.Tiff"/>.</param>
    /// <param name="random">The random sequence for this work item.</param>
    /// <returns>The mutated bytes and their audit entry.</returns>
    /// <exception cref="ArgumentException">The bytes are not a TIFF, the first directory does not fit in them, or the type is unknown.</exception>
    public static TiffAnomalyResult Apply(byte[] tiffBytes, string fileName, string recordId, string chaosType, Random random)
    {
        ArgumentNullException.ThrowIfNull(tiffBytes);
        ArgumentNullException.ThrowIfNull(random);

        if (!HasByteOrderMark(tiffBytes))
        {
            throw new ArgumentException("The input is not a TIFF file: it does not start with an II or MM byte-order mark.", nameof(tiffBytes));
        }

        bool bigEndian = tiffBytes[0] == (byte)'M';

        // Read the directory offset as a long so a value near uint.MaxValue cannot overflow the bounds checks.
        long ifdOffset = ReadUInt32(tiffBytes, 4, bigEndian);
        if (ifdOffset < HeaderSize || ifdOffset + EntryCountSize > tiffBytes.Length)
        {
            throw new ArgumentException("The first Image File Directory does not start inside the file.", nameof(tiffBytes));
        }

        int ifd = (int)ifdOffset;
        int entryCount = ReadUInt16(tiffBytes, ifd, bigEndian);
        if (ifd + EntryCountSize + ((long)EntrySize * entryCount) + NextIfdOffsetSize > tiffBytes.Length)
        {
            throw new ArgumentException("The first Image File Directory does not fit in the file.", nameof(tiffBytes));
        }

        return chaosType switch
        {
            "tiff-dimension-lie" => ApplyDimensionLie(tiffBytes, ifd, entryCount, bigEndian, fileName, recordId, random),
            "tiff-truncated-ifd" => ApplyTruncatedIfd(tiffBytes, ifd, entryCount, fileName, recordId, random),
            "tiff-tag-count-lie" => ApplyTagCountLie(tiffBytes, ifd, entryCount, bigEndian, fileName, recordId, random),
            "tiff-strip-offset" => ApplyStripOffset(tiffBytes, ifd, entryCount, bigEndian, fileName, recordId, random),
            _ => throw new ArgumentException($"Unknown TIFF anomaly type '{chaosType}'.", nameof(chaosType)),
        };
    }

    private static TiffAnomalyResult ApplyDimensionLie(
        byte[] tiff, int ifd, int entryCount, bool bigEndian, string fileName, string recordId, Random random)
    {
        // A pixel dimension of 2^32-1 claims far more image data than the strip holds.
        bool width = random.Next(2) == 0;
        ushort tag = width ? ImageWidthTag : ImageLengthTag;
        string column = width ? "ImageWidth" : "ImageLength";

        var content = (byte[])tiff.Clone();
        WriteUInt32(content, EntryValueOffset(FindEntry(tiff, ifd, entryCount, tag, bigEndian)), uint.MaxValue, bigEndian);

        return new TiffAnomalyResult(
            content,
            Audit(
                fileName,
                recordId,
                column,
                "tiff-dimension-lie",
                $"{column} is set to {uint.MaxValue.ToString(CultureInfo.InvariantCulture)}, far beyond the image data in the strip.",
                $"IFD 1, tag {tag}"));
    }

    private static TiffAnomalyResult ApplyTruncatedIfd(
        byte[] tiff, int ifd, int entryCount, string fileName, string recordId, Random random)
    {
        // Cut the file inside the first directory, after the entry count and before its next-directory offset ends.
        int declaredEnd = ifd + EntryCountSize + (EntrySize * entryCount) + NextIfdOffsetSize;
        int cut = random.Next(ifd + EntryCountSize + 1, declaredEnd);

        return new TiffAnomalyResult(
            tiff[..cut],
            Audit(
                fileName,
                recordId,
                "N/A",
                "tiff-truncated-ifd",
                $"The first Image File Directory is cut off at byte {cut.ToString(CultureInfo.InvariantCulture)}, so its entries or next-directory offset run past the end of the file.",
                $"IFD 1, file cut at byte {cut.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static TiffAnomalyResult ApplyTagCountLie(
        byte[] tiff, int ifd, int entryCount, bool bigEndian, string fileName, string recordId, Random random)
    {
        // The directory claims 1 to 4 entries that are not there, so readers parse the next-offset and extra data as entries.
        int declared = entryCount + random.Next(1, 5);

        var content = (byte[])tiff.Clone();
        WriteUInt16(content, ifd, (ushort)declared, bigEndian);

        return new TiffAnomalyResult(
            content,
            Audit(
                fileName,
                recordId,
                "N/A",
                "tiff-tag-count-lie",
                $"The first Image File Directory declares {declared.ToString(CultureInfo.InvariantCulture)} entries but contains {entryCount.ToString(CultureInfo.InvariantCulture)}.",
                "IFD 1, entry count"));
    }

    private static TiffAnomalyResult ApplyStripOffset(
        byte[] tiff, int ifd, int entryCount, bool bigEndian, string fileName, string recordId, Random random)
    {
        // Either the strip starts at the end of the file, or its byte count is longer than the bytes after its offset.
        bool offsetLie = random.Next(2) == 0;
        ushort tag = offsetLie ? StripOffsetsTag : StripByteCountsTag;
        string column = offsetLie ? "StripOffsets" : "StripByteCounts";

        var content = (byte[])tiff.Clone();
        WriteUInt32(content, EntryValueOffset(FindEntry(tiff, ifd, entryCount, tag, bigEndian)), (uint)tiff.Length, bigEndian);

        return new TiffAnomalyResult(
            content,
            Audit(
                fileName,
                recordId,
                column,
                "tiff-strip-offset",
                $"{column} places the image strip past the end of the file.",
                $"IFD 1, tag {tag}"));
    }

    private static bool HasByteOrderMark(byte[] tiff) =>
        tiff.Length >= HeaderSize
        && ((tiff[0] == (byte)'I' && tiff[1] == (byte)'I') || (tiff[0] == (byte)'M' && tiff[1] == (byte)'M'));

    private static int FindEntry(byte[] tiff, int ifd, int entryCount, ushort tag, bool bigEndian)
    {
        for (int i = 0; i < entryCount; i++)
        {
            int entry = ifd + EntryCountSize + (EntrySize * i);
            if (ReadUInt16(tiff, entry, bigEndian) == tag)
            {
                return entry;
            }
        }

        throw new ArgumentException($"The first Image File Directory has no tag {tag}.", nameof(tiff));
    }

    // A LONG with a count of one is stored in the entry's value field, the last four bytes of its twelve.
    private static int EntryValueOffset(int entry) => entry + 8;

    private static ushort ReadUInt16(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadUInt32(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void WriteUInt16(byte[] bytes, int offset, ushort value, bool bigEndian)
    {
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        }
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value, bool bigEndian)
    {
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        }
    }

    // TIFF anomalies are whole-file, so the audit reports line 1 as the email whole-message anomalies do.
    private static ChaosAnomaly Audit(
        string fileName,
        string recordId,
        string column,
        string errorType,
        string description,
        string location) =>
        new()
        {
            File = fileName,
            LineNumber = "1",
            RecordID = recordId,
            Column = column,
            ErrorType = errorType,
            Description = description,
            Location = location,
        };
}
