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
        await using var writer = new StreamWriter(stream, EncodingHelper.GetEncodingOrDefault(request.Encoding), leaveOpen: true);

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig!, workItem.Index - 1);
            var imagePath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif")
                .Replace(Path.DirectorySeparatorChar, '\\');

            var docBreak = "Y";
            var line = $"{batesNumber},{workItem.FolderName},{imagePath},{docBreak},,1";

            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync();
    }
}
