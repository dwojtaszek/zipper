using System.Text;
using System.Xml;
using System.Xml.Linq;
using Zipper.Utils;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes XML format load files - structured markup format.
/// </summary>
internal class XmlLoadFileWriter : LoadFileWriterBase
{
    public override string FormatName => "XML";

    public override string FileExtension => ".xml";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,

            // Intentionally UTF-8: EDRM XML schema requires UTF-8 per the XML declaration.
            Encoding = Encoding.UTF8,
            CloseOutput = false,
        };

        await using var writer = System.Xml.XmlWriter.Create(stream, settings);

        try
        {
            // Match the original XDeclaration("1.0", "UTF-8", "yes")
            await writer.WriteStartDocumentAsync(standalone: true);
            await writer.WriteStartElementAsync(null, "documents", null);

#pragma warning disable S2245
            var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
            var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

            foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
            {
                var element = CreateDocumentElement(fileData.WorkItem, fileData, request, random, now);
                await element.WriteToAsync(writer, CancellationToken.None);
            }

            await writer.WriteEndElementAsync(); // </documents>
            await writer.WriteEndDocumentAsync();

            await writer.FlushAsync();
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new InvalidOperationException(
                $"Failed to write XML load file: {ex.Message}", ex);
        }
    }

    private static XElement CreateDocumentElement(
        FileWorkItem workItem,
        FileData fileData,
        FileGenerationRequest request,
        Random random,
        DateTime now)
    {
        var namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;
        var docElement = new XElement(
            "document",
            new XElement(NamingConventionHelper.ApplyConvention("controlNumber", namingConvention), GenerateDocumentId(workItem)),
            new XElement(NamingConventionHelper.ApplyConvention("filePath", namingConvention), workItem.FilePathInZip));

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            var metadata = GenerateMetadataValues(workItem, fileData, random, now, request);
            docElement.Add(new XElement(
                "metadata",
                new XElement(NamingConventionHelper.ApplyConvention("custodian", namingConvention), metadata.Custodian),
                new XElement(NamingConventionHelper.ApplyConvention("dateSent", namingConvention), metadata.DateSent),
                new XElement(NamingConventionHelper.ApplyConvention("author", namingConvention), metadata.Author),
                new XElement(NamingConventionHelper.ApplyConvention("fileSize", namingConvention), metadata.FileSize)));
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            var eml = GenerateEmlValues(workItem, fileData, random, now, request);
            docElement.Add(new XElement(
                "email",
                new XElement(NamingConventionHelper.ApplyConvention("to", namingConvention), eml.To),
                new XElement(NamingConventionHelper.ApplyConvention("from", namingConvention), eml.From),
                new XElement(NamingConventionHelper.ApplyConvention("subject", namingConvention), eml.Subject),
                new XElement(NamingConventionHelper.ApplyConvention("sentDate", namingConvention), eml.SentDate),
                new XElement(NamingConventionHelper.ApplyConvention("attachment", namingConvention), eml.Attachment)));
        }

        if (request.Bates != null)
        {
            docElement.Add(new XElement(NamingConventionHelper.ApplyConvention("batesNumber", namingConvention), GenerateBatesNumber(request, workItem)));
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            docElement.Add(new XElement(NamingConventionHelper.ApplyConvention("pageCount", namingConvention), fileData.PageCount));
        }

        if (request.Output.WithText)
        {
            docElement.Add(new XElement(NamingConventionHelper.ApplyConvention("extractedTextPath", namingConvention), GenerateTextPath(request, workItem)));
        }

        return docElement;
    }
}
