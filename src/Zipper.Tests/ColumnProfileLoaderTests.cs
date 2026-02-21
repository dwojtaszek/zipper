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
            this.tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
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

            Assert.Contains("Invalid JSON", exception.Message);
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

            Assert.Contains("Failed to parse", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("must have a name", exception.Message);
        }

        [Fact]
        public void Validate_WithNoColumns_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "NoColumnsProfile",
                Columns = new List<ColumnDefinition>(),
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("at least one column", exception.Message);
        }

        [Fact]
        public void Validate_WithNullColumns_ThrowsInvalidOperationException()
        {
            // Arrange
            var profile = new ColumnProfile
            {
                Name = "NullColumnsProfile",
                Columns = null!,
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("at least one column", exception.Message);
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

            Assert.Contains("DataSources dictionary", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("exceeds maximum of 200", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("Duplicate column name", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("null or empty name", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("'identifier' type column", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(), // Empty!
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("undefined data source", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("invalid type", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("invalid emptyPercentage", exception.Message);
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
                DataSources = new Dictionary<string, DataSourceConfig>(),
            };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ColumnProfileLoader.Validate(profile));

            Assert.Contains("invalid truePercentage", exception.Message);
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
    }
}
