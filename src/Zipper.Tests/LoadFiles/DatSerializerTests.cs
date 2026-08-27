using Xunit;

using Zipper.LoadFiles;

namespace Zipper.Tests;

public class DatSerializerTests
{
    [Fact]
    public void RenderHeader_ProducesDelimitedHeader()
    {
        var serializer = new DatSerializer();
        var columns = new List<string> { "DOCID", "FILEPATH", "CUSTODIAN" };

        var content = serializer.RenderHeader(columns);

        Assert.Contains("DOCID", content, StringComparison.Ordinal);
        Assert.Contains("FILEPATH", content, StringComparison.Ordinal);
        Assert.Contains("CUSTODIAN", content, StringComparison.Ordinal);
        Assert.Contains("\x14", content, StringComparison.Ordinal); // column delimiter
    }

    [Fact]
    public void RenderRecord_ProducesDelimitedRow()
    {
        var serializer = new DatSerializer();
        var columns = new List<string> { "DOCID", "FILEPATH" };
        var record = new LoadFileRecord
        {
            Columns = columns,
            Values = new[] { "DOC001", "folder/file.pdf" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Contains("DOC001", content, StringComparison.Ordinal);
        Assert.Contains("folder/file.pdf", content, StringComparison.Ordinal);
        Assert.Contains("\x14", content, StringComparison.Ordinal); // column delimiter
    }

    [Fact]
    public void RenderRecord_EscapesQuoteDelimiter()
    {
        var serializer = new DatSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "VALUE" },
            Values = new[] { "has\xfequote" },
        };

        var content = serializer.RenderRecord(record);

        // Quote delimiter should be doubled
        Assert.Contains("\xfe\xfe", content, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRecord_ReplacesNewlines()
    {
        var serializer = new DatSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "TEXT" },
            Values = new[] { "line1\nline2" },
        };

        var content = serializer.RenderRecord(record);

        Assert.DoesNotContain("\n", content, StringComparison.Ordinal);
        Assert.Contains("\xae", content, StringComparison.Ordinal);
    }

    [Fact]
    public void NoQuoteMode_OmitsQuoteDelimiters()
    {
        var serializer = new DatSerializer(columnDelimiter: '|', quoteDelimiter: '\0');
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "A", "B" },
            Values = new[] { "x", "y" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("x|y", content);
    }

    [Fact]
    public void RenderHeader_EscapesSpecialCharacters()
    {
        var serializer = new DatSerializer();
        var columns = new List<string> { "has\xfeþ", "has\nline" };

        var content = serializer.RenderHeader(columns);

        // Quote delimiter should be doubled in header fields
        Assert.Contains("\xfe\xfe", content, StringComparison.Ordinal);
        // Newlines should be replaced with the configured newline delimiter
        Assert.DoesNotContain("\n", content, StringComparison.Ordinal);
        Assert.Contains("\xae", content, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHeader_DoesNotEscapePlainColumns()
    {
        var serializer = new DatSerializer();
        var columns = new List<string> { "DOCID", "FILEPATH" };

        var content = serializer.RenderHeader(columns);

        Assert.Equal("þDOCIDþ\x14þFILEPATHþ", content);
    }

    [Fact]
    public void RenderRecord_ShortValues_RendersTrailingEmptyFields()
    {
        var serializer = new DatSerializer(columnDelimiter: '|', quoteDelimiter: '\0');
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "A", "B", "C" },
            Values = new[] { "x", "y" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("x|y|", content);
    }
}
