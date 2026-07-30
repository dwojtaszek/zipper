namespace Zipper.LoadFiles;

/// <summary>
/// Renders <see cref="LoadFileRecord"/> instances to OPT (Opticon) format: a fixed
/// positional comma-separated layout with no header row and no quoting. Each record carries
/// the seven Opticon fields in order; empty reserved fields are rendered as empty columns.
/// </summary>
internal sealed class OptSerializer : ILoadFileSerializer
{
    public string FormatName => "OPT";

    public string FileExtension => ".opt";

    public string RenderHeader(IReadOnlyList<string> columns) => string.Empty;

    public string RenderRecord(LoadFileRecord record) =>
        string.Join(",", record.Columns.Select((_, i) => i < record.Values.Count ? record.Values[i] : string.Empty));
}
