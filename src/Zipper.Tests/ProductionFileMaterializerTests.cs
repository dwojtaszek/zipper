using System.IO.Compression;
using Xunit;

namespace Zipper.Tests;

/// <summary>
/// Guards output-byte parity of <see cref="ProductionFileMaterializer.CreateZipAsync(string, string, Config.ZipCompressionMethod, CancellationToken)"/>
/// against the legacy <see cref="ZipFile.CreateFromDirectory(string, string, CompressionLevel, bool)"/> behavior it replaced.
/// The golden scenarios do not cover Production Zips, so this is the only byte-level guard for that seam.
/// </summary>
public class ProductionFileMaterializerTests
{
    [Fact]
    public async Task CreateZipAsync_WithNestedFilesAndFixedTimestamps_ProducesByteIdenticalArchiveToZipFileCreateFromDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZipBytesParity_" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "PROD_SET");
        Directory.CreateDirectory(Path.Combine(sourceDir, "NATIVES"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "IMAGES", "VOL001"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "_manifest.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "NATIVES", "a.pdf"), "AAA");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "NATIVES", "b.pdf"), "BBB");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "IMAGES", "VOL001", "c.tif"), "CCC");

        // Fixed timestamps keep the comparison deterministic: entry times are the only
        // per-run variable in the archive bytes once file contents are fixed.
        var fixedTime = new DateTime(2024, 5, 5, 12, 34, 56, DateTimeKind.Utc);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, fixedTime);
        }

        try
        {
            var legacyZip = Path.Combine(root, "legacy.zip");
            var materializerZip = Path.Combine(root, "materializer.zip");

            ZipFile.CreateFromDirectory(sourceDir, legacyZip, CompressionLevel.Optimal, true);
            await new ProductionFileMaterializer().CreateZipAsync(
                sourceDir,
                materializerZip,
                Config.ZipCompressionMethod.Deflate,
                CancellationToken.None);

            var legacyBytes = await File.ReadAllBytesAsync(legacyZip);
            var materializerBytes = await File.ReadAllBytesAsync(materializerZip);

            Assert.Equal(legacyBytes, materializerBytes);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task CreateZipAsync_WithEmptyDirectories_ProducesSameEntryNamesAsZipFileCreateFromDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZipEntryParity_" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "PROD_SET");
        Directory.CreateDirectory(Path.Combine(sourceDir, "NATIVES"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "EMPTY_SUB"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "NATIVES", "a.pdf"), "AAA");
        var emptyRoot = Path.Combine(root, "EMPTY_ROOT");
        Directory.CreateDirectory(emptyRoot);

        try
        {
            var legacyZip = Path.Combine(root, "legacy.zip");
            var materializerZip = Path.Combine(root, "materializer.zip");
            var legacyEmptyZip = Path.Combine(root, "legacy_empty.zip");
            var materializerEmptyZip = Path.Combine(root, "materializer_empty.zip");

            ZipFile.CreateFromDirectory(sourceDir, legacyZip, CompressionLevel.Optimal, true);
            await new ProductionFileMaterializer().CreateZipAsync(
                sourceDir,
                materializerZip,
                Config.ZipCompressionMethod.Deflate,
                CancellationToken.None);

            ZipFile.CreateFromDirectory(emptyRoot, legacyEmptyZip, CompressionLevel.Optimal, true);
            await new ProductionFileMaterializer().CreateZipAsync(
                emptyRoot,
                materializerEmptyZip,
                Config.ZipCompressionMethod.Deflate,
                CancellationToken.None);

            Assert.Equal(ReadEntryNames(legacyZip), ReadEntryNames(materializerZip));
            Assert.Equal(ReadEntryNames(legacyEmptyZip), ReadEntryNames(materializerEmptyZip));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static IReadOnlyList<string> ReadEntryNames(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Select(e => e.FullName).ToList();
    }
}
