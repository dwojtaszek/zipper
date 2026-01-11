using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Base class for load file writers providing common column building functionality
/// </summary>
internal abstract class LoadFileWriterBase : ILoadFileWriter
{
    public abstract string FormatName { get; }
    public abstract string FileExtension { get; }

    public abstract System.Threading.Tasks.Task WriteAsync(
        System.IO.Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles);

    /// <summary>
    /// Gets the file type in lowercase for comparisons
    /// </summary>
    protected static string GetFileTypeLower(FileGenerationRequest request) =>
        request.FileType.ToLowerInvariant();

    /// <summary>
    /// Determines if metadata columns should be included
    /// </summary>
    protected static bool ShouldIncludeMetadata(FileGenerationRequest request) =>
        request.WithMetadata || GetFileTypeLower(request) == "eml";

    /// <summary>
    /// Determines if EML-specific columns should be included
    /// </summary>
    protected static bool ShouldIncludeEmlColumns(FileGenerationRequest request) =>
        GetFileTypeLower(request) == "eml";

    /// <summary>
    /// Determines if page count column should be included
    /// </summary>
    protected static bool ShouldIncludePageCount(FileGenerationRequest request) =>
        GetFileTypeLower(request) == "tiff" && request.TiffPageRange.HasValue;

    /// <summary>
    /// Generates metadata column values for a file
    /// </summary>
    protected static MetadataColumns GenerateMetadataValues(FileWorkItem workItem, FileData fileData)
    {
        return new MetadataColumns
        {
            Custodian = $"Custodian {workItem.FolderNumber}",
            DateSent = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd"),
            Author = $"Author {Random.Shared.Next(1, 100):D3}",
            FileSize = fileData.Data.Length
        };
    }

    /// <summary>
    /// Generates EML-specific column values for a file
    /// </summary>
    protected static EmlColumns GenerateEmlValues(FileWorkItem workItem, FileData fileData)
    {
        return new EmlColumns
        {
            To = $"recipient{workItem.Index}@example.com",
            From = $"sender{workItem.Index}@example.com",
            Subject = $"Email Subject {workItem.Index}",
            SentDate = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss"),
            Attachment = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : ""
        };
    }

    /// <summary>
    /// Generates the Bates number for a file
    /// </summary>
    protected static string GenerateBatesNumber(FileGenerationRequest request, FileWorkItem workItem)
    {
        return request.BatesConfig != null
            ? BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1)
            : "";
    }

    /// <summary>
    /// Generates the extracted text file path for a file
    /// </summary>
    protected static string GenerateTextPath(FileGenerationRequest request, FileWorkItem workItem)
    {
        return workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
    }

    /// <summary>
    /// Generates the document ID for a file
    /// </summary>
    protected static string GenerateDocumentId(FileWorkItem workItem) => $"DOC{workItem.Index:D8}";
}

/// <summary>
/// Holds metadata column values
/// </summary>
internal record MetadataColumns
{
    public string Custodian { get; init; } = "";
    public string DateSent { get; init; } = "";
    public string Author { get; init; } = "";
    public long FileSize { get; init; }
}

/// <summary>
/// Holds EML-specific column values
/// </summary>
internal record EmlColumns
{
    public string To { get; init; } = "";
    public string From { get; init; } = "";
    public string Subject { get; init; } = "";
    public string SentDate { get; init; } = "";
    public string Attachment { get; init; } = "";
}
