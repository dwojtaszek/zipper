// <copyright file="XmlWriter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;
using System.Xml.Linq;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes XML format load files - structured markup format.
/// </summary>
internal class XmlWriter : LoadFileWriterBase
{
    public override string FormatName => "XML";

    public override string FileExtension => ".xml";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles)
    {
        var root = new XElement("documents");

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            root.Add(CreateDocumentElement(fileData.WorkItem, fileData, request));
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);

        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        // XDocument.ToString() doesn't include the declaration, so we write it explicitly
        await writer.WriteAsync(document.Declaration?.ToString() ?? string.Empty);
        await writer.WriteAsync(document.ToString());

        // Flush to ensure data is written
        await writer.FlushAsync();
    }

    private static XElement CreateDocumentElement(
        FileWorkItem workItem,
        FileData fileData,
        FileGenerationRequest request)
    {
        var docElement = new XElement(
            "document",
            new XElement("controlNumber", GenerateDocumentId(workItem)),
            new XElement("filePath", workItem.FilePathInZip));

        if (ShouldIncludeMetadata(request))
        {
            var metadata = GenerateMetadataValues(workItem, fileData);
            docElement.Add(new XElement(
                "metadata",
                new XElement("custodian", metadata.Custodian),
                new XElement("dateSent", metadata.DateSent),
                new XElement("author", metadata.Author),
                new XElement("fileSize", metadata.FileSize)));
        }

        if (ShouldIncludeEmlColumns(request))
        {
            var eml = GenerateEmlValues(workItem, fileData);
            docElement.Add(new XElement(
                "email",
                new XElement("to", eml.To),
                new XElement("from", eml.From),
                new XElement("subject", eml.Subject),
                new XElement("sentDate", eml.SentDate),
                new XElement("attachment", eml.Attachment)));
        }

        if (request.BatesConfig != null)
        {
            docElement.Add(new XElement("batesNumber", GenerateBatesNumber(request, workItem)));
        }

        if (ShouldIncludePageCount(request))
        {
            docElement.Add(new XElement("pageCount", fileData.PageCount));
        }

        if (request.WithText)
        {
            docElement.Add(new XElement("extractedTextPath", GenerateTextPath(request, workItem)));
        }

        return docElement;
    }
}
