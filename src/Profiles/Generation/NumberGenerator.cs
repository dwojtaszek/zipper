namespace Zipper.Profiles.Generation;

/// <summary>
/// Generates numeric string values based on the column definition.
/// Supports ranges and distributions (uniform, exponential, gaussian).
/// </summary>
internal sealed class NumberGenerator : IColumnValueGenerator
{
    private readonly string colName;
    private readonly int min;
    private readonly int max;
    private readonly string? distribution;

    /// <summary>
    /// Initializes a new instance of the <see cref="NumberGenerator"/> class.
    /// </summary>
    /// <param name="col">The column definition containing the range and distribution settings.</param>
    public NumberGenerator(ColumnDefinition col)
    {
        this.colName = col.Name;
        this.min = col.Range?.Min ?? 0;
        this.max = col.Range?.Max ?? 1000;
        this.distribution = col.Distribution;
    }

    /// <summary>
    /// Generates a numeric value according to the configured range and distribution.
    /// </summary>
    /// <param name="context">The generation context containing random seeds and row state.</param>
    /// <returns>A string representation of the generated number.</returns>
    public string Generate(ColumnGenerationContext context)
    {
        if (this.colName.Equals("FILESIZE", StringComparison.OrdinalIgnoreCase))
        {
            return (context.FileData?.DataLength ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (this.colName.Equals("PAGECOUNT", StringComparison.OrdinalIgnoreCase))
        {
            return (context.FileData?.PageCount ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (this.max < this.min)
        {
            return this.min.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (this.max == this.min)
        {
            return this.min.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (string.Equals(this.distribution, "exponential", StringComparison.Ordinal))
        {
            var lambda = 3.0 / (this.max - this.min);
            var value = this.min + (int)(-Math.Log(1 - context.Seeded.NextDouble()) / lambda);
            return Math.Min(value, this.max).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (string.Equals(this.distribution, "gaussian", StringComparison.Ordinal) || string.Equals(this.distribution, "normal", StringComparison.Ordinal))
        {
            double u1 = 1.0 - context.Seeded.NextDouble();
            double u2 = 1.0 - context.Seeded.NextDouble();
            double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            double mean = this.min + (this.max - this.min) / 2.0;
            double stdDev = (this.max - this.min) / 6.0; // 99.7% of values within [min, max]

            int value = (int)Math.Round(mean + z0 * stdDev);
            value = Math.Max(this.min, Math.Min(this.max, value));
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (this.max == int.MaxValue)
        {
            return (this.min + (long)(context.Seeded.NextDouble() * ((long)this.max - this.min + 1))).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return context.Seeded.Next(this.min, this.max + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
