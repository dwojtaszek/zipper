using Xunit;

namespace Zipper
{
    public class ProgramTests
    {
        [Fact]
        public async Task Main_WithHelpFlag_ReturnsOne()
        {
            // Arrange
            string[] args = { "--help" };

            // Act — redirect Console to avoid ObjectDisposedException in parallel tests
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);
                int exitCode = await Program.Main(args);

                // Assert
                Assert.Equal(1, exitCode); // Help shows usage and returns 1
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        [Fact]
        public async Task Main_WithInvalidArguments_ReturnsErrorCode()
        {
            // Arrange - Missing required arguments
            string[] args = { };

            // Act — redirect Console to avoid ObjectDisposedException in parallel tests
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);
                int exitCode = await Program.Main(args);

                // Assert
                Assert.Equal(1, exitCode); // Should return error code for invalid args
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        [Fact]
        public async Task Main_WithMissingOutputPath_ReturnsErrorCode()
        {
            // Arrange - Missing required --output argument
            string[] args = { "--count", "10", "--type", "pdf" };

            // Act — redirect Console to avoid ObjectDisposedException in parallel tests
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);
                int exitCode = await Program.Main(args);

                // Assert
                Assert.Equal(1, exitCode); // Should return error code
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
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

            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);

                // Act
                int exitCode = await Program.Main(args);

                // Assert
                Assert.Equal(1, exitCode); // Should return error code for invalid type
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);

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

            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);

                // Act
                int exitCode = await Program.Main(args);

                // Assert
                Assert.Equal(1, exitCode); // Should return error code
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);

                // Cleanup
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "Benchmark")]
        public async Task Main_WithBenchmarkFlag_ReturnsZero()
        {
            // Arrange
            string[] args = { "--benchmark" };

            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);

                // Act
                int exitCode = await Program.Main(args);

                // Assert
                Assert.Equal(0, exitCode);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }
}
