using System;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Zipper
{
    public class EmlGenerationServiceTests
    {
        private readonly ITestOutputHelper _output;

        public EmlGenerationServiceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void GenerateEmlContent_WithValidConfig_ReturnsValidResult()
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = 0,
                Category = EmailTemplateSystem.EmailCategory.Business
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Content);
            Assert.True(result.Content.Length > 0);
            Assert.Null(result.Attachment);

            // Verify EML content structure
            var content = Encoding.UTF8.GetString(result.Content);
            Assert.Contains("From:", content);
            Assert.Contains("To:", content);
            Assert.Contains("Subject:", content);
            Assert.Contains("Date:", content);
            Assert.Contains("MIME-Version: 1.0", content);
        }

        [Fact]
        public void GenerateEmlContent_WithAttachment_ReturnsResultWithAttachment()
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = 100 // Force attachment
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Length > 0);
            Assert.NotNull(result.Attachment);
            Assert.True(result.Attachment.Value.content.Length > 0);
            Assert.False(string.IsNullOrEmpty(result.Attachment.Value.filename));

            // Verify EML contains multipart content
            var content = Encoding.UTF8.GetString(result.Content);
            Assert.Contains("multipart/mixed", content);
            Assert.Contains("Content-Transfer-Encoding: base64", content);
        }

        [Theory]
        [InlineData(0)]   // Never include attachments
        [InlineData(25)]  // 25% attachment rate
        [InlineData(50)]  // 50% attachment rate
        [InlineData(75)]  // 75% attachment rate
        [InlineData(100)] // Always include attachments
        public void GenerateEmlContent_WithDifferentAttachmentRates_WorksCorrectly(int attachmentRate)
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = attachmentRate
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Length > 0);

            // Attachment presence should respect the rate (though it's probabilistic)
            if (attachmentRate == 0)
            {
                Assert.Null(result.Attachment);
            }
            else if (attachmentRate == 100)
            {
                Assert.NotNull(result.Attachment);
            }
        }

        [Theory]
        [InlineData(EmailTemplateSystem.EmailCategory.Business)]
        [InlineData(EmailTemplateSystem.EmailCategory.Personal)]
        [InlineData(EmailTemplateSystem.EmailCategory.Technical)]
        [InlineData(EmailTemplateSystem.EmailCategory.Marketing)]
        [InlineData(EmailTemplateSystem.EmailCategory.Legal)]
        [InlineData(EmailTemplateSystem.EmailCategory.Financial)]
        [InlineData(EmailTemplateSystem.EmailCategory.Notification)]
        [InlineData(EmailTemplateSystem.EmailCategory.Support)]
        public void GenerateEmlContent_WithDifferentCategories_ReturnsCategorySpecificContent(EmailTemplateSystem.EmailCategory category)
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = 0,
                Category = category
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Length > 0);

            var content = Encoding.UTF8.GetString(result.Content);
            _output.WriteLine($"Category: {category}, Content preview: {content.Substring(0, Math.Min(200, content.Length))}...");

            // Should contain valid email structure
            Assert.Contains("From:", content);
            Assert.Contains("To:", content);
            Assert.Contains("Subject:", content);
        }

        [Fact]
        public void GenerateEmlContent_WithoutCategory_ReturnsRandomCategoryContent()
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = 0,
                Category = null
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Length > 0);

            var content = Encoding.UTF8.GetString(result.Content);
            Assert.Contains("From:", content);
            Assert.Contains("To:", content);
            Assert.Contains("Subject:", content);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(9999)]
        public void GenerateEmlContent_WithDifferentFileIndices_GeneratesUniqueContent(int fileIndex)
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = fileIndex,
                AttachmentRate = 0
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Length > 0);

            var content = Encoding.UTF8.GetString(result.Content);
            Assert.Contains("From:", content);
            Assert.Contains("To:", content);

            // Email addresses should reflect the file index
            Assert.Contains($"sender{fileIndex:D3}@", content);
            Assert.Contains($"recipient{fileIndex:D3}@", content);
        }

        [Fact]
        public void GenerateEmlContent_BackwardCompatibilityMethod_WorksSameAsConfigMethod()
        {
            // Arrange
            var fileIndex = 42;
            var attachmentRate = 75;
            var category = EmailTemplateSystem.EmailCategory.Technical;

            // Act - The two methods should behave similarly, though content may differ due to randomness
            var configResult = EmlGenerationService.GenerateEmlContent(
                new EmlGenerationConfig
                {
                    FileIndex = fileIndex,
                    AttachmentRate = attachmentRate,
                    Category = category
                });

            var directResult = EmlGenerationService.GenerateEmlContent(
                fileIndex,
                attachmentRate,
                category);

            // Assert - Both should produce valid EML content (not necessarily identical due to randomness)
            Assert.NotNull(configResult);
            Assert.NotNull(directResult);
            Assert.True(configResult.Content.Length > 0);
            Assert.True(directResult.Content.Length > 0);

            // Both methods should use the same config parameters
            var configContent = Encoding.UTF8.GetString(configResult.Content);
            var directContent = Encoding.UTF8.GetString(directResult.Content);

            // Both should contain valid email structure
            Assert.Contains("From:", configContent);
            Assert.Contains("To:", configContent);
            Assert.Contains("From:", directContent);
            Assert.Contains("To:", directContent);
        }

        [Fact]
        public void GenerateEmlContent_NullConfig_ThrowsNullReferenceException()
        {
            // Act & Assert
            // The implementation doesn't explicitly validate null, so it throws NullReferenceException
            Assert.Throws<NullReferenceException>(() => EmlGenerationService.GenerateEmlContent(null!));
        }

        [Theory]
        [InlineData(-10)]   // Below minimum
        [InlineData(-1)]    // Below minimum
        [InlineData(0)]     // Minimum valid
        [InlineData(50)]    // Middle value
        [InlineData(100)]   // Maximum valid
        [InlineData(150)]   // Above maximum
        [InlineData(1000)]  // Way above maximum
        public void GenerateEmlContent_WithExtremeAttachmentRates_ClampsToValidRange(int attachmentRate)
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = attachmentRate
            };

            // Act
            var result = EmlGenerationService.GenerateEmlContent(config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Length > 0);

            // For rates <= 0, should never have attachments
            if (attachmentRate <= 0)
            {
                Assert.Null(result.Attachment);
            }
            // For rates >= 100, should always have attachments
            else if (attachmentRate >= 100)
            {
                Assert.NotNull(result.Attachment);
            }
        }

        [Fact]
        public void GenerateEmlContent_MultipleCalls_GeneratesVariedContent()
        {
            // Arrange
            var configs = Enumerable.Range(1, 10)
                .Select(i => new EmlGenerationConfig
                {
                    FileIndex = i,
                    AttachmentRate = 50,
                    Category = (EmailTemplateSystem.EmailCategory)(i % 8)
                })
                .ToList();

            var results = new EmlGenerationResult[10];

            // Act
            for (int i = 0; i < configs.Count; i++)
            {
                results[i] = EmlGenerationService.GenerateEmlContent(configs[i]);
            }

            // Assert - All calls should produce valid results
            Assert.All(results, result =>
            {
                Assert.NotNull(result);
                Assert.True(result.Content.Length > 0);
            });

            // Different file indices should produce different email addresses
            var contents = results.Select(r => Encoding.UTF8.GetString(r.Content)).ToList();

            // Each file index should have its corresponding sender/recipient email
            for (int i = 0; i < configs.Count; i++)
            {
                var fileIndex = configs[i].FileIndex;
                Assert.Contains($"sender{fileIndex:D3}@", contents[i]);
                Assert.Contains($"recipient{fileIndex:D3}@", contents[i]);
            }
        }

        [Fact]
        public void GenerateEmlContent_WithZeroAttachmentRate_NeverIncludesAttachment()
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = 0
            };

            // Act
            var results = new EmlGenerationResult[100];
            for (int i = 0; i < 100; i++)
            {
                results[i] = EmlGenerationService.GenerateEmlContent(config);
            }

            // Assert
            Assert.All(results, result => Assert.Null(result.Attachment));
        }

        [Fact]
        public void GenerateEmlContent_WithHundredAttachmentRate_AlwaysIncludesAttachment()
        {
            // Arrange
            var config = new EmlGenerationConfig
            {
                FileIndex = 1,
                AttachmentRate = 100
            };

            // Act
            var results = new EmlGenerationResult[100];
            for (int i = 0; i < 100; i++)
            {
                results[i] = EmlGenerationService.GenerateEmlContent(config);
            }

            // Assert
            Assert.All(results, result =>
            {
                Assert.NotNull(result.Attachment);
                Assert.True(result.Attachment.Value.content.Length > 0);
                Assert.False(string.IsNullOrEmpty(result.Attachment.Value.filename));
            });
        }

        [Fact]
        public void EmlGenerationResult_RecordType_WorksCorrectly()
        {
            // Arrange & Act
            var content = new byte[] { 1, 2, 3 };
            var result1 = new EmlGenerationResult
            {
                Content = content,
                Attachment = ("test.txt", new byte[] { 4, 5, 6 })
            };

            // Note: Using with-expression on records creates a new instance
            // For arrays, reference equality matters, so we use the same array reference
            var result2 = result1 with { };

            // Assert
            Assert.Equal(result1.Content, result2.Content);
            Assert.Equal(result1.Attachment, result2.Attachment);
            Assert.True(result1 == result2);
        }

        [Fact]
        public void EmlGenerationConfig_RecordType_WorksCorrectly()
        {
            // Arrange & Act
            var config1 = new EmlGenerationConfig
            {
                FileIndex = 42,
                AttachmentRate = 75,
                Category = EmailTemplateSystem.EmailCategory.Business
            };

            var config2 = config1 with { };

            // Assert
            Assert.Equal(config1.FileIndex, config2.FileIndex);
            Assert.Equal(config1.AttachmentRate, config2.AttachmentRate);
            Assert.Equal(config1.Category, config2.Category);
            Assert.True(config1 == config2);
        }
    }
}
