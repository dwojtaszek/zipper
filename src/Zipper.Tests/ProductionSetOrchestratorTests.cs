using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zipper;
using Zipper.Config;

namespace Zipper.Tests;

public class ProductionSetOrchestratorTests
{
    [Fact]
    public async Task GenerateAsync_DelegatesDiskWrites_ToMaterializer()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        var orchestrator = new ProductionSetOrchestrator(materializer, new HashComputer());

        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.NotEmpty(materializer.CreatedDirectories);
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("DATA"));
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("NATIVES"));
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("TEXT"));
        Assert.Contains(materializer.CreatedDirectories, p => p.EndsWith("IMAGES"));

        var nativeWrite = Assert.Single(materializer.WrittenBytes, w => w.path.EndsWith(".pdf"));
        Assert.NotEmpty(nativeWrite.content);

        var textWrite = Assert.Single(materializer.WrittenTexts, w => w.path.EndsWith(".txt"));
        Assert.Contains("Extracted text", textWrite.text);

        var imageWrite = Assert.Single(materializer.WrittenBytes, w => w.path.EndsWith(".tif"));
        Assert.NotEmpty(imageWrite.content);

        Assert.Contains(materializer.WrittenBytes, w => w.path.Contains("VOL001"));
    }

    [Fact]
    public async Task GenerateAsync_RedactedMode_DelegatesRedactedFiles_ToMaterializer()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Production = request.Production with { RedactedProduction = true };
        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.Contains(materializer.CreatedDirectories, p => p.Contains("REDACTED"));
        var redactedImage = Assert.Single(materializer.WrittenBytes, w => w.path.Contains("REDACTED") && w.path.EndsWith(".tif"));
        var redactedText = Assert.Single(materializer.WrittenTexts, w => w.path.Contains("REDACTED") && w.path.EndsWith(".txt"));
        Assert.Contains("Redacted text", redactedText.text);
    }

    [Fact]
    public async Task GenerateAsync_HashComputation_UsesSharedHashComputer()
    {
        var materializer = new FakeMaterializer();
        var request = CreateRequest(count: 1, fileType: "pdf", outputPath: Path.GetTempPath());
        request.Hash = request.Hash with { Mode = Config.HashMode.Actual, Algorithms = new List<Config.HashAlgorithm> { Config.HashAlgorithm.MD5 } };
        var hashComputer = new HashComputer();
        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, hashComputer);

        var fileData = Assert.Single(materializer.FileDataItems);
        Assert.False(string.IsNullOrEmpty(fileData.Hash));
        Assert.Equal(32, fileData.Hash.Length);
    }

    private sealed class FakeMaterializer : IFileMaterializer
    {
        public List<string> CreatedDirectories { get; } = new();
        public List<(string path, byte[] content)> WrittenBytes { get; } = new();
        public List<(string path, string text, string encoding)> WrittenTexts { get; } = new();
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

        public Task WriteTextAsync(string path, string text, string encoding, CancellationToken cancellationToken = default)
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
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task CreateZipAsync(string sourceDir, string zipPath, CancellationToken cancellationToken = default)
        {
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
                IsEnabled = false,
                Mode = Config.HashMode.Actual,
                Algorithms = new List<Config.HashAlgorithm>(),
            },
            LoadFile = new LoadFileConfig
            {
                Encoding = "UTF-8",
            },
        };
    }
}
