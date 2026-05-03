using Xunit;
using Zipper.Profiles;

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
    public void FirstNamesLower_AllMatchUpperCaseCounterpart()
    {
        for (int i = 0; i < Names.FirstNames.Length; i++)
        {
            Assert.Equal(Names.FirstNames[i].ToLowerInvariant(), Names.FirstNamesLower[i]);
        }
    }

    [Fact]
    public void LastNamesLower_AllMatchUpperCaseCounterpart()
    {
        for (int i = 0; i < Names.LastNames.Length; i++)
        {
            Assert.Equal(Names.LastNames[i].ToLowerInvariant(), Names.LastNamesLower[i]);
        }
    }
}
