using System.Text;
using System.Text.Json;
using Xunit;
using Zipper.ManifestComparison;

namespace Zipper.Tests;

public class ProductionManifestComparerTests
{
    [Fact]
    public async Task CompareAndReportAsync_WithNullMode_ShouldThrowArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ProductionManifestComparer.CompareAndReportAsync("path1,path2", null!, "output.json"));
    }

    [Fact]
    public async Task CompareAndReportAsync_WithNullOutputPath_ShouldThrowArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ProductionManifestComparer.CompareAndReportAsync("path1,path2", "replacement", null!));
    }

    [Fact]
    public async Task CompareAndReportAsync_WithEmptyManifestPaths_ShouldThrowArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProductionManifestComparer.CompareAndReportAsync(string.Empty, "replacement", "output.json"));
    }

    [Fact]
    public async Task CompareAndReportAsync_WithSingleManifestPath_ShouldThrowArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProductionManifestComparer.CompareAndReportAsync("path1", "replacement", "output.json"));
    }

    [Fact]
    public async Task CompareAndReportAsync_WithNonExistentManifestPath_ShouldThrowFileNotFoundException()
    {
        var path1 = Path.Combine(Path.GetTempPath(), $"nonexistent_manifest_{Guid.NewGuid()}.json");
        var path2 = Path.Combine(Path.GetTempPath(), $"nonexistent_manifest_{Guid.NewGuid()}.json");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            ProductionManifestComparer.CompareAndReportAsync($"{path1},{path2}", "replacement", "output.json"));
    }

    [Fact]
    public async Task CompareAndReportAsync_WithMalformedManifestJson_ShouldThrowJsonException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_malformed_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var manifest1 = Path.Combine(tempDir, "manifest1.json");
            var manifest2 = Path.Combine(tempDir, "manifest2.json");

            await File.WriteAllTextAsync(manifest1, "{ malformed json }");
            await File.WriteAllTextAsync(manifest2, "{ malformed json }");

            await Assert.ThrowsAsync<JsonException>(() =>
                ProductionManifestComparer.CompareAndReportAsync($"{manifest1},{manifest2}", "replacement", Path.Combine(tempDir, "output.json")));
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithNullManifestJson_ShouldThrowInvalidDataException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_null_json_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var manifest1 = Path.Combine(tempDir, "manifest1.json");
            var manifest2 = Path.Combine(tempDir, "manifest2.json");

            await File.WriteAllTextAsync(manifest1, "null");
            await File.WriteAllTextAsync(manifest2, "null");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProductionManifestComparer.CompareAndReportAsync($"{manifest1},{manifest2}", "replacement", Path.Combine(tempDir, "output.json")));
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithMissingDatFile_ShouldThrowFileNotFoundException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_missing_dat_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var manifest1Path = Path.Combine(tempDir, "manifest1.json");
            var manifest2Path = Path.Combine(tempDir, "manifest2.json");

            var manifestData = new
            {
                ProductionId = "PROD001",
                LoadFiles = new { Dat = "DATA/nonexistent.dat" }
            };

            var json = JsonSerializer.Serialize(manifestData);
            await File.WriteAllTextAsync(manifest1Path, json);
            await File.WriteAllTextAsync(manifest2Path, json);

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                ProductionManifestComparer.CompareAndReportAsync($"{manifest1Path},{manifest2Path}", "replacement", Path.Combine(tempDir, "output.json")));
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithPathTraversalInDatPath_ShouldThrowInvalidOperationException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_traversal_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var manifest1Path = Path.Combine(tempDir, "manifest1.json");
            var manifest2Path = Path.Combine(tempDir, "manifest2.json");

            var manifestData = new
            {
                ProductionId = "PROD001",
                LoadFiles = new { Dat = "../DATA/secret.dat" }
            };

            var json = JsonSerializer.Serialize(manifestData);
            await File.WriteAllTextAsync(manifest1Path, json);
            await File.WriteAllTextAsync(manifest2Path, json);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProductionManifestComparer.CompareAndReportAsync($"{manifest1Path},{manifest2Path}", "replacement", Path.Combine(tempDir, "output.json")));
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithMissingBatesColumnInDat_ShouldThrowInvalidDataException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_missing_bates_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var manifest1Path = CreateTestProductionSetWithCustomDatHeader(tempDir, "PROD001", "VOL001", new[] { "ABC00000001" }, "þNO_BATES_COLþ\u0014þBEGBATCHþ");
            var manifest2Path = CreateTestProductionSetWithCustomDatHeader(tempDir, "PROD002", "VOL001", new[] { "ABC00000002" }, "þNO_BATES_COLþ\u0014þBEGBATCHþ");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProductionManifestComparer.CompareAndReportAsync($"{manifest1Path},{manifest2Path}", "replacement", Path.Combine(tempDir, "output.json")));
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithDirectoryPathInput_ShouldResolveManifestJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_dir_input_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var prod1Dir = Path.Combine(tempDir, "PROD001");
            var prod2Dir = Path.Combine(tempDir, "PROD002");
            CreateTestProductionSet(tempDir, "PROD001", "VOL001", new[] { "ABC00000001" });
            CreateTestProductionSet(tempDir, "PROD002", "VOL001", new[] { "ABC00000002" });
            var outputPath = Path.Combine(tempDir, "report.json");

            var success = await ProductionManifestComparer.CompareAndReportAsync($"{prod1Dir},{prod2Dir}", "replacement", outputPath);

            Assert.True(success);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithEmptyManifests_ShouldGenerateValidReport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_empty_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = CreateTestProductionSet(tempDir, "PROD001", "VOL001", Array.Empty<string>());
            var p2 = CreateTestProductionSet(tempDir, "PROD002", "VOL001", Array.Empty<string>());
            var outputPath = Path.Combine(tempDir, "report.json");

            var success = await ProductionManifestComparer.CompareAndReportAsync($"{p1},{p2}", "replacement", outputPath);

            Assert.True(success);
            Assert.True(File.Exists(outputPath));

            var reportJson = await File.ReadAllTextAsync(outputPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;
            Assert.Equal(0, root.GetProperty("summary").GetProperty("totalPriorRecords").GetInt32());
            Assert.Equal(0, root.GetProperty("summary").GetProperty("totalNewRecords").GetInt32());
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithOverlappingBatesRanges_ShouldDetectOverlaps()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_overlaps_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = CreateTestProductionSet(tempDir, "PROD001", "VOL001", new[] { "ABC00000001", "ABC00000002" });
            var p2 = CreateTestProductionSet(tempDir, "PROD002", "VOL001", new[] { "ABC00000002", "ABC00000003" });
            var outputPath = Path.Combine(tempDir, "report.json");

            var success = await ProductionManifestComparer.CompareAndReportAsync($"{p1},{p2}", "supplemental", outputPath);

            Assert.True(success);
            var reportJson = await File.ReadAllTextAsync(outputPath);
            using var doc = JsonDocument.Parse(reportJson);
            var overlaps = doc.RootElement.GetProperty("batesAnalysis").GetProperty("overlaps");
            Assert.True(overlaps.GetArrayLength() > 0);
            Assert.Equal("ABC00000002", overlaps[0].GetProperty("start").GetString());
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithBatesGaps_ShouldDetectGaps()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_gaps_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = CreateTestProductionSet(tempDir, "PROD001", "VOL001", new[] { "ABC00000001", "ABC00000002" });
            var p2 = CreateTestProductionSet(tempDir, "PROD002", "VOL001", new[] { "ABC00000005", "ABC00000006" });
            var outputPath = Path.Combine(tempDir, "report.json");

            var success = await ProductionManifestComparer.CompareAndReportAsync($"{p1},{p2}", "supplemental", outputPath);

            Assert.True(success);
            var reportJson = await File.ReadAllTextAsync(outputPath);
            using var doc = JsonDocument.Parse(reportJson);
            var gaps = doc.RootElement.GetProperty("batesAnalysis").GetProperty("gaps");
            Assert.True(gaps.GetArrayLength() > 0);
            Assert.Equal("ABC00000003", gaps[0].GetProperty("start").GetString());
            Assert.Equal("ABC00000004", gaps[0].GetProperty("end").GetString());
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithMarkdownOutput_ShouldWriteMarkdownReport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_md_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = CreateTestProductionSet(tempDir, "PROD001", "VOL001", new[] { "ABC00000001" });
            var p2 = CreateTestProductionSet(tempDir, "PROD002", "VOL001", new[] { "ABC00000002" });
            var outputPath = Path.Combine(tempDir, "report.json");

            var success = await ProductionManifestComparer.CompareAndReportAsync($"{p1},{p2}", "replacement", outputPath);

            Assert.True(success);
            Assert.True(File.Exists(outputPath));

            var summaryPath = Path.ChangeExtension(outputPath, ".summary.md");
            Assert.True(File.Exists(summaryPath));

            var reportMd = await File.ReadAllTextAsync(summaryPath);
            Assert.Contains("# Production Set Comparison Report", reportMd, StringComparison.Ordinal);
            Assert.Contains("Total Prior Records", reportMd, StringComparison.Ordinal);
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CompareAndReportAsync_WithDuplicateRecordsInSets_ShouldIdentifyDuplicates()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_dups_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = CreateTestProductionSet(tempDir, "PROD001", "VOL001", new[] { "ABC00000001", "ABC00000001" });
            var p2 = CreateTestProductionSet(tempDir, "PROD002", "VOL001", new[] { "ABC00000002" });
            var outputPath = Path.Combine(tempDir, "report.json");

            var success = await ProductionManifestComparer.CompareAndReportAsync($"{p1},{p2}", "replacement", outputPath);

            Assert.True(success);
            var reportJson = await File.ReadAllTextAsync(outputPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("summary").GetProperty("duplicateCount").GetInt32() > 0);
            Assert.True(root.GetProperty("details").GetProperty("duplicates").GetArrayLength() > 0);
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    private static string CreateTestProductionSet(string baseDir, string prodId, string volume, string[] batesNumbers)
    {
        return CreateTestProductionSetWithCustomDatHeader(baseDir, prodId, volume, batesNumbers, "þBATES_NUMBERþ\u0014þBEGBATCHþ\u0014þFILE_PATHþ\u0014þMD5HASHþ");
    }

    private static string CreateTestProductionSetWithCustomDatHeader(string baseDir, string prodId, string volume, string[] batesNumbers, string datHeader)
    {
        var prodDir = Path.Combine(baseDir, prodId);
        var dataDir = Path.Combine(prodDir, "DATA");
        Directory.CreateDirectory(dataDir);

        var datPath = Path.Combine(dataDir, "loadfile.dat");
        var sb = new StringBuilder();
        sb.AppendLine(datHeader);
        foreach (var bates in batesNumbers)
        {
            sb.AppendLine($"þ{bates}þ\u0014þ{volume}þ\u0014þIMAGES/001.tifþ\u0014þd41d8cd98f00b204e9800998ecf8427eþ");
        }
        File.WriteAllText(datPath, sb.ToString(), Encoding.UTF8);

        var manifestData = new
        {
            ProductionId = prodId,
            BatesNumberStart = batesNumbers.Length > 0 ? batesNumbers[0] : string.Empty,
            BatesNumberEnd = batesNumbers.Length > 0 ? batesNumbers[^1] : string.Empty,
            BatesRange = new { Start = batesNumbers.Length > 0 ? batesNumbers[0] : string.Empty, End = batesNumbers.Length > 0 ? batesNumbers[^1] : string.Empty, Prefix = "ABC", Digits = 8 },
            LoadFiles = new { Dat = "DATA/loadfile.dat", Opt = "DATA/loadfile.opt" },
            Settings = new { Encoding = "UTF-8", ColumnDelimiter = "\u0014", QuoteDelimiter = "þ" }
        };

        var manifestPath = Path.Combine(prodDir, "_manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestData));

        return manifestPath;
    }

    private static void CleanupDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // Best effort
            }
        }
    }
}
