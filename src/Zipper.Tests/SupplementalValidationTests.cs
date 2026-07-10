using System.Text.Json;
using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

public class SupplementalValidationTests : IDisposable
{
    private readonly string _tempDir;

    public SupplementalValidationTests()
    {
        _tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string CreateMockManifest(string start, string end, string prefix, int digits)
    {
        var manifestPath = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        var manifest = new
        {
            productionDate = "2026-07-10T00:00:00Z",
            batesRange = new
            {
                start = start,
                end = end,
                prefix = prefix,
                digits = digits
            }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        return manifestPath;
    }

    private string CreateMalformedManifest(string invalidJson)
    {
        var manifestPath = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath, invalidJson);
        return manifestPath;
    }

    [Fact]
    public async Task ValidateAsync_WithSinglePriorManifest_ShouldParseCorrectly()
    {
        // Arrange
        var priorPath = CreateMockManifest("PROD00000001", "PROD00000050", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { priorPath },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act
        var report = await SupplementalValidator.ValidateAsync(request, "PROD00000051", "PROD00000100");

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report.DuplicateRanges);
        Assert.Empty(report.SkippedRanges);
        Assert.Equal("PROD00000051", report.ExpectedNextBates);
        Assert.Equal("PROD00000051", report.ActualStartingBates);
    }

    [Fact]
    public async Task ValidateAsync_WithMultiplePriorManifests_ShouldUnionRanges()
    {
        // Arrange
        var prior1 = CreateMockManifest("PROD00000001", "PROD00000050", "PROD", 8);
        var prior2 = CreateMockManifest("PROD00000051", "PROD00000100", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior1, prior2 },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act
        var report = await SupplementalValidator.ValidateAsync(request, "PROD00000101", "PROD00000150");

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report.DuplicateRanges);
        Assert.Empty(report.SkippedRanges);
        Assert.Equal("PROD00000101", report.ExpectedNextBates);
        Assert.Equal("PROD00000101", report.ActualStartingBates);
    }

    [Fact]
    public async Task ValidateAsync_WithDuplicateBatesRange_ShouldFailAndReportDuplicates()
    {
        // Arrange
        var prior = CreateMockManifest("PROD00000001", "PROD00000050", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await SupplementalValidator.ValidateAsync(request, "PROD00000040", "PROD00000080");
        });

        Assert.Contains("Duplicate", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PROD00000040", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PROD00000050", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_WithSkippedBatesRange_InContinuousMode_ShouldFailAndReportSkipped()
    {
        // Arrange
        var prior = CreateMockManifest("PROD00000001", "PROD00000050", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await SupplementalValidator.ValidateAsync(request, "PROD00000060", "PROD00000100");
        });

        Assert.Contains("Skipped", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PROD00000051", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PROD00000059", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_WithSkippedBatesRange_InAllowMode_ShouldPassAndReportSkipped()
    {
        // Arrange
        var prior = CreateMockManifest("PROD00000001", "PROD00000050", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior },
                SupplementalGapPolicy = "allow"
            }
        };

        // Act
        var report = await SupplementalValidator.ValidateAsync(request, "PROD00000060", "PROD00000100");

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report.DuplicateRanges);
        Assert.Single(report.SkippedRanges);
        Assert.Equal("PROD00000051", report.SkippedRanges[0].Start);
        Assert.Equal("PROD00000059", report.SkippedRanges[0].End);
        Assert.Equal("PROD00000051", report.ExpectedNextBates);
        Assert.Equal("PROD00000060", report.ActualStartingBates);
    }

    [Fact]
    public async Task ValidateAsync_WithMalformedPriorManifest_ShouldFail()
    {
        // Arrange
        var prior = CreateMalformedManifest("{ invalid json");
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await SupplementalValidator.ValidateAsync(request, "PROD00000051", "PROD00000100");
        });

        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_WithIncompatibleBatesPrefix_ShouldFail()
    {
        // Arrange
        var prior = CreateMockManifest("OLD00000001", "OLD00000050", "OLD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await SupplementalValidator.ValidateAsync(request, "PROD00000051", "PROD00000100");
        });

        Assert.Contains("incompatible", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PROD+0000051")]
    [InlineData("PROD 0000051")]
    [InlineData("PROD0000051 ")]
    [InlineData("PROD-0000051")]
    public async Task ValidateAsync_WithInvalidNumericPartFormat_ShouldFail(string invalidStartBates)
    {
        // Arrange
        var prior = CreateMockManifest("PROD00000001", "PROD00000050", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await SupplementalValidator.ValidateAsync(request, invalidStartBates, "PROD00000100");
        });
    }

    [Fact]
    public async Task ValidateAsync_WithMalformedPriorManifest_ShouldReportResolvedPathInException()
    {
        // Arrange
        var priorDir = Path.Combine(_tempDir, $"prior_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(priorDir);
        var manifestPath = Path.Combine(priorDir, "_manifest.json");
        File.WriteAllText(manifestPath, "{ invalid json");

        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { priorDir },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await SupplementalValidator.ValidateAsync(request, "PROD00000051", "PROD00000100");
        });

        Assert.Contains("_manifest.json", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_WithPriorBatesWiderThanDigitsCapacity_ShouldPass()
    {
        // Arrange: prior has BATES number of length 9, but digits configuration is 8
        var prior = CreateMockManifest("PROD100000000", "PROD100000005", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act
        var report = await SupplementalValidator.ValidateAsync(request, "PROD100000006", "PROD100000010");

        // Assert
        Assert.NotNull(report);
        Assert.Equal("PROD100000006", report.ExpectedNextBates);
    }

    [Fact]
    public async Task ValidateAsync_WithGapsInPriorManifests_InRejectMode_ShouldFail()
    {
        // Arrange
        var prior1 = CreateMockManifest("PROD00000001", "PROD00000010", "PROD", 8);
        var prior2 = CreateMockManifest("PROD00000021", "PROD00000030", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior1, prior2 },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await SupplementalValidator.ValidateAsync(request, "PROD00000031", "PROD00000040");
        });

        Assert.Contains("Skipped", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PROD00000011", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PROD00000020", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_WithGapsInPriorManifests_InAllowMode_ShouldPassAndReportGaps()
    {
        // Arrange
        var prior1 = CreateMockManifest("PROD00000001", "PROD00000010", "PROD", 8);
        var prior2 = CreateMockManifest("PROD00000021", "PROD00000030", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior1, prior2 },
                SupplementalGapPolicy = "allow"
            }
        };

        // Act
        var report = await SupplementalValidator.ValidateAsync(request, "PROD00000031", "PROD00000040");

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report.DuplicateRanges);
        Assert.Single(report.SkippedRanges);
        Assert.Equal("PROD00000011", report.SkippedRanges[0].Start);
        Assert.Equal("PROD00000020", report.SkippedRanges[0].End);
        Assert.Equal("PROD00000031", report.ExpectedNextBates);
    }

    [Fact]
    public async Task ValidateAsync_WithPlanFillingGap_InRejectMode_ShouldPass()
    {
        // Arrange
        var prior1 = CreateMockManifest("PROD00000001", "PROD00000010", "PROD", 8);
        var prior2 = CreateMockManifest("PROD00000021", "PROD00000030", "PROD", 8);
        var request = new FileGenerationRequest
        {
            Bates = new BatesNumberConfig { Prefix = "PROD", Digits = 8 },
            Production = new ProductionConfig
            {
                SupplementalProduction = true,
                PriorManifests = new[] { prior1, prior2 },
                SupplementalGapPolicy = "reject"
            }
        };

        // Act
        var report = await SupplementalValidator.ValidateAsync(request, "PROD00000011", "PROD00000020");

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report.DuplicateRanges);
        Assert.Empty(report.SkippedRanges);
        Assert.Equal("PROD00000031", report.ExpectedNextBates);
    }
}
