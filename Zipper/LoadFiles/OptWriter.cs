// <copyright file="OptWriter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes OPT (Opticon) format load files - tab-separated format used by Relativity.
/// </summary>
internal class OptWriter : LoadFileWriterBase
{
    public override string FormatName => "OPT";

    public override string FileExtension => ".opt";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles)
    {
        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        const char tab = '\t';

        await WriteHeaderAsync(writer, request, tab);
        await WriteRowsAsync(writer, request, processedFiles, tab);
    }

    private static Task WriteHeaderAsync(StreamWriter writer, FileGenerationRequest request, char tab)
    {
        var header = new StringBuilder();
        header.Append($"Control Number{tab}File Path");

        if (ShouldIncludeMetadata(request))
        {
            header.Append($"{tab}Custodian{tab}Date Sent{tab}Author{tab}File Size");
        }

        if (ShouldIncludeEmlColumns(request))
        {
            header.Append($"{tab}To{tab}From{tab}Subject{tab}Sent Date{tab}Attachment");
        }

        if (request.BatesConfig != null)
        {
            header.Append($"{tab}Bates Number");
        }

        if (ShouldIncludePageCount(request))
        {
            header.Append($"{tab}Page Count");
        }

        if (request.WithText)
        {
            header.Append($"{tab}Extracted Text");
        }

        return writer.WriteLineAsync(header.ToString());
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        char tab)
    {
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var docId = GenerateDocumentId(workItem);
            var line = new StringBuilder();

            line.Append($"{docId}{tab}{workItem.FilePathInZip}");

            if (ShouldIncludeMetadata(request))
            {
                var metadata = GenerateMetadataValues(workItem, fileData);
                line.Append($"{tab}{metadata.Custodian}{tab}{metadata.DateSent}{tab}{metadata.Author}{tab}{metadata.FileSize}");
            }

            if (ShouldIncludeEmlColumns(request))
            {
                var eml = GenerateEmlValues(workItem, fileData);
                line.Append($"{tab}{eml.To}{tab}{eml.From}{tab}{eml.Subject}{tab}{eml.SentDate}{tab}{eml.Attachment}");
            }

            if (request.BatesConfig != null)
            {
                line.Append($"{tab}{GenerateBatesNumber(request, workItem)}");
            }

            if (ShouldIncludePageCount(request))
            {
                line.Append($"{tab}{fileData.PageCount}");
            }

            if (request.WithText)
            {
                line.Append($"{tab}{GenerateTextPath(request, workItem)}");
            }

            await writer.WriteLineAsync(line.ToString());
        }
    }
}
