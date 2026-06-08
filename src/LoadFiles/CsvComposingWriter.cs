namespace Zipper.LoadFiles;

/// <summary>
/// Drives the CSV format through the record seam (<see cref="CsvComposer"/> +
/// <see cref="CsvSerializer"/> + <see cref="LoadFileEmitter"/>), replacing the fat
/// <c>CsvWriter</c>. CSV is standard-only, never applies chaos, and uses the platform newline.
/// </summary>
internal sealed class CsvComposingWriter : ILoadFileWriter
{
    public string FormatName => "CSV";

    public string FileExtension => ".csv";

    public async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var composer = new CsvComposer(request);
        var serializer = new CsvSerializer();
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        await LoadFileEmitter.EmitAsync(
            stream, serializer, composer.HeaderColumns, composer.Compose(processedFiles), encoding, Environment.NewLine, chaosEngine: null);
    }
}
