namespace Zipper.Tests;

/// <summary>
/// Helper to find the repository root directory.
/// </summary>
public static class RepoRootFinder
{
    private const string SolutionFileName = "zipper.sln";

    /// <summary>
    /// Walks up the directory tree from AppContext.BaseDirectory to locate the root directory containing the solution file.
    /// </summary>
    public static string Find() => Find(AppContext.BaseDirectory);

    /// <summary>
    /// Walks up the directory tree from the specified directory to locate the root directory containing the solution file.
    /// </summary>
    public static string Find(string startDir)
    {
        if (startDir is null)
        {
            throw new ArgumentNullException(nameof(startDir));
        }

        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find repo root ({SolutionFileName}) walking up from: {startDir}");
    }
}
