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
}
