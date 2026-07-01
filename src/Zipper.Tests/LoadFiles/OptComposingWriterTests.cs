using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

public class OptComposingWriterTests : TempDirectoryTestBase
{
    [Fact]
    public void OptWriter_FormatName_ReturnsExpectedValuePerMode()
    {
        var standard = new OptComposingWriter(WriterMode.Standard);
        Assert.Equal("OPT", standard.FormatName);

        var loadfileOnly = new OptComposingWriter(WriterMode.LoadfileOnly);
        Assert.Equal("OPT (Image)", loadfileOnly.FormatName);

        var productionSet = new OptComposingWriter(WriterMode.ProductionSet);
        Assert.Equal("Production Set OPT", productionSet.FormatName);
    }

    [Fact]
    public async Task WriteAsync_StandardMode_WritesTiffPages()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "tiff" },
            LoadFile = new LoadFileConfig { Encoding = "ANSI" },
            Metadata = new MetadataConfig { WithMetadata = false },
            Tiff = new TiffConfig { PageRange = (1, 10) }
        };

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FileName = "doc1.tif",
                    FilePathInZip = "IMAGES\\doc1.tif"
                },
                PageCount = 2
            }
        };

        var writer = new OptComposingWriter(WriterMode.Standard);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("DOC00000001_001,VOL001,IMAGES\\DOC00000001_001.tif,Y", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("DOC00000001_002,VOL001,IMAGES\\DOC00000001_002.tif,", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_LoadFileOnlyMode_GeneratesImages()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 3, FileType = "tiff" },
            LoadFile = new LoadFileConfig { Encoding = "ANSI" },
            Metadata = new MetadataConfig { Seed = 42 },
            Tiff = new TiffConfig { PageRange = (1, 10) }
        };

        var writer = new OptComposingWriter(WriterMode.LoadfileOnly);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new List<FileData>());

        var content = Encoding.UTF8.GetString(stream.ToArray());
        Assert.NotEmpty(content);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 3);
    }
}
