namespace Zipper;

/// <summary>
/// Provides centralized content type detection for file attachments.
/// </summary>
internal static class ContentTypeHelper
{
    /// <summary>
    /// Gets the MIME content type for a file extension.
    /// </summary>
    /// <param name="extension">File extension including the dot (e.g., ".pdf").</param>
    /// <returns>MIME content type string.</returns>
    public static string GetContentTypeForExtension(string extension)
    {
        return (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".eml" => "message/rfc822",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream",
        };
    }
}
