using System.IO;
using System.Text;

namespace Zipper;

/// <summary>
/// Abstraction for all file-system I/O performed during production set generation.
/// Implementations can write to the real filesystem (default) or to an in-memory
/// store for unit testing the orchestrator without touching disk.
/// </summary>
internal interface IFileMaterializer
{
    /// <summary>Creates a directory tree on disk (or registers it in a fake store).</summary>
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Writes raw bytes to the given path.</summary>
    Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Writes text content to the given path.</summary>
    Task WriteTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default);

    /// <summary>Opens an output stream for the target path (used by writers).</summary>
    Stream OpenWriteStream(string path);

    /// <summary>Records that a file was written (used by test fakes to capture output).</summary>
    void AddFileData(FileData data);

    /// <summary>Writes an EML attachment to the target native path.</summary>
    Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default);

    /// <summary>Deletes a directory tree if it exists.</summary>
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Deletes a file if it exists.</summary>
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Returns whether a directory exists.</summary>
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Returns whether a file exists.</summary>
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Creates a ZIP archive from a directory tree.</summary>
    Task CreateZipAsync(string sourceDir, string zipPath, CancellationToken cancellationToken = default);
}
