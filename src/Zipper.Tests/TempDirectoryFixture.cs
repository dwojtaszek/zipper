namespace Zipper.Tests
{
    public sealed class TempDirectoryFixture : IDisposable
    {
        public string DirectoryPath { get; }

        public TempDirectoryFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(DirectoryPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                try
                {
                    Directory.Delete(DirectoryPath, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }
    }
}
