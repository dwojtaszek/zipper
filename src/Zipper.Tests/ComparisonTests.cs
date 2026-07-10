using System.Text.Json;
using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class ComparisonTests
{
    private string CreateTempProductionSet(
        string tempDir,
        string productionId,
        string batesStart,
        string batesEnd,
        string batesPrefix,
        int batesDigits,
        List<(string Bates, string DocId, string Path, string Hash, string Volume)> records)
    {
        var prodDir = Path.Combine(tempDir, productionId);
        Directory.CreateDirectory(prodDir);
        Directory.CreateDirectory(Path.Combine(prodDir, "DATA"));

        var datPath = Path.Combine(prodDir, "DATA", "loadfile.dat");
        using (var sw = new StreamWriter(datPath, false, System.Text.Encoding.UTF8))
        {
            sw.Write('\xfe'); sw.Write("BATES_NUMBER"); sw.Write("\xfe\x14\xfe");
            sw.Write("DOCID"); sw.Write("\xfe\x14\xfe");
            sw.Write("NATIVE_PATH"); sw.Write("\xfe\x14\xfe");
            sw.Write("MD5HASH"); sw.Write("\xfe\x14\xfe");
            sw.Write("VOLUME"); sw.Write('\xfe');
            sw.Write("\r\n");

            foreach (var r in records)
            {
                sw.Write('\xfe'); sw.Write(r.Bates); sw.Write("\xfe\x14\xfe");
                sw.Write(r.DocId); sw.Write("\xfe\x14\xfe");
                sw.Write(r.Path); sw.Write("\xfe\x14\xfe");
                sw.Write(r.Hash); sw.Write("\xfe\x14\xfe");
                sw.Write(r.Volume); sw.Write('\xfe');
                sw.Write("\r\n");
            }
        }

        var manifest = new
        {
            productionDate = DateTime.UtcNow.ToString("o"),
            productionId = productionId,
            batesNumberStart = batesStart,
            batesNumberEnd = batesEnd,
            batesRangeMode = "continuous",
            batesRange = new
            {
                start = batesStart,
                end = batesEnd,
                prefix = batesPrefix,
                digits = batesDigits
            },
            nativeFileCount = (long)records.Count,
            volumeCount = 1,
            volumeSize = 1000,
            directories = new
            {
                data = "DATA",
                natives = "NATIVES"
            },
            loadFiles = new
            {
                dat = "DATA/loadfile.dat"
            },
            settings = new
            {
                encoding = "UTF-8",
                columnDelimiter = "ascii:20",
                quoteDelimiter = "ascii:254"
            }
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(prodDir, "_manifest.json"), json);

        return Path.Combine(prodDir, "_manifest.json");
    }

    [Fact]
    public async Task Compare_ReplacementMode_IdentifiesChangesCorrectly()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var oldRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "hash1", "VOL001"),
                ("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "hash2", "VOL001"),
                ("PR000003", "DOC003", "NATIVES/VOL001/doc3.pdf", "hash3", "VOL001")
            };

            var newRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "hash1", "VOL001"), // Unchanged
                ("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "hash2_modified", "VOL001"), // Changed (hash changed)
                ("PR000004", "DOC003", "NATIVES/VOL001/doc3.pdf", "hash3", "VOL001"), // Replaced (same DOCID, different bates)
                ("PR000005", "DOC005", "NATIVES/VOL001/doc5.pdf", "hash5", "VOL001")  // Added
            };

            var oldManifest = CreateTempProductionSet(tempDir, "ProdOld", "PR000001", "PR000003", "PR", 6, oldRecords);
            var newManifest = CreateTempProductionSet(tempDir, "ProdNew", "PR000001", "PR000005", "PR", 6, newRecords);

            var reportPath = Path.Combine(tempDir, "report.json");

            // Act
            var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                $"{oldManifest},{newManifest}",
                "replacement",
                reportPath);

            // Assert
            Assert.True(success);
            Assert.True(File.Exists(reportPath));

            var reportJson = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;

            Assert.Equal("replacement", root.GetProperty("comparisonMode").GetString());
            var summary = root.GetProperty("summary");
            Assert.Equal(3, summary.GetProperty("totalPriorRecords").GetInt32());
            Assert.Equal(4, summary.GetProperty("totalNewRecords").GetInt32());
            Assert.Equal(1, summary.GetProperty("addedCount").GetInt32());
            Assert.Equal(0, summary.GetProperty("removedCount").GetInt32()); // DOC003 is replaced, not removed
            Assert.Equal(1, summary.GetProperty("unchangedCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("changedCount").GetInt32()); // DOC002 is changed
            Assert.Equal(1, summary.GetProperty("replacedCount").GetInt32()); // DOC003 is replaced
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Compare_SupplementalMode_IdentifiesDuplicateBatesNumbers()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var oldRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "hash1", "VOL001"),
                ("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "hash2", "VOL001")
            };

            var newRecords = new List<(string, string, string, string, string)>
            {
                ("PR000002", "DOC003", "NATIVES/VOL001/doc3.pdf", "hash3", "VOL001"), // Bates overlap
                ("PR000003", "DOC004", "NATIVES/VOL001/doc4.pdf", "hash4", "VOL001")
            };

            var oldManifest = CreateTempProductionSet(tempDir, "ProdOld", "PR000001", "PR000002", "PR", 6, oldRecords);
            var newManifest = CreateTempProductionSet(tempDir, "ProdNew", "PR000002", "PR000003", "PR", 6, newRecords);

            var reportPath = Path.Combine(tempDir, "report.json");

            // Act
            var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                $"{oldManifest},{newManifest}",
                "supplemental",
                reportPath);

            // Assert
            // It should write the report even if there are duplicates/overlaps, but success could be false or true depending on policy.
            // Let's assert it completed and we got the details.
            var reportJson = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;

            var summary = root.GetProperty("summary");
            Assert.True(summary.GetProperty("duplicateCount").GetInt32() > 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Compare_ReproductionMode_IdentifiesChangesAndBatesMatches()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var oldRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "hash1", "VOL001"),
                ("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "hash2", "VOL001")
            };

            var newRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "hash1", "VOL001"),
                ("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "hash2_modified", "VOL001") // Changed
            };

            var oldManifest = CreateTempProductionSet(tempDir, "ProdOld", "PR000001", "PR000002", "PR", 6, oldRecords);
            var newManifest = CreateTempProductionSet(tempDir, "ProdNew", "PR000001", "PR000002", "PR", 6, newRecords);

            var reportPath = Path.Combine(tempDir, "report.json");

            var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                $"{oldManifest},{newManifest}",
                "reproduction",
                reportPath);

            Assert.True(success);
            var reportJson = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;

            var summary = root.GetProperty("summary");
            Assert.Equal(1, summary.GetProperty("unchangedCount").GetInt32());
            Assert.Equal(1, summary.GetProperty("changedCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Compare_SkippedBatesNumbers_IdentifiesGaps()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var oldRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "hash1", "VOL001"),
                ("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "hash2", "VOL001")
            };

            var newRecords = new List<(string, string, string, string, string)>
            {
                ("PR000004", "DOC003", "NATIVES/VOL001/doc3.pdf", "hash3", "VOL001"), // PR000003 skipped
                ("PR000005", "DOC004", "NATIVES/VOL001/doc4.pdf", "hash4", "VOL001")
            };

            var oldManifest = CreateTempProductionSet(tempDir, "ProdOld", "PR000001", "PR000002", "PR", 6, oldRecords);
            var newManifest = CreateTempProductionSet(tempDir, "ProdNew", "PR000004", "PR000005", "PR", 6, newRecords);

            var reportPath = Path.Combine(tempDir, "report.json");

            var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                $"{oldManifest},{newManifest}",
                "supplemental",
                reportPath);

            Assert.True(success);
            var reportJson = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;

            var batesAnalysis = root.GetProperty("batesAnalysis");
            var gaps = batesAnalysis.GetProperty("gaps");
            Assert.True(gaps.GetArrayLength() > 0);

            var gapStart = gaps[0].GetProperty("start").GetString();
            var gapEnd = gaps[0].GetProperty("end").GetString();
            Assert.Equal("PR000003", gapStart);
            Assert.Equal("PR000003", gapEnd);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Compare_MissingHashBehavior_DoesNotThrow()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var oldRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "", "VOL001")
            };

            var newRecords = new List<(string, string, string, string, string)>
            {
                ("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "", "VOL001")
            };

            var oldManifest = CreateTempProductionSet(tempDir, "ProdOld", "PR000001", "PR000001", "PR", 6, oldRecords);
            var newManifest = CreateTempProductionSet(tempDir, "ProdNew", "PR000001", "PR000001", "PR", 6, newRecords);

            var reportPath = Path.Combine(tempDir, "report.json");

            var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                $"{oldManifest},{newManifest}",
                "replacement",
                reportPath);

            Assert.True(success);
            var reportJson = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;

            var summary = root.GetProperty("summary");
            Assert.Equal(1, summary.GetProperty("unchangedCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Compare_E2EGeneratedManifests_VerifiesSuccessfully()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputA = Path.Combine(tempDir, "SetA");
            Directory.CreateDirectory(outputA);
            var requestA = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputA,
                    FileCount = 3,
                    FileType = "pdf"
                },
                Production = new ProductionConfig
                {
                    ProductionSet = true,
                    VolumeSize = 100,
                    ProductionId = "PRODA"
                },
                Bates = new BatesNumberConfig
                {
                    Prefix = "PROD",
                    Start = 1,
                    Digits = 6
                }
            };
            var resultA = await ProductionSetGenerator.GenerateAsync(requestA);

            var outputB = Path.Combine(tempDir, "SetB");
            Directory.CreateDirectory(outputB);
            var requestB = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputB,
                    FileCount = 4,
                    FileType = "pdf"
                },
                Production = new ProductionConfig
                {
                    ProductionSet = true,
                    VolumeSize = 100,
                    ProductionId = "PRODB"
                },
                Bates = new BatesNumberConfig
                {
                    Prefix = "PROD",
                    Start = 1,
                    Digits = 6
                }
            };
            var resultB = await ProductionSetGenerator.GenerateAsync(requestB);

            var reportPath = Path.Combine(tempDir, "report.json");
            var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                $"{resultA.ManifestPath},{resultB.ManifestPath}",
                "replacement",
                reportPath);

            Assert.True(success);
            Assert.True(File.Exists(reportPath));

            var reportJson = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;
            Assert.Equal("replacement", root.GetProperty("comparisonMode").GetString());
            var summary = root.GetProperty("summary");
            Assert.Equal(3, summary.GetProperty("totalPriorRecords").GetInt32());
            Assert.Equal(4, summary.GetProperty("totalNewRecords").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Compare_CommandLineE2E_VerifiesSuccessfully()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputA = Path.Combine(tempDir, "SetA");
            Directory.CreateDirectory(outputA);
            var requestA = new FileGenerationRequest
            {
                Output = new OutputConfig { OutputPath = outputA, FileCount = 2, FileType = "pdf" },
                Production = new ProductionConfig { ProductionSet = true, VolumeSize = 100, ProductionId = "PRODA" },
                Bates = new BatesNumberConfig { Prefix = "PROD", Start = 1, Digits = 6 }
            };
            var resultA = await ProductionSetGenerator.GenerateAsync(requestA);

            var outputB = Path.Combine(tempDir, "SetB");
            Directory.CreateDirectory(outputB);
            var requestB = new FileGenerationRequest
            {
                Output = new OutputConfig { OutputPath = outputB, FileCount = 2, FileType = "pdf" },
                Production = new ProductionConfig { ProductionSet = true, VolumeSize = 100, ProductionId = "PRODB" },
                Bates = new BatesNumberConfig { Prefix = "PROD", Start = 1, Digits = 6 }
            };
            var resultB = await ProductionSetGenerator.GenerateAsync(requestB);

            var reportPath = Path.Combine(tempDir, "report_cli.json");

            var args = new[]
            {
                "--compare-production-manifests", $"{resultA.ManifestPath},{resultB.ManifestPath}",
                "--comparison-mode", "replacement",
                "--comparison-output", reportPath
            };

            var exitCode = await Program.Main(args);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(reportPath));

            var reportJson = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(reportJson);
            var root = doc.RootElement;
            Assert.Equal("replacement", root.GetProperty("comparisonMode").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
