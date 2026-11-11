using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Composes and serializes DAT and OPT line buffers before applying coordinated anomalies.
/// Encoding and Line Ending belong to the Emitter, after this string transformation.
/// </summary>
internal static class CrossFormatLoadFileRenderer
{
    public static CrossFormatAnomalyResult RenderAndApply(
        FileGenerationRequest datRequest,
        FileGenerationRequest optRequest,
        IReadOnlyList<FileData> records,
        WriterMode writerMode,
        string datFileName,
        string optFileName,
        CancellationToken cancellationToken)
    {
        var datComposer = new DatComposer(datRequest, writerMode);
        var optComposer = new OptComposer(optRequest, writerMode);
        if (writerMode == WriterMode.Standard && !optRequest.LoadfileOnly)
        {
            OptComposingWriter.WarnUnsupportedStandardColumns(optRequest);
        }

        var datLines = RenderLines(datComposer, new DatSerializer(datRequest), records, new TextOutputPolicy(datRequest, LoadFileFormat.Dat, writerMode, hasChaos: true), cancellationToken, datComposer.TrailingEmptyRows);
        var optLines = RenderLines(optComposer, new OptSerializer(), records, new TextOutputPolicy(optRequest, LoadFileFormat.Opt, writerMode, hasChaos: true), cancellationToken);

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

    internal static List<string> RenderLines(
        ILoadFileComposer composer,
        ILoadFileSerializer serializer,
        IReadOnlyList<FileData> records,
        TextOutputPolicy policy,
        CancellationToken cancellationToken,
        int trailingEmptyRows = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lines = new List<string>();
        if (composer.HeaderColumns.Count > 0)
        {
            AddLine(serializer.RenderHeader(composer.HeaderColumns));
        }

        foreach (var record in composer.Compose(records))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddLine(serializer.RenderRecord(record));
        }

        for (int i = 0; i < trailingEmptyRows; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add(string.Empty);
        }

        return lines;

        void AddLine(string line)
        {
            line = policy.NormalizeForAnomalies(line);
            // The legacy stream split physical lines before applying coordinated anomalies.
            if (line.Contains(policy.EndOfLine, StringComparison.Ordinal))
            {
                lines.AddRange(line.Split(policy.EndOfLine, StringSplitOptions.None));
            }
            else
            {
                lines.Add(line);
            }
        }
    }
}
