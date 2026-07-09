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
        ChaosEngine? chaosEngine = null,
        CancellationToken cancellationToken = default)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,

            // Intentionally UTF-8: EDRM XML schema requires UTF-8 per the XML declaration.
            Encoding = Encoding.UTF8,
            CloseOutput = false,
        };

        var writer = System.Xml.XmlWriter.Create(stream, settings);
        await using (writer.ConfigureAwait(false))
        {
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
                var batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;

                var relationships = new List<(string ParentId, string ChildId)>();

                foreach (var fileData in processedFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
                    string parentId = batesSequence is not null
                        ? batesSequence.Format(fileData.WorkItem.Index - 1).ToString()
                        : $"DOC{fileData.WorkItem.Index:D8}";
                    string childId = hasAttachment ? $"{parentId}_A001" : parentId;

                    var meta = request.Metadata.ShouldIncludeMetadataColumns(request.Output) ? SyntheticRowValues.Metadata(fileData.WorkItem, fileData, random, now) : default;
                    var eml = request.Metadata.ShouldIncludeEmlColumns(request.Output) ? SyntheticRowValues.Eml(fileData.WorkItem, fileData, random, now) : default;

                    var parentElement = CreateDocumentElement(
                        fileData.WorkItem,
                        fileData,
                        request,
                        batesSequence,
                        isChild: false,
                        parentId: parentId,
                        childId: childId,
                        parentDocId: string.Empty,
                        actualAttachment: null,
                        meta,
                        eml);
                    await parentElement.WriteToAsync(writer, cancellationToken).ConfigureAwait(false);

                    if (hasAttachment)
                    {
                        relationships.Add((parentId, childId));

                        var childElement = CreateDocumentElement(
                            fileData.WorkItem,
                            fileData,
                            request,
                            batesSequence,
                            isChild: true,
                            parentId: parentId,
                            childId: childId,
                            parentDocId: parentId,
                            actualAttachment: fileData.Attachment,
                            meta,
                            eml);
                        await childElement.WriteToAsync(writer, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (relationships.Count > 0)
                {
                    await writer.WriteStartElementAsync(null, "Relationships", null).ConfigureAwait(false);
                    foreach (var rel in relationships)
                    {
                        await writer.WriteStartElementAsync(null, "Relationship", null).ConfigureAwait(false);
                        await writer.WriteAttributeStringAsync(null, "Type", null, "Attachment").ConfigureAwait(false);
                        await writer.WriteAttributeStringAsync(null, "ParentDocID", null, rel.ParentId).ConfigureAwait(false);
                        await writer.WriteAttributeStringAsync(null, "ChildDocID", null, rel.ChildId).ConfigureAwait(false);
                        await writer.WriteEndElementAsync().ConfigureAwait(false); // </Relationship>
                    }
                    await writer.WriteEndElementAsync().ConfigureAwait(false); // </Relationships>
                }

                await writer.WriteEndElementAsync().ConfigureAwait(false); // </Batch>
                await writer.WriteEndElementAsync().ConfigureAwait(false); // </Root>
                await writer.WriteEndDocumentAsync().ConfigureAwait(false);

                await writer.FlushAsync().ConfigureAwait(false);
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
        BatesSequence? batesSequence,
        bool isChild,
        string parentId,
        string childId,
        string parentDocId,
        (string filename, byte[] content)? actualAttachment,
        (string Custodian, string DateSent, string Author, string FileSize) meta,
        (string To, string From, string Subject, string SentDate, string Attachment) eml)
    {
        var namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;
        var docElement = new XElement("Document", new XAttribute("DocID", isChild ? childId : parentId));

        var filesElement = new XElement("Files");

        if (isChild)
        {
            var attach = actualAttachment!.Value;
            var sanitizedFilename = FamilyPlan.SanitizeAttachmentFilename(attach.filename);
            var childNativePath = $"{workItem.FolderName}/{workItem.Index}_{sanitizedFilename}".Replace('\\', '/');
            var childTextPath = $"{workItem.FolderName}/{workItem.Index}_{Path.GetFileNameWithoutExtension(sanitizedFilename)}.txt".Replace('\\', '/');

            // Resolve child hashes
            string childHash = string.Empty;
            var childHashes = new Dictionary<Config.HashAlgorithm, string>();
            if (request.Hash.IsEnabled)
            {
                if (request.Hash.Mode == Config.HashMode.Actual)
                {
                    foreach (var algo in request.Hash.Algorithms)
                    {
                        childHashes[algo] = Config.HashUtility.ComputeHashHex(attach.content, algo);
                    }
                }
                else // Simulated
                {
                    var childRng = Config.HashUtility.CreateSeededRandom(request, workItem.Index + 1000000);
                    foreach (var algo in request.Hash.Algorithms)
                    {
                        childHashes[algo] = Config.HashUtility.GenerateSimulatedHash(algo, childRng);
                    }
                }
                childHash = childHashes.TryGetValue(Config.HashAlgorithm.MD5, out var md5)
                    ? md5
                    : (childHashes.Values.FirstOrDefault() ?? string.Empty);
            }
            else
            {
#pragma warning disable S4790
                childHash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(attach.content)).ToLowerInvariant();
#pragma warning restore S4790
            }

            var childNativeExternalFile = new XElement(
                "ExternalFile",
                new XAttribute("FilePath", childNativePath),
                new XAttribute("FileName", sanitizedFilename),
                new XAttribute("FileSize", attach.content.Length),
                new XAttribute("Hash", childHash));

            if (request.Hash.IsEnabled)
            {
                foreach (var kvp in childHashes)
                {
                    if (kvp.Key == Config.HashAlgorithm.MD5)
                        continue;

                    var attrName = kvp.Key switch
                    {
                        Config.HashAlgorithm.SHA1 => "Sha1Hash",
                        Config.HashAlgorithm.SHA256 => "Sha256Hash",
                        _ => null,
                    };
                    if (attrName is not null)
                    {
                        childNativeExternalFile.Add(new XAttribute(attrName, kvp.Value));
                    }
                }
            }

            var nativeFile = new XElement("File", new XAttribute("FileType", "Native"), childNativeExternalFile);
            filesElement.Add(nativeFile);

            if (request.Output.WithText)
            {
#pragma warning disable S4790
                var childTextHash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(PlaceholderFiles.ExtractedText)).ToLowerInvariant();
#pragma warning restore S4790

                var textFile = new XElement(
                    "File",
                    new XAttribute("FileType", "Text"),
                    new XElement(
                        "ExternalFile",
                        new XAttribute("FilePath", childTextPath),
                        new XAttribute("FileName", Path.GetFileName(childTextPath)),
                        new XAttribute("FileSize", PlaceholderFiles.ExtractedText.Length),
                        new XAttribute("Hash", childTextHash)));
                filesElement.Add(textFile);
            }
        }
        else
        {
            var nativeExternalFile = new XElement(
                "ExternalFile",
                new XAttribute("FilePath", workItem.FilePathInZip),
                new XAttribute("FileName", workItem.FileName),
                new XAttribute("FileSize", fileData.DataLength),
                new XAttribute("Hash", fileData.Hash));

            if (fileData.Hashes is not null)
            {
                foreach (var kvp in fileData.Hashes)
                {
                    if (kvp.Key == Config.HashAlgorithm.MD5)
                        continue;

                    var attrName = kvp.Key switch
                    {
                        Config.HashAlgorithm.SHA1 => "Sha1Hash",
                        Config.HashAlgorithm.SHA256 => "Sha256Hash",
                        _ => null,
                    };

                    if (attrName is not null)
                    {
                        nativeExternalFile.Add(new XAttribute(attrName, kvp.Value));
                    }
                }
            }

            var nativeFile = new XElement("File", new XAttribute("FileType", "Native"), nativeExternalFile);
            filesElement.Add(nativeFile);

            if (request.Output.WithText)
            {
#pragma warning disable S4790
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
        }

        docElement.Add(filesElement);

        var tagsElement = new XElement("Tags");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            AddTag(tagsElement, "Custodian", meta.Custodian, namingConvention);
            AddTag(tagsElement, "DateSent", isChild ? string.Empty : meta.DateSent, namingConvention);
            AddTag(tagsElement, "Author", isChild ? string.Empty : meta.Author, namingConvention);
            AddTag(tagsElement, "FileSize", isChild ? actualAttachment!.Value.content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) : meta.FileSize, namingConvention);
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            AddTag(tagsElement, "To", isChild ? string.Empty : eml.To, namingConvention);
            AddTag(tagsElement, "From", isChild ? string.Empty : eml.From, namingConvention);
            AddTag(tagsElement, "Subject", isChild ? string.Empty : eml.Subject, namingConvention);
            AddTag(tagsElement, "SentDate", isChild ? string.Empty : eml.SentDate, namingConvention);
            AddTag(tagsElement, "Attachment", isChild ? string.Empty : eml.Attachment, namingConvention);
        }

        if (request.Bates != null)
        {
            AddTag(tagsElement, "BatesNumber", isChild ? childId : GenerateBatesNumber(batesSequence!, workItem), namingConvention);
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            AddTag(tagsElement, "PageCount", isChild ? 1 : fileData.PageCount, namingConvention);
        }

        if (request.Output.WithText)
        {
            var textPath = isChild
                ? $"{workItem.FolderName}/{workItem.Index}_{Path.GetFileNameWithoutExtension(FamilyPlan.SanitizeAttachmentFilename(actualAttachment!.Value.filename))}.txt"
                : GenerateTextPath(request, workItem);
            AddTag(tagsElement, "ExtractedTextPath", textPath, namingConvention);
        }

        if (request.Metadata.WithFamilies)
        {
            AddTag(tagsElement, "BEGATTACH", parentId, namingConvention);
            AddTag(tagsElement, "ENDATTACH", childId, namingConvention);
            AddTag(tagsElement, "PARENTDOCID", parentDocId, namingConvention);
        }

        if (tagsElement.HasElements)
        {
            docElement.Add(tagsElement);
        }

        return docElement;
    }

    private static string GenerateTextPath(FileGenerationRequest request, FileWorkItem workItem)
        => workItem.FilePathInZip.Replace($".{request.Output.FileType}", ".txt", StringComparison.Ordinal);

    private static string GenerateBatesNumber(BatesSequence batesSequence, FileWorkItem workItem)
        => batesSequence.Format(workItem.Index - 1).ToString();

}
