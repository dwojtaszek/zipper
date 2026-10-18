using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class ConcordanceSerializerTests
{
    [Theory]
    [InlineData("LF only", "line1\nline2", "þline1\nline2þ")]
    [InlineData("CR only", "line1\rline2", "þline1\rline2þ")]
    [InlineData("CRLF sequence", "line1\r\nline2", "þline1\r\nline2þ")]
    [InlineData("Mixed line endings", "line1\r\nline2\nline3\rline4", "þline1\r\nline2\nline3\rline4þ")]
    [InlineData("Consecutive newlines", "\r\n\r\n", "þ\r\n\r\nþ")]
    [InlineData("Leading and trailing newlines", "\ntext\r", "þ\ntext\rþ")]
    [InlineData("Empty field", "", "þþ")]
    [InlineData("Single line plain text", "plain", "þplainþ")]
    public void RenderRecord_PreservesEmbeddedNewlinesIntact(string scenario, string input, string expected)
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest());
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "FIELD" },
            Values = new[] { input },
        };

        var content = serializer.RenderRecord(record);

        Assert.True(string.Equals(expected, content, StringComparison.Ordinal), $"{scenario}: expected <{expected}> but got <{content}>.");
    }

    [Theory]
    [InlineData("LF header", "COL\nNAME", "þCOL\nNAMEþ")]
    [InlineData("CR header", "COL\rNAME", "þCOL\rNAMEþ")]
    [InlineData("CRLF header", "COL\r\nNAME", "þCOL\r\nNAMEþ")]
    public void RenderHeader_PreservesEmbeddedNewlinesIntact(string scenario, string headerInput, string expected)
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest());
        var columns = new List<string> { headerInput };

        var content = serializer.RenderHeader(columns);

        Assert.True(string.Equals(expected, content, StringComparison.Ordinal), $"{scenario}: expected <{expected}> but got <{content}>.");
    }

    [Fact]
    public void RenderRecord_WithConfiguredNewlineDelimiter_IgnoresNewlineDelimiterAndPreservesNewlines()
    {
        // REQ-083 & REQ-086 contract: Concordance format preserves embedded newlines intact
        // within quote qualifiers. Configured newline delimiters apply only to DAT format.
        var request = new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                NewlineDelimiter = "\u00ae", // ASCII 174 (®)
            },
        };
        var serializer = new ConcordanceSerializer(request);
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "MULTILINE" },
            Values = new[] { "first\r\nsecond\nthird\rfourth" },
        };

        var concordanceOutput = serializer.RenderRecord(record);

        // Newlines are NOT replaced with ®
        Assert.DoesNotContain("\u00ae", concordanceOutput, StringComparison.Ordinal);
        Assert.Equal("þfirst\r\nsecond\nthird\rfourthþ", concordanceOutput);

        // In contrast, DatSerializer replaces them with the newline delimiter
        var datSerializer = new DatSerializer(request);
        var datOutput = datSerializer.RenderRecord(record);
        Assert.Contains("\u00ae", datOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", datOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", datOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRecord_EmbeddedNewlinesAndQuoteDelimiter_DoublesQuotesAndPreservesNewlines()
    {
        // Default quote delimiter (þ) with embedded quotes and newlines
        var defaultSerializer = new ConcordanceSerializer(new FileGenerationRequest());
        var defaultRecord = new LoadFileRecord
        {
            Columns = new List<string> { "FIELD" },
            Values = new[] { "line1\r\nhasþquote\nline2" },
        };
        var defaultContent = defaultSerializer.RenderRecord(defaultRecord);
        Assert.Equal("þline1\r\nhasþþquote\nline2þ", defaultContent);

        // Custom quote delimiter (') with embedded quotes and newlines
        var customSerializer = new ConcordanceSerializer(new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                QuoteDelimiter = "'",
            },
        });
        var customRecord = new LoadFileRecord
        {
            Columns = new List<string> { "FIELD" },
            Values = new[] { "line1\r\nhas'quote\nline2" },
        };
        var customContent = customSerializer.RenderRecord(customRecord);
        Assert.Equal("'line1\r\nhas''quote\nline2'", customContent);
    }

    [Fact]
    public void RenderRecord_EmbeddedNewlinesAndColumnDelimiter_SeparatesFieldsAndPreservesNewlines()
    {
        // Default delimiters: column = ASCII 20 (\u0014), quote = ASCII 254 (þ)
        var defaultSerializer = new ConcordanceSerializer(new FileGenerationRequest());
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "COL1", "COL2" },
            Values = new[] { "val1\r\npart2", "val2\npart3" },
        };
        var defaultContent = defaultSerializer.RenderRecord(record);
        Assert.Equal("þval1\r\npart2þ\u0014þval2\npart3þ", defaultContent);

        // Custom delimiters: column = |, quote = '
        var customSerializer = new ConcordanceSerializer(new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = "|",
                QuoteDelimiter = "'",
            },
        });
        var customContent = customSerializer.RenderRecord(record);
        Assert.Equal("'val1\r\npart2'|'val2\npart3'", customContent);
    }

    [Fact]
    public void RenderRecord_FieldContainingColumnDelimiterAndNewlines_PreservesBothWithinQuotes()
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = "|",
                QuoteDelimiter = "'",
            },
        });
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "DATA" },
            Values = new[] { "line1|col\r\nline2" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("'line1|col\r\nline2'", content);
    }

    [Fact]
    public void RenderRecord_ValueMissingForColumn_EmitsEmptyQuotedField()
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest());
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "A", "B", "C" },
            Values = new[] { "val1\r\nval2" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("þval1\r\nval2þ\u0014þþ\u0014þþ", content);
    }

    [Fact]
    public void RenderRecord_NullValue_EmitsEmptyQuotedField()
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest());
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "A" },
            Values = new string[] { null! },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("þþ", content);
    }

    [Fact]
    public void RenderHeader_CustomDelimitersWithQuotesAndNewlines_EscapesQuotesAndPreservesNewlines()
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = ",",
                QuoteDelimiter = "'",
            },
        });
        var columns = new List<string> { "COL'1\r\n", "COL2\n" };

        var content = serializer.RenderHeader(columns);

        Assert.Equal("'COL''1\r\n','COL2\n'", content);
    }

    [Fact]
    public void FormatProperties_ReturnExpectedValues()
    {
        var serializer = new ConcordanceSerializer(new FileGenerationRequest());

        Assert.Equal("CONCORDANCE", serializer.FormatName);
        Assert.Equal(".dat", serializer.FileExtension);
    }
}
