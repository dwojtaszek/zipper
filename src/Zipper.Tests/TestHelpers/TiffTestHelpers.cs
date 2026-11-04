using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit;

namespace Zipper.Tests.TestHelpers;

/// <summary>
/// Shared TIFF inspection helpers: reads tags from the first Image File Directory, counts pages
/// through the IFD chain, and runs external libtiff/ImageMagick tools for decoder checks.
/// </summary>
internal static class TiffTestHelpers
{
    public static Dictionary<ushort, (ushort Type, uint Count, uint Value)> ReadFirstIfdTags(byte[] tiff)
    {
        bool littleEndian = tiff[0] == 0x49 && tiff[1] == 0x49;

        ushort ReadUInt16(int offset) => littleEndian
            ? (ushort)(tiff[offset] | (tiff[offset + 1] << 8))
            : (ushort)((tiff[offset] << 8) | tiff[offset + 1]);

        uint ReadUInt32(int offset) => littleEndian
            ? (uint)(tiff[offset] | (tiff[offset + 1] << 8) | (tiff[offset + 2] << 16) | (tiff[offset + 3] << 24))
            : ((uint)tiff[offset] << 24) | ((uint)tiff[offset + 1] << 16) | ((uint)tiff[offset + 2] << 8) | tiff[offset + 3];

        var dict = new Dictionary<ushort, (ushort Type, uint Count, uint Value)>();
        uint ifdOffset = ReadUInt32(4);
        int entryCount = ReadUInt16((int)ifdOffset);
        int current = (int)ifdOffset + 2;

        for (int i = 0; i < entryCount; i++)
        {
            ushort tag = ReadUInt16(current);
            ushort type = ReadUInt16(current + 2);
            uint count = ReadUInt32(current + 4);
            uint value = ReadUInt32(current + 8);
            dict[tag] = (type, count, value);
            current += 12;
        }

        return dict;
    }

    /// <summary>
    /// Decodes every entry of the first Image File Directory as "type:count:values", honoring the byte order from
    /// the magic bytes. SHORT values are read from their own two bytes, so a little-endian and a big-endian file
    /// with the same content decode to the same fields.
    /// </summary>
    public static Dictionary<ushort, string> ReadFirstIfdFields(byte[] tiff)
    {
        bool bigEndian = tiff[0] == (byte)'M';
        var fields = new Dictionary<ushort, string>();
        uint ifdOffset = ReadUInt32(tiff, 4, bigEndian);
        int entryCount = ReadUInt16(tiff, (int)ifdOffset, bigEndian);

        for (int i = 0; i < entryCount; i++)
        {
            int entry = (int)ifdOffset + 2 + (12 * i);
            ushort tag = ReadUInt16(tiff, entry, bigEndian);
            ushort type = ReadUInt16(tiff, entry + 2, bigEndian);
            uint count = ReadUInt32(tiff, entry + 4, bigEndian);
            int unitSize = type switch
            {
                2 => 1,
                3 => 2,
                4 => 4,
                5 => 8,
                _ => throw new NotSupportedException($"Field type {type} is not decoded by this helper."),
            };

            // Values of four bytes or less sit inline in the entry; larger values are stored at the offset they name.
            int valueAt = (long)unitSize * count <= 4 ? entry + 8 : (int)ReadUInt32(tiff, entry + 8, bigEndian);
            string summary = type switch
            {
                2 => Encoding.ASCII.GetString(tiff, valueAt, (int)count),
                3 => string.Join(",", Enumerable.Range(0, (int)count).Select(v => ReadUInt16(tiff, valueAt + (2 * v), bigEndian).ToString(CultureInfo.InvariantCulture))),
                4 => string.Join(",", Enumerable.Range(0, (int)count).Select(v => ReadUInt32(tiff, valueAt + (4 * v), bigEndian).ToString(CultureInfo.InvariantCulture))),
                _ => string.Join(",", Enumerable.Range(0, (int)count).Select(v =>
                    $"{ReadUInt32(tiff, valueAt + (8 * v), bigEndian)}/{ReadUInt32(tiff, valueAt + (8 * v) + 4, bigEndian)}")),
            };
            fields[tag] = $"{type}:{count}:{summary}";
        }

        return fields;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadUInt32(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    /// <summary>
    /// Walks a TIFF's Image File Directory chain and counts its pages, reading offsets in the
    /// endianness declared by the magic bytes. A cycle guard bounds malformed inputs.
    /// </summary>
    public static int CountTiffPages(byte[] tiff)
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

    /// <summary>
    /// Returns the full path of an executable found on PATH, or null when it is not installed.
    /// </summary>
    public static string? FindToolOnPath(string tool)
    {
        var names = OperatingSystem.IsWindows() ? new[] { tool + ".exe", tool } : new[] { tool };
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static (int ExitCode, string StdOut, string StdErr) RunTool(string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        var stdErrTask = process.StandardError.ReadToEndAsync();
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = stdErrTask.GetAwaiter().GetResult();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }
}
