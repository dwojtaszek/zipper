using Xunit;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class PipelineTests
{
    [Fact]
    public void Build_StandardMode_SetsAllDefaults()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory() });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);

        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
        Assert.Equal(100, result!.Output.FileCount);
        Assert.Equal("pdf", result!.Output.FileType);
        Assert.Equal(1, result!.Output.Folders);
        Assert.Equal(DistributionType.Proportional, result!.LoadFile.Distribution);
        Assert.False(result!.Metadata.WithMetadata);
        Assert.False(result!.Output.WithText);
        Assert.False(result!.Output.IncludeLoadFile);
        Assert.Equal(0, result!.LoadFile.AttachmentRate);
        Assert.Null(result!.Bates);
    }

    [Fact]
    public void Build_WithValidPath_ResolvesDirectory()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory() });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
    }

    [Fact]
    public void Build_LoadfileOnly_SetsProperties()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only", "--load-file-format", "opt" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.True(result!.LoadfileOnly);
        Assert.Single(result!.LoadFile.Formats);
        Assert.Equal(LoadFileFormat.Opt, result!.LoadFile.Formats[0]);
    }

    [Fact]
    public void Build_ProductionSet_SetsVolumeSize()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--production-set", "--bates-prefix", "PREFIX", "--volume-size", "1000" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.True(result!.Production.ProductionSet);
        Assert.Equal(1000, result!.Production.VolumeSize);
    }

    [Fact]
    public void Build_BatesConfig_SetsCorrectly()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result!.Bates);
        Assert.Equal("CL001", result!.Bates.Prefix);
        Assert.Equal(100, result!.Bates.Start);
        Assert.Equal(6, result!.Bates.Digits);
    }

    [Fact]
    public void Build_ColumnProfile_LoadsProfile()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "standard" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result!.Metadata.ColumnProfile);
    }

    [Fact]
    public void Build_MultiFormat_CreatesFormatList()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--load-file-formats", "dat,opt,csv" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.Equal(3, result!.LoadFile.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Opt, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Csv, result!.LoadFile.Formats);
    }

    [Fact]
    public void Build_LoadfileOnlyEncoding_UsesExtendedSet()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only", "--encoding", "WINDOWS-1252" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.Equal("WINDOWS-1252", result!.LoadFile.Encoding);
    }

    [Fact]
    public void Build_Encoding_PreservesNormalizedInputName()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--encoding", "UTF-16" });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.Equal("UTF-16", result!.LoadFile.Encoding);
    }

    // --- REQ-106: relative output paths resolve against CWD, traversal outside CWD rejected ---

    [Fact]
    public void Build_OutputPathWithParentTraversal_RejectsPathOutsideCwd()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "../escape" });
        Assert.True(ok);
        Assert.Null(PipelineTestHelper.Build(modules: modules));
    }

    [Fact]
    public void Build_OutputPathWithinCwd_IsAccepted()
    {
        var uniqueDirName = "output_" + Guid.NewGuid().ToString("N");
        try
        {
            var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", uniqueDirName });
            Assert.True(ok);
            Assert.NotNull(PipelineTestHelper.Build(modules: modules));
        }
        finally
        {
            if (Directory.Exists(uniqueDirName))
            {
                Directory.Delete(uniqueDirName, recursive: true);
            }
        }
    }

    // --- REQ-164: custom column profile paths resolve against CWD, traversal rejected ---

    [Fact]
    public void Build_ColumnProfileWithParentTraversal_RejectsPathOutsideCwd()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "../outside-profile.json" });
        Assert.True(ok);
        Assert.Null(PipelineTestHelper.Build(modules: modules));
    }

    [Fact]
    public void Build_ColumnProfileWithinCwd_IsAccepted()
    {
        var tempProfilePath = Path.Combine(Directory.GetCurrentDirectory(), "temp_profile_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var validProfileJson = @"{
                ""name"": ""TempProfile"",
                ""columns"": [{ ""name"": ""DocID"", ""type"": ""identifier"" }],
                ""dataSources"": {}
            }";
            File.WriteAllText(tempProfilePath, validProfileJson);

            var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", tempProfilePath });
            Assert.True(ok);
            Assert.NotNull(PipelineTestHelper.Build(modules: modules));
        }
        finally
        {
            if (File.Exists(tempProfilePath))
            {
                File.Delete(tempProfilePath);
            }
        }
    }
}
