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
            if (!OperatingSystem.IsWindows())
            {
                // ZipFile.CreateFromDirectory preserves the source Unix mode in the entry's
                // external attributes while ZipArchive.CreateEntry writes the 0644 default;
                // pin the mode so byte parity does not depend on the host umask.
                File.SetUnixFileMode(
                    file,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
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

    [Fact]
    public void ShouldValidate_Always_ReturnsTrue()
    {
        // The filesystem-backed materializer owns Production Set artifacts, so
        // post-generation validation must run for it.
        var materializer = new ProductionFileMaterializer();

        Assert.True(materializer.ShouldValidate);
    }

    [Fact]
    public async Task CreateZipAsync_EdiscoveryProductionSet_MatchesStreamingHashAndIndependentReader()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZipProdEdiscovery_" + Guid.NewGuid().ToString("N"));
        var prodDir = Path.Combine(root, "PRODUCTION_001");
        Directory.CreateDirectory(Path.Combine(prodDir, "DATA"));
        Directory.CreateDirectory(Path.Combine(prodDir, "NATIVES", "VOL001"));
        Directory.CreateDirectory(Path.Combine(prodDir, "NATIVES", "VOL002"));
        Directory.CreateDirectory(Path.Combine(prodDir, "IMAGES", "VOL001"));
        Directory.CreateDirectory(Path.Combine(prodDir, "IMAGES", "VOL002"));
        Directory.CreateDirectory(Path.Combine(prodDir, "TEXT", "VOL001"));
        Directory.CreateDirectory(Path.Combine(prodDir, "TEXT", "VOL002"));

        await File.WriteAllTextAsync(Path.Combine(prodDir, "_manifest.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "_validation_report.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "DATA", "loadfile.dat"), "header");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "DATA", "loadfile.opt"), "opt");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "NATIVES", "VOL001", "PROD0001.pdf"), "PDF-BYTES");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "NATIVES", "VOL002", "PROD0002.docx"), "DOCX-BYTES");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "IMAGES", "VOL001", "PROD0001.tif"), "TIF1");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "IMAGES", "VOL002", "PROD0002.tif"), "TIF2");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "TEXT", "VOL001", "PROD0001.txt"), "TXT1");
        await File.WriteAllTextAsync(Path.Combine(prodDir, "TEXT", "VOL002", "PROD0002.txt"), "TXT2");

        var zipPath = Path.Combine(root, "PRODUCTION_001.zip");

        try
        {
            await new ProductionFileMaterializer().CreateZipAsync(
                prodDir,
                zipPath,
                Config.ZipCompressionMethod.Deflate,
                CancellationToken.None);

            // Reuses ZipAssert independent reader and streaming integrity assertions
            TestHelpers.ZipAssert.AssertProductionZipMatchesDisk(zipPath, prodDir, Config.ZipCompressionMethod.Deflate);
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
