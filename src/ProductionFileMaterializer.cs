using System.IO.Compression;
using System.Text;

namespace Zipper;

/// <summary>
/// Real filesystem-backed <see cref="IFileMaterializer"/> implementation.
/// </summary>
internal sealed class ProductionFileMaterializer : IFileMaterializer
{
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        return File.WriteAllBytesAsync(path, content, cancellationToken);
    }

    public Task WriteTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default)
    {
        return File.WriteAllTextAsync(path, text, encoding, cancellationToken);
    }

    public Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, useAsync: true));
    }

    public Stream OpenWriteStream(string path)
    {
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, useAsync: true);
    }

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Directory.Exists(path));
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(path));
    }

    public Task CreateZipAsync(string sourceDir, string zipPath, CancellationToken cancellationToken = default)
    {
        ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, true);
        return Task.CompletedTask;
    }

    public void AddFileData(FileData data)
    {
    }

    public Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default)
    {
        return File.WriteAllBytesAsync(childNativePath, attach.content, cancellationToken);
    }
}
