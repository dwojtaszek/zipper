using Xunit;
using Zipper.Validation;

namespace Zipper.Tests;

public sealed class DatOptDocumentMatcherTests
{
    private sealed record TestDatRecord(string DocId, string Bates);

    [Fact]
    public void ResolveDatHeaderSchema_WithBatesNumber_UsesItForColumnAndIdentity()
    {
        var schema = DatOptDocumentMatcher.ResolveDatHeaderSchema(new List<string> { "DOCID", "BATES_NUMBER", "BEGBATES" });

        Assert.Equal(1, schema.BatesColumnIndex);
        Assert.Equal(1, schema.BatesIdentityIndex);
    }

    [Fact]
    public void ResolveDatHeaderSchema_WithoutBatesColumn_FallsBackToBegBatesForIdentity()
    {
        var schema = DatOptDocumentMatcher.ResolveDatHeaderSchema(new List<string> { "DOCID", "BEGBATES" });

        Assert.Equal(-1, schema.BatesColumnIndex);
        Assert.Equal(1, schema.BatesIdentityIndex);
    }

    [Fact]
    public void ResolveDatHeaderSchema_WithoutAnyBates_ReportsNoIdentity()
    {
        var schema = DatOptDocumentMatcher.ResolveDatHeaderSchema(new List<string> { "DOCID" });

        Assert.Equal(-1, schema.BatesColumnIndex);
        Assert.Equal(-1, schema.BatesIdentityIndex);
    }

    [Theory]
    [InlineData("Y", true)]
    [InlineData("y", true)]
    [InlineData("1", true)]
    [InlineData(" Y ", true)]
    [InlineData("", false)]
    [InlineData("N", false)]
    [InlineData("0", false)]
    public void IsOptDocumentBreak_ClassifiesDocBreakField(string field, bool expected)
    {
        Assert.Equal(expected, DatOptDocumentMatcher.IsOptDocumentBreak(field));
    }

    [Theory]
    [InlineData("ABC,VOL001,img.tif,Y,,,1", true, 7)]
    [InlineData("ABC,VOL001,img.tif,1,,,1", true, 7)]
    [InlineData("ABC,VOL001,img.tif,,,,", false, 7)]
    [InlineData("ABC,VOL001,img.tif", false, 3)]
    public void ParseOptPageRow_SplitsColumnsAndClassifiesDocBreak(string line, bool expectedBreak, int expectedColumns)
    {
        var row = DatOptDocumentMatcher.ParseOptPageRow(line);

        Assert.Equal(expectedColumns, row.Columns.Length);
        Assert.Equal(expectedBreak, row.IsDocumentBreak);
    }

    [Fact]
    public void FindMatchingOptByKey_MatchesFirstPageSuffixForm()
    {
        var optByKey = DatOptDocumentMatcher.BuildOptIndex(new[] { "ABC_001" }, key => key);

        Assert.Equal("ABC_001", DatOptDocumentMatcher.FindMatchingOptByKey(optByKey, "ABC"));
        Assert.Equal("ABC_001", DatOptDocumentMatcher.FindMatchingOptByKey(optByKey, "ABC_001"));
        Assert.Null(DatOptDocumentMatcher.FindMatchingOptByKey(optByKey, "XYZ"));
        Assert.Null(DatOptDocumentMatcher.FindMatchingOptByKey(optByKey, string.Empty));
    }

    [Fact]
    public void FindMatchingDatByOptKey_StripsFirstPageSuffix()
    {
        var datByKey = DatOptDocumentMatcher.BuildDatIndex(
            new[] { new TestDatRecord("ABC", "ABC") },
            record => record.DocId,
            record => record.Bates,
            includeBates: true);

        Assert.Equal("ABC", DatOptDocumentMatcher.FindMatchingDatByOptKey(datByKey, "ABC_001")?.DocId);
        Assert.Equal("ABC", DatOptDocumentMatcher.FindMatchingDatByOptKey(datByKey, "ABC")?.DocId);
        Assert.Null(DatOptDocumentMatcher.FindMatchingDatByOptKey(datByKey, "_001"));
        Assert.Null(DatOptDocumentMatcher.FindMatchingDatByOptKey(datByKey, "XYZ_001"));
    }

    [Fact]
    public void FindMatchingDatByOptKey_WhenDatIdentityItselfEndsInSuffix_MatchesOwnKey()
    {
        // REQ-239: a Bates identity ending in _001 is the document's own key, not a first-page form.
        var datByKey = DatOptDocumentMatcher.BuildDatIndex(
            new[] { new TestDatRecord("ABC_001", "ABC_001") },
            record => record.DocId,
            record => record.Bates,
            includeBates: true);

        Assert.Equal("ABC_001", DatOptDocumentMatcher.FindMatchingDatByOptKey(datByKey, "ABC_001")?.DocId);
    }

    [Fact]
    public void BuildDatIndex_PrefersBatesOverRepeatingDocId()
    {
        var records = new[]
        {
            new TestDatRecord("SHARED", "BATES1"),
            new TestDatRecord("SHARED2", "SHARED"),
        };

        var index = DatOptDocumentMatcher.BuildDatIndex(records, record => record.DocId, record => record.Bates, includeBates: true);

        Assert.Equal("SHARED", index["SHARED"].Bates);
    }

    [Theory]
    [InlineData("ABC", "ABC", true)]
    [InlineData("ABC", "ABC_001", true)]
    [InlineData("ABC", "ABC_002", false)]
    [InlineData("ABC", "XYZ", false)]
    public void DocumentKeyMatches_AllowsFirstPageSuffix(string datKey, string optKey, bool expected)
    {
        Assert.Equal(expected, DatOptDocumentMatcher.DocumentKeyMatches(datKey, optKey));
    }

    [Fact]
    public void AreDocumentKeysMatching_PrefersBatesOverDocId()
    {
        Assert.True(DatOptDocumentMatcher.AreDocumentKeysMatching("DOC1", "ABC", "ABC_001"));
        Assert.False(DatOptDocumentMatcher.AreDocumentKeysMatching("DOC1", "ABC", "DOC1"));
        Assert.True(DatOptDocumentMatcher.AreDocumentKeysMatching("DOC1", null, "DOC1_001"));
        Assert.False(DatOptDocumentMatcher.AreDocumentKeysMatching(null, null, "DOC1"));
        Assert.False(DatOptDocumentMatcher.AreDocumentKeysMatching("DOC1", null, string.Empty));
    }
}
