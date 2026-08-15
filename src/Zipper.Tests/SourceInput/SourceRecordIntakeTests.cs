using Xunit;
using Zipper.SourceInput;

namespace Zipper.Tests;

public class SourceRecordIntakeTests
{
    private static SourceRecordIntake Create(int maxRecords = SourceRecordIntake.MaxSourceRecords)
        => new("test source", maxRecords);

    private static bool TryAdd(
        SourceRecordIntake intake,
        string rawPath,
        string? fileTypeText,
        out string? error,
        string rowContext = "Row 1")
        => intake.TryAdd(rawPath, fileTypeText, controlNumber: null, batesNumber: null, metadata: null, rowContext, out error);

    [Fact]
    public void TryAdd_CsvDeclaredFileType_NormalizesAndValidates()
    {
        var intake = Create();

        var ok = TryAdd(intake, "docs/a.pdf", ".PDF", out var error);

        Assert.True(ok, error);
        Assert.Null(error);
        Assert.True(intake.TryBuild(out var records, out _));
        Assert.Equal("docs/a.pdf", records[0].RelativePath);
        Assert.Equal("pdf", records[0].FileType);
    }

    [Fact]
    public void TryAdd_CsvDeclaredFileTypeUnknown_ReturnsError()
    {
        var intake = Create();

        var ok = TryAdd(intake, "a.bin", "bin", out var error);

        Assert.False(ok);
        Assert.Contains("unsupported File Type 'bin'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_CsvFileTypeExtensionMismatch_ReturnsError()
    {
        var intake = Create();

        var ok = TryAdd(intake, "a.eml", "pdf", out var error);

        Assert.False(ok);
        Assert.Contains("extension '.eml' does not match FileType 'pdf'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_DirectoryFileType_InferredFromExtension()
    {
        var intake = Create();

        var ok = TryAdd(intake, "folder/a.jpeg", fileTypeText: null, out var error);

        Assert.True(ok, error);
        Assert.True(intake.TryBuild(out var records, out _));
        Assert.Equal("jpg", records[0].FileType);
    }

    [Fact]
    public void TryAdd_DirectoryFileTypeUnsupportedExtension_ReturnsError()
    {
        var intake = Create();

        var ok = TryAdd(intake, "notes.txt", fileTypeText: null, out var error);

        Assert.False(ok);
        Assert.Contains(".txt", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_DirectoryFileTypeNoExtension_ReturnsError()
    {
        var intake = Create();

        var ok = TryAdd(intake, "notes", fileTypeText: null, out var error);

        Assert.False(ok);
        Assert.Contains("has no extension", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_CsvFileTypeEmpty_ReturnsError()
    {
        var intake = Create();

        var ok = TryAdd(intake, "a.pdf", "   ", out var error);

        Assert.False(ok);
        Assert.Contains("FileType is empty", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_UnsafePath_ReturnsErrorWithContext()
    {
        var intake = Create();

        var ok = intake.TryAdd("../escape.pdf", "pdf", null, null, null, "Row 3", out var error);

        Assert.False(ok);
        Assert.Contains("Row 3", error, StringComparison.Ordinal);
        Assert.Contains("invalid FilePath", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_DuplicatePath_ReturnsError()
    {
        var intake = Create();
        Assert.True(TryAdd(intake, "docs/a.pdf", "pdf", out _));

        var ok = TryAdd(intake, "docs/a.pdf", "pdf", out var error);

        Assert.False(ok);
        Assert.Contains("Duplicate FilePath 'docs/a.pdf'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_DuplicatePathDifferentCase_ReturnsError()
    {
        var intake = Create();
        Assert.True(TryAdd(intake, "docs/a.pdf", "pdf", out _));

        var ok = TryAdd(intake, "docs/A.PDF", "pdf", out var error);

        Assert.False(ok);
        Assert.Contains("Duplicate FilePath", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_DuplicateControlNumber_ReturnsError()
    {
        var intake = Create();
        Assert.True(intake.TryAdd("a.pdf", "pdf", "ABC-001", null, null, "Row 2", out _));

        var ok = intake.TryAdd("b.eml", "eml", "abc-001", null, null, "Row 3", out var error);

        Assert.False(ok);
        Assert.Contains("Row 3: Duplicate ControlNumber 'abc-001'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_DuplicateBatesNumber_ReturnsError()
    {
        var intake = Create();
        Assert.True(intake.TryAdd("a.pdf", "pdf", null, "ABC_0001", null, "Row 2", out _));

        var ok = intake.TryAdd("b.eml", "eml", null, "ABC_0001", null, "Row 3", out var error);

        Assert.False(ok);
        Assert.Contains("Row 3: Duplicate BatesNumber", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_IdentityValueWithPathSeparator_ReturnsError()
    {
        var intake = Create();

        var ok = intake.TryAdd("a.pdf", "pdf", "bad\\id", null, null, "Row 2", out var error);

        Assert.False(ok);
        Assert.Contains("ControlNumber contains invalid characters", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_IdentityValueWithControlCharacter_ReturnsError()
    {
        var intake = Create();

        var ok = intake.TryAdd("a.pdf", "pdf", "bad\u0007id", null, null, "Row 2", out var error);

        Assert.False(ok);
        Assert.Contains("ControlNumber contains invalid characters", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAdd_MetadataPassthrough_PreservedOnRecord()
    {
        var intake = Create();
        var metadata = new Dictionary<string, string> { ["Custodian"] = "Acme" };

        var ok = intake.TryAdd("a.pdf", "pdf", null, null, metadata, "Row 2", out var error);

        Assert.True(ok, error);
        Assert.True(intake.TryBuild(out var records, out _));
        Assert.Equal("Acme", records[0].Metadata!["Custodian"]);
    }

    [Fact]
    public void TryAdd_RowCapExceeded_ReturnsError()
    {
        var intake = Create(maxRecords: 2);
        Assert.True(TryAdd(intake, "a.pdf", "pdf", out _));
        Assert.True(TryAdd(intake, "b.eml", "eml", out _));

        var ok = TryAdd(intake, "c.tiff", "tiff", out var error, rowContext: "Row 4");

        Assert.False(ok);
        Assert.Contains("Row 4 exceeds the maximum of 2 Source Records", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_EmptyIntake_ReturnsError()
    {
        var intake = Create();

        var ok = intake.TryBuild(out _, out var error);

        Assert.False(ok);
        Assert.Contains("test source contains no Source Records", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMapCsvColumns_RequiredAndIdentityRoles_Mapped()
    {
        var header = new[] { "ControlNumber", "FilePath", "FileType", "BatesNumber", "Custodian" };

        var ok = SourceRecordIntake.TryMapCsvColumns(header, out var layout, out var error);

        Assert.True(ok, error);
        Assert.Equal(1, layout.PathIndex);
        Assert.Equal(2, layout.TypeIndex);
        Assert.Equal(0, layout.ControlIndex);
        Assert.Equal(3, layout.BatesIndex);
        Assert.Equal(new[] { (4, "Custodian") }, layout.MetadataColumns);
    }

    [Fact]
    public void TryMapCsvColumns_MissingFilePath_ReturnsError()
    {
        var header = new[] { "FileType", "Other" };

        var ok = SourceRecordIntake.TryMapCsvColumns(header, out _, out var error);

        Assert.False(ok);
        Assert.Contains("missing the required 'FilePath'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMapCsvColumns_AliasConflict_ReturnsError()
    {
        var header = new[] { "FilePath", "FileType", "ControlNumber", "DocId" };

        var ok = SourceRecordIntake.TryMapCsvColumns(header, out _, out var error);

        Assert.False(ok);
        Assert.Contains("multiple columns", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMapCsvColumns_DuplicateNormalizedHeader_ReturnsError()
    {
        var header = new[] { "FilePath", "FileType", "Notes", "N otes" };

        var ok = SourceRecordIntake.TryMapCsvColumns(header, out _, out var error);

        Assert.False(ok);
        Assert.Contains("duplicate column", error, StringComparison.OrdinalIgnoreCase);
    }
}
