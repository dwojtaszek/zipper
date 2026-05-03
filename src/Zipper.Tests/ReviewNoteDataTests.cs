using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

public class ReviewNoteDataTests
{
    [Fact]
    public void GetRandomNote_ReturnsNonEmptyString()
    {
        var result = ReviewNotes.GetRandomNote(new Random(42));
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetRandomNote_IsDeterministicForSameSeed()
    {
        var r1 = ReviewNotes.GetRandomNote(new Random(42));
        var r2 = ReviewNotes.GetRandomNote(new Random(42));
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void GetRandomNote_EndsWithPeriod()
    {
        var result = ReviewNotes.GetRandomNote(new Random(42));
        Assert.EndsWith(".", result);
    }
}
