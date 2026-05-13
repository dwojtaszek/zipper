namespace Zipper.LoadFiles;

/// <summary>
/// Serializes <see cref="LoadFileRecord"/> instances to a specific load file format.
/// Separates "what data goes in a row" from "how that row is written."
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
    /// Writes a header row to the stream.
    /// </summary>
    Task WriteHeaderAsync(Stream stream, IReadOnlyList<string> columns);

    /// <summary>
    /// Writes a single data record to the stream.
    /// </summary>
    Task WriteRecordAsync(Stream stream, LoadFileRecord record);

    /// <summary>
    /// Flushes any buffered output.
    /// </summary>
    Task FlushAsync(Stream stream);
}
