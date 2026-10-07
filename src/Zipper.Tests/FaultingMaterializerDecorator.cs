using System.Text;

namespace Zipper.Tests;

/// <summary>
/// Decorator around <see cref="IFileMaterializer"/> that allows tests to intercept or inject
/// faults and cancellation hooks during filesystem and archive operations.
/// </summary>
internal sealed class FaultingMaterializerDecorator : IFileMaterializer
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
