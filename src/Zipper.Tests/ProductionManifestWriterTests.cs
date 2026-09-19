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

    [Fact]
    public async Task WriteAsync_EmitsDerivedAzureMetadataBlock()
    {
        // Arrange
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var request = new FileGenerationRequest();

        try
        {
            // Act
            var path = await ProductionManifestWriter.WriteAsync(
                tempDir,
                request,
                "PROD00000001",
                "PROD00000010",
                2,
                TimeSpan.Zero);

            // Assert
            var jsonContent = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(jsonContent);
            var metadata = doc.RootElement.GetProperty("metadata");

            Assert.Equal(4, metadata.EnumerateObject().Count());
            Assert.Equal(Path.GetFileName(tempDir), metadata.GetProperty("production_id").GetString());
            Assert.Equal("PROD00000001", metadata.GetProperty("bates_number_start").GetString());
            Assert.Equal("PROD00000010", metadata.GetProperty("bates_number_end").GetString());
            Assert.Equal("2", metadata.GetProperty("volume_count").GetString());
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
    public async Task WriteAsync_WithNonAsciiAndCrlfValues_NormalizesMetadataToAzureSafe()
    {
        // Arrange: production folder name and Bates range carry non-ASCII and
        // CR/LF characters; the emitted metadata block must be Azure-safe.
        var productionName = $"meta_{Path.GetRandomFileName()}_café";
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), productionName);
        Directory.CreateDirectory(tempDir);

        var request = new FileGenerationRequest();

        try
        {
            // Act
            var path = await ProductionManifestWriter.WriteAsync(
                tempDir,
                request,
                "PRÉF\r\n00000001",
                "PROD00000010",
                1,
                TimeSpan.Zero);

            // Assert
            var jsonContent = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(jsonContent);
            var metadata = doc.RootElement.GetProperty("metadata");

            foreach (var pair in metadata.EnumerateObject())
            {
                var value = pair.Value.GetString() ?? string.Empty;
                Assert.True(value.All(c => c <= '\u007f'), $"Metadata value for '{pair.Name}' is not ASCII-only: '{value}'");
                Assert.DoesNotContain("\r", value, StringComparison.Ordinal);
                Assert.DoesNotContain("\n", value, StringComparison.Ordinal);
            }

            Assert.EndsWith("caf_", metadata.GetProperty("production_id").GetString(), StringComparison.Ordinal);
            Assert.Equal("PR_F  00000001", metadata.GetProperty("bates_number_start").GetString());
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
