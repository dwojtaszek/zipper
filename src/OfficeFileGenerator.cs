using System.IO.Compression;

namespace Zipper;

/// <summary>
/// Generates Microsoft Office format Native Files (DOCX, XLSX).
/// Implements <see cref="IFileGenerator"/> directly for pipeline use.
/// Pre-computes minimal valid Office files at static init for O(1) generation.
/// </summary>
internal sealed class OfficeFileGenerator : IFileGenerator
{
    private static readonly DateTimeOffset FixedTimestamp = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Pre-computed minimal valid DOCX Native File.
    /// </summary>
    private static readonly byte[] PrecomputedDocx = CreateMinimalDocx();

    /// <summary>
    /// Pre-computed minimal valid XLSX spreadsheet.
    /// </summary>
    private static readonly byte[] PrecomputedXlsx = CreateMinimalXlsx();

    public OfficeFileGenerator(string fileType)
    {
        this.FileType = fileType;
    }

    public string FileType { get; }

    public bool IsPlaceholderBased => false;

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        return new GeneratedFileContent
        {
            Content = GenerateContent(this.FileType, workItem),
        };
    }

    /// <summary>
    /// Returns a pre-computed minimal DOCX Native File.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid DOCX Native File.</returns>
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
    /// <returns>Byte array containing the generated Native File.</returns>
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
    /// For test use only. Determines if the specified file type is an Office format.
    /// </summary>
    /// <param name="fileType">The file type to check.</param>
    /// <returns>True if the file type is an Office format.</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool IsOfficeFormat(string fileType)
    {
        return fileType.Equals("docx", System.StringComparison.OrdinalIgnoreCase) ||
               fileType.Equals("xlsx", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a minimal valid DOCX Native File once at static init.
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
            contentTypesEntry.LastWriteTime = FixedTimestamp;
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
            relsEntry.LastWriteTime = FixedTimestamp;
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
            documentEntry.LastWriteTime = FixedTimestamp;
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

        using var rawStream = new MemoryStream();
        workbook.SaveAs(rawStream);
        rawStream.Position = 0;

        using var inArchive = new ZipArchive(rawStream, ZipArchiveMode.Read);
        using var outStream = new MemoryStream();
        using (var outArchive = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var staticCoreXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                "xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<dc:creator>Zipper</dc:creator><cp:lastModifiedBy>Zipper</cp:lastModifiedBy>" +
                "<dcterms:created xsi:type=\"dcterms:W3CDTF\">2024-01-01T00:00:00Z</dcterms:created>" +
                "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">2024-01-01T00:00:00Z</dcterms:modified>" +
                "</cp:coreProperties>";

            var staticRelsXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"/xl/workbook.xml\" Id=\"rId1\" />" +
                "<Relationship Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"/docProps/app.xml\" Id=\"rId2\" />" +
                "<Relationship Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"/docProps/core.xml\" Id=\"rId3\" />" +
                "</Relationships>";

            var staticContentTypes = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\" />" +
                "<Default Extension=\"psmdcp\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\" />" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />" +
                "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\" />" +
                "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\" />" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\" />" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\" />" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" />" +
                "<Override PartName=\"/xl/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\" />" +
                "</Types>";

            foreach (var entry in inArchive.Entries)
            {
                if (entry.FullName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal))
                {
                    continue;
                }

                var newEntry = outArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                newEntry.LastWriteTime = FixedTimestamp;

                using var destStream = newEntry.Open();
                if (entry.FullName == "_rels/.rels")
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(staticRelsXml);
                    destStream.Write(bytes, 0, bytes.Length);
                }
                else if (entry.FullName == "[Content_Types].xml")
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(staticContentTypes);
                    destStream.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    using var srcStream = entry.Open();
                    srcStream.CopyTo(destStream);
                }
            }

            var coreEntry = outArchive.CreateEntry("docProps/core.xml", CompressionLevel.Optimal);
            coreEntry.LastWriteTime = FixedTimestamp;
            using (var destStream = coreEntry.Open())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(staticCoreXml);
                destStream.Write(bytes, 0, bytes.Length);
            }
        }

        return outStream.ToArray();
    }
}
