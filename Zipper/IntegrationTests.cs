using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Zipper
{
    public class IntegrationTests
    {
        [Fact]
        public async Task Main_WithParallelGeneration_ShouldCreateValidArchive()
        {
            var tempDir = Path.GetTempPath();
            var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputPath);

            try
            {
                // Test the actual program entry point
                var args = new[] { "--type", "pdf", "--count", "100", "--output-path", outputPath, "--folders", "5" };
                var result = await Program.Main(args);

                Assert.Equal(0, result);

                // Verify files were created
                var files = Directory.GetFiles(outputPath);
                Assert.True(files.Length >= 1); // At least the zip file

                var zipFile = Array.Find(files, f => f.EndsWith(".zip"));
                Assert.NotNull(zipFile);

                // Verify zip contains files
                using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
                Assert.True(archive.Entries.Count > 0);
            }
            finally
            {
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, true);
            }
        }
    }
}