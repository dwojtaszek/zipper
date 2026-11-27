using System.IO.Compression;
using Xunit;
using Zipper.Config;

namespace Zipper.Tests.TestHelpers;

public static class ZipAssert
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "LPT¹", "LPT²", "LPT³",
    };
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
            Assert.DoesNotContain('\\', entry.FullName);
            var normalizedName = entry.FullName.Replace('\\', '/');
            Assert.False(
                normalizedName.StartsWith('/') ||
                (normalizedName.Length >= 2 && char.IsLetter(normalizedName[0]) && normalizedName[1] == ':'),
                $"ZIP entry name must be relative: '{entry.FullName}'.");
            var segments = normalizedName.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (i == segments.Length - 1 && segments[i].Length == 0 && normalizedName.EndsWith('/'))
                {
                    continue;
                }

                Assert.False(
                    segments[i] is ".." or "." || segments[i].Length == 0,
                    $"ZIP entry name has an unsupported path segment: '{entry.FullName}'.");
                Assert.False(
                    segments[i].EndsWith('.') || segments[i].EndsWith(' '),
                    $"ZIP entry '{entry.FullName}' has path segment '{segments[i]}' ending in a period or space, which is invalid on Windows.");

                var stemEnd = segments[i].IndexOf('.');
                var stem = (stemEnd >= 0 ? segments[i][..stemEnd] : segments[i]).TrimEnd(' ', '.');
                Assert.False(
                    ReservedDeviceNames.Contains(stem),
                    $"ZIP entry '{entry.FullName}' has path segment '{segments[i]}' which is a reserved Windows device name.");
            }
        }

        var allEntryNames = archive.Entries
            .Select(e => e.FullName)
            .ToList();
        Assert.Equal(allEntryNames.Count, allEntryNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var fileEntryNames = allEntryNames
            .Where(name => !name.EndsWith('/'))
            .ToList();

        // A path cannot be both a file and a directory: extraction fails on the collision,
        // including directories implied by a child entry.
        var directoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var entryPath = entry.FullName.TrimEnd('/');
            if (entry.FullName.EndsWith('/'))
            {
                directoryPaths.Add(entryPath);
            }

            var separatorIndex = entryPath.LastIndexOf('/');
            while (separatorIndex > 0)
            {
                entryPath = entryPath[..separatorIndex];
                directoryPaths.Add(entryPath);
                separatorIndex = entryPath.LastIndexOf('/');
            }
        }

        foreach (var fileName in fileEntryNames)
        {
            Assert.False(
                directoryPaths.Contains(fileName),
                $"ZIP entry path is used as both a file and a directory: '{fileName}'.");
        }

        // Decompress every member through SharpZipLib so the assertion does not only prove
        // that the writer and System.IO.Compression agree with each other.
        AssertSharpZipLibPayloads(zipPath);

        extraAsserts?.Invoke(archive);
    }

    public static void AssertProductionZipMatchesDisk(
        string zipPath,
        string productionDir,
        ZipCompressionMethod expectedMethod)
    {
        Assert.True(File.Exists(zipPath), $"Expected production zip at '{zipPath}' to exist.");
        Assert.True(Directory.Exists(productionDir), $"Expected production directory at '{productionDir}' to exist.");

        // First, assert basic ZIP validity, full decompression, and CRC integrity.
        AssertValidZip(zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.NotEmpty(archive.Entries);

        var normalizedProductionDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(productionDir));
        var prodName = Path.GetFileName(normalizedProductionDir);
        var rootPrefix = prodName + "/";

        // Root layout assertion: all entries must start with the intended root prefix.
        foreach (var entry in archive.Entries)
        {
            Assert.StartsWith(rootPrefix, entry.FullName, StringComparison.Ordinal);
        }

        // Method code assertion (Store = 0, Deflate = 8). A Store request must always
        // require Store (method 0), even for empty files. For Deflate, the .NET writer
        // stores empty entries with method 0, so empty entries may be Stored while
        // non-empty members must be Deflated.
        int expectedMethodCode = expectedMethod == ZipCompressionMethod.Store ? 0 : 8;
        using (var sharpZip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipPath))
        {
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in sharpZip)
            {
                if (!entry.IsDirectory)
                {
                    if (expectedMethod == ZipCompressionMethod.Store)
                    {
                        Assert.Equal(0, (int)entry.CompressionMethod);
                    }
                    else if (entry.Size > 0)
                    {
                        Assert.Equal(expectedMethodCode, (int)entry.CompressionMethod);
                    }
                    else
                    {
                        var method = (int)entry.CompressionMethod;
                        Assert.True(method == 0 || method == expectedMethodCode, $"Unexpected compression method {method} for empty entry '{entry.Name}'.");
                    }
                }
            }
        }

        // Compare complete Archive file-entry and directory namespace against on-disk tree using the independent reader.
        var parentDir = Path.GetDirectoryName(normalizedProductionDir)!;
        var diskFiles = Directory.GetFiles(productionDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(parentDir, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var diskDirs = Directory.GetDirectories(productionDir, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(parentDir, d).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        using (var sharpZip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipPath))
        {
            var archiveFileEntries = new HashSet<string>(StringComparer.Ordinal);
            var archiveDirs = new HashSet<string>(StringComparer.Ordinal);
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in sharpZip)
            {
                Assert.Equal(entry.Name.EndsWith('/'), entry.IsDirectory);
                if (!entry.IsDirectory)
                {
                    archiveFileEntries.Add(entry.Name);
                }

                var entryName = entry.Name.TrimEnd('/');
                if (entry.IsDirectory || entry.Name.EndsWith('/'))
                {
                    if (!string.Equals(entryName, prodName, StringComparison.Ordinal))
                    {
                        archiveDirs.Add(entryName);
                    }
                }

                var slashIdx = entryName.LastIndexOf('/');
                while (slashIdx > prodName.Length)
                {
                    var dir = entryName[..slashIdx];
                    archiveDirs.Add(dir);
                    slashIdx = dir.LastIndexOf('/');
                }
            }

            // Missing entries or extra entries must fail.
            Assert.True(
                diskFiles.SetEquals(archiveFileEntries),
                $"Archive entries do not match on-disk tree. Missing from archive: [{string.Join(", ", diskFiles.Except(archiveFileEntries))}]. Extra in archive: [{string.Join(", ", archiveFileEntries.Except(diskFiles))}].");

            Assert.True(
                diskDirs.SetEquals(archiveDirs),
                $"Archive directories do not match on-disk tree. Missing from archive: [{string.Join(", ", diskDirs.Except(archiveDirs))}]. Extra in archive: [{string.Join(", ", archiveDirs.Except(diskDirs))}].");

            // Verify DATA load files and manifest are present.
            Assert.Contains(archiveFileEntries, e => e.EndsWith("/_manifest.json", StringComparison.Ordinal));
            Assert.Contains(archiveFileEntries, e => e.Contains("/DATA/", StringComparison.Ordinal));

            // Hash each entry and its disk file incrementally using the independent reader so memory
            // stays bounded by the hash buffer, not by the largest Native File.
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in sharpZip)
            {
                if (entry.IsDirectory || entry.Name.EndsWith('/'))
                {
                    continue;
                }

                var diskPath = Path.Combine(parentDir, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(diskPath), $"Matching disk file not found for archive entry: {entry.Name}");

                using var entryStream = sharpZip.GetInputStream(entry);
                using var diskStream = File.OpenRead(diskPath);
                Assert.Equal(diskStream.Length, entry.Size);

                var entryHash = System.Security.Cryptography.SHA256.HashData(entryStream);
                var diskHash = System.Security.Cryptography.SHA256.HashData(diskStream);

                Assert.True(
                    diskHash.AsSpan().SequenceEqual(entryHash),
                    $"Content mismatch for entry '{entry.Name}' against disk file '{diskPath}'.");
            }
        }
    }

    private static void AssertSharpZipLibPayloads(string zipPath)
    {
        byte[] buffer = new byte[8192];
        using var sharpZip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipPath);
        Assert.True(sharpZip.TestArchive(testData: false), $"SharpZipLib header integrity test failed for '{zipPath}'.");
        foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in sharpZip)
        {
            if (entry.IsDirectory)
            {
                Assert.Equal(0, entry.Size);
                Assert.Equal(0u, (uint)entry.Crc);
            }

            using var stream = sharpZip.GetInputStream(entry);
            var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
            long totalBytes = 0;
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc.Update(new ArraySegment<byte>(buffer, 0, bytesRead));
                totalBytes += bytesRead;
            }

            Assert.Equal(entry.Size, totalBytes);
            Assert.Equal((uint)entry.Crc, (uint)crc.Value);
        }
    }
}
