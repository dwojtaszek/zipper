// <copyright file="OfficeFileGenerator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.IO.Compression;

namespace Zipper;

/// <summary>
/// Generates Microsoft Office format documents (DOCX, XLSX)
/// Note: This is a simplified implementation that creates minimal valid Office files.
/// </summary>
internal static class OfficeFileGenerator
{
    /// <summary>
    /// Generates a minimal DOCX document
    /// DOCX files are ZIP archives containing XML files.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid DOCX document.</returns>
    public static byte[] GenerateDocx(FileWorkItem workItem)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
        {
            // Create [Content_Types].xml
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

            // Create _rels/.rels
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

            // Create word/document.xml
            var documentXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:body>" +
                "<w:p><w:r><w:t>Document " + workItem.Index + "</w:t></w:r></w:p>" +
                "<w:p><w:r><w:t>Control Number: DOC" + workItem.Index.ToString("D8") + "</w:t></w:r></w:p>" +
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
    /// Generates an XLSX spreadsheet with sample data using ClosedXML.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid XLSX spreadsheet.</returns>
    public static byte[] GenerateXlsx(FileWorkItem workItem)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell("A1").Value = "Control Number";
        worksheet.Cell("B1").Value = "Date";
        worksheet.Cell("C1").Value = "Description";

        for (int i = 2; i <= 10; i++)
        {
            worksheet.Cell($"A{i}").Value = $"DOC{workItem.Index:D8}";
            worksheet.Cell($"B{i}").Value = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 365));
            worksheet.Cell($"C{i}").Value = $"Item {i} for document {workItem.Index}";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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
}
