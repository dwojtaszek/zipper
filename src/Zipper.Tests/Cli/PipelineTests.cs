using Xunit;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class PipelineTests : WorkingDirectoryTestBase
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

    [Fact]
    public void Build_ImageTypeWithExplicitLoadFileFormats_RespectsExplicitFormats()
    {
        // Tests lines 67-74 in Pipeline.cs: image type × explicit --load-file-format(s)
        // When --load-file-formats is explicitly provided, it should NOT be overridden by image type logic
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "tiff",
            "--count", "10",
            "--output-path", Directory.GetCurrentDirectory(),
            "--load-file-formats", "csv" // Explicitly only CSV, not DAT+OPT
        });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result);
        Assert.Single(result!.LoadFile.Formats);
        Assert.Equal(LoadFileFormat.Csv, result!.LoadFile.Formats[0]);
    }

    [Fact]
    public void Build_ImageTypeWithoutExplicitLoadFileFormats_AddsDatAndOpt()
    {
        // Tests lines 67-74: when image type (tiff/jpg) and NO explicit --load-file-formats,
        // DAT and OPT are automatically added
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "tiff",
            "--count", "10",
            "--output-path", Directory.GetCurrentDirectory()
            // No --load-file-formats
        });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result);
        Assert.Equal(2, result!.LoadFile.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Opt, result!.LoadFile.Formats);
    }

    [Fact]
    public void Build_JpgTypeWithoutExplicitLoadFileFormats_AddsDatAndOpt()
    {
        // Tests lines 67-74: jpg image type without explicit --load-file-formats
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "jpg",
            "--count", "10",
            "--output-path", Directory.GetCurrentDirectory()
        });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result);
        Assert.Equal(2, result!.LoadFile.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Opt, result!.LoadFile.Formats);
    }

    [Fact]
    public void Build_JpgTypeWithExplicitLoadFileFormats_RespectsExplicitFormats()
    {
        // Tests lines 67-74: jpg image type with explicit --load-file-formats
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "jpg",
            "--count", "10",
            "--output-path", Directory.GetCurrentDirectory(),
            "--load-file-formats", "csv"
        });
        Assert.True(ok);
        var result = PipelineTestHelper.Build(modules: modules);
        Assert.NotNull(result);
        Assert.Single(result!.LoadFile.Formats);
        Assert.Equal(LoadFileFormat.Csv, result!.LoadFile.Formats[0]);
    }

    [Fact]
    public void Build_SourceInputWithImageTypeWithoutExplicitFormats_AddsDatAndOpt()
    {
        // Tests lines 67-74: source input with image type (tiff/jpg) without explicit --load-file-formats
        var tempCsv = Path.Combine(Directory.GetCurrentDirectory(), "source_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(tempCsv, "FilePath,FileType\nfolder/doc1.tiff,tiff\n");
            var (ok, modules) = PipelineTestHelper.Parse(new[]
            {
                "--input-csv", tempCsv,
                "--output-path", Directory.GetCurrentDirectory()
            });
            Assert.True(ok);
            var result = PipelineTestHelper.Build(modules: modules);
            Assert.NotNull(result);
            Assert.Equal(2, result!.LoadFile.Formats.Count);
            Assert.Contains(LoadFileFormat.Dat, result!.LoadFile.Formats);
            Assert.Contains(LoadFileFormat.Opt, result!.LoadFile.Formats);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Theory]
    [InlineData(30_000, false)]
    [InlineData(30_001, true)]
    public void Build_SourceInputDiscoRange_WarnsOnlyAboveLimit(int recordCount, bool expectWarning)
    {
        // The range check needs the Source Record count, which exists only after source input loads.
        var tempCsv = Path.Combine(Directory.GetCurrentDirectory(), "source_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var lines = new List<string> { "FilePath,FileType" };
            lines.AddRange(Enumerable.Range(1, recordCount).Select(i => $"doc{i:D5}.pdf,pdf"));
            File.WriteAllLines(tempCsv, lines);
            var (ok, modules) = PipelineTestHelper.Parse(new[]
            {
                "--input-csv", tempCsv,
                "--output-path", Directory.GetCurrentDirectory(),
                "--bates-prefix", "CL001",
            });
            Assert.True(ok);

            FileGenerationRequest? result = null;
            var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

            Assert.NotNull(result);
            Assert.Equal("CL001", result!.Bates!.Prefix);
            Assert.Equal(1, result.Bates.Start);
            if (expectWarning)
            {
                Assert.Contains($"Warning: Bates range 1 through {recordCount} spans", stderr, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("Warning: Bates range", stderr, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Fact]
    public void Build_ProductionSetRollingSetWiderThanLimit_WarnsForThatSet()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "pdf", "--count", "2", "--output-path", Directory.GetCurrentDirectory(),
            "--production-set", "--rolling-count", "2", "--bates-prefix", "CL001", "--bates-start", "1,9999999999999999",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.Contains("Warning: Bates numbers are 17 characters", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TiffPagesPast999WithPageSuffix_WarnsWithFourDigitPageOrdinal()
    {
        // A page range can reach page 1000, which the page suffix prints as _1000 (five characters).
        // The range's minimum is 1, so a build that reads the minimum instead of the maximum prints no warning.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "tiff", "--count", "1", "--output-path", Directory.GetCurrentDirectory(),
            "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix", "--tiff-pages", "1-1000",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.Contains("Warning: Bates numbers are 17 characters including the page suffix", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TiffPagesPast999WithPageSuffixOnDatOnly_PrintsNoPlatformWarningForPlainTiff()
    {
        // DAT keeps document-level Bates numbers, and a plain TIFF has no page endorsement, so no page label is written.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "tiff", "--count", "1", "--output-path", Directory.GetCurrentDirectory(),
            "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix", "--tiff-pages", "1-1000",
            "--loadfile-format", "dat",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.DoesNotContain("Warning: Bates numbers are", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TiffPagesPast999WithPageSuffixOnDatOnlyRealisticTiff_WarnsWithFourDigitPageOrdinal()
    {
        // A realistic TIFF endorses each page with its _NNN label, so a DAT-only run still reaches page 1000.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "tiff", "--count", "1", "--output-path", Directory.GetCurrentDirectory(),
            "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix", "--tiff-pages", "1-1000",
            "--loadfile-format", "dat", "--with-tiff-realism",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.Contains("Warning: Bates numbers are 17 characters including the page suffix", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ProductionSetTiffPastPage999WithDatFormat_WarnsWithFourDigitPageOrdinal()
    {
        // A Production Set always writes an OPT Load File, so its page labels reach output even when DAT is requested.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "tiff", "--count", "1", "--output-path", Directory.GetCurrentDirectory(), "--production-set",
            "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix", "--tiff-pages", "1-1000",
            "--loadfile-format", "dat",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.Contains("Warning: Bates numbers are 17 characters including the page suffix", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TiffPagesPast999OnPdfWithPageSuffix_PrintsNoPlatformWarning()
    {
        // A PDF run numbers one page per document, so its page suffix stays _001 (four characters).
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "pdf", "--count", "1", "--output-path", Directory.GetCurrentDirectory(),
            "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix", "--tiff-pages", "1-1000",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.NotNull(result.Bates);
        Assert.True(result.Bates.PageSuffix);
        Assert.DoesNotContain("Warning: Bates numbers are", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TiffPagesPastMixThatAllocatesNoTiff_PrintsNoPlatformWarning()
    {
        // --types declares TIFF, but a 1:1 mix at --count 1 allocates the only document to PDF, so no TIFF page is numbered.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--types", "pdf:1,tiff:1", "--count", "1", "--output-path", Directory.GetCurrentDirectory(),
            "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix", "--tiff-pages", "1-1000",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.DoesNotContain("Warning: Bates numbers are", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TiffPagesPastMixThatAllocatesTiff_WarnsWithFourDigitPageOrdinal()
    {
        // The same mix at --count 2 allocates one TIFF document, which numbers page 1000 and widens the page suffix.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--types", "pdf:1,tiff:1", "--count", "2", "--output-path", Directory.GetCurrentDirectory(),
            "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix", "--tiff-pages", "1-1000",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.Contains("Warning: Bates numbers are 17 characters including the page suffix", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("tiff")]
    public void Build_TiffPagesPast999InLoadfileOnlyDat_PrintsNoPlatformWarning(string fileType)
    {
        // DAT Bates stay document-level (REQ-239) and Loadfile-Only writes no images, so a DAT-only run has no page-level number to widen.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", fileType, "--count", "1", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only",
            "--load-file-format", "dat", "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix",
            "--tiff-pages", "1000-1000",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.DoesNotContain("Warning: Bates numbers are", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TiffPagesPast999InLoadfileOnlyPdf_WarnsWithFourDigitPageOrdinal()
    {
        // Loadfile-Only OPT rows number pages from --tiff-pages for any output type, so page 1000 prints as _1000.
        var (ok, modules) = PipelineTestHelper.Parse(new[]
        {
            "--type", "pdf", "--count", "1", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only",
            "--load-file-format", "opt", "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix",
            "--tiff-pages", "1000-1000",
        });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.Contains("Warning: Bates numbers are 17 characters including the page suffix", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pdf", false)]
    [InlineData("tiff", true)]
    public void Build_TiffPagesPast999InSourceDrivenLoadfileOnlyOpt_WarnsOnlyForTiffOutput(string fileType, bool expectWarning)
    {
        // Source-driven OPT rows take their pages from --tiff-pages only for TIFF output; a source-driven PDF keeps one page.
        var tempCsv = Path.Combine(Directory.GetCurrentDirectory(), "source_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(tempCsv, $"FilePath,FileType\nfolder/doc.{fileType},{fileType}\n");
            var (ok, modules) = PipelineTestHelper.Parse(new[]
            {
                "--loadfile-only", "--input-csv", tempCsv, "--output-path", Directory.GetCurrentDirectory(),
                "--load-file-format", "opt", "--bates-prefix", "CL001", "--bates-digits", "12", "--bates-page-suffix",
                "--tiff-pages", "1000-1000",
            });
            Assert.True(ok);

            FileGenerationRequest? result = null;
            var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

            Assert.NotNull(result);
            if (expectWarning)
            {
                Assert.Contains("Warning: Bates numbers are 17 characters including the page suffix", stderr, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("Warning: Bates numbers are", stderr, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Fact]
    public void Build_WithoutBatesFlags_PrintsNoPlatformWarning()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--type", "pdf", "--count", "30001", "--output-path", Directory.GetCurrentDirectory() });
        Assert.True(ok);

        FileGenerationRequest? result = null;
        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

        Assert.NotNull(result);
        Assert.Null(result!.Bates);
        Assert.DoesNotContain("Warning: Bates", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OverLimitBatesWithLaterValidationError_PrintsNoPlatformWarning()
    {
        // Platform warnings describe a configuration that builds, so a run that fails a later check prints none.
        var tempCsv = Path.Combine(Directory.GetCurrentDirectory(), "source_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(tempCsv, "FilePath,FileType\nfolder/doc.pdf,pdf\n");
            var (ok, modules) = PipelineTestHelper.Parse(new[]
            {
                "--loadfile-only", "--input-csv", tempCsv, "--count", "1", "--output-path", Directory.GetCurrentDirectory(),
                "--chaos-mode", "--chaos-scenario", "adversarial-values", "--seed", "42",
                "--bates-prefix", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDE",
            });
            Assert.True(ok);

            FileGenerationRequest? result = null;
            var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => result = PipelineTestHelper.Build(modules: modules));

            Assert.Null(result);
            Assert.Contains("requires --with-metadata or a --column-profile", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("Warning: Bates prefix", stderr, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }
}
