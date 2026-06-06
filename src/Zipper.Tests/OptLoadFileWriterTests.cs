using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class OptLoadFileWriterTests : TempDirectoryTestBase
{
    [Fact]
    public async Task OptWriter_WithMultiPageTiff_ProducesCorrectPageLevelSuffixesAndDocumentBreaks()
    {
        var request = this.CreateTestRequest("tiff");
        request.Tiff = request.Tiff with { PageRange = (1, 10) };

        var files = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FilePathInZip = "folder_001/file_00000001.tiff",
                    },
                    PageCount = 3,
                },
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 2,
                        FolderNumber = 1,
                        FilePathInZip = "folder_001/file_00000002.tiff",
                    },
                    PageCount = 1,
                }
            };

        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
        var outputPath = Path.Combine(this.TempDir, "test_multipage.opt");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, files);
        }

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);

        var parts1 = lines[0].Split(',');
        Assert.Equal("DOC00000001_001", parts1[0]);
        Assert.Equal("VOL001", parts1[1]);
        Assert.Equal("IMAGES\\DOC00000001_001.tif", parts1[2]);
        Assert.Equal("Y", parts1[3]);
        Assert.Equal("3", parts1[6]);

        var parts2 = lines[1].Split(',');
        Assert.Equal("DOC00000001_002", parts2[0]);
        Assert.Equal("VOL001", parts2[1]);
        Assert.Equal("IMAGES\\DOC00000001_002.tif", parts2[2]);
        Assert.Equal(string.Empty, parts2[3]);
        Assert.Equal(string.Empty, parts2[6]);

        var parts3 = lines[2].Split(',');
        Assert.Equal("DOC00000001_003", parts3[0]);
        Assert.Equal("VOL001", parts3[1]);
        Assert.Equal("IMAGES\\DOC00000001_003.tif", parts3[2]);
        Assert.Equal(string.Empty, parts3[3]);
        Assert.Equal(string.Empty, parts3[6]);

        var parts4 = lines[3].Split(',');
        Assert.Equal("DOC00000002", parts4[0]);
        Assert.Equal("VOL001", parts4[1]);
        Assert.Equal("IMAGES\\DOC00000002.tif", parts4[2]);
        Assert.Equal("Y", parts4[3]);
        Assert.Equal("1", parts4[6]);
    }

    [Fact]
    public async Task OptWriter_ShouldWriteCommaDelimitedNoHeaderFormat()
    {
        var request = this.CreateTestRequest();
        var fileData = this.CreateTestFileData();
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
        var outputPath = Path.Combine(this.TempDir, "test.opt");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Contains(',', lines[0]);
        Assert.DoesNotContain('\t', lines[0]);
        Assert.DoesNotContain("Control Number", lines[0]);
        Assert.Equal(6, lines[0].Count(c => c == ','));
    }

    [Theory]
    [InlineData("UTF-16")]
    [InlineData("ANSI")]
    public async Task OptWriter_ShouldRespectRequestEncoding(string encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var request = this.CreateTestRequest();
        request.LoadFile = request.LoadFile with { Encoding = encoding };
        var fileData = this.CreateTestFileData();
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
        var outputPath = Path.Combine(this.TempDir, "test.opt");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, fileData);
        }

        var targetEncoding = encoding.ToUpperInvariant() switch
        {
            "UTF-16" => Encoding.Unicode,
            "ANSI" => Encoding.GetEncoding("Windows-1252"),
            _ => Encoding.UTF8,
        };

        var content = await File.ReadAllTextAsync(outputPath, targetEncoding);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Contains(',', lines[0]);
        Assert.Equal(6, lines[0].Count(c => c == ','));
    }

    [Fact]
    public async Task OptWriter_WithDefaultSettings_ShouldUseAnsiDefault()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var request = this.CreateTestRequest();
        request.LoadFile = request.LoadFile with { Encoding = "UTF-8", IsEncodingExplicit = false };
        var fileData = this.CreateTestFileData();

        var optWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
        var datWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);

        var optOutputPath = Path.Combine(this.TempDir, "default_test.opt");
        var datOutputPath = Path.Combine(this.TempDir, "default_test.dat");

        await using (var stream = File.OpenWrite(optOutputPath))
        {
            await optWriter.WriteAsync(stream, request, fileData);
        }

        await using (var stream = File.OpenWrite(datOutputPath))
        {
            await datWriter.WriteAsync(stream, request, fileData);
        }

        var optTargetEncoding = Encoding.GetEncoding("Windows-1252");
        var optContentWithAnsi = await File.ReadAllTextAsync(optOutputPath, optTargetEncoding);

        Assert.NotEmpty(optContentWithAnsi);
        Assert.Contains(",", optContentWithAnsi);

        var datContentWithUtf8 = await File.ReadAllTextAsync(datOutputPath, Encoding.UTF8);
        Assert.NotEmpty(datContentWithUtf8);

        var optAuditJson = LoadfileAuditWriter.GenerateAuditJson(optOutputPath, request, fileData.Count, null, LoadFileFormat.Opt);
        Assert.Contains("\"encoding\": \"ANSI\"", optAuditJson);

        var datAuditJson = LoadfileAuditWriter.GenerateAuditJson(datOutputPath, request, fileData.Count, null, LoadFileFormat.Dat);
        Assert.Contains("\"encoding\": \"UTF-8\"", datAuditJson);
    }

    [Fact]
    public async Task LoadfileOnlyOptWriter_ProducesCorrectFormat()
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
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
        };
        var writer = new OptComposingWriter(WriterMode.LoadfileOnly);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new List<FileData>());

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(16, lines.Length);
        foreach (var line in lines)
        {
            int commaCount = line.Count(c => c == ',');
            Assert.Equal(6, commaCount);
            Assert.StartsWith("IMG", line);
            Assert.Contains("VOL001", line);
        }
    }

    [Fact]
    public async Task ProductionSetOptWriter_ProducesCorrectFormat()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = 3,
                FileType = "pdf",
                OutputPath = this.TempDir,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Production = new ProductionConfig { VolumeSize = 10 },
            Bates = new BatesNumberConfig { Prefix = "PROD", Start = 1, Digits = 6 },
        };

        var files = new List<FileData>();
        for (int i = 0; i < 3; i++)
        {
            files.Add(new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = i + 1,
                    FolderNumber = 1,
                    FolderName = "VOL001",
                    FileName = $"PROD{i + 1:D6}.pdf",
                    FilePathInZip = $"NATIVES\\VOL001\\PROD{i + 1:D6}.pdf",
                },
                DataLength = 1024,
            });
        }

        var writer = new OptComposingWriter(WriterMode.ProductionSet);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        foreach (var line in lines)
        {
            Assert.StartsWith("PROD", line);
            Assert.Contains(".tif", line);
            Assert.Contains("VOL001", line);
        }

        Assert.Contains("\r\n", content);
    }

    [Fact]
    public async Task OptWriter_WithoutEncodingSpecified_ShouldDefaultToWindows1252()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var request = this.CreateTestRequest();
        request.Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 };
        request.LoadFile = request.LoadFile with { Encoding = "UTF-8", IsEncodingExplicit = false };

        var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FolderName = "VOL001",
                        FileName = "TEST_ü.tif",
                        FilePathInZip = "NATIVES\\VOL001\\TEST_ü.tif"
                    },
                    PageCount = 1
                }
            };

        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt, WriterMode.ProductionSet);
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, request, fileData);

        var bytes = stream.ToArray();
        var decodedWithAnsi = Encoding.GetEncoding(1252).GetString(bytes);
        var decodedWithUtf8 = Encoding.UTF8.GetString(bytes);

        Assert.Contains("TEST_ü", decodedWithAnsi);
        Assert.DoesNotContain("TEST_ü", decodedWithUtf8);
    }

    [Fact]
    public async Task OptWriter_WithExplicitUtf8Encoding_ShouldUseUtf8()
    {
        var request = this.CreateTestRequest();
        request.Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 };
        request.LoadFile = request.LoadFile with { Encoding = "UTF-8", IsEncodingExplicit = true };

        var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FolderName = "VOL001",
                        FileName = "TEST_ü.tif",
                        FilePathInZip = "NATIVES\\VOL001\\TEST_ü.tif"
                    },
                    PageCount = 1
                }
            };

        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt, WriterMode.ProductionSet);
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, request, fileData);

        var bytes = stream.ToArray();
        var decodedWithUtf8 = Encoding.UTF8.GetString(bytes);

        Assert.Contains("TEST_ü", decodedWithUtf8);
    }

    [Fact]
    public async Task OptWriter_WithImplicitDefaultUtf8Alias_ShouldDefaultToWindows1252()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var request = this.CreateTestRequest();
        request.Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 };
        request.LoadFile = request.LoadFile with { IsEncodingExplicit = false };

        var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FolderName = "VOL001",
                        FileName = "TEST_ü.tif",
                        FilePathInZip = "NATIVES\\VOL001\\TEST_ü.tif"
                    },
                    PageCount = 1
                }
            };

        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt, WriterMode.ProductionSet);
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, request, fileData);

        var bytes = stream.ToArray();
        var decodedWithAnsi = Encoding.GetEncoding(1252).GetString(bytes);
        var decodedWithUtf8 = Encoding.UTF8.GetString(bytes);

        Assert.Contains("TEST_ü", decodedWithAnsi);
        Assert.DoesNotContain("TEST_ü", decodedWithUtf8);
    }

    [Fact]
    public async Task OptWriter_WithExplicitDefaultUtf8Alias_ShouldUseUtf8()
    {
        var request = this.CreateTestRequest();
        request.Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 };
        request.LoadFile = request.LoadFile with { IsEncodingExplicit = true };

        var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FolderName = "VOL001",
                        FileName = "TEST_ü.tif",
                        FilePathInZip = "NATIVES\\VOL001\\TEST_ü.tif"
                    },
                    PageCount = 1
                }
            };

        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt, WriterMode.ProductionSet);
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, request, fileData);

        var bytes = stream.ToArray();
        var decodedWithUtf8 = Encoding.UTF8.GetString(bytes);

        Assert.Contains("TEST_ü", decodedWithUtf8);
    }
}
