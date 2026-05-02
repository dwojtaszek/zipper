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
        var col = string.IsNullOrEmpty(request.ColumnDelimiter) ? "\u0014" : request.ColumnDelimiter;
        var quote = string.IsNullOrEmpty(request.QuoteDelimiter) ? "\u00fe" : request.QuoteDelimiter;
        var eol = GetEolString(request.EndOfLine);

        await using var writer = CreateWriter(stream, request);

        // Header
        var headers = new[] { "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE" };
        await writer.WriteAsync(string.Join(col, headers.Select(h => $"{quote}{h}{quote}")) + eol);

        // Data rows
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig!, workItem.Index - 1);
            var imagePath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif");

            var nativePath = workItem.FilePathInZip.Replace(Path.DirectorySeparatorChar, '\\');
            var textPath = nativePath.Replace($".{request.FileType}", ".txt");
            var imagesPath = imagePath.Replace(Path.DirectorySeparatorChar, '\\');

#pragma warning disable S2245
            var random = request.Seed.HasValue ? new Random(request.Seed.Value + (int)workItem.Index) : Random.Shared;
#pragma warning restore S2245
            var builder = new MetadataRowBuilder(request, random, DateTime.UtcNow);

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
                request.FileType.ToUpperInvariant(),
            };
            await writer.WriteAsync(string.Join(col, fields.Select(f => $"{quote}{f}{quote}")) + eol);
        }

        await writer.FlushAsync();
    }
}
