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

    // ---- ValidationResult tests ----

    [Fact]
    public void Result_WithNoFindings_HasNoErrorsOrWarnings()
    {
        var result = new ValidationResult();

        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal("Validation passed: no issues found.", result.GetSummary());
    }

    [Fact]
    public void Result_WithError_HasErrors()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Error, "Test", "error"));

        Assert.True(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void Result_WithWarning_HasWarnings()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Warning, "Test", "warning"));

        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void Result_GetSummary_GroupsByCategory()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e1"));
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e2"));
        result.Add(new ValidationFinding(ValidationSeverity.Warning, "CatB", "w1"));

        var summary = result.GetSummary();

        Assert.Contains("CatA: 2 error(s), 0 warning(s)", summary, StringComparison.Ordinal);
        Assert.Contains("CatB: 0 error(s), 1 warning(s)", summary, StringComparison.Ordinal);
    }

    // ---- ColumnCountValidator tests ----

    [Theory]
    [InlineData("a,b,c\nd,e,f\n", 3, true)]
    [InlineData("a,b,c\nd,e\n", 3, false)]
    [InlineData("a,b,c\nd,e,f,g\n", 3, false)]
    [InlineData("a,b,c\n\"d,e\",f,g\n", 3, true)]
    [InlineData("a,b,c\nd,e,f,g,h,i\n", 3, false)]
    public void ColumnCountValidator_ValidatesCsvColumns(string csvContent, int expectedColumns, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateCsv(csvContent, expectedColumns, "test.csv", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    [Theory]
    [InlineData("DOCID\u001eFILEPATH\ndoc001\u001efile.pdf\n", 2, true)]
    [InlineData("DOCID\u001eFILEPATH\ndoc001\n", 2, false)]
    public void ColumnCountValidator_ValidatesDatColumns(string datContent, int expectedColumns, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateDat(datContent, expectedColumns, '\x1e', "test.dat", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    [Theory]
    [InlineData("þDOCIDþ\x14þFILEPATHþ\nþdoc001þ\x14þfile.pdfþ\n", true)]
    [InlineData("þDOCIDþ\x14þFILEPATHþ\nþdoc001þ\n", false)]
    public void ColumnCountValidator_ValidatesConcordanceColumns(string content, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateConcordance(content, '\x14', "test.dat", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    // ---- OptBoundaryValidator tests ----

    [Theory]
    [InlineData("a,b,c,d,e,f,g\n", true)]
    [InlineData("a,b,c,d,e,f\n", false)]
    [InlineData("a,b,c,d,e,f,g,h\n", false)]
    [InlineData("a,b,c,d,e,f,g\na,b,c\n", false)]
    public void OptBoundaryValidator_ValidatesColumns(string optContent, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new OptBoundaryValidator();

        validator.Validate(optContent, "test.opt", result);

        Assert.Equal(shouldPass, !result.HasErrors);
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

    // ---- LineEndingValidator tests ----

    [Theory]
    [InlineData("line1\nline2\n", "\n", true)]
    [InlineData("line1\r\nline2\r\n", "\r\n", true)]
    [InlineData("line1\nline2\r\n", "\n", false)]
    [InlineData("line1\r\nline2\n", "\r\n", false)]
    [InlineData("line1\nline2\n", "\r\n", false)]
    public void LineEndingValidator_DetectsInconsistentEol(string content, string expectedEol, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new LineEndingValidator();

        validator.Validate(content, expectedEol, "test.dat", result);

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
                IsChaosMode = true,
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
}
