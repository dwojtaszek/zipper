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

            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(1, exitCode); // Should return error code
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
                await RunWithRedirectedConsole(() => Program.Main(args1));
                await RunWithRedirectedConsole(() => Program.Main(args2));

                // Assert
                var files1 = Directory.GetFiles(tempPath1, "*.*", SearchOption.AllDirectories)
                                      .Select(f => Path.GetFileName(f)).OrderBy(n => n).ToList();
                var files2 = Directory.GetFiles(tempPath2, "*.*", SearchOption.AllDirectories)
                                      .Select(f => Path.GetFileName(f)).OrderBy(n => n).ToList();

                Assert.Equal(files1, files2);

                if (files1.Count > 0)
                {
                    long size1 = files1.Sum(f => new FileInfo(Directory.GetFiles(tempPath1, f, SearchOption.AllDirectories).First()).Length);
                    long size2 = files2.Sum(f => new FileInfo(Directory.GetFiles(tempPath2, f, SearchOption.AllDirectories).First()).Length);

                    Assert.Equal(size1, size2);
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
