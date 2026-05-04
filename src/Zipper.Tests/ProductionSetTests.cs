using System.Text.Json;
using Xunit;

using Zipper.Config;
namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ProductionSetTests : IDisposable
{
    private readonly string testOutputPath;

    public ProductionSetTests()
    {
        this.testOutputPath = Path.Combine(Path.GetTempPath(), $"zipper_prod_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.testOutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.testOutputPath))
        {
            Directory.Delete(this.testOutputPath, true);
        }
    }

    // === CLI Validation Tests ===
    [Fact]
    public void ProductionSet_RequiresBatesPrefix()
    {
        var args = new[] { "--production-set", "--count", "10", "--output-path", this.testOutputPath };
        var request = CommandLineValidator.ValidateAndParseArguments(args);
        Assert.Null(request);
    }

    [Fact]
    public void ProductionSet_ConflictsWithLoadfileOnly()
    {
        var args = new[] { "--production-set", "--loadfile-only", "--count", "10", "--output-path", this.testOutputPath, "--bates-prefix", "TEST" };
        var request = CommandLineValidator.ValidateAndParseArguments(args);
        Assert.Null(request);
    }

    [Fact]
    public void ProductionZip_RequiresProductionSet()
    {
        var args = new[] { "--production-zip", "--count", "10", "--output-path", this.testOutputPath, "--type", "pdf", "--bates-prefix", "TEST" };
        var request = CommandLineValidator.ValidateAndParseArguments(args);
        Assert.Null(request);
    }

    [Fact]
    public void VolumeSize_RequiresProductionSet()
    {
        var args = new[] { "--volume-size", "100", "--count", "10", "--output-path", this.testOutputPath, "--type", "pdf", "--bates-prefix", "TEST" };
        var request = CommandLineValidator.ValidateAndParseArguments(args);
        Assert.Null(request);
    }

    [Fact]
    public void ProductionSet_ValidArgs_ParsesCorrectly()
    {
        var args = new[]
        {
            "--production-set", "--count", "50", "--output-path", this.testOutputPath,
            "--bates-prefix", "PROD", "--bates-start", "1", "--bates-digits", "6",
            "--volume-size", "25", "--type", "pdf",
        };
        var request = CommandLineValidator.ValidateAndParseArguments(args);

        Assert.NotNull(request);
        Assert.True(request.Production.ProductionSet);
        Assert.Equal(50L, request.Output.FileCount);
        Assert.Equal("PROD", request.Bates!.Prefix);
        Assert.Equal(6, request.Bates.Digits);
        Assert.Equal(25, request.Production.VolumeSize);
    }

    [Fact]
    public void ProductionSet_DefaultVolumeSize_Is5000()
    {
        var args = new[]
        {
            "--production-set", "--count", "10", "--output-path", this.testOutputPath,
            "--bates-prefix", "TEST", "--type", "pdf",
        };
        var request = CommandLineValidator.ValidateAndParseArguments(args);

        Assert.NotNull(request);
        Assert.Equal(5000, request.Production.VolumeSize);
    }

    [Fact]
    public void ProductionSet_DefaultFileType_IsPdf()
    {
        var args = new[]
        {
            "--production-set", "--count", "10", "--output-path", this.testOutputPath,
            "--bates-prefix", "TEST",
        };
        var request = CommandLineValidator.ValidateAndParseArguments(args);

        Assert.NotNull(request);
        Assert.Equal("pdf", request.Output.FileType);
    }

    // === End-to-End Generation Tests ===
    [Fact]
    public async Task ProductionSet_GeneratesDirectoryStructure()
    {
        var request = this.CreateTestRequest(count: 5);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Verify directory structure
        Assert.True(Directory.Exists(Path.Combine(result.ProductionPath, "DATA")));
        Assert.True(Directory.Exists(Path.Combine(result.ProductionPath, "NATIVES")));
        Assert.True(Directory.Exists(Path.Combine(result.ProductionPath, "TEXT")));
        Assert.True(Directory.Exists(Path.Combine(result.ProductionPath, "IMAGES")));
    }

    [Fact]
    public async Task ProductionSet_CreatesCorrectFileCount()
    {
        var request = this.CreateTestRequest(count: 10);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.*", SearchOption.AllDirectories);
        var textFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "TEXT"), "*.*", SearchOption.AllDirectories);
        var imageFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "IMAGES"), "*.*", SearchOption.AllDirectories);

        Assert.Equal(10, nativeFiles.Length);
        Assert.Equal(10, textFiles.Length);
        Assert.Equal(10, imageFiles.Length);
    }

    [Fact]
    public async Task ProductionSet_BatesNumberingIsCorrect()
    {
        var request = this.CreateTestRequest(count: 3, batesPrefix: "DOC", batesStart: 1, batesDigits: 6);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.*", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(f => f)
            .ToArray();

        Assert.Equal("DOC000001", nativeFiles[0]);
        Assert.Equal("DOC000002", nativeFiles[1]);
        Assert.Equal("DOC000003", nativeFiles[2]);
    }

    [Fact]
    public async Task ProductionSet_VolumeSubfoldersCreated()
    {
        var request = this.CreateTestRequest(count: 10, volumeSize: 3);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        // 10 docs / 3 per volume = 4 volumes
        Assert.Equal(4, result.VolumeCount);

        var volDirs = Directory.GetDirectories(Path.Combine(result.ProductionPath, "NATIVES"));
        Assert.Equal(4, volDirs.Length);
        Assert.Contains(volDirs, d => Path.GetFileName(d) == "VOL001");
        Assert.Contains(volDirs, d => Path.GetFileName(d) == "VOL004");
    }

    [Fact]
    public async Task ProductionSet_DatLoadFileIsValid()
    {
        var request = this.CreateTestRequest(count: 5);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var datContent = await File.ReadAllLinesAsync(result.DatFilePath);

        // Header + 5 data rows
        Assert.Equal(6, datContent.Length);

        // Header should contain expected columns
        var header = datContent[0];
        Assert.Contains("DOCID", header);
        Assert.Contains("BATES_NUMBER", header);
        Assert.Contains("NATIVE_PATH", header);
        Assert.Contains("IMAGE_PATH", header);
    }

    [Fact]
    public async Task ProductionSet_OptLoadFileIsValid()
    {
        var request = this.CreateTestRequest(count: 5);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var optContent = await File.ReadAllLinesAsync(result.OptFilePath);

        // 5 data rows (no header in OPT)
        Assert.Equal(5, optContent.Length);

        // Each line should contain the Bates number
        Assert.StartsWith("TEST", optContent[0]);
        Assert.Contains(".tif", optContent[0]);
    }

    [Fact]
    public async Task ProductionSet_ManifestJsonIsValid()
    {
        var request = this.CreateTestRequest(count: 8, volumeSize: 5);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var json = await File.ReadAllTextAsync(result.ManifestPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(8, manifest.GetProperty("documentCount").GetInt64());
        Assert.Equal(2, manifest.GetProperty("volumeCount").GetInt32());
        Assert.Equal(5, manifest.GetProperty("volumeSize").GetInt32());
        Assert.Equal("TEST", manifest.GetProperty("batesRange").GetProperty("prefix").GetString());
        Assert.Equal("DATA", manifest.GetProperty("directories").GetProperty("data").GetString());
    }

    [Fact]
    public async Task ProductionSet_WithZip_CreatesArchive()
    {
        var request = this.CreateTestRequest(count: 3);
        request.Production = request.Production with { ProductionZip = true };
        var result = await ProductionSetGenerator.GenerateAsync(request);

        Assert.NotNull(result.ZipFilePath);
        Assert.True(File.Exists(result.ZipFilePath));
        Assert.True(new FileInfo(result.ZipFilePath).Length > 0);
    }

    [Fact]
    public async Task ProductionSet_WithoutZip_NoArchive()
    {
        var request = this.CreateTestRequest(count: 3);
        request.Production = request.Production with { ProductionZip = false };
        var result = await ProductionSetGenerator.GenerateAsync(request);

        Assert.Null(result.ZipFilePath);
    }

    [Fact]
    public async Task ProductionSet_WithSeed_IsReproducible()
    {
        var request1 = this.CreateTestRequest(count: 5, seed: 42);
        var result1 = await ProductionSetGenerator.GenerateAsync(request1);
        var dat1 = await File.ReadAllTextAsync(result1.DatFilePath);

        var request2 = this.CreateTestRequest(count: 5, seed: 42);
        var result2 = await ProductionSetGenerator.GenerateAsync(request2);
        var dat2 = await File.ReadAllTextAsync(result2.DatFilePath);

        Assert.Equal(dat1, dat2);
    }

    // === Integration Test via CLI ===
    [Fact]
    public async Task ProductionSet_FullCLI_EndToEnd()
    {
        var args = new[]
        {
            "--production-set", "--count", "5", "--output-path", this.testOutputPath,
            "--bates-prefix", "E2E", "--type", "pdf", "--seed", "99",
        };

        var originalOut = Console.Out;
        var originalError = Console.Error;
        int exitCode;
        try
        {
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            exitCode = await Program.Main(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(0, exitCode);

        // Find the production directory
        var prodDirs = Directory.GetDirectories(this.testOutputPath, "PRODUCTION_*");
        Assert.Single(prodDirs);

        // Verify structure
        Assert.True(Directory.Exists(Path.Combine(prodDirs[0], "DATA")));
        Assert.True(Directory.Exists(Path.Combine(prodDirs[0], "NATIVES")));
        Assert.True(File.Exists(Path.Combine(prodDirs[0], "DATA", "loadfile.dat")));
        Assert.True(File.Exists(Path.Combine(prodDirs[0], "DATA", "loadfile.opt")));
        Assert.True(File.Exists(Path.Combine(prodDirs[0], "_manifest.json")));
    }

    // === C3: EML files in production sets (regression) ===
    [Fact]
    public async Task ProductionSet_WithEml_CreatesNonZeroNativeFiles()
    {
        // C3 regression: EML files in production sets were zero-byte because
        // PlaceholderFiles.GetContent("eml") returns Array.Empty<byte>() and
        // no EML-specific generation branch existed.
        var request = this.CreateTestRequest(count: 5, fileType: "eml");
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.eml", SearchOption.AllDirectories);
        Assert.Equal(5, nativeFiles.Length);

        // Each native file should have non-zero content
        foreach (var file in nativeFiles)
        {
            var info = new FileInfo(file);
            Assert.True(info.Length > 0, $"EML native file {file} is zero bytes");
        }
    }

    [Fact]
    public async Task ProductionSet_WithEml_ContainsValidEmailHeaders()
    {
        // Verify EML content in production sets has valid email headers
        var request = this.CreateTestRequest(count: 3, fileType: "eml");
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.eml", SearchOption.AllDirectories);

        foreach (var file in nativeFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            Assert.Contains("From:", content);
            Assert.Contains("To:", content);
            Assert.Contains("Subject:", content);
            Assert.Contains("Date:", content);
        }
    }

    private FileGenerationRequest CreateTestRequest(
        int count = 10,
        string batesPrefix = "TEST",
        long batesStart = 1,
        int batesDigits = 8,
        int volumeSize = 5000,
        int? seed = null,
        string fileType = "pdf")
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.testOutputPath,
                FileCount = count,
                FileType = fileType,
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = volumeSize,
            },
            Metadata = new MetadataConfig { Seed = seed },
            Bates = new BatesNumberConfig,
            Prefix = batesPrefix,
            Start = batesStart,
            Digits = batesDigits,
        };
    }

    [Fact]
    public async Task ProductionSet_FileCountOne_CreatesSingleFile()
    {
        var request = this.CreateTestRequest(count: 1);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        Assert.Equal(1, result.TotalDocuments);
        Assert.Equal(1, result.VolumeCount);

        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.*", SearchOption.AllDirectories);
        Assert.Single(nativeFiles);
    }

    [Fact]
    public async Task ProductionSet_VolumeSizeOne_EveryFileInOwnVolume()
    {
        var request = this.CreateTestRequest(count: 5, volumeSize: 1);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        Assert.Equal(5, result.VolumeCount);

        var volDirs = Directory.GetDirectories(Path.Combine(result.ProductionPath, "NATIVES"));
        Assert.Equal(5, volDirs.Length);
    }

    [Fact]
    public async Task ProductionSet_VolumeSizeExceedsCount_SingleVolume()
    {
        var request = this.CreateTestRequest(count: 5, volumeSize: 100);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        Assert.Equal(1, result.VolumeCount);

        var volDirs = Directory.GetDirectories(Path.Combine(result.ProductionPath, "NATIVES"));
        Assert.Single(volDirs);
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("xlsx")]
    public async Task ProductionSet_WithOfficeTypes_CreatesNativeFiles(string fileType)
    {
        var request = this.CreateTestRequest(count: 3, fileType: fileType);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), $"*.{fileType}", SearchOption.AllDirectories);
        Assert.Equal(3, nativeFiles.Length);

        foreach (var file in nativeFiles)
        {
            var info = new FileInfo(file);
            Assert.True(info.Length > 0);
        }
    }

    [Fact]
    public async Task ProductionSet_WithTiff_CreatesNativeFiles()
    {
        var request = this.CreateTestRequest(count: 3, fileType: "tiff");
        request.Tiff = request.Tiff with { PageRange = (1, 5) };
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.tiff", SearchOption.AllDirectories);
        Assert.Equal(3, nativeFiles.Length);

        foreach (var file in nativeFiles)
        {
            var info = new FileInfo(file);
            Assert.True(info.Length > 0);
        }
    }

    [Fact]
    public async Task ProductionSet_WithCustomEncoding_ProducesValidDat()
    {
        var request = this.CreateTestRequest(count: 3);
        request.LoadFile = request.LoadFile with { Encoding = "UTF-16" };
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var datContent = await File.ReadAllTextAsync(result.DatFilePath, System.Text.LoadFile.Encoding.Unicode);
        Assert.Contains("DOCID", datContent);
        Assert.Equal(4, datContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
