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
        => this.CreateZipAsync(sourceDir, zipPath, Config.ZipCompressionMethod.Deflate, cancellationToken);

    public async Task CreateZipAsync(string sourceDir, string zipPath, Config.ZipCompressionMethod method, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var level = method == Config.ZipCompressionMethod.Store ? CompressionLevel.NoCompression : CompressionLevel.Optimal;

        var fullSource = Path.GetFullPath(sourceDir);
        var sourceInfo = new DirectoryInfo(fullSource);
        if (!sourceInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: '{sourceDir}'");
        }

        var parent = sourceInfo.Parent;
        var basePath = parent?.FullName ?? fullSource;

        try
        {
            await using (var zipStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, useAsync: true))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                bool hasEntries = false;
                foreach (var item in sourceInfo.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hasEntries = true;

                    var relativePath = Path.GetRelativePath(basePath, item.FullName).Replace('\\', '/');
                    if (item is FileInfo fileInfo)
                    {
                        var entry = archive.CreateEntry(relativePath, level);
                        // Match ZipFile.CreateFromDirectory, which stored the local file time;
                        // using UTC here would change entry bytes on non-UTC hosts.
                        entry.LastWriteTime = new DateTimeOffset(fileInfo.LastWriteTime);
                        await using var entryStream = entry.Open();
                        await using var fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, PerformanceConstants.DefaultBufferSize, useAsync: true);
                        await fileStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                    }
                    else if (item is DirectoryInfo subDir)
                    {
                        if (!subDir.EnumerateFileSystemInfos().Any())
                        {
                            archive.CreateEntry(relativePath + "/");
                        }
                    }
                }

                if (!hasEntries)
                {
                    var relativePath = Path.GetRelativePath(basePath, fullSource).Replace('\\', '/');
                    archive.CreateEntry(relativePath + "/");
                }
            }
        }
        catch
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
                // Preserve original exception
            }

            throw;
        }
    }

    public void AddFileData(FileData data)
    {
    }

    public Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default)
    {
        return File.WriteAllBytesAsync(childNativePath, attach.content, cancellationToken);
    }
}
