using System.Text;
using Xunit;
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
        this.tempDir = Path.Combine(Path.GetTempPath(), $"zipper_{Guid.NewGuid():N}");
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
                Formats = new List<LoadFileFormat> { LoadFileFormat.Dat },
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
        if (content.StartsWith('﻿'))
        {
            content = content.Substring(1);
        }

        var firstLine = content.Split('\n')[0].TrimEnd('\r');

        var expectedExactHeader = $"^{expectedHeader.Replace("|", "^|^")}^";
        Assert.Equal(expectedExactHeader, firstLine);
    }

    [Theory]
    [InlineData("UPPERCASE", "^DOCID^|^CUSTODIANNAME^")]
    [InlineData("lowercase", "^docid^|^custodianname^")]
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
        if (content.StartsWith('﻿'))
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

    [Theory]
    [InlineData("MD5Hash", "PascalCase", "Md5Hash")]
    [InlineData("SHA1Hash", "PascalCase", "Sha1Hash")]
    [InlineData("SHA256Hash", "PascalCase", "Sha256Hash")]
    [InlineData("OCRPath", "PascalCase", "OcrPath")]
    [InlineData("TimeZone", "PascalCase", "TimeZone")]
    [InlineData("RecordType", "PascalCase", "RecordType")]
    [InlineData("FileExt", "PascalCase", "FileExt")]
    [InlineData("FileName", "PascalCase", "FileName")]
    [InlineData("MD5Hash", "snake_case", "md5_hash")]
    [InlineData("SHA1Hash", "snake_case", "sha1_hash")]
    [InlineData("SHA256Hash", "snake_case", "sha256_hash")]
    [InlineData("OCRPath", "snake_case", "ocr_path")]
    [InlineData("TimeZone", "snake_case", "time_zone")]
    [InlineData("RecordType", "snake_case", "record_type")]
    [InlineData("FileExt", "snake_case", "file_ext")]
    [InlineData("FileName", "snake_case", "file_name")]
    [InlineData("MD5HASH", "UPPERCASE", "MD5HASH")]
    [InlineData("SHA1HASH", "UPPERCASE", "SHA1HASH")]
    [InlineData("SHA256HASH", "UPPERCASE", "SHA256HASH")]
    [InlineData("OCRPATH", " lowercase ", "ocrpath")]
    [InlineData("TIMEZONE", "lowercase", "timezone")]
    [InlineData("RECORDTYPE", "lowercase", "recordtype")]
    [InlineData("FILEEXT", "lowercase", "fileext")]
    [InlineData("FILENAME", "lowercase", "filename")]
    public void ApplyConvention_StandardLoadFileFieldNames_ShouldTransformAllConventions(
        string input,
        string convention,
        string expected)
    {
        var result = NamingConventionHelper.ApplyConvention(input, convention);
        Assert.Equal(expected, result);
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
    public void LoadFileModule_ShouldRejectInvalidFormatInLoadFileOnlyMode()
    {
        // Phase 2: the loadfile-only DAT/OPT restriction moved to LoadFileModule.TryBuild.
        var module = new Zipper.Cli.Modules.LoadFileModule();
        Assert.True(module.TryApply("--loadfile-only", null));
        Assert.True(module.TryApply("--load-file-format", "csv"));
        Assert.False(module.TryBuild(0, null, false, null, null, false, out _));
    }
}
