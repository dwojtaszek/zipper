// <copyright file="PathValidatorTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Xunit;

namespace Zipper
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
            Assert.Null(PathValidator.ValidateAndCreateDirectory(string.Empty));
            Assert.Null(PathValidator.ValidateAndCreateDirectory("   "));
        }

        [Fact]
        public void ValidateAndCreateDirectory_PathWithTraversal_ReturnsNull()
        {
            // Arrange - Test directory traversal which is a security vulnerability
            string[] traversalPaths =
            {
                "..",
                "../",
                "/../",
                "folder/../folder",
                "folder/../../folder",
                "..\\",
                "folder\\..\\folder",
                "folder\\..\\..\\folder",
            };

            // Act & Assert
            foreach (string path in traversalPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result); // Path traversal should be blocked for security
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_RelativePathWithTraversal_ReturnsNull()
        {
            // Arrange - Test relative path traversal attacks
            string[] relativePaths =
            {
                "./../../../etc",
                "test/../../../etc/passwd",
                "folder/subfolder/../../../sensitive",
            };

            // Act & Assert
            foreach (string path in relativePaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result); // Relative path traversal should be blocked
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_MixedSlashesWithTraversal_ReturnsNull()
        {
            // Arrange - Test mixed slash traversal
            string[] mixedPaths =
            {
                "folder\\..\\..\\etc",
                "folder/../..\\etc",
                "folder\\../etc/passwd",
            };

            // Act & Assert
            foreach (string path in mixedPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result); // Mixed slash traversal should be blocked
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_DotPaths_Valid()
        {
            // Arrange - These are safe paths (no traversal)
            string[] dotPaths =
            {
                ".",
                "./",
                "./folder",
                "folder/.",
                "folder/./subfolder",
            };

            // Act & Assert
            foreach (string path in dotPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.NotNull(result); // Dot paths without traversal are valid
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("folder/../file")]
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
        [InlineData("simple-folder")]
        public void IsPathSafe_ValidPaths_ReturnsTrue(string path)
        {
            // Act
            bool result = PathValidator.IsPathSafe(path);

            // Assert
            Assert.True(result);
        }
    }
}
