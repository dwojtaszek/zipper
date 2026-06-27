using System.Text;
using Xunit;
using Zipper.Cli;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;
using Zipper.Utils;

namespace Zipper.Tests;

public class FieldNamingTests : IDisposable
{
    private readonly string tempDir;

    public FieldNamingTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    private FileGenerationRequest CreateRequest(string namingConvention)
    {
        var profile = new ColumnProfile
        {
            Name = "test",
            FieldNamingConvention = namingConvention,
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DocID", Type = "identifier", Required = true },
                new() { Name = "CustodianName", Type = "text" }
            }
        };

        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.tempDir,
                FileCount = 1,
                FileType = "pdf"
            },
            LoadFile = new LoadFileConfig
            {
                LoadFileFormat = LoadFileFormat.Dat,
                Encoding = "UTF-8"
            },
            Metadata = new MetadataConfig
            {
                WithMetadata = true,
                ColumnProfile = profile
            },
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = "|",
                QuoteDelimiter = "^",
                EndOfLine = "LF"
            }
        };
    }

    [Theory]
    [InlineData("UPPERCASE", "DOCID|CUSTODIANNAME")]
    [InlineData("lowercase", "docid|custodianname")]
    [InlineData("PascalCase", "DocId|CustodianName")]
    [InlineData("snake_case", "doc_id|custodian_name")]
    public async Task ProfileDrivenDatWriter_ShouldRespectFieldNamingConvention(string convention, string expectedHeader)
    {
        ArgumentNullException.ThrowIfNull(expectedHeader);

        // Arrange
        var request = this.CreateRequest(convention);
        var writer = new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly);
        var stream = new MemoryStream();

        // Act
        await writer.WriteAsync(stream, request, new List<FileData>());

        // Assert
        var content = Encoding.UTF8.GetString(stream.ToArray());
        if (content.StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            content = content.Substring(1);
        }

        var firstLine = content.Split('\n')[0].TrimEnd('\r');

        var expectedExactHeader = $"^{expectedHeader.Replace("|", "^|^")}^";
        Assert.Equal(expectedExactHeader, firstLine);
    }

    [Theory]
    [InlineData("UPPERCASE", "^CONTROL NUMBER^|^FILE PATH^|^CUSTODIAN^|^DATE SENT^|^AUTHOR^|^FILE SIZE^")]
    [InlineData("lowercase", "^control number^|^file path^|^custodian^|^date sent^|^author^|^file size^")]
    public async Task DatWriter_ShouldRespectFieldNamingConvention(string convention, string expectedHeader)
    {
        ArgumentNullException.ThrowIfNull(expectedHeader);

        // Arrange
        var request = this.CreateRequest(convention);
        var writer = new DatComposingWriter();
        var fileData = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem { Index = 1, FolderNumber = 1, FilePathInZip = "test.pdf" }
            }
        };
        var stream = new MemoryStream();

        // Act
        await writer.WriteAsync(stream, request, fileData);

        // Assert
        var content = Encoding.UTF8.GetString(stream.ToArray());
        if (content.StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            content = content.Substring(1);
        }

        var firstLine = content.Split('\n')[0].TrimEnd('\r');
        Assert.Equal(expectedHeader, firstLine);
    }

    [Fact]
    public void NamingConventionHelper_ShouldCollapseConsecutiveSeparators()
    {
        var input = "Control - - Number";
        var result = NamingConventionHelper.ApplyConvention(input, "snake_case");
        Assert.Equal("control_number", result);
    }

    [Fact]
    public void NamingConventionHelper_PascalCase_ShouldHaveLowAllocations()
    {
        var input = "Some_Very_Long_Word_To_Convert_To_Pascal_Case_With_Many_Words";
        // Warm up
        _ = NamingConventionHelper.ToPascalCase(input);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = NamingConventionHelper.ToPascalCase(input);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long allocated = after - before;

        // Assert that the allocated bytes are less than 1,500,000 bytes.
        // The current unoptimized code allocates more than this due to Substring + ToLowerInvariant per word.
        Assert.True(allocated < 1500000, $"Allocated too much: {allocated} bytes");
    }

    [Fact]
    public void NamingConventionHelper_ToPascalCase_WithWordLongerThan256Characters_ShouldSucceed()
    {
        var input = "A" + new string('b', 300);
        var result = NamingConventionHelper.ToPascalCase(input);
        Assert.Equal("A" + new string('b', 300), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NamingConventionHelper_ToPascalCase_WithNullOrEmpty_ReturnsOriginal(string? input)
    {
        var result = NamingConventionHelper.ToPascalCase(input!);
        Assert.Equal(input, result);
    }

    [Fact]
    public void ColumnProfileLoader_ShouldThrowOnInvalidConvention()
    {
        var profile = new ColumnProfile
        {
            Name = "invalid-test",
            FieldNamingConvention = "INVALID_CONVENTION",
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DocID", Type = "identifier" }
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ColumnProfileLoader.Validate(profile));
        Assert.Contains("has an invalid fieldNamingConvention", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CliValidator_ShouldRejectInvalidFormatInLoadFileOnlyMode()
    {
        var parsed = new ParsedArguments
        {
            LoadfileOnly = true,
            LoadFileFormat = "csv",
            Count = 100,
            OutputPathStr = this.tempDir,
            FileType = "pdf"
        };

        var isValid = CliValidator.Validate(parsed);
        Assert.False(isValid);
    }
}
