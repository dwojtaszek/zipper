using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class MixedFileTypeGenerationTests : IDisposable
{
    private const char Col = '\u0014';
    private readonly string tempDir;

    public MixedFileTypeGenerationTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_mixed_gen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    private static IReadOnlyList<FileTypeRatio> Ratios(params string[] types)
    {
        return types.Select(t => new FileTypeRatio { Type = t, Weight = 1 }).ToList();
    }

    private FileGenerationRequest CreateMixedRequest(long count, IReadOnlyList<FileTypeRatio> ratios, int? seed = null)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.tempDir,
                FileCount = count,
                FileType = ratios[0].Type,
                FileTypeRatios = ratios,
                FileTypePlan = new FileTypePlan(ratios, count),
                Folders = 1,
                Concurrency = 2,
            },
            Metadata = new MetadataConfig { Seed = seed },
        };
    }

    // === Standard Archive generation ===

    [Fact]
    public async Task StandardArchive_MixedTypes_ZipContainsExactPerTypeCounts()
    {
        var request = CreateMixedRequest(10, Ratios("pdf", "eml"), seed: 42);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        Assert.Equal(10, result.FilesGenerated);
        using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
        var nativeEntries = archive.Entries.Select(e => e.FullName).Where(n => !n.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && !n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(10, nativeEntries.Count);
        Assert.Equal(5, nativeEntries.Count(n => n.EndsWith(".pdf", StringComparison.Ordinal)));
        Assert.Equal(5, nativeEntries.Count(n => n.EndsWith(".eml", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StandardArchive_MixedTypes_LoadFileHasPerRecordFileTypeColumn()
    {
        var request = CreateMixedRequest(10, Ratios("pdf", "eml"), seed: 42);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var lines = (await File.ReadAllLinesAsync(result.LoadFilePath)).Where(l => l.Length > 0).ToArray();
        var header = lines[0].Split(Col);
        var fileTypeIndex = Array.FindIndex(header, h => h.Trim('þ') == "File Type");
        Assert.True(fileTypeIndex >= 0, $"File Type column missing from header: {lines[0]}");

        var rows = lines.Skip(1).Select(l => l.Split(Col)).ToArray();
        Assert.Equal(10, rows.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("PDF", rows[i][fileTypeIndex].Trim('þ'));
        }

        for (int i = 5; i < 10; i++)
        {
            Assert.Equal("EML", rows[i][fileTypeIndex].Trim('þ'));
        }
    }

    // === Standard DAT writer level ===

    [Fact]
    public async Task DatWriter_MixedTypes_EmailColumnsBlankForNonEmailRecords()
    {
        var request = CreateMixedRequest(2, Ratios("pdf", "eml"), seed: 42);
        var files = new List<FileData>
        {
            MakeFileData(1, "pdf"),
            MakeFileData(2, "eml"),
        };

        var output = await WriteDat(request, files);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(Col).Select(h => h.Trim('þ')).ToArray();

        Assert.Contains("File Type", header);
        var toIndex = Array.IndexOf(header, "To");
        Assert.True(toIndex >= 0, "Email columns should be included when eml is in the mix");

        var pdfRow = lines[1].Split(Col);
        var emlRow = lines[2].Split(Col);
        Assert.Equal(string.Empty, pdfRow[toIndex].Trim('þ'));
        Assert.Contains("@example.com", emlRow[toIndex], StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatWriter_MixedTypes_PageCountBlankForNonTiffRecords()
    {
        var ratios = Ratios("tiff", "pdf");
        var request = CreateMixedRequest(2, ratios, seed: 42);
        request.Tiff = request.Tiff with { PageRange = (1, 10) };
        var files = new List<FileData>
        {
            MakeFileData(1, "tiff", pageCount: 3),
            MakeFileData(2, "pdf"),
        };

        var output = await WriteDat(request, files);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(Col).Select(h => h.Trim('þ')).ToArray();
        var pageCountIndex = Array.IndexOf(header, "Page Count");
        Assert.True(pageCountIndex >= 0, "Page Count column should be included when tiff is in the mix");

        var tiffRow = lines[1].Split(Col);
        var pdfRow = lines[2].Split(Col);
        Assert.Equal("3", tiffRow[pageCountIndex].Trim('þ'));
        Assert.Equal(string.Empty, pdfRow[pageCountIndex].Trim('þ'));
    }

    [Fact]
    public async Task DatWriter_MixedTypes_FamiliesAttachOnlyToEmailRecords()
    {
        var request = CreateMixedRequest(2, Ratios("pdf", "eml"), seed: 42);
        request.Metadata = request.Metadata with { WithFamilies = true };
        request.LoadFile = request.LoadFile with { AttachmentRate = 100 };
        var files = new List<FileData>
        {
            MakeFileData(1, "pdf"),
            MakeFileData(2, "eml", attachment: ("report.pdf", new byte[] { 1, 2, 3 })),
        };

        var output = await WriteDat(request, files);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // 1 header + 2 parents + 1 child (only the eml record has an Attachment)
        Assert.Equal(4, lines.Length);
        Assert.Contains("_A001", lines[3], StringComparison.Ordinal);
    }

    // === Production Set generation ===

    [Fact]
    public async Task ProductionSet_MixedTypes_NativesAndDatReflectPerRecordType()
    {
        var ratios = Ratios("pdf", "eml");
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.tempDir,
                FileCount = 10,
                FileType = "pdf",
                FileTypeRatios = ratios,
                FileTypePlan = new FileTypePlan(ratios, 10),
            },
            Production = new ProductionConfig { ProductionSet = true, VolumeSize = 5000 },
            Metadata = new MetadataConfig { Seed = 42 },
            Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
        };

        var result = await ProductionSetGenerator.GenerateAsync(request);

        var natives = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.*", SearchOption.AllDirectories);
        Assert.Equal(10, natives.Length);
        Assert.Equal(5, natives.Count(n => n.EndsWith(".pdf", StringComparison.Ordinal)));
        Assert.Equal(5, natives.Count(n => n.EndsWith(".eml", StringComparison.Ordinal)));

        var datLines = (await File.ReadAllLinesAsync(result.DatFilePath)).Where(l => l.Length > 0).ToArray();
        var header = datLines[0].Split(Col).Select(h => h.Trim('þ')).ToArray();
        var fileTypeIndex = Array.IndexOf(header, "FILE_TYPE");
        Assert.True(fileTypeIndex >= 0);

        var rows = datLines.Skip(1).Select(l => l.Split(Col)).ToArray();
        Assert.Equal(10, rows.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("PDF", rows[i][fileTypeIndex].Trim('þ'));
        }

        for (int i = 5; i < 10; i++)
        {
            Assert.Equal("EML", rows[i][fileTypeIndex].Trim('þ'));
        }
    }

    // === CSV / Concordance / OPT / EDRM-XML per-record gating ===

    [Fact]
    public async Task CsvWriter_MixedTypes_FileTypeColumnPerRecord()
    {
        var request = CreateMixedRequest(2, Ratios("pdf", "eml"), seed: 42);
        var files = new List<FileData>
        {
            MakeFileData(1, "pdf"),
            MakeFileData(2, "eml"),
        };

        using var stream = new MemoryStream();
        await new CsvComposingWriter().WriteAsync(stream, request, files);
        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var header = lines[0].Split(',').Select(h => h.Trim('"', '\r')).ToArray();
        var fileTypeIndex = Array.FindIndex(header, h => string.Equals(h, "File Type", StringComparison.OrdinalIgnoreCase));
        Assert.True(fileTypeIndex >= 0, $"File Type column missing from CSV header: {lines[0]}");

        Assert.Equal("PDF", lines[1].Split(',')[fileTypeIndex].Trim('"', '\r'));
        Assert.Equal("EML", lines[2].Split(',')[fileTypeIndex].Trim('"', '\r'));
    }

    [Fact]
    public async Task ConcordanceWriter_MixedTypes_FileTypeColumnPerRecord()
    {
        var request = CreateMixedRequest(2, Ratios("pdf", "eml"), seed: 42);
        var files = new List<FileData>
        {
            MakeFileData(1, "pdf"),
            MakeFileData(2, "eml"),
        };

        using var stream = new MemoryStream();
        await new ConcordanceComposingWriter().WriteAsync(stream, request, files);
        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        var header = lines[0].Split(Col).Select(h => h.Trim('þ')).ToArray();
        var fileTypeIndex = Array.IndexOf(header, "FILE_TYPE");
        Assert.True(fileTypeIndex >= 0, $"FILE_TYPE column missing from Concordance header: {lines[0]}");

        Assert.Equal("PDF", lines[1].Split(Col)[fileTypeIndex].Trim('þ'));
        Assert.Equal("EML", lines[2].Split(Col)[fileTypeIndex].Trim('þ'));
    }

    [Fact]
    public async Task OptWriter_MixedTypes_PageExpansionOnlyForTiffRecords()
    {
        var ratios = Ratios("tiff", "pdf");
        var request = CreateMixedRequest(2, ratios, seed: 42);
        request.Tiff = request.Tiff with { PageRange = (1, 10) };
        var files = new List<FileData>
        {
            MakeFileData(1, "tiff", pageCount: 3),
            MakeFileData(2, "pdf"),
        };

        using var stream = new MemoryStream();
        await new OptComposingWriter(WriterMode.Standard).WriteAsync(stream, request, files);
        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // 3 page lines for the TIFF record (DOC00000001_001.._003), 1 line for the PDF record.
        // OPT image paths always use .tif, so expansion is observed via Bates prefixes.
        Assert.Equal(4, lines.Length);
        Assert.Equal(3, lines.Count(l => l.StartsWith("DOC00000001", StringComparison.Ordinal)));
        Assert.Single(lines, l => l.StartsWith("DOC00000002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task XmlWriter_MixedTypes_EmailTagsBlankForNonEmailRecords()
    {
        var request = CreateMixedRequest(2, Ratios("pdf", "eml"), seed: 42);
        var files = new List<FileData>
        {
            MakeFileData(1, "pdf"),
            MakeFileData(2, "eml"),
        };

        using var stream = new MemoryStream();
        await new XmlLoadFileWriter().WriteAsync(stream, request, files);

        stream.Position = 0;
        var doc = System.Xml.Linq.XDocument.Load(stream);
        var documents = doc.Root!.Element("Batch")!.Elements("Document").ToArray();
        Assert.Equal(2, documents.Length);

        string? ToTag(System.Xml.Linq.XElement document) => document
            .Element("Tags")!
            .Elements("Tag")
            .First(t => t.Attribute("TagName")?.Value == "To")
            .Attribute("TagValue")?.Value;

        Assert.Equal(string.Empty, ToTag(documents[0]));
        Assert.Contains("@", ToTag(documents[1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task XmlWriter_MixedTypes_PageCountBlankForNonTiffRecords()
    {
        var ratios = Ratios("tiff", "pdf");
        var request = CreateMixedRequest(2, ratios, seed: 42);
        request.Tiff = request.Tiff with { PageRange = (1, 10) };
        var files = new List<FileData>
        {
            MakeFileData(1, "tiff", pageCount: 3),
            MakeFileData(2, "pdf"),
        };

        using var stream = new MemoryStream();
        await new XmlLoadFileWriter().WriteAsync(stream, request, files);

        stream.Position = 0;
        var doc = System.Xml.Linq.XDocument.Load(stream);
        var documents = doc.Root!.Element("Batch")!.Elements("Document").ToArray();
        Assert.Equal(2, documents.Length);

        string? PageCountTag(System.Xml.Linq.XElement document) => document
            .Element("Tags")!
            .Elements("Tag")
            .First(t => t.Attribute("TagName")?.Value == "PageCount")
            .Attribute("TagValue")?.Value;

        Assert.Equal("3", PageCountTag(documents[0]));
        Assert.Equal(string.Empty, PageCountTag(documents[1]));
    }

    private static FileData MakeFileData(long index, string fileType, int pageCount = 1, (string filename, byte[] content)? attachment = null)
    {
        return new FileData
        {
            WorkItem = new FileWorkItem
            {
                Index = index,
                FolderNumber = 1,
                FolderName = "folder_001",
                FileName = $"{index:D8}.{fileType}",
                FilePathInZip = $"folder_001/{index:D8}.{fileType}",
                FileType = fileType,
            },
            Data = Encoding.UTF8.GetBytes("content"),
            DataLength = 7,
            PageCount = pageCount,
            Attachment = attachment,
        };
    }

    private static async Task<string> WriteDat(FileGenerationRequest request, List<FileData> files)
    {
        using var stream = new MemoryStream();
        var writer = new DatComposingWriter();
        await writer.WriteAsync(stream, request, files).ConfigureAwait(false);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
