using System.Text;
using Zipper.Config;

namespace Zipper;

/// <summary>
/// Bitonal TIFF renderer that burns in Bates endorsement bands and emits standard
/// Baseline (uncompressed) or CCITT Group 4 (ITU-T T.6) single-page and multi-page TIFFs.
/// </summary>
internal static class TiffRenderer
{
    public const int DefaultWidth = 2550;
    public const int DefaultHeight = 3300;
    public const int DefaultDpi = 300;

    private const int IfdEntryCount = 16;
    private const uint FirstIfdOffset = 8;

    // Image File Directory: 2-byte entry count + entries * 12 bytes + 4-byte next-IFD offset
    private const int IfdSize = 2 + (IfdEntryCount * 12) + 4;

    private static readonly byte[] SoftwareBytes = Encoding.ASCII.GetBytes("Zipper\0");

    /// <summary>
    /// Renders a single-page TIFF image with a burned-in Bates endorsement band in the bottom margin.
    /// </summary>
    public static byte[] RenderPage(string? batesNumber, int pageIndex, int pageCount, TiffConfig? tiffConfig)
    {
        bool useGroup4 = UseGroup4(tiffConfig);

        var raster = CreatePageRaster(batesNumber, DefaultWidth, DefaultHeight);
        var stripData = useGroup4
            ? CompressGroup4(raster, DefaultWidth, DefaultHeight)
            : raster;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteTiffHeader(bw);
        WritePage(bw, FirstIfdOffset, stripData, pageIndex, pageCount, useGroup4, isLast: true);
        return ms.ToArray();
    }

    /// <summary>
    /// Renders a multi-page TIFF file with chained IFDs for each page.
    /// Pages are streamed directly to the writer to avoid retaining uncompressed rasters in memory.
    /// </summary>
    public static byte[] RenderMultiPage(int pageCount, string? baseBates, TiffConfig? tiffConfig)
    {
        bool useGroup4 = UseGroup4(tiffConfig);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteTiffHeader(bw);
        uint offset = FirstIfdOffset;

        for (int p = 1; p <= pageCount; p++)
        {
            var pageBates = string.IsNullOrEmpty(baseBates) || pageCount <= 1
                ? baseBates
                : $"{baseBates}_{p:D3}";
            var raster = CreatePageRaster(pageBates, DefaultWidth, DefaultHeight);
            var stripData = useGroup4
                ? CompressGroup4(raster, DefaultWidth, DefaultHeight)
                : raster;

            offset = WritePage(bw, offset, stripData, p, pageCount, useGroup4, isLast: p == pageCount);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Group 4 unless the config asks for uncompressed strips. <see cref="TiffConfig.Compression"/> is the
    /// only compression source; the module resolves --tiff-realism uncompressed into it.
    /// </summary>
    private static bool UseGroup4(TiffConfig? tiffConfig)
        => tiffConfig is null || !string.Equals(tiffConfig.Compression, "uncompressed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a 1-bit monochrome raster (min-is-white: 0 = white, 1 = black)
    /// with the Bates endorsement text burned into the bottom-right margin.
    /// </summary>
    public static byte[] CreatePageRaster(string? batesNumber, int width, int height)
    {
        var bitmap = new BilevelBitmap(width, height);

        if (!string.IsNullOrEmpty(batesNumber))
        {
            const int maxScale = 5;
            const int margin = 150; // 0.5 inch margin at 300 DPI

            // Shrink the glyphs for labels too wide for the band at full size, so labels up to 400 characters
            // are not clipped and consecutive page labels keep distinct endorsements. Default-length labels keep maxScale.
            int scale = Math.Clamp((width - (2 * margin)) / (batesNumber.Length * 6), 1, maxScale);
            int textWidth = batesNumber.Length * 6 * scale;
            int textHeight = 7 * scale;
            int startX = Math.Max(margin, width - margin - textWidth);
            int startY = Math.Max(margin, height - margin - textHeight);

            DrawText(bitmap, batesNumber, startX, startY, scale);
        }

        return bitmap.Pixels;
    }

    private static void DrawText(BilevelBitmap bitmap, string text, int startX, int startY, int scale)
    {
        for (int charIdx = 0; charIdx < text.Length; charIdx++)
        {
            DrawGlyph(bitmap, text[charIdx], startX + charIdx * 6 * scale, startY, scale);
        }
    }

    private static void DrawGlyph(BilevelBitmap bitmap, char ch, int originX, int originY, int scale)
    {
        int glyphIndex = ch >= 32 && ch <= 126 ? (ch - 32) * 5 : ('?' - 32) * 5;
        for (int col = 0; col < 5; col++)
        {
            byte colBits = Font5X7[glyphIndex + col];
            for (int row = 0; row < 7; row++)
            {
                if (((colBits >> row) & 1) != 0)
                {
                    FillCell(bitmap, originX + col * scale, originY + row * scale, scale);
                }
            }
        }
    }

    /// <summary>
    /// Blackens a square cell of scale x scale pixels. Pixels outside the bitmap are skipped.
    /// </summary>
    private static void FillCell(BilevelBitmap bitmap, int x, int y, int scale)
    {
        for (int dy = 0; dy < scale; dy++)
        {
            for (int dx = 0; dx < scale; dx++)
            {
                bitmap.SetBlack(x + dx, y + dy);
            }
        }
    }

    private static void WriteTiffHeader(BinaryWriter bw)
    {
        // Header: "II", magic 42, offset to first IFD (8)
        bw.Write((byte)'I');
        bw.Write((byte)'I');
        bw.Write((ushort)42);
        bw.Write(FirstIfdOffset);
    }

    private static uint WritePage(
        BinaryWriter bw,
        uint currentIfdOffset,
        byte[] stripData,
        int pageIndex,
        int pageCount,
        bool isGroup4,
        bool isLast)
    {
        uint extraDataOffset = currentIfdOffset + IfdSize;
        uint xResOffset = extraDataOffset;
        uint yResOffset = extraDataOffset + 8;
        uint swOffset = extraDataOffset + 16;
        uint stripOffset = AlignToWord(swOffset + (uint)SoftwareBytes.Length);
        uint nextIfdOffset = isLast ? 0 : AlignToWord(stripOffset + (uint)stripData.Length);

        // Directory entries sorted in strictly ascending tag order
        bw.Write((ushort)IfdEntryCount);

        WriteEntry(bw, 254, 4, 1, 0);                               // NewSubfileType: 0
        WriteEntry(bw, 256, 4, 1, (uint)DefaultWidth);              // ImageWidth
        WriteEntry(bw, 257, 4, 1, (uint)DefaultHeight);             // ImageLength
        WriteEntry(bw, 258, 3, 1, 1);                               // BitsPerSample: 1
        WriteEntry(bw, 259, 3, 1, (uint)(isGroup4 ? 4 : 1));        // Compression: 4 (G4) or 1 (None)
        WriteEntry(bw, 262, 3, 1, 0);                               // Photometric: 0 (WhiteIsZero)
        WriteEntry(bw, 273, 4, 1, stripOffset);                     // StripOffsets
        WriteEntry(bw, 277, 3, 1, 1);                               // SamplesPerPixel: 1
        WriteEntry(bw, 278, 4, 1, (uint)DefaultHeight);             // RowsPerStrip
        WriteEntry(bw, 279, 4, 1, (uint)stripData.Length);          // StripByteCounts
        WriteEntry(bw, 282, 5, 1, xResOffset);                      // XResolution -> 300/1
        WriteEntry(bw, 283, 5, 1, yResOffset);                      // YResolution -> 300/1
        WriteEntry(bw, 284, 3, 1, 1);                               // PlanarConfiguration: 1
        WriteEntry(bw, 296, 3, 1, 2);                               // ResolutionUnit: 2 (Inch)
        WriteEntry(bw, 297, 3, 2, (uint)((pageCount << 16) | ((pageIndex - 1) & 0xFFFF))); // PageNumber
        WriteEntry(bw, 305, 2, (uint)SoftwareBytes.Length, swOffset); // Software: "Zipper\0"

        bw.Write(nextIfdOffset);

        // Extra data
        // XResolution: 300 / 1
        bw.Write((uint)DefaultDpi);
        bw.Write((uint)1);
        // YResolution: 300 / 1
        bw.Write((uint)DefaultDpi);
        bw.Write((uint)1);
        // Software, padded so the strip starts at its word-aligned offset
        bw.Write(SoftwareBytes);
        PadTo(bw, stripOffset);

        // Strip data, padded up to the next IFD (the last page has none, so nothing is written)
        bw.Write(stripData);
        PadTo(bw, nextIfdOffset);

        return nextIfdOffset;
    }

    /// <summary>
    /// Rounds an offset up to the next even byte. TIFF requires word-aligned IFD and strip offsets.
    /// </summary>
    private static uint AlignToWord(uint offset) => offset % 2 == 0 ? offset : offset + 1;

    /// <summary>
    /// Writes zero bytes until the stream reaches <paramref name="offset"/>. Does nothing when it is already there.
    /// </summary>
    private static void PadTo(BinaryWriter bw, uint offset)
    {
        while (bw.BaseStream.Position < offset)
        {
            bw.Write((byte)0);
        }
    }

    private static void WriteEntry(BinaryWriter bw, ushort tag, ushort type, uint count, uint valOrOffset)
    {
        bw.Write(tag);
        bw.Write(type);
        bw.Write(count);
        if (type == 3 && count == 1)
        {
            bw.Write((ushort)valOrOffset);
            bw.Write((ushort)0);
        }
        else
        {
            bw.Write(valOrOffset);
        }
    }

    /// <summary>
    /// Compresses a 1-bit raster using ITU-T T.6 (CCITT Group 4 Fax 2-D) encoding.
    /// </summary>
    public static byte[] CompressGroup4(byte[] raster, int width, int height)
    {
        int bytesPerRow = (width + 7) / 8;
        var bw = new BitStreamWriter();
        var refLine = new byte[bytesPerRow];

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * bytesPerRow;
            int a0 = -1;
            int a0Color = 0; // Starts as imaginary white element before x=0

            while (a0 < width)
            {
                int a1 = FindA1(raster, rowOffset, a0, a0Color, width);
                int b1 = FindB1(refLine, a0, a0Color, width);
                int b2 = FindB2(refLine, b1, width);

                if (b2 < a1)
                {
                    // Pass mode: 0001
                    bw.WriteBits(0x01, 4);
                    a0 = b2;
                    // a0Color remains unchanged in Pass mode
                }
                else if (Math.Abs(a1 - b1) <= 3)
                {
                    // Vertical mode
                    WriteVerticalCode(bw, a1 - b1);
                    a0 = a1;
                    a0Color = 1 - a0Color; // toggles to a1's color
                }
                else
                {
                    // Horizontal mode: 001
                    bw.WriteBits(0x01, 3);
                    int a2 = FindA2(raster, rowOffset, a1, width);
                    int run1 = a1 - (a0 < 0 ? 0 : a0);
                    int run2 = a2 - a1;
                    EncodeRun(bw, run1, a0Color == 0);
                    EncodeRun(bw, run2, a0Color != 0);
                    a0 = a2;
                    // a0Color remains unchanged after coding two runs
                }
            }

            Buffer.BlockCopy(raster, rowOffset, refLine, 0, bytesPerRow);
        }

        // EOFB: two consecutive 12-bit EOL markers (000000000001)
        bw.WriteBits(0x001, 12);
        bw.WriteBits(0x001, 12);
        bw.Flush();

        return bw.ToArray();
    }

    /// <summary>
    /// Writes the T.6 vertical-mode codeword for a1 - b1 = <paramref name="d"/>, where |d| is at most 3.
    /// </summary>
    private static void WriteVerticalCode(BitStreamWriter bw, int d)
    {
        switch (d)
        {
            case 0:
                bw.WriteBits(1, 1);
                break;
            case 1:
                bw.WriteBits(3, 3); // 011
                break;
            case 2:
                bw.WriteBits(3, 6); // 000011
                break;
            case 3:
                bw.WriteBits(3, 7); // 0000011
                break;
            case -1:
                bw.WriteBits(2, 3); // 010
                break;
            case -2:
                bw.WriteBits(2, 6); // 000010
                break;
            case -3:
                bw.WriteBits(2, 7); // 0000010
                break;
        }
    }

    private static int GetBit(byte[] data, int offset, int bitIndex)
    {
        return (data[offset + (bitIndex >> 3)] >> (7 - (bitIndex & 7))) & 1;
    }

    private static int FindA1(byte[] data, int offset, int a0, int a0Color, int width)
    {
        int start = Math.Max(0, a0 + 1);
        int x = start;
        byte matchByte = a0Color == 0 ? (byte)0x00 : (byte)0xFF;

        while (x < width && (x & 7) != 0)
        {
            if (GetBit(data, offset, x) != a0Color)
            {
                return x;
            }
            x++;
        }

        while (x + 8 <= width && data[offset + (x >> 3)] == matchByte)
        {
            x += 8;
        }

        while (x < width)
        {
            if (GetBit(data, offset, x) != a0Color)
            {
                return x;
            }
            x++;
        }

        return width;
    }

    private static int FindA2(byte[] data, int offset, int a1, int width)
    {
        if (a1 >= width)
        {
            return width;
        }

        int a1Color = GetBit(data, offset, a1);
        return FindA1(data, offset, a1, a1Color, width);
    }

    private static int FindB1(byte[] refLine, int a0, int a0Color, int width)
    {
        int start = Math.Max(0, a0 + 1);
        int prevColor = start == 0 ? 0 : GetBit(refLine, 0, start - 1);
        for (int x = start; x < width; x++)
        {
            int curColor = GetBit(refLine, 0, x);
            if (curColor != prevColor && curColor != a0Color)
            {
                return x;
            }

            prevColor = curColor;
        }

        return width;
    }

    private static int FindB2(byte[] refLine, int b1, int width)
    {
        if (b1 >= width)
        {
            return width;
        }

        int b1Color = GetBit(refLine, 0, b1);
        return FindA1(refLine, 0, b1, b1Color, width);
    }

    private static void EncodeRun(BitStreamWriter bw, int length, bool isWhite)
    {
        var makeup = isWhite ? WhiteMakeup : BlackMakeup;
        var term = isWhite ? WhiteTerminating : BlackTerminating;

        while (length >= 2624)
        {
            var (len, code) = makeup[(2560 / 64) - 1];
            bw.WriteBits(code, len);
            length -= 2560;
        }

        if (length >= 64)
        {
            int m = (length / 64) * 64;
            var (len, code) = makeup[(m / 64) - 1];
            bw.WriteBits(code, len);
            length %= 64;
        }

        var (termLen, termCode) = term[length];
        bw.WriteBits(termCode, termLen);
    }

    /// <summary>
    /// 1-bit min-is-white pixel buffer. Each row is padded to whole bytes.
    /// </summary>
    private sealed class BilevelBitmap
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _bytesPerRow;

        public BilevelBitmap(int width, int height)
        {
            _width = width;
            _height = height;
            _bytesPerRow = (width + 7) / 8;
            Pixels = new byte[_bytesPerRow * height];
        }

        public byte[] Pixels { get; }

        /// <summary>
        /// Sets the pixel to black (bit 1). Coordinates outside the bitmap are ignored.
        /// </summary>
        public void SetBlack(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height)
            {
                return;
            }

            Pixels[(y * _bytesPerRow) + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
        }
    }

    private sealed class BitStreamWriter
    {
        private readonly List<byte> _bytes = new(1024);
        private byte _currentByte;
        private int _bitCount;

        public void WriteBits(int code, int length)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                int bit = (code >> i) & 1;
                _currentByte = (byte)((_currentByte << 1) | bit);
                _bitCount++;
                if (_bitCount == 8)
                {
                    _bytes.Add(_currentByte);
                    _currentByte = 0;
                    _bitCount = 0;
                }
            }
        }

        public void Flush()
        {
            if (_bitCount > 0)
            {
                _currentByte = (byte)(_currentByte << (8 - _bitCount));
                _bytes.Add(_currentByte);
                _currentByte = 0;
                _bitCount = 0;
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }

    // 5x7 ASCII font glyphs (ASCII 32..126, 5 columns each)
    private static readonly byte[] Font5X7 = new byte[]
    {
        0x00, 0x00, 0x00, 0x00, 0x00, // 32 ' '
        0x00, 0x00, 0x5F, 0x00, 0x00, // 33 '!'
        0x00, 0x07, 0x00, 0x07, 0x00, // 34 '"'
        0x14, 0x7F, 0x14, 0x7F, 0x14, // 35 '#'
        0x24, 0x2A, 0x7F, 0x2A, 0x12, // 36 '$'
        0x23, 0x13, 0x08, 0x64, 0x62, // 37 '%'
        0x36, 0x49, 0x55, 0x22, 0x50, // 38 '&'
        0x00, 0x05, 0x03, 0x00, 0x00, // 39 '\''
        0x00, 0x1C, 0x22, 0x41, 0x00, // 40 '('
        0x00, 0x41, 0x22, 0x1C, 0x00, // 41 ')'
        0x08, 0x2A, 0x1C, 0x2A, 0x08, // 42 '*'
        0x08, 0x08, 0x3E, 0x08, 0x08, // 43 '+'
        0x00, 0x50, 0x30, 0x00, 0x00, // 44 ','
        0x08, 0x08, 0x08, 0x08, 0x08, // 45 '-'
        0x00, 0x60, 0x60, 0x00, 0x00, // 46 '.'
        0x20, 0x10, 0x08, 0x04, 0x02, // 47 '/'
        0x3E, 0x51, 0x49, 0x45, 0x3E, // 48 '0'
        0x00, 0x42, 0x7F, 0x40, 0x00, // 49 '1'
        0x42, 0x61, 0x51, 0x49, 0x46, // 50 '2'
        0x21, 0x41, 0x45, 0x4B, 0x31, // 51 '3'
        0x18, 0x14, 0x12, 0x7F, 0x10, // 52 '4'
        0x27, 0x45, 0x45, 0x45, 0x39, // 53 '5'
        0x3C, 0x4A, 0x49, 0x49, 0x30, // 54 '6'
        0x01, 0x71, 0x09, 0x05, 0x03, // 55 '7'
        0x36, 0x49, 0x49, 0x49, 0x36, // 56 '8'
        0x06, 0x49, 0x49, 0x29, 0x1E, // 57 '9'
        0x00, 0x36, 0x36, 0x00, 0x00, // 58 ':'
        0x00, 0x56, 0x36, 0x00, 0x00, // 59 ';'
        0x08, 0x14, 0x22, 0x41, 0x00, // 60 '<'
        0x14, 0x14, 0x14, 0x14, 0x14, // 61 '='
        0x00, 0x41, 0x22, 0x14, 0x08, // 62 '>'
        0x02, 0x01, 0x51, 0x09, 0x06, // 63 '?'
        0x32, 0x49, 0x79, 0x41, 0x3E, // 64 '@'
        0x7E, 0x11, 0x11, 0x11, 0x7E, // 65 'A'
        0x7F, 0x49, 0x49, 0x49, 0x36, // 66 'B'
        0x3E, 0x41, 0x41, 0x41, 0x22, // 67 'C'
        0x7F, 0x41, 0x41, 0x22, 0x1C, // 68 'D'
        0x7F, 0x49, 0x49, 0x49, 0x41, // 69 'E'
        0x7F, 0x09, 0x09, 0x09, 0x01, // 70 'F'
        0x3E, 0x41, 0x49, 0x49, 0x7A, // 71 'G'
        0x7F, 0x08, 0x08, 0x08, 0x7F, // 72 'H'
        0x00, 0x41, 0x7F, 0x41, 0x00, // 73 'I'
        0x20, 0x40, 0x41, 0x3F, 0x01, // 74 'J'
        0x7F, 0x08, 0x14, 0x22, 0x41, // 75 'K'
        0x7F, 0x40, 0x40, 0x40, 0x40, // 76 'L'
        0x7F, 0x02, 0x04, 0x02, 0x7F, // 77 'M'
        0x7F, 0x04, 0x08, 0x10, 0x7F, // 78 'N'
        0x3E, 0x41, 0x41, 0x41, 0x3E, // 79 'O'
        0x7F, 0x09, 0x09, 0x09, 0x06, // 80 'P'
        0x3E, 0x41, 0x51, 0x21, 0x5E, // 81 'Q'
        0x7F, 0x09, 0x19, 0x29, 0x46, // 82 'R'
        0x46, 0x49, 0x49, 0x49, 0x31, // 83 'S'
        0x01, 0x01, 0x7F, 0x01, 0x01, // 84 'T'
        0x3F, 0x40, 0x40, 0x40, 0x3F, // 85 'U'
        0x1F, 0x20, 0x40, 0x20, 0x1F, // 86 'V'
        0x7F, 0x20, 0x18, 0x20, 0x7F, // 87 'W'
        0x63, 0x14, 0x08, 0x14, 0x63, // 88 'X'
        0x07, 0x08, 0x70, 0x08, 0x07, // 89 'Y'
        0x61, 0x51, 0x49, 0x45, 0x43, // 90 'Z'
        0x00, 0x7F, 0x41, 0x41, 0x00, // 91 '['
        0x02, 0x04, 0x08, 0x10, 0x20, // 92 '\'
        0x00, 0x41, 0x41, 0x7F, 0x00, // 93 ']'
        0x04, 0x02, 0x01, 0x02, 0x04, // 94 '^'
        0x40, 0x40, 0x40, 0x40, 0x40, // 95 '_'
        0x00, 0x01, 0x02, 0x04, 0x00, // 96 '`'
        0x20, 0x54, 0x54, 0x54, 0x78, // 97 'a'
        0x7F, 0x48, 0x44, 0x44, 0x38, // 98 'b'
        0x38, 0x44, 0x44, 0x44, 0x20, // 99 'c'
        0x38, 0x44, 0x44, 0x48, 0x7F, // 100 'd'
        0x38, 0x54, 0x54, 0x54, 0x18, // 101 'e'
        0x08, 0x7E, 0x09, 0x01, 0x02, // 102 'f'
        0x0C, 0x52, 0x52, 0x52, 0x3E, // 103 'g'
        0x7F, 0x08, 0x04, 0x04, 0x78, // 104 'h'
        0x00, 0x44, 0x7D, 0x40, 0x00, // 105 'i'
        0x20, 0x40, 0x44, 0x3D, 0x00, // 106 'j'
        0x7F, 0x10, 0x28, 0x44, 0x00, // 107 'k'
        0x00, 0x41, 0x7F, 0x40, 0x00, // 108 'l'
        0x7C, 0x04, 0x18, 0x04, 0x78, // 109 'm'
        0x7C, 0x08, 0x04, 0x04, 0x78, // 110 'n'
        0x38, 0x44, 0x44, 0x44, 0x38, // 111 'o'
        0x7C, 0x14, 0x14, 0x14, 0x08, // 112 'p'
        0x08, 0x14, 0x14, 0x18, 0x7C, // 113 'q'
        0x7C, 0x08, 0x04, 0x04, 0x08, // 114 'r'
        0x48, 0x54, 0x54, 0x54, 0x20, // 115 's'
        0x04, 0x3F, 0x44, 0x40, 0x20, // 116 't'
        0x3C, 0x40, 0x40, 0x20, 0x7C, // 117 'u'
        0x1C, 0x20, 0x40, 0x20, 0x1C, // 118 'v'
        0x3C, 0x40, 0x30, 0x40, 0x3C, // 119 'w'
        0x44, 0x28, 0x10, 0x28, 0x44, // 120 'x'
        0x0C, 0x50, 0x50, 0x50, 0x3C, // 121 'y'
        0x44, 0x64, 0x54, 0x4C, 0x44, // 122 'z'
        0x00, 0x08, 0x36, 0x41, 0x00, // 123 '{'
        0x00, 0x00, 0x7F, 0x00, 0x00, // 124 '|'
        0x00, 0x41, 0x36, 0x08, 0x00, // 125 '}'
        0x10, 0x08, 0x08, 0x10, 0x08, // 126 '~'
    };

    // White terminating codes (0..63)
    private static readonly (byte Length, ushort Code)[] WhiteTerminating = new (byte, ushort)[]
    {
        (8, 0x0035), // 0
        (6, 0x0007), // 1
        (4, 0x0007), // 2
        (4, 0x0008), // 3
        (4, 0x000B), // 4
        (4, 0x000C), // 5
        (4, 0x000E), // 6
        (4, 0x000F), // 7
        (5, 0x0013), // 8
        (5, 0x0014), // 9
        (5, 0x0007), // 10
        (5, 0x0008), // 11
        (6, 0x0008), // 12
        (6, 0x0003), // 13
        (6, 0x0034), // 14
        (6, 0x0035), // 15
        (6, 0x002A), // 16
        (6, 0x002B), // 17
        (7, 0x0027), // 18
        (7, 0x000C), // 19
        (7, 0x0008), // 20
        (7, 0x0017), // 21
        (7, 0x0003), // 22
        (7, 0x0004), // 23
        (7, 0x0028), // 24
        (7, 0x002B), // 25
        (7, 0x0013), // 26
        (7, 0x0024), // 27
        (7, 0x0018), // 28
        (8, 0x0002), // 29
        (8, 0x0003), // 30
        (8, 0x001A), // 31
        (8, 0x001B), // 32
        (8, 0x0012), // 33
        (8, 0x0013), // 34
        (8, 0x0014), // 35
        (8, 0x0015), // 36
        (8, 0x0016), // 37
        (8, 0x0017), // 38
        (8, 0x0028), // 39
        (8, 0x0029), // 40
        (8, 0x002A), // 41
        (8, 0x002B), // 42
        (8, 0x002C), // 43
        (8, 0x002D), // 44
        (8, 0x0004), // 45
        (8, 0x0005), // 46
        (8, 0x000A), // 47
        (8, 0x000B), // 48
        (8, 0x0052), // 49
        (8, 0x0053), // 50
        (8, 0x0054), // 51
        (8, 0x0055), // 52
        (8, 0x0024), // 53
        (8, 0x0025), // 54
        (8, 0x0058), // 55
        (8, 0x0059), // 56
        (8, 0x005A), // 57
        (8, 0x005B), // 58
        (8, 0x004A), // 59
        (8, 0x004B), // 60
        (8, 0x0032), // 61
        (8, 0x0033), // 62
        (8, 0x0034), // 63
    };

    // Black terminating codes (0..63)
    private static readonly (byte Length, ushort Code)[] BlackTerminating = new (byte, ushort)[]
    {
        (10, 0x0037), // 0
        (3, 0x0002), // 1
        (2, 0x0003), // 2
        (2, 0x0002), // 3
        (3, 0x0003), // 4
        (4, 0x0003), // 5
        (4, 0x0002), // 6
        (5, 0x0003), // 7
        (6, 0x0005), // 8
        (6, 0x0004), // 9
        (7, 0x0004), // 10
        (7, 0x0005), // 11
        (7, 0x0007), // 12
        (8, 0x0004), // 13
        (8, 0x0007), // 14
        (9, 0x0018), // 15
        (10, 0x0017), // 16
        (10, 0x0018), // 17
        (10, 0x0008), // 18
        (11, 0x0067), // 19
        (11, 0x0068), // 20
        (11, 0x006C), // 21
        (11, 0x0037), // 22
        (11, 0x0028), // 23
        (11, 0x0017), // 24
        (11, 0x0018), // 25
        (12, 0x00CA), // 26
        (12, 0x00CB), // 27
        (12, 0x00CC), // 28
        (12, 0x00CD), // 29
        (12, 0x0068), // 30
        (12, 0x0069), // 31
        (12, 0x006A), // 32
        (12, 0x006B), // 33
        (12, 0x00D2), // 34
        (12, 0x00D3), // 35
        (12, 0x00D4), // 36
        (12, 0x00D5), // 37
        (12, 0x00D6), // 38
        (12, 0x00D7), // 39
        (12, 0x006C), // 40
        (12, 0x006D), // 41
        (12, 0x00DA), // 42
        (12, 0x00DB), // 43
        (12, 0x0054), // 44
        (12, 0x0055), // 45
        (12, 0x0056), // 46
        (12, 0x0057), // 47
        (12, 0x0064), // 48
        (12, 0x0065), // 49
        (12, 0x0052), // 50
        (12, 0x0053), // 51
        (12, 0x0024), // 52
        (12, 0x0037), // 53
        (12, 0x0038), // 54
        (12, 0x0027), // 55
        (12, 0x0028), // 56
        (12, 0x0058), // 57
        (12, 0x0059), // 58
        (12, 0x002B), // 59
        (12, 0x002C), // 60
        (12, 0x005A), // 61
        (12, 0x0066), // 62
        (12, 0x0067), // 63
    };

    // White makeup codes (64..2560 multiples of 64, index = runLength / 64 - 1)
    private static readonly (byte Length, ushort Code)[] WhiteMakeup = new (byte, ushort)[]
    {
        (5, 0x001B), // 64
        (5, 0x0012), // 128
        (6, 0x0017), // 192
        (7, 0x0037), // 256
        (8, 0x0036), // 320
        (8, 0x0037), // 384
        (8, 0x0064), // 448
        (8, 0x0065), // 512
        (8, 0x0068), // 576
        (8, 0x0067), // 640
        (9, 0x00CC), // 704
        (9, 0x00CD), // 768
        (9, 0x00D2), // 832
        (9, 0x00D3), // 896
        (9, 0x00D4), // 960
        (9, 0x00D5), // 1024
        (9, 0x00D6), // 1088
        (9, 0x00D7), // 1152
        (9, 0x00D8), // 1216
        (9, 0x00D9), // 1280
        (9, 0x00DA), // 1344
        (9, 0x00DB), // 1408
        (9, 0x0098), // 1472
        (9, 0x0099), // 1536
        (9, 0x009A), // 1600
        (6, 0x0018), // 1664
        (9, 0x009B), // 1728
        (11, 0x0008), // 1792
        (11, 0x000C), // 1856
        (11, 0x000D), // 1920
        (12, 0x0012), // 1984
        (12, 0x0013), // 2048
        (12, 0x0014), // 2112
        (12, 0x0015), // 2176
        (12, 0x0016), // 2240
        (12, 0x0017), // 2304
        (12, 0x001C), // 2368
        (12, 0x001D), // 2432
        (12, 0x001E), // 2496
        (12, 0x001F), // 2560
    };

    // Black makeup codes (64..2560 multiples of 64, index = runLength / 64 - 1)
    private static readonly (byte Length, ushort Code)[] BlackMakeup = new (byte, ushort)[]
    {
        (10, 0x000F), // 64
        (12, 0x00C8), // 128
        (12, 0x00C9), // 192
        (12, 0x005B), // 256
        (12, 0x0033), // 320
        (12, 0x0034), // 384
        (12, 0x0035), // 448
        (13, 0x006C), // 512
        (13, 0x006D), // 576
        (13, 0x004A), // 640
        (13, 0x004B), // 704
        (13, 0x004C), // 768
        (13, 0x004D), // 832
        (13, 0x0072), // 896
        (13, 0x0073), // 960
        (13, 0x0074), // 1024
        (13, 0x0075), // 1088
        (13, 0x0076), // 1152
        (13, 0x0077), // 1216
        (13, 0x0052), // 1280
        (13, 0x0053), // 1344
        (13, 0x0054), // 1408
        (13, 0x0055), // 1472
        (13, 0x005A), // 1536
        (13, 0x005B), // 1600
        (13, 0x0064), // 1664
        (13, 0x0065), // 1728
        (11, 0x0008), // 1792
        (11, 0x000C), // 1856
        (11, 0x000D), // 1920
        (12, 0x0012), // 1984
        (12, 0x0013), // 2048
        (12, 0x0014), // 2112
        (12, 0x0015), // 2176
        (12, 0x0016), // 2240
        (12, 0x0017), // 2304
        (12, 0x001C), // 2368
        (12, 0x001D), // 2432
        (12, 0x001E), // 2496
        (12, 0x001F), // 2560
    };
}
