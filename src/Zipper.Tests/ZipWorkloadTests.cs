using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.Zip;
using Xunit;

namespace Zipper.Tests;

public class ZipWorkloadTests
{
    [Fact]
    public async Task Main_RepeatedRecipe_ProducesExactInventoryAndMatchingHashes()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            async Task<(string Name, long Bytes, CompressionMethod Method, string Hash)[]> Generate(string name)
            {
                var path = Path.Combine(directory.FullName, name);
                Assert.Equal(0, await Program.Main([
                    "--zip-workload", path, "42", "2:65537:binary:store",
                    "1:3:repeat:deflate", "1:0:repeat:store",
                ]));
                using var archive = new ZipFile(path);
                Assert.True(archive.TestArchive(testData: true));
                return archive.Cast<ZipEntry>().Select(entry =>
                {
                    using var payload = archive.GetInputStream(entry);
                    return (entry.Name, entry.Size, entry.CompressionMethod, Convert.ToHexString(SHA256.HashData(payload)));
                }).ToArray();
            }

            var first = await Generate("first.zip");
            Assert.Equal(first, await Generate("second.zip"));
            Assert.Equal(new[]
            {
                ("00000001.bin", 65537L, CompressionMethod.Stored),
                ("00000002.bin", 65537L, CompressionMethod.Stored),
                ("00000003.bin", 3L, CompressionMethod.Deflated),
                ("00000004.bin", 0L, CompressionMethod.Stored),
            }, first.Select(entry => (entry.Name, entry.Bytes, entry.Method)));
            Assert.NotEqual(first[0].Hash, first[1].Hash);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("-1", "1:1:binary:store")]
    [InlineData("42", "0:1:binary:store")]
    [InlineData("42", "1:-1:binary:store")]
    [InlineData("42", "1:1:unknown:store")]
    [InlineData("42", "1:1:binary:unknown")]
    [InlineData("42", "2:9223372036854775807:binary:store")]
    [InlineData("42", "9223372036854775807:0:binary:store", "1:0:binary:store")]
    [InlineData("42", "1:9223372036854775807:binary:store", "1:1:binary:store")]
    public async Task Main_InvalidRecipe_LeavesDestinationDirectoryEmpty(string seed, string group, string? secondGroup = null)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "workload.zip");
            string[] args = secondGroup is null
                ? ["--zip-workload", path, seed, group]
                : ["--zip-workload", path, seed, group, secondGroup];
            Assert.Equal(1, await Program.Main(args));
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Main_ExistingArchive_PreservesDestination()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "existing.zip");
            await File.WriteAllTextAsync(path, "existing");
            Assert.Equal(1, await Program.Main(["--zip-workload", path, "42", "1:3:repeat:store"]));
            Assert.Equal("existing", await File.ReadAllTextAsync(path));
            Assert.Single(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunZipWorkloadAsync_CancelledDuringWrite_RemovesStagingArchive()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "workload.zip");
            using var cancellation = new CancellationTokenSource();
            var generation = PerformanceBenchmarkRunner.RunZipWorkloadAsync(
                [path, "42", "1:134217729:binary:store"], cancellation.Token);
            bool stagingCreated = directory.EnumerateFiles(".zip-workload-*.tmp").Any();
            await cancellation.CancelAsync();
            Assert.Equal(130, await generation);
            Assert.True(stagingCreated);
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
