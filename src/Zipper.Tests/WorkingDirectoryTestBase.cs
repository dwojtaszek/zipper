namespace Zipper.Tests;

public abstract class WorkingDirectoryTestBase : TempDirectoryTestBase
{
    private readonly string originalCurrentDirectory = Directory.GetCurrentDirectory();

    protected WorkingDirectoryTestBase()
    {
        // Keep path-validation tests inside their owned temp directory, not the repository.
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
