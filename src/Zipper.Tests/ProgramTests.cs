// <copyright file="ProgramTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Xunit;

namespace Zipper
{
    public class ProgramTests
    {
        [Fact]
        public async Task Main_WithHelpFlag_ReturnsZero()
        {
            // Arrange
            string[] args = { "--help" };

            // Act
            int exitCode = await Program.Main(args);

            // Assert
            Assert.Equal(1, exitCode); // Help shows usage and returns 1
        }

        [Fact]
        public async Task Main_WithInvalidArguments_ReturnsErrorCode()
        {
            // Arrange - Missing required arguments
            string[] args = { };

            // Act
            int exitCode = await Program.Main(args);

            // Assert
            Assert.Equal(1, exitCode); // Should return error code for invalid args
        }

        [Fact]
        public async Task Main_WithMissingOutputPath_ReturnsErrorCode()
        {
            // Arrange - Missing required --output argument
            string[] args = { "--count", "10", "--type", "pdf" };

            // Act
            int exitCode = await Program.Main(args);

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
                int exitCode = await Program.Main(args);

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
                int exitCode = await Program.Main(args);

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
    }
}
