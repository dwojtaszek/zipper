using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests
{
    public class DatLoadFileWriterTests : TempDirectoryTestBase
    {
        [Fact]
        public async Task DatWriter_ShouldWriteValidDatFormat()
        {
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = new DatWriter();
            var outputPath = Path.Combine(this.TempDir, "test.dat");

            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(4, lines.Length);
            Assert.Contains("Control Number", lines[0]);
            Assert.Contains("DOC00000001", lines[1]);
            Assert.Contains("DOC00000002", lines[2]);
            Assert.Contains("DOC00000003", lines[3]);
        }

        [Theory]
        [InlineData("UTF-16")]
        [InlineData("ANSI")]
        public async Task DatWriter_WithDifferentEncodings_ShouldWriteCorrectly(string encoding)
        {
            ArgumentNullException.ThrowIfNull(encoding);
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var request = this.CreateTestRequest();
            request.LoadFile = request.LoadFile with { Encoding = encoding };
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
            var outputPath = Path.Combine(this.TempDir, "test.dat");

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

            Assert.True(lines.Length >= 2);
            Assert.Contains("Control Number", lines[0]);
        }

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
            var writer = new DatWriter(WriterMode.LoadfileOnly);
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

            var writer = new DatWriter(WriterMode.LoadfileOnly);
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

            var writer = new DatWriter(WriterMode.ProductionSet);
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
            var writer = new DatWriter(WriterMode.LoadfileOnly);
            using var stream = new MemoryStream();
            await writer.WriteAsync(stream, request, new List<FileData>());

            stream.Position = 0;
            var content = Encoding.UTF8.GetString(stream.ToArray());

            Assert.Contains("\r\n", content);
        }

        [Fact]
        public async Task DatWriter_StandardRow_FieldWithQuoteDelimiter_IsDoubled()
        {
            var request = this.CreateTestRequest();
            var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FilePathInZip = "folderþX/file.pdf",
                    },
                    Data = Array.Empty<byte>(),
                },
            };
            var writer = new DatWriter();
            var outputPath = Path.Combine(this.TempDir, "test_escape.dat");

            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var output = await File.ReadAllTextAsync(outputPath);

            Assert.Contains("folderþþX/file.pdf", output);
        }

        [Fact]
        public async Task DatWriter_ProductionSetRow_FieldWithQuoteDelimiter_IsDoubled()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    FileCount = 1,
                    FileType = "pdf",
                    OutputPath = this.TempDir,
                },
                Metadata = new MetadataConfig { Seed = 42 },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
                Production = new ProductionConfig { VolumeSize = 5000 },
                Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
            };

            var files = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FolderName = "VOL001",
                        FileName = "TEST00000001.pdf",
                        FilePathInZip = "NATIVES/VOL001/fileþX.pdf",
                    },
                    DataLength = 1024,
                },
            };

            var writer = new DatWriter(WriterMode.ProductionSet);
            using var stream = new MemoryStream();
            await writer.WriteAsync(stream, request, files);

            stream.Position = 0;
            var output = Encoding.UTF8.GetString(stream.ToArray());

            Assert.Contains("fileþþX.pdf", output);
        }

        [Fact]
        public async Task DatWriter_WithFamilies_StandardMode_IncludesFamilyColumnsAndRows()
        {
            var request = this.CreateTestRequest("eml");
            request.Metadata = request.Metadata with { WithFamilies = true };

            var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FilePathInZip = "folder_001/00000001.eml"
                    },
                    Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                    DataLength = 100
                }
            };

            var writer = new DatWriter();
            var outputPath = Path.Combine(this.TempDir, "test_families_standard.dat");

            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(3, lines.Length);
            Assert.Contains("BEGATTACH", lines[0]);
            Assert.Contains("ENDATTACH", lines[0]);
            Assert.Contains("PARENTDOCID", lines[0]);

            var parentLine = lines[1];
            Assert.Contains("DOC00000001", parentLine);
            Assert.Contains("DOC00000001_A001", parentLine);

            var childLine = lines[2];
            Assert.Contains("DOC00000001_A001", childLine);
            Assert.Contains("DOC00000001", childLine);
        }

        [Fact]
        public async Task DatWriter_WithFamilies_ProductionSetMode_IncludesFamilyColumnsAndRows()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    FileCount = 1,
                    FileType = "eml",
                    OutputPath = this.TempDir,
                },
                Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
                Production = new ProductionConfig { VolumeSize = 5000 },
                Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
                LoadFile = new LoadFileConfig { Encoding = "UTF-8" }
            };

            var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FolderName = "VOL001",
                        FileName = "TEST00000001.eml",
                        FilePathInZip = "NATIVES\\VOL001\\TEST00000001.eml"
                    },
                    Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                    DataLength = 100
                }
            };

            var writer = new DatWriter(WriterMode.ProductionSet);
            using var stream = new MemoryStream();
            await writer.WriteAsync(stream, request, fileData);

            stream.Position = 0;
            var content = Encoding.UTF8.GetString(stream.ToArray());
            var lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(3, lines.Length);
            Assert.Contains("BEGATTACH", lines[0]);
            Assert.Contains("ENDATTACH", lines[0]);
            Assert.Contains("PARENTDOCID", lines[0]);

            Assert.Contains("TEST00000001", lines[1]);
            Assert.Contains("TEST00000001_A001", lines[1]);

            Assert.Contains("TEST00000001_A001", lines[2]);
            Assert.Contains("TEST00000001", lines[2]);
        }
    }
}
