#pragma warning disable RS0030 // Do not use banned APIs
using System.Text.Json;
using Xunit;
using Zipper.Profiles;

namespace Zipper
{
    public class ColumnProfileLoaderTests : IDisposable
    {
        private readonly string tempDir;

        public ColumnProfileLoaderTests()
        {
            this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(this.tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, true);
            }
        }

        [Fact]
        public void Load_WithBuiltInProfileName_ReturnsProfile()
        {
            // Arrange - "standard" is a built-in profile
            string profileName = "standard";

            // Act
            var profile = ColumnProfileLoader.Load(profileName);

            // Assert
            Assert.NotNull(profile);
            Assert.Equal("standard", profile.Name);  // Built-in profiles use lowercase names
        }

        [Fact]
        public void Load_WithValidFilePath_LoadsProfileFromFile()
        {
            // Arrange
            var testProfile = new ColumnProfile
            {
                Name = "TestProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "DocID", Type = "identifier" },
                    new ColumnDefinition { Name = "Title", Type = "text" },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            var filePath = Path.Combine(this.tempDir, "test-profile.json");
            var json = JsonSerializer.Serialize(testProfile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            // Act
            var loaded = ColumnProfileLoader.Load(filePath);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal("TestProfile", loaded.Name);
            Assert.Equal(2, loaded.Columns.Count);
        }

        [Fact]
        public void Load_WithNonExistentPath_ReturnsNull()
        {
            // Arrange
            string nonExistentPath = "/path/that/does/not/exist.json";

            // Act
            var result = ColumnProfileLoader.Load(nonExistentPath);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void LoadFromFile_WithValidJson_LoadsProfile()
        {
            // Arrange
            var json = @"{
                ""name"": ""CustomProfile"",
                ""columns"": [
                    { ""name"": ""ID"", ""type"": ""identifier"" },
                    { ""name"": ""Description"", ""type"": ""longtext"" }
                ],
                ""dataSources"": {}
            }";

            var filePath = Path.Combine(this.tempDir, "custom.json");
            File.WriteAllText(filePath, json);

            // Act
            var profile = ColumnProfileLoader.LoadFromFile(filePath);

            // Assert
            Assert.NotNull(profile);
            Assert.Equal("CustomProfile", profile.Name);
            Assert.Equal(2, profile.Columns.Count);
        }

        [Fact]
        public void LoadFromFile_WithInvalidJson_ThrowsInvalidOperationException()
        {
            // Arrange
            var invalidJson = "{ this is not valid json }";
            var filePath = Path.Combine(this.tempDir, "invalid.json");
            File.WriteAllText(filePath, invalidJson);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.LoadFromFile(filePath));

            Assert.Contains("Invalid JSON", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void LoadFromFile_WithNullProfile_ThrowsInvalidOperationException()
        {
            // Arrange - JSON that deserializes to null
            var json = "null";
            var filePath = Path.Combine(this.tempDir, "null.json");
            File.WriteAllText(filePath, json);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.LoadFromFile(filePath));

            Assert.Contains("Failed to parse", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithValidProfile_DoesNotThrow()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "ValidProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "DocID", Type = "identifier" },
                    new ColumnDefinition { Name = "Text", Type = "text" },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert - Should not throw
            ColumnProfileLoader.Validate(profile);
        }

        [Fact]
        public void Validate_WithEmptyName_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = string.Empty,
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("must have a name", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithNoColumns_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "NoColumnsProfile",
                Columns = new List<ColumnDefinition>(),
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("at least one column", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithNullColumns_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "NullColumnsProfile",
                Columns = null!,
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("at least one column", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithNullDataSources_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "NullDataSourcesProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                },
                DataSources = null!,
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("DataSources dictionary", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithTooManyColumns_ThrowsInvalidOperationException()
        {
            // Arrange
            var columns = new List<ColumnDefinition>();
            for (int i = 0; i <= 200; i++)
            {
                columns.Add(new ColumnDefinition
                {
                    Name = $"Column{i}",
                    Type = i == 0 ? "identifier" : "text",
                });
            }

            var profile = new ColumnProfile
            {
                Name = "TooManyColumnsProfile",
                Columns = columns,
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("exceeds maximum of 200", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithDuplicateColumnNames_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "DuplicateColumnsProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                    new ColumnDefinition { Name = "ID", Type = "text" }, // Duplicate!
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("Duplicate column name", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithEmptyColumnName_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "EmptyColumnNameProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                    new ColumnDefinition { Name = string.Empty, Type = "text" },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("null or empty name", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithNoIdentifierColumn_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "NoIdentifierProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "Text1", Type = "text" },
                    new ColumnDefinition { Name = "Text2", Type = "text" },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("'identifier' type column", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithUndefinedDataSource_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "UndefinedDataSourceProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                    new ColumnDefinition
                    {
                        Name = "Category",
                        Type = "coded",
                        DataSource = "categories", // References undefined data source
                    },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal), // Empty!
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("undefined data source", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithInvalidColumnType_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "InvalidTypeProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                    new ColumnDefinition { Name = "Invalid", Type = "invalid_type" },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("invalid type", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithInvalidEmptyPercentage_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "InvalidEmptyPercentageProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                    new ColumnDefinition
                    {
                        Name = "Text",
                        Type = "text",
                        EmptyPercentage = 150, // Invalid: > 100
                    },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("invalid emptyPercentage", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithInvalidTruePercentage_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "InvalidTruePercentageProfile",
                Columns = new List<ColumnDefinition>
                {
                    new ColumnDefinition { Name = "ID", Type = "identifier" },
                    new ColumnDefinition
                    {
                        Name = "IsActive",
                        Type = "boolean",
                        TruePercentage = -10, // Invalid: < 0
                    },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("invalid truePercentage", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void IsBuiltInProfile_WithBuiltInName_ReturnsTrue()
        {
            // Act & Assert
            Assert.True(ColumnProfileLoader.IsBuiltInProfile("standard"));
            Assert.True(ColumnProfileLoader.IsBuiltInProfile("Standard")); // Case insensitive
        }

        [Fact]
        public void IsBuiltInProfile_WithCustomName_ReturnsFalse()
        {
            // Act & Assert
            Assert.False(ColumnProfileLoader.IsBuiltInProfile("custom-profile"));
            Assert.False(ColumnProfileLoader.IsBuiltInProfile("NonExistent"));
        }

        [Fact]
        public void Validate_WithInvalidDateRangeMin_ThrowsInvalidOperationException()
        {
            var profile = CreateMinimalProfile();
            profile.Columns.Add(new ColumnDefinition
            {
                Name = "BadDate",
                Type = "date",
                DateRange = new DateRangeConfig { Min = "not-a-date", Max = "2024-12-31" },
            });

            var ex = Assert.Throws<InvalidOperationException>(() => ColumnProfileLoader.Validate(profile));
            Assert.Contains("BadDate", ex.Message, StringComparison.Ordinal);
            Assert.Contains("DateRange.Min", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithInvalidDateRangeMax_ThrowsInvalidOperationException()
        {
            var profile = CreateMinimalProfile();
            profile.Columns.Add(new ColumnDefinition
            {
                Name = "BadMax",
                Type = "date",
                DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "garbage" },
            });

            var ex = Assert.Throws<InvalidOperationException>(() => ColumnProfileLoader.Validate(profile));
            Assert.Contains("BadMax", ex.Message, StringComparison.Ordinal);
            Assert.Contains("DateRange.Max", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithMinGreaterThanMax_ThrowsInvalidOperationException()
        {
            var profile = CreateMinimalProfile();
            profile.Columns.Add(new ColumnDefinition
            {
                Name = "Inverted",
                Type = "date",
                DateRange = new DateRangeConfig { Min = "2025-01-01", Max = "2020-01-01" },
            });

            var ex = Assert.Throws<InvalidOperationException>(() => ColumnProfileLoader.Validate(profile));
            Assert.Contains("Inverted", ex.Message, StringComparison.Ordinal);
            Assert.Contains("greater than", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithLegacyColumnKind_DoesNotThrow()
        {
            var profile = CreateMinimalProfile();
            profile.Columns.Add(new ColumnDefinition { Name = "Custodian", Type = "foldercustodian" });
            profile.Columns.Add(new ColumnDefinition { Name = "Size", Type = "filedatasize" });

            ColumnProfileLoader.Validate(profile); // Should not throw
        }

        [Fact]
        public void Validate_WithNonUsCulture_ParsesIsoDateRangesCorrectly()
        {
#pragma warning disable RS0030 // Do not use banned APIs
            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            var originalUiCulture = System.Globalization.CultureInfo.CurrentUICulture;

            try
            {
                var nonUsCulture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.GetCultureInfo("ar-SA").Clone();
                nonUsCulture.DateTimeFormat.Calendar = new System.Globalization.UmAlQuraCalendar();
                System.Globalization.CultureInfo.CurrentCulture = nonUsCulture;
                System.Globalization.CultureInfo.CurrentUICulture = nonUsCulture;

                var profile = CreateMinimalProfile();
                profile.Columns.Add(new ColumnDefinition
                {
                    Name = "ValidDateRange",
                    Type = "date",
                    DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2024-12-31" }
                });

                // Act & Assert - Should not throw FormatException or InvalidOperationException
                ColumnProfileLoader.Validate(profile);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCulture;
                System.Globalization.CultureInfo.CurrentUICulture = originalUiCulture;
            }
#pragma warning restore RS0030 // Do not use banned APIs
        }

        [Fact]
        public void Validate_WithWeightsCountExceedingValuesCount_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = CreateMinimalProfile();
            profile.DataSources["testDS"] = new DataSourceConfig
            {
                Values = new List<string> { "A", "B" },
                Weights = new List<int> { 1, 1, 1 }, // 3 weights, but only 2 values
                Distribution = "weighted"
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("testDS", exception.Message, StringComparison.Ordinal);
            Assert.Contains("more weights than values", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validate_WithWeightsCountExceedingCount_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = CreateMinimalProfile();
            profile.DataSources["testDS"] = new DataSourceConfig
            {
                Count = 2,
                Weights = new List<int> { 1, 1, 1 }, // 3 weights, but Count = 2
                Distribution = "weighted"
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("testDS", exception.Message, StringComparison.Ordinal);
            Assert.Contains("more weights than values", exception.Message, StringComparison.Ordinal);
        }

        private static ColumnProfile CreateMinimalProfile()
        {
            return new ColumnProfile
            {
                Name = "test",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "DOCID", Type = "identifier" },
                },
                DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            };
        }
    }
}
