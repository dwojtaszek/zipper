using Xunit;

using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests;

public class CodedGeneratorTests
{
    private static ColumnDefinition MultiValueColumn(int min = 2, int max = 3) => new()
    {
        Name = "TestCoded",
        Type = "coded",
        MultiValue = true,
        MultiValueCount = new RangeConfig { Min = min, Max = max },
    };

    private static ProfileSettings DefaultSettings() => new() { MultiValueDelimiter = ";" };

    private static ColumnGenerationContext MakeContext(int docIndex, int seed = 42) => new()
    {
        NativeFileIndex = docIndex,
        FolderNumber = 1,
        DocumentIndex = docIndex,
        Seeded = new Random(seed + docIndex),
        Now = DateTime.UtcNow,
    };

    private static int[] WeightedIndices(int count)
    {
        // All point to first value — worst case for the bug
        var indices = new int[count];
        return indices;
    }

    [Fact(Timeout = 2000)]
    public async Task Generate_MultiValue_Weighted_CompletesAndReturnsDistinctValues()
    {
        var values = new string[] { "Alpha", "Beta", "Gamma", "Delta" };
        var indices = WeightedIndices(100);
        var col = MultiValueColumn(min: 2, max: 3);
        var generator = new CodedGenerator(values, indices, col, DefaultSettings());

        var result = await Task.Run(() => generator.Generate(MakeContext(0)));

        var parts = result.Split(';');
        Assert.InRange(parts.Length, 2, 3);
        Assert.Equal(parts.Length, parts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(Timeout = 2000)]
    public async Task Generate_MultiValue_Pareto_CompletesAndReturnsDistinctValues()
    {
        var values = new string[] { "Red", "Green", "Blue", "Yellow" };

        // Pareto-like: heavily skewed toward index 0
        var indices = Enumerable.Range(0, 200).Select(i => i < 180 ? 0 : 1).ToArray();
        var col = MultiValueColumn(min: 2, max: 3);
        var generator = new CodedGenerator(values, indices, col, DefaultSettings());

        var result = await Task.Run(() => generator.Generate(MakeContext(5)));

        var parts = result.Split(';');
        Assert.InRange(parts.Length, 2, 3);
        Assert.Equal(parts.Length, parts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_SingleValue_Weighted_UsesDocumentIndexPath()
    {
        var values = new string[] { "One", "Two", "Three" };
        var indices = new[] { 2, 0, 1 };
        var col = new ColumnDefinition { Name = "Test", Type = "coded", MultiValue = false };
        var generator = new CodedGenerator(values, indices, col, DefaultSettings());

        // index 0 -> distributionIndices[0]=2 -> values[2]="Three"
        var result = generator.Generate(MakeContext(0));
        Assert.Equal("Three", result);

        // index 1 -> distributionIndices[1]=0 -> values[0]="One"
        result = generator.Generate(MakeContext(1));
        Assert.Equal("One", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generate_MultiValue_AllIdenticalPool_CompletesAndReturnsCappedDistinctValue()
    {
        var values = new string[] { "A", "A" };
        var col = MultiValueColumn(min: 2, max: 2);
        var generator = new CodedGenerator(values, null, col, DefaultSettings());

        var result = await Task.Run(() => generator.Generate(MakeContext(0)));

        Assert.Equal("A", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generate_MultiValue_PartiallyDuplicatedPool_CompletesAndReturnsDistinctValues()
    {
        var values = new string[] { "A", "A", "B", "B", "C" };
        var col = MultiValueColumn(min: 2, max: 3);
        var generator = new CodedGenerator(values, null, col, DefaultSettings());

        var result = await Task.Run(() => generator.Generate(MakeContext(0)));

        var parts = result.Split(';');
        Assert.InRange(parts.Length, 2, 3);
        Assert.Equal(parts.Length, parts.Distinct(StringComparer.Ordinal).Count());
        foreach (var part in parts)
        {
            Assert.Contains(part, new[] { "A", "B", "C" });
        }
    }

    [Fact(Timeout = 2000)]
    public async Task Generate_MultiValue_TargetAboveDistinctCount_CappedAtDistinctAvailableCount()
    {
        var values = new string[] { "X", "Y" };
        var col = MultiValueColumn(min: 5, max: 10);
        var generator = new CodedGenerator(values, null, col, DefaultSettings());

        var result = await Task.Run(() => generator.Generate(MakeContext(0)));

        var parts = result.Split(';');
        Assert.Equal(2, parts.Length);
        Assert.Equal(2, parts.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("X", parts);
        Assert.Contains("Y", parts);
    }

    [Fact]
    public void Generate_EmptyPool_ReturnsEmptyString()
    {
        var values = Array.Empty<string>();
        var colMulti = MultiValueColumn(min: 2, max: 3);
        var genMulti = new CodedGenerator(values, null, colMulti, DefaultSettings());
        Assert.Equal(string.Empty, genMulti.Generate(MakeContext(0)));

        var colSingle = new ColumnDefinition { Name = "Test", Type = "coded", MultiValue = false };
        var genSingle = new CodedGenerator(values, null, colSingle, DefaultSettings());
        Assert.Equal(string.Empty, genSingle.Generate(MakeContext(0)));
    }

    [Fact]
    public void Generate_MultiValue_SeededReproducibility_ProducesIdenticalResults()
    {
        var values = new string[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };
        var col = MultiValueColumn(min: 2, max: 4);
        var generator = new CodedGenerator(values, null, col, DefaultSettings());

        var result1 = generator.Generate(MakeContext(0, seed: 12345));
        var result2 = generator.Generate(MakeContext(0, seed: 12345));

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Generate_MultiValue_ZeroOrNegativeCount_ReturnsEmptyString()
    {
        var values = new string[] { "A", "B", "C" };
        var col = MultiValueColumn(min: 0, max: 0);
        var generator = new CodedGenerator(values, null, col, DefaultSettings());

        var result = generator.Generate(MakeContext(0));

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Generate_MultiValue_NullMultiValueCount_UsesDefaultRangeMin1Max3()
    {
        var values = new string[] { "Alpha", "Beta", "Gamma", "Delta" };
        var col = new ColumnDefinition
        {
            Name = "TestCoded",
            Type = "coded",
            MultiValue = true,
            MultiValueCount = null,
        };
        var generator = new CodedGenerator(values, null, col, DefaultSettings());

        for (int i = 0; i < 20; i++)
        {
            var result = generator.Generate(MakeContext(i));
            var parts = result.Split(';');
            Assert.InRange(parts.Length, 1, 3);
            Assert.Equal(parts.Length, parts.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Generate_MultiValue_TargetCountOneOnMultiElementPool_ReturnsSingleValue()
    {
        var values = new string[] { "First", "Second", "Third" };
        var col = MultiValueColumn(min: 1, max: 1);
        var generator = new CodedGenerator(values, null, col, DefaultSettings());

        var result = generator.Generate(MakeContext(0));

        Assert.Contains(result, values);
        Assert.DoesNotContain(";", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_MultiValue_CustomDelimiter_JoinsWithCustomDelimiter()
    {
        var values = new string[] { "One", "Two", "Three" };
        var col = MultiValueColumn(min: 2, max: 2);
        var settings = new ProfileSettings { MultiValueDelimiter = "|" };
        var generator = new CodedGenerator(values, null, col, settings);

        var result = generator.Generate(MakeContext(0));

        var parts = result.Split('|');
        Assert.Equal(2, parts.Length);
        Assert.Equal(2, parts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(Timeout = 15000)]
    public async Task Subprocess_DuplicateCodesProfile_CompletesWithoutHanging()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var profileJsonPath = Path.Combine(tempDir, "duplicate-codes.json");
            var profileJson = """
            {
              "name": "duplicate-codes",
              "settings": {"emptyValuePercentage": 0},
              "dataSources": {"codes": {"values": ["A", "A"]}},
              "columns": [
                {"name": "DOCID", "type": "identifier", "required": true},
                {"name": "CODES", "type": "coded", "required": true,
                 "dataSource": "codes", "multiValue": true,
                 "multiValueCount": {"min": 2, "max": 2}}
              ]
            }
            """;
            await File.WriteAllTextAsync(profileJsonPath, profileJson);

            var outDir = Path.Combine(tempDir, "out");
            Directory.CreateDirectory(outDir);

            var assemblyPath = typeof(Zipper.Program).Assembly.Location;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{assemblyPath}\" --loadfile-only --count 1 --column-profile ./duplicate-codes.json --seed 42 --output-path ./out",
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(process);

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Process timed out and hung on duplicate codes column profile.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            Assert.True(process.ExitCode == 0, $"Process failed with exit code {process.ExitCode}. Stderr: {stderr}. Stdout: {stdout}");

            var datFiles = Directory.GetFiles(outDir, "*.dat");
            Assert.Single(datFiles);
            var datContent = await File.ReadAllTextAsync(datFiles[0]);
            var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length); // header + 1 row
            Assert.Contains("DOC00000001", lines[1], StringComparison.Ordinal);
            Assert.Contains("A", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
