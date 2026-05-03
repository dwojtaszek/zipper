using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

public class LoremIpsumDataTests
{
    [Fact]
    public void GetParagraph_ReturnsNonEmptyText()
    {
        var result = LoremIpsum.GetParagraph(new Random(42));
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetSentence_ReturnsNonEmptyText()
    {
        var result = LoremIpsum.GetSentence(new Random(42));
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetSentence_EndsWithPeriod()
    {
        var result = LoremIpsum.GetSentence(new Random(42));
        Assert.EndsWith(".", result);
    }

    [Fact]
    public void GetParagraph_IsDeterministicForSameSeed()
    {
        var r1 = LoremIpsum.GetParagraph(new Random(99));
        var r2 = LoremIpsum.GetParagraph(new Random(99));
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void GetParagraph_DiffersForDifferentSeeds()
    {
        var r1 = LoremIpsum.GetParagraph(new Random(1));
        var r2 = LoremIpsum.GetParagraph(new Random(2));
        Assert.NotEqual(r1, r2);
    }
}
