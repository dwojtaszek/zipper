namespace Zipper.Config;

/// <summary>
/// Exact per-index File Type assignment for a mixed-type Generation Request.
/// Counts are allocated proportionally by weight using the largest-remainder method,
/// so the per-type totals always sum exactly to the requested File Count (never over-
/// or underproduces). File Types are assigned to contiguous 1-based index ranges in
/// declared order, making the plan deterministic and reproducible under any Seed.
/// </summary>
public sealed class FileTypePlan
{
    private readonly IReadOnlyList<FileTypeRatio> ratios;
    private readonly IReadOnlyList<string> types;
    private readonly long[] cumulativeCounts;
    private readonly long fileCount;

    public FileTypePlan(IReadOnlyList<FileTypeRatio> ratios, long fileCount)
    {
        ArgumentNullException.ThrowIfNull(ratios);
        if (ratios.Count == 0)
        {
            throw new ArgumentException("File type mix requires at least one entry.", nameof(ratios));
        }

        if (fileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileCount), "File count must be positive.");
        }

        this.ratios = ratios;
        this.types = ratios.Select(r => r.Type).ToArray();
        this.fileCount = fileCount;
        this.cumulativeCounts = Allocate(ratios, fileCount);
    }

    /// <summary>Gets the File Types in declared order.</summary>
    public IReadOnlyList<string> Types => this.types;

    /// <summary>Returns the File Type assigned to the given 1-based file index.</summary>
    public string GetFileType(long index)
    {
        if (index < 1 || index > this.fileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 1 and {this.fileCount}.");
        }

        for (int i = 0; i < this.cumulativeCounts.Length; i++)
        {
            if (index <= this.cumulativeCounts[i])
            {
                return this.ratios[i].Type;
            }
        }

        throw new System.Diagnostics.UnreachableException("Cumulative counts must cover every in-range index.");
    }

    /// <summary>Returns the exact number of files allocated to the given File Type.</summary>
    public long GetTypeCount(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        for (int i = 0; i < this.ratios.Count; i++)
        {
            if (string.Equals(this.ratios[i].Type, type, StringComparison.OrdinalIgnoreCase))
            {
                var previous = i > 0 ? this.cumulativeCounts[i - 1] : 0;
                return this.cumulativeCounts[i] - previous;
            }
        }

        return 0;
    }

    /// <summary>Returns whether the given File Type participates in the mix.</summary>
    public bool ContainsType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return this.ratios.Any(r => string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase));
    }

    private static long[] Allocate(IReadOnlyList<FileTypeRatio> ratios, long fileCount)
    {
        long totalWeight = 0;
        foreach (var ratio in ratios)
        {
            totalWeight += ratio.Weight;
        }

        var counts = new long[ratios.Count];
        var fractionalRemainders = new long[ratios.Count];
        long allocated = 0;
        for (int i = 0; i < ratios.Count; i++)
        {
            long scaled = fileCount * ratios[i].Weight;
            counts[i] = scaled / totalWeight;
            fractionalRemainders[i] = scaled % totalWeight;
            allocated += counts[i];
        }

        // Distribute the leftover files one per type, largest fractional remainder first;
        // ties go to the earlier declared type for determinism.
        long remainder = fileCount - allocated;
        foreach (var index in fractionalRemainders
            .Select((fraction, index) => (fraction, index))
            .OrderByDescending(x => x.fraction)
            .ThenBy(x => x.index)
            .Take((int)remainder))
        {
            counts[index.index]++;
        }

        var cumulative = new long[ratios.Count];
        long running = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            running += counts[i];
            cumulative[i] = running;
        }

        return cumulative;
    }
}
