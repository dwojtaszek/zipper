using FsCheck.Xunit;
using Xunit;
using Zipper.Validation;

namespace Zipper.Tests.Properties;

/// <summary>
/// Property-based tests for <see cref="DatOptDocumentMatcher"/> Bates Number / Control Number
/// key matching with Page Suffix normalization (REQ-239): Bates precedence in collisions and
/// DAT-to-OPT / OPT-to-DAT matching invariants across generated keys, casing, and suffixes.
/// </summary>
[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class DatOptDocumentMatcherPropertyTests
{
    private sealed record TestDatRecord(string DocId, string Bates);

    [Property]
    public void DocumentKeyMatches_GeneratedKeyForms_ShouldMatchOnlySameOrFirstPageForm(DocumentKeyMatchPropertyInput input)
    {
        var optKey = input.Mismatch
            ? "ZZZ" + input.Key
            : input.AppendFirstPageSuffix
                ? input.Key + DatOptDocumentMatcher.FirstPageSuffix
                : input.Key;
        if (input.VaryCase)
        {
            optKey = ToggleCase(optKey);
        }

        Assert.Equal(!input.Mismatch, DatOptDocumentMatcher.DocumentKeyMatches(input.Key, optKey));
    }

    [Property]
    public void AreDocumentKeysMatching_WithBatesPresent_ShouldPreferBatesOverDocId(DocumentKeyMatchPropertyInput input)
    {
        var optKeyFromBates = input.AppendFirstPageSuffix
            ? input.Key + DatOptDocumentMatcher.FirstPageSuffix
            : input.Key;
        var optKeyFromDocId = input.AppendFirstPageSuffix
            ? input.OtherKey + DatOptDocumentMatcher.FirstPageSuffix
            : input.OtherKey;
        if (input.VaryCase)
        {
            optKeyFromBates = ToggleCase(optKeyFromBates);
            optKeyFromDocId = ToggleCase(optKeyFromDocId);
        }

        // Bates present: the Bates key names the document; the DocId is never a fallback.
        Assert.True(DatOptDocumentMatcher.AreDocumentKeysMatching(input.OtherKey, input.Key, optKeyFromBates));
        Assert.False(DatOptDocumentMatcher.AreDocumentKeysMatching(input.OtherKey, input.Key, optKeyFromDocId));

        // Bates absent: the DocId names the document instead.
        Assert.True(DatOptDocumentMatcher.AreDocumentKeysMatching(input.Key, null, optKeyFromBates));
    }

    [Property]
    public void BuildDatIndex_BatesCollidesWithDocId_ShouldPreferBatesInBothLookupDirections(DocumentKeyMatchPropertyInput input)
    {
        var batesRecord = new TestDatRecord(input.OtherKey, input.Key);
        var docIdRecord = new TestDatRecord(input.Key, input.OtherKey);
        var index = DatOptDocumentMatcher.BuildDatIndex(
            new[] { batesRecord, docIdRecord },
            record => record.DocId,
            record => record.Bates,
            includeBates: true);

        var lookupKey = input.VaryCase ? ToggleCase(input.Key) : input.Key;

        // Bates is inserted before DocIds, so the colliding key resolves to the Bates-owning record.
        Assert.Same(batesRecord, index[lookupKey]);

        // OPT-to-DAT direction: exact key and first-page form both resolve to the Bates record.
        Assert.Same(batesRecord, DatOptDocumentMatcher.FindMatchingDatByOptKey(index, lookupKey));
        Assert.Same(batesRecord, DatOptDocumentMatcher.FindMatchingDatByOptKey(index, lookupKey + DatOptDocumentMatcher.FirstPageSuffix));

        // DAT-to-OPT direction: the DAT key finds the OPT document keyed exactly or in
        // first-page form (the matcher appends the Page Suffix to the DAT key).
        var exactOptByKey = DatOptDocumentMatcher.BuildOptIndex(new[] { input.Key }, key => key);
        Assert.Equal(input.Key, DatOptDocumentMatcher.FindMatchingOptByKey(exactOptByKey, lookupKey));
        var firstPageKey = input.Key + DatOptDocumentMatcher.FirstPageSuffix;
        var firstPageOptByKey = DatOptDocumentMatcher.BuildOptIndex(new[] { firstPageKey }, key => key);
        Assert.Equal(firstPageKey, DatOptDocumentMatcher.FindMatchingOptByKey(firstPageOptByKey, lookupKey));
    }

    private static string ToggleCase(string value)
        => new(value.Select(c => char.IsLetter(c)
            ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))
            : c).ToArray());
}
