using Xunit;
using Zipper.Cli.Modules;
using Zipper.Config;
using Zipper.SourceInput;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class OutputModuleTests
{
    private static bool TryBuild(string?[] apply, out OutputConfig config)
    {
        var module = new OutputModule();
        for (int i = 0; i < apply.Length; i += 2)
        {
            Assert.True(module.TryApply(apply[i]!, apply[i + 1]));
        }
        return module.TryBuild(null, out config);
    }

    private static string CaptureError(Action action)
    {
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                action();
                return errWriter.ToString();
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    // --- TryApply raw storage ---

    [Fact]
    public void TryApply_Count_StoresValue()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--count", "42"));
        Assert.Equal(42, module.Count);
    }

    [Fact]
    public void TryApply_CountInvalid_ReturnsFalse()
    {
        var error = CaptureError(() => Assert.False(new OutputModule().TryApply("--count", "notanumber")));
        Assert.Contains("Error: Invalid value for --count: 'notanumber'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_FoldersInvalid_ReturnsFalse()
    {
        var error = CaptureError(() => Assert.False(new OutputModule().TryApply("--folders", "notanumber")));
        Assert.Contains("Error: Invalid value for --folders: 'notanumber'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_Type_StoresValue()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--type", "pdf"));
        Assert.Equal("pdf", module.FileType);
    }

    [Fact]
    public void TryApply_Types_StoresValue()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--types", "pdf:1,eml:1"));
        Assert.Equal("pdf:1,eml:1", module.FileTypes);
    }

    [Fact]
    public void TryApply_Encoding_StoresValueAndSetsExplicit()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--encoding", "UTF-16"));
        Assert.Equal("UTF-16", module.Encoding);
        Assert.True(module.IsEncodingExplicit);
    }

    [Fact]
    public void TryApply_Distribution_StoresValue()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--distribution", "gaussian"));
        Assert.Equal("gaussian", module.Distribution);
    }

    [Fact]
    public void TryApply_TargetZipSize_StoresValue()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--target-zip-size", "500MB"));
        Assert.Equal("500MB", module.TargetZipSize);
    }

    [Fact]
    public void TryApply_WithTextFlag_StoresTrue()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--with-text", null));
        Assert.True(module.WithText);
    }

    [Fact]
    public void TryApply_IncludeLoadFileFlag_StoresTrue()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--include-load-file", null));
        Assert.True(module.IncludeLoadFile);
    }

    [Fact]
    public void TryApply_MissingValue_ReturnsFalse()
    {
        var error = CaptureError(() => Assert.False(new OutputModule().TryApply("--count", null)));
        Assert.Contains("Error: --count requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() => Assert.False(new OutputModule().TryApply("--type", null)));
        Assert.Contains("Error: --type requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() => Assert.False(new OutputModule().TryApply("--types", null)));
        Assert.Contains("Error: --types requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() => Assert.False(new OutputModule().TryApply("--output-path", null)));
        Assert.Contains("Error: --output-path requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() => Assert.False(new OutputModule().TryApply("--folders", null)));
        Assert.Contains("Error: --folders requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() => Assert.False(new OutputModule().TryApply("--encoding", null)));
        Assert.Contains("Error: --encoding requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() => Assert.False(new OutputModule().TryApply("--distribution", null)));
        Assert.Contains("Error: --distribution requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() => Assert.False(new OutputModule().TryApply("--target-zip-size", null)));
        Assert.Contains("Error: --target-zip-size requires a value.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new OutputModule().TryApply("--unknown-flag", "x"));
    }

    [Fact]
    public void TakesValue_FlaglessFlags_ReturnsFalse()
    {
        var module = new OutputModule();
        Assert.False(module.TakesValue("--with-text"));
        Assert.False(module.TakesValue("--include-load-file"));
        Assert.True(module.TakesValue("--type"));
        Assert.True(module.TakesValue("--count"));
    }

    // --- TryBuild validation (message order mirrors today's validators) ---

    [Fact]
    public void TryBuild_CountZero_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--count", "0" }, out _));
        });
        Assert.Contains("Error: --count must be a positive number.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_CountNegative_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--count", "-1" }, out _));
        });
        Assert.Contains("Error: --count must be a positive number.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_CountExceedsMax_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--count", "2147483647" }, out _));
        });
        Assert.Contains("Error: --count must not exceed 2147483646.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_NullOutputPath_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10" }, out _));
        });
        Assert.Contains("Error: Output path is required.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_EmptyOutputPath_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", "" }, out _));
        });
        Assert.Contains("Error: Output path is required.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_PathTraversal_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", "../outside" }, out _));
        });
        Assert.Contains("Error: Path traversal detected.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_UnknownType_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "bogus", "--count", "10", "--output-path", Directory.GetCurrentDirectory() }, out _));
        });
        Assert.Contains("Error: Unsupported file type 'bogus'. Supported types: pdf, jpg, tiff, eml, docx, xlsx.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_TypeAndTypes_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--types", "eml:1", "--count", "10", "--output-path", Directory.GetCurrentDirectory() }, out _));
        });
        Assert.Contains("Error: --type and --types cannot be used together. Use --types for a File Type mix.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_InvalidRatio_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--types", "bogus:1", "--count", "10", "--output-path", Directory.GetCurrentDirectory() }, out _));
        });
        Assert.Contains("Error: Unsupported file type 'bogus' in --types.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_TargetZipSizeInvalidFormat_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--target-zip-size", "invalid" }, out _));
        });
        Assert.Contains("Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB).", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_TargetZipSizeZero_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--target-zip-size", "0KB" }, out _));
        });
        Assert.Contains("Error: --target-zip-size must be positive.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_InvalidEncoding_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--encoding", "INVALID_ENCODING" }, out _));
        });
        Assert.Contains("Error: Invalid encoding 'INVALID_ENCODING'. Supported values are UTF-8, UTF-16, ANSI.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_EmptyEncoding_PassesThroughWithoutError()
    {
        var module = new OutputModule();
        Assert.True(module.TryApply("--encoding", ""));
        Assert.True(module.TryApply("--type", "pdf"));
        Assert.True(module.TryApply("--count", "10"));
        Assert.True(module.TryApply("--output-path", Directory.GetCurrentDirectory()));
        Assert.True(module.TryBuild(null, out _));
        Assert.Equal("", module.Encoding);
    }

    [Fact]
    public void TryBuild_InvalidDistribution_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--distribution", "invalid_dist" }, out _));
        });
        Assert.Contains("Error: Invalid distribution 'invalid_dist'. Supported values are proportional, gaussian, exponential.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_FoldersOutOfRange_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--folders", "0" }, out _));
        });
        Assert.Contains("Error: Number of folders must be between 1 and 100.", error, StringComparison.Ordinal);

        error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--folders", "101" }, out _));
        });
        Assert.Contains("Error: Number of folders must be between 1 and 100.", error, StringComparison.Ordinal);
    }

    // --- TryBuild config assembly ---

    [Fact]
    public void TryBuild_SingleType_MatchesRequestBuilderAssembly()
    {
        var cwd = Directory.GetCurrentDirectory();
        var apply = new string?[]
        {
            "--type", "PDF",
            "--count", "100",
            "--output-path", cwd,
            "--folders", "3",
            "--with-text", null,
            "--include-load-file", null,
            "--target-zip-size", "500MB",
            "--distribution", "gaussian",
            "--encoding", "UTF-16",
        };

        Assert.True(TryBuild(apply, out var config));

        var parsed = RequestBuilderTestHelper.Parse(new[]
        {
            "--type", "PDF",
            "--count", "100",
            "--output-path", cwd,
            "--folders", "3",
            "--with-text",
            "--include-load-file",
            "--target-zip-size", "500MB",
            "--distribution", "gaussian",
            "--encoding", "UTF-16",
        });
        Assert.NotNull(parsed.Parsed);
        var request = RequestBuilderTestHelper.Build(modules: parsed.Modules);
        Assert.NotNull(request);

        Assert.Equal(request!.Output.OutputPath, config.OutputPath);
        Assert.Equal(request.Output.FileCount, config.FileCount);
        Assert.Equal(request.Output.FileType, config.FileType);
        Assert.Equal(request.Output.FileTypeRatios, config.FileTypeRatios);
        Assert.Equal(request.Output.FileTypePlan, config.FileTypePlan);
        Assert.Equal(request.Output.SourceFileTypes, config.SourceFileTypes);
        Assert.Equal(request.Output.Folders, config.Folders);
        Assert.Equal(request.Output.Concurrency, config.Concurrency);
        Assert.Equal(request.Output.WithText, config.WithText);
        Assert.Equal(request.Output.TargetZipSize, config.TargetZipSize);
        Assert.Equal(request.Output.IncludeLoadFile, config.IncludeLoadFile);
    }

    [Fact]
    public void TryBuild_SingleTypeDefaults_MatchRequestBuilder()
    {
        var cwd = Directory.GetCurrentDirectory();
        Assert.True(TryBuild(new[] { "--type", "pdf", "--count", "100", "--output-path", cwd }, out var config));

        Assert.Equal(100, config.FileCount);
        Assert.Equal("pdf", config.FileType);
        Assert.Equal(1, config.Folders);
        Assert.False(config.WithText);
        Assert.False(config.IncludeLoadFile);
        Assert.Null(config.TargetZipSize);
        Assert.Null(config.FileTypeRatios);
        Assert.Null(config.FileTypePlan);
        Assert.Null(config.SourceFileTypes);
        Assert.Equal("UTF-8", new OutputModule().Encoding);
        Assert.Equal("proportional", new OutputModule().Distribution);
        Assert.Equal(PerformanceConstants.DefaultConcurrency, config.Concurrency);
    }

    [Fact]
    public void TryBuild_WithText_LowercasesType()
    {
        Assert.True(TryBuild(new[] { "--type", "PDF", "--with-text", null, "--count", "10", "--output-path", Directory.GetCurrentDirectory() }, out var config));
        Assert.Equal("pdf", config.FileType);
        Assert.True(config.WithText);
    }

    [Fact]
    public void TryBuild_SingleRatioMix_BehavesAsSingleType()
    {
        Assert.True(TryBuild(new[] { "--types", "eml:1", "--count", "10", "--output-path", Directory.GetCurrentDirectory() }, out var config));

        Assert.Equal("eml", config.FileType);
        Assert.False(config.IsMixedFileTypes);
        Assert.Null(config.FileTypePlan);
        Assert.Null(config.FileTypeRatios);
    }

    [Fact]
    public void TryBuild_MultiRatioMix_CreatesFileTypePlan()
    {
        Assert.True(TryBuild(new[] { "--types", "pdf:50,eml:30,tiff:20", "--count", "10", "--output-path", Directory.GetCurrentDirectory() }, out var config));

        Assert.True(config.IsMixedFileTypes);
        Assert.NotNull(config.FileTypePlan);
        Assert.Equal("pdf", config.FileType);
        Assert.Equal(5, config.FileTypePlan!.GetTypeCount("pdf"));
        Assert.Equal(3, config.FileTypePlan.GetTypeCount("eml"));
        Assert.Equal(2, config.FileTypePlan.GetTypeCount("tiff"));
        Assert.NotNull(config.FileTypeRatios);
        Assert.Equal(3, config.FileTypeRatios!.Count);
    }

    [Fact]
    public void TryBuild_SourceDriven_ComputesFromRecords()
    {
        var cwd = Directory.GetCurrentDirectory();
        IReadOnlyList<SourceRecord> records = new List<SourceRecord>
        {
            new() { RelativePath = "b.eml", FileType = "eml" },
            new() { RelativePath = "a.pdf", FileType = "pdf" },
            new() { RelativePath = "c.tiff", FileType = "tiff" },
        };

        var module = new OutputModule();
        Assert.True(module.TryApply("--output-path", cwd));
        Assert.True(module.TryBuild(records, out var config));

        Assert.Equal(3, config.FileCount);
        Assert.Equal("eml", config.FileType);
        Assert.Equal(new[] { "eml", "pdf", "tiff" }, config.SourceFileTypes);
        Assert.Null(config.FileTypeRatios);
        Assert.Null(config.FileTypePlan);
    }
}
