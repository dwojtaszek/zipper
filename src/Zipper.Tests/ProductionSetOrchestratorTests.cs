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

    [Fact]
    public async Task GenerateAsync_OnPartialProductionZipFailure_CleansUpPartialArchiveAndPreservesSentinels()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "ProdZipPartialFail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sentinelBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            var sentinelPath = Path.Combine(tempDir, "unowned_sentinel.bin");
            await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

            var request = CreateRequest(count: 2, fileType: "pdf", outputPath: tempDir);
            request.Production = request.Production with { ProductionZip = true, ProductionId = "PROD_PARTIAL" };

            var decorator = new FaultingMaterializerDecorator(new ProductionFileMaterializer())
            {
                OnCreateZipAsync = async (sourceDir, zipPath, method, ct) =>
                {
                    var partialBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x01, 0x02 };
                    await File.WriteAllBytesAsync(zipPath, partialBytes, ct);
                    Assert.True(File.Exists(zipPath));
                    throw new IOException("Simulated disk fault while writing production zip");
                },
            };

            await Assert.ThrowsAsync<IOException>(() =>
                ProductionSetOrchestrator.GenerateAsync(request, decorator, new HashComputer()));

            var prodZipPath = Path.Combine(tempDir, "PROD_PARTIAL.zip");
            var prodDirPath = Path.Combine(tempDir, "PROD_PARTIAL");

            Assert.False(File.Exists(prodZipPath), "Partial owned production zip must be cleaned up on failure.");
            Assert.False(Directory.Exists(prodDirPath), "Owned production directory must be cleaned up on failure.");
            Assert.True(File.Exists(sentinelPath), "Unowned sentinel file must be preserved.");
            var actualSentinelBytes = await File.ReadAllBytesAsync(sentinelPath);
            Assert.Equal(sentinelBytes, actualSentinelBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GenerateAsync_RollingSetNativeWriteFailure_CleansUpCompletedEarlierSetsAndPreservesSentinels()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "ProdRollingFail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sentinelBytes = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            var sentinelPath = Path.Combine(tempDir, "unowned_sentinel.bin");
            await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

            var request = CreateRequest(count: 2, fileType: "pdf", outputPath: tempDir);
            request.Production = request.Production with
            {
                ProductionZip = true,
                ProductionId = "PROD_1,PROD_2",
                RollingCount = 2,
            };

            var decorator = new FaultingMaterializerDecorator(new ProductionFileMaterializer())
            {
                OnWriteBytesAsync = (path, content, ct) =>
                {
                    if (path.Contains("PROD_2", StringComparison.Ordinal) && path.Contains("NATIVES", StringComparison.Ordinal))
                    {
                        throw new IOException("Simulated disk error writing set 2 native file");
                    }
                    return Task.CompletedTask;
                },
            };

            await Assert.ThrowsAsync<IOException>(() =>
                ProductionSetOrchestrator.GenerateAsync(request, decorator, new HashComputer()));

            var prod1DirPath = Path.Combine(tempDir, "PROD_1");
            var prod1ZipPath = Path.Combine(tempDir, "PROD_1.zip");
            var prod2DirPath = Path.Combine(tempDir, "PROD_2");
            var prod2ZipPath = Path.Combine(tempDir, "PROD_2.zip");

            Assert.False(Directory.Exists(prod1DirPath), "Completed owned set 1 directory must be cleaned up when later set fails.");
            Assert.False(File.Exists(prod1ZipPath), "Completed owned set 1 zip must be cleaned up when later set fails.");
            Assert.False(Directory.Exists(prod2DirPath), "Partial owned set 2 directory must be cleaned up when later set fails.");
            Assert.False(File.Exists(prod2ZipPath), "Set 2 zip must not exist.");
            Assert.True(File.Exists(sentinelPath), "Unowned sentinel file must be preserved.");
            var actualSentinelBytes = await File.ReadAllBytesAsync(sentinelPath);
            Assert.Equal(sentinelBytes, actualSentinelBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenerateAsync_PreExistingLaterSetOutput_FailsUpfrontWithoutCreatingNewArtifacts(bool preCreateZip)
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "ProdPreExistingLater_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var outerSentinelBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
            var outerSentinelPath = Path.Combine(tempDir, "unowned_outer.bin");
            await File.WriteAllBytesAsync(outerSentinelPath, outerSentinelBytes);

            var laterCollisionBytes = new byte[] { 0x55, 0x66, 0x77, 0x88 };
            var prod2DirPath = Path.Combine(tempDir, "PROD_2");
            var prod2ZipPath = Path.Combine(tempDir, "PROD_2.zip");
            var internalSentinelPath = Path.Combine(prod2DirPath, "pre_existing_data.bin");

            if (preCreateZip)
            {
                await File.WriteAllBytesAsync(prod2ZipPath, laterCollisionBytes);
            }
            else
            {
                Directory.CreateDirectory(prod2DirPath);
                await File.WriteAllBytesAsync(internalSentinelPath, laterCollisionBytes);
            }

            var request = CreateRequest(count: 2, fileType: "pdf", outputPath: tempDir);
            request.Production = request.Production with
            {
                ProductionZip = true,
                ProductionId = "PROD_1,PROD_2",
                RollingCount = 2,
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProductionSetOrchestrator.GenerateAsync(request, new ProductionFileMaterializer(), new HashComputer()));

            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);

            var prod1DirPath = Path.Combine(tempDir, "PROD_1");
            var prod1ZipPath = Path.Combine(tempDir, "PROD_1.zip");

            Assert.False(Directory.Exists(prod1DirPath), "Set 1 directory must not have been created on upfront failure.");
            Assert.False(File.Exists(prod1ZipPath), "Set 1 zip must not have been created on upfront failure.");

            if (preCreateZip)
            {
                Assert.True(File.Exists(prod2ZipPath), "Pre-existing later set zip must remain.");
                var actualZipBytes = await File.ReadAllBytesAsync(prod2ZipPath);
                Assert.Equal(laterCollisionBytes, actualZipBytes);
            }
            else
            {
                Assert.True(Directory.Exists(prod2DirPath), "Pre-existing later set directory must remain.");
                Assert.True(File.Exists(internalSentinelPath), "Pre-existing internal file must remain.");
                var actualInternalBytes = await File.ReadAllBytesAsync(internalSentinelPath);
                Assert.Equal(laterCollisionBytes, actualInternalBytes);
            }

            Assert.True(File.Exists(outerSentinelPath), "Outer sentinel file must be preserved.");
            var actualOuterBytes = await File.ReadAllBytesAsync(outerSentinelPath);
            Assert.Equal(outerSentinelBytes, actualOuterBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenerateAsync_UnownedLaterSetCollisionAfterUpfrontChecks_CleansOwnedEarlierSetsAndPreservesCollision(bool collideZip)
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "ProdLaterCollision_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var outerSentinelBytes = new byte[] { 0x99, 0x88, 0x77, 0x66 };
            var outerSentinelPath = Path.Combine(tempDir, "unowned_outer.bin");
            await File.WriteAllBytesAsync(outerSentinelPath, outerSentinelBytes);

            var collisionSentinelBytes = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
            var prod2DirPath = Path.Combine(tempDir, "PROD_2");
            var prod2ZipPath = Path.Combine(tempDir, "PROD_2.zip");
            var internalCollisionPath = Path.Combine(prod2DirPath, "unowned_internal.bin");

            var request = CreateRequest(count: 2, fileType: "pdf", outputPath: tempDir);
            request.Production = request.Production with
            {
                ProductionZip = true,
                ProductionId = "PROD_1,PROD_2",
                RollingCount = 2,
            };

            var innerMaterializer = new ProductionFileMaterializer();
            var decorator = new FaultingMaterializerDecorator(innerMaterializer);
            decorator.OnCreateZipAsync = async (sourceDir, zipPath, method, ct) =>
            {
                await innerMaterializer.CreateZipAsync(sourceDir, zipPath, method, ct);

                if (collideZip)
                {
                    await File.WriteAllBytesAsync(prod2ZipPath, collisionSentinelBytes, ct);
                }
                else
                {
                    Directory.CreateDirectory(prod2DirPath);
                    await File.WriteAllBytesAsync(internalCollisionPath, collisionSentinelBytes, ct);
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProductionSetOrchestrator.GenerateAsync(request, decorator, new HashComputer()));

            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);

            var prod1DirPath = Path.Combine(tempDir, "PROD_1");
            var prod1ZipPath = Path.Combine(tempDir, "PROD_1.zip");

            Assert.False(Directory.Exists(prod1DirPath), "Completed owned set 1 directory must be cleaned up when set 2 collides.");
            Assert.False(File.Exists(prod1ZipPath), "Completed owned set 1 zip must be cleaned up when set 2 collides.");

            if (collideZip)
            {
                Assert.True(File.Exists(prod2ZipPath), "Unowned colliding zip must be preserved.");
                var actualZipBytes = await File.ReadAllBytesAsync(prod2ZipPath);
                Assert.Equal(collisionSentinelBytes, actualZipBytes);
            }
            else
            {
                Assert.True(Directory.Exists(prod2DirPath), "Unowned colliding directory must be preserved.");
                Assert.True(File.Exists(internalCollisionPath), "Unowned file in colliding directory must be preserved.");
                var actualInternalBytes = await File.ReadAllBytesAsync(internalCollisionPath);
                Assert.Equal(collisionSentinelBytes, actualInternalBytes);
            }

            Assert.True(File.Exists(outerSentinelPath), "Outer sentinel file must be preserved.");
            var actualOuterBytes = await File.ReadAllBytesAsync(outerSentinelPath);
            Assert.Equal(outerSentinelBytes, actualOuterBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private sealed class FaultingMaterializerDecorator : IFileMaterializer
    {
        private readonly IFileMaterializer _inner;

        public FaultingMaterializerDecorator(IFileMaterializer inner)
        {
            this._inner = inner;
        }

        public IFileMaterializer Inner => this._inner;

        public Func<string, byte[], CancellationToken, Task>? OnWriteBytesAsync { get; set; }

        public Func<string, string, Config.ZipCompressionMethod, CancellationToken, Task>? OnCreateZipAsync { get; set; }

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
            => this._inner.CreateDirectoryAsync(path, cancellationToken);

        public async Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        {
            if (this.OnWriteBytesAsync is not null)
            {
                await this.OnWriteBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
            }

            await this._inner.WriteBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
        }

        public Task WriteTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default)
            => this._inner.WriteTextAsync(path, text, encoding, cancellationToken);

        public Stream OpenWriteStream(string path)
            => this._inner.OpenWriteStream(path);

        public void AddFileData(FileData data)
            => this._inner.AddFileData(data);

        public Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default)
            => this._inner.WriteChildAttachmentAsync(childNativePath, attach, cancellationToken);

        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
            => this._inner.DeleteDirectoryAsync(path, cancellationToken);

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
            => this._inner.DeleteFileAsync(path, cancellationToken);

        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
            => this._inner.DirectoryExistsAsync(path, cancellationToken);

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
            => this._inner.FileExistsAsync(path, cancellationToken);

        public Task CreateZipAsync(string sourceDir, string zipPath, CancellationToken cancellationToken = default)
            => this.CreateZipAsync(sourceDir, zipPath, Config.ZipCompressionMethod.Deflate, cancellationToken);

        public async Task CreateZipAsync(string sourceDir, string zipPath, Config.ZipCompressionMethod method, CancellationToken cancellationToken = default)
        {
            if (this.OnCreateZipAsync is not null)
            {
                await this.OnCreateZipAsync(sourceDir, zipPath, method, cancellationToken).ConfigureAwait(false);
                return;
            }

            await this._inner.CreateZipAsync(sourceDir, zipPath, method, cancellationToken).ConfigureAwait(false);
        }
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
