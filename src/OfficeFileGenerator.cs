using System.IO.Compression;

namespace Zipper;

/// <summary>
/// Generates Microsoft Office format documents (DOCX, XLSX)
/// Pre-computes minimal valid Office files at static init for O(1) generation.
/// </summary>
internal static class OfficeFileGenerator
{
    /// <summary>
    /// Pre-computed minimal valid DOCX document.
    /// </summary>
    private static readonly byte[] PrecomputedDocx = CreateMinimalDocx();

    /// <summary>
    /// Pre-computed minimal valid XLSX spreadsheet.
    /// </summary>
    private static readonly byte[] PrecomputedXlsx = CreateMinimalXlsx();

    /// <summary>
    /// Returns a pre-computed minimal DOCX document.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid DOCX document.</returns>
    public static byte[] GenerateDocx(FileWorkItem workItem)
    {
        return PrecomputedDocx;
    }

    /// <summary>
    /// Returns a pre-computed minimal XLSX spreadsheet.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid XLSX spreadsheet.</returns>
    public static byte[] GenerateXlsx(FileWorkItem workItem)
    {
        return PrecomputedXlsx;
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
        if (fileType.Equals("docx", System.StringComparison.OrdinalIgnoreCase))
        {
            return GenerateDocx(workItem);
        }

        if (fileType.Equals("xlsx", System.StringComparison.OrdinalIgnoreCase))
        {
            return GenerateXlsx(workItem);
        }

        if (fileType.Equals("pptx", System.StringComparison.OrdinalIgnoreCase))
        {
            throw new System.NotImplementedException("PPTX generation is not yet implemented. Please use DOCX or XLSX formats.");
        }

        throw new System.ArgumentException($"Unsupported Office format: {fileType}");
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
    /// Creates a minimal valid DOCX document once at static init.
    /// </summary>
    private static byte[] CreateMinimalDocx()
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
                "<w:p><w:r><w:t>This is a sample document for eDiscovery testing.</w:t></w:r></w:p>" +
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
    /// Creates a minimal valid XLSX spreadsheet once at static init.
    /// </summary>
    private static byte[] CreateMinimalXlsx()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell("A1").Value = "Control Number";
        worksheet.Cell("B1").Value = "Date";
        worksheet.Cell("C1").Value = "Description";

        worksheet.Cell("A2").Value = "DOC00000001";
        worksheet.Cell("B2").Value = "Sample Date";
        worksheet.Cell("C2").Value = "Sample document for eDiscovery testing";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
