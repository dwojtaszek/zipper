using Xunit;

namespace Zipper.Tests;

public class TempDirectoryTestBaseTests : TempDirectoryTestBase
{
    [Fact]
    public void TempDir_UsesSystemTemporaryDirectory()
    {
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
            Path.GetDirectoryName(Path.GetFullPath(this.TempDir)));
    }

    [Fact]
    public void GetTempFilePath_ReturnsUniqueUnusedPathUnderOwnedDirectory()
    {
        var first = this.GetTempFilePath();
        var second = this.GetTempFilePath();

        Assert.NotEqual(first, second);
        Assert.StartsWith(this.TempDir + Path.DirectorySeparatorChar, first, StringComparison.Ordinal);
        Assert.StartsWith(this.TempDir + Path.DirectorySeparatorChar, second, StringComparison.Ordinal);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    [Fact]
    public void Dispose_RemovesOnlyItsOwnedDirectory()
    {
        var owner = new TempDirectoryProbe();
        var ownedPath = owner.DirectoryPath;
        var siblingPath = Path.Combine(
            Path.GetDirectoryName(ownedPath)!,
            $"zipper_test_sentinel_{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Path.Combine(ownedPath, "nested"));
        File.WriteAllText(siblingPath, "pre-existing");

        try
        {
            owner.Dispose();

            Assert.False(Directory.Exists(ownedPath));
            Assert.Equal("pre-existing", File.ReadAllText(siblingPath));
        }
        finally
        {
            if (Directory.Exists(ownedPath))
            {
                Directory.Delete(ownedPath, true);
            }

            if (File.Exists(siblingPath))
            {
                File.Delete(siblingPath);
            }
        }
    }

    private sealed class TempDirectoryProbe : TempDirectoryTestBase
    {
        public string DirectoryPath => this.TempDir;
    }
}
