using System.Xml.Linq;
using System.Xml.Schema;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

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

        var document = root.Element("Batch")?.Element("Document");
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

        Assert.Contains("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", content.ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Contains("<Root DataInterchangeType=\"Export\" MajorVersion=\"1\" MinorVersion=\"2\">", content, StringComparison.Ordinal);
        Assert.Contains("<Batch>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<Documents>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("</Documents>", content, StringComparison.Ordinal);
        Assert.Contains("<Document DocID=\"DOC00000001\">", content, StringComparison.Ordinal);
        Assert.Contains("<Files>", content, StringComparison.Ordinal);
        Assert.Contains("<File FileType=\"Native\">", content, StringComparison.Ordinal);
        Assert.Contains("<ExternalFile FilePath=\"folder_001/file_00000001.pdf\" FileName=\"file_00000001.pdf\" FileSize=\"14\" Hash=\"", content, StringComparison.Ordinal);
        Assert.Contains("<File FileType=\"Text\">", content, StringComparison.Ordinal);
        Assert.Contains("<ExternalFile FilePath=\"folder_001/file_00000001.txt\" FileName=\"file_00000001.txt\" FileSize=\"41\" Hash=\"", content, StringComparison.Ordinal);
        Assert.Contains("<Tags>", content, StringComparison.Ordinal);
        Assert.Contains("<Tag TagName=\"Custodian\" TagValue=\"Custodian 1\" />", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<Fields>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<Field ", content, StringComparison.Ordinal);
        Assert.Contains("</Document>", content, StringComparison.Ordinal);
        Assert.Contains("</Batch>", content, StringComparison.Ordinal);
        Assert.Contains("</Root>", content, StringComparison.Ordinal);
    }

    [Fact]
#pragma warning disable S4426 // Weak cryptographic algorithms are tested for correctness
    public async Task WriteAsync_WithHashModeActual_WritesAlgorithmSpecificHashAttributes()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Metadata = new MetadataConfig { WithMetadata = false },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5, Config.HashAlgorithm.SHA1, Config.HashAlgorithm.SHA256 },
            },
        };

        var contentBytes = "test content"u8.ToArray();
#pragma warning disable S4426 // Weak cryptographic algorithms are tested for correctness
#pragma warning disable CA5350 // Weak cryptographic algorithm is used for e-discovery compat
        var sha1Hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(contentBytes)).ToLowerInvariant();
#pragma warning restore CA5350
#pragma warning restore S4426
        var sha256Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contentBytes)).ToLowerInvariant();

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
                Data = contentBytes,
                DataLength = contentBytes.Length,
                Hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(contentBytes)).ToLowerInvariant(),
                Hashes = new Dictionary<Config.HashAlgorithm, string>
                {
                    [Config.HashAlgorithm.MD5] = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(contentBytes)).ToLowerInvariant(),
                    [Config.HashAlgorithm.SHA1] = sha1Hash,
                    [Config.HashAlgorithm.SHA256] = sha256Hash,
                },
            }
        };

        var writer = new XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        var doc = XDocument.Load(stream);
        var nativeFile = doc.Root!.Element("Batch")!.Element("Document")!.Element("Files")!.Element("File")!.Element("ExternalFile");
        Assert.NotNull(nativeFile);

        Assert.Equal(sha1Hash, nativeFile.Attribute("Sha1Hash")?.Value);
        Assert.Equal(sha256Hash, nativeFile.Attribute("Sha256Hash")?.Value);
    }

    [Fact]
    public async Task WriteAsync_WithHashModeMD5Only_DoesNotWriteExtraHashAttributes()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Metadata = new MetadataConfig { WithMetadata = false },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5 },
            },
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
                Hash = "abcdef1234567890",
                Hashes = new Dictionary<Config.HashAlgorithm, string>
                {
                    [Config.HashAlgorithm.MD5] = "abcdef1234567890",
                },
            }
        };

        var writer = new XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        var doc = XDocument.Load(stream);
        var nativeFile = doc.Root!.Element("Batch")!.Element("Document")!.Element("Files")!.Element("File")!.Element("ExternalFile");
        Assert.NotNull(nativeFile);

        Assert.Null(nativeFile.Attribute("Sha1Hash"));
        Assert.Null(nativeFile.Attribute("Sha256Hash"));
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

    [Fact]
    public async Task XmlLoadFileWriter_WithFamilies_ProducesParentChildDocumentsAndRelationships()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "eml", WithText = true },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Metadata = new MetadataConfig { WithFamilies = true, Seed = 42 }
        };

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FileName = "doc1.eml",
                    FilePathInZip = "folder/doc1.eml"
                },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                DataLength = 100
            }
        };

        var writer = new XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        var doc = XDocument.Load(stream);
        var root = doc.Root;
        Assert.NotNull(root);

        var documents = root.Element("Batch")?.Elements("Document").ToList();
        Assert.NotNull(documents);
        Assert.Equal(2, documents.Count);

        var parentDoc = documents[0];
        Assert.Equal("DOC00000001", parentDoc.Attribute("DocID")?.Value);

        var childDoc = documents[1];
        Assert.Equal("DOC00000001_A001", childDoc.Attribute("DocID")?.Value);

        // Check tags for parent and child
        var parentTags = parentDoc.Element("Tags")?.Elements("Tag").ToDictionary(t => t.Attribute("TagName")?.Value ?? "", t => t.Attribute("TagValue")?.Value ?? "");
        Assert.NotNull(parentTags);
        Assert.Equal("DOC00000001", parentTags["BEGATTACH"]);
        Assert.Equal("DOC00000001_A001", parentTags["ENDATTACH"]);
        Assert.Equal("", parentTags["PARENTDOCID"]);

        var childTags = childDoc.Element("Tags")?.Elements("Tag").ToDictionary(t => t.Attribute("TagName")?.Value ?? "", t => t.Attribute("TagValue")?.Value ?? "");
        Assert.NotNull(childTags);
        Assert.Equal("DOC00000001", childTags["BEGATTACH"]);
        Assert.Equal("DOC00000001_A001", childTags["ENDATTACH"]);
        Assert.Equal("DOC00000001", childTags["PARENTDOCID"]);

        // Check Relationships section
        var relationship = root.Element("Batch")?.Element("Relationships")?.Element("Relationship");
        Assert.NotNull(relationship);
        Assert.Equal("Attachment", relationship.Attribute("Type")?.Value);
        Assert.Equal("DOC00000001", relationship.Attribute("ParentDocID")?.Value);
        Assert.Equal("DOC00000001_A001", relationship.Attribute("ChildDocID")?.Value);
    }

    [Fact]
    public async Task XmlLoadFileWriter_Output_ValidatesAgainstEdrm12XsdSchema()
    {
        var request = this.CreateTestRequest();
        request.Output = request.Output with { WithText = true };
        var fileData = this.CreateTestFileData();
        var writer = new XmlLoadFileWriter();
        using var stream = new MemoryStream();

        await writer.WriteAsync(stream, request, fileData);

        ValidateAgainstEdrmSchema(stream);
    }

    [Fact]
    public async Task XmlLoadFileWriter_WithFamilies_ValidatesAgainstEdrm12XsdSchema()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "eml", WithText = true },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Metadata = new MetadataConfig { WithFamilies = true, Seed = 42 }
        };

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FileName = "doc1.eml",
                    FilePathInZip = "folder/doc1.eml"
                },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                DataLength = 100
            }
        };

        var writer = new XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        ValidateAgainstEdrmSchema(stream);
    }

    [Fact]
#pragma warning disable S4426 // Weak cryptographic algorithms are tested for correctness
#pragma warning disable CA5350 // Weak cryptographic algorithm is used for e-discovery compat
    public async Task XmlLoadFileWriter_WithMultipleHashes_ValidatesAgainstEdrm12XsdSchema()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Metadata = new MetadataConfig { WithMetadata = false },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5, Config.HashAlgorithm.SHA1, Config.HashAlgorithm.SHA256 },
            },
        };

        var contentBytes = "test content"u8.ToArray();
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
                Data = contentBytes,
                DataLength = contentBytes.Length,
                Hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(contentBytes)).ToLowerInvariant(),
                Hashes = new Dictionary<Config.HashAlgorithm, string>
                {
                    [Config.HashAlgorithm.MD5] = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(contentBytes)).ToLowerInvariant(),
                    [Config.HashAlgorithm.SHA1] = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(contentBytes)).ToLowerInvariant(),
                    [Config.HashAlgorithm.SHA256] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contentBytes)).ToLowerInvariant(),
                },
            }
        };

        var writer = new XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        ValidateAgainstEdrmSchema(stream);
    }
#pragma warning restore CA5350
#pragma warning restore S4426

    [Fact]
    public void XmlLoadFileWriter_DeliberatelyInvalidXml_FailsXsdSchemaValidation()
    {
        const string invalidXmlMissingDocID = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Root DataInterchangeType=""Export"" MajorVersion=""1"" MinorVersion=""2"">
  <Batch>
    <Document>
      <Files>
        <File FileType=""Native"">
          <ExternalFile FilePath=""doc.pdf"" FileName=""doc.pdf"" FileSize=""100"" Hash=""123"" />
        </File>
      </Files>
    </Document>
  </Batch>
</Root>";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalidXmlMissingDocID));
        var errors = GetEdrmSchemaValidationErrors(stream);
        Assert.NotEmpty(errors);
    }

    private static readonly string Edrm12XsdSchema = @"<?xml version=""1.0"" encoding=""utf-8""?>
<xs:schema xmlns:xs=""http://www.w3.org/2001/XMLSchema"" elementFormDefault=""qualified"">
  <xs:element name=""Root"">
    <xs:complexType>
      <xs:sequence>
        <xs:element name=""Batch"" maxOccurs=""unbounded"">
          <xs:complexType>
            <xs:sequence>
              <xs:element name=""Document"" minOccurs=""0"" maxOccurs=""unbounded"">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name=""Files"" minOccurs=""0"">
                      <xs:complexType>
                        <xs:sequence>
                          <xs:element name=""File"" minOccurs=""0"" maxOccurs=""unbounded"">
                            <xs:complexType>
                              <xs:sequence>
                                <xs:element name=""ExternalFile"" minOccurs=""0"">
                                  <xs:complexType>
                                    <xs:attribute name=""FilePath"" type=""xs:string"" use=""required"" />
                                    <xs:attribute name=""FileName"" type=""xs:string"" use=""required"" />
                                    <xs:attribute name=""FileSize"" type=""xs:integer"" use=""required"" />
                                    <xs:attribute name=""Hash"" type=""xs:string"" use=""required"" />
                                    <xs:attribute name=""Sha1Hash"" type=""xs:string"" use=""optional"" />
                                    <xs:attribute name=""Sha256Hash"" type=""xs:string"" use=""optional"" />
                                  </xs:complexType>
                                </xs:element>
                              </xs:sequence>
                              <xs:attribute name=""FileType"" type=""xs:string"" use=""required"" />
                            </xs:complexType>
                          </xs:element>
                        </xs:sequence>
                      </xs:complexType>
                    </xs:element>
                    <xs:element name=""Tags"" minOccurs=""0"">
                      <xs:complexType>
                        <xs:sequence>
                          <xs:element name=""Tag"" minOccurs=""0"" maxOccurs=""unbounded"">
                            <xs:complexType>
                              <xs:attribute name=""TagName"" type=""xs:string"" use=""required"" />
                              <xs:attribute name=""TagValue"" type=""xs:string"" use=""optional"" />
                              <xs:attribute name=""TagDataType"" type=""xs:string"" use=""optional"" />
                            </xs:complexType>
                          </xs:element>
                        </xs:sequence>
                      </xs:complexType>
                    </xs:element>
                  </xs:sequence>
                  <xs:attribute name=""DocID"" type=""xs:string"" use=""required"" />
                </xs:complexType>
              </xs:element>
              <xs:element name=""Relationships"" minOccurs=""0"">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name=""Relationship"" minOccurs=""0"" maxOccurs=""unbounded"">
                      <xs:complexType>
                        <xs:attribute name=""Type"" type=""xs:string"" use=""required"" />
                        <xs:attribute name=""ParentDocID"" type=""xs:string"" use=""required"" />
                        <xs:attribute name=""ChildDocID"" type=""xs:string"" use=""required"" />
                      </xs:complexType>
                    </xs:element>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:sequence>
          </xs:complexType>
        </xs:element>
      </xs:sequence>
      <xs:attribute name=""DataInterchangeType"" type=""xs:string"" use=""required"" />
      <xs:attribute name=""MajorVersion"" type=""xs:string"" use=""required"" />
      <xs:attribute name=""MinorVersion"" type=""xs:string"" use=""required"" />
    </xs:complexType>
  </xs:element>
</xs:schema>";

    private static readonly XmlSchemaSet CachedEdrm12SchemaSet = CreateEdrm12SchemaSet();

    private static XmlSchemaSet CreateEdrm12SchemaSet()
    {
        var schemas = new XmlSchemaSet();
        using var schemaReader = System.Xml.XmlReader.Create(new StringReader(Edrm12XsdSchema));
        schemas.Add("", schemaReader);
        schemas.Compile();
        return schemas;
    }

    private static IReadOnlyList<string> GetEdrmSchemaValidationErrors(Stream stream)
    {
        stream.Position = 0;
        var doc = XDocument.Load(stream);
        List<string> validationErrors = new();
        doc.Validate(CachedEdrm12SchemaSet, (_, args) =>
        {
            validationErrors.Add(args.Message);
        });
        return validationErrors;
    }

    private static void ValidateAgainstEdrmSchema(Stream stream)
    {
        var errors = GetEdrmSchemaValidationErrors(stream);
        Assert.Empty(errors);
    }
}

