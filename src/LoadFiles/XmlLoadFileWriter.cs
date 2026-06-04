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
    private const string FieldElement = "Field";

    public override string FormatName => "XML";

    public override string FileExtension => ".xml";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
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
            await writer.WriteStartElementAsync(null, "Root", null);
            await writer.WriteAttributeStringAsync(null, "DataInterchangeType", null, "Export");
            await writer.WriteAttributeStringAsync(null, "MajorVersion", null, "1");
            await writer.WriteAttributeStringAsync(null, "MinorVersion", null, "2");

            await writer.WriteStartElementAsync(null, "Batch", null);

#pragma warning disable S2245
            var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
            var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

            foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
            {
                var element = CreateDocumentElement(fileData.WorkItem, fileData, request, random, now);
                await element.WriteToAsync(writer, CancellationToken.None);
            }

            await writer.WriteEndElementAsync(); // </Batch>
            await writer.WriteEndElementAsync(); // </Root>
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
        var docElement = new XElement("Document", new XAttribute("DocID", GenerateDocumentId(workItem)));

        var filesElement = new XElement("Files");

        // Native file reference
        var nativeFile = new XElement(
            "File",
            new XAttribute("FileType", "Native"),
            new XElement(
                "ExternalFile",
                new XAttribute("FilePath", workItem.FilePathInZip),
                new XAttribute("FileName", workItem.FileName),
                new XAttribute("FileSize", fileData.DataLength),
                new XAttribute("Hash", fileData.Hash)));
        filesElement.Add(nativeFile);

        // Extracted Text file reference if applicable
        if (request.Output.WithText)
        {
#pragma warning disable S4790 // Cryptographic algorithms should be robust
            var textHash = string.Equals(request.Output.FileType, "eml", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToHexString(System.Security.Cryptography.MD5.HashData(PlaceholderFiles.EmlExtractedText)).ToLowerInvariant()
                : Convert.ToHexString(System.Security.Cryptography.MD5.HashData(PlaceholderFiles.ExtractedText)).ToLowerInvariant();
#pragma warning restore S4790

            var textFileSize = string.Equals(request.Output.FileType, "eml", StringComparison.OrdinalIgnoreCase)
                ? PlaceholderFiles.EmlExtractedText.Length
                : PlaceholderFiles.ExtractedText.Length;

            var textFile = new XElement(
                "File",
                new XAttribute("FileType", "Text"),
                new XElement(
                    "ExternalFile",
                    new XAttribute("FilePath", GenerateTextPath(request, workItem)),
                    new XAttribute("FileName", System.IO.Path.GetFileName(GenerateTextPath(request, workItem))),
                    new XAttribute("FileSize", textFileSize),
                    new XAttribute("Hash", textHash)));
            filesElement.Add(textFile);
        }

        docElement.Add(filesElement);

        var fieldsElement = new XElement("Fields");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            var metadata = GenerateMetadataValues(workItem, fileData, random, now, request);

            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("Custodian", namingConvention)), metadata.Custodian));
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("DateSent", namingConvention)), metadata.DateSent));
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("Author", namingConvention)), metadata.Author));
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("FileSize", namingConvention)), metadata.FileSize));
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            var eml = GenerateEmlValues(workItem, fileData, random, now, request);

            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("To", namingConvention)), eml.To));
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("From", namingConvention)), eml.From));
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("Subject", namingConvention)), eml.Subject));
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("SentDate", namingConvention)), eml.SentDate));
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("Attachment", namingConvention)), eml.Attachment));
        }

        if (request.Bates != null)
        {
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("BatesNumber", namingConvention)), GenerateBatesNumber(request, workItem)));
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("PageCount", namingConvention)), fileData.PageCount));
        }

        if (request.Output.WithText)
        {
            fieldsElement.Add(new XElement(FieldElement, new XAttribute("Name", NamingConventionHelper.ApplyConvention("ExtractedTextPath", namingConvention)), GenerateTextPath(request, workItem)));
        }

        if (fieldsElement.HasElements)
        {
            docElement.Add(fieldsElement);
        }

        return docElement;
    }
}
