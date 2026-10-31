using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Renders the DAT and OPT Load Files to line buffers and applies a coordinated
/// cross-format Chaos Scenario across both before they are written to disk.
/// </summary>
internal static class CrossFormatLoadFileRenderer
{
    public static async Task<CrossFormatAnomalyResult> RenderAndApplyAsync(
        FileGenerationRequest datRequest,
        FileGenerationRequest optRequest,
        IReadOnlyList<FileData> records,
        Func<LoadFileFormat, WriterMode, ILoadFileWriter> createWriter,
        WriterMode writerMode,
        string datFileName,
        string optFileName,
        Encoding datEncoding,
        Encoding optEncoding,
        CancellationToken cancellationToken)
    {
        var datLines = await RenderLinesAsync(datRequest, LoadFileFormat.Dat, records, createWriter, writerMode, datEncoding, cancellationToken).ConfigureAwait(false);
        var optLines = await RenderLinesAsync(optRequest, LoadFileFormat.Opt, records, createWriter, writerMode, optEncoding, cancellationToken).ConfigureAwait(false);

        return ChaosEngine.ApplyCrossFormat(
            datLines,
            optLines,
            datRequest.Chaos.ChaosScenario,
            datRequest.Chaos.ChaosAmount,
            datRequest.Metadata.Seed,
            datRequest.Delimiters.ColumnDelimiter ?? "\u0014",
            datRequest.Delimiters.QuoteDelimiter ?? "\u00fe",
            datFileName,
            optFileName,
            datRequest.Delimiters.NewlineDelimiter);
    }

    private static async Task<List<string>> RenderLinesAsync(
        FileGenerationRequest request,
        LoadFileFormat format,
        IReadOnlyList<FileData> records,
        Func<LoadFileFormat, WriterMode, ILoadFileWriter> createWriter,
        WriterMode writerMode,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var writer = createWriter(format, writerMode);
        await writer.WriteAsync(stream, request, records, null, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;

        using var reader = new StreamReader(stream, encoding);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
