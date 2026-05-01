using Xunit;

namespace Zipper
{
    public class IntegrationTests
    {
        [Fact]
        public async Task Main_WithParallelGeneration_ShouldCreateValidArchive()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var args = new[] { "--type", "pdf", "--count", "100", "--output-path", tempDir, "--folders", "5" };
                var result = await Program.Main(args);

                Assert.Equal(0, result);

                var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
                Assert.NotNull(zipFile);

                using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
                Assert.Equal(100, archive.Entries.Count);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Main_WithEmlAndAttachments_ShouldCreateArchive()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var args = new[] { "--type", "eml", "--count", "10", "--output-path", tempDir, "--attachment-rate", "50", "--seed", "42" };
                var result = await Program.Main(args);

                Assert.Equal(0, result);

                var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
                Assert.NotNull(zipFile);

                using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
                Assert.True(archive.Entries.Count >= 10);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Main_WithTiffPageRange_ShouldCreateArchive()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var args = new[] { "--type", "tiff", "--count", "10", "--output-path", tempDir, "--tiff-pages", "1-5", "--seed", "42" };
                var result = await Program.Main(args);

                Assert.Equal(0, result);

                var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
                Assert.NotNull(zipFile);

                using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
                Assert.Equal(10, archive.Entries.Count);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Main_WithLoadfileOnly_ShouldCreateLoadFileOnly()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var args = new[] { "--loadfile-only", "--count", "10", "--output-path", tempDir, "--loadfile-format", "dat" };
                var result = await Program.Main(args);

                Assert.Equal(0, result);

                Assert.Empty(Directory.GetFiles(tempDir, "*.zip"));
                Assert.Single(Directory.GetFiles(tempDir, "*.dat"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Main_WithChaosMode_ShouldCreateLoadFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var args = new[] { "--loadfile-only", "--count", "10", "--output-path", tempDir, "--loadfile-format", "dat", "--chaos-mode", "--chaos-amount", "10%" };
                var result = await Program.Main(args);

                Assert.Equal(0, result);
                Assert.Single(Directory.GetFiles(tempDir, "*.dat"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Main_WithTargetZipSize_ShouldCreateArchiveNearTarget()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var args = new[] { "--type", "pdf", "--count", "200", "--output-path", tempDir, "--target-zip-size", "2MB" };
                var result = await Program.Main(args);

                Assert.Equal(0, result);

                var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
                Assert.NotNull(zipFile);

                var fileInfo = new FileInfo(zipFile!);
                Assert.True(fileInfo.Length > 1024 * 1024);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Main_ConcurrentGeneration_ShouldHandleParallelRuns()
        {
            var tempDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var tempDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir1);
            Directory.CreateDirectory(tempDir2);

            try
            {
                var args1 = new[] { "--type", "pdf", "--count", "20", "--output-path", tempDir1, "--seed", "42" };
                var args2 = new[] { "--type", "pdf", "--count", "30", "--output-path", tempDir2, "--seed", "99" };

                var task1 = Program.Main(args1);
                var task2 = Program.Main(args2);

                var results = await Task.WhenAll(task1, task2);

                Assert.Equal(0, results[0]);
                Assert.Equal(0, results[1]);

                var zip1 = Directory.GetFiles(tempDir1, "*.zip").FirstOrDefault();
                var zip2 = Directory.GetFiles(tempDir2, "*.zip").FirstOrDefault();
                Assert.NotNull(zip1);
                Assert.NotNull(zip2);

                using var archive1 = System.IO.Compression.ZipFile.OpenRead(zip1!);
                using var archive2 = System.IO.Compression.ZipFile.OpenRead(zip2!);
                Assert.Equal(20, archive1.Entries.Count);
                Assert.Equal(30, archive2.Entries.Count);
            }
            finally
            {
                if (Directory.Exists(tempDir1))
                {
                    Directory.Delete(tempDir1, true);
                }

                if (Directory.Exists(tempDir2))
                {
                    Directory.Delete(tempDir2, true);
                }
            }
        }
    }
}
