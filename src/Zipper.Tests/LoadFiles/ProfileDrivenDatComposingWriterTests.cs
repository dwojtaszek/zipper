using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;

namespace Zipper.Tests;

public class ProfileDrivenDatComposingWriterTests
{
    private static ColumnProfile MakeMinimalProfile()
    {
        return new ColumnProfile
        {
            Name = "minimal",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "FILEPATH", Type = "text", Required = true },
            },
        };
    }

    private static FileGenerationRequest MakeRequest(ColumnProfile profile, int fileCount = 3, int? custodianCountOverride = null)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = fileCount, FileType = "pdf" },
            Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42, CustodianCountOverride = custodianCountOverride },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
        };
    }

    private static async Task<string> CaptureOutputAsync(FileGenerationRequest request)
    {
        var writer = new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly);
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new List<FileData>()).ConfigureAwait(false);
        stream.Position = 0;
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_ProducesHeaderFromProfileColumns()
    {
        var profile = MakeMinimalProfile();
        var request = MakeRequest(profile);
        var content = await CaptureOutputAsync(request);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + 3 data rows
        Assert.Equal(4, lines.Length);
        Assert.Contains("DOCID", lines[0], StringComparison.Ordinal);
        Assert.Contains("FILEPATH", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_DataRows_WrappedInQuoteDelimiter()
    {
        var profile = MakeMinimalProfile();
        var request = MakeRequest(profile, 1);
        var content = await CaptureOutputAsync(request);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Each data field should be wrapped in the quote delimiter (\u00fe)
        Assert.Contains("\u00fe", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_FieldContainingQuoteDelimiter_IsDoubled()
    {
        var profile = new ColumnProfile
        {
            Name = "escape-test",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>
(StringComparer.Ordinal)
            {
                ["quoteValues"] = new DataSourceConfig
                {
                    Values = new List<string> { "val\u00feSpecial" },
                },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "QUOTEFIELD", Type = "coded", DataSource = "quoteValues", EmptyPercentage = 0 },
            },
        };

        var request = MakeRequest(profile, 1);
        var content = await CaptureOutputAsync(request);

        // \u00fe inside the field value must be doubled to \u00fe\u00fe per Concordance escaping
        Assert.Contains("val\u00fe\u00feSpecial", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// When CustodianCountOverride is set on the request, ProfileDrivenDatWriter
    /// must pass it to DataGenerator so custodian values are bounded to that count.
    /// </summary>
    [Fact]
    public async Task WriteAsync_WithCustodianCountOverride_LimitsCustodianValuesToOverrideCount()
    {
        // Standard profile has 25 custodians; we override to 2
        var profile = BuiltInProfiles.Standard;
        var request = MakeRequest(profile, fileCount: 200, custodianCountOverride: 2);
        var content = await CaptureOutputAsync(request);

        // Parse CUSTODIAN column values from all data rows
        var lines = content.Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries);
        var headerLine = lines[0];
        var colDelim = '\u0014';
        var quote = '\u00fe';
        var headers = headerLine.Split(colDelim)
            .Select(h => h.Trim(quote))
            .ToList();
        var custodianIdx = headers.IndexOf("CUSTODIAN");
        Assert.True(custodianIdx >= 0, "CUSTODIAN column not found in profile output");

        var custodianValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(colDelim);
            if (custodianIdx < fields.Length)
            {
                var val = fields[custodianIdx].Trim(quote);
                if (!string.IsNullOrEmpty(val))
                {
                    custodianValues.Add(val);
                }
            }
        }

        // With override=2, must only see Custodian_1 and/or Custodian_2
        Assert.True(
            custodianValues.Count <= 2,
            $"Expected at most 2 distinct custodians but found {custodianValues.Count}: {string.Join(", ", custodianValues)}");
        foreach (var v in custodianValues)
        {
            Assert.Matches(@"^Custodian_[12]$", v);
        }
    }

    /// <summary>
    /// Tests that a chaos encoding anomaly successfully injects invalid bytes on both the header boundary
    /// and the final data record boundary.
    /// </summary>
    [Fact]
    public async Task WriteAsync_WithChaosEncoding_TargetsHeaderAndLastLine_InjectsInvalidBytesAndCreatesAudit()
    {
        var profile = MakeMinimalProfile();
        var request = MakeRequest(profile);
        request.Output = request.Output with { FileCount = 3 };

        using var baseStream = new MemoryStream();
        await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(baseStream, request, []);
        var baseBytes = baseStream.ToArray();

        var chaosEngine = new ChaosEngine(
            totalLines: 4,
            chaosAmount: "100%",
            chaosTypes: "encoding",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        using var chaosStream = new MemoryStream();
        await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(chaosStream, request, [], chaosEngine);
        var chaosBytes = chaosStream.ToArray();

        var headerAnomaly = chaosEngine.Anomalies.FirstOrDefault(a => string.Equals(a.LineNumber, "Boundary 1-2", StringComparison.Ordinal));
        var lastLineAnomaly = chaosEngine.Anomalies.FirstOrDefault(a => string.Equals(a.LineNumber, "Boundary 4-5", StringComparison.Ordinal));

        Assert.NotNull(headerAnomaly);
        Assert.NotNull(lastLineAnomaly);
        Assert.True(chaosBytes.Length > baseBytes.Length, "Encoding chaos should inject extra bytes");
    }

    [Fact]
    public async Task WriteAsync_WithChaosEngine_AltersOutputAndPreservesHeaders()
    {
        var profile = MakeMinimalProfile();
        var request = MakeRequest(profile, 3);
        request.LoadfileOnly = true;
        request.Chaos = new Config.ChaosConfig
        {
            ChaosMode = true,
            ChaosAmount = "100%",
            ChaosTypes = "encoding"
        };
        request.Metadata = request.Metadata with { Seed = 42 };

        using var baseStream = new MemoryStream();
        await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(baseStream, request, []);
        var baseBytes = baseStream.ToArray();
        var baseLines = Encoding.UTF8.GetString(baseBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        int totalLines = baseLines.Length;
        var chaosEngine = ChaosEngineBuilder.Build(request, totalLines, LoadFileFormat.Dat);
        Assert.NotNull(chaosEngine);
        Assert.NotNull(chaosEngine.Anomalies);

        using var chaosStream = new MemoryStream();
        await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(chaosStream, request, [], chaosEngine);
        var chaosBytes = chaosStream.ToArray();
        var chaosLines = Encoding.UTF8.GetString(chaosBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // (a) output differs from the no-chaos baseline
        Assert.NotEqual(baseBytes, chaosBytes);
        Assert.True(chaosEngine.Anomalies.Count > 0, "Chaos anomalies should be generated");

        // (b) header columns still come from the profile
        Assert.Contains("DOCID", chaosLines[0], StringComparison.Ordinal);
        Assert.Contains("FILEPATH", chaosLines[0], StringComparison.Ordinal);

        // (c) with chaosEngine: null output is byte-identical
        using var nullChaosStream = new MemoryStream();
        await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(nullChaosStream, request, [], null);
        var nullChaosBytes = nullChaosStream.ToArray();
        Assert.Equal(baseBytes, nullChaosBytes);

        // Verify specific corrupted lines based on seed 42
        var corruptedLines = chaosEngine.Anomalies.Select(a => a.LineNumber).ToList();
        Assert.Contains("Boundary 1-2", corruptedLines);
        Assert.Contains("Boundary 4-5", corruptedLines);
    }

    [Fact]
    public async Task WriteAsync_WithExplicitTiffPageRange_ConstantRange_RendersConfiguredPageCountInDatAndOpt()
    {
        var profile = new ColumnProfile
        {
            Name = "pages",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "PAGECOUNT", Type = "number", Required = true },
            },
        };

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 3, FileType = "tiff" },
            Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Tiff = new TiffConfig { PageRange = (11, 11) },
            LoadfileOnly = true,
        };

        var datContent = await CaptureOutputAsync(request);
        var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Header + 3 data rows
        Assert.Equal(4, datLines.Length);
        Assert.Contains("DOCID", datLines[0], StringComparison.Ordinal);
        Assert.Contains("PAGECOUNT", datLines[0], StringComparison.Ordinal);

        // Each data row must have PAGECOUNT = 11
        for (int i = 1; i <= 3; i++)
        {
            var fields = datLines[i].Split('\u0014').Select(f => f.Trim('\u00fe')).ToList();
            Assert.Equal("11", fields[1]);
        }

        // Companion OPT must have 33 page records (11 per document)
        var optWriter = new OptComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly);
        using var optStream = new MemoryStream();
        await optWriter.WriteAsync(optStream, request, new List<FileData>());
        optStream.Position = 0;
        var optContent = Encoding.UTF8.GetString(optStream.ToArray());
        var optLines = optContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(33, optLines.Length);
    }

    [Fact]
    public async Task WriteAsync_WithExplicitTiffPageRange_VariableRange_MatchesOptCountsPerRecord()
    {
        var profile = new ColumnProfile
        {
            Name = "pages",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "PAGECOUNT", Type = "number", Required = true },
            },
        };

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 5, FileType = "tiff" },
            Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Tiff = new TiffConfig { PageRange = (1, 20) },
            LoadfileOnly = true,
        };

        var datContent = await CaptureOutputAsync(request);
        var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var datPageCounts = new List<int>();
        for (int i = 1; i <= 5; i++)
        {
            var fields = datLines[i].Split('\u0014').Select(f => f.Trim('\u00fe')).ToList();
            var count = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(count, 1, 20);
            datPageCounts.Add(count);
        }

        // Companion OPT page counts per document must match exactly
        var optWriter = new OptComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly);
        using var optStream = new MemoryStream();
        await optWriter.WriteAsync(optStream, request, new List<FileData>());
        optStream.Position = 0;
        var optContent = Encoding.UTF8.GetString(optStream.ToArray());
        var optLines = optContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // In OPT, the 4th column is "Y" for doc break, 7th is page count on doc break
        var optDocPageCounts = new List<int>();
        foreach (var line in optLines)
        {
            var parts = line.Split(',');
            if (parts[3] == "Y")
            {
                optDocPageCounts.Add(int.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(5, optDocPageCounts.Count);
        Assert.Equal(datPageCounts, optDocPageCounts);
        Assert.Equal(datPageCounts.Sum(), optLines.Length);
    }

    [Fact]
    public async Task WriteAsync_WithExplicitTiffPageRange_SeededReproducibility_ProducesIdenticalOutputs()
    {
        var profile = new ColumnProfile
        {
            Name = "pages",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "PAGECOUNT", Type = "number", Required = true },
            },
        };

        var request1 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 10, FileType = "tiff" },
            Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Tiff = new TiffConfig { PageRange = (2, 15) },
            LoadfileOnly = true,
        };

        var request2 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 10, FileType = "tiff" },
            Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Tiff = new TiffConfig { PageRange = (2, 15) },
            LoadfileOnly = true,
        };

        var dat1 = await CaptureOutputAsync(request1);
        var dat2 = await CaptureOutputAsync(request2);

        Assert.Equal(dat1, dat2);
    }

    [Theory]
    [InlineData("PAGECOUNT")]
    [InlineData("PAGE_COUNT")]
    [InlineData("PAGE COUNT")]
    public async Task WriteAsync_CustomPageCountColumnNames_RendersConfiguredPageCount(string columnName)
    {
        var profile = new ColumnProfile
        {
            Name = "pages",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = columnName, Type = "number", Required = true },
            },
        };

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 2, FileType = "tiff" },
            Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Tiff = new TiffConfig { PageRange = (7, 7) },
            LoadfileOnly = true,
        };

        var datContent = await CaptureOutputAsync(request);
        var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i <= 2; i++)
        {
            var fields = datLines[i].Split('\u0014').Select(f => f.Trim('\u00fe')).ToList();
            Assert.Equal("7", fields[1]);
        }
    }

    [Fact]
    public async Task WriteAsync_WhenTiffPagesOmitted_PreservesDefaultBehavior()
    {
        var profile = new ColumnProfile
        {
            Name = "pages",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "PAGECOUNT", Type = "number", Required = true },
            },
        };

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = 5, FileType = "tiff" },
            Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Tiff = new TiffConfig { PageRange = null }, // Omitted --tiff-pages
            LoadfileOnly = true,
        };

        var datContent = await CaptureOutputAsync(request);
        var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Data rows should have random 1-10 page count
        for (int i = 1; i <= 5; i++)
        {
            var fields = datLines[i].Split('\u0014').Select(f => f.Trim('\u00fe')).ToList();
            var count = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(count, 1, 10);
        }
    }
}
