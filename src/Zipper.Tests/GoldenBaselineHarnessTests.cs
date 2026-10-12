using System.IO.Compression;
using Xunit;

namespace Zipper.Tests;

public class GoldenBaselineHarnessTests
{
    [Fact]
    public void Harness_GeneratesDeterministicManifest_ForMixedArtifactTree()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "loadfile.dat"), "BATES001\tDOC001\tC:\\path\\file.pdf\r\n");
            File.WriteAllText(Path.Combine(tempDir.FullName, "loadfile_properties.json"), "{\"productionDate\":\"2025-01-01T00:00:00Z\"}");
            File.WriteAllText(Path.Combine(tempDir.FullName, "manifest.json"), "{\"generationTime\":\"2025-01-01T00:00:00Z\"}");

            using (var zip = ZipFile.Open(Path.Combine(tempDir.FullName, "archive.zip"), ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("DOC001.pdf");
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write("PDF content");
            }

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir.FullName);

            Assert.Equal(manifest1, manifest2);
            Assert.DoesNotContain(tempDir.FullName, manifest1, StringComparison.Ordinal);
            Assert.Contains("loadfile.dat", manifest1, StringComparison.Ordinal);
            Assert.Contains("manifest.json", manifest1, StringComparison.Ordinal);
            Assert.Contains("archive.zip::DOC001.pdf", manifest1, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir.FullName))
            {
                Directory.Delete(tempDir.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_ProducesDifferentManifests_WhenFileContentChanges()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(Path.Combine(tempDir1.FullName, "loadfile.dat"), "A\tB\tC\r\n");
            File.WriteAllText(Path.Combine(tempDir2.FullName, "loadfile.dat"), "A\tB\tC\r\nX");

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.NotEqual(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_NormalizesTimestampAndWallClockFields_InJsonArtifacts()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDir1.FullName, "_manifest.json"),
                "{\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"generationTime\": \"0.1s\",\n  \"productionId\": \"GOLDPROD\"\n}");
            File.WriteAllText(
                Path.Combine(tempDir2.FullName, "_manifest.json"),
                "{\n  \"productionDate\": \"2026-10-12T23:59:59Z\",\n  \"generationTime\": \"4.8s\",\n  \"productionId\": \"GOLDPROD\"\n}");

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.Equal(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void GenerateRawSha256Manifest_DoesNotNormalizeTimestamps_ProducesDifferentManifests()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDir1.FullName, "_manifest.json"),
                "{\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"generationTime\": \"0.1s\",\n  \"productionId\": \"GOLDPROD\"\n}");
            File.WriteAllText(
                Path.Combine(tempDir2.FullName, "_manifest.json"),
                "{\n  \"productionDate\": \"2026-10-12T23:59:59Z\",\n  \"generationTime\": \"4.8s\",\n  \"productionId\": \"GOLDPROD\"\n}");

            string rawManifest1 = GoldenBaselineHarness.GenerateRawSha256Manifest(tempDir1.FullName);
            string rawManifest2 = GoldenBaselineHarness.GenerateRawSha256Manifest(tempDir2.FullName);

            Assert.NotEqual(rawManifest1, rawManifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_ManifestString_UsesPlatformIndependentLineEndings()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "file1.txt"), "hello");
            File.WriteAllText(Path.Combine(tempDir.FullName, "file2.txt"), "world");

            string manifest = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir.FullName);

            Assert.Contains("\n", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", manifest, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir.FullName))
            {
                Directory.Delete(tempDir.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_DetectsDifferencesInStableJsonFields()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDir1.FullName, "_manifest.json"),
                "{\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"generationTime\": \"0.1s\",\n  \"productionId\": \"GOLDPROD_A\"\n}");
            File.WriteAllText(
                Path.Combine(tempDir2.FullName, "_manifest.json"),
                "{\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"generationTime\": \"0.1s\",\n  \"productionId\": \"GOLDPROD_B\"\n}");

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.NotEqual(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_PreservesFormattingAndDetectsPropertyReordering()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDir1.FullName, "_manifest.json"),
                "{\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"productionId\": \"GOLDPROD\",\n  \"generationTime\": \"0.1s\"\n}");
            File.WriteAllText(
                Path.Combine(tempDir2.FullName, "_manifest.json"),
                "{\n  \"productionId\": \"GOLDPROD\",\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"generationTime\": \"0.1s\"\n}");

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.NotEqual(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_NormalizesStructuredTransientJsonValues()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDir1.FullName, "_manifest.json"),
                "{\n  \"timestamp\": { \"utc\": \"2026-10-11T20:00:00Z\", \"ticks\": 12345 },\n  \"id\": \"TEST\"\n}");
            File.WriteAllText(
                Path.Combine(tempDir2.FullName, "_manifest.json"),
                "{\n  \"timestamp\": { \"utc\": \"2026-10-12T23:59:59Z\", \"ticks\": 67890 },\n  \"id\": \"TEST\"\n}");

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.Equal(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_NormalizesTransientJsonValues_WhenFileContainsUtf8Bom()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            var bom = System.Text.Encoding.UTF8.GetPreamble();
            var json1Bytes = bom.Concat(System.Text.Encoding.UTF8.GetBytes("{\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"id\": \"TEST\"\n}")).ToArray();
            var json2Bytes = bom.Concat(System.Text.Encoding.UTF8.GetBytes("{\n  \"productionDate\": \"2026-10-12T23:59:59Z\",\n  \"id\": \"TEST\"\n}")).ToArray();

            File.WriteAllBytes(Path.Combine(tempDir1.FullName, "_manifest.json"), json1Bytes);
            File.WriteAllBytes(Path.Combine(tempDir2.FullName, "_manifest.json"), json2Bytes);

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.Equal(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void GenerateTimestampNormalizedSha256Manifest_UnsupportedCompressionEntry_FallsBackToArchiveHash()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            var zipPath = Path.Combine(tempDir.FullName, "unsupported.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("doc.txt");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("PDF content");
            }

            // Patch both headers to LZMA (14): ZipArchiveEntry.Open() rejects it
            // (net10 surfaces InvalidDataException; NotSupportedException is also
            // caught), so the harness must fall back to hashing the whole file.
            var zipBytes = File.ReadAllBytes(zipPath);
            SetCompressionMethod(zipBytes, 14);
            File.WriteAllBytes(zipPath, zipBytes);

            string manifest = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir.FullName);

            var manifestLines = manifest.Split('\n');
            Assert.Contains(manifestLines, line => line.EndsWith("  unsupported.zip", StringComparison.Ordinal));
            Assert.DoesNotContain(manifestLines, line => line.Contains("unsupported.zip::", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempDir.FullName))
            {
                Directory.Delete(tempDir.FullName, true);
            }
        }
    }

    [Fact]
    public void GenerateTimestampNormalizedSha256Manifest_TransientFieldsInUnlistedJsonArtifact_KeepsOriginalBytes()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDir1.FullName, "metadata.json"),
                "{\n  \"timestamp\": \"2026-10-11T20:00:00Z\",\n  \"id\": \"TEST\"\n}");
            File.WriteAllText(
                Path.Combine(tempDir2.FullName, "metadata.json"),
                "{\n  \"timestamp\": \"2026-10-12T23:59:59Z\",\n  \"id\": \"TEST\"\n}");

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.NotEqual(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    private static void SetCompressionMethod(byte[] zipBytes, ushort method)
    {
        for (int i = 0; i <= zipBytes.Length - 4; i++)
        {
            if (zipBytes[i] == 0x50 && zipBytes[i + 1] == 0x4B && zipBytes[i + 2] == 0x03 && zipBytes[i + 3] == 0x04)
            {
                zipBytes[i + 8] = (byte)(method & 0xFF);
                zipBytes[i + 9] = (byte)(method >> 8);
            }
            else if (zipBytes[i] == 0x50 && zipBytes[i + 1] == 0x4B && zipBytes[i + 2] == 0x01 && zipBytes[i + 3] == 0x02)
            {
                zipBytes[i + 10] = (byte)(method & 0xFF);
                zipBytes[i + 11] = (byte)(method >> 8);
            }
        }
    }

    [Fact]
    public void GenerateRawSha256Manifest_HashesArchiveDirectly_WithoutExtractingEntries()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            using (var zip = ZipFile.Open(Path.Combine(tempDir.FullName, "test.zip"), ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("doc.txt");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("entry content");
            }

            string rawManifest = GoldenBaselineHarness.GenerateRawSha256Manifest(tempDir.FullName);
            string normManifest = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir.FullName);

            Assert.Contains("test.zip", rawManifest, StringComparison.Ordinal);
            Assert.DoesNotContain("test.zip::doc.txt", rawManifest, StringComparison.Ordinal);

            Assert.Contains("test.zip::doc.txt", normManifest, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir.FullName))
            {
                Directory.Delete(tempDir.FullName, true);
            }
        }
    }

    [Fact]
    public void Harness_IncludesArchiveHash_WhenZipArchiveHasNoFileEntries()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            using (var zip = ZipFile.Open(Path.Combine(tempDir.FullName, "empty.zip"), ZipArchiveMode.Create))
            {
                zip.CreateEntry("emptyfolder/");
            }

            string manifest = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir.FullName);

            Assert.Contains("empty.zip", manifest, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir.FullName))
            {
                Directory.Delete(tempDir.FullName, true);
            }
        }
    }

    [Fact]
    public void GenerateTimestampNormalizedSha256Manifest_DirectoryOnlyArchivesWithDifferentEntryTimes_Match()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            CreateDirectoryOnlyZip(tempDir1.FullName, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            CreateDirectoryOnlyZip(tempDir2.FullName, new DateTimeOffset(2030, 6, 2, 12, 30, 0, TimeSpan.Zero));

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.Contains("empty.zip", manifest1, StringComparison.Ordinal);
            Assert.Equal(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    [Fact]
    public void GenerateRawSha256Manifest_DirectoryOnlyArchivesWithDifferentEntryTimes_Differ()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            CreateDirectoryOnlyZip(tempDir1.FullName, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            CreateDirectoryOnlyZip(tempDir2.FullName, new DateTimeOffset(2030, 6, 2, 12, 30, 0, TimeSpan.Zero));

            string manifest1 = GoldenBaselineHarness.GenerateRawSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateRawSha256Manifest(tempDir2.FullName);

            Assert.NotEqual(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }

    private static void CreateDirectoryOnlyZip(string dir, DateTimeOffset lastWriteTime)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "empty.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("emptyfolder/");
        entry.LastWriteTime = lastWriteTime;
    }

    [Fact]
    public void Harness_NormalizesTransientJsonValues_WhenZipEntryHasBackslashSeparators()
    {
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

        try
        {
            var zip1Path = Path.Combine(tempDir1.FullName, "archive.zip");
            var zip2Path = Path.Combine(tempDir2.FullName, "archive.zip");

            using (var zip1 = ZipFile.Open(zip1Path, ZipArchiveMode.Create))
            {
                var entry = zip1.CreateEntry("subfolder\\_manifest.json");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("{\n  \"productionDate\": \"2026-10-11T20:00:00Z\",\n  \"id\": \"TEST\"\n}");
            }

            using (var zip2 = ZipFile.Open(zip2Path, ZipArchiveMode.Create))
            {
                var entry = zip2.CreateEntry("subfolder\\_manifest.json");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("{\n  \"productionDate\": \"2026-10-12T23:59:59Z\",\n  \"id\": \"TEST\"\n}");
            }

            string manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir1.FullName);
            string manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(tempDir2.FullName);

            Assert.Equal(manifest1, manifest2);
        }
        finally
        {
            if (Directory.Exists(tempDir1.FullName))
            {
                Directory.Delete(tempDir1.FullName, true);
            }

            if (Directory.Exists(tempDir2.FullName))
            {
                Directory.Delete(tempDir2.FullName, true);
            }
        }
    }
}
