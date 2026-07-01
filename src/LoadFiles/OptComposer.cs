namespace Zipper.LoadFiles;

/// <summary>
/// Column authority for the OPT (Opticon) format across Standard, Loadfile-Only, and
/// Production Set modes. OPT has no header row; each record is one page-level entry expanded
/// from a Native File (multipage Native Files, Native File break markers, and child Attachment rows).
/// </summary>
internal sealed class OptComposer : ILoadFileComposer
{
    // Fixed positional Opticon fields: Bates, Volume, ImagePath, DocBreak, two reserved
    // (always empty) columns, and the page count.
    private static readonly string[] OptColumns =
    {
        "Bates", "Volume", "ImagePath", "DocBreak", "Reserved1", "Reserved2", "PageCount",
    };

    private readonly FileGenerationRequest request;
    private readonly WriterMode mode;
    private readonly BatesSequence? batesSequence;

    public OptComposer(FileGenerationRequest request, WriterMode mode)
    {
        this.request = request;
        this.mode = mode;
        this.batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;
    }

    public IReadOnlyList<string> HeaderColumns => Array.Empty<string>();

    public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles)
        => this.mode == WriterMode.LoadfileOnly
            ? this.ComposeLoadfileOnly()
            : this.ComposeFromFiles(processedFiles, isProductionSet: this.mode == WriterMode.ProductionSet);

    private static LoadFileRecord MakeRecord(string bates, string volume, string imagePath, string docBreak, string pageCountStr)
        => new()
        {
            Columns = OptColumns,
            Values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Bates"] = bates,
                ["Volume"] = volume,
                ["ImagePath"] = imagePath,
                ["DocBreak"] = docBreak,
                ["Reserved1"] = string.Empty,
                ["Reserved2"] = string.Empty,
                ["PageCount"] = pageCountStr,
            },
            RecordId = bates,
        };

    private IEnumerable<LoadFileRecord> ComposeLoadfileOnly()
    {
#pragma warning disable S2245
        var random = this.request.Metadata.Seed.HasValue ? new Random(this.request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        for (long i = 1; i <= this.request.Output.FileCount; i++)
        {
            string batesNumber = this.batesSequence is not null
                ? this.batesSequence.Next().ToString()
                : $"IMG{i:D8}";
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesNumber}.tif";

            int pageCount = this.request.Tiff.PageRange.HasValue
                ? TiffMultiPageGenerator.GetPageCount(this.request.Tiff.PageRange, this.request.Metadata.Seed, i)
                : random.Next(1, 11);

            foreach (var entry in GeneratePageEntries(batesNumber, imagePath, pageCount))
            {
                yield return MakeRecord(entry.Bates, volume, entry.ImagePath, entry.DocBreak, entry.PageCountStr);
            }
        }
    }

    private IEnumerable<LoadFileRecord> ComposeFromFiles(IReadOnlyList<FileData> processedFiles, bool isProductionSet)
    {
        foreach (var fileData in processedFiles)
        {
            var workItem = fileData.WorkItem;
            string baseBatesNumber;
            if (isProductionSet)
            {
                baseBatesNumber = this.batesSequence!.Format(workItem.Index - 1).ToString();
            }
            else if (this.batesSequence is not null)
            {
                baseBatesNumber = this.batesSequence.Format(workItem.Index - 1).ToString();
            }
            else
            {
                baseBatesNumber = $"DOC{workItem.Index:D8}";
            }

            string volume = isProductionSet ? workItem.FolderName : "VOL001";
            string baseImagePath = isProductionSet
                ? workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                    .Replace(Path.GetExtension(workItem.FilePathInZip) ?? string.Empty, ".tif", StringComparison.Ordinal)
                    .Replace('/', '\\') // FilePathInZip uses '/' (ZIP spec); normalize on all platforms incl. Windows
                : $"IMAGES\\{baseBatesNumber}.tif";

            int actualPages = this.request.Tiff.ShouldIncludePageCount(this.request.Output) ? Math.Max(1, fileData.PageCount) : 1;
            bool hasAttachment = this.request.Metadata.WithFamilies && this.request.Output.IsEml && fileData.Attachment.HasValue;

            foreach (var entry in GeneratePageEntries(baseBatesNumber, baseImagePath, actualPages))
            {
                yield return MakeRecord(entry.Bates, volume, entry.ImagePath, entry.DocBreak, entry.PageCountStr);
            }

            if (hasAttachment)
            {
                string childBates = $"{baseBatesNumber}_A001";
                string childImagePath = isProductionSet
                    ? Path.Combine("IMAGES", volume, $"{childBates}.tif").Replace('/', '\\')
                    : $"IMAGES\\{childBates}.tif";
                yield return MakeRecord(childBates, volume, childImagePath, "Y", "1");
            }
        }
    }

    private static IEnumerable<(string Bates, string ImagePath, string DocBreak, string PageCountStr)> GeneratePageEntries(
        string baseBates,
        string baseImagePath,
        int actualPages)
    {
        if (actualPages > 1)
        {
            var ext = Path.GetExtension(baseImagePath) ?? string.Empty;
            var pathWithoutExt = baseImagePath[..^ext.Length];

            for (int pageIdx = 1; pageIdx <= actualPages; pageIdx++)
            {
                var pageBates = $"{baseBates}_{pageIdx:D3}";
                var pageImagePath = $"{pathWithoutExt}_{pageIdx:D3}{ext}";
                var docBreak = pageIdx == 1 ? "Y" : string.Empty;
                var pageCountStr = pageIdx == 1 ? actualPages.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

                yield return (pageBates, pageImagePath, docBreak, pageCountStr);
            }
        }
        else
        {
            yield return (baseBates, baseImagePath, "Y", "1");
        }
    }
}
