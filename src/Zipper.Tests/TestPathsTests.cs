using Xunit;

namespace Zipper.Tests;

public class TestPathsTests
{
    [Fact]
    public void Canonicalize_PathBelowSymbolicLinkRoot_ResolvesToRealTargetDirectory()
    {
        // Mirrors the macOS layout (system temporary directory below /var -> /private/var):
        // a fixture reached through a symbolic-link root must canonicalize to the real
        // directory, which is the form PathValidator.ResolveSecurePath reports back.
        var realRoot = TestPaths.Canonicalize(
            Path.Combine(Path.GetTempPath(), "zipper_realroot_" + Guid.NewGuid().ToString("N")));
        var linkRoot = Path.Combine(Path.GetTempPath(), "zipper_linkroot_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(realRoot, "nested"));
            try
            {
                Directory.CreateSymbolicLink(linkRoot, realRoot);
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
            catch (IOException)
            {
                return;
            }

            var canonical = TestPaths.Canonicalize(Path.Combine(linkRoot, "nested"));

            Assert.Equal(Path.Combine(realRoot, "nested"), canonical);
        }
        finally
        {
            if (Directory.Exists(linkRoot)) Directory.Delete(linkRoot, true);
            if (Directory.Exists(realRoot)) Directory.Delete(realRoot, true);
        }
    }

    [Fact]
    public void Canonicalize_ExistingDirectory_ReturnsIdempotentPathForSameDirectory()
    {
        // Canonical paths are fixed points: re-canonicalizing must be a no-op, and the result
        // must name the same directory. That is what makes a raw fixture path and the
        // system's resolved form of it interchangeable in assertions.
        var dir = Path.Combine(Path.GetTempPath(), "zipper_canonical_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(dir);

            var canonical = TestPaths.Canonicalize(dir);

            Assert.True(Directory.Exists(canonical));
            Assert.Equal(Path.GetFileName(dir), Path.GetFileName(canonical));
            Assert.Equal(canonical, TestPaths.Canonicalize(canonical));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
