using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class CommandLineValidatorTests
    {
        private readonly ITestOutputHelper _output;

        public CommandLineValidatorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ValidateAndParseArguments_NullArguments_ReturnsNull()
        {
            // Arrange
            string[]? args = null;

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_EmptyArguments_ReturnsNull()
        {
            // Arrange
            var args = Array.Empty<string>();

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp" }, true)]
        [InlineData(new[] { "--type", "jpg", "--count", "50", "--output-path", "/tmp/test" }, true)]
        [InlineData(new[] { "--type", "tiff", "--count", "10", "--output-path", "C:\\temp" }, true)]
        [InlineData(new[] { "--type", "eml", "--count", "1000", "--output-path", "/tmp" }, true)]
        public void ValidateAndParseArguments_ValidRequiredArguments_ReturnsFileGenerationRequest(string[] args, bool shouldSucceed)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            if (shouldSucceed)
            {
                Assert.NotNull(result);
                Assert.Equal(args[1], result.FileType.ToLower());
                Assert.Equal(int.Parse(args[3]), result.FileCount);
                Assert.Equal(args[5], result.OutputPath);
            }
            else
            {
                Assert.Null(result);
            }
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--folders", "5" }, 5)]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--folders", "10" }, 10)]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--folders", "1" }, 1)]
        public void ValidateAndParseArguments_WithFoldersArgument_SetsCorrectFolders(string[] args, int expectedFolders)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedFolders, result.Folders);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--encoding", "UTF-8" }, "UTF-8")]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--encoding", "UTF-16" }, "UTF-16")]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--encoding", "ANSI" }, "ANSI")]
        public void ValidateAndParseArguments_WithEncodingArgument_SetsCorrectEncoding(string[] args, string expectedEncoding)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedEncoding, result.Encoding);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--distribution", "proportional" }, "proportional")]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--distribution", "gaussian" }, "gaussian")]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--distribution", "exponential" }, "exponential")]
        public void ValidateAndParseArguments_WithDistributionArgument_SetsCorrectDistribution(string[] args, string expectedDistribution)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedDistribution, result.Distribution);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--with-metadata" }, true)]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp" }, false)]
        public void ValidateAndParseArguments_WithMetadataArgument_SetsCorrectFlag(string[] args, bool expectedWithMetadata)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedWithMetadata, result.WithMetadata);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--with-text" }, true)]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp" }, false)]
        public void ValidateAndParseArguments_WithTextArgument_SetsCorrectFlag(string[] args, bool expectedWithText)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedWithText, result.WithText);
        }

        [Theory]
        [InlineData(new[] { "--type", "eml", "--count", "100", "--output-path", "/tmp", "--attachment-rate", "50" }, 50)]
        [InlineData(new[] { "--type", "eml", "--count", "100", "--output-path", "/tmp", "--attachment-rate", "0" }, 0)]
        [InlineData(new[] { "--type", "eml", "--count", "100", "--output-path", "/tmp", "--attachment-rate", "100" }, 100)]
        public void ValidateAndParseArguments_WithAttachmentRateArgument_SetsCorrectRate(string[] args, int expectedAttachmentRate)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedAttachmentRate, result.AttachmentRate);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--target-zip-size", "1MB" }, 1048576)]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--target-zip-size", "10MB" }, 10485760)]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--target-zip-size", "1GB" }, 1073741824)]
        public void ValidateAndParseArguments_WithTargetZipSizeArgument_SetsCorrectSize(string[] args, long expectedSize)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedSize, result.TargetZipSize);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--include-load-file" }, true)]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp" }, true)]
        public void ValidateAndParseArguments_WithIncludeLoadFileArgument_SetsCorrectFlag(string[] args, bool expectedIncludeLoadFile)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedIncludeLoadFile, result.IncludeLoadFile);
        }

        [Theory]
        [InlineData(new[] { "--count", "100", "--output-path", "/tmp" })] // Missing --type
        [InlineData(new[] { "--type", "pdf", "--output-path", "/tmp" })] // Missing --count
        [InlineData(new[] { "--type", "pdf", "--count", "100" })] // Missing --output-path
        public void ValidateAndParseArguments_MissingRequiredArguments_ReturnsNull(string[] args)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "0", "--output-path", "/tmp" })] // Zero count
        [InlineData(new[] { "--type", "pdf", "--count", "-1", "--output-path", "/tmp" })] // Negative count
        [InlineData(new[] { "--type", "pdf", "--count", "abc", "--output-path", "/tmp" })] // Non-numeric count
        public void ValidateAndParseArguments_InvalidCount_ReturnsNull(string[] args)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--folders", "0" })] // Zero folders
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--folders", "-1" })] // Negative folders
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--folders", "101" })] // Too many folders
        public void ValidateAndParseArguments_InvalidFolders_ReturnsNull(string[] args)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--attachment-rate", "-1" })] // Negative rate
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--attachment-rate", "101" })] // Rate too high
        public void ValidateAndParseArguments_InvalidAttachmentRate_ReturnsNull(string[] args)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData(new[] { "--type", "invalid", "--count", "100", "--output-path", "/tmp" })] // Invalid file type
        [InlineData(new[] { "--type", "PDF", "--count", "100", "--output-path", "/tmp" })] // Uppercase file type (should be case insensitive)
        public void ValidateAndParseArguments_InvalidFileType_ReturnsNull(string[] args)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            if (args[1].Equals("PDF", StringComparison.OrdinalIgnoreCase))
            {
                Assert.NotNull(result); // Should succeed - case insensitive
                Assert.Equal("pdf", result.FileType.ToLower());
            }
            else
            {
                Assert.Null(result); // Should fail - invalid type
            }
        }

        [Theory]
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--encoding", "INVALID" })] // Invalid encoding
        [InlineData(new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp", "--distribution", "INVALID" })] // Invalid distribution
        public void ValidateAndParseArguments_InvalidEnumValues_ReturnsNull(string[] args)
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_AllOptionalArguments_SetsAllValuesCorrectly()
        {
            // Arrange
            var args = new[]
            {
                "--type", "eml",
                "--count", "500",
                "--output-path", "/tmp/test",
                "--folders", "8",
                "--encoding", "UTF-16",
                "--distribution", "gaussian",
                "--with-metadata",
                "--with-text",
                "--attachment-rate", "75",
                "--target-zip-size", "50MB",
                "--include-load-file"
            };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("eml", result.FileType.ToLower());
            Assert.Equal(500, result.FileCount);
            Assert.Equal("/tmp/test", result.OutputPath);
            Assert.Equal(8, result.Folders);
            Assert.Equal("UTF-16", result.Encoding);
            Assert.Equal("gaussian", result.Distribution);
            Assert.True(result.WithMetadata);
            Assert.True(result.WithText);
            Assert.Equal(75, result.AttachmentRate);
            Assert.Equal(52428800, result.TargetZipSize); // 50MB in bytes
            Assert.True(result.IncludeLoadFile);
        }

        [Fact]
        public void ValidateAndParseArguments_DefaultValues_SetsCorrectDefaults()
        {
            // Arrange
            var args = new[]
            {
                "--type", "pdf",
                "--count", "100",
                "--output-path", "/tmp"
            };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("pdf", result.FileType.ToLower());
            Assert.Equal(100, result.FileCount);
            Assert.Equal("/tmp", result.OutputPath);
            Assert.Equal(1, result.Folders); // Default
            Assert.Equal("UTF-8", result.Encoding); // Default
            Assert.Equal("proportional", result.Distribution); // Default
            Assert.False(result.WithMetadata); // Default
            Assert.False(result.WithText); // Default
            Assert.Equal(0, result.AttachmentRate); // Default
            Assert.False(result.TargetZipSize.HasValue); // Default
            Assert.True(result.IncludeLoadFile); // Default
        }
    }
}