namespace Zipper.LoadFiles;

/// <summary>
/// Renders <see cref="LoadFileRecord"/> instances to a specific load file format.
/// Pure rendering only: a serializer turns columns/values into an escaped, delimited
/// line. It owns no stream, no end-of-line, no encoding, and no chaos — those belong
/// to <see cref="LoadFileEmitter"/>. This separates "what data goes in a row" (composer)
/// from "how that row renders" (serializer) from "how it reaches the stream" (emitter).
/// </summary>
internal interface ILoadFileSerializer
{
    /// <summary>
    /// Gets the human-readable format name.
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Gets the file extension (including the dot).
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Renders a header line from the ordered column names.
    /// Returns an empty string for formats that have no header row (e.g. OPT).
    /// </summary>
    string RenderHeader(IReadOnlyList<string> columns);

    /// <summary>
    /// Renders a single data record to a delimited line, applying format-specific escaping.
    /// </summary>
    string RenderRecord(LoadFileRecord record);
}
