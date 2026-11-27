using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.Zip;
using Xunit;
using Zipper.ArchiveTests;
using Zipper.Cli;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ZipWorkloadTests : TempDirectoryTestBase
{
    private const int ReadBufferSize = 16 * 1024;
    private static readonly DateTime ExpectedTimestamp = new(1980, 1, 1, 0, 0, 0);
    private static readonly string EmptySha256Hex = Convert.ToHexString(SHA256.HashData([]));

    private static (string Name, long Bytes, CompressionMethod Method, long CompressedBytes, DateTime Timestamp, string Hash, byte? UniformByte)[] ReadArchiveInventory(string path)
    {
        using var archive = new ZipFile(path);
        Assert.True(archive.TestArchive(testData: true));

        var buffer = new byte[ReadBufferSize];
        var entries = new List<(string, long, CompressionMethod, long, DateTime, string, byte?)>();
        foreach (ZipEntry entry in archive)
        {
            using var payload = archive.GetInputStream(entry);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long totalRead = 0;
            byte? firstByte = null;
            bool isUniform = true;
            int read;
            while ((read = payload.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                totalRead += read;
                firstByte ??= buffer[0];
                if (isUniform && buffer.AsSpan(0, read).IndexOfAnyExcept(firstByte.Value) >= 0)
                {
                    isUniform = false;
                }
            }

            Assert.Equal(entry.Size, totalRead);
            entries.Add((
                entry.Name,
                entry.Size,
                entry.CompressionMethod,
                entry.CompressedSize,
                entry.DateTime,
                Convert.ToHexString(hasher.GetHashAndReset()),
                entry.Size > 0 && isUniform ? firstByte : null));
        }

        return [.. entries];
    }

    [Fact]
    public async Task Main_RepeatedRecipe_ProducesExactInventorySummaryAndMatchingHashes()
    {
        async Task<((string Name, long Bytes, CompressionMethod Method, long CompressedBytes, DateTime Timestamp, string Hash, byte? UniformByte)[] Inventory, string Stdout, long FileSize)> Generate(string name)
        {
            var path = Path.Combine(this.TempDir, name);
            var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main([
                "--zip-workload", path, "42",
                "2:65537:binary:store",
                "1:65536:repeat:store",
                "1:65537:repeat:deflate",
                "1:129:binary:deflate",
                "1:3:repeat:deflate",
                "1:0:repeat:store",
                "1:0:binary:deflate",
            ]));

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            var fileSize = new FileInfo(path).Length;
            return (ReadArchiveInventory(path), stdout, fileSize);
        }

        var first = await Generate("first.zip");
        var second = await Generate("second.zip");

        Assert.Equal(
            first.Inventory.Select(e => (e.Name, e.Bytes, e.Method, e.Timestamp, e.Hash)),
            second.Inventory.Select(e => (e.Name, e.Bytes, e.Method, e.Timestamp, e.Hash)));
        Assert.Equal(new[]
        {
            ("00000001.bin", 65537L, CompressionMethod.Stored, ExpectedTimestamp),
            ("00000002.bin", 65537L, CompressionMethod.Stored, ExpectedTimestamp),
            ("00000003.bin", 65536L, CompressionMethod.Stored, ExpectedTimestamp),
            ("00000004.bin", 65537L, CompressionMethod.Deflated, ExpectedTimestamp),
            ("00000005.bin", 129L, CompressionMethod.Deflated, ExpectedTimestamp),
            ("00000006.bin", 3L, CompressionMethod.Deflated, ExpectedTimestamp),
            ("00000007.bin", 0L, CompressionMethod.Stored, ExpectedTimestamp),
            ("00000008.bin", 0L, CompressionMethod.Stored, ExpectedTimestamp),
        }, first.Inventory.Select(entry => (entry.Name, entry.Bytes, entry.Method, entry.Timestamp)));

        // Binary entries vary across members and are non-uniform; repeat entries stay uniform across 64 KiB chunks.
        Assert.NotEqual(first.Inventory[0].Hash, first.Inventory[1].Hash);
        Assert.Null(first.Inventory[0].UniformByte);
        Assert.NotNull(first.Inventory[2].UniformByte);
        Assert.NotNull(first.Inventory[3].UniformByte);
        Assert.True(first.Inventory[3].CompressedBytes < first.Inventory[3].Bytes);
        Assert.Null(first.Inventory[4].UniformByte);
        Assert.Equal(EmptySha256Hex, first.Inventory[6].Hash);
        Assert.Equal(EmptySha256Hex, first.Inventory[7].Hash);

        var expectedSummary = $"ZIP workload seed=42 recipe=2:65537:binary:store,1:65536:repeat:store,1:65537:repeat:deflate,1:129:binary:deflate,1:3:repeat:deflate,1:0:repeat:store,1:0:binary:deflate entries=8 payloadBytes=262279 archiveBytes={first.FileSize}";
        Assert.Equal(expectedSummary, first.Stdout.Trim());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2147483647")]
    public async Task Main_BoundarySeed_GeneratesValidDeterministicArchive(string seed)
    {
        var firstPath = Path.Combine(this.TempDir, $"first-{seed}.zip");
        var secondPath = Path.Combine(this.TempDir, $"second-{seed}.zip");

        var (firstExit, _, _) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--zip-workload", firstPath, seed, "1:17:binary:store", "1:17:repeat:deflate"]));
        var (secondExit, _, _) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--zip-workload", secondPath, seed, "1:17:binary:store", "1:17:repeat:deflate"]));

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Equal(
            ReadArchiveInventory(firstPath).Select(e => (e.Name, e.Bytes, e.Method, e.Hash)),
            ReadArchiveInventory(secondPath).Select(e => (e.Name, e.Bytes, e.Method, e.Hash)));
    }

    public static TheoryData<string[]> InvalidWorkloadInputs =>
    [
        [],
        ["workload.zip"],
        ["workload.zip", "42"],
        ["workload.zip", "-1", "1:1:binary:store"],
        ["workload.zip", "abc", "1:1:binary:store"],
        ["workload.zip", "2147483648", "1:1:binary:store"],
        ["workload.zip", "42", "0:1:binary:store"],
        ["workload.zip", "42", "1:-1:binary:store"],
        ["workload.zip", "42", "+1:1:binary:store"],
        ["workload.zip", "42", "1:1:binary"],
        ["workload.zip", "42", "1:1:binary:store:extra"],
        ["workload.zip", "42", "1:1:unknown:store"],
        ["workload.zip", "42", "1:1:binary:unknown"],
        ["workload.zip", "42", "2:9223372036854775807:binary:store"],
        ["workload.zip", "42", "9223372036854775807:0:binary:store", "1:0:binary:store"],
        ["workload.zip", "42", "1:9223372036854775807:binary:store", "1:1:binary:store"],
        ["workload.zip", "42", "1:1:binary:store", "--archive-test-suite", "smoke"],
        ["missing-dir/workload.zip", "42", "1:1:binary:store"],
        ["trailing-slash/", "42", "1:1:binary:store"],
    ];

    [Theory]
    [MemberData(nameof(InvalidWorkloadInputs))]
    public async Task Main_InvalidInput_ReturnsOneAndLeavesDestinationDirectoryEmpty(string[] tailArgs)
    {
        ArgumentNullException.ThrowIfNull(tailArgs);
        string[] args = tailArgs.Length == 0
            ? ["--zip-workload"]
            : ["--zip-workload", Path.Combine(this.TempDir, tailArgs[0]), .. tailArgs[1..]];

        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("ZIP workload error:", stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task Main_ChaosListCombinedWithZipWorkload_RejectsMixedCommand()
    {
        var (exitCode, stdout, _) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--chaos-list", "--zip-workload"]));

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
    }

    [Fact]
    public async Task Main_WhitespaceDestinationPath_ReturnsOneWithoutWriting()
    {
        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--zip-workload", "   ", "42", "1:1:binary:store"]));

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("ZIP workload error:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_InvalidInputWithControlCharacters_SanitizesStderr()
    {
        var path = Path.Combine(this.TempDir, "workload.zip");
        var (exitCode, _, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--zip-workload", path, "42", "1:1:bad\n\x1b[2K:store"]));

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("\x1b", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("bad\n", stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task Main_ExistingArchive_PreservesDestination()
    {
        var path = Path.Combine(this.TempDir, "existing.zip");
        await File.WriteAllTextAsync(path, "existing");
        var (exitCode, _, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--zip-workload", path, "42", "1:3:repeat:store"]));

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", stderr, StringComparison.Ordinal);
        Assert.Equal("existing", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task Main_ExistingDirectoryAtDestination_PreservesDirectoryAndReturnsOne()
    {
        var targetDir = Path.Combine(this.TempDir, "existing-dir");
        Directory.CreateDirectory(targetDir);
        var marker = Path.Combine(targetDir, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");

        var (exitCode, _, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--zip-workload", targetDir, "42", "1:3:repeat:store"]));

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", stderr, StringComparison.Ordinal);
        Assert.Equal("keep", await File.ReadAllTextAsync(marker));
    }

    [SkippableFact]
    public async Task Main_DanglingSymlinkAtDestination_PreservesSymlinkAndReturnsOne()
    {
        Skip.If(OperatingSystem.IsWindows(), "Symlink creation requires elevated privileges on Windows.");

        var linkPath = Path.Combine(this.TempDir, "dangling.zip");
        File.CreateSymbolicLink(linkPath, Path.Combine(this.TempDir, "missing-target.zip"));

        var (exitCode, _, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--zip-workload", linkPath, "42", "1:3:repeat:store"]));

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", stderr, StringComparison.Ordinal);
        Assert.NotNull(new FileInfo(linkPath).LinkTarget);
        Assert.Single(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task Main_ArchiveTestSuiteCombinedWithZipWorkload_ReturnsOneAndLeavesOutputUntouched()
    {
        var outDir = Path.Combine(this.TempDir, "suite");
        var (exitCode, _, _) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            Program.Main(["--archive-test-suite", "smoke", "--output-path", outDir, "--zip-workload"]));

        Assert.Equal(1, exitCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task RunZipWorkloadAsync_PreCancelled_Returns130AndLeavesDirectoryEmpty()
    {
        var path = Path.Combine(this.TempDir, "workload.zip");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            PerformanceBenchmarkRunner.RunZipWorkloadAsync([path, "42", "1:8:binary:store"], cancellation.Token));

        Assert.Equal(130, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("cancelled", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task RunZipWorkloadAsync_CancelledDuringWrite_RemovesStagingArchive()
    {
        var path = Path.Combine(this.TempDir, "workload.zip");
        using var cancellation = new CancellationTokenSource();
        bool stagingObserved = false;

        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            PerformanceBenchmarkRunner.RunZipWorkloadAsync(
                [path, "42", "1:131073:binary:store"],
                cancellation.Token,
                staging =>
                {
                    stagingObserved = File.Exists(staging);
                    cancellation.Cancel();
                }));

        Assert.Equal(130, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("cancelled", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.True(stagingObserved);
        Assert.Empty(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task RunZipWorkloadAsync_WriteFailureDuringStream_RemovesStagingAndReturnsOne()
    {
        var path = Path.Combine(this.TempDir, "workload.zip");
        bool stagingObserved = false;

        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            PerformanceBenchmarkRunner.RunZipWorkloadAsync(
                [path, "42", "2:1024:binary:store"],
                CancellationToken.None,
                staging =>
                {
                    stagingObserved = File.Exists(staging);
                    throw new IOException("simulated disk write failure");
                }));

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("simulated disk write failure", stderr, StringComparison.Ordinal);
        Assert.True(stagingObserved);
        Assert.Empty(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public async Task RunZipWorkloadAsync_TargetCreatedDuringStaging_RemovesStagingAndPreservesTarget()
    {
        var path = Path.Combine(this.TempDir, "raced.zip");
        bool stagingObserved = false;

        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() =>
            PerformanceBenchmarkRunner.RunZipWorkloadAsync(
                [path, "42", "1:1024:binary:store"],
                CancellationToken.None,
                staging =>
                {
                    stagingObserved = File.Exists(staging);
                    if (!File.Exists(path))
                    {
                        File.WriteAllText(path, "raced");
                    }
                }));

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("already exists", stderr, StringComparison.Ordinal);
        Assert.True(stagingObserved);
        Assert.Equal("raced", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.EnumerateFileSystemEntries(this.TempDir));
    }

    [Fact]
    public void Show_WithZipWorkload_PreservesNormalCliDefaultsAndArchiveTestBudgets()
    {
        Assert.Equal(1048576L, ArchiveTestRequest.MaxSidecarBytes);
        Assert.Equal(256L * 1024 * 1024, ArchiveTestRequest.MaxSuiteBytes);

        var defaults = new OutputConfig();
        Assert.Equal(ZipCompressionMethod.Deflate, defaults.CompressionMethod);
        Assert.Equal(1, defaults.Folders);
        Assert.Equal("pdf", defaults.FileType);

        var (_, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(HelpTextGenerator.Show);
        Assert.Contains("--compression <method>   ZIP compression method: store, deflate (default: deflate", stderr, StringComparison.Ordinal);
        Assert.Contains("--zip-workload", stderr, StringComparison.Ordinal);
    }
}
