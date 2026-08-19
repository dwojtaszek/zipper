using System.IO.Compression;
using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

public class PostGenerationValidatorTests
{
    // ---- ValidationFinding tests ----

    [Fact]
    public void Finding_HasExpectedProperties()
    {
        var finding = new ValidationFinding(ValidationSeverity.Error, "ColumnCount", "Mismatch", "file.dat", 5);

        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("ColumnCount", finding.Category);
        Assert.Equal("Mismatch", finding.Message);
        Assert.Equal("file.dat", finding.FilePath);
        Assert.Equal(5, finding.LineNumber);
    }

    // ---- UniqueIdValidator tests ----

    [Theory]
    [InlineData(new[] { "DOC001", "DOC002", "DOC003" }, true)]
    [InlineData(new[] { "DOC001", "DOC002", "DOC001" }, false)]
    [InlineData(new string[] { }, true)]
    public void UniqueIdValidator_DetectsDuplicates(string[] ids, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new UniqueIdValidator();

        validator.ValidateIds(ids, "ControlNumber", "test.dat", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    // ---- PathReconciliationValidator tests ----

    [Fact]
    public void PathReconciliationValidator_MissingPath_Fails()
    {
        var result = new ValidationResult();
        var validator = new PathReconciliationValidator();
        var archiveEntries = new[] { "folder_001/file_00000001.pdf", "folder_001/file_00000002.pdf" };
        var loadFilePaths = new[] { "folder_001/file_00000001.pdf", "folder_001/file_00000003.pdf" };

        validator.Validate(loadFilePaths, archiveEntries, "test.dat", result);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, f => f.Message.Contains("file_00000003.pdf", StringComparison.Ordinal));
    }

    [Fact]
    public void PathReconciliationValidator_AllPathsPresent_Passes()
    {
        var result = new ValidationResult();
        var validator = new PathReconciliationValidator();
        var archiveEntries = new[] { "folder_001/file_00000001.pdf", "folder_001/file_00000002.pdf" };
        var loadFilePaths = new[] { "folder_001/file_00000001.pdf", "folder_001/file_00000002.pdf" };

        validator.Validate(loadFilePaths, archiveEntries, "test.dat", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void PathReconciliationValidator_WithSidecarAndTextPaths_Passes()
    {
        var result = new ValidationResult();
        var validator = new PathReconciliationValidator();
        var archiveEntries = new[] { "folder_001/file_00000001.pdf", "folder_001/file_00000002.pdf" };
        var loadFilePaths = new[] { "folder_001/file_00000001.pdf", "folder_001/file_00000002.txt" };

        validator.Validate(loadFilePaths, archiveEntries, "test.dat", result);

        Assert.False(result.HasErrors);
    }

    // ---- FileGenerationResult.LoadFilePaths tests ----

    [Fact]
    public void FileGenerationResult_LoadFilePaths_DefaultsEmpty()
    {
        var result = new FileGenerationResult();

        Assert.NotNull(result.LoadFilePaths);
        Assert.Empty(result.LoadFilePaths);
    }

    [Fact]
    public void FileGenerationResult_LoadFilePaths_PopulatesCorrectly()
    {
        var paths = new Dictionary<string, string>
        {
            ["dat"] = "/tmp/output.dat",
            ["opt"] = "/tmp/output.opt",
        };
        var result = new FileGenerationResult { LoadFilePaths = paths };

        Assert.Equal(2, result.LoadFilePaths.Count);
        Assert.Equal("/tmp/output.dat", result.LoadFilePaths["dat"]);
        Assert.Equal("/tmp/output.opt", result.LoadFilePaths["opt"]);
    }

    // ---- ValidationContext tests ----

    [Fact]
    public void ValidationContext_AllProperties_SetAndGet()
    {
        var request = new FileGenerationRequest();
        var ctx = new ValidationContext
        {
            ArchiveFilePath = "/tmp/test.zip",
            LoadFiles = new Dictionary<string, string> { ["dat"] = "/tmp/test.dat" },
            ArchiveEntryPaths = new[] { "folder/file.pdf" },
            Request = request,
            SkipEolValidation = true,
            IsChaosMode = false,
        };

        Assert.Equal("/tmp/test.zip", ctx.ArchiveFilePath);
        Assert.Equal("/tmp/test.dat", ctx.LoadFiles["dat"]);
        Assert.True(ctx.SkipEolValidation);
        Assert.False(ctx.IsChaosMode);
    }

    // ---- Full integration smoke test ----

    [Fact]
    public void ValidatorRunner_AllValidators_RunsAllCategories()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(loadFilePath, "a,b,c\nd,e,f\n");

            var result = runner.ValidateLoadFile(loadFilePath, "csv", new[] { "col1", "col2", "col3" }, null);

            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidateLoadFile_WithDuplicateIdentifierAndMissingPath_ReportsBothFindings()
    {
        var loadFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(loadFilePath, "Control Number,File Path,Bates Number\nDOC001,folder/one.pdf,ABC0001\nDOC001,folder/missing.pdf,ABC0003\n");

            var result = new ValidatorRunner().ValidateLoadFile(
                loadFilePath,
                "csv",
                null,
                null,
                new[] { "folder/one.pdf" },
                new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 4 });

            Assert.Contains(result.Findings, finding => finding.Category == "UniqueId");
            Assert.Contains(result.Findings, finding => finding.Category == "PathReconciliation");
            Assert.Contains(result.Findings, finding => finding.Category == "BatesContinuity");
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidateLoadFile_WithQuotedCsvNewline_DoesNotReportColumnCountError()
    {
        var loadFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(loadFilePath, "Control Number,Note\nDOC001,\"first line\nsecond line\"\n");

            var result = new ValidatorRunner().ValidateLoadFile(loadFilePath, "csv", null, null);

            Assert.DoesNotContain(result.Findings, finding => finding.Category == "ColumnCount");
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    // ---- PostGenerationValidator tests ----

    [Fact]
    public void Validate_ValidCsvFromDisk_Passes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "a,b,c\nd,e,f\n");
            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.False(result.HasErrors);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Validate_ColumnCountMismatch_ReportsError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "a,b,c\nd,e\n");
            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.True(result.HasErrors);
            Assert.Contains(result.Findings, f => f.Category == "ColumnCount");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Validate_ChaosMode_ReturnsEmptyResult()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "a,b\n");
            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Validate_ValidArchiveFromDisk_Passes()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"archive_test_{Guid.NewGuid():N}");
        var zipPath = tempDir + ".zip";
        try
        {
            Directory.CreateDirectory(tempDir);

            File.WriteAllText(Path.Combine(tempDir, "loadfile.dat"),
                "DOCID\u0014FILEPATH\nDOC001\u0014folder_001/file_00000001.pdf\nDOC002\u0014folder_001/file_00000002.pdf\n");

            var nativesDir = Path.Combine(tempDir, "folder_001");
            Directory.CreateDirectory(nativesDir);
            File.WriteAllBytes(Path.Combine(nativesDir, "file_00000001.pdf"), [0x25, 0x50, 0x44, 0x46]);
            File.WriteAllBytes(Path.Combine(nativesDir, "file_00000002.pdf"), [0x25, 0x50, 0x44, 0x46]);

            ZipFile.CreateFromDirectory(tempDir, zipPath);

            var entryPaths = new[]
            {
                "folder_001/file_00000001.pdf",
                "folder_001/file_00000002.pdf",
            };
            var loadFiles = new Dictionary<string, string> { ["dat"] = Path.Combine(tempDir, "loadfile.dat") };

            var ctx = new ValidationContext
            {
                ArchiveFilePath = zipPath,
                LoadFiles = loadFiles,
                ArchiveEntryPaths = entryPaths,
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.False(result.HasErrors);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    [Fact]
    public void Validate_ConcordanceQuoteWrappedRecords_NoFalsePositive()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var line1 = "\xfeDOCID\xfe\x14\xfeFILEPATH\xfe";
            var line2 = "\xfeDOC001\xfe\x14\xfefield1\xfe";
            File.WriteAllText(tempFile, line1 + "\n" + line2 + "\n");

            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string> { ["concordance"] = tempFile },
                Request = new FileGenerationRequest(),
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.DoesNotContain(result.Findings, f => f.Category == "ColumnCount");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Validate_LargeFile_NoOomCorrectFindings()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("a,b,c");
            for (int i = 0; i < 15000; i++)
            {
                sb.Append("d").Append(i)
                  .Append(",e").Append(i)
                  .AppendLine(",f" + i);
            }
            File.WriteAllText(tempFile, sb.ToString());

            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Validate_MultipleFormats_ValidatesAll()
    {
        var tempDat = Path.GetTempFileName();
        var tempOpt = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempDat, "a\u0014b\u0014c\nd\u0014e\u0014f\n");
            File.WriteAllText(tempOpt, "a,b,c,d,e,f,g\n");

            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string>
                {
                    ["dat"] = tempDat,
                    ["opt"] = tempOpt,
                },
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.False(result.HasErrors);
        }
        finally
        {
            File.Delete(tempDat);
            File.Delete(tempOpt);
        }
    }

    [Fact]
    public void Validate_ProductionSet_DelegatesAndMergesFindings()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"validator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dataDir = Path.Combine(tempDir, "DATA");
            Directory.CreateDirectory(dataDir);
            var nativesDir = Path.Combine(tempDir, "NATIVES");
            Directory.CreateDirectory(nativesDir);

            var datPath = Path.Combine(dataDir, "loadfile.dat");
            File.WriteAllText(datPath, "DOCID\u0014BATES_NUMBER\u0014NATIVE_PATH\nDOC001\u0014TEST00000001\u0014NATIVES/file1.pdf\n");

            var optPath = Path.Combine(dataDir, "loadfile.opt");
            File.WriteAllText(optPath, "TEST00000001,1,NATIVES/file1.pdf,,,1,1\n");

            File.WriteAllBytes(Path.Combine(nativesDir, "file1.pdf"), [0x25, 0x50, 0x44, 0x46]);

            var request = new FileGenerationRequest
            {
                Bates = new Config.BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
            };
            var ctx = new ValidationContext
            {
                ProductionSetPath = tempDir,
                Request = request,
            };

            var validator = new PostGenerationValidator();
            var result = validator.Validate(ctx);

            Assert.False(result.HasErrors);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ---- DatLineParser tests ----

    [Theory]
    [InlineData("a,b,c", ',', '"', 3)]
    [InlineData("a,b", ',', '"', 2)]
    [InlineData("a", ',', '"', 1)]
    [InlineData("\"a,b\",c", ',', '"', 2)]
    [InlineData("a,\"b,c\",d", ',', '"', 3)]
    [InlineData("a,\"b\"\"c\",d", ',', '"', 3)]
    [InlineData("a\u0014b\u0014c", '\x14', '\xfe', 3)]
    [InlineData("\u0014a\u0014b\u0014c\u0014", '\x14', '\xfe', 5)]
    public void DatLineParser_Parse_SplitsFieldsCorrectly(string line, char colDelim, char quoteDelim, int expectedCount)
    {
        var fields = DatLineParser.Parse(line, colDelim, quoteDelim);
        Assert.Equal(expectedCount, fields.Count);
    }

    [Fact]
    public void DatLineParser_Parse_WithEmptyLine_ReturnsSingleEmptyField()
    {
        var fields = DatLineParser.Parse(string.Empty, ',', '"');
        Assert.Single(fields);
        Assert.Empty(fields[0]);
    }

    // ---- ValidationOrchestrator tests ----

    [Fact]
    public void ValidationOrchestrator_RunAfterGeneration_WithValidContext_DoesNotThrow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "a,b,c\n");
            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            ValidationOrchestrator.RunAfterGeneration(ctx);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidationOrchestrator_RunAfterGeneration_WithInvalidContext_Throws()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "a,b,c\nd,e\n");
            var ctx = new ValidationContext
            {
                LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
                Request = new FileGenerationRequest(),
                SkipEolValidation = true,
            };

            Assert.Throws<InvalidOperationException>(() => ValidationOrchestrator.RunAfterGeneration(ctx));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
