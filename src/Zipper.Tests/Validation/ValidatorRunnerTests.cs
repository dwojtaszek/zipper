using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

public class ValidatorRunnerTests : TempDirectoryTestBase
{
    // ---- Independent Malformed Fixtures & Clean Controls (All Categories) ----

    [Fact]
    public void ValidatorRunner_RunsCategory_ColumnCount_ReportsExactFinding()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "col1,col2,col3\nval1,val2\n");

            var result = runner.ValidateLoadFile(loadFilePath, "csv", new[] { "col1", "col2", "col3" }, null);

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("ColumnCount", finding.Category);
            Assert.Equal(loadFilePath, finding.FilePath);
            Assert.Equal(2, finding.LineNumber);
            Assert.Equal("Expected 3 columns, got 2 on line 2", finding.Message);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidatorRunner_RunsCategory_MalformedCsv_ReportsExactFinding()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "col1,col2\n\"unclosed,val\n");

            var result = runner.ValidateLoadFile(loadFilePath, "csv", new[] { "col1", "col2" }, null);

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("MalformedCsv", finding.Category);
            Assert.Equal(loadFilePath, finding.FilePath);
            Assert.Equal(2, finding.LineNumber);
            Assert.Equal("Unclosed quote at the end of the CSV file.", finding.Message);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidatorRunner_RunsCategory_OptBoundary_ReportsExactFinding()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "DOC001,VOL01,IMAGES/doc001.tif,Y,,1\n");

            var result = runner.ValidateLoadFile(loadFilePath, "opt", null, null);

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("OptBoundary", finding.Category);
            Assert.Equal(loadFilePath, finding.FilePath);
            Assert.Equal(1, finding.LineNumber);
            Assert.Equal("OPT line 1 has 6 columns, expected 7", finding.Message);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidatorRunner_RunsCategory_LineEnding_ReportsExactFinding()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "col1,col2\r\nval1,val2\n");

            var result = runner.ValidateLoadFile(loadFilePath, "csv", new[] { "col1", "col2" }, "\r\n");

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("LineEnding", finding.Category);
            Assert.Equal(loadFilePath, finding.FilePath);
            Assert.Equal(2, finding.LineNumber);
            Assert.Equal("Inconsistent line ending on or before line 2.", finding.Message);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidatorRunner_RunsCategory_UniqueId_ReportsExactFinding()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "Control Number,Name\nDOC001,One\nDOC001,Two\n");

            var result = runner.ValidateLoadFile(loadFilePath, "csv", null, null);

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("UniqueId", finding.Category);
            Assert.Equal(loadFilePath, finding.FilePath);
            Assert.Equal(3, finding.LineNumber);
            Assert.Equal("Duplicate Control Number: 'DOC001'", finding.Message);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidatorRunner_RunsCategory_PathReconciliation_ReportsExactFinding()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "Control Number,File Path\nDOC001,missing/file.pdf\n");

            var result = runner.ValidateLoadFile(
                loadFilePath,
                "csv",
                null,
                null,
                expectedPaths: new[] { "present/file.pdf" });

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("PathReconciliation", finding.Category);
            Assert.Equal(loadFilePath, finding.FilePath);
            Assert.Equal(2, finding.LineNumber);
            Assert.Equal("Load file path 'missing/file.pdf' does not resolve to any archive entry or sidecar file.", finding.Message);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidatorRunner_RunsCategory_BatesContinuity_ReportsExactFinding()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "Control Number,Bates Number\nDOC001,ABC0001\nDOC002,ABC0003\n");

            var result = runner.ValidateLoadFile(
                loadFilePath,
                "csv",
                null,
                null,
                bates: new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 4, Increment = 1 });

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("BatesContinuity", finding.Category);
            Assert.Equal(loadFilePath, finding.FilePath);
            Assert.Equal(3, finding.LineNumber);
            Assert.Equal("Expected Bates number ABC0002, got 'ABC0003'", finding.Message);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    [Fact]
    public void ValidatorRunner_CleanControl_AllCategoriesPassWithZeroFindings()
    {
        var runner = new ValidatorRunner();
        var loadFilePath = this.GetTempFilePath();
        try
        {
            File.WriteAllText(loadFilePath, "Control Number,File Path,Bates Number\nDOC001,folder/one.pdf,ABC0001\nDOC002,folder/two.pdf,ABC0002\n");

            var result = runner.ValidateLoadFile(
                loadFilePath,
                "csv",
                headerColumns: null,
                expectedEol: "\n",
                expectedPaths: new[] { "folder/one.pdf", "folder/two.pdf" },
                bates: new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 4, Increment = 1 });

            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        finally
        {
            File.Delete(loadFilePath);
        }
    }

    // ---- ValidatorRunner: Line Ending (EOL) Matrix ----

    [Theory]
    // Expected CR: valid CR passes; wrong LF/CRLF fails on line 1; mixed fails on line 2
    [InlineData("line1\rline2\r", "\r", true, 0)]
    [InlineData("line1\nline2\n", "\r", false, 1)]
    [InlineData("line1\r\nline2\r\n", "\r", false, 1)]
    [InlineData("line1\rline2\n", "\r", false, 2)]
    // Expected LF: valid LF passes; wrong CRLF/CR fails on line 1; mixed fails on line 2
    [InlineData("line1\nline2\n", "\n", true, 0)]
    [InlineData("line1\r\nline2\r\n", "\n", false, 1)]
    [InlineData("line1\rline2\r", "\n", false, 1)]
    [InlineData("line1\nline2\r\n", "\n", false, 2)]
    [InlineData("line1\nline2\r", "\n", false, 2)]
    // Expected CRLF: valid CRLF passes; wrong LF/CR fails on line 1; mixed fails on line 2
    [InlineData("line1\r\nline2\r\n", "\r\n", true, 0)]
    [InlineData("line1\nline2\n", "\r\n", false, 1)]
    [InlineData("line1\rline2\r", "\r\n", false, 1)]
    [InlineData("line1\r\nline2\n", "\r\n", false, 2)]
    [InlineData("line1\r\nline2\r", "\r\n", false, 2)]
    // Empty & whitespace controls
    [InlineData("", "\r", true, 0)]
    [InlineData("", "\n", true, 0)]
    [InlineData("", "\r\n", true, 0)]
    [InlineData("   \t  ", "\r", true, 0)]
    [InlineData("   \t  ", "\n", true, 0)]
    [InlineData("   \t  ", "\r\n", true, 0)]
    public void ValidatorRunner_ValidateLineEndings_Matrix(string content, string expectedEol, bool shouldPass, int expectedFindingLine)
    {
        var runner = new ValidatorRunner();
        var tempFile = this.GetTempFilePath();
        try
        {
            File.WriteAllText(tempFile, content);

            var result = runner.ValidateLineEndings(tempFile, expectedEol);

            if (shouldPass)
            {
                Assert.False(result.HasErrors);
                Assert.Empty(result.Findings);
            }
            else
            {
                Assert.True(result.HasErrors);
                var finding = Assert.Single(result.Findings);
                Assert.Equal(ValidationSeverity.Error, finding.Severity);
                Assert.Equal("LineEnding", finding.Category);
                Assert.Equal(tempFile, finding.FilePath);
                Assert.Equal(expectedFindingLine, finding.LineNumber);
                Assert.Equal($"Inconsistent line ending on or before line {expectedFindingLine}.", finding.Message);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidatorRunner_ValidateLoadFile_ExpectedCr_WithValidCr_Passes()
    {
        var runner = new ValidatorRunner();
        var tempFile = this.GetTempFilePath();
        try
        {
            File.WriteAllText(tempFile, "col1,col2\rval1,val2\r");

            var result = runner.ValidateLoadFile(tempFile, "csv", new[] { "col1", "col2" }, "\r");

            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidatorRunner_ValidateLoadFile_ExpectedCr_WithWrongLf_FailsWithExactFinding()
    {
        var runner = new ValidatorRunner();
        var tempFile = this.GetTempFilePath();
        try
        {
            File.WriteAllText(tempFile, "col1,col2\nval1,val2\n");

            var result = runner.ValidateLoadFile(tempFile, "csv", new[] { "col1", "col2" }, "\r");

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal("LineEnding", finding.Category);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(tempFile, finding.FilePath);
            Assert.Equal(1, finding.LineNumber);
            Assert.Equal("Inconsistent line ending on or before line 1.", finding.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidatorRunner_ValidateLoadFile_ExpectedCr_WithWrongCrlf_FailsWithExactFinding()
    {
        var runner = new ValidatorRunner();
        var tempFile = this.GetTempFilePath();
        try
        {
            File.WriteAllText(tempFile, "col1,col2\r\nval1,val2\r\n");

            var result = runner.ValidateLoadFile(tempFile, "csv", new[] { "col1", "col2" }, "\r");

            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal("LineEnding", finding.Category);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(tempFile, finding.FilePath);
            Assert.Equal(1, finding.LineNumber);
            Assert.Equal("Inconsistent line ending on or before line 1.", finding.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ---- ValidatorRunner: CSV Quoting, Newlines & Column Count Matrix ----

    [Theory]
    // Clean controls
    [InlineData("a,b,c\nd,e,f\n", 3, true, 0, 0)]
    [InlineData("a,b,c\n\"d,e\",f,g\n", 3, true, 0, 0)]
    [InlineData("a,b,c\nd,\"e,f\",g\n", 3, true, 0, 0)]
    [InlineData("a,b,c\n\"d\"\"e\",f,g\n", 3, true, 0, 0)]
    [InlineData("a,b,c\n\"d\"\"e\"\"f\",g,h\n", 3, true, 0, 0)]
    [InlineData("a,b,c\n\nd,e,f\n", 3, true, 0, 0)]
    // Mismatched column count
    [InlineData("a,b,c\nd,e\n", 3, false, 2, 2)]
    [InlineData("a,b,c\nd,e,f,g\n", 3, false, 2, 4)]
    [InlineData("a,b,c\nd,e,f,g,h,i\n", 3, false, 2, 6)]
    public void ValidatorRunner_ValidateLoadFile_CsvColumns_Matrix(
        string content, int expectedColumns, bool shouldPass, int expectedFindingLine, int actualColumns)
    {
        var runner = new ValidatorRunner();
        using var reader = new StringReader(content);
        var header = Enumerable.Range(1, expectedColumns).Select(i => $"col{i}").ToArray();

        var result = runner.ValidateLoadFile(reader, "memory.csv", "csv", header);

        if (shouldPass)
        {
            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        else
        {
            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("ColumnCount", finding.Category);
            Assert.Equal("memory.csv", finding.FilePath);
            Assert.Equal(expectedFindingLine, finding.LineNumber);
            Assert.Equal($"Expected {expectedColumns} columns, got {actualColumns} on line {expectedFindingLine}", finding.Message);
        }
    }

    [Fact]
    public void ValidatorRunner_ValidateLoadFile_Csv_QuotedNewline_Passes()
    {
        var runner = new ValidatorRunner();
        var content = "col1,col2,col3\n\"first line\nsecond line\",val2,val3\n\"third\nfourth\nfifth\",val4,val5\n";
        using var reader = new StringReader(content);

        var result = runner.ValidateLoadFile(reader, "memory.csv", "csv", new[] { "col1", "col2", "col3" });

        Assert.False(result.HasErrors);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ValidatorRunner_ValidateLoadFile_Csv_UnclosedQuoteAtEof_ReportsMalformedCsvExactFinding()
    {
        var runner = new ValidatorRunner();
        var content = "col1,col2\n\"unclosed,val\n";
        using var reader = new StringReader(content);

        var result = runner.ValidateLoadFile(reader, "memory.csv", "csv", new[] { "col1", "col2" });

        Assert.True(result.HasErrors);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("MalformedCsv", finding.Category);
        Assert.Equal("memory.csv", finding.FilePath);
        Assert.Equal(2, finding.LineNumber);
        Assert.Equal("Unclosed quote at the end of the CSV file.", finding.Message);
    }

    [Fact]
    public void ValidatorRunner_ValidateLoadFile_Csv_EmptyContent_Passes()
    {
        var runner = new ValidatorRunner();
        using var reader = new StringReader(string.Empty);

        var result = runner.ValidateLoadFile(reader, "memory.csv", "csv", new[] { "col1", "col2" });

        Assert.False(result.HasErrors);
        Assert.Empty(result.Findings);
    }

    // ---- ValidatorRunner: DAT & Concordance Delimiter Matrices ----

    [Theory]
    // Default DAT delimiters (\x14 and \xfe)
    [InlineData("DOCID\u0014FILEPATH\ndoc001\u0014file.pdf\n", '\u0014', '\u00fe', 2, true, 0, 0)]
    [InlineData("DOCID\u0014FILEPATH\ndoc001\n", '\u0014', '\u00fe', 2, false, 2, 1)]
    [InlineData("DOCID\u0014FILEPATH\ndoc001\u0014file.pdf\u0014extra\n", '\u0014', '\u00fe', 2, false, 2, 3)]
    [InlineData("DOCID\u0014NOTE\ndoc001\u0014\u00fenote\u0014value\u00fe\n", '\u0014', '\u00fe', 2, true, 0, 0)]
    [InlineData("DOCID\u0014NOTE\ndoc001\u0014\u00fenote\u00fe\u00fevalue\u00fe\n", '\u0014', '\u00fe', 2, true, 0, 0)]
    [InlineData("DOCID\u0014FILEPATH\n\ndoc001\u0014file.pdf\n", '\u0014', '\u00fe', 2, true, 0, 0)]
    // Custom DAT delimiters (\x1e col, \xfe quote)
    [InlineData("DOCID\u001eFILEPATH\ndoc001\u001efile.pdf\n", '\u001e', '\u00fe', 2, true, 0, 0)]
    [InlineData("DOCID\u001eFILEPATH\ndoc001\n", '\u001e', '\u00fe', 2, false, 2, 1)]
    // Custom pipe and single-quote delimiters
    [InlineData("DOCID|FILEPATH\ndoc001|file.pdf\n", '|', '\'', 2, true, 0, 0)]
    [InlineData("DOCID|NOTE\n'doc|001'|note1\n", '|', '\'', 2, true, 0, 0)]
    [InlineData("DOCID|NOTE\n'doc''001'|note1\n", '|', '\'', 2, true, 0, 0)]
    [InlineData("DOCID|FILEPATH\ndoc001\n", '|', '\'', 2, false, 2, 1)]
    [InlineData("DOCID|FILEPATH\ndoc001|file.pdf|extra\n", '|', '\'', 2, false, 2, 3)]
    public void ValidatorRunner_ValidateLoadFile_Dat_Matrix(
        string content, char colDelim, char quoteDelim, int expectedColumns, bool shouldPass, int expectedFindingLine, int actualColumns)
    {
        var runner = new ValidatorRunner();
        using var reader = new StringReader(content);

        var result = runner.ValidateLoadFile(
            reader,
            "test.dat",
            "dat",
            headerColumns: null,
            columnDelimiter: colDelim,
            quoteDelimiter: quoteDelim);

        if (shouldPass)
        {
            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        else
        {
            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("ColumnCount", finding.Category);
            Assert.Equal("test.dat", finding.FilePath);
            Assert.Equal(expectedFindingLine, finding.LineNumber);
            Assert.Equal($"Expected {expectedColumns} columns, got {actualColumns} on line {expectedFindingLine}", finding.Message);
        }
    }

    [Theory]
    // Concordance standard delimiter format (\x14 and \xfe)
    [InlineData("\u00feDOCID\u00fe\u0014\u00feFILEPATH\u00fe\n\u00fedoc001\u00fe\u0014\u00fefile.pdf\u00fe\n", 2, true, 0, 0)]
    [InlineData("\u00feDOCID\u00fe\u0014\u00feFILEPATH\u00fe\n\u00fedoc001\u00fe\n", 2, false, 2, 1)]
    [InlineData("\u00feDOCID\u00fe\u0014\u00feFILEPATH\u00fe\n\u00fedoc001\u00fe\u0014\u00fefile.pdf\u00fe\u0014\u00feextra\u00fe\n", 2, false, 2, 3)]
    [InlineData("\u00feDOCID\u00fe\u0014\u00feNOTE\u00fe\n\u00fedoc001\u00fe\u0014\u00fenote\u00fe\u00fevalue\u00fe\n", 2, true, 0, 0)]
    [InlineData("\u00feDOCID\u00fe\u0014\u00feFILEPATH\u00fe\n\n\u00fedoc001\u00fe\u0014\u00fefile.pdf\u00fe\n", 2, true, 0, 0)]
    public void ValidatorRunner_ValidateLoadFile_Concordance_Matrix(
        string content, int expectedColumns, bool shouldPass, int expectedFindingLine, int actualColumns)
    {
        var runner = new ValidatorRunner();
        using var reader = new StringReader(content);

        var result = runner.ValidateLoadFile(
            reader,
            "test.dat",
            "concordance",
            headerColumns: null);

        if (shouldPass)
        {
            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        else
        {
            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("ColumnCount", finding.Category);
            Assert.Equal("test.dat", finding.FilePath);
            Assert.Equal(expectedFindingLine, finding.LineNumber);
            Assert.Equal($"Expected {expectedColumns} columns, got {actualColumns} on line {expectedFindingLine}", finding.Message);
        }
    }

    // ---- ValidatorRunner: OPT Column Boundary Matrix ----

    [Theory]
    // 7 columns passes (clean control); 6 and 8 columns fail with exact finding
    [InlineData("a,b,c,d,e,f,g\n", true, 0, 0)]
    [InlineData("a,b,c,d,e,f\n", false, 1, 6)]
    [InlineData("a,b,c,d,e,f,g,h\n", false, 1, 8)]
    [InlineData("a,b,c,d,e,f,g\n\na,b,c,d,e,f,g\n", true, 0, 0)]
    public void ValidatorRunner_ValidateLoadFile_Opt_Matrix(
        string content, bool shouldPass, int expectedFindingLine, int actualColumns)
    {
        var runner = new ValidatorRunner();
        using var reader = new StringReader(content);

        var result = runner.ValidateLoadFile(reader, "test.opt", "opt", headerColumns: null);

        if (shouldPass)
        {
            Assert.False(result.HasErrors);
            Assert.Empty(result.Findings);
        }
        else
        {
            Assert.True(result.HasErrors);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("OptBoundary", finding.Category);
            Assert.Equal("test.opt", finding.FilePath);
            Assert.Equal(expectedFindingLine, finding.LineNumber);
            Assert.Equal($"OPT line {expectedFindingLine} has {actualColumns} columns, expected 7", finding.Message);
        }
    }

    [Fact]
    public void ValidatorRunner_ValidateLoadFile_Opt_MixedLines_ReportsAllViolationsWithExactLines()
    {
        var runner = new ValidatorRunner();
        var content = "a,b,c,d,e,f,g\na,b,c,d,e,f\na,b,c,d,e,f,g,h\na,b,c,d,e,f,g\n";
        using var reader = new StringReader(content);

        var result = runner.ValidateLoadFile(reader, "test.opt", "opt", headerColumns: null);

        Assert.True(result.HasErrors);
        Assert.Equal(2, result.Findings.Count);

        var finding1 = result.Findings[0];
        Assert.Equal(ValidationSeverity.Error, finding1.Severity);
        Assert.Equal("OptBoundary", finding1.Category);
        Assert.Equal("test.opt", finding1.FilePath);
        Assert.Equal(2, finding1.LineNumber);
        Assert.Equal("OPT line 2 has 6 columns, expected 7", finding1.Message);

        var finding2 = result.Findings[1];
        Assert.Equal(ValidationSeverity.Error, finding2.Severity);
        Assert.Equal("OptBoundary", finding2.Category);
        Assert.Equal("test.opt", finding2.FilePath);
        Assert.Equal(3, finding2.LineNumber);
        Assert.Equal("OPT line 3 has 8 columns, expected 7", finding2.Message);
    }
}
