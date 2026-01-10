using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes XML format load files - structured markup format
/// </summary>
internal class XmlWriter : ILoadFileWriter
{
    public string FormatName => "XML";
    public string FileExtension => ".xml";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, System.Collections.Generic.List<FileData> processedFiles)
    {
        var root = new XElement("documents");

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var docElement = new XElement("document",
                new XElement("controlNumber", $"DOC{workItem.Index:D8}"),
                new XElement("filePath", workItem.FilePathInZip));

            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                docElement.Add(new XElement("metadata",
                    new XElement("custodian", $"Custodian {workItem.FolderNumber}"),
                    new XElement("dateSent", System.DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd")),
                    new XElement("author", $"Author {Random.Shared.Next(1, 100):D3}"),
                    new XElement("fileSize", fileData.Data.Length)));
            }

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                docElement.Add(new XElement("email",
                    new XElement("to", $"recipient{workItem.Index}@example.com"),
                    new XElement("from", $"sender{workItem.Index}@example.com"),
                    new XElement("subject", $"Email Subject {workItem.Index}"),
                    new XElement("sentDate", System.DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss")),
                    new XElement("attachment", fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : "")));
            }

            if (request.BatesConfig != null)
            {
                var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1);
                docElement.Add(new XElement("batesNumber", batesNumber));
            }

            if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
            {
                docElement.Add(new XElement("pageCount", fileData.PageCount));
            }

            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                docElement.Add(new XElement("extractedTextPath", textFilePath));
            }

            root.Add(docElement);
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            root);

        using var writer = new StreamWriter(stream, Encoding.UTF8);
        // Write XML declaration manually since XDocument.ToString() doesn't include it
        await writer.WriteAsync($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        await writer.WriteAsync(document.ToString());
    }
}
