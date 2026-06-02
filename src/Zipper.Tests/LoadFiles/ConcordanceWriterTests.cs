using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles
{
    public class ConcordanceWriterTests : TempDirectoryTestBase
    {
        [Fact]
        public async Task WriteAsync_ConcordanceWriter_ProducesConcordanceFormattedOutput()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
                LoadFile = new LoadFileConfig { Encoding = "ANSI" },
                Metadata = new MetadataConfig { WithMetadata = true, Seed = 42 }
            };

            var files = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem
                    {
                        Index = 1,
                        FolderNumber = 1,
                        FileName = "doc1.pdf",
                        FilePathInZip = "folder/doc1.pdf"
                    },
                    DataLength = 1234
                }
            };

            var writer = new ConcordanceWriter();
            using var stream = new MemoryStream();
            await writer.WriteAsync(stream, request, files);

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var content = System.Text.Encoding.GetEncoding(1252).GetString(stream.ToArray());
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(2, lines.Length);

            Assert.Contains("\u00feCONTROLNUMBER\u00fe", lines[0]);
            Assert.Contains("\u00fePATH\u00fe", lines[0]);
            Assert.Contains("\u00feDOC00000001\u00fe", lines[1]);
            Assert.Contains("\u00fefolder/doc1.pdf\u00fe", lines[1]);
        }

        [Fact]
        public async Task ConcordanceWriter_ShouldUseDatEscapingForQuoteDelimiter()
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
                        FilePathInZip = "folder_001/file_with_\u00fe_char.pdf"
                    },
                    Data = Array.Empty<byte>()
                },
            };
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            var outputPath = Path.Combine(this.TempDir, "test.dat");

            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var dataLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1];
            var fields = dataLine.Split('\u0014');

            Assert.Contains("\u00fe\u00fe", fields[3]);
        }

        [Fact]
        public async Task ConcordanceWriter_ShouldUseProperDelimiters()
        {
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            var outputPath = Path.Combine(this.TempDir, "test.dat");

            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var colDelim = '\u0014';
            Assert.Contains(colDelim, content);
            Assert.Contains("CONTROLNUMBER", content);

            var lines_split = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("BEGATTY", lines_split[0]);
            Assert.Contains("CONTROLNUMBER", lines_split[0]);
            Assert.Contains("PATH", lines_split[0]);
        }
    }
}
