using Zipper.Profiles;

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

    public DatComposer(FileGenerationRequest request, WriterMode mode)
    {
        this.mode = mode;
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
            this.standardComposer = new DatStandardComposer(
                request, this.batesSequence, this.headerColumns);
        }
    }

    public IReadOnlyList<string> HeaderColumns => this.headerColumns;

    public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles)
        => this.mode switch
        {
            WriterMode.LoadfileOnly => this.profileGenerator is not null
                ? this.standardComposer!.ComposeProfile()
                : this.loadfileOnlyComposer!.Compose(),
            WriterMode.ProductionSet => this.productionComposer!.Compose(processedFiles),
            _ => this.standardComposer!.ComposeStandard(processedFiles),
        };
}
