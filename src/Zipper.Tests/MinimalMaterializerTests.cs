using Xunit;

namespace Zipper.Tests;

public class MinimalMaterializerTests
{
    [Fact]
    public void ShouldValidate_Always_ReturnsFalse()
    {
        // MinimalMaterializer is the no-op test/in-memory seam; post-generation
        // validation must only run for the filesystem-backed materializer.
        var materializer = new MinimalMaterializer();

        Assert.False(materializer.ShouldValidate);
    }
}
