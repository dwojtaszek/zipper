// <copyright file="LoadFileWriterBase.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
    protected static MetadataColumns GenerateMetadataValues(FileWorkItem workItem, FileData fileData)
    {
        return new MetadataColumns
        {
            Custodian = $"Custodian {workItem.FolderNumber}",
            DateSent = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd"),
            Author = $"Author {Random.Shared.Next(1, 100):D3}",
            FileSize = fileData.Data.Length,
        };
    }

    /// <summary>
    /// Generates EML-specific column values for a file.
    /// </summary>
    /// <returns></returns>
    protected static EmlColumns GenerateEmlValues(FileWorkItem workItem, FileData fileData)
    {
        return new EmlColumns
        {
            To = $"recipient{workItem.Index}@example.com",
            From = $"sender{workItem.Index}@example.com",
            Subject = $"Email Subject {workItem.Index}",
            SentDate = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss"),
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
