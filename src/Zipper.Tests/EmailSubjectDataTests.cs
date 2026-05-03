using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

public class EmailSubjectDataTests
{
    [Fact]
    public void GetRandom_ReturnsNonEmptyString()
    {
        var result = EmailSubjects.GetRandom(new Random(42));
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetRandom_IsDeterministicForSameSeed()
    {
        var r1 = EmailSubjects.GetRandom(new Random(42));
        var r2 = EmailSubjects.GetRandom(new Random(42));
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void GetRandom_ReturnsKnownSubjectContent()
    {
        var random = new Random(42);
        var results = Enumerable.Range(0, 50).Select(_ => EmailSubjects.GetRandom(random)).ToList();
        Assert.Contains(results, s => s.Length > 0);
        Assert.All(results, s => Assert.NotEmpty(s));
    }
}
