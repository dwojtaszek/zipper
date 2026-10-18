using System.Text.Json;
using Xunit;
using Zipper.Config;
using Zipper.Emails;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class FamilyPlanTests
{
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(0, 37.5)]
    [InlineData(0, 50.0)]
    [InlineData(0, 100.0)]
    [InlineData(42, 0.0)]
    [InlineData(42, 37.5)]
    [InlineData(42, 50.0)]
    [InlineData(42, 100.0)]
    [InlineData(99, 0.0)]
    [InlineData(99, 37.5)]
    [InlineData(99, 50.0)]
    [InlineData(99, 100.0)]
    public void HasAttachment_SeededEmailPipeline_MatchesActualAttachmentDecision(int seed, double attachmentRate)
    {
        const int startIndex = 1;
        const int endIndex = 50;

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "eml",
                FileCount = endIndex,
            },
            Metadata = new MetadataConfig
            {
                Seed = seed,
                WithFamilies = true,
            },
            LoadFile = new LoadFileConfig
            {
                AttachmentRate = attachmentRate,
            },
        };

        var generator = new EmlFileGenerator();
        var mismatches = new List<string>();

        for (int index = startIndex; index <= endIndex; index++)
        {
            var workItem = new FileWorkItem { Index = index };
            bool expected = FamilyPlan.HasAttachment(request, index);
            var generated = generator.Generate(workItem, request);
            bool actual = generated.Attachment.HasValue;

            if (expected != actual)
            {
                mismatches.Add(
                    $"Seed={seed}, Rate={attachmentRate}, Index={index}: expected FamilyPlan.HasAttachment={expected}, but EmlFileGenerator.Attachment.HasValue={actual}");
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void GetTemplatesCountForCategory_MatchesEmailTemplatesJson()
    {
        var assembly = typeof(EmailFactory).Assembly;
        const string resourceName = "Zipper.Emails.email-templates.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var doc = JsonDocument.Parse(json);
        var templates = doc.RootElement.GetProperty("templates");

        foreach (var category in Enum.GetValues<EmailCategory>())
        {
            var categoryName = category.ToString();
            Assert.True(templates.TryGetProperty(categoryName, out var categoryTemplates), $"Missing template category: {categoryName}");
            int actualCount = categoryTemplates.GetArrayLength();
            int simulatedCount = FamilyPlan.GetTemplatesCountForCategory(category);
            Assert.Equal(actualCount, simulatedCount);
        }
    }

    [Fact]
    public void HasAttachment_WithoutFamilies_ReturnsFalse()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 10 },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = false },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };

        Assert.False(FamilyPlan.HasAttachment(request, 1));
    }

    [Fact]
    public void HasAttachment_NotEml_ReturnsFalse()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "pdf", FileCount = 10 },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };

        Assert.False(FamilyPlan.HasAttachment(request, 1));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public void HasAttachment_AttachmentRateZeroOrNegative_ReturnsFalse(double rate)
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 10 },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            LoadFile = new LoadFileConfig { AttachmentRate = rate },
        };

        Assert.False(FamilyPlan.HasAttachment(request, 1));
    }

    [Theory]
    [InlineData("test.jpg", "test.jpg")]
    [InlineData("path/to/test.jpg", "test.jpg")]
    [InlineData("path\\to\\test.jpg", "test.jpg")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void SanitizeAttachmentFilename_ReturnsOnlyFileName(string? input, string? expected)
    {
        string? result = FamilyPlan.SanitizeAttachmentFilename(input!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetSimulatedChildCount_MatchesSumOfHasAttachmentDecisions()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 20 },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            LoadFile = new LoadFileConfig { AttachmentRate = 50 },
        };

        long expectedCount = 0;
        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            if (FamilyPlan.HasAttachment(request, i))
            {
                expectedCount++;
            }
        }

        long actualCount = FamilyPlan.GetSimulatedChildCount(request);
        Assert.Equal(expectedCount, actualCount);
    }
}
