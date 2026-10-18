using Xunit;

namespace Zipper.Tests;

public class SmokeTests : WorkingDirectoryTestBase
{
    [Fact]
    public async Task Main_WithParallelGeneration_ShouldCreateValidArchive()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", tempDir, "--folders", "5" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
            Assert.NotNull(zipFile);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
            Assert.Equal(100, archive.Entries.Count);

            var datFiles = Directory.GetFiles(tempDir, "*.dat");
            Assert.Single(datFiles);
            var datContent = await File.ReadAllTextAsync(datFiles[0]);
            var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(101, datLines.Length); // 100 records + 1 header
            Assert.Contains("DOC00000001", datContent, StringComparison.Ordinal);
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
    public async Task Main_WithEmlAndAttachments_ShouldCreateArchive()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--type", "eml", "--count", "10", "--output-path", tempDir, "--attachment-rate", "50", "--seed", "42" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
            Assert.NotNull(zipFile);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
            Assert.True(archive.Entries.Count >= 10);

            var datFiles = Directory.GetFiles(tempDir, "*.dat");
            Assert.Single(datFiles);
            var datContent = await File.ReadAllTextAsync(datFiles[0]);
            Assert.Contains("Attachment", datContent, StringComparison.Ordinal);
            Assert.Contains("Subject", datContent, StringComparison.Ordinal);
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
    public async Task Main_WithTiffPageRange_ShouldCreateArchive()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--type", "tiff", "--count", "10", "--output-path", tempDir, "--tiff-pages", "1-5", "--seed", "42" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
            Assert.NotNull(zipFile);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
            Assert.Equal(10, archive.Entries.Count);

            var datFiles = Directory.GetFiles(tempDir, "*.dat");
            Assert.Single(datFiles);
            var datContent = await File.ReadAllTextAsync(datFiles[0]);
            Assert.Contains("Page Count", datContent, StringComparison.Ordinal);
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
    public async Task Main_WithLoadfileOnly_ShouldCreateLoadFileOnly()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--loadfile-only", "--count", "10", "--output-path", tempDir, "--loadfile-format", "dat" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            Assert.Empty(Directory.GetFiles(tempDir, "*.zip"));
            var datFiles = Directory.GetFiles(tempDir, "*.dat");
            Assert.Single(datFiles);

            var datContent = await File.ReadAllTextAsync(datFiles[0]);
            var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(11, datLines.Length); // 10 records + 1 header
            Assert.Contains("DOC00000001", datContent, StringComparison.Ordinal);

            // Verify load file column structure
            var header = datLines[0];
            Assert.Contains("\x14", header, StringComparison.Ordinal);
            Assert.Contains("\xFE", header, StringComparison.Ordinal);

            var propFiles = Directory.GetFiles(tempDir, "*_properties.json");
            Assert.Single(propFiles);
            var propContent = await File.ReadAllTextAsync(propFiles[0]);
            Assert.Contains("DAT (Metadata)", propContent, StringComparison.Ordinal);
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
    public async Task Main_WithChaosMode_ShouldCreateLoadFile()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--loadfile-only", "--count", "10", "--output-path", tempDir, "--loadfile-format", "dat", "--chaos-mode", "--chaos-amount", "10%" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);
            var datFiles = Directory.GetFiles(tempDir, "*.dat");
            Assert.Single(datFiles);

            var datContent = await File.ReadAllTextAsync(datFiles[0]);
            Assert.NotEmpty(datContent);
            var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(datLines.Length > 0);

            var propFiles = Directory.GetFiles(tempDir, "*_properties.json");
            Assert.Single(propFiles);
            var propContent = await File.ReadAllTextAsync(propFiles[0]);
            Assert.Contains("\"enabled\": true", propContent, StringComparison.Ordinal);
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
    public async Task Main_WithTargetZipSize_ShouldCreateArchiveNearTarget()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--type", "pdf", "--count", "200", "--output-path", tempDir, "--target-zip-size", "2MB" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
            Assert.NotNull(zipFile);

            var fileInfo = new FileInfo(zipFile!);
            Assert.True(fileInfo.Length > 1024 * 1024);
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
    public async Task Main_ConcurrentGeneration_ShouldHandleParallelRuns()
    {
        var tempDir1 = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);

        try
        {
            var args1 = new[] { "--type", "pdf", "--count", "20", "--output-path", tempDir1, "--seed", "42" };
            var args2 = new[] { "--type", "pdf", "--count", "30", "--output-path", tempDir2, "--seed", "99" };

            var task1 = Program.Main(args1);
            var task2 = Program.Main(args2);

            var results = await Task.WhenAll(task1, task2);

            Assert.Equal(0, results[0]);
            Assert.Equal(0, results[1]);

            var zip1 = Directory.GetFiles(tempDir1, "*.zip").FirstOrDefault();
            var zip2 = Directory.GetFiles(tempDir2, "*.zip").FirstOrDefault();
            Assert.NotNull(zip1);
            Assert.NotNull(zip2);

            using var archive1 = System.IO.Compression.ZipFile.OpenRead(zip1!);
            using var archive2 = System.IO.Compression.ZipFile.OpenRead(zip2!);
            Assert.Equal(20, archive1.Entries.Count);
            Assert.Equal(30, archive2.Entries.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir1))
            {
                Directory.Delete(tempDir1, true);
            }

            if (Directory.Exists(tempDir2))
            {
                Directory.Delete(tempDir2, true);
            }
        }
    }

    [Fact]
    public async Task Main_WithAutoOptGeneration_ForTiffAndJpg_CreatesBothDatAndOpt()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Run for --type tiff
            var argsTiff = new[] { "--type", "tiff", "--count", "5", "--output-path", tempDir };
            var resultTiff = await Program.Main(argsTiff);
            Assert.Equal(0, resultTiff);

            // Should write load files to output directory (not in zip)
            var datFiles = Directory.GetFiles(tempDir, "*.dat");
            var optFiles = Directory.GetFiles(tempDir, "*.opt");

            Assert.Single(datFiles);
            Assert.Single(optFiles);

            var optContent = await File.ReadAllTextAsync(optFiles[0]);
            Assert.Contains("DOC00000001", optContent, StringComparison.Ordinal);
            Assert.Contains("VOL001", optContent, StringComparison.Ordinal);

            // Clean up files in tempDir
            foreach (var file in Directory.GetFiles(tempDir))
            {
                File.Delete(file);
            }

            // 2. Run for --type jpg
            var argsJpg = new[] { "--type", "jpg", "--count", "5", "--output-path", tempDir };
            var resultJpg = await Program.Main(argsJpg);
            Assert.Equal(0, resultJpg);

            datFiles = Directory.GetFiles(tempDir, "*.dat");
            optFiles = Directory.GetFiles(tempDir, "*.opt");

            Assert.Single(datFiles);
            Assert.Single(optFiles);

            var optContentJpg = await File.ReadAllTextAsync(optFiles[0]);
            Assert.Contains("DOC00000001", optContentJpg, StringComparison.Ordinal);
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
    public async Task Main_WithEmlFamiliesAndAttachments_ShouldCreateFamilyColumnsAndRows()
    {
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--type", "eml", "--count", "5", "--output-path", tempDir, "--attachment-rate", "100", "--with-families", "--seed", "42" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            var datFile = Directory.GetFiles(tempDir, "*.dat").FirstOrDefault();
            Assert.NotNull(datFile);

            var content = await File.ReadAllTextAsync(datFile!);

            // Assert family columns are in header
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length > 0);
            var header = lines[0];
            Assert.Contains("BEGATTACH", header, StringComparison.Ordinal);
            Assert.Contains("ENDATTACH", header, StringComparison.Ordinal);
            Assert.Contains("PARENTDOCID", header, StringComparison.Ordinal);

            // Assert there are more rows than emails since there are child attachment rows
            Assert.True(lines.Length > 6);
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
    public async Task Main_WithCollectionMetadata_EmitsCollectionMetadataColumnsAndValues()
    {
        // REQ-183: --with-collection-metadata adds five collection metadata columns in Standard mode and is deterministic per --seed.
        var tempDir1 = Path.Combine(this.TempDir, $"zipper_s42a_{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(this.TempDir, $"zipper_s42b_{Guid.NewGuid():N}");
        var tempDir3 = Path.Combine(this.TempDir, $"zipper_s99_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);
        Directory.CreateDirectory(tempDir3);

        try
        {
            var result1 = await Program.Main(new[]
            {
                "--type", "pdf",
                "--count", "5",
                "--output-path", tempDir1,
                "--with-collection-metadata",
                "--seed", "42",
            });
            var result2 = await Program.Main(new[]
            {
                "--type", "pdf",
                "--count", "5",
                "--output-path", tempDir2,
                "--with-collection-metadata",
                "--seed", "42",
            });
            var result3 = await Program.Main(new[]
            {
                "--type", "pdf",
                "--count", "5",
                "--output-path", tempDir3,
                "--with-collection-metadata",
                "--seed", "99",
            });

            Assert.Equal(0, result1);
            Assert.Equal(0, result2);
            Assert.Equal(0, result3);

            var datFile1 = Assert.Single(Directory.GetFiles(tempDir1, "*.dat"));
            var datFile2 = Assert.Single(Directory.GetFiles(tempDir2, "*.dat"));
            var datFile3 = Assert.Single(Directory.GetFiles(tempDir3, "*.dat"));

            var content1 = await File.ReadAllTextAsync(datFile1);
            var content2 = await File.ReadAllTextAsync(datFile2);
            var content3 = await File.ReadAllTextAsync(datFile3);

            Assert.Equal(content1, content2);
            Assert.NotEqual(content1, content3);

            var lines = content1.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(6, lines.Length);

            var header = lines[0];
            Assert.Contains("Data Source", header, StringComparison.Ordinal);
            Assert.Contains("Collection Date", header, StringComparison.Ordinal);
            Assert.Contains("De-Nisted", header, StringComparison.Ordinal);
            Assert.Contains("Dedupe Group ID", header, StringComparison.Ordinal);
            Assert.Contains("Processing Status", header, StringComparison.Ordinal);

            Assert.Contains("GRP", lines[1], StringComparison.Ordinal);
            Assert.Contains("2024-", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir1))
            {
                Directory.Delete(tempDir1, true);
            }

            if (Directory.Exists(tempDir2))
            {
                Directory.Delete(tempDir2, true);
            }

            if (Directory.Exists(tempDir3))
            {
                Directory.Delete(tempDir3, true);
            }
        }
    }

    [Fact]
    public async Task Main_TypesSinglePdf100_MatchesSingleTypePdfOutput()
    {
        // REQ-192: --types pdf:100 produces equivalent archive entries and Load File rows to --type pdf with the same seed.
        var singleDir = Path.Combine(this.TempDir, $"zipper_single_{Guid.NewGuid():N}");
        var mixedDir = Path.Combine(this.TempDir, $"zipper_mixed_{Guid.NewGuid():N}");
        Directory.CreateDirectory(singleDir);
        Directory.CreateDirectory(mixedDir);

        try
        {
            var singleResult = await Program.Main(new[]
            {
                "--type", "pdf",
                "--count", "5",
                "--output-path", singleDir,
                "--with-metadata",
                "--seed", "42",
            });
            var mixedResult = await Program.Main(new[]
            {
                "--types", "pdf:100",
                "--count", "5",
                "--output-path", mixedDir,
                "--with-metadata",
                "--seed", "42",
            });

            Assert.Equal(0, singleResult);
            Assert.Equal(0, mixedResult);

            var singleDat = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(singleDir, "*.dat")));
            var mixedDat = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(mixedDir, "*.dat")));
            Assert.Equal(singleDat, mixedDat);

            using var singleZip = System.IO.Compression.ZipFile.OpenRead(Assert.Single(Directory.GetFiles(singleDir, "*.zip")));
            using var mixedZip = System.IO.Compression.ZipFile.OpenRead(Assert.Single(Directory.GetFiles(mixedDir, "*.zip")));

            var singleEntries = singleZip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();
            var mixedEntries = mixedZip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();
            Assert.Equal(singleEntries.Count, mixedEntries.Count);

            for (int i = 0; i < singleEntries.Count; i++)
            {
                Assert.Equal(singleEntries[i].FullName, mixedEntries[i].FullName);
                Assert.Equal(singleEntries[i].Length, mixedEntries[i].Length);

                using var singleStream = singleEntries[i].Open();
                using var mixedStream = mixedEntries[i].Open();
                using var singleBuffer = new MemoryStream();
                using var mixedBuffer = new MemoryStream();
                await singleStream.CopyToAsync(singleBuffer);
                await mixedStream.CopyToAsync(mixedBuffer);
                Assert.Equal(singleBuffer.ToArray(), mixedBuffer.ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(singleDir))
            {
                Directory.Delete(singleDir, true);
            }

            if (Directory.Exists(mixedDir))
            {
                Directory.Delete(mixedDir, true);
            }
        }
    }

    [Fact]
    public async Task Main_WithMultipleLoadFileFormats_GeneratesAllRequestedFormatsAndAuditSidecars()
    {
        // REQ-038: --load-file-formats emits every requested Load File format conforming to format specifications and its audit sidecar.
        var tempDir = Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}");
        var concordanceDir = Path.Combine(this.TempDir, $"zipper_conc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(concordanceDir);

        try
        {
            var args = new[]
            {
                "--type", "pdf",
                "--count", "4",
                "--output-path", tempDir,
                "--load-file-formats", "dat,opt,csv,edrm-xml",
                "--seed", "42",
            };

            var result = await Program.Main(args);
            Assert.Equal(0, result);

            var datFile = Assert.Single(Directory.GetFiles(tempDir, "*.dat"));
            var optFile = Assert.Single(Directory.GetFiles(tempDir, "*.opt"));
            var csvFile = Assert.Single(Directory.GetFiles(tempDir, "*.csv"));
            var xmlFile = Assert.Single(Directory.GetFiles(tempDir, "*.xml"));

            var datContent = await File.ReadAllTextAsync(datFile);
            Assert.Contains('\u0014', datContent);
            Assert.Contains('\u00FE', datContent);
            Assert.Contains("Control Number", datContent, StringComparison.Ordinal);

            var optLines = (await File.ReadAllLinesAsync(optFile)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            Assert.Equal(4, optLines.Length);
            Assert.All(optLines, line => Assert.Equal(7, line.Split(',').Length));

            var csvLines = (await File.ReadAllLinesAsync(csvFile)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            Assert.Equal(5, csvLines.Length);
            Assert.Contains("CONTROL NUMBER", csvLines[0], StringComparison.OrdinalIgnoreCase);

            var xmlDoc = System.Xml.Linq.XDocument.Load(xmlFile);
            Assert.Equal("Root", xmlDoc.Root?.Name.LocalName);
            Assert.Equal(4, xmlDoc.Root?.Descendants().Count(e => e.Name.LocalName == "Document"));

            var auditFiles = Directory.GetFiles(tempDir, "*_properties.json");
            Assert.Equal(4, auditFiles.Length);

            var concordanceResult = await Program.Main(new[]
            {
                "--type", "pdf",
                "--count", "4",
                "--output-path", concordanceDir,
                "--load-file-format", "concordance",
                "--seed", "42",
            });
            Assert.Equal(0, concordanceResult);
            var concordanceFile = Assert.Single(Directory.GetFiles(concordanceDir, "*.dat"));
            var concordanceLines = (await File.ReadAllLinesAsync(concordanceFile)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            Assert.Equal(5, concordanceLines.Length);
            Assert.StartsWith("\u00FEBEGATTY\u00FE\u0014\u00FEENDATTY\u00FE", concordanceLines[0], StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(concordanceDir, "*_properties.json"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            if (Directory.Exists(concordanceDir))
            {
                Directory.Delete(concordanceDir, true);
            }
        }
    }
}
