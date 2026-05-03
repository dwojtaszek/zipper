namespace Zipper.Profiles;

/// <summary>
/// Encoding names.
/// </summary>
internal static class Encodings
{
    private static readonly string[] Values = { "UTF-8", "ASCII", "UTF-16", "ISO-8859-1", "Windows-1252" };

    /// <summary>
    /// Gets a random encoding name.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Encoding name.</returns>
    public static string GetRandom(Random random) => Values[random.Next(Values.Length)];
}
