namespace Zipper.Profiles.Generation;

internal sealed class BooleanGenerator : IColumnValueGenerator
{
    private readonly int truePct;
    private readonly string? format;

    public BooleanGenerator(ColumnDefinition col)
    {
        this.truePct = col.TruePercentage ?? 50;
        this.format = col.Format;
    }

    public string Generate(ColumnGenerationContext context)
    {
        var value = context.Seeded.Next(100) < this.truePct;
        return this.format?.ToLowerInvariant() switch
        {
            "yn" => value ? "Y" : "N",
            "truefalse" => value ? "True" : "False",
            "10" => value ? "1" : "0",
            _ => value ? "Y" : "N",
        };
    }
}
