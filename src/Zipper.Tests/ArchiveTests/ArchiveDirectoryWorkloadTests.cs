using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zipper.ArchiveTests;
using IndependentZipEntry = ICSharpCode.SharpZipLib.Zip.ZipEntry;
using IndependentZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ArchiveDirectoryWorkloadTests : WorkingDirectoryTestBase
{
    private const string TreeFile = "café/文/file.txt";

    private static readonly string[] TreeDirectories =
        ["café/", "café/文/", "café/空/", "café/空/a/", "café/空/a/b/"];

    [Theory]
    [InlineData("directory-tree-explicit", true, true)]
    [InlineData("directory-tree-implicit", false, true)]
    [InlineData("directory-tree-only", true, false)]
    public async Task Tree_ListsExactNamesAndKindsByOrdinal(string key, bool explicitDirectories, bool hasFile)
    {
        var (path, fixture) = Assert.Single(await PublishAsync("compatibility", key));
        var expected = new List<(string Name, bool IsDirectory)>();
        if (explicitDirectories)
        {
            expected.AddRange(TreeDirectories.Select(name => (name, true)));
        }

        if (hasFile)
        {
            expected.Add((TreeFile, false));
        }

        using var archive = new IndependentZipFile(path);
        Assert.Equal(expected, archive.Cast<IndependentZipEntry>().Select(entry => (entry.Name, entry.IsDirectory)));
        Assert.Equal(expected, fixture.Entries.Select(entry => (entry.ReadableName, entry.Kind == "directory")));
        Assert.All(archive.Cast<IndependentZipEntry>().Where(entry => entry.IsDirectory), entry =>
        {
            Assert.Equal(0, entry.Size);
            Assert.Equal(0, entry.Crc);
        });
        if (hasFile)
        {
            using var stream = archive.GetInputStream(archive.GetEntry(TreeFile));
            Assert.Equal(Hash(Encoding.UTF8.GetBytes("atc-directory-tree")), Hash(stream));
        }
    }

    [SkippableTheory]
    [InlineData("directory-tree-explicit", true, true)]
    [InlineData("directory-tree-implicit", false, true)]
    [InlineData("directory-tree-only", true, false)]
    public async Task Tree_MaterializesItsDestinationProjection(string key, bool explicitDirectories, bool hasFile)
    {
        var directories = explicitDirectories ? TreeDirectories : ["café/", "café/文/"];
        AssertDestinationCanStore(directories.Select(name => name + "probe").ToArray());

        var (path, _) = Assert.Single(await PublishAsync("compatibility", key));
        var destination = Path.Combine(TempDir, "extracted");
        ZipFile.ExtractToDirectory(path, destination);
        Assert.Equal(
            directories.Order(StringComparer.Ordinal),
            DestinationPaths(destination, Directory.EnumerateDirectories(destination, "*", SearchOption.AllDirectories))
                .Select(name => name + "/"));
        Assert.Equal(
            hasFile ? [TreeFile] : Array.Empty<string>(),
            DestinationPaths(destination, Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)));
        if (hasFile)
        {
            Assert.Equal("atc-directory-tree", await File.ReadAllTextAsync(Path.Combine(destination, TreeFile)));
        }
    }

    [Theory]
    [InlineData("case-collision", "node.txt", "Node.txt")]
    [InlineData("unicode-normalization-collision", "café.txt", "cafe\u0301.txt")]
    public async Task CollidingNames_PreserveExactZipNamesAndDistinctHashes(string key, string first, string second)
    {
        var (path, fixture) = Assert.Single(await PublishAsync("security", key));
        using var archive = new IndependentZipFile(path);
        Assert.Equal([first, second], archive.Cast<IndependentZipEntry>().Select(entry => entry.Name));
        Assert.Equal([first, second], fixture.Entries.Select(entry => entry.ReadableName));

        var hashes = archive.Cast<IndependentZipEntry>().Select(entry =>
        {
            using var content = archive.GetInputStream(entry);
            return Hash(content);
        }).ToArray();
        Assert.NotEqual(hashes[0], hashes[1]);
        Assert.Equal(fixture.Entries.Select(entry => entry.ContentSha256), hashes);
    }

    [SkippableTheory]
    [InlineData("case-collision", "node.txt", "Node.txt")]
    [InlineData("unicode-normalization-collision", "café.txt", "cafe\u0301.txt")]
    public async Task CollidingNames_MaterializeWhereBothSpellingsAreSupported(string key, string first, string second)
    {
        AssertDestinationCanStore([first, second]);

        var (path, fixture) = Assert.Single(await PublishAsync("security", key));
        var destination = Path.Combine(TempDir, "extracted");
        ZipFile.ExtractToDirectory(path, destination);
        Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal),
            DestinationPaths(destination, Directory.EnumerateFiles(destination)));
        Assert.All(fixture.Entries, entry =>
        {
            using var stream = File.OpenRead(Path.Combine(destination, entry.ReadableName));
            Assert.Equal(entry.ContentSha256, Hash(stream));
        });
    }

    [Theory]
    [InlineData("compatibility", "valid-name-length-boundaries", 0, 1024, 1023, 5)]
    [InlineData("compatibility", "valid-name-length-boundaries", 1, 1025, 1024, 5)]
    [InlineData("compatibility", "valid-name-length-boundaries", 2, 1026, 1025, 5)]
    [InlineData("security", "path-adls-segments-boundary", 0, 258, 258, 63)]
    [InlineData("security", "path-adls-segments-exceeded", 0, 262, 262, 64)]
    public async Task PathBoundaries_MeasureBytesScalarsAndDepthIncludingFilename(
        string suite, string key, int ordinal, int bytes, int scalars, int segments)
    {
        var (path, fixture) = Assert.Single(await PublishAsync(suite, key));
        using var archive = new IndependentZipFile(path);
        var entry = archive[ordinal];
        Assert.True(entry.IsFile);
        Assert.Equal(fixture.Entries[ordinal].ReadableName, entry.Name);
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(entry.Name));
        Assert.Equal(scalars, entry.Name.EnumerateRunes().Count());
        Assert.Equal(segments, entry.Name.Split('/').Length);
        Assert.Equal(bytes, fixture.Entries[ordinal].NameLengthUtf8Bytes);
        Assert.Equal(scalars, fixture.Entries[ordinal].NameLengthScalars);

        var rawName = Encoding.UTF8.GetBytes(entry.Name);
        Assert.Equal(rawName, Convert.FromHexString(fixture.Entries[ordinal].LocalNameRaw));
        Assert.Equal(rawName, Convert.FromHexString(fixture.Entries[ordinal].CentralNameRaw));
    }

    [SkippableTheory]
    [InlineData("compatibility", "valid-name-length-boundaries")]
    [InlineData("security", "path-adls-segments-boundary,path-adls-segments-exceeded")]
    public async Task PathBoundaries_MaterializeWhereTheDestinationSupportsThem(string suite, string keys)
    {
        var pairs = await PublishAsync(suite, keys);
        AssertDestinationCanStore(pairs.SelectMany(pair => pair.Fixture.Entries.Select(entry => entry.ReadableName)).ToArray());

        foreach (var (path, fixture) in pairs)
        {
            var destination = Path.Combine(TempDir, fixture.CaseKey);
            ZipFile.ExtractToDirectory(path, destination);
            Assert.Equal(fixture.Entries.Select(entry => entry.ReadableName).Order(StringComparer.Ordinal),
                DestinationPaths(destination, Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)));
            Assert.All(fixture.Entries, entry =>
            {
                using var stream = File.OpenRead(Path.Combine(destination, entry.ReadableName));
                Assert.Equal(entry.ContentSha256, Hash(stream));
            });
        }
    }

    [Fact]
    public async Task OptInWorkload_PublishesTenBoundedFixturesWithTenThousandUniqueEmptyDirectories()
    {
        var pairs = await PublishAsync("directory-workload", null);
        var names = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal(10, pairs.Length);
        foreach (var (path, fixture) in pairs)
        {
            Assert.Equal(1000, fixture.Entries.Count);
            Assert.Equal(0, fixture.Limits.ExpandedBytesBudget);
            using var archive = new IndependentZipFile(path);
            Assert.Equal(1000, archive.Count);
            Assert.Equal(fixture.Entries.Select(entry => entry.ReadableName),
                archive.Cast<IndependentZipEntry>().Select(entry => entry.Name));
            Assert.All(archive.Cast<IndependentZipEntry>(), entry =>
            {
                Assert.True(entry.IsDirectory);
                Assert.Equal(0, entry.Size);
                Assert.Equal(0, entry.Crc);
                Assert.True(names.Add(entry.Name), entry.Name);
            });
        }

        Assert.Equal(10000, names.Count);
    }

    private async Task<(string Path, ArchiveTestCase Fixture)[]> PublishAsync(string suite, string? keys)
    {
        var destination = Path.Combine(TempDir, "fixtures");
        var args = new List<string> { "--archive-test-suite", suite, "--seed", "42", "--output-path", destination };
        if (keys is not null)
        {
            args.AddRange(["--archive-test-cases", keys]);
        }

        var (exitCode, _, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args.ToArray()));
        Assert.True(exitCode == 0, stderr);
        return Directory.GetFiles(destination, "*.json")
            .Select(path => (Path.ChangeExtension(path, ".zip"), ArchiveTestJson.Parse(File.ReadAllBytes(path))))
            .ToArray();
    }

    private void AssertDestinationCanStore(string[] names)
    {
        // Keep the probe root at least as long as the actual fixture destinations.
        var probe = Path.Combine(TempDir, "destination-capability-probe");
        try
        {
            foreach (var name in names)
            {
                Assert.All(name.Split('/'), segment =>
                {
                    Assert.NotEmpty(segment);
                    Assert.DoesNotContain(segment, new[] { ".", ".." });
                    Assert.DoesNotContain('\\', segment);
                    Assert.DoesNotContain(':', segment);
                });
                var path = Path.Combine(probe, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = File.Open(path, FileMode.CreateNew);
            }
        }
        catch (IOException ex)
        {
            Skip.If(true, $"Destination rejected the exact paths: {ex.Message}");
        }

        Skip.If(!names.Order(StringComparer.Ordinal).SequenceEqual(
            DestinationPaths(probe, Directory.EnumerateFiles(probe, "*", SearchOption.AllDirectories))),
            "Destination changes the spelling of the requested paths.");
    }

    private static IEnumerable<string> DestinationPaths(string root, IEnumerable<string> paths) =>
        paths.Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);

    private static string Hash(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
