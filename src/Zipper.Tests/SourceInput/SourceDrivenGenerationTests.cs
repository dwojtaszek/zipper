using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;
using Zipper.SourceInput;

namespace Zipper.Tests;

public class SourceDrivenGenerationTests : IDisposable
{
    private const char Col = '\u0014';
    private readonly string tempDir;

    public SourceDrivenGenerationTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_source_gen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    private FileGenerationRequest CreateSourceRequest(IReadOnlyList<SourceRecord> rows, bool withText = false, bool withMetadata = false, ColumnProfile? profile = null, BatesNumberConfig? bates = null, int? seed = 42)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.tempDir,
                FileCount = rows.Count,
                FileType = rows[0].FileType,
                SourceFileTypes = rows.Select(r => r.FileType).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList(),
                Folders = 1,
                Concurrency = 2,
                WithText = withText,
            },
            Metadata = new MetadataConfig { Seed = seed, WithMetadata = withMetadata, ColumnProfile = profile },
            Bates = bates,
            SourceRecords = rows,
        };
    }

    private static SourceRecord Row(string path, string type, string? control = null, string? bates = null, IReadOnlyDictionary<string, string>? metadata = null)
        => new()
        {
            RelativePath = path,
            FileType = type,
            ControlNumber = control,
            BatesNumber = bates,
            Metadata = metadata,
        };

    private static List<string[]> DatRows(string loadFilePath, out string[] header)
    {
        var lines = File.ReadAllLines(loadFilePath).Where(l => l.Length > 0).ToArray();
        header = lines[0].Split(Col).Select(h => h.Trim('þ')).ToArray();
        return lines.Skip(1).Select(l => l.Split(Col)).ToList();
    }

    [Fact]
    public async Task StandardArchive_SourceRows_ZipEntriesAndDatPathsMatchRelativePaths()
    {
        var rows = new[]
        {
            Row("docs/a.pdf", "pdf", control: "CTRL-001"),
            Row("b.eml", "eml", control: "CTRL-002"),
            Row("deep/nested/c.tiff", "tiff", control: "CTRL-003"),
        };
        var request = this.CreateSourceRequest(rows);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        Assert.Equal(3, result.FilesGenerated);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("docs/a.pdf", names);
            Assert.Contains("b.eml", names);
            Assert.Contains("deep/nested/c.tiff", names);
        }

        var datRows = DatRows(result.LoadFilePath, out var header);
        Assert.Equal(3, datRows.Count);
        var controlIndex = Array.IndexOf(header, "Control Number");
        var pathIndex = Array.IndexOf(header, "File Path");
        Assert.Equal("CTRL-001", datRows[0][controlIndex].Trim('þ'));
        Assert.Equal("docs/a.pdf", datRows[0][pathIndex].Trim('þ'));
        Assert.Equal("CTRL-002", datRows[1][controlIndex].Trim('þ'));
        Assert.Equal("b.eml", datRows[1][pathIndex].Trim('þ'));
        Assert.Equal("CTRL-003", datRows[2][controlIndex].Trim('þ'));
        Assert.Equal("deep/nested/c.tiff", datRows[2][pathIndex].Trim('þ'));
    }

    [Fact]
    public async Task StandardArchive_SourceMix_FileTypeColumnAndEmailGatingPerRecord()
    {
        var rows = new[] { Row("a.pdf", "pdf"), Row("b.eml", "eml") };
        var request = this.CreateSourceRequest(rows);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        var typeIndex = Array.IndexOf(header, "File Type");
        var toIndex = Array.IndexOf(header, "To");
        Assert.True(typeIndex >= 0, $"File Type column missing: {string.Join("|", header)}");
        Assert.True(toIndex >= 0, "Email columns should be present when eml participates");
        Assert.Equal("PDF", datRows[0][typeIndex].Trim('þ'));
        Assert.Equal(string.Empty, datRows[0][toIndex].Trim('þ'));
        Assert.Equal("EML", datRows[1][typeIndex].Trim('þ'));
        Assert.Contains("@", datRows[1][toIndex], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardArchive_SourceBates_BatesColumnUsesRowOverride()
    {
        var rows = new[]
        {
            Row("a.pdf", "pdf", bates: "ABC_00000099"),
            Row("b.pdf", "pdf"),
        };
        var request = this.CreateSourceRequest(rows, bates: new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 8 });

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        var batesIndex = Array.IndexOf(header, "Bates Number");
        Assert.True(batesIndex >= 0, "Bates Number column missing");
        Assert.Equal("ABC_00000099", datRows[0][batesIndex].Trim('þ'));
        Assert.Equal("ABC00000002", datRows[1][batesIndex].Trim('þ'));
    }

    [Fact]
    public async Task StandardArchive_RootLevelRowWithText_TextEntryBesideNative()
    {
        var rows = new[] { Row("root.pdf", "pdf") };
        var request = this.CreateSourceRequest(rows, withText: true);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("root.pdf", names);
        Assert.Contains("root.txt", names);
        Assert.DoesNotContain(names, n => n.StartsWith("/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StandardArchive_ProfileMapsSourceMetadataIntoDatColumns()
    {
        var profile = new ColumnProfile
        {
            Name = "test",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "text", Required = true },
                new() { Name = "FILEPATH", Type = "text", Required = true },
                new() { Name = "CUSTODIAN", Type = "text", Required = true },
                new() { Name = "REVIEWED", Type = "text", Required = true },
            },
        };
        var rows = new[]
        {
            Row("docs/a.pdf", "pdf", control: "CTRL-100", metadata: new Dictionary<string, string>
            {
                ["Custodian"] = "source-custodian",
                ["Reviewed"] = "yes-source",
            }),
        };
        var request = this.CreateSourceRequest(rows, profile: profile);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        Assert.Single(datRows);
        Assert.Equal("CTRL-100", datRows[0][Array.IndexOf(header, "DOCID")].Trim('þ'));
        Assert.Equal("docs/a.pdf", datRows[0][Array.IndexOf(header, "FILEPATH")].Trim('þ'));
        Assert.Equal("source-custodian", datRows[0][Array.IndexOf(header, "CUSTODIAN")].Trim('þ'));
        Assert.Equal("yes-source", datRows[0][Array.IndexOf(header, "REVIEWED")].Trim('þ'));
    }

    [Fact]
    public async Task LoadfileOnly_SourceRows_EmitsSourceRecordsWithoutArchive()
    {
        var rows = new[]
        {
            Row("docs/a.pdf", "pdf", control: "CTRL-201"),
            Row("b.eml", "eml", control: "CTRL-202"),
        };
        var request = this.CreateSourceRequest(rows);
        request.LoadfileOnly = true;

        var result = await LoadFileOnlyGenerator.GenerateAsync(request);

        Assert.Equal(2, result.TotalRecords);
        Assert.Empty(Directory.GetFiles(this.tempDir, "*.zip"));
        var datRows = DatRows(result.LoadFilePath, out var header);
        Assert.Equal(2, datRows.Count);
        var controlIndex = Array.IndexOf(header, "Control Number");
        var pathIndex = Array.IndexOf(header, "File Path");
        Assert.Equal("CTRL-201", datRows[0][controlIndex].Trim('þ'));
        Assert.Equal("docs/a.pdf", datRows[0][pathIndex].Trim('þ'));
        Assert.Equal("CTRL-202", datRows[1][controlIndex].Trim('þ'));
        Assert.Equal("b.eml", datRows[1][pathIndex].Trim('þ'));
    }

    [Fact]
    public async Task LoadfileOnly_SourceMix_FileTypeColumnPresentPerRecord()
    {
        var rows = new[] { Row("a.pdf", "pdf"), Row("b.eml", "eml") };
        var request = this.CreateSourceRequest(rows);
        request.LoadfileOnly = true;

        var result = await LoadFileOnlyGenerator.GenerateAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        var typeIndex = Array.IndexOf(header, "File Type");
        Assert.True(typeIndex >= 0, $"File Type column missing: {string.Join("|", header)}");
        Assert.Equal("PDF", datRows[0][typeIndex].Trim('þ'));
        Assert.Equal("EML", datRows[1][typeIndex].Trim('þ'));
    }

    [Fact]
    public async Task OptWriter_SourceRows_BatesOverrideAndPaths()
    {
        var rows = new[]
        {
            Row("a.tiff", "tiff", bates: "ABC_00000009"),
            Row("b.jpg", "jpg"),
        };
        var request = this.CreateSourceRequest(rows, bates: new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 8 });
        request.Tiff = request.Tiff with { PageRange = (2, 2) };
        var shells = rows.Select((r, i) => new FileData
        {
            WorkItem = r.ToWorkItem(i + 1),
            DataLength = 2048,
            PageCount = 2,
        }).ToList();

        using var stream = new MemoryStream();
        await new OptComposingWriter(WriterMode.Standard).WriteAsync(stream, request, shells);

        var lines = Encoding.UTF8.GetString(stream.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
        Assert.Equal(3, lines.Length); // tiff row expands to 2 pages, jpg to 1
        Assert.Contains("ABC_00000009", lines[0], StringComparison.Ordinal);
        Assert.Contains("ABC_00000009_002", lines[1], StringComparison.Ordinal);
        Assert.Contains("ABC00000002", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void FileDataSerializer_SourceWorkItemFields_RoundTrip()
    {
        using var list = new DiskBackedFileDataList();
        var workItem = new FileWorkItem
        {
            Index = 7,
            FolderNumber = 1,
            FolderName = "docs",
            FileName = "a.pdf",
            FilePathInZip = "docs/a.pdf",
            FileType = "pdf",
            ControlNumberOverride = "CTRL-777",
            BatesNumberOverride = "ABC_00000777",
            SourceMetadata = new Dictionary<string, string> { ["Custodian"] = "jsmith" },
        };
        list.Add(new FileData { WorkItem = workItem, DataLength = 10, PageCount = 1 });

        var restored = list.Single();

        Assert.Equal("CTRL-777", restored.WorkItem.ControlNumberOverride);
        Assert.Equal("ABC_00000777", restored.WorkItem.BatesNumberOverride);
        Assert.NotNull(restored.WorkItem.SourceMetadata);
        Assert.Equal("jsmith", restored.WorkItem.SourceMetadata!["Custodian"]);
    }

    [Fact]
    public void FileDataSerializer_LegacyWorkItemFields_NullOverrides()
    {
        using var list = new DiskBackedFileDataList();
        var workItem = new FileWorkItem
        {
            Index = 1,
            FolderNumber = 1,
            FolderName = "folder_001",
            FileName = "00000001.pdf",
            FilePathInZip = "folder_001/00000001.pdf",
        };
        list.Add(new FileData { WorkItem = workItem, DataLength = 10, PageCount = 1 });

        var restored = list.Single();

        Assert.Null(restored.WorkItem.ControlNumberOverride);
        Assert.Null(restored.WorkItem.BatesNumberOverride);
        Assert.Null(restored.WorkItem.SourceMetadata);
    }

    [Fact]
    public async Task StandardArchive_DirectoryTemplate_NestedPathsRecreated()
    {
        var template = Path.Combine(this.tempDir, "tpl");
        Directory.CreateDirectory(Path.Combine(template, "folder_a", "deep"));
        File.WriteAllText(Path.Combine(template, "root.pdf"), "x");
        File.WriteAllText(Path.Combine(template, "folder_a", "inner.eml"), "x");
        File.WriteAllText(Path.Combine(template, "folder_a", "deep", "x.tiff"), "x");

        var readOk = DirectoryTemplateReader.TryRead(template, out var rows, out var readError);
        Assert.True(readOk, readError);
        var request = this.CreateSourceRequest(rows);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        Assert.Equal(3, result.FilesGenerated);
        using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("root.pdf", names);
        Assert.Contains("folder_a/inner.eml", names);
        Assert.Contains("folder_a/deep/x.tiff", names);

        var datRows = DatRows(result.LoadFilePath, out _);
        Assert.Equal(3, datRows.Count);
    }

    [Fact]
    public async Task StandardArchive_AliasUppercaseAndFolderExtensions_TextEntriesDerivedFromRealExtension()
    {
        var rows = new[]
        {
            Row("scan.tif", "tiff"),
            Row("photo.jpeg", "jpg"),
            Row("REPORT.PDF", "pdf"),
            Row("sub.tiff/x.tiff", "tiff"),
        };
        var request = this.CreateSourceRequest(rows, withText: true);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        using (var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("scan.txt", names);
            Assert.Contains("photo.txt", names);
            Assert.Contains("REPORT.txt", names);
            Assert.Contains("sub.tiff/x.txt", names);
        }

        var datRows = DatRows(result.LoadFilePath, out var header);
        var textIndex = Array.IndexOf(header, "Extracted Text");
        Assert.True(textIndex >= 0, "Extracted Text column missing");
        Assert.Equal("scan.txt", datRows[0][textIndex].Trim('þ'));
        Assert.Equal("photo.txt", datRows[1][textIndex].Trim('þ'));
        Assert.Equal("REPORT.txt", datRows[2][textIndex].Trim('þ'));
        Assert.Equal("sub.tiff/x.txt", datRows[3][textIndex].Trim('þ'));
    }

    [Fact]
    public async Task StandardArchive_ControlOverrideWithFamilies_BegAttachMatchesControlNumber()
    {
        var rows = new[]
        {
            Row("a.eml", "eml", control: "CTRL-500"),
            Row("b.pdf", "pdf", control: "CTRL-501"),
        };
        var request = this.CreateSourceRequest(rows);
        request.Metadata = request.Metadata with { WithFamilies = true };
        request.LoadFile = request.LoadFile with { AttachmentRate = 100 };

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        var begIndex = Array.IndexOf(header, "BEGATTACH");
        var controlIndex = Array.IndexOf(header, "Control Number");
        Assert.True(begIndex >= 0, "BEGATTACH column missing");
        Assert.Equal("CTRL-500", datRows[0][controlIndex].Trim('þ'));
        Assert.Equal("CTRL-500", datRows[0][begIndex].Trim('þ'));

        // The child row's Control Number must match the parent's ENDATTACH reference.
        var endIndex = Array.IndexOf(header, "ENDATTACH");
        var parentEnd = datRows[0][endIndex].Trim('þ');
        var childRow = datRows.Skip(1).First(r => r[controlIndex].Trim('þ').EndsWith("_A001", StringComparison.Ordinal));
        Assert.Equal(parentEnd, childRow[controlIndex].Trim('þ'));
        Assert.Equal("CTRL-500_A001", parentEnd);
    }

    [Fact]
    public async Task StandardArchive_ProfileSourceMetadata_NonAlphanumericKeyMatch()
    {
        var profile = new ColumnProfile
        {
            Name = "test",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "text", Required = true },
                new() { Name = "FILEPATH", Type = "text", Required = true },
                new() { Name = "DATESENT", Type = "text", Required = true },
            },
        };
        var rows = new[]
        {
            Row("a.pdf", "pdf", metadata: new Dictionary<string, string> { ["Date Sent"] = "2020-01-02" }),
        };
        var request = this.CreateSourceRequest(rows, profile: profile);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        Assert.Equal("2020-01-02", datRows[0][Array.IndexOf(header, "DATESENT")].Trim('þ'));
    }

    [Fact]
    public async Task StandardArchive_ProfileWithBothOverrides_BatesColumnsUseBatesIdentity()
    {
        var profile = new ColumnProfile
        {
            Name = "test",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "text", Required = true },
                new() { Name = "FILEPATH", Type = "text", Required = true },
                new() { Name = "BEGBATES", Type = "text", Required = true },
            },
        };
        var rows = new[]
        {
            Row("a.pdf", "pdf", control: "CTRL-900", bates: "ABC_00000042"),
        };
        var request = this.CreateSourceRequest(rows, profile: profile, bates: new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 8 });

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        Assert.Equal("CTRL-900", datRows[0][Array.IndexOf(header, "DOCID")].Trim('þ'));
        Assert.Equal("ABC_00000042", datRows[0][Array.IndexOf(header, "BEGBATES")].Trim('þ'));
    }

    [Fact]
    public async Task LoadfileOnly_SourceTiffRows_OptExpandsSyntheticPageCounts()
    {
        var rows = new[]
        {
            Row("a.tiff", "tiff"),
            Row("b.tiff", "tiff"),
            Row("c.tiff", "tiff"),
        };
        var request = this.CreateSourceRequest(rows);
        request.LoadfileOnly = true;
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Opt } };

        var result = await LoadFileOnlyGenerator.GenerateAsync(request);

        var lines = File.ReadAllLines(result.LoadFilePath);
        Assert.True(lines.Length > 3, $"Expected synthetic page expansion, got {lines.Length} OPT lines for 3 records");
    }

    [Fact]
    public async Task XmlWriter_RootLevelSourceEml_ChildPathsHaveNoLeadingSlash()
    {
        var rows = new[]
        {
            Row("root.eml", "eml", control: "CTRL-700"),
        };
        var request = this.CreateSourceRequest(rows);
        request.Metadata = request.Metadata with { WithFamilies = true };
        request.LoadFile = request.LoadFile with { AttachmentRate = 100 };
        var shells = rows.Select((r, i) => new FileData
        {
            WorkItem = r.ToWorkItem(i + 1),
            DataLength = 2048,
            PageCount = 1,
            Attachment = ("attach.pdf", new byte[] { 1, 2, 3 }),
        }).ToList();

        using var stream = new MemoryStream();
        await new XmlLoadFileWriter().WriteAsync(stream, request, shells);

        var xml = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("1_attach.pdf", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("/1_attach.pdf", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardArchive_SourceMetadataWithoutExplicitProfile_NotMerged()
    {
        var rows = new[]
        {
            Row("a.eml", "eml", metadata: new Dictionary<string, string> { ["Custodian"] = "source-custodian" }),
        };
        var request = this.CreateSourceRequest(rows);

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        var datRows = DatRows(result.LoadFilePath, out var header);
        var custodianIndex = Array.IndexOf(header, "Custodian");
        Assert.True(custodianIndex >= 0, "Custodian column missing (eml record)");
        var custodian = datRows[0][custodianIndex].Trim('þ');
        Assert.NotEqual("source-custodian", custodian);
        Assert.NotEmpty(custodian);
    }
}
