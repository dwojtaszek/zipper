using System.IO.Compression;
using Xunit;
using Zipper.Config;

namespace Zipper.Tests.TestHelpers;

public static class ZipAssert
{
    public static void AssertValidZip(string zipPath, Action<ZipArchive>? extraAsserts = null)
    {
        AssertValidZip(zipPath, expectedCount: null, extraAsserts);
    }

    public static void AssertValidZip(string zipPath, int expectedCount, Action<ZipArchive>? extraAsserts = null)
    {
        AssertValidZip(zipPath, (int?)expectedCount, extraAsserts);
    }

    public static void AssertValidZip(string zipPath, int? expectedCount, Action<ZipArchive>? extraAsserts = null)
    {
        Assert.True(File.Exists(zipPath), $"Expected zip file at {zipPath} to exist.");
        using var archive = ZipFile.OpenRead(zipPath);
        if (expectedCount.HasValue)
        {
            Assert.Equal(expectedCount.Value, archive.Entries.Count);
        }

        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
            byte[] buffer = new byte[8192];
            int bytesRead;
            long totalBytes = 0;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc.Update(new ArraySegment<byte>(buffer, 0, bytesRead));
                totalBytes += bytesRead;
            }

            // Keep readability and CRC as separate explicit assertions per issue guidance.
            Assert.Equal(entry.Length, totalBytes);
            Assert.Equal(entry.Crc32, (uint)crc.Value);
        }

        extraAsserts?.Invoke(archive);
    }

    public static void AssertProductionZipMatchesDisk(
        string zipPath,
        string productionDir,
        ZipCompressionMethod expectedMethod)
    {
        Assert.True(File.Exists(zipPath), $"Expected production zip at '{zipPath}' to exist.");
        Assert.True(Directory.Exists(productionDir), $"Expected production directory at '{productionDir}' to exist.");

        // First, assert basic ZIP validity, full decompression, and CRC integrity
        AssertValidZip(zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.NotEmpty(archive.Entries);

        var prodName = Path.GetFileName(Path.TrimEndingDirectorySeparator(productionDir));
        var rootPrefix = prodName + "/";

        // Root layout assertion: all entries must start with the intended root prefix
        foreach (var entry in archive.Entries)
        {
            Assert.StartsWith(rootPrefix, entry.FullName, StringComparison.Ordinal);
        }

        // Method code assertion (Store = 0, Deflate = 8)
        int expectedMethodCode = expectedMethod == ZipCompressionMethod.Store ? 0 : 8;
        using (var sharpZip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipPath))
        {
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in sharpZip)
            {
                if (!entry.IsDirectory)
                {
                    Assert.Equal(expectedMethodCode, (int)entry.CompressionMethod);
                }
            }
        }

        // Compare complete Archive file-entry namespace against on-disk tree
        var parentDir = Path.GetDirectoryName(Path.GetFullPath(productionDir))!;
        var diskFiles = Directory.GetFiles(productionDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(parentDir, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var archiveFileEntries = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToHashSet(StringComparer.Ordinal);

        // Missing entries or extra entries must fail
        Assert.True(
            diskFiles.SetEquals(archiveFileEntries),
            $"Archive entries do not match on-disk tree. Missing from archive: [{string.Join(", ", diskFiles.Except(archiveFileEntries))}]. Extra in archive: [{string.Join(", ", archiveFileEntries.Except(diskFiles))}].");

        // Verify DATA load files and manifest are present
        Assert.Contains(archiveFileEntries, e => e.EndsWith("/_manifest.json", StringComparison.Ordinal));
        Assert.Contains(archiveFileEntries, e => e.Contains("/DATA/", StringComparison.Ordinal));

        // Byte/hash comparison: assert every entry byte matches disk
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            var diskPath = Path.Combine(parentDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(diskPath), $"Matching disk file not found for archive entry: {entry.FullName}");

            using var entryStream = entry.Open();
            using var entryMs = new MemoryStream();
            entryStream.CopyTo(entryMs);
            byte[] entryBytes = entryMs.ToArray();
            byte[] diskBytes = File.ReadAllBytes(diskPath);

            Assert.Equal(diskBytes.Length, entryBytes.Length);
            Assert.True(
                diskBytes.AsSpan().SequenceEqual(entryBytes),
                $"Content mismatch for entry '{entry.FullName}' against disk file '{diskPath}'.");
        }
    }
}
