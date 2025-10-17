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
                Assert.Equal(10, archive.Entries.Count - 1); // -1 for load file
            }
            finally
            {
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, true);
            }
        }
    }
}