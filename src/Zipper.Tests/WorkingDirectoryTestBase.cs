namespace Zipper.Tests;

public abstract class WorkingDirectoryTestBase : TempDirectoryTestBase
{
    private readonly string originalCurrentDirectory = Directory.GetCurrentDirectory();

    protected WorkingDirectoryTestBase()
    {
        // Keep path-validation tests inside their owned temp directory, not the repository.
        // NOTE: Directory.SetCurrentDirectory is process-global, so this base class is only
        // safe while test parallelization stays disabled assembly-wide (see the
        // Zipper.Tests.csproj comment, issue #433): re-enabling it would race every
        // Directory.GetCurrentDirectory consumer against these CWD switches.
        Directory.SetCurrentDirectory(this.TempDir);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Directory.SetCurrentDirectory(this.originalCurrentDirectory);
        }

        base.Dispose(disposing);
    }
}
