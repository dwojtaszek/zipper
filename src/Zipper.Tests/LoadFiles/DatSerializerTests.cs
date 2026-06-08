using Xunit;

using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

public class DatSerializerTests
{
    [Fact]
    public void RenderHeader_ProducesDelimitedHeader()
    {
        var serializer = new DatSerializer();
        var columns = new List<string> { "DOCID", "FILEPATH", "CUSTODIAN" };

        var content = serializer.RenderHeader(columns);

        Assert.Contains("DOCID", content);
        Assert.Contains("FILEPATH", content);
        Assert.Contains("CUSTODIAN", content);
        Assert.Contains("\x14", content); // column delimiter
    }

    [Fact]
    public void RenderRecord_ProducesDelimitedRow()
    {
        var serializer = new DatSerializer();
        var columns = new List<string> { "DOCID", "FILEPATH" };
        var record = new LoadFileRecord
        {
            Columns = columns,
            Values = new Dictionary<string, string>
            {
                ["DOCID"] = "DOC001",
                ["FILEPATH"] = "folder/file.pdf",
            },
        };

        var content = serializer.RenderRecord(record);

        Assert.Contains("DOC001", content);
        Assert.Contains("folder/file.pdf", content);
        Assert.Contains("\x14", content); // column delimiter
    }

    [Fact]
    public void RenderRecord_EscapesQuoteDelimiter()
    {
        var serializer = new DatSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "VALUE" },
            Values = new Dictionary<string, string>
            {
                ["VALUE"] = "has\xfequote",
            },
        };

        var content = serializer.RenderRecord(record);

        // Quote delimiter should be doubled
        Assert.Contains("\xfe\xfe", content);
    }

    [Fact]
    public void RenderRecord_ReplacesNewlines()
    {
        var serializer = new DatSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "TEXT" },
            Values = new Dictionary<string, string>
            {
                ["TEXT"] = "line1\nline2",
            },
        };

        var content = serializer.RenderRecord(record);

        Assert.DoesNotContain("\n", content);
        Assert.Contains("\xae", content);
    }

    [Fact]
    public void NoQuoteMode_OmitsQuoteDelimiters()
    {
        var serializer = new DatSerializer(columnDelimiter: '|', quoteDelimiter: '\0');
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "A", "B" },
            Values = new Dictionary<string, string> { ["A"] = "x", ["B"] = "y" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("x|y", content);
    }

    [Fact]
    public void RenderRecord_MissingValue_RendersEmptyField()
    {
        var serializer = new DatSerializer(columnDelimiter: '|', quoteDelimiter: '\0');
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "A", "MISSING", "B" },
            Values = new Dictionary<string, string> { ["A"] = "x", ["B"] = "y" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("x||y", content);
    }
}
