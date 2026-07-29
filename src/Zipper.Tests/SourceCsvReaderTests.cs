using Xunit;
using Zipper.SourceInput;

namespace Zipper.Tests;

public class SourceCsvReaderTests : IDisposable
{
    private readonly string tempDir;

    public SourceCsvReaderTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_source_csv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    private string WriteCsv(string content, string name = "input.csv")
    {
        var path = Path.Combine(this.tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void TryRead_DuplicateNormalizedHeaders_ReturnsFalse()
    {
        // "File Path" and "File_Path" normalize to the same column (REQ-197 header rules).
        var path = this.WriteCsv("File Path,File_Path,FileType\na.pdf,a.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("multiple columns", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_DuplicateExactHeaders_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,FileType,Notes,Notes\na.pdf,pdf,x,y\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("duplicate column", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_AliasConflictControlAndDocId_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,FileType,ControlNumber,DocId\na.pdf,pdf,C1,C2\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("multiple columns", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_AliasConflictBatesAndBegBates_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,FileType,Bates,BegBates\na.pdf,pdf,B1,B2\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("multiple columns", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_BlankLineBeforeBadRow_ReportsPhysicalRowNumber()
    {
        var path = this.WriteCsv("FilePath,FileType\n\n../bad.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("Row 3", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FilePath,FileType,ControlNumber\na.pdf,pdf,bad\\id\n")]
    [InlineData("FilePath,FileType,BatesNumber\na.pdf,pdf,bad/id\n")]
    public void TryRead_IdentityValueWithPathSeparator_ReturnsFalse(string content)
    {
        var path = this.WriteCsv(content);

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("invalid characters", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_MinimalCsv_ReturnsRecords()
    {
        var path = this.WriteCsv("FilePath,FileType\ndocs/a.pdf,pdf\nb.eml,eml\nc/x.tiff,tiff\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(3, records.Count);
        Assert.Equal("docs/a.pdf", records[0].RelativePath);
        Assert.Equal("pdf", records[0].FileType);
        Assert.Equal("b.eml", records[1].RelativePath);
        Assert.Equal("eml", records[1].FileType);
        Assert.Equal("c/x.tiff", records[2].RelativePath);
        Assert.Equal("tiff", records[2].FileType);
        Assert.Null(records[0].ControlNumber);
        Assert.Null(records[0].BatesNumber);
        Assert.Null(records[0].Metadata);
    }

    [Fact]
    public void TryRead_FullCsv_MapsControlBatesAndExtraColumns()
    {
        var path = this.WriteCsv(
            "ControlNumber,FilePath,FileType,BatesNumber,Custodian,Reviewed\n" +
            "ABC-001,a.pdf,pdf,ABC_00000001,Jsmith,yes\n" +
            "ABC-002,sub/b.eml,eml,ABC_00000002,Jdoe,no\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, records.Count);
        Assert.Equal("ABC-001", records[0].ControlNumber);
        Assert.Equal("ABC_00000001", records[0].BatesNumber);
        Assert.NotNull(records[0].Metadata);
        Assert.Equal("Jsmith", records[0].Metadata!["Custodian"]);
        Assert.Equal("yes", records[0].Metadata!["Reviewed"]);
        Assert.Equal("Jdoe", records[1].Metadata!["Custodian"]);
        Assert.Equal("no", records[1].Metadata!["Reviewed"]);
    }

    [Fact]
    public void TryRead_HeadersCaseInsensitiveAndTrimmed_Matches()
    {
        var path = this.WriteCsv("  filepath , FILETYPE \na.pdf,PDF\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Single(records);
        Assert.Equal("a.pdf", records[0].RelativePath);
        Assert.Equal("pdf", records[0].FileType);
    }

    [Fact]
    public void TryRead_QuotedFieldsWithCommas_PreservesValue()
    {
        var path = this.WriteCsv("FilePath,FileType,Notes\n\"a,b.pdf\",pdf,\"needs review, urgent\"\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Single(records);
        Assert.Equal("a,b.pdf", records[0].RelativePath);
        Assert.Equal("needs review, urgent", records[0].Metadata!["Notes"]);
    }

    [Fact]
    public void TryRead_EscapedQuotesInQuotedField_Unescapes()
    {
        var path = this.WriteCsv("FilePath,FileType,Notes\na.pdf,pdf,\"say \"\"hi\"\"\"\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Equal("say \"hi\"", records[0].Metadata!["Notes"]);
    }

    [Fact]
    public void TryRead_MultilineQuotedMetadata_PreservesNewline()
    {
        var path = this.WriteCsv("FilePath,FileType,Notes\na.pdf,pdf,\"line1\nline2\"\nb.pdf,pdf,x\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Equal(2, records.Count);
        Assert.Equal("line1\nline2", records[0].Metadata!["Notes"]);
    }

    [Fact]
    public void TryRead_BlankLines_Skipped()
    {
        var path = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n\n   \nb.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void TryRead_Utf8Bom_StillParsesHeaders()
    {
        var path = this.WriteCsv("﻿FilePath,FileType\na.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Single(records);
    }

    [Fact]
    public void TryRead_MissingFilePathColumn_ReturnsFalse()
    {
        var path = this.WriteCsv("FileType,Other\npdf,x\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("FilePath", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_MissingFileTypeColumn_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,Other\na.pdf,x\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("FileType", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_HeaderOnly_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,FileType\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryRead_UnknownFileType_ReturnsFalseWithRowNumber()
    {
        var path = this.WriteCsv("FilePath,FileType\na.pdf,pdf\nb.bin,bin\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("bin", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_PathTraversalRow_ReturnsFalseWithRowNumber()
    {
        var path = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n../escape.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("3", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_DuplicateRelativePaths_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,FileType\ndocs/a.pdf,pdf\ndocs\\a.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("Duplicate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_DuplicateRelativePathsCaseInsensitive_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,FileType\nDocs/A.pdf,pdf\ndocs/a.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("Duplicate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_FileTypeWithLeadingDot_Normalizes()
    {
        var path = this.WriteCsv("FilePath,FileType\na.pdf,.PDF\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Equal("pdf", records[0].FileType);
    }

    [Fact]
    public void TryRead_RowWithFewerFieldsThanHeader_MissingFieldsTreatedEmpty()
    {
        var path = this.WriteCsv("FilePath,FileType,Notes\na.pdf,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out var records, out _);

        Assert.True(ok);
        Assert.Single(records);
        Assert.Null(records[0].Metadata);
    }

    [Fact]
    public void TryRead_EmptyFilePathRow_ReturnsFalse()
    {
        var path = this.WriteCsv("FilePath,FileType\n,pdf\n");

        var ok = SourceCsvReader.TryRead(path, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsFalse()
    {
        var ok = SourceCsvReader.TryRead(Path.Combine(this.tempDir, "nope.csv"), out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
