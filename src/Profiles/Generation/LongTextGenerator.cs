namespace Zipper.Profiles.Generation;

internal sealed class LongTextGenerator : IColumnValueGenerator
{
    private readonly string? generatorName;
    private readonly int minParagraphs;
    private readonly int maxParagraphs;

    public LongTextGenerator(ColumnDefinition col)
    {
        this.generatorName = col.Generator;
        this.minParagraphs = 1;
        this.maxParagraphs = 3;
        if (col.GeneratorParams != null)
        {
            if (col.GeneratorParams.TryGetValue("min", out var minObj))
            {
                this.minParagraphs = Convert.ToInt32(minObj, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (col.GeneratorParams.TryGetValue("max", out var maxObj))
            {
                this.maxParagraphs = Convert.ToInt32(maxObj, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    public string Generate(ColumnGenerationContext context)
    {
        if (string.Equals(this.generatorName, "reviewNote", StringComparison.Ordinal))
        {
            return ReviewNotes.GetRandomNote(context.Seeded);
        }

        var paragraphCount = context.Seeded.Next(this.minParagraphs, this.maxParagraphs + 1);
        return string.Join(" ", Enumerable.Range(0, paragraphCount).Select(_ => LoremIpsum.GetParagraph(context.Seeded)));
    }
}
