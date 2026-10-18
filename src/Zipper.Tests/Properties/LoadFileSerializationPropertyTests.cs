using System.Globalization;
using System.Text;
using FsCheck.Xunit;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Tests.TestHelpers;
using Zipper.Validation;

namespace Zipper.Tests.Properties;

/// <summary>
/// Property-based tests for Load File serialization, delimiter-aware parsing round-trips,
/// record construction, and encoding resolution.
/// </summary>
[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class LoadFileSerializationPropertyTests
{
    private static readonly (string Name, int ExpectedCodePage)[] SupportedEncodingSpecs =
    [
        ("UTF-8", 65001),
        ("ANSI", 1252),
        ("UTF-16", 1200),
        ("UTF-16LE", 1200),
        ("UNICODE", 1200),
        ("WINDOWS-1252", 1252),
        ("WESTERN EUROPEAN (WINDOWS)", 1252),
        ("ASCII", 20127)
    ];

    [Property]
    public void DatSerializer_RenderRecord_ShouldRoundTripThroughDatLineParser(DelimitedRecordPropertyInput input)
    {
        var newlineToken = input.NewlineDelimiter.ToString();
        var normalizedValues = input.Values
            .Select(v => v.Replace(newlineToken, string.Empty, StringComparison.Ordinal))
            .ToArray();

        var expectedValues = normalizedValues
            .Select(v => v
                .Replace("\r\n", newlineToken, StringComparison.Ordinal)
                .Replace("\n", newlineToken, StringComparison.Ordinal)
                .Replace("\r", newlineToken, StringComparison.Ordinal))
            .ToArray();

        var serializer = new DatSerializer(input.ColumnDelimiter, input.QuoteDelimiter, newlineToken);
        var recordId = $"DOC_{input.Columns.Length.ToString(CultureInfo.InvariantCulture)}";
        var record = LoadFileRecordBuilder.Build(input.Columns, normalizedValues, recordId);

        var rendered = serializer.RenderRecord(record);
        var parsedFields = DatLineParser.Parse(rendered, input.ColumnDelimiter, input.QuoteDelimiter);
        var parsedMap = DelimitedTestParser.ParseRecord(input.Columns, rendered, input.ColumnDelimiter, input.QuoteDelimiter);

        Assert.Equal(input.Columns.Length, parsedFields.Count);
        for (int i = 0; i < input.Columns.Length; i++)
        {
            Assert.Equal(expectedValues[i], parsedFields[i]);
            Assert.Equal(expectedValues[i], parsedMap[input.Columns[i]]);
        }
    }

    [Property]
    public void DatSerializer_RenderHeader_ShouldRoundTripThroughDatLineParser(DelimitedRecordPropertyInput input)
    {
        var newlineToken = input.NewlineDelimiter.ToString();
        var serializer = new DatSerializer(input.ColumnDelimiter, input.QuoteDelimiter, newlineToken);

        Assert.Equal("DAT", serializer.FormatName);
        Assert.Equal(".dat", serializer.FileExtension);

        var renderedStandardHeader = serializer.RenderHeader(input.Columns);
        var parsedStandardHeader = DatLineParser.Parse(renderedStandardHeader, input.ColumnDelimiter, input.QuoteDelimiter);
        var testParsedStandardHeader = DelimitedTestParser.ParseLine(renderedStandardHeader, input.ColumnDelimiter, input.QuoteDelimiter);

        Assert.Equal(input.Columns, parsedStandardHeader);
        Assert.Equal(input.Columns, testParsedStandardHeader);

        var arbitraryHeaders = input.Values
            .Select((v, idx) => $"H{idx.ToString(CultureInfo.InvariantCulture)}_{v.Replace(newlineToken, string.Empty, StringComparison.Ordinal)}")
            .ToArray();
        var expectedHeaders = arbitraryHeaders
            .Select(h => h
                .Replace("\r\n", newlineToken, StringComparison.Ordinal)
                .Replace("\n", newlineToken, StringComparison.Ordinal)
                .Replace("\r", newlineToken, StringComparison.Ordinal))
            .ToArray();

        var renderedArbitraryHeader = serializer.RenderHeader(arbitraryHeaders);
        var parsedArbitraryHeader = DatLineParser.Parse(renderedArbitraryHeader, input.ColumnDelimiter, input.QuoteDelimiter);

        Assert.Equal(expectedHeaders, parsedArbitraryHeader);
    }

    [Property]
    public void DatSerializer_FromRequest_ShouldMatchExplicitConstructorOutput(DelimitedRecordPropertyInput input)
    {
        var newlineToken = input.NewlineDelimiter.ToString();
        var request = new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = input.ColumnDelimiter.ToString(),
                QuoteDelimiter = input.QuoteDelimiter.ToString(),
                NewlineDelimiter = newlineToken,
            },
        };

        var fromRequest = new DatSerializer(request);
        var explicitSerializer = new DatSerializer(input.ColumnDelimiter, input.QuoteDelimiter, newlineToken);
        var record = LoadFileRecordBuilder.Build(input.Columns, input.Values, "DOC_REQ");

        Assert.Equal(explicitSerializer.RenderHeader(input.Columns), fromRequest.RenderHeader(input.Columns));
        Assert.Equal(explicitSerializer.RenderRecord(record), fromRequest.RenderRecord(record));
    }

    [Property]
    public void DatSerializer_NoQuoteMode_ShouldSplitOnColumnDelimiterAndPadShortRows(DelimitedRecordPropertyInput input)
    {
        var newlineToken = input.NewlineDelimiter.ToString();
        var colToken = input.ColumnDelimiter.ToString();
        var cleanValues = input.Values
            .Select(v => v
                .Replace(colToken, string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal))
            .ToArray();

        var serializer = new DatSerializer(input.ColumnDelimiter, '\0', newlineToken);
        var fullRecord = new LoadFileRecord
        {
            Columns = input.Columns,
            Values = cleanValues,
            RecordId = "DOC_NOQUOTE",
        };

        var renderedFull = serializer.RenderRecord(fullRecord);
        var parsedFull = DatLineParser.Parse(renderedFull, input.ColumnDelimiter, '\0');
        Assert.Equal(cleanValues, parsedFull);

        AssertShortRecordPadsEmptyFields(
            serializer,
            input.Columns,
            cleanValues,
            "DOC_SHORT",
            line => DatLineParser.Parse(line, input.ColumnDelimiter, '\0'));
    }

    [Property]
    public void CsvSerializer_RenderRecord_ShouldRoundTripSingleLineValues(DelimitedRecordPropertyInput input)
    {
        var singleLineValues = input.Values
            .Select(v => v
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal))
            .ToArray();

        var serializer = new CsvSerializer();
        Assert.Equal("CSV", serializer.FormatName);
        Assert.Equal(".csv", serializer.FileExtension);

        var headerLine = serializer.RenderHeader(input.Columns);
        var parsedHeaders = DelimitedTestParser.ParseLine(headerLine, ',', '"');
        Assert.Equal(input.Columns, parsedHeaders);

        var record = LoadFileRecordBuilder.Build(input.Columns, singleLineValues, "CSV_0001");
        var renderedRecord = serializer.RenderRecord(record);
        var parsedValues = DelimitedTestParser.ParseLine(renderedRecord, ',', '"');
        var parsedRecordMap = DelimitedTestParser.ParseRecord(input.Columns, renderedRecord, ',', '"');

        Assert.Equal(singleLineValues.Length, parsedValues.Count);
        for (int i = 0; i < singleLineValues.Length; i++)
        {
            Assert.Equal(singleLineValues[i], parsedValues[i]);
            Assert.Equal(singleLineValues[i], parsedRecordMap[input.Columns[i]]);
        }
    }

    [Property]
    public void CsvSerializer_RenderRecord_ShouldQuoteSpecialCharactersAndHandleShortValues(DelimitedRecordPropertyInput input)
    {
        var serializer = new CsvSerializer();
        string[] singleColumn = ["COL_1"];

        foreach (var rawValue in input.Values)
        {
            var singleRecord = new LoadFileRecord
            {
                Columns = singleColumn,
                Values = [rawValue],
                RecordId = "CSV_SINGLE",
            };

            var renderedSingle = serializer.RenderRecord(singleRecord);
            bool requiresQuotes = !string.IsNullOrEmpty(rawValue) &&
                (rawValue.Contains(',', StringComparison.Ordinal) ||
                 rawValue.Contains('"', StringComparison.Ordinal) ||
                 rawValue.Contains('\n', StringComparison.Ordinal) ||
                 rawValue.Contains('\r', StringComparison.Ordinal));

            if (requiresQuotes)
            {
                var expectedQuoted = $"\"{rawValue.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                Assert.Equal(expectedQuoted, renderedSingle);
            }
            else
            {
                Assert.Equal(rawValue, renderedSingle);
            }

            var parsedSingle = DatLineParser.Parse(renderedSingle, ',', '"');
            Assert.Single(parsedSingle);
            Assert.Equal(rawValue, parsedSingle[0]);
        }

        var singleLineSource = input.Values
            .Select(v => v
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal))
            .ToArray();

        AssertShortRecordPadsEmptyFields(
            serializer,
            input.Columns,
            singleLineSource,
            "CSV_SHORT",
            line => DelimitedTestParser.ParseLine(line, ',', '"'));
    }

    [Property]
    public void OptSerializer_RenderRecordAndHeader_ShouldPreserveCommaSeparatedFields(DelimitedRecordPropertyInput input)
    {
        var serializer = new OptSerializer();
        Assert.Equal("OPT", serializer.FormatName);
        Assert.Equal(".opt", serializer.FileExtension);
        Assert.Equal(string.Empty, serializer.RenderHeader(input.Columns));

        var sanitizedValues = input.Values
            .Select(v => v.Replace(",", "_", StringComparison.Ordinal))
            .ToArray();

        var fullRecord = LoadFileRecordBuilder.Build(input.Columns, sanitizedValues, "OPT_0001");
        var renderedFull = serializer.RenderRecord(fullRecord);
        var splitFull = renderedFull.Split(',');

        Assert.Equal(input.Columns.Length, splitFull.Length);
        Assert.Equal(sanitizedValues, splitFull);

        AssertShortRecordPadsEmptyFields(
            serializer,
            input.Columns,
            sanitizedValues,
            "OPT_SHORT",
            line => line.Split(','));
    }

    [Property]
    public void ConcordanceSerializer_RenderRecordAndHeader_ShouldQuoteEveryFieldAndPreserveNewlines(DelimitedRecordPropertyInput input)
    {
        var request = new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig
            {
                ColumnDelimiter = input.ColumnDelimiter.ToString(),
                QuoteDelimiter = input.QuoteDelimiter.ToString(),
                NewlineDelimiter = input.NewlineDelimiter.ToString(),
            },
        };

        var serializer = new ConcordanceSerializer(request);
        Assert.Equal("CONCORDANCE", serializer.FormatName);
        Assert.Equal(".dat", serializer.FileExtension);

        var renderedHeader = serializer.RenderHeader(input.Columns);
        Assert.StartsWith(input.QuoteDelimiter.ToString(), renderedHeader, StringComparison.Ordinal);
        Assert.EndsWith(input.QuoteDelimiter.ToString(), renderedHeader, StringComparison.Ordinal);

        var parsedHeader = DatLineParser.Parse(renderedHeader, input.ColumnDelimiter, input.QuoteDelimiter);
        Assert.Equal(input.Columns, parsedHeader);

        string[] quotedAndEmptyColumns = [$"{input.Columns[0]}{input.QuoteDelimiter}Quoted", string.Empty];
        var renderedQuotedHeader = serializer.RenderHeader(quotedAndEmptyColumns);
        var parsedQuotedHeader = DatLineParser.Parse(renderedQuotedHeader, input.ColumnDelimiter, input.QuoteDelimiter);
        Assert.Equal(quotedAndEmptyColumns, parsedQuotedHeader);

        var fullRecord = LoadFileRecordBuilder.Build(input.Columns, input.Values, "CONC_0001");
        var renderedFull = serializer.RenderRecord(fullRecord);
        Assert.StartsWith(input.QuoteDelimiter.ToString(), renderedFull, StringComparison.Ordinal);
        Assert.EndsWith(input.QuoteDelimiter.ToString(), renderedFull, StringComparison.Ordinal);

        var parsedFull = DatLineParser.Parse(renderedFull, input.ColumnDelimiter, input.QuoteDelimiter);
        Assert.Equal(input.Values, parsedFull);

        AssertShortRecordPadsEmptyFields(
            serializer,
            input.Columns,
            input.Values,
            "CONC_SHORT",
            line => DatLineParser.Parse(line, input.ColumnDelimiter, input.QuoteDelimiter));
    }

    [Property]
    public void LoadFileRecordBuilder_Build_ShouldAlignColumnsAndValuesAndRejectMismatchedCounts(DelimitedRecordPropertyInput input)
    {
        var recordId = $"REC_{input.Columns.Length.ToString(CultureInfo.InvariantCulture)}";
        var record = LoadFileRecordBuilder.Build(input.Columns, input.Values, recordId);

        Assert.Same(input.Columns, record.Columns);
        Assert.Same(input.Values, record.Values);
        Assert.Equal(recordId, record.RecordId);
        Assert.Equal(input.Columns.Length, record.Values.Count);

        var defaultIdRecord = new LoadFileRecord
        {
            Columns = input.Columns,
            Values = input.Values,
        };
        Assert.Equal(string.Empty, defaultIdRecord.RecordId);

        var tooFewValues = input.Values.Take(input.Values.Length - 1).ToArray();
        var shortException = Assert.Throws<InvalidOperationException>(
            () => LoadFileRecordBuilder.Build(input.Columns, tooFewValues, recordId));
        Assert.Contains(
            tooFewValues.Length.ToString(CultureInfo.InvariantCulture),
            shortException.Message,
            StringComparison.Ordinal);

        var tooManyValues = input.Values.Concat(["EXTRA"]).ToArray();
        var longException = Assert.Throws<InvalidOperationException>(
            () => LoadFileRecordBuilder.Build(input.Columns, tooManyValues, recordId));
        Assert.Contains(
            tooManyValues.Length.ToString(CultureInfo.InvariantCulture),
            longException.Message,
            StringComparison.Ordinal);
    }

    [Property]
    public void EncodingHelper_GetEncoding_ShouldResolveSupportedEncodingsDeterministically(
        int casingMask,
        byte unknownSeed)
    {
        for (int i = 0; i < SupportedEncodingSpecs.Length; i++)
        {
            var (canonicalName, expectedCodePage) = SupportedEncodingSpecs[i];
            var toggledName = ToggleCasingByBitMask(canonicalName, unchecked(casingMask ^ (i * 0x5A5A)));

            var resolved = EncodingHelper.GetEncoding(toggledName);
            Assert.NotNull(resolved);
            Assert.Equal(expectedCodePage, resolved.CodePage);

            var resolvedOrDefault = EncodingHelper.GetEncodingOrDefault(toggledName);
            Assert.Equal(expectedCodePage, resolvedOrDefault.CodePage);
        }

        string? nullOrEmpty = (unknownSeed % 2 == 0) ? null : string.Empty;
        Assert.Null(EncodingHelper.GetEncoding(nullOrEmpty));
        Assert.Equal(Encoding.UTF8.CodePage, EncodingHelper.GetEncodingOrDefault(nullOrEmpty).CodePage);

        var unknownName = $"UNKNOWN_ENCODING_{unknownSeed.ToString(CultureInfo.InvariantCulture)}_{unchecked((uint)casingMask).ToString("X8", CultureInfo.InvariantCulture)}";
        Assert.Null(EncodingHelper.GetEncoding(unknownName));
        Assert.Equal(Encoding.UTF8.CodePage, EncodingHelper.GetEncodingOrDefault(unknownName).CodePage);
    }

    private static void AssertShortRecordPadsEmptyFields(
        ILoadFileSerializer serializer,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> fullValues,
        string recordId,
        Func<string, IReadOnlyList<string>> parseLine)
    {
        int shortCount = Math.Max(0, fullValues.Count - 1);
        var shortValues = fullValues.Take(shortCount).ToArray();
        var shortRecord = new LoadFileRecord
        {
            Columns = columns,
            Values = shortValues,
            RecordId = recordId,
        };

        var renderedShort = serializer.RenderRecord(shortRecord);
        var parsedShort = parseLine(renderedShort);

        Assert.Equal(columns.Count, parsedShort.Count);
        for (int i = 0; i < columns.Count; i++)
        {
            var expected = i < shortValues.Length ? shortValues[i] : string.Empty;
            Assert.Equal(expected, parsedShort[i]);
        }
    }

    private static string ToggleCasingByBitMask(string value, int mask)
    {
        var buffer = value.ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            bool upper = ((mask >> (i % 31)) & 1) == 1;
            buffer[i] = upper
                ? char.ToUpperInvariant(buffer[i])
                : char.ToLowerInvariant(buffer[i]);
        }

        return new string(buffer);
    }
}
