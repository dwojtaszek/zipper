namespace Zipper.Profiles.Generation;

/// <summary>
/// Generates coded metadata values from a configured data source pool.
/// In multi-value mode, distinct values are selected up to the requested count.
/// When the requested count exceeds the number of distinct available values in the pool,
/// the output cardinality is capped at the number of distinct available values.
/// </summary>
internal sealed class CodedGenerator : IColumnValueGenerator
{
    private readonly string[] values;
    private readonly string[] distinctValues;
    private readonly int[]? distributionIndices;
    private readonly bool multiValue;
    private readonly int multiValueMin;
    private readonly int multiValueMax;
    private readonly string multiValueDelimiter;

    public CodedGenerator(string[] values, int[]? distributionIndices, ColumnDefinition col, ProfileSettings settings)
    {
        this.values = values;
        this.distinctValues = values.Distinct(StringComparer.Ordinal).ToArray();
        this.distributionIndices = distributionIndices;
        this.multiValue = col.MultiValue;
        this.multiValueMin = col.MultiValueCount?.Min ?? 1;
        this.multiValueMax = col.MultiValueCount?.Max ?? 3;
        this.multiValueDelimiter = settings.MultiValueDelimiter;
    }

    public string Generate(ColumnGenerationContext context)
    {
        if (this.values.Length == 0 || this.distinctValues.Length == 0)
        {
            return string.Empty;
        }

        if (this.multiValue)
        {
            var count = context.Seeded.Next(this.multiValueMin, this.multiValueMax + 1);
            if (count <= 0)
            {
                return string.Empty;
            }

            var targetCount = Math.Min(count, this.distinctValues.Length);
            var first = this.PickValue(context);
            if (targetCount == 1)
            {
                return first;
            }

            var selected = new string[targetCount];
            selected[0] = first;

            var firstIdx = Array.IndexOf(this.distinctValues, first);
            int remainingCandidatesCount = this.distinctValues.Length - 1;
            int needed = targetCount - 1;

            var sparseMap = new Dictionary<int, int>(needed);
            for (int i = 0; i < needed; i++)
            {
                int r = context.Seeded.Next(i, remainingCandidatesCount);
                int valR = sparseMap.TryGetValue(r, out int vr) ? vr : r;
                int valI = sparseMap.TryGetValue(i, out int vi) ? vi : i;
                sparseMap[r] = valI;
                sparseMap[i] = valR;
                int chosenVirtual = valR;
                int realIdx = chosenVirtual < firstIdx ? chosenVirtual : chosenVirtual + 1;
                selected[i + 1] = this.distinctValues[realIdx];
            }

            return string.Join(this.multiValueDelimiter, selected);
        }

        return this.PickValue(context);
    }

    private string PickValue(ColumnGenerationContext context)
    {
        if (this.distributionIndices is not null)
        {
            var idx = this.distributionIndices[context.DocumentIndex % this.distributionIndices.Length];
            return this.values[idx % this.values.Length];
        }

        return this.values[context.Seeded.Next(this.values.Length)];
    }
}
