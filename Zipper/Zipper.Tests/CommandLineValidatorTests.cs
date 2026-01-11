using System;
using System.IO;
using Xunit;

namespace Zipper
{
    public class CommandLineValidatorTests : IDisposable
    {
        private readonly string _tempDir;

        public CommandLineValidatorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Fact]
        public void ValidateAndParseArguments_WithValidRequiredArguments_ShouldReturnValidRequest()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(_tempDir, result!.OutputPath);
            Assert.Equal(100, result.FileCount);
            Assert.Equal("pdf", result.FileType);
            Assert.Equal(1, result.Folders);
            Assert.Equal("Unicode (UTF-8)", result.Encoding);
            Assert.Equal(DistributionType.Proportional, result.Distribution);
            Assert.False(result.WithMetadata);
            Assert.False(result.WithText);
            Assert.False(result.IncludeLoadFile);
            Assert.Equal(0, result.AttachmentRate);
        }

        [Fact]
        public void ValidateAndParseArguments_WithAllArguments_ShouldReturnValidRequest()
        {
            // Arrange
            var args = new[]
            {
                "--type", "jpg",
                "--count", "500",
                "--output-path", _tempDir,
                "--folders", "5",
                "--encoding", "UTF-16",
                "--distribution", "gaussian",
                "--with-metadata",
                "--with-text",
                "--attachment-rate", "25",
                "--target-zip-size", "10MB",
                "--include-load-file"
            };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(_tempDir, result!.OutputPath);
            Assert.Equal(500, result.FileCount);
            Assert.Equal("jpg", result.FileType);
            Assert.Equal(5, result.Folders);
            Assert.Equal("Unicode", result.Encoding);
            Assert.Equal(DistributionType.Gaussian, result.Distribution);
            Assert.True(result.WithMetadata);
            Assert.True(result.WithText);
            Assert.True(result.IncludeLoadFile);
            Assert.Equal(25, result.AttachmentRate);
            Assert.Equal(10 * 1024 * 1024, result.TargetZipSize);
        }

        [Fact]
        public void ValidateAndParseArguments_WithNullArgs_ShouldReturnNull()
        {
            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(null!);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithEmptyArgs_ShouldReturnNull()
        {
            // Arrange
            var args = new string[0];

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_MissingType_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--count", "100", "--output-path", _tempDir };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_MissingCount_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--output-path", _tempDir };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_MissingOutputPath_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithInvalidOutputPath_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", "../../../etc/passwd" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithInvalidFoldersRange_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--folders", "0" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithFoldersTooHigh_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--folders", "101" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithInvalidAttachmentRate_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--attachment-rate", "-1" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithAttachmentRateTooHigh_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--attachment-rate", "101" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithInvalidEncoding_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--encoding", "INVALID" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithInvalidDistribution_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--distribution", "INVALID" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithInvalidTargetZipSize_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--target-zip-size", "INVALID" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndParseArguments_WithTargetZipSizeWithoutCount_ShouldReturnNull()
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--output-path", _tempDir, "--target-zip-size", "10MB" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData("UTF-8", "Unicode (UTF-8)")]
        [InlineData("utf-8", "Unicode (UTF-8)")]
        [InlineData("UTF-16", "Unicode")]
        [InlineData("utf-16", "Unicode")]
        [InlineData("ANSI", "Western European (Windows)")]
        [InlineData("ansi", "Western European (Windows)")]
        public void ValidateAndParseArguments_WithValidEncodings_ShouldReturnValidRequest(string inputEncoding, string expectedEncoding)
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--encoding", inputEncoding };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedEncoding, result!.Encoding);
        }

        [Theory]
        [InlineData("proportional", DistributionType.Proportional)]
        [InlineData("PROPORTIONAL", DistributionType.Proportional)]
        [InlineData("gaussian", DistributionType.Gaussian)]
        [InlineData("GAUSSIAN", DistributionType.Gaussian)]
        [InlineData("exponential", DistributionType.Exponential)]
        [InlineData("EXPONENTIAL", DistributionType.Exponential)]
        public void ValidateAndParseArguments_WithValidDistributions_ShouldReturnValidRequest(string inputDistribution, DistributionType expectedDistribution)
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--distribution", inputDistribution };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedDistribution, result!.Distribution);
        }

        [Theory]
        [InlineData("1KB", 1024)]
        [InlineData("1MB", 1024 * 1024)]
        [InlineData("1GB", 1024L * 1024 * 1024)]
        [InlineData("500MB", 500L * 1024 * 1024)]
        [InlineData("10GB", 10L * 1024 * 1024 * 1024)]
        public void ValidateAndParseArguments_WithValidTargetZipSizes_ShouldReturnValidRequest(string inputSize, long expectedBytes)
        {
            // Arrange
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", _tempDir, "--target-zip-size", inputSize };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedBytes, result!.TargetZipSize);
        }

        [Theory]
        [InlineData("pdf", "pdf")]
        [InlineData("PDF", "pdf")]
        [InlineData("jpg", "jpg")]
        [InlineData("JPG", "jpg")]
        [InlineData("tiff", "tiff")]
        [InlineData("TIFF", "tiff")]
        [InlineData("eml", "eml")]
        [InlineData("EML", "eml")]
        public void ValidateAndParseArguments_WithValidFileTypes_ShouldReturnValidRequest(string inputType, string expectedType)
        {
            // Arrange
            var args = new[] { "--type", inputType, "--count", "100", "--output-path", _tempDir };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedType, result!.FileType);
        }

        [Fact]
        public void ShowUsage_ShouldDisplayUsageInformation()
        {
            // Arrange
            var originalError = Console.Error;
            var errorOutput = new StringWriter();
            Console.SetError(errorOutput);

            try
            {
                // Act
                CommandLineValidator.ShowUsage();

                // Assert
                var output = errorOutput.ToString();
                Assert.Contains("Error: Missing required arguments.", output);
                Assert.Contains("Usage:", output);
                Assert.Contains("--type <pdf|jpg|tiff|eml|docx|xlsx>", output);
                Assert.Contains("--count <number>", output);
                Assert.Contains("--output-path <directory>", output);
            }
            finally
            {
                Console.SetError(originalError);
                errorOutput.Dispose();
            }
        }

        [Fact]
        public void ValidateAndParseArguments_WithTiffPagesRange_ShouldParseCorrectly()
        {
            // Arrange
            var args = new[] { "--type", "tiff", "--count", "10", "--output-path", "/tmp/test", "--tiff-pages", "5-20" };

            // Act
            var result = CommandLineValidator.ValidateAndParseArguments(args);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.TiffPageRange);
            Assert.Equal(5, result.TiffPageRange.Value.Min);
            Assert.Equal(20, result.TiffPageRange.Value.Max);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("1-")]
        [InlineData("-10")]
        [InlineData("10-5")]  // min > max
        public void ValidateAndParseArguments_WithInvalidTiffPagesRange_ShouldReturnNull(string range)
        {
            // Arrange
            var originalError = Console.Error;
            var errorOutput = new StringWriter();
            Console.SetError(errorOutput);

            try
            {
                var args = new[] { "--type", "tiff", "--count", "10", "--output-path", "/tmp/test", "--tiff-pages", range };

                // Act
                var result = CommandLineValidator.ValidateAndParseArguments(args);

                // Assert
                Assert.Null(result);
                var output = errorOutput.ToString();
                Assert.Contains("Error: Invalid TIFF pages range", output);
            }
            finally
            {
                Console.SetError(originalError);
                errorOutput.Dispose();
            }
        }
    }
}