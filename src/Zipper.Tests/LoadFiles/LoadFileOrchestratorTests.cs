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
}
