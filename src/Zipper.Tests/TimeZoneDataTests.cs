using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

public class TimeZoneDataTests
{
    private static readonly System.Collections.Generic.HashSet<string> ValidTimeZones =
        new() { "UTC", "America/New_York", "America/Los_Angeles", "America/Chicago", "Europe/London", "Europe/Paris", "Asia/Tokyo", "Asia/Shanghai" };

    [Fact]
    public void GetRandom_ReturnsNonEmptyString()
    {
        var result = TimeZones.GetRandom(new Random(42));
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetRandom_ReturnsKnownTimeZone()
    {
        var random = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            var result = TimeZones.GetRandom(random);
            Assert.Contains(result, ValidTimeZones);
        }
    }

    [Fact]
    public void GetRandom_IsDeterministicForSameSeed()
    {
        var r1 = TimeZones.GetRandom(new Random(42));
        var r2 = TimeZones.GetRandom(new Random(42));
        Assert.Equal(r1, r2);
    }
}
