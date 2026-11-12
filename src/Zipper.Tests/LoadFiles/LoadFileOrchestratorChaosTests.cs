using System.Text;
using System.Text.Json;
using Xunit;

using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

/// <summary>
/// Tests that <see cref="LoadFileOrchestrator.EmitAllAsync"/> correctly injects
/// per-format chaos engines and delegates audit + sidecar emission, eliminating
/// the duplicated loop previously in <see cref="LoadFileOnlyGenerator"/>.
/// </summary>
public sealed class LoadFileOrchestratorChaosTests
{
    [Fact]
    public async Task EmitAllAsync_WithChaosEngineFactory_InjectsAnomaliesIntoLoadFile()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 10, FileType = "pdf" },
            Metadata = new MetadataConfig { Seed = 42 },
            Chaos = new ChaosConfig { ChaosMode = true, ChaosAmount = "5" },
            LoadfileOnly = true,
        };

        var streams = new Dictionary<string, NonDisposingStream>(StringComparer.Ordinal);
        var formats = new List<LoadFileFormat> { LoadFileFormat.Dat };

        var lastTarget = await LoadFileOrchestrator.EmitAllAsync(
            request,
            Array.Empty<FileData>(),
            formats,
            WriterMode.LoadfileOnly,
            (format, writer) => (writer.FileExtension, writer.FileExtension + "_properties.json"),
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            },
            cancellationToken: default,
            chaosEngineFactory: (req, records, format) => LoadFileAuditWriter.BuildChaosEngine(req, records, format));

        Assert.Equal(".dat", lastTarget);
        Assert.Equal(3, streams.Count); // .dat + _properties.json + _chaos_expectations.json

        // The audit file should have anomalies
        var auditJson = ReadText(streams[".dat_properties.json"]);
        using var doc = JsonDocument.Parse(auditJson);
        Assert.True(doc.RootElement.GetProperty("chaosMode").GetProperty("enabled").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("chaosMode").GetProperty("totalAnomalies").GetInt32() > 0);
    }

    [Fact]
    public async Task EmitAllAsync_WithChaosEngineFactory_EmitsSidecarForNonAdversarialScenario()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 20, FileType = "pdf" },
            Metadata = new MetadataConfig { Seed = 42 },
            Chaos = new ChaosConfig { ChaosMode = true, ChaosAmount = "3" },
            LoadfileOnly = true,
        };

        var streams = new Dictionary<string, NonDisposingStream>(StringComparer.Ordinal);
        var formats = new List<LoadFileFormat> { LoadFileFormat.Dat };

        await LoadFileOrchestrator.EmitAllAsync(
            request,
            Array.Empty<FileData>(),
            formats,
            WriterMode.LoadfileOnly,
            (format, writer) => (writer.FileExtension, writer.FileExtension + "_properties.json"),
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            },
            cancellationToken: default,
            chaosEngineFactory: (req, records, format) => LoadFileAuditWriter.BuildChaosEngine(req, records, format));

        // Should have sidecar (non-adversarial scenario)
        Assert.Contains(streams.Keys, k => k.Contains("chaos_expectations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmitAllAsync_WithoutChaosEngineFactory_NoChaosInjected()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 5, FileType = "pdf" },
            Metadata = new MetadataConfig { Seed = 42 },
            LoadfileOnly = true,
        };

        var streams = new Dictionary<string, NonDisposingStream>(StringComparer.Ordinal);
        var formats = new List<LoadFileFormat> { LoadFileFormat.Dat };

        await LoadFileOrchestrator.EmitAllAsync(
            request,
            Array.Empty<FileData>(),
            formats,
            WriterMode.LoadfileOnly,
            (format, writer) => (writer.FileExtension, writer.FileExtension + "_properties.json"),
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            });

        Assert.Equal(2, streams.Count); // .dat + _properties.json (no sidecar)
        var auditJson = ReadText(streams[".dat_properties.json"]);
        using var doc = JsonDocument.Parse(auditJson);
        Assert.False(doc.RootElement.GetProperty("chaosMode").GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData(LoadFileFormat.Dat)]
    [InlineData(LoadFileFormat.Opt)]
    public async Task EmitAllAsync_WithPerFormatTransform_AppliesTransformBeforeWriting(
        LoadFileFormat format)
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 3, FileType = "pdf" },
            Metadata = new MetadataConfig { Seed = 42 },
            LoadfileOnly = true,
        };

        bool transformCalled = false;
        var streams = new Dictionary<string, NonDisposingStream>(StringComparer.Ordinal);

        await LoadFileOrchestrator.EmitAllAsync(
            request,
            Array.Empty<FileData>(),
            new List<LoadFileFormat> { format },
            WriterMode.LoadfileOnly,
            (fmt, writer) => (writer.FileExtension, writer.FileExtension + "_properties.json"),
            target =>
            {
                var stream = new NonDisposingStream();
                streams[target] = stream;
                return stream;
            },
            perFormatTransform: (req, fmt) =>
            {
                transformCalled = true;
                return req;
            });

        Assert.True(transformCalled);
    }

    [Fact]
    public async Task EmitAllAsync_ChaosEngine_ByteParityWithDirectWriterCall()
    {
        // Verify that passing a chaos engine through EmitAllAsync produces the same
        // load file bytes as calling writer.WriteAsync directly with the same engine.
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 10, FileType = "pdf" },
            Metadata = new MetadataConfig { Seed = 42 },
            Chaos = new ChaosConfig { ChaosMode = true, ChaosAmount = "3" },
            LoadfileOnly = true,
        };

        var records = Array.Empty<FileData>();
        var engine = LoadFileAuditWriter.BuildChaosEngine(request, records, LoadFileFormat.Dat);

        // Direct writer call
        using var directStream = new MemoryStream();
        var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat, WriterMode.LoadfileOnly);
        await writer.WriteAsync(directStream, request, records, engine);
        var directBytes = directStream.ToArray();

        // Via orchestrator with same-seed engine factory
        var request2 = request.Clone();
        var orchStreams = new Dictionary<string, NonDisposingStream>(StringComparer.Ordinal);
        await LoadFileOrchestrator.EmitAllAsync(
            request2,
            records,
            new List<LoadFileFormat> { LoadFileFormat.Dat },
            WriterMode.LoadfileOnly,
            (format, w) => (w.FileExtension, w.FileExtension + "_properties.json"),
            target =>
            {
                var s = new NonDisposingStream();
                orchStreams[target] = s;
                return s;
            },
            chaosEngineFactory: (req, recs, fmt) => LoadFileAuditWriter.BuildChaosEngine(req, recs, fmt));

        var orchBytes = orchStreams[".dat"].ToArray();
        Assert.Equal(directBytes, orchBytes);
    }

    private static string ReadText(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    internal sealed class NonDisposingStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            // Keep the backing MemoryStream readable so the test can assert on the written bytes.
        }
    }
}
