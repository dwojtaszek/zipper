using System.IO.Compression;

namespace Zipper;

/// <summary>
/// Generates Microsoft Office format documents (DOCX, XLSX)
/// Creates unique documents per work item by injecting index-based content.
/// </summary>
internal static class OfficeFileGenerator
{
    /// <summary>
    /// Generates a unique DOCX document with content varied by work item index.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid DOCX document with unique content.</returns>
    public static byte[] GenerateDocx(FileWorkItem workItem)
    {
        return CreateDocx(workItem);
    }

    /// <summary>
    /// Generates a unique XLSX spreadsheet with content varied by work item index.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid XLSX spreadsheet with unique content.</returns>
    public static byte[] GenerateXlsx(FileWorkItem workItem)
    {
        return CreateXlsx(workItem);
    }

    /// <summary>
    /// Generates Office format content based on the specified file type.
    /// </summary>
    /// <param name="fileType">The Office file type (docx or xlsx).</param>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing the generated document.</returns>
    /// <exception cref="System.ArgumentException">Thrown when file type is not a supported Office format.</exception>
    public static byte[] GenerateContent(string fileType, FileWorkItem workItem)
    {
        return fileType.ToLowerInvariant() switch
        {
            "docx" => GenerateDocx(workItem),
            "xlsx" => GenerateXlsx(workItem),
            "pptx" => throw new System.NotImplementedException("PPTX generation is not yet implemented. Please use DOCX or XLSX formats."),
            _ => throw new System.ArgumentException($"Unsupported Office format: {fileType}"),
        };
    }

    /// <summary>
    /// Determines if the specified file type is an Office format.
    /// </summary>
    /// <param name="fileType">The file type to check.</param>
    /// <returns>True if the file type is an Office format.</returns>
    public static bool IsOfficeFormat(string fileType)
    {
        return fileType.Equals("docx", System.StringComparison.OrdinalIgnoreCase) ||
               fileType.Equals("xlsx", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a DOCX document with unique content based on the work item index.
    /// </summary>
    private static byte[] CreateDocx(FileWorkItem workItem)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
        {
            var contentTypes = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-wordprocessingml.document.main+xml\"/>" +
                "</Types>";

            var contentTypesEntry = archive.CreateEntry("[Content_Types].xml");
            using (var contentTypesStream = contentTypesEntry.Open())
            {
                var contentTypesBytes = System.Text.Encoding.UTF8.GetBytes(contentTypes);
                contentTypesStream.Write(contentTypesBytes, 0, contentTypesBytes.Length);
            }

            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"/word/document.xml\"/>" +
                "</Relationships>";

            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var relsStream = relsEntry.Open())
            {
                var relsBytes = System.Text.Encoding.UTF8.GetBytes(rels);
                relsStream.Write(relsBytes, 0, relsBytes.Length);
            }

            var documentXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:body>" +
                $"<w:p><w:r><w:t>This is document {workItem.Index} for eDiscovery testing.</w:t></w:r></w:p>" +
                $"<w:p><w:r><w:t>Control Number: DOC{workItem.Index:D8}</w:t></w:r></w:p>" +
                $"<w:p><w:r><w:t>Folder: {workItem.FolderName}</w:t></w:r></w:p>" +
                "</w:body>" +
                "</w:document>";

            var documentEntry = archive.CreateEntry("word/document.xml");
            using (var documentStream = documentEntry.Open())
            {
                var documentBytes = System.Text.Encoding.UTF8.GetBytes(documentXml);
                documentStream.Write(documentBytes, 0, documentBytes.Length);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Creates an XLSX spreadsheet with unique content based on the work item index.
    /// </summary>
    private static byte[] CreateXlsx(FileWorkItem workItem)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell("A1").Value = "Control Number";
        worksheet.Cell("B1").Value = "Date";
        worksheet.Cell("C1").Value = "Description";

        worksheet.Cell("A2").Value = $"DOC{workItem.Index:D8}";
        worksheet.Cell("B2").Value = DateTime.Now.ToString("yyyy-MM-dd");
        worksheet.Cell("C2").Value = $"Document {workItem.Index} for eDiscovery testing in {workItem.FolderName}";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
