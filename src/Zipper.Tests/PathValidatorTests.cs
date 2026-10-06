
using Xunit;

namespace Zipper.Tests;

public class PathValidatorTests
{
    // Expected values for resolved paths are derived from Path.GetFullPath rather than from
    // PathValidator itself: ResolveSecurePath_ValidPath_ReturnsDirectoryInfo pins
    // ResolveSecurePath(currentDirectory) == Path.GetFullPath(currentDirectory), which is green
    // on every CI runner, so no runner's checkout sits under an unresolved symbolic link. Using
    // the system under test to build expectations would hide a base-resolution regression.

    [Fact]
    public void ResolveSecurePath_ValidPath_ReturnsDirectoryInfo()
    {
        // Arrange
        string validPath = Directory.GetCurrentDirectory();

        // Act
        var result = PathValidator.ResolveSecurePath(validPath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(validPath), result.FullName);
    }

    [Fact]
    public void ResolveSecurePath_NullOrEmptyPath_ReturnsNull()
    {
        // Arrange & Act & Assert
        Assert.Null(PathValidator.ResolveSecurePath(null!));
        Assert.Null(PathValidator.ResolveSecurePath(string.Empty));
        Assert.Null(PathValidator.ResolveSecurePath("   "));
    }

    [Fact]
    public void ResolveSecurePath_PathWithTraversal_ReturnsNull()
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
            var result = PathValidator.ResolveSecurePath(path, baseDir);
            Assert.Null(result); // Traversal escaping base directory should be blocked
        }
    }

    [Fact]
    public void ResolveSecurePath_RelativePathWithTraversal_ReturnsNull()
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
                var result = PathValidator.ResolveSecurePath(path, baseDir);
                Assert.Null(result);
            }
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void ResolveSecurePath_MixedSlashesWithTraversal_ReturnsNull()
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
                var result = PathValidator.ResolveSecurePath(path, baseDir);
                Assert.Null(result);
            }
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void ResolveSecurePath_DotPaths_Valid()
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
            var result = PathValidator.ResolveSecurePath(path);
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
    public void ResolveSecurePath_WithEmbeddedNullCharacter_ReturnsNull()
    {
        // Arrange - an embedded NUL is rejected by path resolution on every platform, so
        // PathValidator must report it as invalid rather than hand back a usable DirectoryInfo.
        string path = "folder\0test";

        // Act
        var result = PathValidator.ResolveSecurePath(path);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveSecurePath_WithOverlongPath_ResolvesOnEveryPlatform(bool rooted)
    {
        // Arrange - no platform in the CI matrix rejects a 500-character segment: modern .NET
        // grows its GetFullPathName buffer up to the UNICODE_STRING limit, well past MAX_PATH.
        // A prior "returns null on Windows" expectation had to be relaxed for that reason, so the
        // contract asserted here is uniform: the path resolves. Each leg pins an exact expected
        // value, because a hedged "null or contained" disjunction would pass for an
        // implementation that simply always returned null.
        var currentDir = Directory.GetCurrentDirectory();
        var overlongSegment = new string('x', 500);
        var path = rooted ? Path.Combine(currentDir, overlongSegment) : overlongSegment;

        // Act
        var result = PathValidator.ResolveSecurePath(path);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(path), result.FullName);
    }

    [Fact]
    public void ResolveSecurePath_WithComplexTraversalAttempts_ReturnsNull()
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
            var result = PathValidator.ResolveSecurePath(path, baseDir);
            Assert.Null(result); // Complex traversal attacks should be blocked
        }
    }

    [Fact]
    public void ResolveSecurePath_WithoutBaseDirectory_ResolvesTraversalInsteadOfRejectingIt()
    {
        // Arrange - containment only applies when a base directory is supplied. Blank paths are
        // already covered by ResolveSecurePath_NullOrEmptyPath_ReturnsNull; these must resolve.
        string[] traversalPaths =
        {
            "../etc/passwd",
            "test/../../../data",
        };

        // Act & Assert
        foreach (string path in traversalPaths)
        {
            var result = PathValidator.ResolveSecurePath(path);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(path), result.FullName);
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
    public void ResolveSecurePath_CanonicalTraversalAttempt_ReturnsNull()
    {
        // Arrange
        string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperBase_" + Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(baseDir);

            // A path that technically starts with the base dir name but canonically resolves outside it
            string escapePath = Path.Combine(baseDir, "..", "..", "etc", "passwd");

            // Act
            var result = PathValidator.ResolveSecurePath(escapePath, baseDir);

            // Assert
            Assert.Null(result); // Canonical path escapes baseDir, should be rejected
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void ResolveSecurePath_WithUncPath_IsRejectedAsOutsideTheBaseDirectory()
    {
        // A UNC root cannot sit under a freshly created sibling directory on any platform:
        // Windows resolves it to \\server\share, and Unix treats the backslashes as ordinary
        // characters and resolves it under the current directory - outside this base either way.
        // Using a sibling base (not the current directory itself) is what makes the expected
        // outcome null everywhere instead of only on Windows.
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "ZipperUncBase_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);

        try
        {
            var result = PathValidator.ResolveSecurePath(@"\\server\share\folder", baseDir);

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, true);
            }
        }
    }

    [Fact]
    public void ResolveSecurePath_WithUnicodeCharacters_ReturnsDirectoryInfo()
    {
        var tempPath = Directory.GetCurrentDirectory();
        var unicodePath = Path.Combine(tempPath, "über-cool_文件_パス");
        var result = PathValidator.ResolveSecurePath(unicodePath);
        Assert.NotNull(result);
    }

    [Fact]
    public void ResolveSecurePath_WithTrailingSeparator_NormalizesPath()
    {
        var tempPath = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar);
        var pathWithTrailing = tempPath + Path.DirectorySeparatorChar;
        var result = PathValidator.ResolveSecurePath(pathWithTrailing);
        Assert.NotNull(result);
        Assert.Equal(tempPath, result.FullName.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void IsPathSafe_WithUncPath_ReturnsTrueOnlyWhereThePathStaysUnderBaseDirectory()
    {
        var baseDir = Directory.GetCurrentDirectory();
        var uncPath = @"\\server\share\folder";

        // Windows resolves the UNC root outside the base directory (false); Unix resolves it to a
        // name under the base directory (true).
        Assert.Equal(!OperatingSystem.IsWindows(), PathValidator.IsPathSafe(uncPath, baseDir));
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
    public void ResolveSecurePath_SymlinkEscapingBase_ReturnsNull()
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
            catch (System.IO.IOException ex) when (ex.Message.Contains("privilege", StringComparison.Ordinal) || ex.HResult == -2147024564)
            {
                return;
            }

            var result = PathValidator.ResolveSecurePath(linkPath, baseDir);
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
    public void ResolveSecurePath_SymlinkWithChildSuffix_EscapingBase_ReturnsNull()
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
            catch (System.IO.IOException)
            {
                return;
            }

            // The child segment does not exist on disk, so resolution must walk up to the
            // symlink and rebuild the path from the resolved target plus the child suffix.
            var escapePath = Path.Combine(linkPath, "child");
            var result = PathValidator.ResolveSecurePath(escapePath, baseDir);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        }
    }

    [Fact]
    public void ResolveSecurePath_SymlinkWithChildSuffix_InsideBase_ReturnsDirectoryInfo()
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
            catch (System.IO.IOException)
            {
                return;
            }

            var insidePath = Path.Combine(linkPath, "child");
            var result = PathValidator.ResolveSecurePath(insidePath, baseDir);

            Assert.NotNull(result);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            Assert.StartsWith(baseDir, result.FullName, comparison);
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        }
    }
}
