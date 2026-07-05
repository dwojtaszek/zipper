using Xunit;
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
}
