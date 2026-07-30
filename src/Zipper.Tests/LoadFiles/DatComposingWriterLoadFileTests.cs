using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Emails;
using Zipper.LoadFiles;


namespace Zipper.Tests;

public class DatComposingWriterLoadFileTests : TempDirectoryTestBase
{




    [Fact]
    public async Task LoadfileOnlyDatWriter_ProducesCorrectFormat()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 5,
                FileType = "pdf",
            },
            Metadata = new MetadataConfig { Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
        };
        var writer = new DatComposingWriter(WriterMode.LoadfileOnly);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new List<FileData>());

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(6, lines.Length);
        Assert.Contains("Control Number", lines[0], StringComparison.Ordinal);
        Assert.Contains("File Path", lines[0], StringComparison.Ordinal);
        Assert.Contains("EmailSubject", lines[0], StringComparison.Ordinal);
        Assert.Contains("ExtractedText", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadfileOnlyDatWriter_WithChaos_ProducesCorruptedLines()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 20,
                FileType = "pdf",
            },
            Metadata = new MetadataConfig { Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
        };

        var eol = "\r\n";
        long totalLines = request.Output.FileCount + 1;
        var chaos = new ChaosEngine(totalLines, "5", null, LoadFileFormat.Dat, "\u0014", "\u00fe", eol, 42);

        var writer = new DatComposingWriter(WriterMode.LoadfileOnly);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new List<FileData>(), chaos);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());

        Assert.NotEmpty(content);
        Assert.True(chaos.Anomalies.Count > 0);
    }

    [Fact]
    public async Task ProductionSetDatWriter_ProducesCorrectFormat()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 5,
                FileType = "pdf",
                OutputPath = this.TempDir,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Production = new ProductionConfig { VolumeSize = 5000 },
            Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
        };

        var files = new List<FileData>();
        for (int i = 0; i < 5; i++)
        {
            files.Add(new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = i + 1,
                    FolderNumber = 1,
                    FolderName = "VOL001",
                    FileName = $"TEST{i + 1:D8}.pdf",
                    FilePathInZip = $"NATIVES\\VOL001\\TEST{i + 1:D8}.pdf",
                },
                DataLength = 1024 * (i + 1),
            });
        }

        var writer = new DatComposingWriter(WriterMode.ProductionSet);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(6, lines.Length);
        Assert.Contains("DOCID", lines[0], StringComparison.Ordinal);
        Assert.Contains("BATES_NUMBER", lines[0], StringComparison.Ordinal);
        Assert.Contains("NATIVE_PATH", lines[0], StringComparison.Ordinal);
        Assert.Contains("IMAGE_PATH", lines[0], StringComparison.Ordinal);
        Assert.Contains("\r\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadfileOnlyDatWriter_WithNullEol_FallsBackToCrlf()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 3,
                FileType = "pdf",
            },
            Metadata = new MetadataConfig { Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = string.Empty },
        };
        var writer = new DatComposingWriter(WriterMode.LoadfileOnly);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new List<FileData>());

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("\r\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadfileOnlyDatWriter_IncludesEmailCcInHeaderAndRows()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 2, FileType = "pdf" },
            Metadata = new MetadataConfig { Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
        };
        var writer = new DatComposingWriter(WriterMode.LoadfileOnly);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new List<FileData>());

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("EmailCC", lines[0], StringComparison.Ordinal);
        Assert.Contains("cc1@example.com", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardEmlDatWriter_IncludesCcInHeaderAndRows()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 1, FileType = "eml" },
            Metadata = new MetadataConfig { Seed = 42, WithMetadata = true },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
        };
        var fileData = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem { Index = 1, FolderNumber = 1, FolderName = "001", FileName = "test1.eml", FilePathInZip = "001/test1.eml" },
                DataLength = 100,
                PageCount = 1,
                Email = new Email { To = "to@ex.com", From = "from@ex.com", Cc = "cc@ex.com", Subject = "Subj", SentDate = new DateTime(2026, 1, 1) },
            }
        };

        var writer = new DatComposingWriter(WriterMode.Standard);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, fileData);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("\u00feCC\u00fe", lines[0], StringComparison.Ordinal);
        Assert.Contains("\u00fecc@ex.com\u00fe", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionSetEmlDatWriter_IncludesEmailCcInHeaderAndRows()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 1, FileType = "eml", OutputPath = this.TempDir },
            Metadata = new MetadataConfig { Seed = 42 },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Production = new ProductionConfig { VolumeSize = 5000 },
            Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
        };
        var fileData = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem { Index = 1, FolderNumber = 1, FolderName = "VOL001", FileName = "TEST00000001.eml", FilePathInZip = "NATIVES\\VOL001\\TEST00000001.eml" },
                DataLength = 100,
                PageCount = 1,
                Email = new Email { To = "to@ex.com", From = "from@ex.com", Cc = "cc@ex.com", Subject = "Subj", SentDate = new DateTime(2026, 1, 1) },
            }
        };

        var writer = new DatComposingWriter(WriterMode.ProductionSet);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, fileData);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("\u00feEmailCC\u00fe", lines[0], StringComparison.Ordinal);
        Assert.Contains("\u00fecc@ex.com\u00fe", lines[1], StringComparison.Ordinal);
    }
}

