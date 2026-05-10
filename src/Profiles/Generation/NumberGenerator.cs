namespace Zipper.Profiles.Generation;

internal sealed class NumberGenerator : IColumnValueGenerator
{
    private readonly string colName;
    private readonly int min;
    private readonly int max;
    private readonly string? distribution;

    public NumberGenerator(ColumnDefinition col)
    {
        this.colName = col.Name;
        this.min = col.Range?.Min ?? 0;
        this.max = col.Range?.Max ?? 1000;
        this.distribution = col.Distribution;
    }

    public string Generate(ColumnGenerationContext context)
    {
        if (this.colName.Equals("FILESIZE", StringComparison.OrdinalIgnoreCase))
        {
            return (context.FileData?.DataLength ?? 0).ToString();
        }

        if (this.colName.Equals("PAGECOUNT", StringComparison.OrdinalIgnoreCase))
        {
            return (context.FileData?.PageCount ?? 1).ToString();
        }

        if (this.max < this.min)
        {
            return this.min.ToString();
        }

        if (this.max == this.min)
        {
            return this.min.ToString();
        }

        if (this.distribution == "exponential")
        {
            var lambda = 3.0 / (this.max - this.min);
            var value = this.min + (int)(-Math.Log(1 - context.Seeded.NextDouble()) / lambda);
            return Math.Min(value, this.max).ToString();
        }

        if (this.max == int.MaxValue)
        {
            return (this.min + (long)(context.Seeded.NextDouble() * ((long)this.max - this.min + 1))).ToString();
        }

        return context.Seeded.Next(this.min, this.max + 1).ToString();
    }
}
