using Xunit;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ProgramTests
{
    private static async Task<int> RunWithRedirectedConsole(Func<Task<int>> action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            return await action().ConfigureAwait(false);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunWithRedirectedConsoleAndCapture(Func<Task<int>> action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            int exitCode = await action().ConfigureAwait(false);
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Main_WithHelpFlag_ReturnsOne()
    {
        // Arrange
        string[] args = { "--help" };

        // Act
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

        // Assert
        Assert.Equal(1, exitCode); // Help shows usage and returns 1
    }

    [Fact]
    public async Task Main_WithInvalidArguments_ReturnsErrorCode()
    {
        // Arrange - Missing required arguments
        string[] args = Array.Empty<string>();

        // Act
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

        // Assert
        Assert.Equal(1, exitCode); // Should return error code for invalid args
    }

    [Fact]
    public async Task Main_WithMissingOutputPath_ReturnsErrorCode()
    {
        // Arrange - Missing required --output argument
        string[] args = { "--count", "10", "--type", "pdf" };

        // Act
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

        // Assert
        Assert.Equal(1, exitCode); // Should return error code
    }

    [Fact]
    public async Task Main_WithInvalidFileType_ReturnsErrorCode()
    {
        // Arrange
        string tempPath = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        string[] args =
        {
            "--output", tempPath,
            "--count", "1",
            "--type", "invalid_type",
        };

        // Act
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

        // Assert
        Assert.Equal(1, exitCode); // Should return error code for invalid type

        // Cleanup
        if (Directory.Exists(tempPath))
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task Main_WithInvalidCount_ReturnsErrorCode()
    {
        // Arrange
        string tempPath = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        string[] args =
        {
            "--output", tempPath,
            "--count", "-1",  // Invalid count
            "--type", "pdf",
        };

        // Act
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

        // Assert
        Assert.Equal(1, exitCode); // Should return error code

        // Cleanup
        if (Directory.Exists(tempPath))
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task Main_WithInvalidNumericArgument_ReturnsErrorCode()
    {
        // Arrange
        string tempPath = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        string[] args =
        {
            "--output", tempPath,
            "--count", "abc",  // Invalid count (must be integer string)
            "--type", "pdf",
        };

        try
        {
            // Act
            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));

            // Assert
            Assert.Equal(1, exitCode); // Should return error code
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task Main_WithBenchmarkFlag_RunsAndReturnsValidExitCode()
    {
        // Arrange
        string[] args = { "--benchmark" };

        // Act
        var (exitCode, output) = await RunWithRedirectedConsoleAndCapture(() => Program.Main(args));

        // Assert: 0 = all metrics passed, 1 = some metrics failed (timing-dependent on CI runners).
        // Both are valid outcomes; verify the benchmark suite actually ran (not a crash).
        Assert.True(exitCode is 0 or 1, $"Expected exit code 0 or 1, got {exitCode}");
        Assert.Contains("Benchmark Suite", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WithUnknownFlag_ReturnsErrorCode()
    {
        string[] args = { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--unknown-flag" };
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithUnknownPositionalValue_ReturnsErrorCode()
    {
        string[] args = { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "extra_value" };
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithUnknownFlagInValuePosition_ReturnsErrorCode()
    {
        string[] args = { "--type", "pdf", "--count", "10", "--output-path", "--unknown-flag" };
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithBenchmarkAndUnknownFlag_BypassesValidationAndRunsBenchmark()
    {
        string[] args = { "--benchmark", "--unknown-flag" };
        var (exitCode, output) = await RunWithRedirectedConsoleAndCapture(() => Program.Main(args));
        Assert.True(exitCode is 0 or 1, $"Expected exit code 0 or 1, got {exitCode}");
        Assert.Contains("Benchmark Suite", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WithChaosListAndUnknownFlag_BypassesValidationAndReturnsZero()
    {
        string[] args = { "--chaos-list", "--unknown-flag" };
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Main_WithVersionFlag_PrintsVersionAndReturnsZero()
    {
        // Arrange
        string[] args = { "--version" };
        var originalOut = Console.Out;
        var originalError = Console.Error;
        string output;
        int exitCode;

        // Act
        try
        {
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            exitCode = await Program.Main(args);
            output = outWriter.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Zipper v", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WithVersionAndUnknownFlag_ReturnsErrorCode()
    {
        string[] args = { "--version", "--unknown-flag" };
        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithoutVersionFlag_DoesNotPrintStartupBanner()
    {
        // Arrange — any invocation other than --version must not emit the
        // version banner (REQ-034). Empty args fail fast to the help path,
        // proving banner suppression without touching the filesystem.
        string[] args = [];
        var originalOut = Console.Out;
        var originalError = Console.Error;
        string output;

        // Act
        try
        {
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            _ = await Program.Main(args);
            output = outWriter.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        // Assert
        Assert.DoesNotContain("https://github.com/dwojtaszek/zipper/", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WithSameSeed_ProducesIdenticalOutputSizes()
    {
        // Arrange
        string tempPath1 = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        string tempPath2 = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());

        try
        {
            string[] args1 =
            {
                "--output-path", tempPath1,
                "--count", "10",
                "--type", "pdf",
                "--seed", "42",
                "--with-metadata",
                "--with-text"
            };

            string[] args2 =
            {
                "--output-path", tempPath2,
                "--count", "10",
                "--type", "pdf",
                "--seed", "42",
                "--with-metadata",
                "--with-text"
            };

            // Act
            int exitCode1 = await RunWithRedirectedConsole(() => Program.Main(args1));
            int exitCode2 = await RunWithRedirectedConsole(() => Program.Main(args2));

            // Assert exit codes
            Assert.Equal(0, exitCode1);
            Assert.Equal(0, exitCode2);

            var files1 = Directory.GetFiles(tempPath1, "*.*", SearchOption.AllDirectories)
                                  .Select(f => Path.GetRelativePath(tempPath1, f))
                                  .Where(f => !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                              !f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) &&
                                              !f.Contains("_properties.json", StringComparison.OrdinalIgnoreCase))
                                  .OrderBy(n => n, StringComparer.Ordinal).ToList();
            var files2 = Directory.GetFiles(tempPath2, "*.*", SearchOption.AllDirectories)
                                  .Select(f => Path.GetRelativePath(tempPath2, f))
                                  .Where(f => !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                              !f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) &&
                                              !f.Contains("_properties.json", StringComparison.OrdinalIgnoreCase))
                                  .OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(files1, files2);

            using var sha256 = System.Security.Cryptography.SHA256.Create();

            foreach (var relFile in files1)
            {
                var file1Path = Path.Combine(tempPath1, relFile);
                var file2Path = Path.Combine(tempPath2, relFile);

                using var fs1 = File.OpenRead(file1Path);
                using var fs2 = File.OpenRead(file2Path);

                // 1. Compare header length
                Assert.Equal(fs1.Length, fs2.Length);

                // 2. Compare first 16 bytes
                var header1 = new byte[Math.Min(16, fs1.Length)];
                var header2 = new byte[Math.Min(16, fs2.Length)];

                _ = fs1.Read(header1, 0, header1.Length);
                _ = fs2.Read(header2, 0, header2.Length);

                Assert.Equal(header1, header2);

                // 3. Compare full file hash
                fs1.Position = 0;
                fs2.Position = 0;

                var hash1 = sha256.ComputeHash(fs1);
                var hash2 = sha256.ComputeHash(fs2);

                Assert.Equal(hash1, hash2);
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath1))
            {
                Directory.Delete(tempPath1, true);
            }

            if (Directory.Exists(tempPath2))
            {
                Directory.Delete(tempPath2, true);
            }
        }
    }

    [Fact]
    public async Task Main_WithSeedAndTargetZipSize_ProducesIdenticalOutputAndHashes()
    {
        // Arrange
        string tempPath1 = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        string tempPath2 = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());

        try
        {
            string[] args1 =
            {
                "--output-path", tempPath1,
                "--count", "5",
                "--type", "pdf",
                "--seed", "123",
                "--target-zip-size", "1MB",
                "--with-metadata"
            };

            string[] args2 =
            {
                "--output-path", tempPath2,
                "--count", "5",
                "--type", "pdf",
                "--seed", "123",
                "--target-zip-size", "1MB",
                "--with-metadata"
            };

            // Act
            int exitCode1 = await RunWithRedirectedConsole(() => Program.Main(args1));
            int exitCode2 = await RunWithRedirectedConsole(() => Program.Main(args2));

            // Assert
            Assert.Equal(0, exitCode1);
            Assert.Equal(0, exitCode2);

            // Find the load file in both outputs
            var datFile1 = Directory.GetFiles(tempPath1, "*.dat").FirstOrDefault();
            var datFile2 = Directory.GetFiles(tempPath2, "*.dat").FirstOrDefault();

            Assert.NotNull(datFile1);
            Assert.NotNull(datFile2);

            var content1 = await File.ReadAllTextAsync(datFile1);
            var content2 = await File.ReadAllTextAsync(datFile2);

            // Entire load file contents must be identical, including columns
            Assert.Equal(content1, content2);

            var zipFile1 = Directory.GetFiles(tempPath1, "*.zip").FirstOrDefault();
            var zipFile2 = Directory.GetFiles(tempPath2, "*.zip").FirstOrDefault();

            Assert.NotNull(zipFile1);
            Assert.NotNull(zipFile2);

            using (var archive1 = System.IO.Compression.ZipFile.OpenRead(zipFile1))
            using (var archive2 = System.IO.Compression.ZipFile.OpenRead(zipFile2))
            {
                var entries1 = archive1.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();
                var entries2 = archive2.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();

                Assert.Equal(entries1.Count, entries2.Count);
                for (int i = 0; i < entries1.Count; i++)
                {
                    var entry1 = entries1[i];
                    var entry2 = entries2[i];

                    Assert.Equal(entry1.FullName, entry2.FullName);
                    Assert.Equal(entry1.Length, entry2.Length);

                    using var stream1 = entry1.Open();
                    using var stream2 = entry2.Open();

                    var hash1 = System.Security.Cryptography.MD5.HashData(stream1);
                    var hash2 = System.Security.Cryptography.MD5.HashData(stream2);

                    Assert.Equal(hash1, hash2);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempPath1))
            {
                Directory.Delete(tempPath1, true);
            }

            if (Directory.Exists(tempPath2))
            {
                Directory.Delete(tempPath2, true);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Archive Test workflow dispatch (ticket #844, ADR-0008)
    // -----------------------------------------------------------------------

    private static readonly System.Text.RegularExpressions.Regex FixturePairPattern =
        new("^atc-[0-9a-f]{64}\\.(zip|json)$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Main_WithArchiveTestSuite_PublishesPairsAndReturnsZero()
    {
        string tempPath = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        try
        {
            string[] args = { "--archive-test-suite", "smoke", "--seed", "42", "--output-path", tempPath };

            int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
            Assert.Equal(0, exitCode);

            // Flat fixture pairs only: no outer Archive, no subfolders. The smoke suite
            // is the five frozen Case Keys (#834), so exactly ten flat files publish.
            string[] files = Directory.GetFiles(tempPath);
            Assert.Equal(10, files.Length);
            Assert.All(files, file => Assert.Matches(FixturePairPattern, Path.GetFileName(file)));

            // JSON identity and filenames agree.
            foreach (string jsonPath in Directory.GetFiles(tempPath, "*.json"))
            {
                var testCase = ArchiveTests.ArchiveTestJson.Parse(File.ReadAllBytes(jsonPath));
                Assert.Equal(Path.GetFileNameWithoutExtension(jsonPath), testCase.FixtureId);
                Assert.Equal(testCase.FixtureId + ".zip", testCase.Archive.FileName);
                Assert.True(File.Exists(Path.Combine(tempPath, testCase.Archive.FileName)));
            }
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task Main_WithArchiveTestSuiteAndGenerationFlag_ReturnsOne()
    {
        string[] args = { "--archive-test-suite", "smoke", "--folders", "1", "--output-path", Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString()) };

        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithArchiveTestSuiteAndBenchmarkFlag_ReturnsOne()
    {
        string[] args = { "--archive-test-suite", "smoke", "--benchmark" };

        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithArchiveTestSuiteAndVersionFlag_ReturnsOne()
    {
        // --version only short-circuits as a single argument; combined with the
        // Archive Test workflow it is an unowned flag and the closed set rejects it.
        string[] args = { "--archive-test-suite", "smoke", "--version" };

        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithArchiveTestSuiteAndChaosListFlag_ReturnsOne()
    {
        string[] args = { "--archive-test-suite", "smoke", "--chaos-list" };

        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithArchiveTestCasesWithoutSuite_ReturnsOne()
    {
        string[] args = { "--archive-test-cases", "valid-empty", "--output-path", Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString()) };

        int exitCode = await RunWithRedirectedConsole(() => Program.Main(args));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Main_WithArchiveTestSuiteRepeatedRunIntoSameDirectory_ReturnsOneAndPreservesFirstRun()
    {
        string tempPath = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        try
        {
            string[] args = { "--archive-test-suite", "smoke", "--output-path", tempPath };

            Assert.Equal(0, await RunWithRedirectedConsole(() => Program.Main(args)));
            string[] before = Directory.GetFiles(tempPath).Order(StringComparer.Ordinal).ToArray();
            var beforeBytes = before.Select(File.ReadAllBytes).ToList();

            Assert.Equal(1, await RunWithRedirectedConsole(() => Program.Main(args)));

            string[] after = Directory.GetFiles(tempPath).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(before, after);
            Assert.Equal(beforeBytes, after.Select(File.ReadAllBytes).ToList());
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }
}
