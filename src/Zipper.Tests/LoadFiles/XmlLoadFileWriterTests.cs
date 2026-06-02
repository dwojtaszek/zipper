using System.Xml.Linq;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles
{
    public class XmlLoadFileWriterTests : TempDirectoryTestBase
    {
        [Fact]
        public async Task WriteAsync_XmlLoadFileWriter_ProducesValidXmlOutput()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
                LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
                Metadata = new MetadataConfig { WithMetadata = false }
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
                    DataLength = 500,
                    Hash = "abcdef"
                }
            };

            var writer = new XmlLoadFileWriter();
            using var stream = new MemoryStream();
            await writer.WriteAsync(stream, request, files);

            stream.Position = 0;
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            Assert.NotNull(root);
            Assert.Equal("Root", root.Name.LocalName);
            Assert.Equal("Export", root.Attribute("DataInterchangeType")?.Value);

            var document = root.Element("Batch")?.Element("Documents")?.Element("Document");
            Assert.NotNull(document);
            Assert.Equal("DOC00000001", document.Attribute("DocID")?.Value);

            var nativeFile = document.Element("Files")?.Element("File")?.Element("ExternalFile");
            Assert.NotNull(nativeFile);
            Assert.Equal("folder/doc1.pdf", nativeFile.Attribute("FilePath")?.Value);
            Assert.Equal("doc1.pdf", nativeFile.Attribute("FileName")?.Value);
            Assert.Equal("500", nativeFile.Attribute("FileSize")?.Value);
            Assert.Equal("abcdef", nativeFile.Attribute("Hash")?.Value);
        }

        [Fact]
        public async Task XmlLoadFileWriter_ShouldWriteValidXml()
        {
            var request = this.CreateTestRequest();
            request.Output = request.Output with { WithText = true };
            var fileData = this.CreateTestFileData();
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.EdrmXml);
            var outputPath = Path.Combine(this.TempDir, "test.xml");

            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);

            Assert.Contains("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", content.ToLowerInvariant());
            Assert.Contains("<Root DataInterchangeType=\"Export\" MajorVersion=\"1\" MinorVersion=\"2\">", content);
            Assert.Contains("<Batch>", content);
            Assert.Contains("<Documents>", content);
            Assert.Contains("<Document DocID=\"DOC00000001\">", content);
            Assert.Contains("<Files>", content);
            Assert.Contains("<File FileType=\"Native\">", content);
            Assert.Contains("<ExternalFile FilePath=\"folder_001/file_00000001.pdf\" FileName=\"file_00000001.pdf\" FileSize=\"14\" Hash=\"", content);
            Assert.Contains("<File FileType=\"Text\">", content);
            Assert.Contains("<ExternalFile FilePath=\"folder_001/file_00000001.txt\" FileName=\"file_00000001.txt\" FileSize=\"41\" Hash=\"", content);
            Assert.Contains("<Fields>", content);
            Assert.Contains("<Field Name=\"Custodian\">Custodian 1</Field>", content);
            Assert.Contains("</Document>", content);
            Assert.Contains("</Documents>", content);
            Assert.Contains("</Batch>", content);
            Assert.Contains("</Root>", content);
        }

        [Fact]
        public async Task XmlLoadFileWriter_WhenOperationCanceledException_Rethrows()
        {
            var request = this.CreateTestRequest();
            var fileData = this.CreateTestFileData();
            var writer = new XmlLoadFileWriter();
            using var stream = new CancelingStream();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                writer.WriteAsync(stream, request, fileData));
        }

        private class CancelingStream : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => 0;

            public override long Position
            {
                get => 0;
                set { }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return 0;
            }

            public override void SetLength(long value)
            {
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new OperationCanceledException();
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                throw new OperationCanceledException();
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw new OperationCanceledException();
            }
        }
    }
}
