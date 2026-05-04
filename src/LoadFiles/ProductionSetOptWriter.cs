namespace Zipper.LoadFiles;

internal class ProductionSetOptWriter : LoadFileWriterBase
{
    public override string FormatName => "Production Set OPT";

    public override string FileExtension => ".opt";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var eol = GetEolString(request.Delimiters.EndOfLine);
        await using var writer = CreateWriter(stream, request);

        foreach (var workItem in processedFiles.OrderBy(f => f.WorkItem.Index).Select(f => f.WorkItem))
        {
            var batesNumber = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
            var imagePath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif")
                .Replace(Path.DirectorySeparatorChar, '\\');

            var docBreak = "Y";
            var line = $"{batesNumber},{workItem.FolderName},{imagePath},{docBreak},,1";

            await writer.WriteAsync(line + eol);
        }

        await writer.FlushAsync();
    }
}
