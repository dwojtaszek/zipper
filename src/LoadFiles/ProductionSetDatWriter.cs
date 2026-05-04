namespace Zipper.LoadFiles;

internal class ProductionSetDatWriter : LoadFileWriterBase
{
    public override string FormatName => "Production Set DAT";

    public override string FileExtension => ".dat";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var col = string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? "\u0014" : request.Delimiters.ColumnDelimiter;
        var quote = string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? "\u00fe" : request.Delimiters.QuoteDelimiter;
        var eol = GetEolString(request.Delimiters.EndOfLine);

        await using var writer = CreateWriter(stream, request);

        // Header
        var headers = new[] { "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE" };
        await writer.WriteAsync(string.Join(col, headers.Select(h => $"{quote}{h}{quote}")) + eol);

        // Data rows
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var batesNumber = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
            var imagePath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif");

            var nativePath = workItem.FilePathInZip.Replace(Path.DirectorySeparatorChar, '\\');
            var textPath = nativePath.Replace($".{request.Output.FileType}", ".txt");
            var imagesPath = imagePath.Replace(Path.DirectorySeparatorChar, '\\');

#pragma warning disable S2245
            var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + (int)workItem.Index) : Random.Shared;
#pragma warning restore S2245
            var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
            var builder = new MetadataRowBuilder(request, random, now);

            var fields = new[]
            {
                batesNumber,
                batesNumber,
                workItem.FolderName,
                nativePath,
                textPath,
                imagesPath,
                builder.GetCustodian(),
                builder.GetDateCreated(),
                fileData.DataLength.ToString(),
                request.Output.FileType.ToUpperInvariant(),
            };
            await writer.WriteAsync(string.Join(col, fields.Select(f => $"{quote}{f}{quote}")) + eol);
        }

        await writer.FlushAsync();
    }
}
