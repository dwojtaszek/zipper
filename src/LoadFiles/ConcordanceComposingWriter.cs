namespace Zipper.LoadFiles;

/// <summary>
/// Drives the Concordance DAT format through the record seam (<see cref="ConcordanceComposer"/>
/// + <see cref="ConcordanceSerializer"/> + <see cref="LoadFileEmitter"/>), replacing the fat
/// <c>ConcordanceWriter</c>. Standard mode uses the platform newline; Loadfile-Only Mode honours
/// the configured EOL and chaos.
/// </summary>
internal sealed class ConcordanceComposingWriter : ILoadFileWriter
{
    public string FormatName => "CONCORDANCE";

    public string FileExtension => ".dat";

    public async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null,
        CancellationToken cancellationToken = default)
    {
        var composer = new ConcordanceComposer(request);
        var serializer = new ConcordanceSerializer(request);
        var mode = request.LoadfileOnly ? WriterMode.LoadfileOnly : WriterMode.Standard;
        var policy = new TextOutputPolicy(request, LoadFileFormat.Concordance, mode, hasChaos: chaosEngine is not null);

        await LoadFileEmitter.EmitAsync(
            stream, serializer, composer.HeaderColumns, composer.Compose(processedFiles), policy.Encoding, policy.EndOfLine, chaosEngine, cancellationToken).ConfigureAwait(false);
    }
}
