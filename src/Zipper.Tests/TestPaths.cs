namespace Zipper.Tests;

/// <summary>
/// Test-side path helpers that never consult the system under test.
/// </summary>
internal static class TestPaths
{
    private const int MaxSymbolicLinkHops = 32;

    /// <summary>
    /// Returns the symbolic-link-free absolute form of <paramref name="path"/> (the equivalent of
    /// POSIX realpath). Fixture roots are canonicalized at creation so a raw expected path and a
    /// resolved actual path describe the same directory even when the system temporary directory
    /// itself sits below a symbolic link (macOS <c>/var</c> -&gt; <c>/private/var</c>), where
    /// <see cref="Path.GetTempPath"/> reports the unresolved form while
    /// <c>PathValidator.ResolveSecurePath</c> reports the resolved one.
    /// Deliberately independent of <see cref="PathValidator"/>: expectations derived from the
    /// system under test would hide a base-resolution regression.
    /// </summary>
    /// <param name="path">Absolute or relative path; the path need not exist, but its existing
    /// ancestors are the ones whose symbolic links get resolved.</param>
    public static string Canonicalize(string path)
    {
        return Canonicalize(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), 0);
    }

    private static string Canonicalize(string fullPath, int linkHops)
    {
        if (linkHops > MaxSymbolicLinkHops)
        {
            throw new IOException($"Too many levels of symbolic links while resolving '{fullPath}'.");
        }

        if (Directory.Exists(fullPath) || File.Exists(fullPath))
        {
            FileSystemInfo entry = File.Exists(fullPath) ? new FileInfo(fullPath) : new DirectoryInfo(fullPath);
            var target = entry.ResolveLinkTarget(true);
            if (target is not null)
            {
                return Canonicalize(Path.TrimEndingDirectorySeparator(target.FullName), linkHops + 1);
            }
        }

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent))
        {
            return fullPath;
        }

        // Walking up resolves each ancestor that is itself a symbolic link; only link hops
        // consume the cycle guard, so arbitrarily deep fixture paths stay within the budget.
        return Path.Combine(Canonicalize(parent, linkHops), Path.GetFileName(fullPath));
    }
}
