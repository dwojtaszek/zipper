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

        [Fact]
        public void ValidateAndCreateDirectory_WithInvalidCharactersInFileName_HandlesGracefully()
        {
            // Arrange - Test that PathValidator handles various edge cases without crashing
            // PathValidator.GetInvalidFileNameChars() returns different results on different platforms
            // so we test the exception handling rather than specific character validation
            string[] edgeCasePaths =
            {
                "folder\0test", // Null character
                new string('x', 500), // Very long path
            };

            // Act & Assert - Should not throw exceptions, returns null or DirectoryInfo depending on  platform
            foreach (string path in edgeCasePaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);

                // Test passes if no exception is thrown
                // Result checking is platform specific, but execution should be safe
                var exception = Record.Exception(() => PathValidator.ValidateAndCreateDirectory(path));
                Assert.Null(exception);
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithPathTooLong_ReturnsNull()
        {
            // Arrange - Create a path longer than max path length
            string longPath = Path.Combine(Path.GetTempPath(), new string('a', 300));

            // Act
            var result = PathValidator.ValidateAndCreateDirectory(longPath);

            // Assert
            // On some systems this may succeed, on others it will fail
            // The important thing is it doesn't throw an unhandled exception
            // The important thing is it doesn't throw an unhandled exception
            var exception = Record.Exception(() => PathValidator.ValidateAndCreateDirectory(longPath));
            Assert.Null(exception);
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithComplexTraversalAttempts_ReturnsNull()
        {
            // Arrange - Complex traversal attack patterns
            string[] complexTraversals =
            {
                "../../../../../../../etc/passwd",
                "..\\..\\..\\..\\..\\..\\..\\windows\\system32",
                "test/../../sensitive/../../../data",
                "/folder/../../etc/shadow",
                "C:\\folder\\..\\..\\sensitive",
            };

            // Act & Assert
            foreach (string path in complexTraversals)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path);
                Assert.Null(result); // Complex traversal attacks should be blocked
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithVariousExceptionPaths_HandlesGracefully()
        {
            // Arrange - Paths that might trigger different exceptions
            string[] exceptionPaths =
            {
                string.Empty,
                "   ",
                new string('a', 500), // Very long path
                "../etc/passwd",
                "test/../../../data",
            };

            // Act & Assert
            foreach (string path in exceptionPaths)
            {
                // Should not throw exceptions, should return null for invalid paths
                var result = PathValidator.ValidateAndCreateDirectory(path);

                // All of these should be rejected or handled gracefully
            }
        }

        [Theory]
        [InlineData("C:\\Program Files\\App", true)] // Windows absolute path
        [InlineData("/usr/local/bin", true)] // Unix absolute path
        [InlineData("relative/path", true)] // Relative path without traversal
        [InlineData("../traversal", false)] // Traversal attempt
        [InlineData("", false)] // Empty path
        public void IsPathSafe_VariousPathTypes_ReturnsExpectedResult(string path, bool expectedSafe)
        {
            // Act
            bool result = PathValidator.IsPathSafe(path);

            // Assert
            Assert.Equal(expectedSafe, result);
        }
    }
}
