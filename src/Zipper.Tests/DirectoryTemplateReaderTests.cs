using Xunit;
using Zipper.SourceInput;

namespace Zipper.Tests;

public class DirectoryTemplateReaderTests : IDisposable
{
    private readonly string tempDir;

    public DirectoryTemplateReaderTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_dir_template_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    private string CreateFile(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var full = Path.Combine(this.tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "template content");
        return full;
    }

    [Fact]
    public void TryRead_DotfileEntry_IncludedNotSkipped()
    {
        this.CreateFile("root.pdf");
        this.CreateFile(".hidden.pdf");

        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out var records, out var error);

        Assert.True(ok, error);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.RelativePath == ".hidden.pdf");
    }

    [Fact]
    public void TryRead_NestedStructure_RecreatesRelativePathsSorted()
    {
        this.CreateFile("root.pdf");
        this.CreateFile("folder_a/inner.eml");
        this.CreateFile("folder_a/deep/x.tiff");

        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out var records, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "folder_a/deep/x.tiff", "folder_a/inner.eml", "root.pdf" }, records.Select(r => r.RelativePath).ToArray());
        Assert.Equal("tiff", records[0].FileType);
        Assert.Equal("eml", records[1].FileType);
        Assert.Equal("pdf", records[2].FileType);
        Assert.Null(records[0].ControlNumber);
        Assert.Null(records[0].BatesNumber);
    }

    [Theory]
    [InlineData("a.jpeg", "jpg")]
    [InlineData("a.JPG", "jpg")]
    [InlineData("a.tif", "tiff")]
    [InlineData("a.TIFF", "tiff")]
    [InlineData("a.docx", "docx")]
    [InlineData("a.xlsx", "xlsx")]
    [InlineData("a.eml", "eml")]
    [InlineData("a.pdf", "pdf")]
    public void TryRead_ExtensionInference_MapsToFileType(string name, string expectedType)
    {
        this.CreateFile(name);

        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out var records, out _);

        Assert.True(ok);
        Assert.Single(records);
        Assert.Equal(expectedType, records[0].FileType);
    }

    [Fact]
    public void TryRead_UnsupportedExtension_ReturnsFalse()
    {
        this.CreateFile("notes.txt");

        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out _, out var error);

        Assert.False(ok);
        Assert.Contains(".txt", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_FileWithoutExtension_ReturnsFalse()
    {
        this.CreateFile("README");

        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryRead_EmptyDirectory_ReturnsFalse()
    {
        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryRead_MissingDirectory_ReturnsFalse()
    {
        var ok = DirectoryTemplateReader.TryRead(Path.Combine(this.tempDir, "nope"), out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
