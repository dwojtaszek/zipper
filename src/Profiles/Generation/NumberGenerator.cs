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

        if (this.distribution == "gaussian" || this.distribution == "normal")
        {
            double u1 = 1.0 - context.Seeded.NextDouble();
            double u2 = 1.0 - context.Seeded.NextDouble();
            double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            double mean = this.min + (this.max - this.min) / 2.0;
            double stdDev = (this.max - this.min) / 6.0; // 99.7% of values within [min, max]

            int value = (int)Math.Round(mean + z0 * stdDev);
            value = Math.Max(this.min, Math.Min(this.max, value));
            return value.ToString();
        }

        if (this.max == int.MaxValue)
        {
            return (this.min + (long)(context.Seeded.NextDouble() * ((long)this.max - this.min + 1))).ToString();
        }

        return context.Seeded.Next(this.min, this.max + 1).ToString();
    }
}
