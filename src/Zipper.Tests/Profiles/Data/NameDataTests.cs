using Xunit;
using Zipper.Profiles.Data;

namespace Zipper.Tests;

public class NameDataTests
{
    [Fact]
    public void FirstNames_HasExpectedCount()
    {
        Assert.Equal(32, Names.FirstNames.Length);
    }

    [Fact]
    public void LastNames_HasExpectedCount()
    {
        Assert.Equal(31, Names.LastNames.Length);
    }

    [Fact]
    public void FirstNamesLower_SameLengthAsFirstNames()
    {
        Assert.Equal(Names.FirstNames.Length, Names.FirstNamesLower.Length);
    }

    [Fact]
    public void LastNamesLower_SameLengthAsLastNames()
    {
        Assert.Equal(Names.LastNames.Length, Names.LastNamesLower.Length);
    }

    [Fact]
    public void LowercaseNamePools_MatchUpperCaseCounterparts()
    {
        Assert.Equal(Names.FirstNames.Select(n => n.ToLowerInvariant()), Names.FirstNamesLower);
        Assert.Equal(Names.LastNames.Select(n => n.ToLowerInvariant()), Names.LastNamesLower);
    }
}
