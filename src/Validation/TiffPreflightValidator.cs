using System.Buffers.Binary;
using System.Globalization;

namespace Zipper.Validation;

/// <summary>
/// Preflight check of a TIFF's header and Image File Directory chain. Reports the malformations that readers reject
/// and that TIFF chaos injects: a header without a byte-order mark, invalid or out-of-order entries, required tags
/// that are missing, dimensions outside 1-65535 pixels, strips that run past the end of the file, and directories
/// that run past the end of the file or loop.
/// </summary>
/// <remarks>
/// Entry, dimension, and strip checks cover the first Image File Directory. Every later directory is checked for its
/// position in the file and for loops. Every read is bounds-checked first, so arbitrary bytes produce findings, not exceptions.
/// </remarks>
public sealed class TiffPreflightValidator
{
    /// <summary>The category recorded on every TIFF preflight finding.</summary>
    public const string Category = "TiffPreflight";

    private const int HeaderSize = 8;
    private const int EntryCountSize = 2;
    private const int EntrySize = 12;
    private const int NextIfdOffsetSize = 4;
    private const int ValueFieldSize = 4;
    private const long MaxPixelDimension = 65535;

    private const ushort ImageWidthTag = 256;
    private const ushort ImageLengthTag = 257;
    private const ushort StripOffsetsTag = 273;
    private const ushort StripByteCountsTag = 279;

    private const ushort ShortType = 3;
    private const ushort LongType = 4;

    private static readonly (ushort Tag, string Name)[] RequiredTags =
    [
        (ImageWidthTag, "ImageWidth"),
        (ImageLengthTag, "ImageLength"),
        (StripOffsetsTag, "StripOffsets"),
        (StripByteCountsTag, "StripByteCounts"),
    ];

    /// <summary>
    /// Checks the TIFF bytes and adds one error finding per malformation to <paramref name="result"/>.
    /// </summary>
    /// <param name="content">The TIFF file bytes.</param>
    /// <param name="filePath">The file path recorded on each finding.</param>
    /// <param name="result">The validation result that receives the findings.</param>
    public void Validate(byte[] content, string filePath, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(result);

        if (content.Length < HeaderSize)
        {
            Add(result, filePath, "File is shorter than the 8-byte TIFF header.");
            return;
        }

        bool bigEndian;
        if (content[0] == (byte)'I' && content[1] == (byte)'I')
        {
            bigEndian = false;
        }
        else if (content[0] == (byte)'M' && content[1] == (byte)'M')
        {
            bigEndian = true;
        }
        else
        {
            Add(result, filePath, "File does not start with a TIFF byte-order mark (II or MM).");
            return;
        }

        if (ReadUInt16(content, 2, bigEndian) != 42)
        {
            Add(result, filePath, "TIFF magic number is not 42.");
            return;
        }

        ValidateDirectoryChain(content, bigEndian, filePath, result, ReadUInt32(content, 4, bigEndian));
    }

    private static void ValidateDirectoryChain(byte[] content, bool bigEndian, string filePath, ValidationResult result, long firstIfd)
    {
        var visited = new HashSet<long> { firstIfd };
        long ifd = firstIfd;
        int ordinal = 1;

        while (true)
        {
            if (ifd + EntryCountSize > content.Length)
            {
                Add(result, filePath, $"IFD {ordinal} extends past the end of the file.");
                return;
            }

            int entryCount = ReadUInt16(content, ifd, bigEndian);
            long directoryEnd = ifd + EntryCountSize + ((long)EntrySize * entryCount);
            if (directoryEnd + NextIfdOffsetSize > content.Length)
            {
                Add(result, filePath, $"IFD {ordinal} extends past the end of the file.");
                return;
            }

            if (ordinal == 1)
            {
                ValidateFirstDirectory(content, bigEndian, ifd, entryCount, filePath, result);
            }

            long next = ReadUInt32(content, directoryEnd, bigEndian);
            if (next == 0)
            {
                return;
            }

            if (next + EntryCountSize > content.Length)
            {
                Add(result, filePath, $"IFD {ordinal} points to a next directory past the end of the file.");
                return;
            }

            if (!visited.Add(next))
            {
                Add(result, filePath, $"IFD chain loops at IFD {ordinal + 1}.");
                return;
            }

            ifd = next;
            ordinal++;
        }
    }

    private static void ValidateFirstDirectory(byte[] content, bool bigEndian, long ifd, int entryCount, string filePath, ValidationResult result)
    {
        var entries = new Dictionary<ushort, long>();
        ushort previousTag = 0;

        for (int i = 0; i < entryCount; i++)
        {
            long entry = ifd + EntryCountSize + ((long)EntrySize * i);
            ushort tag = ReadUInt16(content, entry, bigEndian);
            ushort type = ReadUInt16(content, entry + 2, bigEndian);
            uint count = ReadUInt32(content, entry + 4, bigEndian);

            if (i > 0 && tag <= previousTag)
            {
                Add(result, filePath, $"IFD 1 entry {i + 1} (tag {tag}) is not in ascending tag order.");
            }

            previousTag = tag;
            entries.TryAdd(tag, entry);

            int unitSize = UnitSize(type);
            if (unitSize == 0)
            {
                Add(result, filePath, $"IFD 1 entry {i + 1} (tag {tag}) has an invalid field type {type}.");
                continue;
            }

            if (!ValuesFit(content, bigEndian, entry, unitSize, count))
            {
                Add(result, filePath, $"{TagName(tag)} values extend past the end of the file.");
            }
        }

        foreach (var (tag, name) in RequiredTags)
        {
            if (!entries.ContainsKey(tag))
            {
                Add(result, filePath, $"IFD 1 is missing required tag {name}.");
            }
        }

        CheckDimension(content, bigEndian, entries, ImageWidthTag, "ImageWidth", filePath, result);
        CheckDimension(content, bigEndian, entries, ImageLengthTag, "ImageLength", filePath, result);
        CheckStrips(content, bigEndian, entries, filePath, result);
    }

    private static void CheckDimension(
        byte[] content, bool bigEndian, Dictionary<ushort, long> entries, ushort tag, string name, string filePath, ValidationResult result)
    {
        if (!entries.TryGetValue(tag, out long entry))
        {
            return;
        }

        ushort type = ReadUInt16(content, entry + 2, bigEndian);
        uint count = ReadUInt32(content, entry + 4, bigEndian);
        if (!IsIntegerFieldType(name, type, filePath, result))
        {
            return;
        }

        if (count != 1 || !ValuesFit(content, bigEndian, entry, UnitSize(type), count))
        {
            return;
        }

        long value = ReadValue(content, bigEndian, entry, type, 0);
        if (value is < 1 or > MaxPixelDimension)
        {
            Add(result, filePath, $"{name} {value.ToString(CultureInfo.InvariantCulture)} is outside the 1-65535 pixel range.");
        }
    }

    private static void CheckStrips(byte[] content, bool bigEndian, Dictionary<ushort, long> entries, string filePath, ValidationResult result)
    {
        if (!entries.TryGetValue(StripOffsetsTag, out long offsetsEntry) || !entries.TryGetValue(StripByteCountsTag, out long countsEntry))
        {
            return;
        }

        var offsetsType = ReadUInt16(content, offsetsEntry + 2, bigEndian);
        var countsType = ReadUInt16(content, countsEntry + 2, bigEndian);
        if (!IsIntegerFieldType("StripOffsets", offsetsType, filePath, result)
            || !IsIntegerFieldType("StripByteCounts", countsType, filePath, result))
        {
            return;
        }

        uint offsetsCount = ReadUInt32(content, offsetsEntry + 4, bigEndian);
        uint countsCount = ReadUInt32(content, countsEntry + 4, bigEndian);
        if (!ValuesFit(content, bigEndian, offsetsEntry, UnitSize(offsetsType), offsetsCount)
            || !ValuesFit(content, bigEndian, countsEntry, UnitSize(countsType), countsCount))
        {
            return;
        }

        long strips = Math.Min(offsetsCount, countsCount);
        for (int strip = 0; strip < strips; strip++)
        {
            long offset = ReadValue(content, bigEndian, offsetsEntry, offsetsType, strip);
            long byteCount = ReadValue(content, bigEndian, countsEntry, countsType, strip);
            if (offset + byteCount > content.Length)
            {
                Add(result, filePath, $"Strip {strip + 1} extends past the end of the file.");
            }
        }
    }

    // Dimensions and strip arrays are read as SHORT or LONG integers, so any other field type cannot be decoded.
    private static bool IsIntegerFieldType(string name, ushort type, string filePath, ValidationResult result)
    {
        if (type is ShortType or LongType)
        {
            return true;
        }

        Add(result, filePath, $"{name} has an unsupported field type {type}.");
        return false;
    }

    // An entry whose values total four bytes or less keeps them in its value field. A longer run is stored at the
    // offset the value field names, and that run must lie inside the file.
    private static bool ValuesFit(byte[] content, bool bigEndian, long entry, int unitSize, uint count)
    {
        long bytes = (long)unitSize * count;
        if (bytes <= ValueFieldSize)
        {
            return true;
        }

        long offset = ReadUInt32(content, entry + 8, bigEndian);
        return offset + bytes <= content.Length;
    }

    // Reads the index-th value of an entry whose values have already been checked to fit in the file.
    private static long ReadValue(byte[] content, bool bigEndian, long entry, ushort type, int index)
    {
        int unitSize = UnitSize(type);
        uint count = ReadUInt32(content, entry + 4, bigEndian);
        long valuesAt = (long)unitSize * count <= ValueFieldSize
            ? entry + 8
            : ReadUInt32(content, entry + 8, bigEndian);
        long position = valuesAt + ((long)index * unitSize);

        return type == ShortType
            ? ReadUInt16(content, position, bigEndian)
            : ReadUInt32(content, position, bigEndian);
    }

    private static int UnitSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => 0,
    };

    private static string TagName(ushort tag) => tag switch
    {
        ImageWidthTag => "ImageWidth",
        ImageLengthTag => "ImageLength",
        StripOffsetsTag => "StripOffsets",
        StripByteCountsTag => "StripByteCounts",
        _ => $"tag {tag.ToString(CultureInfo.InvariantCulture)}",
    };

    private static ushort ReadUInt16(byte[] bytes, long offset, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan((int)offset))
            : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)offset));

    private static uint ReadUInt32(byte[] bytes, long offset, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)offset))
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)offset));

    private static void Add(ValidationResult result, string filePath, string message) =>
        result.Add(new ValidationFinding(ValidationSeverity.Error, Category, message, filePath));
}
