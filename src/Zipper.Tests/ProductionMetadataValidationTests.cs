using Xunit;
using Zipper.Validation;

namespace Zipper.Tests;

public class ProductionMetadataValidationTests
{
    private static (ProductionSetValidationReport Report, string TempDir) ValidateManifestJson(string manifestJson)
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "_manifest.json"), manifestJson);

        var report = ProductionSetPostValidator.Validate(tempDir, new FileGenerationRequest());
        return (report, tempDir);
    }

    private static string[] MetadataFindings(ProductionSetValidationReport report) =>
        report.Findings
            .Where(f => f.Code == "MetadataAzureConstraint")
            .Select(f => f.Message)
            .ToArray();

    [Fact]
    public void Validate_WithCompliantMetadataBlock_ProducesNoMetadataFindings()
    {
        var manifest = """
            {
              "metadata": {
                "production_id": "PROD_20250101",
                "bates_number_start": "PROD00000001",
                "bates_number_end": "PROD00000010",
                "volume_count": "2"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            Assert.Empty(MetadataFindings(report));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithNonAsciiMetadataValue_ReportsAzureConstraint()
    {
        var manifest = """
            {
              "metadata": {
                "production_id": "caf\u00e9_prod"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Single(findings);
            Assert.Contains("tab (U+0009) or U+0020 through U+007E", findings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithCrlfMetadataValue_ReportsAzureConstraint()
    {
        // The JSON file carries escaped \r\n; JsonDocument decodes them to CR/LF.
        var manifest = """
            {
              "metadata": {
                "production_id": "line1\r\nline2"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Single(findings);
            Assert.Contains("tab (U+0009) or U+0020 through U+007E", findings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithControlCharacterMetadataValue_ReportsAzureConstraint()
    {
        var manifest = """
            {
              "metadata": {
                "production_id": "value\u0000",
                "bates_number_start": "value\u007f"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Equal(2, findings.Length);
            Assert.All(findings, finding =>
                Assert.Contains("tab (U+0009) or U+0020 through U+007E", finding, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithInvalidMetadataKey_ReportsAzureConstraint()
    {
        var manifest = """
            {
              "metadata": {
                "1bad-key": "value"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Single(findings);
            Assert.Contains("naming rule", findings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithNonStringMetadataValue_ReportsAzureConstraint()
    {
        var manifest = """
            {
              "metadata": {
                "volume_count": 2
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Single(findings);
            Assert.Contains("must be a string", findings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithOversizedMetadataBlock_ReportsAzureConstraint()
    {
        var manifest = $$"""
            {
              "metadata": {
                "production_id": "{{new string('a', 9000)}}"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Single(findings);
            Assert.Contains("8,192 bytes", findings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithMetadataAtExactBudget_ProducesNoMetadataFindings()
    {
        // "production_id" is 13 bytes; 13 + 8179 = 8,192, the exact Azure budget.
        var manifest = $$"""
            {
              "metadata": {
                "production_id": "{{new string('a', 8179)}}"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            Assert.Empty(MetadataFindings(report));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithMetadataOneByteOverBudget_ReportsAzureConstraint()
    {
        // 13 + 8180 = 8,193 — one byte past the Azure budget.
        var manifest = $$"""
            {
              "metadata": {
                "production_id": "{{new string('a', 8180)}}"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Single(findings);
            Assert.Contains("8,192 bytes", findings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithEmptyMetadataObject_ProducesNoMetadataFindings()
    {
        var manifest = """
            {
              "metadata": {}
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            Assert.Empty(MetadataFindings(report));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithEmptyMetadataValue_ProducesNoMetadataFindings()
    {
        var manifest = """
            {
              "metadata": {
                "production_id": ""
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            Assert.Empty(MetadataFindings(report));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithMultipleViolations_ReportsOneFindingPerViolation()
    {
        // Invalid key AND CR/LF value on the same block: two findings.
        var manifest = """
            {
              "metadata": {
                "1bad-key": "line1\r\nline2"
              }
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Equal(2, findings.Length);
            Assert.Contains(findings, f => f.Contains("naming rule", StringComparison.Ordinal));
            Assert.Contains(findings, f => f.Contains("tab (U+0009) or U+0020 through U+007E", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_WithNonObjectMetadata_ReportsAzureConstraint()
    {
        var manifest = """
            {
              "metadata": "not-an-object"
            }
            """;

        var (report, tempDir) = ValidateManifestJson(manifest);
        try
        {
            var findings = MetadataFindings(report);
            Assert.Single(findings);
            Assert.Contains("must be an object", findings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
