using System.Text;
using Zipper.Config;

namespace Zipper.Tests;

public abstract class TempDirectoryTestBase : IDisposable
{
    protected string TempDir { get; }

    protected TempDirectoryTestBase()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "zipper_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // The system stores canonical (symlink-free) paths for everything it writes, while
        // Path.GetTempPath() can sit below a symbolic link (macOS /var -> /private/var).
        // A canonical temp directory root is a fixed point of both Path.GetFullPath and
        // PathValidator.ResolveSecurePath, so raw expectations and resolved outputs agree.
        this.TempDir = TestPaths.Canonicalize(tempDir);
    }

    protected string GetTempFilePath() => Path.Combine(this.TempDir, $"zipper_{Guid.NewGuid():N}.tmp");

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (Directory.Exists(this.TempDir))
            {
                try
                {
                    Directory.Delete(this.TempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }
    }

    internal FileGenerationRequest CreateTestRequest(string fileType = "pdf")
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.TempDir,
                FileCount = 3,
                FileType = fileType,
                Folders = 1,
                WithText = false,
            },
            LoadFile = new LoadFileConfig
            {
                Encoding = "Unicode (UTF-8)",
                Formats = new List<LoadFileFormat> { LoadFileFormat.Dat },
            },
            Metadata = new MetadataConfig { WithMetadata = true },
        };
    }

    internal List<FileData> CreateTestFileData(int count = 3)
    {
        var fileList = new List<FileData>();
        for (int i = 1; i <= count; i++)
        {
            var contentBytes = Encoding.UTF8.GetBytes($"Test content {i}");
            var hashBytes = System.Security.Cryptography.MD5.HashData(contentBytes);
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            fileList.Add(new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = i,
                    FolderNumber = 1,
                    FileName = $"file_{i:D8}.pdf",
                    FilePathInZip = $"folder_001/file_{i:D8}.pdf",
                },
                Data = contentBytes,
                DataLength = contentBytes.Length,
                Hash = hash,
            });
        }

        return fileList;
    }
}
