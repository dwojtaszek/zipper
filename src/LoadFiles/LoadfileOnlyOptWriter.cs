using System.Text;

namespace Zipper.LoadFiles;

internal class LoadfileOnlyOptWriter : LoadFileWriterBase
{
    public override string FormatName => "OPT (Image)";

    public override string FileExtension => ".opt";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.Encoding);
        var eolString = GetEolString(request.EndOfLine);

        using var memStream = new MemoryStream();

        var now = request.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

#pragma warning disable S2245
        var random = request.Seed.HasValue ? new Random(request.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        var buffer = new StringBuilder();

        for (long i = 1; i <= request.FileCount; i++)
        {
            long lineNumber = i;
            string batesId = $"IMG{i:D8}";
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesId}.tif";
            string docBreak = "Y";
            string folderBreak = string.Empty;
            string boxBreak = string.Empty;
            int pageCount = random.Next(1, 11);

            string line = $"{batesId},{volume},{imagePath},{docBreak},{folderBreak},{boxBreak},{pageCount}";

            line = ApplyChaosInterception(chaosEngine, lineNumber, line, batesId);

            buffer.Append(line);
            buffer.Append(eolString);

            if (buffer.Length > 1000 * 200)
            {
                var batchBytes = encoding.GetBytes(buffer.ToString());
                await memStream.WriteAsync(batchBytes);
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            var remainingBytes = encoding.GetBytes(buffer.ToString());
            await memStream.WriteAsync(remainingBytes);
        }

        memStream.Position = 0;
        await memStream.CopyToAsync(stream);
    }
}
