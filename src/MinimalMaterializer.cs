using System.Text;

namespace Zipper;

/// <summary>
/// Minimal IFileMaterializer implementation that implements the required CreateZipAsync overload.
/// This demonstrates the fix for the mutual recursion bug in IFileMaterializer defaults.
/// </summary>
internal sealed class MinimalMaterializer : IFileMaterializer
{
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task WriteTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Stream OpenWriteStream(string path) => new MemoryStream();
    public void AddFileData(FileData data) { }
    public Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task CreateZipAsync(string sourceDir, string zipPath, Config.ZipCompressionMethod method, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
