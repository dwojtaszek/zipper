using System.Text.Json;
using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

/// <summary>
/// Tests for the column profile system.
/// </summary>
public class ColumnProfileTests
{
    /// <summary>
    /// Test that minimal profile has expected column count.
    /// </summary>
    [Fact]
    public void MinimalProfile_HasExpectedColumns()
    {
        var profile = BuiltInProfiles.Minimal;

        Assert.Equal("minimal", profile.Name);
        Assert.Equal(5, profile.Columns.Count);
        Assert.Contains(profile.Columns, c => c.Name == "DOCID");
        Assert.Contains(profile.Columns, c => c.Name == "FILEPATH");
        Assert.Contains(profile.Columns, c => c.Name == "CUSTODIAN");
    }

    /// <summary>
    /// Test that standard profile has expected column count.
    /// </summary>
    [Fact]
    public void StandardProfile_HasExpectedColumns()
    {
        var profile = BuiltInProfiles.Standard;

        Assert.Equal("standard", profile.Name);
        Assert.Equal(24, profile.Columns.Count);
        Assert.Contains(profile.Columns, c => c.Name == "BEGBATES");
        Assert.Contains(profile.Columns, c => c.Name == "EMAILFROM");
        Assert.Contains(profile.Columns, c => c.Name == "RESPONSIVE");
    }

    /// <summary>
    /// Test that litigation profile has expected column count.
    /// </summary>
    [Fact]
    public void LitigationProfile_HasExpectedColumns()
    {
        var profile = BuiltInProfiles.Litigation;

        Assert.Equal("litigation", profile.Name);
        Assert.Equal(48, profile.Columns.Count);
        Assert.Contains(profile.Columns, c => c.Name == "MD5HASH");
        Assert.Contains(profile.Columns, c => c.Name == "PRIVILEGE");
    }

    /// <summary>
    /// Test that full profile has expected column count.
    /// </summary>
    [Fact]
    public void FullProfile_HasExpectedColumns()
    {
        var profile = BuiltInProfiles.Full;

        Assert.Equal("full", profile.Name);
        Assert.True(profile.Columns.Count >= 100, $"Expected at least 100 columns, got {profile.Columns.Count}");
    }

    /// <summary>
    /// Test that built-in profiles can be loaded by name.
    /// </summary>
    [Theory]
    [InlineData("minimal")]
    [InlineData("standard")]
    [InlineData("litigation")]
    [InlineData("full")]
    public void BuiltInProfiles_CanBeLoadedByName(string profileName)
    {
        var profile = ColumnProfileLoader.Load(profileName);

        Assert.NotNull(profile);
        Assert.Equal(profileName, profile.Name);
    }

    /// <summary>
    /// Test that LoadProfile returns null for invalid name.
    /// </summary>
    [Fact]
    public void LoadProfile_ReturnsNull_ForInvalidName()
    {
        var profile = ColumnProfileLoader.Load("nonexistent");

        Assert.Null(profile);
    }

    /// <summary>
    /// Test that IsBuiltInProfile correctly identifies built-in profiles.
    /// </summary>
    [Theory]
    [InlineData("minimal", true)]
    [InlineData("standard", true)]
    [InlineData("STANDARD", true)]
    [InlineData("custom", false)]
    [InlineData("nonexistent", false)]
    public void IsBuiltInProfile_ReturnsCorrectResult(string name, bool expected)
    {
        var result = ColumnProfileLoader.IsBuiltInProfile(name);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Test that all built-in profiles have at least one identifier column.
    /// </summary>
    [Theory]
    [InlineData("minimal")]
    [InlineData("standard")]
    [InlineData("litigation")]
    [InlineData("full")]
    public void AllBuiltInProfiles_HaveIdentifierColumn(string profileName)
    {
        var profile = BuiltInProfiles.GetProfile(profileName);

        Assert.NotNull(profile);
        Assert.Contains(profile.Columns, c => c.Type == "identifier");
    }

    /// <summary>
    /// Test that profiles can be serialized and deserialized.
    /// </summary>
    [Fact]
    public void Profile_CanBeSerialized_AndDeserialized()
    {
        var profile = BuiltInProfiles.Minimal;
        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<ColumnProfile>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(profile.Name, deserialized.Name);
        Assert.Equal(profile.Columns.Count, deserialized.Columns.Count);
    }

    /// <summary>
    /// Test that profile settings have sensible defaults.
    /// </summary>
    [Fact]
    public void ProfileSettings_HaveSensibleDefaults()
    {
        var settings = new ProfileSettings();

        Assert.Equal("yyyy-MM-dd", settings.DateFormat);
        Assert.Equal(";", settings.MultiValueDelimiter);
        Assert.InRange(settings.EmptyValuePercentage, 0, 100);
    }

    /// <summary>
    /// Test that column definitions have valid types.
    /// </summary>
    [Theory]
    [InlineData("minimal")]
    [InlineData("standard")]
    [InlineData("litigation")]
    public void AllColumns_HaveValidTypes(string profileName)
    {
        var validTypes = new[] { "identifier", "text", "longtext", "date", "datetime", "number", "boolean", "coded", "email" };
        var profile = BuiltInProfiles.GetProfile(profileName);

        Assert.NotNull(profile);
        foreach (var column in profile.Columns)
        {
            Assert.Contains(column.Type.ToLowerInvariant(), validTypes);
        }
    }

    /// <summary>
    /// Test that data sources referenced by columns exist.
    /// </summary>
    [Theory]
    [InlineData("minimal")]
    [InlineData("standard")]
    [InlineData("litigation")]
    public void AllDataSourceReferences_AreValid(string profileName)
    {
        var profile = BuiltInProfiles.GetProfile(profileName);

        Assert.NotNull(profile);
        foreach (var column in profile.Columns.Where(c => !string.IsNullOrEmpty(c.DataSource)))
        {
            Assert.Contains(column.DataSource, profile.DataSources.Keys);
        }
    }
}
