using Xunit;

namespace Zipper.Tests;

public class ProductionFileMaterializerTests
{
    [Fact]
    public void ShouldValidate_Always_ReturnsTrue()
    {
        // The filesystem-backed materializer owns Production Set artifacts, so
        // post-generation validation must run for it.
        var materializer = new ProductionFileMaterializer();

        Assert.True(materializer.ShouldValidate);
    }
}
