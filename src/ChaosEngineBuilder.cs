namespace Zipper;

/// <summary>
/// Factory for constructing a <see cref="ChaosEngine"/> from a request's chaos configuration.
/// A fresh instance must be created per format because <see cref="ChaosEngine"/> is stateful.
/// </summary>
internal static class ChaosEngineBuilder
{
    private static readonly HashSet<string> DateColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "datetime", "legacydatesent", "legacydatecreated", "emailsentdate", "syntheticemailsentdate",
    };

    /// <summary>
    /// Builds a <see cref="ChaosEngine"/> for the specified request and format,
    /// or returns <c>null</c> when chaos is disabled.
    /// </summary>
    /// <param name="request">The file generation request.</param>
    /// <param name="totalLines">Total line count used by the engine (including header if applicable).</param>
    /// <param name="format">The load file format being written.</param>
    /// <returns>A configured <see cref="ChaosEngine"/>, or <c>null</c> when chaos is not enabled.</returns>
    internal static ChaosEngine? Build(FileGenerationRequest request, long totalLines, LoadFileFormat format)
    {
        if (!request.Chaos.ChaosMode)
        {
            return null;
        }

        if (ChaosScenarios.GeneratesAdversarialValues(request.Chaos.ChaosScenario)
            || ChaosScenarios.IsCrossFormat(request.Chaos.ChaosScenario))
        {
            return null;
        }

        string? resolvedTypes = request.Chaos.ChaosTypes;
        string? resolvedAmount = request.Chaos.ChaosAmount;

        if (!string.IsNullOrEmpty(request.Chaos.ChaosScenario))
        {
            var scenario = ChaosScenarios.GetByName(request.Chaos.ChaosScenario);
            if (scenario != null)
            {
                resolvedTypes = string.Join(",", ChaosScenarios.ResolveTypes(scenario, format));
                if (string.IsNullOrEmpty(resolvedAmount))
                {
                    resolvedAmount = scenario.DefaultAmount;
                }
            }
        }

        var eolString = LoadFiles.LoadFileEmitter.GetEolString(request.Delimiters.EndOfLine);
        string chaosColDelim = format == LoadFileFormat.Opt ? "," : request.Delimiters.ColumnDelimiter ?? "\u0014";
        string chaosQuoteDelim = format == LoadFileFormat.Opt ? string.Empty : request.Delimiters.QuoteDelimiter ?? "\u00fe";

        return new ChaosEngine(
            totalLines,
            resolvedAmount,
            resolvedTypes,
            format,
            chaosColDelim,
            chaosQuoteDelim,
            eolString,
            request.Metadata.Seed,
            GetDateColumnNames(request),
            format == LoadFileFormat.Dat ? request.SourcePaths : null,
            GetDateColumnIndexes(request));
    }

    /// <summary>
    /// Builds a <see cref="ChaosEngine"/> that samples the eml work items of a Standard run,
    /// or returns <c>null</c> when chaos is disabled or no work item is an eml.
    /// </summary>
    internal static ChaosEngine? BuildForStandardEmail(FileGenerationRequest request)
    {
        if (!request.Chaos.ChaosMode)
        {
            return null;
        }

        var output = request.Output;
        var sourceRecords = request.SourceRecords;
        if (sourceRecords is null && output.FileTypePlan is null)
        {
            // One File Type for every work item: sample all of them, or none.
            return IsEml(output.FileTypeLower)
                ? BuildForEmail(request, output.FileCount, itemIndexes: null)
                : null;
        }

        // Source Records carry their own File Type; an empty one falls back to the request-level type.
        string FileTypeAt(long index) => sourceRecords is null
            ? output.ResolveFileType(index)
            : string.IsNullOrEmpty(sourceRecords[(int)(index - 1)].FileType)
                ? output.FileTypeLower
                : sourceRecords[(int)(index - 1)].FileType;

        var indexes = new List<long>();
        for (long index = 1; index <= output.FileCount; index++)
        {
            if (IsEml(FileTypeAt(index)))
            {
                indexes.Add(index);
            }
        }

        return BuildForEmail(request, indexes.Count, indexes);
    }

    /// <summary>
    /// Builds a <see cref="ChaosEngine"/> that samples the eml work items of a Production Set,
    /// or returns <c>null</c> when chaos is disabled or no work item is an eml.
    /// </summary>
    internal static ChaosEngine? BuildForProductionEmail(FileGenerationRequest request, IReadOnlyList<ProductionNativeFilePlan> plans)
    {
        if (!request.Chaos.ChaosMode)
        {
            return null;
        }

        // Production plans are 0-based; work item indexes are 1-based.
        var indexes = plans.Where(plan => IsEml(plan.FileType)).Select(plan => plan.Index + 1).ToList();
        return BuildForEmail(request, indexes.Count, indexes.Count == plans.Count ? null : indexes);
    }

    private static ChaosEngine? BuildForEmail(FileGenerationRequest request, long totalItems, IReadOnlyList<long>? itemIndexes)
    {
        if (totalItems <= 0)
        {
            return null;
        }

        if (ChaosScenarios.GeneratesAdversarialValues(request.Chaos.ChaosScenario)
            || ChaosScenarios.IsCrossFormat(request.Chaos.ChaosScenario))
        {
            return null;
        }

        return new ChaosEngine(
            totalItems,
            request.Chaos.ChaosAmount,
            request.Chaos.ChaosTypes,
            request.Metadata.Seed,
            itemIndexes,
            request.Bates is null ? null : BatesSequence.FromConfig(request.Bates));
    }

    private static bool IsEml(string? fileType) =>
        string.Equals(fileType, "eml", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string> GetDateColumnNames(FileGenerationRequest request)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.Metadata.ColumnProfile is not null)
        {
            return columns;
        }

        if (request.LoadfileOnly && request.SourceRecords is null)
        {
            columns.Add("Date Sent");
            columns.Add("EmailSentDate");
        }
        else
        {
            if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
            {
                columns.Add("Date Sent");
            }

            if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
            {
                columns.Add("Sent Date");
                columns.Add("EmailSentDate");
            }

            if (request.Metadata.ShouldIncludeCollectionMetadataColumns())
            {
                columns.Add("Collection Date");
            }
        }

        return columns;
    }

    private static IReadOnlySet<int>? GetDateColumnIndexes(FileGenerationRequest request)
    {
        if (request.Metadata.ColumnProfile is not { } profile)
        {
            return null;
        }

        return profile.Columns
            .Select((column, index) => (Column: column, Index: index))
            .Where(column => DateColumnTypes.Contains(column.Column.Type))
            .Select(column => column.Index)
            .ToHashSet();
    }
}
