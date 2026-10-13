using Xunit;
using Zipper.SourceInput;

namespace Zipper.Tests;

public class DirectoryTemplateReaderTests : IDisposable
{
    private readonly string tempDir;

    public DirectoryTemplateReaderTests()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), $"zipper_dir_template_{Guid.NewGuid():N}");
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
    public void TryRead_FileCapExceeded_ReturnsFalse()
    {
        this.CreateFile("a.pdf");
        this.CreateFile("b.eml");
        this.CreateFile("c.tiff");

        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out _, out var error, maxRecords: 2);

        Assert.False(ok);
        Assert.Contains("exceeds the maximum of 2 Source Records", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_FileCapExact_Allowed()
    {
        this.CreateFile("a.pdf");
        this.CreateFile("b.eml");

        var ok = DirectoryTemplateReader.TryRead(this.tempDir, out var records, out var error, maxRecords: 2);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, records.Count);
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

    [SkippableFact]
    public void TryRead_InaccessibleSubdirectory_ReturnsFalse()
    {
        Skip.If(OperatingSystem.IsWindows(), "chmod-based permission tests are not reliable on Windows");
        Skip.If(SourceInputTestHelper.RunningAsRoot(), "Permission bits do not restrict root; cannot provoke UnauthorizedAccessException");

        var blockedDir = Path.Combine(this.tempDir, "blocked");
        Directory.CreateDirectory(blockedDir);
        File.WriteAllText(Path.Combine(blockedDir, "x.pdf"), "template content");
        try
        {
            SourceInputTestHelper.RunChmod(blockedDir, "000");
            var ok = DirectoryTemplateReader.TryRead(this.tempDir, out _, out var error);
            Assert.False(ok);
            Assert.Contains("Cannot read directory template", error, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                SourceInputTestHelper.RunChmod(blockedDir, "700");
                Directory.Delete(blockedDir, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [SkippableFact]
    public void TryRead_SymbolicLinksExternalTargetsAndCycles_SkippedAndOrdinaryEntriesPreserved()
    {
        var templateDir = Path.Combine(this.tempDir, "template");
        var outsideDir = Path.Combine(this.tempDir, "outside");
        Directory.CreateDirectory(templateDir);
        Directory.CreateDirectory(outsideDir);

        const string ordinarySentinel = "ordinary-template-native-file-content";
        const string hiddenSentinel = "hidden-template-native-file-content";
        const string outsideSentinel = "outside-unsupported-sentinel-content";

        var ordinaryFile = Path.Combine(templateDir, "doc.pdf");
        File.WriteAllText(ordinaryFile, ordinarySentinel);

        var hiddenFile = Path.Combine(templateDir, ".hidden.pdf");
        File.WriteAllText(hiddenFile, hiddenSentinel);

        var outsideFile = Path.Combine(outsideDir, "outside_unsupported.txt");
        File.WriteAllText(outsideFile, outsideSentinel);

        var outsideDirLink = Path.Combine(templateDir, "link_to_outside");
        var ancestorCycleLink = Path.Combine(templateDir, "cycle_to_root");
        var fileSymlink = Path.Combine(templateDir, "file_symlink.pdf");
        var danglingTarget = Path.Combine(this.tempDir, $"dangling_target_{Guid.NewGuid():N}.pdf");
        var danglingLink = Path.Combine(templateDir, "dangling_link.pdf");

        try
        {
            Directory.CreateSymbolicLink(outsideDirLink, outsideDir);
            Directory.CreateSymbolicLink(ancestorCycleLink, templateDir);
            File.CreateSymbolicLink(fileSymlink, ordinaryFile);
            File.CreateSymbolicLink(danglingLink, danglingTarget);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                or PlatformNotSupportedException
                                or System.ComponentModel.Win32Exception
                                or IOException)
        {
            Skip.If(true, $"Symbolic link creation is not supported or permitted on this platform/environment: {ex.Message}");
        }

        try
        {
            var deadline = TimeSpan.FromSeconds(5);
            var readTask = Task.Run(() =>
            {
                var success = DirectoryTemplateReader.TryRead(templateDir, out var r, out var err);
                return (success, records: r, error: err);
            });

            Assert.True(readTask.Wait(deadline), "DirectoryTemplateReader exceeded bounded completion deadline (possible cycle traversal loop).");

            var (ok, records, error) = readTask.Result;
            Assert.True(ok, error);
            Assert.NotNull(records);
            Assert.Equal(2, records.Count);
            Assert.Equal(new[] { ".hidden.pdf", "doc.pdf" }, records.Select(r => r.RelativePath).ToArray());
            Assert.Equal("pdf", records[0].FileType);
            Assert.Equal("pdf", records[1].FileType);

            // Keep target sentinels unchanged
            Assert.True(File.Exists(ordinaryFile));
            Assert.Equal(ordinarySentinel, File.ReadAllText(ordinaryFile));
            Assert.True(File.Exists(hiddenFile));
            Assert.Equal(hiddenSentinel, File.ReadAllText(hiddenFile));
            Assert.True(File.Exists(outsideFile));
            Assert.Equal(outsideSentinel, File.ReadAllText(outsideFile));
        }
        finally
        {
            try
            {
                if (Directory.Exists(ancestorCycleLink))
                {
                    Directory.Delete(ancestorCycleLink);
                }

                if (Directory.Exists(outsideDirLink))
                {
                    Directory.Delete(outsideDirLink);
                }

                File.Delete(fileSymlink);
                File.Delete(danglingLink);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}

