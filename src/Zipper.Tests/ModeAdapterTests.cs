using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class StandardModeTests
{
    [Fact]
    public async Task RunAsync_WithValidRequest_LogsConfigAndResult()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var mode = new StandardMode((req, ct) => Task.FromResult(new FileGenerationResult
            {
                GenerationTime = TimeSpan.FromSeconds(5),
                FilesPerSecond = 100.0,
                ZipFilePath = "/out/test.zip",
                ActualZipSize = 1024,
                ZipSizeVerification = new ZipSizeVerificationResult(true, 0.0)
            }));

            var request = new FileGenerationRequest
            {
                Output = new Config.OutputConfig
                {
                    FileType = "pdf",
                    FileCount = 5,
                    OutputPath = "/out",
                    Folders = 1
                },
                LoadFile = new Config.LoadFileConfig
                {
                    Encoding = "utf-8",
                    Distribution = DistributionType.Proportional
                }
            };

            await mode.RunAsync(request, CancellationToken.None);

            var consoleOutput = output.ToString();
            Assert.True(consoleOutput.Contains("Starting parallel file generation...", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Generation complete in 5.0 seconds.", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Archive created: /out/test.zip", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Performance: 100.0 files/second", StringComparison.Ordinal));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task RunAsync_WithChaosMode_ThrowsInvalidOperationException()
    {
        var mode = new StandardMode((req, ct) => Task.FromResult(new FileGenerationResult()));
        var request = new FileGenerationRequest
        {
            Chaos = new Config.ChaosConfig
            {
                ChaosMode = true
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => mode.RunAsync(request, CancellationToken.None));
    }

    [Fact]
    public void Constructor_WithNullGenerate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new StandardMode(null!));
    }
}

[Collection("ConsoleTests")]
public class LoadFileOnlyModeTests
{
    [Fact]
    public async Task RunAsync_SingleConcordanceFormat_WhenPathLacksExtension_DoesNotThrow()
    {
        var mode = new LoadFileOnlyMode((_, _) => Task.FromResult(new LoadFileOnlyResult
        {
            LoadFilePath = "x",
            PropertiesFilePath = "loadfile_properties.json",
            TotalRecords = 1,
        }));
        var request = new FileGenerationRequest
        {
            LoadFile = new Config.LoadFileConfig
            {
                Formats = [LoadFileFormat.Concordance],
                Encoding = "UTF-8",
            },
            Output = new Config.OutputConfig
            {
                FileCount = 1,
                OutputPath = Path.GetTempPath(),
            },
            Delimiters = new Config.DelimiterConfig { EndOfLine = "CRLF" },
        };

        await mode.RunAsync(request);
    }

    [Theory]
    [InlineData(LoadFileFormat.Csv, "COLUMN_A,COLUMN_B\r\nvalue\r\n")]
    [InlineData(LoadFileFormat.Concordance, "\u00feCOLUMN_A\u00fe\u0014\u00feCOLUMN_B\u00fe\r\n\u00fevalue\u00fe\r\n")]
    public async Task RunAsync_MultiFormat_ValidatesEachDelimitedFileFromItsOwnPath(
        LoadFileFormat format,
        string invalidLoadFile)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"zipper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var xmlPath = Path.Combine(tempDir, "loadfile.xml");
            await File.WriteAllTextAsync(xmlPath, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Root/>\r\n");
            string invalidPath = Path.Combine(tempDir, format == LoadFileFormat.Csv ? "loadfile.csv" : "loadfile.concordance.dat");
            await File.WriteAllTextAsync(invalidPath, invalidLoadFile);

            var mode = new LoadFileOnlyMode((_, _) => Task.FromResult(new LoadFileOnlyResult
            {
                LoadFilePath = xmlPath,
                PropertiesFilePath = Path.Combine(tempDir, "loadfile_properties.json"),
                TotalRecords = 1,
            }));
            var request = new FileGenerationRequest
            {
                LoadFile = new Config.LoadFileConfig
                {
                    Formats = [LoadFileFormat.EdrmXml, format],
                    Encoding = "UTF-8",
                },
                Output = new Config.OutputConfig
                {
                    FileCount = 1,
                    OutputPath = tempDir,
                },
                Delimiters = new Config.DelimiterConfig { EndOfLine = "CRLF" },
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mode.RunAsync(request));

            Assert.Equal("Post-generation validation failed.", exception.Message);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithValidRequest_LogsResult()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var mode = new LoadFileOnlyMode((req, ct) => Task.FromResult(new LoadFileOnlyResult
            {
                LoadFilePath = "/out/test.dat",
                PropertiesFilePath = "/out/test.json",
                TotalRecords = 42,
                GenerationTime = TimeSpan.FromSeconds(2.5)
            }));

            var request = new FileGenerationRequest
            {
                LoadFile = new Config.LoadFileConfig
                {
                    Formats = new List<LoadFileFormat> { LoadFileFormat.Dat },
                    Encoding = "utf-8"
                },
                Output = new Config.OutputConfig
                {
                    FileCount = 42,
                    OutputPath = "/out"
                },
                Delimiters = new Config.DelimiterConfig
                {
                    EndOfLine = "\r\n"
                }
            };

            await mode.RunAsync(request, CancellationToken.None);

            var consoleOutput = output.ToString();
            Assert.True(consoleOutput.Contains("Starting loadfile-only generation...", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Generation complete in 2.5 seconds.", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Load file: /out/test.dat", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Properties: /out/test.json", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Records: 42", StringComparison.Ordinal));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Constructor_WithNullGenerate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LoadFileOnlyMode(null!));
    }
}

[Collection("ConsoleTests")]
public class ProductionSetModeTests
{
    [Fact]
    public async Task RunAsync_WithValidRequest_LogsProductionDetails()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var mode = new ProductionSetMode((req, ct) => Task.FromResult(new ProductionSetResult
            {
                ProductionPath = "/out/prod",
                ZipFilePath = "/out/prod.zip",
                DatFilePath = "/out/prod/prod.dat",
                OptFilePath = "/out/prod/prod.opt",
                ManifestPath = "/out/prod/manifest.txt",
                TotalDocuments = 3,
                BatesRange = "TST00000001-TST00000003",
                VolumeCount = 1,
                GenerationTime = TimeSpan.FromSeconds(3.5)
            }));

            var request = new FileGenerationRequest
            {
                Output = new Config.OutputConfig
                {
                    FileType = "pdf",
                    FileCount = 3,
                    OutputPath = "/out"
                },
                Production = new Config.ProductionConfig
                {
                    VolumeSize = 1000,
                    ProductionZip = true
                },
                Bates = new BatesNumberConfig { Prefix = "TST", Start = 1, Digits = 8 }
            };

            await mode.RunAsync(request, CancellationToken.None);

            var consoleOutput = output.ToString();
            Assert.True(consoleOutput.Contains("Starting production set generation...", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Production set complete in 3.5 seconds.", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Production: /out/prod", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Documents: 3", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Bates Range: TST00000001-TST00000003", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Volumes: 1", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("DAT: /out/prod/prod.dat", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("OPT: /out/prod/prod.opt", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("Manifest: /out/prod/manifest.txt", StringComparison.Ordinal));
            Assert.True(consoleOutput.Contains("ZIP: /out/prod.zip", StringComparison.Ordinal));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Constructor_WithNullGenerate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ProductionSetMode(null!));
    }
}
