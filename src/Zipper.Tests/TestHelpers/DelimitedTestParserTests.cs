using Xunit;
using Xunit.Sdk;

namespace Zipper.Tests.TestHelpers;

public class DelimitedTestParserTests
{
    [Fact]
    public void ParseLine_CsvSimple_ReturnsCorrectFields()
    {
        var line = "CONTROL NUMBER,FILE PATH,BEGATTACH,ENDATTACH,PARENTDOCID";
        var fields = DelimitedTestParser.ParseLine(line, ',', '"');

        Assert.Equal(new[] { "CONTROL NUMBER", "FILE PATH", "BEGATTACH", "ENDATTACH", "PARENTDOCID" }, fields);
    }

    [Fact]
    public void ParseLine_CsvWithQuotesAndCommas_HandlesQuotesCorrectly()
    {
        var line = "1,\"hello, world\",2";
        var fields = DelimitedTestParser.ParseLine(line, ',', '"');

        Assert.Equal(new[] { "1", "hello, world", "2" }, fields);
    }

    [Fact]
    public void ParseLine_CsvWithDoubledQuotes_UnescapesQuotes()
    {
        var line = "1,\"say \"\"hi\"\"\",2";
        var fields = DelimitedTestParser.ParseLine(line, ',', '"');

        Assert.Equal(new[] { "1", "say \"hi\"", "2" }, fields);
    }

    [Fact]
    public void ParseLine_CsvEmptyFields_PreservesEmptyStrings()
    {
        var line = "DOC00000001,folder/doc1.eml,DOC00000001,DOC00000001_A001,";
        var fields = DelimitedTestParser.ParseLine(line, ',', '"');

        Assert.Equal(new[] { "DOC00000001", "folder/doc1.eml", "DOC00000001", "DOC00000001_A001", string.Empty }, fields);
    }

    [Fact]
    public void ParseLine_ConcordanceDatLine_SplitsAndStripsQuotes()
    {
        var line = "þBEGATTYþ\u0014þENDATTYþ\u0014þCONTROLNUMBERþ";
        var fields = DelimitedTestParser.ParseLine(line, '\u0014', 'þ');

        Assert.Equal(new[] { "BEGATTY", "ENDATTY", "CONTROLNUMBER" }, fields);
    }

    [Fact]
    public void ParseLine_ConcordanceDatEmptyFields_ReturnsEmptyStrings()
    {
        var line = "þþ\u0014þDOC00000001þ\u0014þþ";
        var fields = DelimitedTestParser.ParseLine(line, '\u0014', 'þ');

        Assert.Equal(new[] { string.Empty, "DOC00000001", string.Empty }, fields);
    }

    [Fact]
    public void ParseLine_ConcordanceDoubledQuotes_Unescapes()
    {
        var line = "þAþþBþ\u0014þCþ";
        var fields = DelimitedTestParser.ParseLine(line, '\u0014', 'þ');

        Assert.Equal(new[] { "AþB", "C" }, fields);
    }

    [Fact]
    public void ParseLine_NullLine_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DelimitedTestParser.ParseLine(null!, ','));
    }

    [Fact]
    public void ParseLine_EmptyLine_ReturnsSingleEmptyField()
    {
        var fields = DelimitedTestParser.ParseLine(string.Empty, ',');
        Assert.Equal(new[] { string.Empty }, fields);
    }

    [Fact]
    public void ParseRecord_ValidHeadersAndValues_ReturnsDictionary()
    {
        var headers = new[] { "COL1", "COL2", "COL3" };
        var line = "A,B,C";
        var record = DelimitedTestParser.ParseRecord(headers, line, ',', '"');

        Assert.Equal("A", record["COL1"]);
        Assert.Equal("B", record["COL2"]);
        Assert.Equal("C", record["COL3"]);
    }

    [Fact]
    public void ParseRecord_HeaderCountMismatch_ThrowsXunitException()
    {
        var headers = new[] { "COL1", "COL2" };
        var line = "A,B,C";

        Assert.Throws<EqualException>(() => DelimitedTestParser.ParseRecord(headers, line, ',', '"'));
    }

    [Fact]
    public void ParseRecord_NullHeaders_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DelimitedTestParser.ParseRecord(null!, "A,B", ','));
    }

    [Fact]
    public void ParseRecord_NullLine_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DelimitedTestParser.ParseRecord(new[] { "COL1" }, null!, ','));
    }

    [Fact]
    public void ParseLine_WithLeadingUtf8Bom_StripsBom()
    {
        var line = "\uFEFFþBEGATTYþ\u0014þENDATTYþ";
        var fields = DelimitedTestParser.ParseLine(line, '\u0014', 'þ');

        Assert.Equal(new[] { "BEGATTY", "ENDATTY" }, fields);
    }

    [Fact]
    public void ParseLine_ConcordanceEmbeddedDelimiterInQuotes_PreservesDelimiter()
    {
        var line = "þA\u0014Bþ\u0014þCþ";
        var fields = DelimitedTestParser.ParseLine(line, '\u0014', 'þ');

        Assert.Equal(new[] { "A\u0014B", "C" }, fields);
    }

    [Fact]
    public void ParseLine_UnterminatedQuote_ThrowsFormatException()
    {
        var line = "1,\"unterminated,2";
        Assert.Throws<FormatException>(() => DelimitedTestParser.ParseLine(line, ',', '"'));
    }
}
