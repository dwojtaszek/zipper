namespace Zipper.LoadFiles;

/// <summary>
/// Single owner of Load File format dispatch for Standard and Production Set modes: for
/// every requested format it creates the writer, opens the output stream, writes the Load
/// File, then emits the Audit File. ZipArchiveSink (entries inside the ZIP or next to it)
/// and ProductionSetGenerator (files on disk) both delegate here so "which formats to write
/// and how to invoke writers" has exactly one code path, preserving the
/// composer → serializer → emitter seam behind ILoadFileWriter. Loadfile-Only Mode keeps
/// its own loop because it applies chaos per format.
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
        CancellationToken cancellationToken = default)
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

            var auditJson = LoadFileAuditWriter.GenerateAuditJson(loadFileTarget, request, processedFiles, null, format);
            var auditStream = openTarget(auditTarget);
            await using (auditStream.ConfigureAwait(false))
            {
                using var auditWriter = new System.IO.StreamWriter(auditStream);
                await auditWriter.WriteAsync(auditJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            lastLoadFileTarget = loadFileTarget;
        }

        return lastLoadFileTarget;
    }
}
