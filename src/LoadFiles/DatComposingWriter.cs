namespace Zipper.LoadFiles;

/// <summary>
/// Drives the DAT format through the record seam: <see cref="DatComposer"/> decides columns
/// and raw values, <see cref="DatSerializer"/> renders each record, and
/// <see cref="LoadFileEmitter"/> writes the lines (preamble, EOL, batching, chaos).
/// Replaces the fat <c>DatWriter</c> and <c>ProfileDrivenDatWriter</c>.
/// </summary>
internal sealed class DatComposingWriter : ILoadFileWriter
{
    private readonly WriterMode mode;

    internal DatComposingWriter(WriterMode mode = WriterMode.Standard)
    {
        this.mode = mode;
    }

    public string FormatName => this.mode switch
    {
        WriterMode.LoadfileOnly => "DAT (Metadata)",
        WriterMode.ProductionSet => "Production Set DAT",
        _ => "DAT",
    };

    public string FileExtension => ".dat";

    public async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null,
        CancellationToken cancellationToken = default)
    {
        var composer = new DatComposer(request, this.mode);
        var serializer = new DatSerializer(request);
        var policy = new TextOutputPolicy(request, LoadFileFormat.Dat, this.mode, chaosEngine != null);

        var records = composer.Compose(processedFiles);
        await LoadFileEmitter.EmitAsync(stream, serializer, composer.HeaderColumns, records, policy.Encoding, policy.EndOfLine, chaosEngine, cancellationToken).ConfigureAwait(false);
    }
}
