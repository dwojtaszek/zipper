using System.Text;

namespace Zipper.Profiles;

/// <summary>
/// Lorem ipsum text generator.
/// </summary>
internal static class LoremIpsum
{
    private static readonly string[] Words =
    {
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
        "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
        "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
        "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
        "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
        "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
        "deserunt", "mollit", "anim", "id", "est", "laborum",
    };

    /// <summary>
    /// Gets a paragraph of Lorem ipsum text.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>A paragraph.</returns>
    public static string GetParagraph(Random random)
    {
        var sentenceCount = random.Next(3, 7);
        var sb = new StringBuilder();

        for (int i = 0; i < sentenceCount; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            AppendSentence(sb, random);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets a sentence of Lorem ipsum text.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>A sentence.</returns>
    public static string GetSentence(Random random)
    {
        var sb = new StringBuilder();
        AppendSentence(sb, random);
        return sb.ToString();
    }

    private static void AppendSentence(StringBuilder sb, Random random)
    {
        var wordCount = random.Next(8, 20);
        for (int i = 0; i < wordCount; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            var word = Words[random.Next(Words.Length)];
            if (i == 0)
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                {
                    sb.Append(word, 1, word.Length - 1);
                }
            }
            else
            {
                sb.Append(word);
            }
        }

        sb.Append('.');
    }
}
