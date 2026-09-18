using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #931: the Deflate64-specific valid control carries a genuine
/// Deflate64-only stream (a 38,000-byte match distance enabled by the 64 KiB
/// window), pinned from 7-Zip output. An ordinary 32 KiB DEFLATE decoder
/// cannot decode it; a Deflate64-capable decoder (7-Zip, E2E) extracts exact
/// expected bytes.
/// </summary>
public class Deflate64LongMatchTests : TempDirectoryTestBase
{
    private const string CaseKey = "valid-deflate64-long-match";

    // Provenance: content recipe below compressed with
    // `7zz a -tzip -mm=Deflate64 -mx=9` (7-Zip 26.03, linux-x64).
    private const string ExpectedContentSha256 =
        "92267b4cc99ade8b536e3c1794cabf9cd795d5a9c766993086d8caa17cdbce5e";
    private const int ExpectedContentLength = 42000;
    private const int ExpectedMaxMatchDistance = 38000;

    [Fact]
    public void Catalog_ContainsDeflate64LongMatchControl()
    {
        var keys = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.AllSuites)
            .Select(definition => definition.CaseKey);
        Assert.Contains(CaseKey, keys);
    }

    [Fact]
    public void Build_ContentMatchesProvenanceHash()
    {
        var definition = ArchiveTestCatalog.GetCase(CaseKey);
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var entry = Assert.Single(artifact.Entries);
        Assert.Equal(ExpectedContentSha256, entry.ContentSha256);
        Assert.Equal(ExpectedContentLength, entry.Content.Length);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Build_ContentIsSeedIndependent(int seed)
    {
        // The pinned stream only matches one content: the recipe ignores the
        // seed so every seed publishes the same valid control.
        var definition = ArchiveTestCatalog.GetCase(CaseKey);
        var artifact = ArchiveFixtureBuilder.Build(definition, seed, CancellationToken.None);
        var entry = Assert.Single(artifact.Entries);
        Assert.Equal(ExpectedContentSha256, entry.ContentSha256);
    }

    [Fact]
    public void Build_StreamUsesDeflate64OnlyDistance()
    {
        var definition = ArchiveTestCatalog.GetCase(CaseKey);
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var raw = ReadRawStream(artifact.ArchiveBytes, "long-match.bin");
        Assert.Equal(ExpectedMaxMatchDistance, MaxMatchDistance(raw));
    }

    [Fact]
    public void Build_OrdinaryDeflateDecoderCannotDecodeStream()
    {
        var definition = ArchiveTestCatalog.GetCase(CaseKey);
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var raw = ReadRawStream(artifact.ArchiveBytes, "long-match.bin");
        var expected = Assert.Single(artifact.Entries).Content;
        try
        {
            using var input = new MemoryStream(raw, writable: false);
            using var decoder = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            decoder.CopyTo(output);
            Assert.NotEqual(
                Convert.ToHexStringLower(SHA256.HashData(expected)),
                Convert.ToHexStringLower(SHA256.HashData(output.ToArray())));
        }
        catch (InvalidDataException)
        {
            // An ordinary 32 KiB DEFLATE decoder rejects the Deflate64-only
            // distance codes outright.
        }
        catch (IOException)
        {
            // Runtimes may surface the truncated-window failure as I/O instead.
        }
    }

    private static byte[] ReadRawStream(byte[] archiveBytes, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var offset = FindBytes(archiveBytes, [0x50, 0x4b, 0x03, 0x04]);
        Assert.True(offset >= 0, "local header signature not found");
        var entryNameLength = BitConverter.ToUInt16(archiveBytes, offset + 26);
        var extraLength = BitConverter.ToUInt16(archiveBytes, offset + 28);
        Assert.Equal(nameBytes.Length, entryNameLength);
        var compressedSize = BitConverter.ToUInt32(archiveBytes, offset + 18);
        var dataOffset = offset + 30 + entryNameLength + extraLength;
        Assert.True((long)dataOffset + compressedSize <= archiveBytes.Length, "stream range exceeds the Archive");
        return archiveBytes[dataOffset..(dataOffset + (int)compressedSize)];
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Minimal raw-deflate symbol walk reporting the maximum match distance.
    /// Literals are skipped; only length/distance pairs matter. Deflate64
    /// distance codes 30-31 carry 14 extra bits each. Limitation: fixed blocks
    /// always declare the 32-code distance alphabet, so codes 30-31 inside a
    /// fixed block read as Deflate64 — fine for this pinned dynamic-block
    /// stream, not a general decoder.
    /// </summary>
    internal static int MaxMatchDistance(byte[] raw)
    {
        var reader = new BitReader(raw);
        var maxDistance = 0;
        while (true)
        {
            var final = reader.ReadBits(1);
            var type = reader.ReadBits(2);
            if (type == 0)
            {
                reader.AlignToByte();
                var length = reader.ReadBits(16);
                reader.ReadBits(16);
                reader.SkipBytes(length);
            }
            else if (type is 1 or 2)
            {
                int[] literalLengths;
                int[] distanceLengths;
                if (type == 1)
                {
                    literalLengths = [.. Enumerable.Repeat(8, 144), .. Enumerable.Repeat(9, 112), .. Enumerable.Repeat(7, 24), .. Enumerable.Repeat(8, 8)];
                    distanceLengths = Enumerable.Repeat(5, 32).ToArray();
                }
                else
                {
                    var hlit = reader.ReadBits(5) + 257;
                    var hdist = reader.ReadBits(5) + 1;
                    var hclen = reader.ReadBits(4) + 4;
                    var order = new[] { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };
                    var codeLengths = new int[19];
                    for (var i = 0; i < hclen; i++)
                    {
                        codeLengths[order[i]] = reader.ReadBits(3);
                    }

                    var codeLengthTree = HuffmanTree.Build(codeLengths);
                    var lengths = new List<int>();
                    while (lengths.Count < hlit + hdist)
                    {
                        var symbol = codeLengthTree.Decode(reader);
                        if (symbol <= 15)
                        {
                            lengths.Add(symbol);
                        }
                        else if (symbol == 16)
                        {
                            lengths.AddRange(Enumerable.Repeat(lengths[^1], 3 + reader.ReadBits(2)));
                        }
                        else if (symbol == 17)
                        {
                            lengths.AddRange(Enumerable.Repeat(0, 3 + reader.ReadBits(3)));
                        }
                        else
                        {
                            lengths.AddRange(Enumerable.Repeat(0, 11 + reader.ReadBits(7)));
                        }
                    }

                    literalLengths = lengths.Take(hlit).ToArray();
                    distanceLengths = lengths.Skip(hlit).ToArray();
                }

                var literalTree = HuffmanTree.Build(literalLengths);
                var distanceTree = HuffmanTree.Build(distanceLengths);
                var isDeflate64 = distanceLengths.Length > 30;
                while (true)
                {
                    var symbol = literalTree.Decode(reader);
                    if (symbol < 256)
                    {
                        continue;
                    }

                    if (symbol == 256)
                    {
                        break;
                    }

                    var lengthIndex = symbol - 257;
                    reader.ReadBits(LengthExtraBits[lengthIndex]);
                    var distanceCode = distanceTree.Decode(reader);
                    int distance;
                    if (isDeflate64 && distanceCode >= 30)
                    {
                        distance = (distanceCode == 30 ? 32769 : 49153) + reader.ReadBits(14);
                    }
                    else if (distanceCode < DistanceBases.Length)
                    {
                        distance = DistanceBases[distanceCode] + reader.ReadBits(DistanceExtraBits[distanceCode]);
                    }
                    else
                    {
                        throw new InvalidDataException($"Distance code {distanceCode} is outside the 32 KiB alphabet.");
                    }

                    maxDistance = Math.Max(maxDistance, distance);
                }
            }
            else
            {
                throw new InvalidDataException($"Reserved deflate block type {type}.");
            }

            if (final == 1)
            {
                break;
            }
        }

        return maxDistance;
    }

    private static readonly int[] LengthExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0,
    ];

    private static readonly int[] DistanceBases =
    [
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577,
    ];

    private static readonly int[] DistanceExtraBits =
    [
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13,
    ];

    private sealed class BitReader(byte[] data)
    {
        private int _buffer;
        private int _available;
        private int _position;

        public int ReadBits(int count)
        {
            while (_available < count)
            {
                _buffer |= data[_position++] << _available;
                _available += 8;
            }

            var value = _buffer & ((1 << count) - 1);
            _buffer >>= count;
            _available -= count;
            return value;
        }

        public void AlignToByte()
        {
            _buffer = 0;
            _available = 0;
        }

        public void SkipBytes(int count)
        {
            _position += count;
        }
    }

    private sealed class HuffmanTree
    {
        private readonly Dictionary<(int Code, int Length), int> _table = new();

        public static HuffmanTree Build(int[] lengths)
        {
            var tree = new HuffmanTree();
            var maxLength = lengths.Max();
            var counts = new int[maxLength + 1];
            foreach (var length in lengths)
            {
                if (length > 0)
                {
                    counts[length]++;
                }
            }

            var code = 0;
            var next = new int[maxLength + 1];
            for (var bits = 1; bits <= maxLength; bits++)
            {
                code = (code + counts[bits - 1]) << 1;
                next[bits] = code;
            }

            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                if (lengths[symbol] > 0)
                {
                    tree._table[(next[lengths[symbol]]++, lengths[symbol])] = symbol;
                }
            }

            return tree;
        }

        public int Decode(BitReader reader)
        {
            var code = 0;
            for (var length = 1; length <= 15; length++)
            {
                code = (code << 1) | reader.ReadBits(1);
                if (_table.TryGetValue((code, length), out var symbol))
                {
                    return symbol;
                }
            }

            throw new InvalidDataException("Invalid Huffman code in deflate stream.");
        }
    }
}
