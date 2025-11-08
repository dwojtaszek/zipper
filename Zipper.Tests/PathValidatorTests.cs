using System;
using System.IO;
using Xunit;

namespace Zipper.Tests
{
    public class PathValidatorTests
    {
        [Fact]
        public void ValidateAndCreateDirectory_ValidPath_ReturnsDirectoryInfo()
        {
            // Arrange
            string validPath = Path.GetTempPath();

            // Act
            var result = PathValidator.ValidateAndCreateDirectory(validPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(validPath), result.FullName);
        }

        [Fact]
        public void ValidateAndCreateDirectory_NullOrEmptyPath_ReturnsNull()
        {
            // Arrange & Act & Assert
            Assert.Null(PathValidator.ValidateAndCreateDirectory(null!));
            Assert.Null(PathValidator.ValidateAndCreateDirectory(""));
            Assert.Null(PathValidator.ValidateAndCreateDirectory("   "));
        }

        [Fact]
        public void ValidateAndCreateDirectory_PathWithTraversal_ReturnsNull()
        {
            // Arrange
            string[] traversalPaths = {
                "..",
                "../",
                "/../",
                "folder/../folder",
                "folder/../../folder",
                "..\\",
                "folder\\..\\folder",
                "folder\\..\\..\\folder"
            };

            // Act & Assert
            foreach (string path in traversalPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result, $"Path '{path}' should not be valid due to traversal");
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_PathWithInvalidCharacters_ReturnsNull()
        {
            // Arrange
            string[] invalidPaths = {
                "folder<name",
                "folder>name",
                "folder|name",
                "folder?name",
                "folder*name",
                "folder\"name",
                "folder:name"
            };

            // Act & Assert
            foreach (string path in invalidPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result, $"Path '{path}' should not be valid due to invalid characters");
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_RelativePathWithTraversal_ReturnsNull()
        {
            // Arrange
            string[] relativePaths = {
                "./../../../etc",
                "test/../../../etc/passwd",
                "folder/subfolder/../../../sensitive"
            };

            // Act & Assert
            foreach (string path in relativePaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result, $"Path '{path}' should not be valid due to relative traversal");
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_NormalizedValidPaths_ReturnsDirectoryInfo()
        {
            // Arrange
            string[] validPaths = {
                Path.GetTempPath(),
                Path.Combine(Path.GetTempPath(), "test"),
                "./subfolder",
                "subfolder",
                "subfolder/nested/folder"
            };

            // Act & Assert
            foreach (string path in validPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.NotNull(result, $"Path '{path}' should be valid");
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("folder/../file")]
        [InlineData("folder<invalid")]
        [InlineData("/absolute/path/with/../traversal")]
        public void IsPathSafe_InvalidPaths_ReturnsFalse(string path)
        {
            // Act
            bool result = PathValidator.IsPathSafe(path);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("./valid/path")]
        [InlineData("valid/path")]
        [InlineData(Path.GetTempPath())]
        [InlineData("simple-folder")]
        public void IsPathSafe_ValidPaths_ReturnsTrue(string path)
        {
            // Act
            bool result = PathValidator.IsPathSafe(path);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateAndCreateDirectory_PathTooLong_ReturnsNull()
        {
            // Arrange
            string longPath = new string('a', 300) + "\\test";

            // Act
            var result = PathValidator.ValidateAndCreateDirectory(longPath);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateAndCreateDirectory_MixedSlashesWithTraversal_ReturnsNull()
        {
            // Arrange
            string[] mixedPaths = {
                "folder\\..\\..\\etc",
                "folder/../..\\etc",
                "folder\\../etc/passwd"
            };

            // Act & Assert
            foreach (string path in mixedPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result, $"Mixed path '{path}' should not be valid due to traversal");
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_DotPaths_Valid()
        {
            // Arrange
            string[] dotPaths = {
                ".",
                "./",
                "./folder",
                "folder/.",
                "folder/./subfolder"
            };

            // Act & Assert
            foreach (string path in dotPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.NotNull(result, $"Path '{path}' with dot should be valid");
            }
        }
    }
}