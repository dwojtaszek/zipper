namespace Zipper.Utils;

internal static class SourceMetadataKeyNormalizer
{
    internal static string Normalize(string key)
    {
        var builder = new System.Text.StringBuilder(key.Length);
        foreach (var character in key)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
