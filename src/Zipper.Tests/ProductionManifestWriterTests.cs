using System.Text.Json;
using Xunit;
using Zipper.Config;
using Zipper.Profiles;

namespace Zipper.Tests;

public class ProductionManifestWriterTests
{
    [Fact]
    public async Task WriteAsync_GeneratesManifestWithExpectedStructure()
    {
        // Arrange
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var request = new FileGenerationRequest();
        request.Output = request.Output with
        {
            FileCount = 120,
            FileType = "eml"
        };
        request.Production = request.Production with
        {
            VolumeSize = 50
        };
        request.Bates = new BatesNumberConfig
        {
            Prefix = "PROD",
            Digits = 8
        };
        request.LoadFile = request.LoadFile with
        {
            Encoding = "UTF-8"
        };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "|",
            QuoteDelimiter = "\""
        };
        request.Metadata = request.Metadata with
        {
            ColumnProfile = new ColumnProfile { Name = "StandardProfile" },
            Seed = 42
        };

        var batesStart = "PROD00000001";
        var batesEnd = "PROD00000120";
        var volumeCount = 3;
        var generationTime = TimeSpan.FromMilliseconds(4500); // Should format to 4.5s

        try
        {
            // Act
            var path = await ProductionManifestWriter.WriteAsync(
                tempDir,
                request,
                batesStart,
                batesEnd,
                volumeCount,
                generationTime);

            // Assert
            Assert.True(File.Exists(path));
            var jsonContent = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Date validation (should not be empty)
            Assert.False(string.IsNullOrEmpty(root.GetProperty("productionDate").GetString()));

            // Bates Range
            var batesRange = root.GetProperty("batesRange");
            Assert.Equal(batesStart, batesRange.GetProperty("start").GetString());
            Assert.Equal(batesEnd, batesRange.GetProperty("end").GetString());
            Assert.Equal("PROD", batesRange.GetProperty("prefix").GetString());
            Assert.Equal(8, batesRange.GetProperty("digits").GetInt32());

            // Counts and Volume
            Assert.Equal(120, root.GetProperty("nativeFileCount").GetInt64());
            Assert.Equal("eml", root.GetProperty("fileType").GetString());
            Assert.Equal(3, root.GetProperty("volumeCount").GetInt32());
            Assert.Equal(50, root.GetProperty("volumeSize").GetInt32());
            Assert.Equal("4.5s", root.GetProperty("generationTime").GetString());

            // Directories
            var dirs = root.GetProperty("directories");
            Assert.Equal("DATA", dirs.GetProperty("data").GetString());
            Assert.Equal("NATIVES", dirs.GetProperty("natives").GetString());
            Assert.Equal("TEXT", dirs.GetProperty("text").GetString());
            Assert.Equal("IMAGES", dirs.GetProperty("images").GetString());

            // Load files
            var loadFiles = root.GetProperty("loadFiles");
            Assert.Equal("DATA/loadfile.dat", loadFiles.GetProperty("dat").GetString());
            Assert.Equal("DATA/loadfile.opt", loadFiles.GetProperty("opt").GetString());

            // Settings
            var settings = root.GetProperty("settings");
            Assert.Equal("UTF-8", settings.GetProperty("encoding").GetString());
            Assert.Equal("char:|", settings.GetProperty("columnDelimiter").GetString());
            Assert.Equal("char:\"", settings.GetProperty("quoteDelimiter").GetString());
            Assert.Equal("StandardProfile", settings.GetProperty("columnProfile").GetString());
            Assert.Equal(42, settings.GetProperty("seed").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_WithEmptyDelimiters_FormatsCorrectly()
    {
        // Arrange
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var request = new FileGenerationRequest();
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = string.Empty,
            QuoteDelimiter = null!
        };

        try
        {
            // Act
            var path = await ProductionManifestWriter.WriteAsync(
                tempDir,
                request,
                "B1",
                "B2",
                1,
                TimeSpan.Zero);

            // Assert
            var jsonContent = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            var settings = root.GetProperty("settings");

            Assert.Equal(string.Empty, settings.GetProperty("columnDelimiter").GetString());
            Assert.Equal(string.Empty, settings.GetProperty("quoteDelimiter").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_WithNonPrintableDelimiters_FormatsCorrectly()
    {
        // Arrange
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var request = new FileGenerationRequest();
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "\u0014",
            QuoteDelimiter = "\u0011"
        };

        try
        {
            // Act
            var path = await ProductionManifestWriter.WriteAsync(
                tempDir,
                request,
                "B1",
                "B2",
                1,
                TimeSpan.Zero);

            // Assert
            var jsonContent = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            var settings = root.GetProperty("settings");

            Assert.Equal("ascii:20", settings.GetProperty("columnDelimiter").GetString());
            Assert.Equal("ascii:17", settings.GetProperty("quoteDelimiter").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
