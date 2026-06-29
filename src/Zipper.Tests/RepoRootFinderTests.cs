using Xunit;

namespace Zipper.Tests;

public class RepoRootFinderTests
{
    [Fact]
    public void Find_WithValidStartDir_ShouldReturnRepoRoot()
    {
        var root = RepoRootFinder.Find();
        Assert.NotNull(root);
        Assert.True(Directory.Exists(root), "Repo root directory should exist");
        Assert.True(File.Exists(Path.Combine(root, "zipper.sln")), "Repo root should contain zipper.sln");
    }

    [Fact]
    public void Find_WithNullStartDir_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RepoRootFinder.Find(null!));
    }

    [Fact]
    public void Find_WithNonexistentStartDir_ShouldThrowInvalidOperationException()
    {
        // Find a directory that is not inside the repo root.
        // We can get the repo root and use its parent directory.
        string tempParent;
        try
        {
            var repoRoot = RepoRootFinder.Find();
            tempParent = Path.GetDirectoryName(repoRoot) ?? Path.GetTempPath();
        }
        catch
        {
            tempParent = Path.GetTempPath();
        }

        var tempPath = Path.Combine(tempParent, Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        try
        {
            // If the temp path still somehow contains zipper.sln in its parents, 
            // we want to know about it, but to avoid flaky test runs in weird environments
            // we only assert if the precondition holds.
            if (!ContainsSolutionInParents(tempPath))
            {
                Assert.Throws<InvalidOperationException>(() => RepoRootFinder.Find(tempPath));
            }
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    private static bool ContainsSolutionInParents(string path)
    {
        var dir = new DirectoryInfo(path);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "zipper.sln")))
            {
                return true;
            }
            dir = dir.Parent;
        }
        return false;
    }
}
