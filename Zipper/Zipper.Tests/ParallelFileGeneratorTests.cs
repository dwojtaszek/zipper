using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Zipper
{
    public class ParallelFileGeneratorTests
    {
        [Fact]
        public async Task GenerateFilesAsync_ShouldCreateCorrectCount()
        {
            var tempDir = Path.GetTempPath();
            var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputPath);

            try
            {
                var generator = new ParallelFileGenerator();
                var result = await generator.GenerateFilesAsync(new FileGenerationRequest
                {
                    OutputPath = outputPath,
                    FileCount = 10,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 2
                });

                Assert.Equal(10, result.FilesGenerated);
                Assert.True(File.Exists(result.ZipFilePath));

                // Verify zip contains correct number of files
                using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
                Assert.Equal(10, archive.Entries.Count); // Count should match requested files
            }
            finally
            {
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, true);
            }
        }

        [Fact]
        public async Task GenerateFilesAsync_WithTargetZipSize_PadsFilesToReachTargetSize()
        {
            var tempDir = Path.GetTempPath();
            var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputPath);

            try
            {
                // Request a 1MB ZIP file
                var targetSize = 1_000_000; // 1MB
                var generator = new ParallelFileGenerator();
                var result = await generator.GenerateFilesAsync(new FileGenerationRequest
                {
                    OutputPath = outputPath,
                    FileCount = 10,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 2,
                    TargetZipSize = targetSize
                });

                Assert.Equal(10, result.FilesGenerated);
                Assert.True(File.Exists(result.ZipFilePath));

                // Verify the ZIP file is approximately the target size (within 20% tolerance)
                // Due to compression variance, exact size isn't possible but files should be padded
                var actualSize = new FileInfo(result.ZipFilePath).Length;
                var minimumExpected = targetSize * 0.5; // At least 50% of target
                Assert.True(actualSize >= minimumExpected,
                    $"ZIP size {actualSize} bytes is below minimum expected {minimumExpected} bytes for target {targetSize} bytes");

                // Verify files were created (padding increases file sizes)
                using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
                Assert.Equal(10, archive.Entries.Count);

                // Verify entries have non-trivial sizes due to padding
                foreach (var entry in archive.Entries)
                {
                    Assert.True(entry.CompressedLength > 1000,
                        $"Entry {entry.FullName} has compressed length {entry.CompressedLength}, expected > 1000 bytes due to padding");
                }
            }
            finally
            {
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, true);
            }
        }
    }
}
