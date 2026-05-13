using System.Text;

using Xunit;

using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

public class DatSerializerTests
{
    [Fact]
    public async Task WriteHeaderAsync_ProducesDelimitedHeader()
    {
        var serializer = new DatSerializer();
        using var stream = new MemoryStream();
        var columns = new List<string> { "DOCID", "FILEPATH", "CUSTODIAN" };

        await serializer.WriteHeaderAsync(stream, columns);
        await serializer.FlushAsync(stream);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("DOCID", content);
        Assert.Contains("FILEPATH", content);
        Assert.Contains("CUSTODIAN", content);
        Assert.Contains("\x14", content); // column delimiter
    }

    [Fact]
    public async Task WriteRecordAsync_ProducesDelimitedRow()
    {
        var serializer = new DatSerializer();
        using var stream = new MemoryStream();
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

        await serializer.WriteRecordAsync(stream, record);
        await serializer.FlushAsync(stream);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("DOC001", content);
        Assert.Contains("folder/file.pdf", content);
        Assert.Contains("\x14", content); // column delimiter
    }

    [Fact]
    public async Task WriteRecordAsync_EscapesQuoteDelimiter()
    {
        var serializer = new DatSerializer();
        using var stream = new MemoryStream();
        var columns = new List<string> { "VALUE" };
        var record = new LoadFileRecord
        {
            Columns = columns,
            Values = new Dictionary<string, string>
            {
                ["VALUE"] = "has\xfequote",
            },
        };

        await serializer.WriteRecordAsync(stream, record);
        await serializer.FlushAsync(stream);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());

        // Quote delimiter should be doubled
        Assert.Contains("\xfe\xfe", content);
    }

    [Fact]
    public async Task WriteRecordAsync_ReplacesNewlines()
    {
        var serializer = new DatSerializer();
        using var stream = new MemoryStream();
        var columns = new List<string> { "TEXT" };
        var record = new LoadFileRecord
        {
            Columns = columns,
            Values = new Dictionary<string, string>
            {
                ["TEXT"] = "line1\nline2",
            },
        };

        await serializer.WriteRecordAsync(stream, record);
        await serializer.FlushAsync(stream);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("\n", content.TrimEnd('\r', '\n'));
        Assert.Contains("\xae", content);
    }

    [Fact]
    public async Task NoQuoteMode_OmitsQuoteDelimiters()
    {
        var serializer = new DatSerializer(columnDelimiter: '|', quoteDelimiter: '\0');
        using var stream = new MemoryStream();
        var columns = new List<string> { "A", "B" };
        var record = new LoadFileRecord
        {
            Columns = columns,
            Values = new Dictionary<string, string> { ["A"] = "x", ["B"] = "y" },
        };

        await serializer.WriteRecordAsync(stream, record);
        await serializer.FlushAsync(stream);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());
        Assert.StartsWith("x|y", content);
    }
}
