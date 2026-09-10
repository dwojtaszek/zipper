using System.Globalization;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveTestIdentityTests
{
    private const string EmptyArchiveSha256 = "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85";
    private const string FrozenValidEmptyId = "atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950";

    [Fact]
    public void ComputeFixtureId_FrozenValidEmptyVector_MatchesPublishedId()
    {
        // Independently computed (Python hashlib) before the implementation existed.
        var id = ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 42, EmptyArchiveSha256);

        Assert.Equal(FrozenValidEmptyId, id);
    }

    [Fact]
    public void ComputeFixtureId_IdenticalInputs_ProduceSameId()
    {
        var first = ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 42, EmptyArchiveSha256);
        var second = ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 42, EmptyArchiveSha256);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("2")]                       // different generator contract version
    [InlineData("valid-stored")]            // different Case Key
    [InlineData("3f1b1e2e0b6bd5f3bd1c62f5e6b5a37d0f1a6c3e3a8e69b52b6a1b7ea4a3f6d3")] // different final hash
    public void ComputeFixtureId_ChangedDescriptorField_ProducesDifferentId(string changedValue)
    {
        var baseline = ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 42, EmptyArchiveSha256);

        var changed = changedValue switch
        {
            "2" => ArchiveTestIdentity.ComputeFixtureId("2", "valid-empty", 1, 1, 42, EmptyArchiveSha256),
            "valid-stored" => ArchiveTestIdentity.ComputeFixtureId("1", "valid-stored", 1, 1, 42, EmptyArchiveSha256),
            _ => ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 42, changedValue),
        };

        Assert.NotEqual(baseline, changed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ComputeFixtureId_ChangedRevisionOrSeed_ProducesDifferentId(int descriptorVariant)
    {
        var baseline = ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 42, EmptyArchiveSha256);

        var changed = descriptorVariant switch
        {
            0 => ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 2, 1, 42, EmptyArchiveSha256),
            1 => ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 2, 42, EmptyArchiveSha256),
            _ => ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 43, EmptyArchiveSha256),
        };

        Assert.NotEqual(baseline, changed);
    }

    [Theory]
    [InlineData(int.MinValue, "atc-5745744e06bda556afe6f505a3534ba05250addd5c9e69b50d5337dfce8780d2")]
    [InlineData(0, "atc-3aaffb5829a1c2a1406b0cb4253a87b360719429f14abecdfc88ea43aaea1b32")]
    [InlineData(int.MaxValue, "atc-903e25b212d49eacb528029f9a6f931b29764626b714bc3e63a6695dfdc49733")]
    public void ComputeFixtureId_SeedBoundaryValues_MatchFrozenVectors(int seed, string expectedId)
    {
        var id = ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, seed, EmptyArchiveSha256);

        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void ComputeFixtureId_NonInvariantCulture_ProducesSameId()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        }
        catch (CultureNotFoundException)
        {
            // Globalization-invariant runtime: make the skipped culture switch visible.
            Assert.Equal(string.Empty, CultureInfo.CurrentCulture.Name);
        }

        try
        {
            var id = ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, int.MaxValue, EmptyArchiveSha256);

            Assert.Equal("atc-903e25b212d49eacb528029f9a6f931b29764626b714bc3e63a6695dfdc49733", id);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Valid-Empty")]
    [InlineData("valid_empty")]
    [InlineData("valid.empty")]
    [InlineData("-valid-empty")]
    [InlineData("valid-empty-")]
    [InlineData("valid/../empty")]
    public void ComputeFixtureId_InvalidCaseKey_Throws(string caseKey)
    {
        Assert.Throws<ArgumentException>(
            () => ArchiveTestIdentity.ComputeFixtureId("1", caseKey, 1, 1, 42, EmptyArchiveSha256));
    }

    [Theory]
    [InlineData("")]
    [InlineData("8739C76E681F900923B900C9DF0EF75CF421D39CABB54650C4B9AD19B6A76D85")]
    [InlineData("8739c76e")]
    [InlineData("zz39c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85")]
    public void ComputeFixtureId_InvalidArchiveHash_Throws(string archiveSha256)
    {
        Assert.Throws<ArgumentException>(
            () => ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, 1, 42, archiveSha256));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("one")]
    [InlineData("")]
    public void ComputeFixtureId_InvalidContractVersion_Throws(string contractVersion)
    {
        Assert.Throws<ArgumentException>(
            () => ArchiveTestIdentity.ComputeFixtureId(contractVersion, "valid-empty", 1, 1, 42, EmptyArchiveSha256));
    }

    [Fact]
    public void ComputeFixtureId_NonPositiveRevision_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 0, 1, 42, EmptyArchiveSha256));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArchiveTestIdentity.ComputeFixtureId("1", "valid-empty", 1, -1, 42, EmptyArchiveSha256));
    }

    [Fact]
    public void IsFixtureId_KnownFormats_MatchExpected()
    {
        Assert.True(ArchiveTestIdentity.IsFixtureId(FrozenValidEmptyId));
        Assert.False(ArchiveTestIdentity.IsFixtureId("atc-" + FrozenValidEmptyId[4..].ToUpperInvariant()));
        Assert.False(ArchiveTestIdentity.IsFixtureId("atc-short"));
        Assert.False(ArchiveTestIdentity.IsFixtureId(FrozenValidEmptyId[4..]));
    }
}
