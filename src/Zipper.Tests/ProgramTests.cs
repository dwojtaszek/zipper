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

            try
            {
                // Act
                int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

                // Assert
                Assert.Equal(1, exitCode); // Should return error code for invalid type
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
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

            try
            {
                // Act
                int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

                // Assert
                Assert.Equal(1, exitCode); // Should return error code
            }
            finally
            {
                // Cleanup
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
    }
}
