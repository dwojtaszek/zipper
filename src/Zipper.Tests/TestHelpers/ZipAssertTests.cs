using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Xunit;
using Xunit.Sdk;
using Zipper.Config;

namespace Zipper.Tests.TestHelpers;

public class ZipAssertTests : TempDirectoryTestBase
{
    [Fact]
    public void AssertValidZip_ValidArchive_Passes()
    {
        var zipPath = this.GetTempFilePath();
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("file1.txt", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry1.Open()))
            {
                writer.Write("Content 1");
            }

            var entry2 = archive.CreateEntry("file2.txt", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(entry2.Open()))
            {
                writer.Write("Content 2");
            }
        }

        ZipAssert.AssertValidZip(zipPath, 2);
    }

    [Fact]
    public void AssertValidZip_DeliberatelyWrongCrc_ThrowsException()
    {
        // Control test demonstrating that .NET CopyTo ignores bad CRC-32 metadata,
        // but ZipAssert catches it and throws XunitException.
        var validZipPath = this.GetTempFilePath();
        var corruptZipPath = this.GetTempFilePath();
        byte[] payload = Encoding.UTF8.GetBytes("Integrity check test payload");

        using (var archive = ZipFile.Open(validZipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("payload.txt", CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(payload);
        }

        byte[] zipBytes = File.ReadAllBytes(validZipPath);

        // Find Local Header signature: 0x04034b50 (PK\x03\x04)
        int localHeaderOffset = IndexOfPattern(zipBytes, [0x50, 0x4B, 0x03, 0x04]);
        Assert.True(localHeaderOffset >= 0, "Local header signature not found.");
        int localCrcOffset = localHeaderOffset + 14;

        // Find Central Directory signature: 0x02014b50 (PK\x01\x02)
        int cdHeaderOffset = IndexOfPattern(zipBytes, [0x50, 0x4B, 0x01, 0x02]);
        Assert.True(cdHeaderOffset >= 0, "Central directory header signature not found.");
        int cdCrcOffset = cdHeaderOffset + 16;

        // Corrupt CRC-32 in both headers to 0xDEADBEEF
        const uint corruptCrc = 0xDEADBEEFu;
        BinaryPrimitives.WriteUInt32LittleEndian(zipBytes.AsSpan(localCrcOffset), corruptCrc);
        BinaryPrimitives.WriteUInt32LittleEndian(zipBytes.AsSpan(cdCrcOffset), corruptCrc);

        File.WriteAllBytes(corruptZipPath, zipBytes);

        // Independent control: .NET's raw CopyTo decompresses/reads this entry without error
        using (var readArchive = ZipFile.OpenRead(corruptZipPath))
        {
            var entry = readArchive.Entries[0];
            using var stream = entry.Open();
            stream.CopyTo(Stream.Null); // Must not throw in .NET 10, proving CopyTo is insufficient alone
        }

        // But ZipAssert MUST detect the CRC corruption and fail
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            ZipAssert.AssertValidZip(corruptZipPath, 1);
        });

        Assert.True(ex is XunitException, $"Expected XunitException but got {ex.GetType().Name}: {ex.Message}");
    }

    [Fact]
    public void AssertValidZip_NonExistentFile_ThrowsException()
    {
        var nonExistent = Path.Combine(this.TempDir, "does-not-exist.zip");
        Assert.ThrowsAny<XunitException>(() =>
        {
            ZipAssert.AssertValidZip(nonExistent, 1);
        });
    }

    [Fact]
    public void AssertValidZip_CountMismatch_ThrowsException()
    {
        var zipPath = this.GetTempFilePath();
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("only_one.txt");
        }

        Assert.ThrowsAny<XunitException>(() =>
        {
            ZipAssert.AssertValidZip(zipPath, expectedCount: 2);
        });
    }

    [Fact]
    public void AssertValidZip_ExtraAsserts_IsExecuted()
    {
        var zipPath = this.GetTempFilePath();
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("custom.dat");
        }

        bool invoked = false;
        ZipAssert.AssertValidZip(zipPath, 1, archive =>
        {
            invoked = true;
            Assert.NotNull(archive.GetEntry("custom.dat"));
        });

        Assert.True(invoked);
    }

    [Fact]
    public void AssertProductionZipMatchesDisk_EmptyArchive_ThrowsException()
    {
        var prodDir = Path.Combine(this.TempDir, "PROD_EMPTY");
        Directory.CreateDirectory(prodDir);
        var zipPath = Path.Combine(this.TempDir, "PROD_EMPTY.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Empty archive
        }

        Assert.ThrowsAny<XunitException>(() =>
        {
            ZipAssert.AssertProductionZipMatchesDisk(zipPath, prodDir, ZipCompressionMethod.Store);
        });
    }

    [Fact]
    public void AssertProductionZipMatchesDisk_MissingDataFiles_ThrowsException()
    {
        var prodDir = Path.Combine(this.TempDir, "PROD_MISSING_DATA");
        Directory.CreateDirectory(prodDir);
        File.WriteAllText(Path.Combine(prodDir, "_manifest.json"), "{}");
        var zipPath = Path.Combine(this.TempDir, "PROD_MISSING_DATA.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("PROD_MISSING_DATA/_manifest.json", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{}");
        }

        // Fails because DATA/ is missing
        Assert.ThrowsAny<XunitException>(() =>
        {
            ZipAssert.AssertProductionZipMatchesDisk(zipPath, prodDir, ZipCompressionMethod.Store);
        });
    }

    [Fact]
    public void AssertProductionZipMatchesDisk_ExtraEntries_ThrowsException()
    {
        var prodDir = Path.Combine(this.TempDir, "PROD_EXTRA");
        Directory.CreateDirectory(prodDir);
        var dataDir = Path.Combine(prodDir, "DATA");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "load.dat"), "data");
        File.WriteAllText(Path.Combine(prodDir, "_manifest.json"), "{}");
        var zipPath = Path.Combine(this.TempDir, "PROD_EXTRA.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("PROD_EXTRA/DATA/load.dat", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(entry1.Open())) { writer.Write("data"); }

            var entry2 = archive.CreateEntry("PROD_EXTRA/_manifest.json", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(entry2.Open())) { writer.Write("{}"); }

            // Extra entry not on disk
            var entry3 = archive.CreateEntry("PROD_EXTRA/extra.txt", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(entry3.Open())) { writer.Write("extra"); }
        }

        Assert.ThrowsAny<XunitException>(() =>
        {
            ZipAssert.AssertProductionZipMatchesDisk(zipPath, prodDir, ZipCompressionMethod.Store);
        });
    }

    [Fact]
    public void AssertProductionZipMatchesDisk_WrongCompressionMethod_ThrowsException()
    {
        var prodDir = Path.Combine(this.TempDir, "PROD_METHOD");
        Directory.CreateDirectory(prodDir);
        var dataDir = Path.Combine(prodDir, "DATA");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "load.dat"), "data");
        File.WriteAllText(Path.Combine(prodDir, "_manifest.json"), "{}");
        var zipPath = Path.Combine(this.TempDir, "PROD_METHOD.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Created with Optimal (Deflate), but we will expect Store (0)
            var entry1 = archive.CreateEntry("PROD_METHOD/DATA/load.dat", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry1.Open())) { writer.Write("data"); }

            var entry2 = archive.CreateEntry("PROD_METHOD/_manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry2.Open())) { writer.Write("{}"); }
        }

        // Expects Store, but archive has Deflate -> must fail
        Assert.ThrowsAny<XunitException>(() =>
        {
            ZipAssert.AssertProductionZipMatchesDisk(zipPath, prodDir, ZipCompressionMethod.Store);
        });
    }

    [Fact]
    public void AssertProductionZipMatchesDisk_WrongRootLayout_ThrowsException()
    {
        var prodDir = Path.Combine(this.TempDir, "PROD_ROOT");
        Directory.CreateDirectory(prodDir);
        File.WriteAllText(Path.Combine(prodDir, "file.txt"), "hello");
        var zipPath = Path.Combine(this.TempDir, "PROD_ROOT.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Wrong root: not starting with PROD_ROOT/
            var entry = archive.CreateEntry("WRONG_ROOT/file.txt", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello");
        }

        Assert.ThrowsAny<XunitException>(() =>
        {
            ZipAssert.AssertProductionZipMatchesDisk(zipPath, prodDir, ZipCompressionMethod.Store);
        });
    }

    private static int IndexOfPattern(byte[] source, byte[] pattern)
    {
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
