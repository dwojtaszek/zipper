namespace Zipper.LoadFiles;

/// <summary>
/// Column authority for a load file format: decides the ordered header columns and yields
/// the records (raw column values) for a generation request. Composers hold no I/O, no
/// escaping, and no chaos — a serializer renders each record to a line and the
/// <see cref="LoadFileEmitter"/> puts those lines on the wire.
/// </summary>
internal interface ILoadFileComposer
{
    /// <summary>
    /// Gets the ordered header columns. Empty when the format has no header row (e.g. OPT).
    /// </summary>
    IReadOnlyList<string> HeaderColumns { get; }

    /// <summary>
    /// Yields the records lazily. Each record's <see cref="LoadFileRecord.Columns"/> aligns
    /// with <see cref="HeaderColumns"/> and its values are raw (unescaped).
    /// </summary>
    /// <param name="processedFiles">
    /// The files produced during generation. Ignored by synthetic modes (loadfile-only) that
    /// derive records from the requested file count instead of real file data.
    /// </param>
    IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles);
}
