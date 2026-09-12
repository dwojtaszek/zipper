using System.Text;
using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class ProductionSetOrchestratorTests
{
    [Fact]
    public async Task GenerateAsync_DelegatesDiskWrites_ToMaterializer()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());

        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.NotEmpty(materializer.CreatedDirectories);
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("DATA", System.StringComparison.Ordinal));
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("NATIVES", System.StringComparison.Ordinal));
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("TEXT", System.StringComparison.Ordinal));
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("IMAGES", System.StringComparison.Ordinal));

        var nativeWrite = Assert.Single(materializer.WrittenBytes, w => w.path.EndsWith(".pdf", System.StringComparison.Ordinal));
        Assert.NotEmpty(nativeWrite.content);

        var textWrite = Assert.Single(materializer.WrittenTexts, w => w.path.EndsWith(".txt", System.StringComparison.Ordinal));
        Assert.Contains("Extracted text", textWrite.text, System.StringComparison.Ordinal);

        var imageWrite = Assert.Single(materializer.WrittenBytes, w => w.path.EndsWith(".tif", System.StringComparison.Ordinal));
        Assert.NotEmpty(imageWrite.content);

        Assert.Contains(materializer.WrittenBytes, w => w.path.Contains("VOL001", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_RedactedMode_DelegatesRedactedFiles_ToMaterializer()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Output = request.Output with { WithText = true };
        request.Production = request.Production with { RedactedProduction = true };
        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.Contains(materializer.CreatedDirectories, p => p.Contains("REDACTED", System.StringComparison.Ordinal));
        var redactedImage = Assert.Single(materializer.WrittenBytes, w => w.path.Contains("REDACTED", System.StringComparison.Ordinal) && w.path.EndsWith(".tif", System.StringComparison.Ordinal));
        var redactedText = Assert.Single(materializer.WrittenTexts, w => w.path.Contains("REDACTED", System.StringComparison.Ordinal) && w.path.EndsWith(".txt", System.StringComparison.Ordinal));
        Assert.Contains("Redacted text", redactedText.text, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_HashComputation_UsesSharedHashComputer()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Hash = request.Hash with { Mode = Config.HashMode.Actual, Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5 } };
        var hashComputer = new HashComputer();
        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, hashComputer);

        var fileData = Assert.Single(materializer.FileDataItems);
        Assert.False(string.IsNullOrEmpty(fileData.Hash));
        Assert.Equal(32, fileData.Hash.Length);
    }

    [Fact]
    public async Task GenerateAsync_SeededGoldenBaseline_MatchesGoldenParity()
    {
        var tempDir1 = Path.Combine(Directory.GetCurrentDirectory(), "ProdSetGolden1_" + Guid.NewGuid().ToString("N"));
        var tempDir2 = Path.Combine(Directory.GetCurrentDirectory(), "ProdSetGolden2_" + Guid.NewGuid().ToString("N"));
        try
        {
            var request1 = CreateRequest(count: 3, fileType: "pdf", outputPath: tempDir1);
            request1.Metadata = new MetadataConfig { Seed = 42 };
            request1.Bates = new BatesNumberConfig { Prefix = "GOLD", Start = 1, Digits = 6, Increment = 1 };
            request1.Production = request1.Production with { ProductionId = "GOLDPROD", VolumeSize = 2 };

            var result1 = await ProductionSetOrchestrator.GenerateAsync(
                request1,
                new ProductionFileMaterializer(),
                new HashComputer());

            var request2 = CreateRequest(count: 3, fileType: "pdf", outputPath: tempDir2);
            request2.Metadata = new MetadataConfig { Seed = 42 };
            request2.Bates = new BatesNumberConfig { Prefix = "GOLD", Start = 1, Digits = 6, Increment = 1 };
            request2.Production = request2.Production with { ProductionId = "GOLDPROD", VolumeSize = 2 };

            var result2 = await ProductionSetOrchestrator.GenerateAsync(
                request2,
                new ProductionFileMaterializer(),
                new HashComputer());

            Assert.NotNull(result1);
            Assert.NotNull(result2);

            var manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(result1.ProductionPath);
            var manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(result2.ProductionPath);

            Assert.NotEmpty(manifest1);
            var lines1 = manifest1.Split('\n').Where(l => !l.EndsWith(".json", StringComparison.Ordinal)).ToArray();
            var lines2 = manifest2.Split('\n').Where(l => !l.EndsWith(".json", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(lines1);
            Assert.Equal(lines1, lines2);
            Assert.Contains("DATA/loadfile.dat", manifest1, StringComparison.Ordinal);
            Assert.Contains("DATA/loadfile.opt", manifest1, StringComparison.Ordinal);
            Assert.Contains("NATIVES/VOL001/GOLD000001.pdf", manifest1, StringComparison.Ordinal);
            Assert.Contains("NATIVES/VOL002/GOLD000003.pdf", manifest1, StringComparison.Ordinal);
            Assert.Contains("TEXT/VOL001/GOLD000001.txt", manifest1, StringComparison.Ordinal);
            Assert.Contains("IMAGES/VOL001/GOLD000001.tif", manifest1, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir1))
            {
                Directory.Delete(tempDir1, true);
            }

            if (Directory.Exists(tempDir2))
            {
                Directory.Delete(tempDir2, true);
            }
        }
    }

    [Fact]
    public async Task GenerateAsync_WithFamiliesAndEml_GeneratesChildAttachments()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "eml", outputPath: Path.GetTempPath());
        request.Metadata = request.Metadata with { WithFamilies = true, Seed = 42 };
        request.LoadFile = request.LoadFile with { AttachmentRate = 100 };

        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.NotEmpty(materializer.Attachments);
        Assert.Contains(materializer.WrittenTexts, w => w.path.Contains("_A001.txt", StringComparison.Ordinal));
        Assert.Contains(materializer.WrittenBytes, w => w.path.Contains("_A001.tif", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_WithSourceRecordsAndOriginalsMode_CreatesSourceDirectories()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 2, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Production = request.Production with { SourcePathMode = SourcePathMode.Originals };
        request.SourceRecords = new List<Zipper.SourceInput.SourceRecord>
        {
            new() { RelativePath = "nested/folder/doc1.pdf", FileType = "pdf" },
            new() { RelativePath = "root.pdf", FileType = "pdf" },
        };

        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.Contains(materializer.CreatedDirectories, d => d.Contains("nested/folder", StringComparison.Ordinal) || d.Contains("nested\\folder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_WhenDirectoryExists_ThrowsInvalidOperationException()
    {
        var materializer = new FakeMaterializer { DirectoryExistsResult = true };
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer()));
    }

    [Fact]
    public async Task GenerateAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        var hashComputer = new HashComputer();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ProductionSetOrchestrator.GenerateAsync(null!, materializer, hashComputer));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ProductionSetOrchestrator.GenerateAsync(request, null!, hashComputer));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ProductionSetOrchestrator.GenerateAsync(request, materializer, null!));
    }

    [Fact]
    public async Task GenerateAsync_WithChaosModeAndNotLoadfileOnly_ThrowsInvalidOperationException()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Chaos = request.Chaos with { ChaosMode = true };
        request.LoadfileOnly = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer()));
    }

    [Fact]
    public async Task GenerateAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 5, fileType: "pdf", outputPath: Path.GetTempPath());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer(), cts.Token));
    }

    [Theory]
    [InlineData("omit-native-path", "")]
    [InlineData("replace-with-placeholder", "PLACEHOLDER/VOL001/DOC00000001.pdf")]
    public async Task GenerateAsync_RedactedMode_AppliesWithheldNativePolicy(string policy, string expectedOverride)
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Production = request.Production with { RedactedProduction = true, WithheldNativePolicy = policy };

        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        var fileData = Assert.Single(materializer.FileDataItems);
        Assert.Equal(expectedOverride, fileData.NativePathOverride);
    }

    [Fact]
    public async Task GenerateAsync_WithZip_CallsCreateZipAsync()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Production = request.Production with { ProductionZip = true };

        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.NotNull(result.ZipFilePath);
        Assert.True(materializer.ZipCreated);
    }

    [Fact]
    public async Task GenerateAsync_WhenProductionZipExists_ThrowsInvalidOperationException()
    {
        var materializer = new FakeMaterializer { FileExistsResult = true };
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Production = request.Production with { ProductionZip = true };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer()));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(materializer.CreatedDirectories);
        Assert.Empty(materializer.DeletedFiles);
    }

    private sealed class FakeMaterializer : IFileMaterializer
    {
        public bool DirectoryExistsResult { get; set; }
        public bool FileExistsResult { get; set; }
        public List<string> CreatedDirectories { get; } = new();
        public List<string> DeletedDirectories { get; } = new();
        public List<string> DeletedFiles { get; } = new();
        public List<(string path, byte[] content)> WrittenBytes { get; } = new();
        public List<(string path, string text, Encoding encoding)> WrittenTexts { get; } = new();
        public List<FileData> FileDataItems { get; } = new();
        public List<(string path, byte[] content)> Attachments { get; } = new();

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            this.CreatedDirectories.Add(path);
            return Task.CompletedTask;
        }

        public Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        {
            this.WrittenBytes.Add((path, content));
            return Task.CompletedTask;
        }

        public Task WriteTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default)
        {
            this.WrittenTexts.Add((path, text, encoding));
            return Task.CompletedTask;
        }

        public Stream OpenWriteStream(string path)
        {
            return new FakeStream();
        }

        public Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream>(new FakeStream());
        }

        public void AddFileData(FileData data)
        {
            this.FileDataItems.Add(data);
        }

        public Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default)
        {
            this.Attachments.Add((childNativePath, attach.content));
            return Task.CompletedTask;
        }

        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            this.DeletedDirectories.Add(path);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
        {
            this.DeletedFiles.Add(path);
            return Task.CompletedTask;
        }

        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(this.DirectoryExistsResult);
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(this.FileExistsResult);
        }

        public bool ZipCreated { get; private set; }

        public Task CreateZipAsync(string sourceDir, string zipPath, CancellationToken cancellationToken = default)
        {
            this.ZipCreated = true;
            return Task.CompletedTask;
        }
    }


    private sealed class FakeStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    private static FileGenerationRequest CreateRequest(int count, string fileType, string outputPath)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = outputPath,
                FileCount = count,
                FileType = fileType,
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 5000,
                RedactedProduction = false,
                WithheldNativePolicy = "keep-native",
                SourcePathMode = Config.SourcePathMode.Bates,
            },
            Metadata = new MetadataConfig { Seed = null },
            Bates = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 1,
                Digits = 8,
                Increment = 1,
            },
            Hash = new HashConfig
            {
                Mode = Config.HashMode.Actual,
                Algorithms = new HashSet<Config.HashAlgorithm>(),
            },
            LoadFile = new LoadFileConfig
            {
                Encoding = "UTF-8",
            },
        };
    }
}
