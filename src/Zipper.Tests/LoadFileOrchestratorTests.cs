using System.Text.Json;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public sealed class LoadFileOrchestratorTests
{
    [Fact]
    public async Task EmitCrossFormatAsync_CoordinatedScenario_EmitsBothFormatsAndMatchingSidecarCounts()
    {
        var request = new FileGenerationRequest
        {
            LoadfileOnly = true,
            Output = new OutputConfig { FileCount = 3 },
            Metadata = new MetadataConfig { Seed = 42 },
            Chaos = new ChaosConfig { ChaosMode = true, ChaosScenario = "cross-format-desync", ChaosAmount = "1" },
        };
        var targets = new Dictionary<string, MemoryStream>();
        var result = await LoadFileOrchestrator.EmitCrossFormatAsync(
            request, request, Array.Empty<FileData>(), WriterMode.LoadfileOnly,
            "loadfile.dat", "loadfile.opt", "loadfile_properties.json", "loadfile.opt_properties.json",
            target => targets[target] = new MemoryStream(), CancellationToken.None);

        Assert.Equal(6, targets.Count);
        Assert.Equal(3, result.DatLines.Count - 1);
        foreach (var (audit, sidecar, count) in new[]
        {
            ("loadfile_properties.json", "loadfile_chaos_expectations.json", result.DatLines.Count - 1),
            ("loadfile.opt_properties.json", "loadfile.opt_chaos_expectations.json", result.OptLines.Count),
        })
        {
            using var properties = JsonDocument.Parse(targets[audit].ToArray());
            using var expectations = JsonDocument.Parse(targets[sidecar].ToArray());
            Assert.Equal(count, properties.RootElement.GetProperty("totalRecords").GetInt32());
            Assert.Equal(count, expectations.RootElement.GetProperty("invariants").GetProperty("expectedDataRecords").GetInt32());
        }
    }

    [Fact]
    public async Task EmitCrossFormatAsync_CanceledAfterDat_ClosesStreamAndDoesNotOpenAuditOrOpt()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new FileGenerationRequest
        {
            LoadfileOnly = true,
            Output = new OutputConfig { FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42 },
            Chaos = new ChaosConfig { ChaosMode = true, ChaosScenario = "cross-format-desync", ChaosAmount = "1" },
        };
        var opened = new List<string>();
        var stream = new CancelOnDisposeStream(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoadFileOrchestrator.EmitCrossFormatAsync(
            request, request, [], WriterMode.LoadfileOnly,
            "loadfile.dat", "loadfile.opt", "loadfile_properties.json", "loadfile.opt_properties.json",
            target => { opened.Add(target); return stream; }, cancellation.Token));
        Assert.Equal(new[] { "loadfile.dat" }, opened);
        Assert.False(stream.CanWrite);
    }

    private sealed class CancelOnDisposeStream(CancellationTokenSource cancellation) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                cancellation.Cancel();
            }
        }
    }

}
