using Xunit;

namespace Zipper
{
    [Collection("ConsoleTests")]
    public class ProgramTests
    {
        private static async Task<int> RunWithRedirectedConsole(Func<Task<int>> action)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);
                return await action();
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        [Fact]
        public async Task Main_WithHelpFlag_ReturnsOne()
        {
            // Arrange
            string[] args = { "--help" };

            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(1, exitCode); // Help shows usage and returns 1
        }

        [Fact]
        public async Task Main_WithInvalidArguments_ReturnsErrorCode()
        {
            // Arrange - Missing required arguments
            string[] args = { };

            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(1, exitCode); // Should return error code for invalid args
        }

        [Fact]
        public async Task Main_WithMissingOutputPath_ReturnsErrorCode()
        {
            // Arrange - Missing required --output argument
            string[] args = { "--count", "10", "--type", "pdf" };

            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(1, exitCode); // Should return error code
        }

        [Fact]
        public async Task Main_WithInvalidFileType_ReturnsErrorCode()
        {
            // Arrange
            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string[] args =
            {
                "--output", tempPath,
                "--count", "1",
                "--type", "invalid_type",
            };

            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(1, exitCode); // Should return error code for invalid type

            // Cleanup
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }

        [Fact]
        public async Task Main_WithInvalidCount_ReturnsErrorCode()
        {
            // Arrange
            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string[] args =
            {
                "--output", tempPath,
                "--count", "-1",  // Invalid count
                "--type", "pdf",
            };

            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(1, exitCode); // Should return error code

            // Cleanup
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }

        [Fact]
        public async Task Main_WithInvalidNumericArgument_ReturnsErrorCode()
        {
            // Arrange
            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string[] args =
            {
                "--output", tempPath,
                "--count", "abc",  // Invalid count (must be integer string)
                "--type", "pdf",
            };

            try
            {
                // Act
                int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

                // Assert
                Assert.Equal(1, exitCode); // Should return error code
            }
            finally
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
        }

        [Fact]
        public async Task Main_WithBenchmarkFlag_ReturnsZero()
        {
            // Arrange
            string[] args = { "--benchmark" };

            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(0, exitCode);
        }

        [Fact]
        public async Task Main_WithSameSeed_ProducesIdenticalOutputSizes()
        {
            // Arrange
            string tempPath1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string tempPath2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                string[] args1 =
                {
                    "--output-path", tempPath1,
                    "--count", "10",
                    "--type", "pdf",
                    "--seed", "42",
                    "--with-metadata",
                    "--with-text"
                };

                string[] args2 =
                {
                    "--output-path", tempPath2,
                    "--count", "10",
                    "--type", "pdf",
                    "--seed", "42",
                    "--with-metadata",
                    "--with-text"
                };

                // Act
                int exitCode1 = await RunWithRedirectedConsole(() => Program.Main(args1));
                int exitCode2 = await RunWithRedirectedConsole(() => Program.Main(args2));

                // Assert exit codes
                Assert.Equal(0, exitCode1);
                Assert.Equal(0, exitCode2);

                // Assert file contents
                var files1 = Directory.GetFiles(tempPath1, "*.*", SearchOption.AllDirectories)
                                      .Select(f => Path.GetRelativePath(tempPath1, f)).OrderBy(n => n).ToList();
                var files2 = Directory.GetFiles(tempPath2, "*.*", SearchOption.AllDirectories)
                                      .Select(f => Path.GetRelativePath(tempPath2, f)).OrderBy(n => n).ToList();

                Assert.Equal(files1, files2);

                using var sha256 = System.Security.Cryptography.SHA256.Create();

                foreach (var relFile in files1)
                {
                    if (relFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        relFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                    {
                        // Output zip and dat filenames contain timestamps, ignore them in this test or compare internal contents.
                        // Here we just test generated raw documents if any were outputted outside ZIP.
                        continue;
                    }

                    var file1Path = Path.Combine(tempPath1, relFile);
                    var file2Path = Path.Combine(tempPath2, relFile);

                    using var fs1 = File.OpenRead(file1Path);
                    using var fs2 = File.OpenRead(file2Path);

                    // 1. Compare header length
                    Assert.Equal(fs1.Length, fs2.Length);

                    // 2. Compare first 16 bytes
                    var header1 = new byte[Math.Min(16, fs1.Length)];
                    var header2 = new byte[Math.Min(16, fs2.Length)];

                    _ = fs1.Read(header1, 0, header1.Length);
                    _ = fs2.Read(header2, 0, header2.Length);

                    Assert.Equal(header1, header2);

                    // 3. Compare full file hash
                    fs1.Position = 0;
                    fs2.Position = 0;

                    var hash1 = sha256.ComputeHash(fs1);
                    var hash2 = sha256.ComputeHash(fs2);

                    Assert.Equal(hash1, hash2);
                }
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempPath1))
                {
                    Directory.Delete(tempPath1, true);
                }

                if (Directory.Exists(tempPath2))
                {
                    Directory.Delete(tempPath2, true);
                }
            }
        }
    }
}
