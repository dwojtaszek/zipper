using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #846: the suite memberships frozen in the parent contract (#834). The
/// catalogue explicitly owns membership — no inference from filenames — and these
/// tests pin every suite so a membership change fails CI instead of drifting silently.
/// </summary>
public class ArchiveTestSuiteContractTests
{
    /// <summary>The five frozen smoke keys, in generation order (ordinal Case Key).</summary>
    private static readonly string[] FrozenSmokeCaseKeys =
    [
        "crc-both-mismatch",
        "missing-eocd",
        "valid-deflate",
        "valid-empty",
        "valid-stored",
    ];

    /// <summary>The eleven policy-sensitive path/collision cases (ticket #842).
    /// Twin of ArchivePathPolicyCaseTests.PolicyCaseKeys — both must change together;
    /// the exact security-suite assertion below fails if they drift apart.</summary>
    private static readonly string[] PolicySecurityCaseKeys =
    [
        "case-collision",
        "duplicate-name",
        "file-directory-conflict",
        "path-parent-traversal",
        "path-posix-absolute",
        "path-reserved-device",
        "path-trailing-dot-space",
        "path-unc",
        "path-windows-drive",
        "symlink-then-descendant",
        "unicode-normalization-collision",
    ];

    /// <summary>The unsupported-feature case (ticket #840) the contract houses in security.</summary>
    private static readonly string[] UnsupportedFeatureSecurityCaseKeys = ["unsupported-method"];

    /// <summary>The bounded resource cases (ticket #843) the contract houses in security.</summary>
    private static readonly string[] BoundedResourceSecurityCaseKeys =
    [
        "declared-size-oversized",
        "high-ratio-bounded",
        "many-small-entries",
        "nested-archives-depth-two",
    ];

    private static List<string> SuiteKeys(string suite) =>
        [.. ArchiveTestCatalog.ListSuite(suite).Select(definition => definition.CaseKey)];

    private static List<string> AllKeys() => SuiteKeys(ArchiveTestCatalog.AllSuites);

    // ---- smoke: exactly the frozen five ----

    [Fact]
    public void ListSuite_Smoke_ContainsExactlyTheFiveFrozenCaseKeys()
    {
        Assert.Equal(FrozenSmokeCaseKeys, SuiteKeys(ArchiveTestCatalog.SmokeSuite));
    }

    [Fact]
    public void ListSuite_Smoke_MembersSpanValidAndMalformedClassifications()
    {
        // The frozen smoke set is deliberately mixed: healthy controls plus the two
        // reader-hostile cases every consumer pipeline must survive.
        var smoke = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.SmokeSuite);
        Assert.Equal(3, smoke.Count(c => c.Classification == "valid"));
        Assert.Equal(2, smoke.Count(c => c.Classification == "malformed"));
    }

    // ---- security: policy, collision, unsupported-feature, bounded resource ----

    [Fact]
    public void ListSuite_Security_ContainsPolicyCollisionUnsupportedFeatureAndBoundedResourceCases()
    {
        var expected =
            UnsupportedFeatureSecurityCaseKeys
                .Concat(BoundedResourceSecurityCaseKeys)
                .Concat(PolicySecurityCaseKeys)
                .Order(StringComparer.Ordinal);

        Assert.Equal(expected, SuiteKeys(ArchiveTestCatalog.SecuritySuite));
    }

    [Fact]
    public void ListSuite_Security_PolicyMembersStayPolicySensitiveDirectRecipes()
    {
        var security = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.SecuritySuite);

        var policyMembers = security.Where(c => PolicySecurityCaseKeys.Contains(c.CaseKey));
        Assert.All(policyMembers, c =>
        {
            Assert.Equal("policy-sensitive", c.Classification);
            Assert.False(c.IsMutation);
            Assert.Null(c.Construction);
        });
    }

    // ---- compatibility and malformed: classification-based, plus pinned structure cases ----

    [Fact]
    public void ListSuite_Compatibility_ContainsExactlyEveryValidClassificationCase()
    {
        var expected = AllKeys()
            .Where(key => ArchiveTestCatalog.GetCase(key).Classification == "valid")
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, SuiteKeys(ArchiveTestCatalog.CompatibilitySuite));
    }

    [Fact]
    public void ListSuite_Malformed_ContainsExactlyEveryMalformedCasePlusPinnedStructureCases()
    {
        var malformed = SuiteKeys(ArchiveTestCatalog.MalformedSuite);

        // Every malformed-classification case plus the one pinned structure case
        // with a non-malformed classification (unsupported-method, policy-sensitive,
        // housed in malformed since ticket #840; declared-size-oversized is already
        // malformed-classified and arrives via the derived set).
        var expected = AllKeys()
            .Where(key => ArchiveTestCatalog.GetCase(key).Classification == "malformed")
            .Append("unsupported-method")
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, malformed);
    }

    // ---- all: the distinct union ----

    [Fact]
    public void ListSuite_All_IsTheDistinctUnionOfEverySuite()
    {
        var union = SuiteKeys(ArchiveTestCatalog.SmokeSuite)
            .Concat(SuiteKeys(ArchiveTestCatalog.CompatibilitySuite))
            .Concat(SuiteKeys(ArchiveTestCatalog.MalformedSuite))
            .Concat(SuiteKeys(ArchiveTestCatalog.SecuritySuite))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var all = SuiteKeys(ArchiveTestCatalog.AllSuites);
        Assert.Equal(union, all);
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalogue_ContainsExactlyTheFrozenFortyNineCaseKeys()
    {
        // The frozen complete-catalogue size (#834): a case dropped from the
        // catalogue or unlisted from every suite fails here, not only in E2E.
        Assert.Equal(49, AllKeys().Count);
    }
}
