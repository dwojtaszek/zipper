using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class DatLoadFileWriterTests : TempDirectoryTestBase
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
        Assert.Contains("Control Number", lines[0]);
        Assert.Contains("File Path", lines[0]);
        Assert.Contains("EmailSubject", lines[0]);
        Assert.Contains("ExtractedText", lines[0]);
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
        Assert.Contains("DOCID", lines[0]);
        Assert.Contains("BATES_NUMBER", lines[0]);
        Assert.Contains("NATIVE_PATH", lines[0]);
        Assert.Contains("IMAGE_PATH", lines[0]);
        Assert.Contains("\r\n", content);
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

        Assert.Contains("\r\n", content);
    }








}
