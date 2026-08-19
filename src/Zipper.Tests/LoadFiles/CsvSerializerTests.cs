using Xunit;

using Zipper.LoadFiles;

namespace Zipper.Tests;

public class CsvSerializerTests
{
    [Fact]
    public void RenderRecord_PlainField_Unchanged()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { "Alice" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("Alice", content);
    }

    [Fact]
    public void RenderRecord_FieldWithComma_Quoted()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { "Smith, John" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("\"Smith, John\"", content);
    }

    [Fact]
    public void RenderRecord_FieldWithQuote_DoubledAndQuoted()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { "She said \"hi\"" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("\"She said \"\"hi\"\"\"", content);
    }

    [Fact]
    public void RenderRecord_FieldWithEmbeddedNewline_Quoted()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { "line1\nline2" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("\"line1\nline2\"", content);
    }

    [Fact]
    public void RenderRecord_FieldWithCarriageReturn_Quoted()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { "a\rb" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("\"a\rb\"", content);
    }

    [Fact]
    public void RenderRecord_MixedDelimitersAndQuotes_AllEscaped()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { "Smith, \"John\"\nNew" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("\"Smith, \"\"John\"\"\nNew\"", content);
    }

    [Fact]
    public void RenderRecord_EmptyField_EmitsNothing()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { string.Empty },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void RenderRecord_ValueMissingForColumn_EmitsEmpty()
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "A", "B" },
            Values = new[] { "1" },
        };

        var content = serializer.RenderRecord(record);

        Assert.Equal("1,", content);
    }

    [Fact]
    public void RenderHeader_ColumnWithComma_NotQuoted()
    {
        var serializer = new CsvSerializer();
        var columns = new List<string> { "A,B", "C" };

        var content = serializer.RenderHeader(columns);

        Assert.Equal("A,B,C", content);
    }

    [Fact]
    public void FileExtension_ReturnsCsv()
    {
        var serializer = new CsvSerializer();

        Assert.Equal(".csv", serializer.FileExtension);
    }

    [Fact]
    public void FormatName_ReturnsCsv()
    {
        var serializer = new CsvSerializer();

        Assert.Equal("CSV", serializer.FormatName);
    }
}
