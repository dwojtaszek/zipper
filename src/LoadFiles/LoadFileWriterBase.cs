namespace Zipper.LoadFiles;

/// <summary>
/// Base class for load file writers providing common column building functionality.
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
    /// Gets the file type in lowercase for comparisons.
    /// </summary>
    /// <returns></returns>
    protected static string GetFileTypeLower(FileGenerationRequest request) =>
        request.FileType.ToLowerInvariant();

    /// <summary>
    /// Determines if metadata columns should be included.
    /// </summary>
    /// <returns></returns>
    protected static bool ShouldIncludeMetadata(FileGenerationRequest request) =>
        request.WithMetadata || GetFileTypeLower(request) == "eml";

    /// <summary>
    /// Determines if EML-specific columns should be included.
    /// </summary>
    /// <returns></returns>
    protected static bool ShouldIncludeEmlColumns(FileGenerationRequest request) =>
        GetFileTypeLower(request) == "eml";

    /// <summary>
    /// Determines if page count column should be included.
    /// </summary>
    /// <returns></returns>
    protected static bool ShouldIncludePageCount(FileGenerationRequest request) =>
        GetFileTypeLower(request) == "tiff" && request.TiffPageRange.HasValue;

    /// <summary>
    /// Generates metadata column values for a file.
    /// </summary>
    /// <returns></returns>
    protected static MetadataColumns GenerateMetadataValues(FileWorkItem workItem, FileData fileData, Random random, DateTime now)
    {
        return new MetadataColumns
        {
            Custodian = $"Custodian {workItem.FolderNumber}",
            DateSent = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd"),
            Author = $"Author {random.Next(1, 100):D3}",
            FileSize = fileData.DataLength,
        };
    }

    /// <summary>
    /// Generates EML-specific column values for a file.
    /// Uses actual EmailTemplate metadata when available for consistency with EML content.
    /// </summary>
    protected static EmlColumns GenerateEmlValues(FileWorkItem workItem, FileData fileData, Random random, DateTime now)
    {
        if (fileData.EmailTemplate is { } template)
        {
            return new EmlColumns
            {
                To = template.To,
                From = template.From,
                Subject = template.Subject,
                SentDate = template.SentDate.ToString("yyyy-MM-dd HH:mm:ss"),
                Attachment = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : string.Empty,
            };
        }

        return new EmlColumns
        {
            To = $"recipient{workItem.Index}@example.com",
            From = $"sender{workItem.Index}@example.com",
            Subject = $"Email Subject {workItem.Index}",
            SentDate = now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss"),
            Attachment = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : string.Empty,
        };
    }

    /// <summary>
    /// Generates the Bates number for a file.
    /// </summary>
    /// <returns></returns>
    protected static string GenerateBatesNumber(FileGenerationRequest request, FileWorkItem workItem)
    {
        return request.BatesConfig != null
            ? BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1)
            : string.Empty;
    }

    /// <summary>
    /// Generates the extracted text file path for a file.
    /// </summary>
    /// <returns></returns>
    protected static string GenerateTextPath(FileGenerationRequest request, FileWorkItem workItem)
    {
        return workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
    }

    /// <summary>
    /// Generates the document ID for a file.
    /// </summary>
    /// <returns></returns>
    protected static string GenerateDocumentId(FileWorkItem workItem) => $"DOC{workItem.Index:D8}";

    /// <summary>
    /// Escapes a field value for CSV/Concordance formats per RFC 4180.
    /// Wraps in quotes if the value contains comma, quote, CR, or LF characters.
    /// </summary>
    protected static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    /// <summary>
    /// Escapes a field value for Concordance DAT format using the configured quote delimiter.
    /// Doubles the quote character within the field value (e.g., þ → þþ).
    /// </summary>
    /// <param name="field">Field value to escape.</param>
    /// <param name="quoteDelimiter">The quote delimiter character (e.g., ASCII 254 þ).</param>
    /// <returns>Escaped field value.</returns>
    protected static string EscapeDatField(string field, char quoteDelimiter)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        if (field.Contains(quoteDelimiter))
        {
            return field.Replace(quoteDelimiter.ToString(), new string(quoteDelimiter, 2));
        }

        return field;
    }
}

/// <summary>
/// Holds metadata column values.
/// </summary>
internal record MetadataColumns
{
    public string Custodian { get; init; } = string.Empty;

    public string DateSent { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public long FileSize { get; init; }
}

/// <summary>
/// Holds EML-specific column values.
/// </summary>
internal record EmlColumns
{
    public string To { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string SentDate { get; init; } = string.Empty;

    public string Attachment { get; init; } = string.Empty;
}
