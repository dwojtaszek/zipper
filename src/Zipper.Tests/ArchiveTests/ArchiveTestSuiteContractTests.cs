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

    /// <summary>The twenty-one policy-sensitive path/collision/entry-type cases
    /// (tickets #842, #880, #882, #885, #875, #884, #888, and #878). Twin of
    /// ArchivePathPolicyCaseTests.PolicyCaseKeys —
    /// both must change together; the exact security-suite assertion below fails if
    /// they drift apart.</summary>
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
        "azure-directory-marker-collision",
        "filename-bidi-override",
        "path-windows-illegal-chars",
        "path-azure-disallowed-unicode",
        "directory-slash-with-payload",
        "directory-attribute-with-payload",
        "zero-width-collision",
        "path-emoji-zwj-namemax",
        "path-adls-segments-boundary",
        "path-adls-segments-exceeded",
    ];

    /// <summary>The unsupported-feature cases (ticket #840, plus Deflate64 #877) the contract houses in security.</summary>
    private static readonly string[] UnsupportedFeatureSecurityCaseKeys = ["unsupported-method", "unsupported-method-deflate64"];

    /// <summary>The bounded resource cases (tickets #843 and #1281) the contract houses in security.</summary>
    private static readonly string[] BoundedResourceSecurityCaseKeys =
    [
        "declared-size-oversized",
        "declared-size-undersized",
        "high-ratio-bounded",
        "many-small-entries",
        "nested-archives-depth-two",
        "nested-archives-cumulative-budget-boundary",
        "zip-bomb-overlapping-deflate",
        "bzip2-high-ratio-bounded",
    ];

    /// <summary>The malformed parser-differential cases (tickets #869, #870, #1280, and #1285) housed in security.</summary>
    private static readonly string[] ParserDifferentialSecurityCaseKeys =
        ["orphan-local-header", "eocdr-ambiguity-comment", "zip64-locator-ambiguity", "trailing-data-undeclared"];

    /// <summary>The hostile filename-byte cases (ticket #876) housed in security.</summary>
    private static readonly string[] HostileNameSecurityCaseKeys = ["filename-null-byte", "filename-c0-control"];

    /// <summary>The Unicode name-policy case (ticket #871) housed in security.</summary>
    private static readonly string[] UnicodePathSecurityCaseKeys = ["unicode-path-extra-mismatch"];

    /// <summary>The method/data disagreement cases (ticket #899) housed in security.</summary>
    private static readonly string[] MethodDataSecurityCaseKeys = ["method-data-deflate-as-bzip2", "method-data-bzip2-as-stored"];

    /// <summary>The split-archive declaration cases (ticket #933) housed in security.</summary>
    private static readonly string[] SplitArchiveSecurityCaseKeys = ["multidisk-eocd-declared", "multidisk-central-entry-declared"];

    /// <summary>The mixed-method one-unsupported-member case (ticket #935) housed in security.</summary>
    private static readonly string[] MixedUnsupportedSecurityCaseKeys = ["mixed-methods-one-unsupported-member"];

    /// <summary>The Zip64 header-vs-extra size source case (ticket #1279) housed in security.</summary>
    private static readonly string[] Zip64ExtraConflictSecurityCaseKeys = ["zip64-extra-without-sentinels"];

    /// <summary>The Zip64 EOCD offset-source disagreement case (ticket #1286) housed in security.</summary>
    private static readonly string[] Zip64EocdDisagreementSecurityCaseKeys = ["zip64-eocd-offset-disagreement"];

    /// <summary>The four policy-sensitive name-encoding cases housed in the encoding suite, in ordinal Case Key order.</summary>
    private static readonly string[] EncodingCaseKeys =
    [
        "encoding-big5-trail-backslash",
        "encoding-cp932-trail-backslash",
        "encoding-gbk-trail-backslash",
        "encoding-local-central-codec-divergence",
    ];

    private static List<string> SuiteKeys(string suite) =>
        [.. ArchiveTestCatalog.ListSuite(suite).Select(definition => definition.CaseKey)];

    private static List<string> AllKeys() => SuiteKeys(ArchiveTestCatalog.AllSuites);

    [Fact]
    public void ResolveSuite_SupportedNames_CanonicalizesCase()
    {
        foreach (var suite in ArchiveTestCatalog.SupportedSuites)
        {
            Assert.Equal(suite, ArchiveTestCatalog.ResolveSuite(suite.ToUpperInvariant()));
            Assert.NotEmpty(ArchiveTestCatalog.ListSuite(suite));
        }

        Assert.Null(ArchiveTestCatalog.ResolveSuite("unsupported"));
        Assert.Throws<ArgumentException>(() => ArchiveTestCatalog.ListSuite("unsupported"));
    }

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

    [Fact]
    public void Catalogue_CrcBothMismatchDeflate_UsesDeflateControlAndStaysOutOfSmoke()
    {
        var definition = ArchiveTestCatalog.GetCase("crc-both-mismatch-deflate");

        Assert.Equal("malformed", definition.Classification);
        Assert.Equal([ArchiveTestCatalog.MalformedSuite], definition.Suites);
        Assert.Equal("valid-deflate", definition.ControlCaseKey);
        Assert.Equal([ArchiveTestMutationKind.CrcBothMismatchDeflate], definition.Mutations);
        Assert.DoesNotContain(definition.CaseKey, SuiteKeys(ArchiveTestCatalog.SmokeSuite));
    }

    // ---- security: policy, collision, unsupported-feature, bounded resource ----

    [Fact]
    public void ListSuite_Security_ContainsPolicyCollisionUnsupportedFeatureAndBoundedResourceCases()
    {
        var expected =
            UnsupportedFeatureSecurityCaseKeys
                .Concat(BoundedResourceSecurityCaseKeys)
                .Concat(ParserDifferentialSecurityCaseKeys)
                .Concat(UnicodePathSecurityCaseKeys)
                .Concat(HostileNameSecurityCaseKeys)
                .Concat(MethodDataSecurityCaseKeys)
                .Concat(SplitArchiveSecurityCaseKeys)
                .Concat(MixedUnsupportedSecurityCaseKeys)
                .Concat(Zip64ExtraConflictSecurityCaseKeys)
                .Concat(Zip64EocdDisagreementSecurityCaseKeys)
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
    public void ListSuite_Security_Zip64ExtraWithoutSentinelsIsPolicySensitiveAndSevenZipExtracts()
    {
        var definition = ArchiveTestCatalog.GetCase("zip64-extra-without-sentinels");
        var adapterCheck = Assert.Single(definition.AdapterChecks!);

        Assert.Equal(ArchiveTestCatalog.PolicySensitiveClassification, definition.Classification);
        Assert.Equal(["malformed", "security"], definition.Suites);
        Assert.Equal([ArchiveTestMutationKind.Zip64SentinelCleared], definition.Mutations!);
        Assert.Equal(("7zip", "extract", "success"),
            (adapterCheck.Adapter, adapterCheck.Action, adapterCheck.ExpectedOutcome));
    }

    [Fact]
    public void ListSuite_Security_Zip64EocdOffsetDisagreementIsPolicySensitiveAndSevenZipExtracts()
    {
        var definition = ArchiveTestCatalog.GetCase("zip64-eocd-offset-disagreement");
        var adapterCheck = Assert.Single(definition.AdapterChecks!);

        Assert.Equal(ArchiveTestCatalog.PolicySensitiveClassification, definition.Classification);
        Assert.Equal(["malformed", "security"], definition.Suites);
        Assert.Equal([ArchiveTestMutationKind.Zip64EocdOffsetDisagreement], definition.Mutations!);
        Assert.Equal(
            ArchiveControlConstruction.Zip64HandBuiltWithRealEocdValues,
            definition.ControlConstruction);
        Assert.Equal(("7zip", "extract", "success"),
            (adapterCheck.Adapter, adapterCheck.Action, adapterCheck.ExpectedOutcome));
    }


    [Fact]
    public void ListSuite_Encoding_ContainsExactlyTheNameEncodingCases()
    {
        Assert.Equal(EncodingCaseKeys, SuiteKeys(ArchiveTestCatalog.EncodingSuite));
    }

    [Fact]
    public void ListSuite_Encoding_MembersStayOutOfDefaultSuites()
    {
        // The #887 and #1283 codec controls stay out of the default suites:
        // only named encoding consumers evaluate the decode-level behavior.
        var encoding = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.EncodingSuite);
        Assert.All(encoding, c =>
        {
            Assert.Equal("policy-sensitive", c.Classification);
            Assert.False(c.IsMutation);
            Assert.Single(c.Suites);
            Assert.Equal(ArchiveTestCatalog.EncodingSuite, c.Suites[0]);
        });
    }

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

        // Every malformed-classification case plus the pinned structure cases
        // with a non-malformed classification (unsupported-method,
        // unsupported-method-deflate64, the #933 split-archive declarations,
        // and the #935 mixed one-unsupported member, all policy-sensitive,
        // housed in malformed since ticket #840;
        // declared-size-oversized and declared-size-undersized are already
        // malformed-classified and arrive via the derived set; the #1279 Zip64
        // source-disagreement case and #1286 Zip64 EOCD offset disagreement are also
        // policy-sensitive and housed here because readers may reject them).
        var expected = AllKeys()
            .Where(key => ArchiveTestCatalog.GetCase(key).Classification == "malformed")
            .Append("unsupported-method")
            .Append("unsupported-method-deflate64")
            .Append("multidisk-eocd-declared")
            .Append("multidisk-central-entry-declared")
            .Append("mixed-methods-one-unsupported-member")
            .Append("eocdr-ambiguity-comment")
            .Append("zip64-locator-ambiguity")
            .Append("zip64-extra-without-sentinels")
            .Append("zip64-eocd-offset-disagreement")
            .Append("trailing-data-undeclared")
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, malformed);
    }

    // ---- all: the distinct union ----

    [Fact]
    public void ListSuite_All_IsTheDistinctUnionOfEverySuite()
    {
        var union = ArchiveTestCatalog.SupportedSuites
            .Where(suite => suite != ArchiveTestCatalog.AllSuites)
            .SelectMany(SuiteKeys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var all = SuiteKeys(ArchiveTestCatalog.AllSuites);
        Assert.Equal(union, all);
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalogue_ContainsExactlyTheFrozenCaseKeys()
    {
        // The frozen complete-catalogue size (#834, +1 for #869, +1 for #871, +1 for
        // #872, +2 for #873, +3 for #874, +2 for #876, +1 for #880, +1 for #882,
        // +1 for #877, +1 for #897, +1 for #898, +1 for the consolidated #885/#886/#879 matrix,
        // +2 for #875, +1 for #884, +1 for #889, +5 for #899, +6 for #900, +1 for #931,
        // +5 for #933, +2 for #934 Zip64 descriptors, +7 for the #934 conflict matrix,
        // +1 for the #934 clean Unicode Path control, +1 for the #935 nested budget boundary,
        // +2 for the #935 mixed one-bad-member cases, +1 for #888, +1 for #870,
        // +2 for #878, +3 for the #887 legacy-codec encoding controls, +1 for #1276,
        // +2 for #1277, +1 for #1279, +1 for #1280, +1 for #1281, +1 for #1282,
        // +1 for #1283 encoding codec divergence, +1 for #1285 undeclared trailing,
        // +1 for #1286 Zip64 EOCD offset disagreement, +1 for #1288 extra-field truncation,
        // +1 for #1289 the DEFLATE-control CRC mismatch
        // data): any case dropped from the catalog
        // or unlisted from every suite fails here.
        Assert.Equal(118, AllKeys().Count);
    }
}
