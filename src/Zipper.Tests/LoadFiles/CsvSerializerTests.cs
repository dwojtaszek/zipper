using Xunit;

using Zipper.LoadFiles;

namespace Zipper.Tests;

public class CsvSerializerTests
{
    [Theory]
    [InlineData("RenderRecord_PlainField_Unchanged", "Alice", "Alice")]
    [InlineData("RenderRecord_FieldWithComma_Quoted", "Smith, John", "\"Smith, John\"")]
    [InlineData("RenderRecord_FieldWithQuote_DoubledAndQuoted", "She said \"hi\"", "\"She said \"\"hi\"\"\"")]
    [InlineData("RenderRecord_FieldWithEmbeddedNewline_Quoted", "line1\nline2", "\"line1\nline2\"")]
    [InlineData("RenderRecord_FieldWithCarriageReturn_Quoted", "a\rb", "\"a\rb\"")]
    [InlineData("RenderRecord_MixedDelimitersAndQuotes_AllEscaped", "Smith, \"John\"\nNew", "\"Smith, \"\"John\"\"\nNew\"")]
    [InlineData("RenderRecord_EmptyField_EmitsNothing", "", "")]
    public void RenderRecord_EscapingRules(string scenario, string input, string expected)
    {
        var serializer = new CsvSerializer();
        var record = new LoadFileRecord
        {
            Columns = new List<string> { "Name" },
            Values = new[] { input },
        };

        var content = serializer.RenderRecord(record);

        Assert.True(string.Equals(expected, content, StringComparison.Ordinal), $"{scenario}: expected <{expected}> but got <{content}>.");
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
