using System.Text;
using Xunit;

using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

public class LoadFileOrchestratorTests
{
    [Fact]
    public async Task EmitAllAsync_MultipleFormats_WritesLoadFileAndAuditPerFormat()
    {
        // Arrange
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "pdf",
                FileCount = 3,
                Concurrency = 1,
            },
        };

        var processedFiles = new List<FileData>();
        for (int i = 1; i <= 3; i++)
        {
            processedFiles.Add(new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = i,
                    FileName = $"test{i}.pdf",
                    FilePathInZip = $"folder{1}/test{i}.pdf",
                    FolderName = "folder1",
                    FolderNumber = 1,
                },
                Data = Encoding.UTF8.GetBytes($"Test content {i}"),
                MemoryOwner = null,
            });
        }

        var streams = new Dictionary<string, MemoryStream>(StringComparer.Ordinal);
        var formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Csv };

        // Act
        var lastTarget = await LoadFileOrchestrator.EmitAllAsync(
            request,
            processedFiles,
            formats,
            WriterMode.Standard,
            (format, writer) => (writer.FileExtension, writer.FileExtension + "_audit.json"),
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            });

        // Assert — one Load File + one audit sidecar per format
        Assert.Equal(".csv", lastTarget);
        Assert.Equal(4, streams.Count);
        Assert.Contains(".dat", streams.Keys);
        Assert.Contains(".dat_audit.json", streams.Keys);
        Assert.Contains(".csv", streams.Keys);
        Assert.Contains(".csv_audit.json", streams.Keys);

        var datLines = ReadLines(streams[".dat"]);
        Assert.Equal(4, datLines.Length); // Header + 3 rows

        var auditJson = ReadText(streams[".dat_audit.json"]);
        using var doc = System.Text.Json.JsonDocument.Parse(auditJson);
        Assert.Equal(3, doc.RootElement.GetProperty("totalRecords").GetInt64());

        // Audit Files are UTF-8 without a BOM (matches File.WriteAllTextAsync semantics)
        var auditBytes = streams[".dat_audit.json"].ToArray();
        Assert.False(auditBytes.Length >= 3 && auditBytes[0] == 0xEF && auditBytes[1] == 0xBB && auditBytes[2] == 0xBF);
    }

    private static string[] ReadLines(MemoryStream stream)
        => ReadText(stream).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string ReadText(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private sealed class NonDisposingStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            // The orchestrator disposes output streams it opens; keep the backing
            // MemoryStream readable so the test can assert on the written bytes.
        }
    }

    [Fact]
    public async Task EmitAllAsync_FormatsAreEmittedInSpecifiedOrder()
    {
        // Tests that formats are emitted in the order provided (not alphabetical)
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "pdf",
                FileCount = 2,
                Concurrency = 1,
            },
        };

        var processedFiles = new List<FileData>();
        for (int i = 1; i <= 2; i++)
        {
            processedFiles.Add(new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = i,
                    FileName = $"test{i}.pdf",
                    FilePathInZip = $"folder1/test{i}.pdf",
                    FolderName = "folder1",
                    FolderNumber = 1,
                },
                Data = Encoding.UTF8.GetBytes($"Test content {i}"),
                MemoryOwner = null,
            });
        }

        var streams = new Dictionary<string, MemoryStream>(StringComparer.Ordinal);
        var emissionOrder = new List<string>();
        var formats = new List<LoadFileFormat> { LoadFileFormat.Opt, LoadFileFormat.Dat, LoadFileFormat.Csv }; // Non-alphabetical order

        // Act
        var lastTarget = await LoadFileOrchestrator.EmitAllAsync(
            request,
            processedFiles,
            formats,
            WriterMode.Standard,
            (format, writer) =>
            {
                emissionOrder.Add(format.ToString());
                return (writer.FileExtension, writer.FileExtension + "_audit.json");
            },
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            });

        // Assert — formats emitted in specified order
        Assert.Equal(new[] { "Opt", "Dat", "Csv" }, emissionOrder);
        Assert.Equal(".csv", lastTarget); // Last format's target returned
    }

    [Fact]
    public async Task EmitAllAsync_AuditFileNaming_MatchesFormat()
    {
        // Tests that audit file names are derived from the load file target + "_audit.json"
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "pdf",
                FileCount = 1,
                Concurrency = 1,
            },
        };

        var processedFiles = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FileName = "test1.pdf",
                    FilePathInZip = "folder1/test1.pdf",
                    FolderName = "folder1",
                    FolderNumber = 1,
                },
                Data = Encoding.UTF8.GetBytes("Test content 1"),
                MemoryOwner = null,
            }
        };

        var streams = new Dictionary<string, MemoryStream>(StringComparer.Ordinal);
        var formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt, LoadFileFormat.Csv, LoadFileFormat.Concordance };

        // Act
        await LoadFileOrchestrator.EmitAllAsync(
            request,
            processedFiles,
            formats,
            WriterMode.Standard,
            (format, writer) => (writer.FileExtension, writer.FileExtension + "_audit.json"),
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            });

        // Assert — audit files named correctly per format
        Assert.Contains(".dat", streams.Keys);
        Assert.Contains(".dat_audit.json", streams.Keys);
        Assert.Contains(".opt", streams.Keys);
        Assert.Contains(".opt_audit.json", streams.Keys);
        Assert.Contains(".csv", streams.Keys);
        Assert.Contains(".csv_audit.json", streams.Keys);
        Assert.Contains(".dat", streams.Keys); // Concordance uses .dat extension
        Assert.Contains(".dat_audit.json", streams.Keys); // But separate audit file
    }

    [Fact]
    public async Task EmitAllAsync_XmlRouting_WriterModeProductionSetUsesXmlWriter()
    {
        // Tests that XML format uses XmlLoadFileWriter in ProductionSet mode
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "pdf",
                FileCount = 1,
                Concurrency = 1,
            },
        };

        var processedFiles = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FileName = "test1.pdf",
                    FilePathInZip = "folder1/test1.pdf",
                    FolderName = "folder1",
                    FolderNumber = 1,
                },
                Data = Encoding.UTF8.GetBytes("Test content 1"),
                MemoryOwner = null,
            }
        };

        var streams = new Dictionary<string, MemoryStream>(StringComparer.Ordinal);
        var formats = new List<LoadFileFormat> { LoadFileFormat.EdrmXml };

        // Act
        await LoadFileOrchestrator.EmitAllAsync(
            request,
            processedFiles,
            formats,
            WriterMode.ProductionSet,
            (format, writer) => (writer.FileExtension, writer.FileExtension + "_audit.json"),
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            });

        // Assert — XML file and audit created (EDRM XML uses .xml extension)
        Assert.Contains(".xml", streams.Keys);
        Assert.Contains(".xml_audit.json", streams.Keys);

        // Verify XML content is valid
        var xmlContent = ReadText(streams[".xml"]);
        Assert.Contains("<Root", xmlContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAllAsync_CancellationBetweenFormats_StopsAndCleansUp()
    {
        // Tests that cancellation between formats stops processing
        // Cancellation is checked AFTER load file write but BEFORE audit write (line 47 in LoadFileOrchestrator)
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "pdf",
                FileCount = 1,
                Concurrency = 1,
            },
        };

        var processedFiles = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FileName = "test1.pdf",
                    FilePathInZip = "folder1/test1.pdf",
                    FolderName = "folder1",
                    FolderNumber = 1,
                },
                Data = Encoding.UTF8.GetBytes("Test content 1"),
                MemoryOwner = null,
            }
        };

        var streams = new Dictionary<string, MemoryStream>(StringComparer.Ordinal);
        var formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Csv, LoadFileFormat.Opt };
        var cts = new CancellationTokenSource();

        // Act - cancel after first format's load file is written (but before audit)
        var task = LoadFileOrchestrator.EmitAllAsync(
            request,
            processedFiles,
            formats,
            WriterMode.Standard,
            (format, writer) =>
            {
                if (format == LoadFileFormat.Dat)
                {
                    cts.Cancel();
                }
                return (writer.FileExtension, writer.FileExtension + "_audit.json");
            },
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            },
            cts.Token);

        // Assert - cancellation should propagate
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        // DAT load file should have been written, but audit file NOT written (cancellation checked before audit)
        Assert.Contains(".dat", streams.Keys);
        Assert.DoesNotContain(".dat_audit.json", streams.Keys);
        Assert.DoesNotContain(".csv", streams.Keys);
    }
}
