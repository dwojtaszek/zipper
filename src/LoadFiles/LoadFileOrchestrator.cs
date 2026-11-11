namespace Zipper.LoadFiles;

/// <summary>
/// Single owner of Load File format dispatch for Standard and Production Set modes: for
/// every requested format it creates the writer, opens the output stream, writes the Load
/// File, then emits the Audit File. ZipArchiveSink (entries inside the ZIP or next to it)
/// and ProductionSetGenerator (files on disk) both delegate here so "which formats to write
/// and how to invoke writers" has exactly one code path, preserving the
/// composer → serializer → emitter seam behind ILoadFileWriter. Loadfile-Only Mode keeps
/// its own loop for per-format chaos; coordinated DAT/OPT emission is shared here.
/// </summary>
internal static class LoadFileOrchestrator
{
    /// <summary>
    /// Emits each format in <paramref name="formats"/> plus its Audit File.
    /// </summary>
    /// <param name="request">File generation request parameters.</param>
    /// <param name="processedFiles">Generated file data consumed by the Load File writers.</param>
    /// <param name="formats">Load File formats to emit, in order.</param>
    /// <param name="mode">Writer mode (Standard or Production Set).</param>
    /// <param name="getTargets">Resolves the Load File and audit targets for a format (target naming is a caller quirk).</param>
    /// <param name="openTarget">Opens the output stream for a target (ZIP entry or disk file).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The target of the last emitted Load File.</returns>
    public static async Task<string> EmitAllAsync(
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        System.Collections.Generic.IReadOnlyList<LoadFileFormat> formats,
        WriterMode mode,
        Func<LoadFileFormat, ILoadFileWriter, (string LoadFileTarget, string AuditTarget)> getTargets,
        Func<string, System.IO.Stream> openTarget,
        CancellationToken cancellationToken = default,
        System.Collections.Generic.IReadOnlyList<ChaosAnomaly>? anomalies = null)
    {
        string lastLoadFileTarget = string.Empty;

        foreach (var format in formats)
        {
            var writer = LoadFileWriterFactory.CreateWriter(format, mode);
            var (loadFileTarget, auditTarget) = getTargets(format, writer);

            var loadFileStream = openTarget(loadFileTarget);
            await using (loadFileStream.ConfigureAwait(false))
            {
                await writer.WriteAsync(loadFileStream, request, processedFiles, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await LoadFileAuditWriter.EmitAsync(loadFileTarget, auditTarget, request, processedFiles,
                openTarget, anomalies, format, cancellationToken: cancellationToken).ConfigureAwait(false);

            lastLoadFileTarget = loadFileTarget;
        }

        return lastLoadFileTarget;
    }

    /// <summary>
    /// Emits coordinated DAT/OPT Load Files, Audit Files, and Chaos Expectation Sidecars.
    /// Callers retain target naming and partial-output cleanup ownership.
    /// </summary>
    internal static async Task<CrossFormatAnomalyResult> EmitCrossFormatAsync(
        FileGenerationRequest datRequest,
        FileGenerationRequest optRequest,
        IReadOnlyList<FileData> records,
        WriterMode mode,
        string datTarget,
        string optTarget,
        string datAuditTarget,
        string optAuditTarget,
        Func<string, Stream> openTarget,
        CancellationToken cancellationToken)
    {
        var result = CrossFormatLoadFileRenderer.RenderAndApply(
            datRequest, optRequest, records, mode,
            Path.GetFileName(datTarget), Path.GetFileName(optTarget), cancellationToken);

        await EmitRenderedAsync(datRequest, LoadFileFormat.Dat, result.DatLines, result.DatAnomalies,
            datTarget, datAuditTarget, result.DatLines.Count - 1L).ConfigureAwait(false);
        await EmitRenderedAsync(optRequest, LoadFileFormat.Opt, result.OptLines, result.OptAnomalies,
            optTarget, optAuditTarget, result.OptLines.Count).ConfigureAwait(false);
        return result;

        async Task EmitRenderedAsync(FileGenerationRequest request, LoadFileFormat format,
            IReadOnlyList<string> lines, IReadOnlyList<ChaosAnomaly> anomalies,
            string target, string auditTarget, long totalRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var policy = new TextOutputPolicy(request, format, mode, hasChaos: true);
            var stream = openTarget(target);
            await using (stream.ConfigureAwait(false))
            {
                await LoadFileEmitter.EmitRenderedLinesAsync(stream, lines, policy.Encoding, policy.EndOfLine, cancellationToken).ConfigureAwait(false);
            }

            await LoadFileAuditWriter.EmitAsync(target, auditTarget, request, records,
                openTarget, anomalies, format, totalRecords, cancellationToken).ConfigureAwait(false);
        }
    }
}
