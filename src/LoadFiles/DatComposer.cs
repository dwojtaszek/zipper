using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.LoadFiles;

/// <summary>
/// Column authority for the DAT (Concordance) format across all three writer modes
/// (Standard, Loadfile-Only, Production Set) plus the column-profile path. Produces the
/// ordered header columns and delegates row generation to the appropriate mode composer.
/// A <see cref="DatSerializer"/> renders each record to a line and the
/// <see cref="LoadFileEmitter"/> owns I/O, EOL, and chaos.
/// </summary>
/// <remarks>
/// Values are emitted raw (unescaped). The serializer applies quote-doubling and newline
/// sanitisation once per field; the historical writers escaped some fields and not others,
/// but those un-escaped fields never contained the quote or newline characters, so uniform
/// escaping is byte-identical.
/// </remarks>
internal sealed class DatComposer : ILoadFileComposer
{
    private readonly WriterMode mode;
    private readonly List<string> headerColumns;
    private readonly DataGenerator? profileGenerator;
    private readonly BatesSequence? batesSequence;
    private readonly DatProductionComposer? productionComposer;
    private readonly DatLoadfileOnlyComposer? loadfileOnlyComposer;
    private readonly DatStandardComposer? standardComposer;
    private readonly AdversarialValueTailPlan? adversarialValueTailPlan;
    private readonly int adversarialValueColumnIndex;

    public DatComposer(FileGenerationRequest request, WriterMode mode)
    {
        this.mode = mode;
        bool isAdversarialValuesScenario = request.Chaos.ChaosMode
            && ChaosScenarios.GeneratesAdversarialValues(request.Chaos.ChaosScenario);
        long recordCount = request.Output.FileCount;
        if (isAdversarialValuesScenario && mode == WriterMode.LoadfileOnly && request.SourceRecords is null)
        {
            recordCount += FamilyPlan.GetSimulatedChildCount(request);
        }
        this.adversarialValueTailPlan = isAdversarialValuesScenario
            ? AdversarialValueTailPlan.Create(request, checked((int)recordCount))
            : null;
        var namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;
        this.batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;

        if (mode == WriterMode.ProductionSet)
        {
            this.headerColumns = DatProductionComposer.BuildHeaders(request, namingConvention);
            this.productionComposer = new DatProductionComposer(
                request, this.batesSequence, this.headerColumns);
        }
        else if (request.Metadata.ColumnProfile is not null)
        {
            var profile = request.Metadata.ColumnProfile;
            this.profileGenerator = new DataGenerator(
                profile,
                request.Metadata.Seed,
                custodianCountOverride: request.Metadata.CustodianCountOverride,
                dateFormatOverride: request.Metadata.DateFormatOverride,
                emptyPercentageOverride: request.Metadata.EmptyPercentageOverride);
            var profileColumnNames = this.profileGenerator.GetColumnNames().ToList();
            this.headerColumns = profileColumnNames.Select(n => DatComposerShared.ApplyConvention(n, namingConvention)).ToList();
            this.standardComposer = new DatStandardComposer(
                request, this.batesSequence, this.headerColumns,
                this.profileGenerator, profileColumnNames);
        }
        else if (mode == WriterMode.LoadfileOnly)
        {
            this.headerColumns = DatLoadfileOnlyComposer.BuildHeaders(request, namingConvention);
            this.loadfileOnlyComposer = new DatLoadfileOnlyComposer(
                request, this.batesSequence, this.headerColumns);
        }
        else
        {
            this.headerColumns = DatStandardComposer.BuildHeaders(request, namingConvention);
            this.standardComposer = new DatStandardComposer(request, this.batesSequence, this.headerColumns);
        }

        this.adversarialValueColumnIndex = this.adversarialValueTailPlan is null
            ? -1
            : FindAdversarialValueColumnIndex(request, this.headerColumns, namingConvention);
    }

    public IReadOnlyList<string> HeaderColumns => this.headerColumns;

    internal int TrailingEmptyRows => this.adversarialValueTailPlan is null
        ? 0
        : AdversarialValueTailPlan.TrailingEmptyRowCount;

    public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles)
    {
        IEnumerable<LoadFileRecord> records = this.mode switch
        {
            WriterMode.LoadfileOnly => this.profileGenerator is not null
                ? this.standardComposer!.ComposeProfile()
                : this.loadfileOnlyComposer!.Compose(),
            WriterMode.ProductionSet => this.productionComposer!.Compose(processedFiles),
            _ => this.standardComposer!.ComposeStandard(processedFiles),
        };

        return this.adversarialValueTailPlan is null
            ? records
            : this.ApplyAdversarialValues(records);
    }

    private static int FindAdversarialValueColumnIndex(
        FileGenerationRequest request,
        IReadOnlyList<string> headerColumns,
        string? namingConvention)
    {
        string? columnName = request.Metadata.ColumnProfile is { } profile
            ? AdversarialValueTailPlan.FindColumn(profile.Columns)?.Name
            : "Author";
        if (columnName is null)
        {
            return -1;
        }

        string headerName = DatComposerShared.ApplyConvention(columnName, namingConvention);
        for (int index = 0; index < headerColumns.Count; index++)
        {
            if (string.Equals(headerColumns[index], headerName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private IEnumerable<LoadFileRecord> ApplyAdversarialValues(IEnumerable<LoadFileRecord> records)
    {
        if (this.adversarialValueColumnIndex < 0)
        {
            throw new InvalidOperationException("The adversarial-values scenario requires an eligible DAT value column.");
        }

        long recordIndex = 0;
        foreach (var record in records)
        {
            string? value = this.adversarialValueTailPlan!.GetValue(++recordIndex);
            if (value is null)
            {
                yield return record;
                continue;
            }

            var values = record.Values.ToArray();
            values[this.adversarialValueColumnIndex] = value;
            yield return LoadFileRecordBuilder.Build(record.Columns, values, record.RecordId);
        }
    }
}
