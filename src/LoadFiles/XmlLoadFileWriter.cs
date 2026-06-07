using System.Text;
using System.Xml;
using System.Xml.Linq;
using Zipper.Utils;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes XML format load files - structured markup format.
/// </summary>
internal sealed class XmlLoadFileWriter : ILoadFileWriter
{
    private const string TagElement = "Tag";

    public string FormatName => "XML";

    public string FileExtension => ".xml";

    public async Task WriteAsync(
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

            foreach (var fileData in processedFiles)
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

    private static void AddTag(XElement parent, string name, object? value, string? namingConvention)
    {
        parent.Add(new XElement(TagElement,
            new XAttribute("TagName", NamingConventionHelper.ApplyConvention(name, namingConvention)),
            new XAttribute("TagValue", value ?? string.Empty)));
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

        var tagsElement = new XElement("Tags");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            var metadata = SyntheticRowValues.Metadata(workItem, fileData, random, now);

            AddTag(tagsElement, "Custodian", metadata.Custodian, namingConvention);
            AddTag(tagsElement, "DateSent", metadata.DateSent, namingConvention);
            AddTag(tagsElement, "Author", metadata.Author, namingConvention);
            AddTag(tagsElement, "FileSize", metadata.FileSize, namingConvention);
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            var eml = SyntheticRowValues.Eml(workItem, fileData, random, now);

            AddTag(tagsElement, "To", eml.To, namingConvention);
            AddTag(tagsElement, "From", eml.From, namingConvention);
            AddTag(tagsElement, "Subject", eml.Subject, namingConvention);
            AddTag(tagsElement, "SentDate", eml.SentDate, namingConvention);
            AddTag(tagsElement, "Attachment", eml.Attachment, namingConvention);
        }

        if (request.Bates != null)
        {
            AddTag(tagsElement, "BatesNumber", GenerateBatesNumber(request, workItem), namingConvention);
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            AddTag(tagsElement, "PageCount", fileData.PageCount, namingConvention);
        }

        if (request.Output.WithText)
        {
            AddTag(tagsElement, "ExtractedTextPath", GenerateTextPath(request, workItem), namingConvention);
        }

        if (tagsElement.HasElements)
        {
            docElement.Add(tagsElement);
        }

        return docElement;
    }

    private static string GenerateDocumentId(FileWorkItem workItem) => $"DOC{workItem.Index:D8}";

    private static string GenerateTextPath(FileGenerationRequest request, FileWorkItem workItem)
        => workItem.FilePathInZip.Replace($".{request.Output.FileType}", ".txt");

    private static string GenerateBatesNumber(FileGenerationRequest request, FileWorkItem workItem)
        => request.Bates != null
            ? BatesNumberGenerator.Generate(request.Bates, workItem.Index - 1)
            : string.Empty;

}
