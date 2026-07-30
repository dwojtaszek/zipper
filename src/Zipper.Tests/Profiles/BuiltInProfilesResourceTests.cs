using System.Globalization;
using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

public class BuiltInProfilesResourceTests
{
    [Fact]
    public void GetProfile_FullProfileGeneratorParams_DeserializeAsUsableIntegers()
    {
        var profile = BuiltInProfiles.GetProfile("full");

        Assert.NotNull(profile);
        var notesColumn = profile.Columns.First(c => c.Generator == "loremParagraphs");
        Assert.NotNull(notesColumn.GeneratorParams);
        Assert.IsType<int>(notesColumn.GeneratorParams["min"]);
        Assert.IsType<int>(notesColumn.GeneratorParams["max"]);
        Assert.Equal(1, Convert.ToInt32(notesColumn.GeneratorParams["min"], CultureInfo.InvariantCulture));
        Assert.Equal(3, Convert.ToInt32(notesColumn.GeneratorParams["max"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetProfile_MutatingReturnedClone_DoesNotAffectLaterCalls()
    {
        var first = BuiltInProfiles.GetProfile("standard");
        Assert.NotNull(first);
        first.Columns.Clear();

        var second = BuiltInProfiles.GetProfile("standard");
        Assert.NotNull(second);
        Assert.Equal(24, second.Columns.Count);
    }

    [Fact]
    public void GetProfile_CollectionMetadataPseudoProfile_ReturnsNull()
    {
        // Parity with the original in-code switch: the collection-metadata pseudo-profile
        // is only reachable through MergeWithCollectionMetadata, never through GetProfile.
        Assert.Null(BuiltInProfiles.GetProfile("legacywithcollectionmetadata"));
    }

    [Fact]
    public void GetProfile_WeightedDataSources_PreserveValuesAndWeights()
    {
        var litigation = BuiltInProfiles.GetProfile("litigation");
        Assert.NotNull(litigation);
        Assert.Equal(new[] { 5, 5, 80, 10 }, litigation.DataSources["privilegeTypes"].Weights);

        var full = BuiltInProfiles.GetProfile("full");
        Assert.NotNull(full);
        Assert.Equal(new[] { 80, 5, 3, 2, 2, 2, 1, 2, 2, 1 }, full.DataSources["languages"].Weights);
    }

    [Fact]
    public void Properties_RepeatedAccess_ReturnSameSharedInstance()
    {
        // Pre-existing contract: the static accessors always returned one shared instance
        // ({ get; } initialized once); DatComposerShared relies on the shared statics directly.
        Assert.Same(BuiltInProfiles.Standard, BuiltInProfiles.Standard);
        Assert.Same(BuiltInProfiles.LegacyEml, BuiltInProfiles.LegacyEml);
    }

    [Fact]
    public void BuiltInProfiles_AllSevenProfiles_LoadWithExpectedNameAndColumnCount()
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["minimal"] = 5,
            ["standard"] = 24,
            ["litigation"] = 48,
            ["full"] = 138,
            ["legacywithmetadata"] = 4,
            ["legacyeml"] = 10,
            ["legacywithcollectionmetadata"] = 5,
        };

        var profiles = new[]
        {
            BuiltInProfiles.Minimal,
            BuiltInProfiles.Standard,
            BuiltInProfiles.Litigation,
            BuiltInProfiles.Full,
            BuiltInProfiles.LegacyWithMetadata,
            BuiltInProfiles.LegacyEml,
            BuiltInProfiles.LegacyWithCollectionMetadata,
        };

        foreach (var profile in profiles)
        {
            Assert.True(
                expected.TryGetValue(profile.Name.ToLowerInvariant(), out var columnCount) && profile.Columns.Count == columnCount,
                $"Profile '{profile.Name}' has {profile.Columns.Count} columns");
        }
    }
}
