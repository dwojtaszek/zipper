using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Tests.TestHelpers;

namespace Zipper.Tests;

public class ConcordanceComposingWriterTests : TempDirectoryTestBase
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

        var writer = new ConcordanceComposingWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var content = System.Text.Encoding.GetEncoding(1252).GetString(stream.ToArray());
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);

        Assert.Contains("\u00feCONTROLNUMBER\u00fe", lines[0], StringComparison.Ordinal);
        Assert.Contains("\u00fePATH\u00fe", lines[0], StringComparison.Ordinal);
        Assert.Contains("\u00feDOC00000001\u00fe", lines[1], StringComparison.Ordinal);
        Assert.Contains("\u00fefolder/doc1.pdf\u00fe", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithMetadata_ContainsCompressionMethodColumn()
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

        var writer = new ConcordanceComposingWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var content = System.Text.Encoding.GetEncoding(1252).GetString(stream.ToArray());
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("\u00feCOMPRESSION_METHOD\u00fe", lines[0], StringComparison.Ordinal);
        Assert.Contains("\u00feDeflate\u00fe", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithoutMetadata_OmitsCompressionMethodColumn()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
            LoadFile = new LoadFileConfig { Encoding = "ANSI" },
            Metadata = new MetadataConfig { WithMetadata = false, Seed = 42 }
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

        var writer = new ConcordanceComposingWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var content = System.Text.Encoding.GetEncoding(1252).GetString(stream.ToArray());

        Assert.DoesNotContain("COMPRESSION_METHOD", content, StringComparison.Ordinal);
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

        Assert.Contains("\u00fe\u00fe", fields[3], StringComparison.Ordinal);
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
        Assert.Contains("CONTROLNUMBER", content, StringComparison.Ordinal);

        var lines_split = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("BEGATTY", lines_split[0], StringComparison.Ordinal);
        Assert.Contains("CONTROLNUMBER", lines_split[0], StringComparison.Ordinal);
        Assert.Contains("PATH", lines_split[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcordanceWriter_ShouldRespectCustomQuoteDelimiter()
    {
        var request = this.CreateTestRequest();
        request.Delimiters = new DelimiterConfig
        {
            ColumnDelimiter = "\u0014",
            QuoteDelimiter = "'"
        };
        var fileData = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FilePathInZip = "folder_001/file_with_'char.pdf"
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
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var headerFields = lines[0].Split('\u0014');
        var dataFields = lines[1].Split('\u0014');
        Assert.Equal("'PATH'", headerFields[3]);
        Assert.Equal("'folder_001/file_with_''char.pdf'", dataFields[3]);
        Assert.DoesNotContain("\u00fe", content, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRecord_CustomQuoteDelimiter_DoublesEmbeddedDelimiterExactly()
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = "\u0014",
                QuoteDelimiter = "'"
            }
        });
        var record = new LoadFileRecord
        {
            Columns = new[] { "BEGATTY", "ENDATTY", "CONTROLNUMBER", "PATH" },
            Values = new[] { string.Empty, string.Empty, "DOC00000001", "folder_001/file_with_'char.pdf" },
        };

        var line = serializer.RenderRecord(record);

        Assert.Equal("''\u0014''\u0014'DOC00000001'\u0014'folder_001/file_with_''char.pdf'", line);
    }

    [Fact]
    public async Task ConcordanceWriter_EscapesHeaderQuoteCharacter()
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = "\u0014",
                QuoteDelimiter = "'"
            }
        });
        var columns = new List<string> { "has'quote", "plain" };
        var header = serializer.RenderHeader(columns);
        Assert.Equal("'has''quote'\u0014'plain'", header);
    }

    [Fact]
    public async Task WriteAsync_EmlWithTextAndBates_EmitsEmailTextPathAndBatesColumns()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 2, FileType = "eml", WithText = true },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Metadata = new MetadataConfig { WithMetadata = true, Seed = 42 },
            Bates = new BatesNumberConfig { Prefix = "TST", Start = 1, Digits = 6 },
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
                    FilePathInZip = "folder/doc1.eml",
                },
                DataLength = 100,
                Email = new Zipper.Emails.Email
                {
                    To = "alice@example.com",
                    From = "bob@example.com",
                    Subject = "Quarterly Report",
                    SentDate = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
                },
                Attachment = ("invoice.pdf", new byte[] { 1, 2, 3 }),
            },
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 2,
                    FolderNumber = 1,
                    FileName = "doc2.eml",
                    FilePathInZip = "folder/doc2.eml",
                },
                DataLength = 50,
            },
        };

        var writer = new ConcordanceComposingWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        var content = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);

        // Header must use Concordance names for the email, text, and Bates columns.
        Assert.Contains("þTOþ", lines[0], StringComparison.Ordinal);
        Assert.Contains("þFROMþ", lines[0], StringComparison.Ordinal);
        Assert.Contains("þSUBJECTþ", lines[0], StringComparison.Ordinal);
        Assert.Contains("þSENTDATEþ", lines[0], StringComparison.Ordinal);
        Assert.Contains("þATTACHMENTþ", lines[0], StringComparison.Ordinal);
        Assert.Contains("þTEXT_PATHþ", lines[0], StringComparison.Ordinal);
        Assert.Contains("þBATESþ", lines[0], StringComparison.Ordinal);

        // First record carries the explicit email template and attachment values.
        Assert.Contains("þalice@example.comþ", lines[1], StringComparison.Ordinal);
        Assert.Contains("þbob@example.comþ", lines[1], StringComparison.Ordinal);
        Assert.Contains("þQuarterly Reportþ", lines[1], StringComparison.Ordinal);
        Assert.Contains("þ2024-01-15 10:30:00þ", lines[1], StringComparison.Ordinal);
        Assert.Contains("þinvoice.pdfþ", lines[1], StringComparison.Ordinal);
        Assert.Contains("þfolder/doc1.txtþ", lines[1], StringComparison.Ordinal);
        Assert.Contains("þTST000001þ", lines[1], StringComparison.Ordinal);

        // Second record has no email template, so synthetic fallbacks are used.
        Assert.Contains("þrecipient2@example.comþ", lines[2], StringComparison.Ordinal);
        Assert.Contains("þsender2@example.comþ", lines[2], StringComparison.Ordinal);
        Assert.Contains("þEmail Subject 2þ", lines[2], StringComparison.Ordinal);
        Assert.Contains("þfolder/doc2.txtþ", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcordanceWriter_WithFamilies_IncludesFamilyColumnsAndRows()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 2, FileType = "eml" },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Metadata = new MetadataConfig { WithFamilies = true, Seed = 42 },
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
                    FilePathInZip = "folder/doc1.eml",
                },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                DataLength = 100,
            },
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 2,
                    FolderNumber = 1,
                    FileName = "doc2.eml",
                    FilePathInZip = "folder/doc2.eml",
                },
                DataLength = 100,
            },
        };

        var writer = new ConcordanceComposingWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        var content = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Cardinality: 1 header + 2 parent records + 1 attachment record = 4 rows
        Assert.Equal(4, lines.Length);

        const char ColumnDelimiter = '\u0014';
        const char QuoteDelimiter = 'þ';
        var headers = DelimitedTestParser.ParseLine(lines[0], ColumnDelimiter, QuoteDelimiter);

        // Format-specific headers and Concordance leading fields
        Assert.Equal("BEGATTY", headers[0]);
        Assert.Equal("ENDATTY", headers[1]);
        Assert.Equal("CONTROLNUMBER", headers[2]);
        Assert.Contains("BEGATTACH", headers);
        Assert.Contains("ENDATTACH", headers);
        Assert.Contains("PARENTDOCID", headers);

        var parent1 = DelimitedTestParser.ParseRecord(headers, lines[1], ColumnDelimiter, QuoteDelimiter);
        var child1 = DelimitedTestParser.ParseRecord(headers, lines[2], ColumnDelimiter, QuoteDelimiter);
        var parent2 = DelimitedTestParser.ParseRecord(headers, lines[3], ColumnDelimiter, QuoteDelimiter);

        // Concordance leading fields (BEGATTY and ENDATTY) match the attachment range
        Assert.Equal("DOC00000001", parent1["BEGATTY"]);
        Assert.Equal("DOC00000001_A001", parent1["ENDATTY"]);
        Assert.Equal("DOC00000001", child1["BEGATTY"]);
        Assert.Equal("DOC00000001_A001", child1["ENDATTY"]);
        Assert.Equal("DOC00000002", parent2["BEGATTY"]);
        Assert.Equal("DOC00000002", parent2["ENDATTY"]);

        // Parent 1 (with attachment)
        Assert.Equal("DOC00000001", parent1["CONTROLNUMBER"]);
        Assert.Equal("DOC00000001", parent1["BEGATTACH"]);
        Assert.Equal("DOC00000001_A001", parent1["ENDATTACH"]);
        Assert.Empty(parent1["PARENTDOCID"]);

        // Child 1 (attachment of Parent 1)
        Assert.Equal("DOC00000001_A001", child1["CONTROLNUMBER"]);
        Assert.Equal("DOC00000001", child1["BEGATTACH"]);
        Assert.Equal("DOC00000001_A001", child1["ENDATTACH"]);
        Assert.Equal("DOC00000001", child1["PARENTDOCID"]);
        // Child must not self-parent: PARENTDOCID points to parent, not child's own Control Number
        Assert.NotEqual(child1["CONTROLNUMBER"], child1["PARENTDOCID"]);

        // Parent 2 (without attachment: no child record, no spurious parent/child links)
        Assert.Equal("DOC00000002", parent2["CONTROLNUMBER"]);
        Assert.Equal("DOC00000002", parent2["BEGATTACH"]);
        Assert.Equal("DOC00000002", parent2["ENDATTACH"]);
        Assert.Empty(parent2["PARENTDOCID"]);
    }

    [Fact]
    public async Task WriteAsync_WithEmbeddedNewlinesInFieldValues_PreservesNewlinesIntactWithoutSanitization()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { NewlineDelimiter = "\u00ae" },
        };
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "folder_001",
                    FileName = "00000001.pdf",
                    FilePathInZip = "line1\r\nline2_unix\nline3_cr\rline4.pdf",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
            },
        };

        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
        var outputPath = Path.Combine(this.TempDir, "newline_test.dat");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, files);
        }

        var content = await File.ReadAllTextAsync(outputPath);

        // Concordance contract: embedded newlines within fields are preserved intact (not replaced with ®)
        Assert.DoesNotContain("\u00ae", content, StringComparison.Ordinal);
        Assert.Contains("þline1\r\nline2_unix\nline3_cr\rline4.pdfþ", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithEmbeddedNewlinesAndCustomDelimiters_PreservesNewlinesAndEscapesCustomQuote()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { OutputPath = this.TempDir, FileCount = 1, FileType = "pdf" },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = "|",
                QuoteDelimiter = "'",
                NewlineDelimiter = "\u00ae",
            },
        };
        var files = new List<FileData>
        {
            new()
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 1,
                    FolderName = "folder_001",
                    FileName = "00000001.pdf",
                    FilePathInZip = "folder/file_with_'quote\r\nand_newline.pdf",
                },
                Data = Encoding.UTF8.GetBytes("content"),
                DataLength = 7,
            },
        };

        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
        var outputPath = Path.Combine(this.TempDir, "custom_newline_test.dat");

        await using (var stream = File.OpenWrite(outputPath))
        {
            await writer.WriteAsync(stream, request, files);
        }

        var content = await File.ReadAllTextAsync(outputPath);

        // Custom quote is doubled (' -> ''), newlines preserved intact, newline delimiter not used, custom column used
        Assert.DoesNotContain("\u00ae", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\u00fe", content, StringComparison.Ordinal);
        Assert.Contains("'folder/file_with_''quote\r\nand_newline.pdf'", content, StringComparison.Ordinal);
        Assert.Contains("|", content, StringComparison.Ordinal);
    }
}

