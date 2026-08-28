using System.IO.Compression;
using Xunit;

namespace Zipper.Tests;

public class GoldenBaselineHarnessTests
{
    [Fact]
    public void Harness_GeneratesDeterministicManifest_ForMixedArtifactTree()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

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
        var tempDir1 = Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));
        var tempDir2 = Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "GoldenHarness_" + Guid.NewGuid().ToString("N")));

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
}
