using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

public class EncodingDataTests
{
    private static readonly System.Collections.Generic.HashSet<string> ValidEncodings =
        new() { "UTF-8", "ASCII", "UTF-16", "ISO-8859-1", "Windows-1252" };

    [Fact]
    public void GetRandom_ReturnsNonEmptyString()
    {
        var result = Encodings.GetRandom(new Random(42));
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetRandom_ReturnsKnownEncoding()
    {
        var random = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            var result = Encodings.GetRandom(random);
            Assert.Contains(result, ValidEncodings);
        }
    }

    [Fact]
    public void GetRandom_IsDeterministicForSameSeed()
    {
        var r1 = Encodings.GetRandom(new Random(42));
        var r2 = Encodings.GetRandom(new Random(42));
        Assert.Equal(r1, r2);
    }
}
