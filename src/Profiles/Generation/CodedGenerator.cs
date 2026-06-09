namespace Zipper.Profiles.Generation;

internal sealed class CodedGenerator : IColumnValueGenerator
{
    private readonly string[] values;
    private readonly int[]? distributionIndices;
    private readonly bool multiValue;
    private readonly int multiValueMin;
    private readonly int multiValueMax;
    private readonly string multiValueDelimiter;

    public CodedGenerator(string[] values, int[]? distributionIndices, ColumnDefinition col, ProfileSettings settings)
    {
        this.values = values;
        this.distributionIndices = distributionIndices;
        this.multiValue = col.MultiValue;
        this.multiValueMin = col.MultiValueCount?.Min ?? 1;
        this.multiValueMax = col.MultiValueCount?.Max ?? 3;
        this.multiValueDelimiter = settings.MultiValueDelimiter;
    }

    public string Generate(ColumnGenerationContext context)
    {
        if (this.values.Length == 0)
        {
            return string.Empty;
        }

        if (this.multiValue)
        {
            var count = context.Seeded.Next(this.multiValueMin, this.multiValueMax + 1);
            if (count == 0)
            {
                return string.Empty;
            }

            var selected = new HashSet<string>(StringComparer.Ordinal);
            selected.Add(this.PickValue(context));
            while (selected.Count < count && selected.Count < this.values.Length)
            {
                selected.Add(this.values[context.Seeded.Next(this.values.Length)]);
            }

            return string.Join(this.multiValueDelimiter, selected);
        }

        return this.PickValue(context);
    }

    private string PickValue(ColumnGenerationContext context)
    {
        if (this.distributionIndices != null)
        {
            var idx = this.distributionIndices[context.DocumentIndex % this.distributionIndices.Length];
            return this.values[idx % this.values.Length];
        }

        return this.values[context.Seeded.Next(this.values.Length)];
    }
}
