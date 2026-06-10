
using Xunit;

namespace Zipper
{
    public class PathValidatorTests
    {
        [Fact]
        public void ValidateAndCreateDirectory_ValidPath_ReturnsDirectoryInfo()
        {
            // Arrange
            string validPath = Directory.GetCurrentDirectory();

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
            // Arrange
            string baseDir = Directory.GetCurrentDirectory();
            string[] traversalPaths =
            {
                "../",
                "folder/../../folder",
                "..".PadRight(3, Path.DirectorySeparatorChar),
                $"folder{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}folder",
            };

            // Act & Assert
            foreach (string path in traversalPaths)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path, baseDir);
                Assert.Null(result); // Traversal escaping base directory should be blocked
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_RelativePathWithTraversal_ReturnsNull()
        {
            // Arrange
            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperBase_" + Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(baseDir);

                string[] relativePaths =
                {
                    "../../../etc",
                    "test/../../../etc/passwd",
                    "folder/subfolder/../../../sensitive",
                };

                // Act & Assert
                foreach (string path in relativePaths)
                {
                    var result = PathValidator.ValidateAndCreateDirectory(path, baseDir);
                    Assert.Null(result);
                }
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_MixedSlashesWithTraversal_ReturnsNull()
        {
            // Arrange
            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperBase_" + Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(baseDir);

                string[] mixedPaths =
                {
                    $"folder{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}etc",
                    $"folder/../..{Path.DirectorySeparatorChar}etc",
                    $"folder{Path.DirectorySeparatorChar}../etc/passwd",
                };

                // Act & Assert
                foreach (string path in mixedPaths)
                {
                    var result = PathValidator.ValidateAndCreateDirectory(path, baseDir);
                    Assert.Null(result);
                }
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
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
        [InlineData("folder/../../file")]
        [InlineData("/absolute/path/with/../../traversal")]
        public void IsPathSafe_InvalidPaths_ReturnsFalse(string path)
        {
            // Arrange
            string baseDir = Directory.GetCurrentDirectory();

            // Act
            bool result = PathValidator.IsPathSafe(path, baseDir);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("./valid/path")]
        [InlineData("valid/path")]
        [InlineData("simple-folder")]
        public void IsPathSafe_ValidPaths_ReturnsTrue(string path)
        {
            // Arrange
            string baseDir = Environment.CurrentDirectory;

            // Act
            bool result = PathValidator.IsPathSafe(path, baseDir);

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

            // Act & Assert - Should not throw exceptions, returns null or DirectoryInfo depending on platform
            foreach (string path in edgeCasePaths)
            {
                var exception = Record.Exception(() => PathValidator.ValidateAndCreateDirectory(path));
                Assert.Null(exception);
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithPathTooLong_DoesNotThrow()
        {
            // Arrange - Create a path longer than max path length
            string longPath = Path.Combine(Directory.GetCurrentDirectory(), new string('a', 300));

            // Act
            var exception = Record.Exception(() => PathValidator.ValidateAndCreateDirectory(longPath));

            // Assert
            Assert.Null(exception);
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithComplexTraversalAttempts_ReturnsNull()
        {
            // Arrange - Complex traversal attack patterns
            string baseDir = Directory.GetCurrentDirectory();
            string[] complexTraversals =
            {
                "../../../../../../../etc/passwd",
                $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}windows{Path.DirectorySeparatorChar}system32",
                "test/../../sensitive/../../../data",
                "/folder/../../etc/shadow",
                OperatingSystem.IsWindows() ? "C:\\folder\\..\\..\\sensitive" : "/folder/../../sensitive",
            };

            // Act & Assert
            foreach (string path in complexTraversals)
            {
                var result = PathValidator.ValidateAndCreateDirectory(path, baseDir);
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
                var exception = Record.Exception(() => PathValidator.ValidateAndCreateDirectory(path));
                Assert.Null(exception);
            }
        }

        [Theory]
        [InlineData("relative/path", true)] // Relative path without traversal
        [InlineData("../traversal", false)] // Traversal attempt outside current dir
        [InlineData("", false)] // Empty path
        public void IsPathSafe_VariousPathTypes_ReturnsExpectedResult(string path, bool expectedSafe)
        {
            // Arrange
            string baseDir = Environment.CurrentDirectory;

            // Act
            bool result = PathValidator.IsPathSafe(path, baseDir);

            // Assert
            Assert.Equal(expectedSafe, result);
        }

        [Fact]
        public void ValidateAndCreateDirectory_CanonicalTraversalAttempt_ReturnsNull()
        {
            // Arrange
            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperBase_" + Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(baseDir);

                // A path that technically starts with the base dir name but canonically resolves outside it
                string escapePath = Path.Combine(baseDir, "..", "..", "etc", "passwd");

                // Act
                var result = PathValidator.ValidateAndCreateDirectory(escapePath, baseDir);

                // Assert
                Assert.Null(result); // Canonical path escapes baseDir, should be rejected
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithUncPath_DoesNotThrow()
        {
            var baseDir = Directory.GetCurrentDirectory();
            var uncPath = @"\\server\share\folder";
            var exception = Record.Exception(() => PathValidator.ValidateAndCreateDirectory(uncPath, baseDir));
            Assert.Null(exception);
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithUnicodeCharacters_ReturnsDirectoryInfo()
        {
            var tempPath = Directory.GetCurrentDirectory();
            var unicodePath = Path.Combine(tempPath, "über-cool_文件_パス");
            var result = PathValidator.ValidateAndCreateDirectory(unicodePath);
            Assert.NotNull(result);
        }

        [Fact]
        public void ValidateAndCreateDirectory_WithTrailingSeparator_NormalizesPath()
        {
            var tempPath = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar);
            var pathWithTrailing = tempPath + Path.DirectorySeparatorChar;
            var result = PathValidator.ValidateAndCreateDirectory(pathWithTrailing);
            Assert.NotNull(result);
            Assert.Equal(tempPath, result.FullName.TrimEnd(Path.DirectorySeparatorChar));
        }

        [Fact]
        public void IsPathSafe_UncPath_DoesNotThrow()
        {
            var uncPath = @"\\server\share\folder";
            var baseDir = Directory.GetCurrentDirectory();
            var exception = Record.Exception(() => PathValidator.IsPathSafe(uncPath, baseDir));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData("über-cool")]
        [InlineData("文件")]
        [InlineData("パス")]
        public void IsPathSafe_UnicodePath_ReturnsTrue(string pathComponent)
        {
            var result = PathValidator.IsPathSafe(pathComponent);
            Assert.True(result);
        }

        [Fact]
        public void IsPathSafe_TrailingSeparator_ReturnsTrue()
        {
            var safePath = Path.Combine(Environment.CurrentDirectory, "folder") + Path.DirectorySeparatorChar;
            var result = PathValidator.IsPathSafe(safePath);
            Assert.True(result);
        }

        [Fact]
        public void ValidateAndCreateDirectory_SymlinkEscapingBase_ReturnsNull()
        {
            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperBaseSymlinkTest_" + Guid.NewGuid().ToString());
            string targetDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperTargetSymlinkTest_" + Guid.NewGuid().ToString());

            try
            {
                Directory.CreateDirectory(baseDir);
                Directory.CreateDirectory(targetDir);

                string linkPath = Path.Combine(baseDir, "symlink");
                try
                {
                    Directory.CreateSymbolicLink(linkPath, targetDir);
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore if symlink creation is not permitted (e.g. Windows non-admin)
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return;
                }
                catch (System.IO.IOException ex) when (ex.Message.Contains("privilege") || ex.HResult == -2147024564)
                {
                    return;
                }

                var result = PathValidator.ValidateAndCreateDirectory(linkPath, baseDir);
                Assert.Null(result);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            }
        }

        [Fact]
        public void IsPathSafe_PathWithEmbeddedNull_ReturnsFalse()
        {
            var result = PathValidator.IsPathSafe("folder\0name", Directory.GetCurrentDirectory());

            Assert.False(result);
        }

        [Fact]
        public void ValidateAndCreateDirectory_SymlinkWithChildSuffix_EscapingBase_ReturnsNull()
        {
            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperBaseSuffixTest_" + Guid.NewGuid().ToString());
            string targetDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperTargetSuffixTest_" + Guid.NewGuid().ToString());

            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(targetDir);

            string linkPath = Path.Combine(baseDir, "symlink");
            try
            {
                try
                {
                    Directory.CreateSymbolicLink(linkPath, targetDir);
                }
                catch (UnauthorizedAccessException)
                {
                    // Symlink creation is not permitted (e.g. Windows non-admin); skip.
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return;
                }
                catch (System.IO.IOException ex) when (ex.Message.Contains("privilege") || ex.HResult == -2147024564)
                {
                    return;
                }

                // The child segment does not exist on disk, so resolution must walk up to the
                // symlink and rebuild the path from the resolved target plus the child suffix.
                var escapePath = Path.Combine(linkPath, "child");
                var result = PathValidator.ValidateAndCreateDirectory(escapePath, baseDir);
                Assert.Null(result);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            }
        }

        [Fact]
        public void ValidateAndCreateDirectory_SymlinkWithChildSuffix_InsideBase_ReturnsDirectoryInfo()
        {
            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperBaseInsideTest_" + Guid.NewGuid().ToString());
            string targetDir = Path.Combine(baseDir, "real");

            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(targetDir);

            string linkPath = Path.Combine(baseDir, "symlink");
            try
            {
                try
                {
                    Directory.CreateSymbolicLink(linkPath, targetDir);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return;
                }
                catch (System.IO.IOException ex) when (ex.Message.Contains("privilege") || ex.HResult == -2147024564)
                {
                    return;
                }

                var insidePath = Path.Combine(linkPath, "child");
                var result = PathValidator.ValidateAndCreateDirectory(insidePath, baseDir);

                Assert.NotNull(result);
                Assert.StartsWith(baseDir, result.FullName, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            }
        }
    }
}
