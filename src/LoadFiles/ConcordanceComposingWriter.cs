namespace Zipper.LoadFiles;

/// <summary>
/// Drives the Concordance DAT format through the record seam (<see cref="ConcordanceComposer"/>
/// + <see cref="ConcordanceSerializer"/> + <see cref="LoadFileEmitter"/>), replacing the fat
/// <c>ConcordanceWriter</c>. Concordance is standard-only, never applies chaos, and uses the
/// platform newline.
/// </summary>
internal sealed class ConcordanceComposingWriter : ILoadFileWriter
{
    public string FormatName => "CONCORDANCE";

    public string FileExtension => ".dat";

    public async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var composer = new ConcordanceComposer(request);
        var serializer = new ConcordanceSerializer(request);
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        await LoadFileEmitter.EmitAsync(
            stream, serializer, composer.HeaderColumns, composer.Compose(processedFiles), encoding, Environment.NewLine, chaosEngine: null);
    }
}
