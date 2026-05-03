namespace Zipper.Profiles;

/// <summary>
/// Time zone names.
/// </summary>
internal static class TimeZones
{
    private static readonly string[] Values =
    {
        "UTC", "America/New_York", "America/Los_Angeles", "America/Chicago",
        "Europe/London", "Europe/Paris", "Asia/Tokyo", "Asia/Shanghai",
    };

    /// <summary>
    /// Gets a random time zone.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Time zone name.</returns>
    public static string GetRandom(Random random) => Values[random.Next(Values.Length)];
}
