namespace Zipper.LoadFiles;

/// <summary>
/// Drives the CSV format through the record seam (<see cref="CsvComposer"/> +
/// <see cref="CsvSerializer"/> + <see cref="LoadFileEmitter"/>), replacing the fat
/// <c>CsvWriter</c>. Standard mode uses the platform newline; Loadfile-Only Mode honours the configured EOL and chaos.
/// </summary>
internal sealed class CsvComposingWriter : ILoadFileWriter
{
    public string FormatName => "CSV";

    public string FileExtension => ".csv";

    public async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null,
        CancellationToken cancellationToken = default)
    {
        var composer = new CsvComposer(request);
        var serializer = new CsvSerializer();
        var mode = request.LoadfileOnly ? WriterMode.LoadfileOnly : WriterMode.Standard;
        var policy = new TextOutputPolicy(request, LoadFileFormat.Csv, mode, hasChaos: chaosEngine is not null);

        await LoadFileEmitter.EmitAsync(
            stream, serializer, composer.HeaderColumns, composer.Compose(processedFiles), policy.Encoding, policy.EndOfLine, chaosEngine, cancellationToken).ConfigureAwait(false);
    }
}
