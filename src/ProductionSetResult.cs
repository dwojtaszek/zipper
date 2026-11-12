namespace Zipper;

/// <summary>
/// Result of a production set generation operation.
/// </summary>
internal class ProductionSetResult
{
    public string ProductionPath { get; set; } = string.Empty;
    public string? ZipFilePath { get; set; }
    public string DatFilePath { get; set; } = string.Empty;
    public string OptFilePath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public long TotalDocuments { get; set; }
    public string BatesRange { get; set; } = string.Empty;
    public int VolumeCount { get; set; }
    public TimeSpan GenerationTime { get; set; }
}
