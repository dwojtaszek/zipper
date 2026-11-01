using System.Globalization;
using System.Text;
using System.Text.Json;
using FsCheck.Xunit;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.ManifestComparison;
using Zipper.Profiles;
using Zipper.Profiles.Generation;
using Zipper.Tests.TestHelpers;
using Zipper.Validation;

namespace Zipper.Tests.Properties;

[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class ChaosAndComparisonPropertyTests
{
    [Property]
    public void ChaosSampler_SelectTargets_ShouldRespectBoundsAndDeterminism(
        ushort rawTotalRecords,
        byte rawAmount,
        bool usePercentage,
        int seed)
    {
        int totalRecords = (rawTotalRecords % 500) + 1;
        int boundedAmount = (rawAmount % 100) + 1;
        string chaosAmount = usePercentage
            ? $"{boundedAmount.ToString(CultureInfo.InvariantCulture)}%"
            : boundedAmount.ToString(CultureInfo.InvariantCulture);

        int expectedCount = ChaosSampler.ParseChaosAmount(chaosAmount, totalRecords);
        Assert.InRange(expectedCount, 0, totalRecords); // 0 is now valid for "0" or "0%"
        // For percentage values > 0, expectedCount must be >= 0; sampling may produce 0 when floor is 0? But we changed to floor to 1
        // But if boundedAmount is 0 and usePercentage is true, chaosAmount is "0%" -> returns 0
        if (usePercentage && boundedAmount == 0)
        {
            Assert.Equal(0, expectedCount);
        }

        var sampler1 = new ChaosSampler(totalRecords, chaosAmount, new Random(seed));
        var sampler2 = new ChaosSampler(totalRecords, chaosAmount, new Random(seed));

        int interceptedCount = 0;
        for (long line = 1; line <= totalRecords; line++)
        {
            bool hit1 = sampler1.ShouldIntercept(line);
            bool hit2 = sampler2.ShouldIntercept(line);
            Assert.Equal(hit1, hit2);
            if (hit1)
            {
                interceptedCount++;
            }
        }

        Assert.Equal(expectedCount, interceptedCount);
        Assert.False(sampler1.ShouldIntercept(0));
        Assert.False(sampler1.ShouldIntercept(totalRecords + 1L));

        int defaultCount = ChaosSampler.ParseChaosAmount(null, totalRecords);
        Assert.Equal(Math.Max(1, totalRecords / 100), defaultCount);

        // "invalid" string now throws ArgumentException instead of falling back
        Assert.Throws<ArgumentException>(() => ChaosSampler.ParseChaosAmount("invalid", totalRecords));
    }

    [Property]
    public void ChaosSampler_ParseLongPercentage_ShouldTruncateExactCount(ChaosLongPercentagePropertyInput input)
    {
        decimal percentage = input.PercentageTenths / 10m;
        string amount = percentage.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        long expectedCount = (long)(input.RecordCount * percentage / 100m);

        Assert.Equal(expectedCount, ChaosSampler.ParseChaosAmount(amount, input.RecordCount));
    }

    [Property]
    public void AdversarialValueTailPlan_Create_ShouldSelectDeterministicBoundedTargets(
        AdversarialValueTailPropertyInput input)
    {
        var request = new FileGenerationRequest
        {
            Chaos = new ChaosConfig
            {
                ChaosMode = true,
                ChaosAmount = input.TargetCount.ToString(CultureInfo.InvariantCulture),
                ChaosScenario = "adversarial-values",
            },
            Metadata = new MetadataConfig { Seed = input.Seed },
        };

        var plan = Assert.IsType<AdversarialValueTailPlan>(
            AdversarialValueTailPlan.Create(request, input.RecordCount));
        var replayPlan = Assert.IsType<AdversarialValueTailPlan>(
            AdversarialValueTailPlan.Create(request, input.RecordCount));
        Assert.Null(plan.GetValue(0));
        Assert.Null(plan.GetValue((long)input.RecordCount + 1));
        string?[] values = Enumerable.Range(1, input.RecordCount)
            .Select(index => plan.GetValue(index))
            .ToArray();
        string?[] replayValues = Enumerable.Range(1, input.RecordCount)
            .Select(index => replayPlan.GetValue(index))
            .ToArray();

        Assert.Equal(values, replayValues);
        var selectedIndices = values
            .Select((value, index) => (value, index))
            .Where(entry => entry.value is not null)
            .Select(entry => (long)entry.index + 1)
            .ToArray();
        Assert.Equal(input.TargetCount, selectedIndices.Length);
        Assert.All(selectedIndices, index => Assert.InRange(index, 1L, input.RecordCount));
        var expectedSelectedIndices = input.TargetCount == input.RecordCount
            ? Enumerable.Range(1, input.RecordCount).Select(index => (long)index).ToArray()
            : ChaosSampler.SelectTargetLines(input.RecordCount, input.TargetCount, new Random(input.Seed))
                .OrderBy(index => index)
                .ToArray();
        Assert.Equal(expectedSelectedIndices, selectedIndices.OrderBy(index => index));

        string[] selectedValues = values.OfType<string>().ToArray();
        if (input.TargetCount == 0)
        {
            Assert.Empty(selectedValues);
            return;
        }

        string platformLengthValue = Assert.Single(
            selectedValues,
            value => value.Length == AdversarialValueTailPlan.PlatformFieldLength);
        int platformLengthRecordIndex = Array.FindIndex(
            values,
            value => value?.Length == AdversarialValueTailPlan.PlatformFieldLength) + 1;
        Assert.Equal(selectedIndices[0], (long)platformLengthRecordIndex);
        Assert.Contains('\u0014', platformLengthValue);
        Assert.Contains('\u00fe', platformLengthValue);
        Assert.Contains("Ã¾", platformLengthValue, StringComparison.Ordinal);
    }

    [Property]
    public void AdversarialValueTailPlan_FindColumn_ShouldAvoidSourceMetadataColumn(byte rawKeyVariant)
    {
        string sourceMetadataKey = (rawKeyVariant % 4) switch
        {
            0 => "AUTHOR",
            1 => "Author",
            2 => "A-U_T H O R",
            _ => " author ",
        };
        var columns = new[]
        {
            new ColumnDefinition { Name = "AUTHOR", Type = "text" },
            new ColumnDefinition { Name = "REVIEWNOTES", Type = "longtext" },
        };

        var selectedColumn = AdversarialValueTailPlan.FindColumn(columns, [sourceMetadataKey]);

        Assert.Equal("REVIEWNOTES", selectedColumn?.Name);
    }

    [Property]
    public void DatAnomalyApplier_Apply_ShouldMutateLineAndRecordAnomalyMetadata(
        DelimitedRecordPropertyInput input,
        byte rawTypeIndex,
        ChaosSeedPropertyInput seedInput)
    {
        string colDelim = input.ColumnDelimiter.ToString();
        string quoteDelim = input.QuoteDelimiter.ToString();
        string recordId = $"DOC{seedInput.RecordOrdinal.ToString("D4", CultureInfo.InvariantCulture)}";
        long lineNumber = seedInput.LineNumber;

        var quotedCells = input.Values
            .Select(v => $"{quoteDelim}{v.Replace(quoteDelim, string.Empty, StringComparison.Ordinal)}{quoteDelim}")
            .ToArray();
        string line = string.Join(colDelim, quotedCells);

        string[] chaosTypes = ["mixed-delimiters", "quotes", "columns", "eol", "encoding", "unknown-type"];
        string chosenType = chaosTypes[rawTypeIndex % chaosTypes.Length];

        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(seedInput.Seed), colDelim, quoteDelim, "\r\n");

        string mutated = applier.Apply(lineNumber, line, recordId, chosenType, anomalies);

        if (string.Equals(chosenType, "encoding", StringComparison.Ordinal))
        {
            Assert.Equal(line, mutated);
            Assert.Empty(anomalies);

            Encoding[] encodings = [Encoding.UTF8, Encoding.Unicode, Encoding.ASCII];
            Encoding chosenEncoding = encodings[rawTypeIndex % encodings.Length];
            byte[]? injectedBytes = applier.GetEncodingAnomaly(lineNumber, lineNumber + 1, chosenEncoding, anomalies);

            Assert.NotNull(injectedBytes);
            Assert.NotEmpty(injectedBytes);
            ChaosAnomaly encodingAnomaly = Assert.Single(anomalies);
            Assert.Equal($"Boundary {lineNumber.ToString(CultureInfo.InvariantCulture)}-{(lineNumber + 1).ToString(CultureInfo.InvariantCulture)}", encodingAnomaly.LineNumber);
            Assert.Equal("N/A", encodingAnomaly.RecordID);
            Assert.Equal("encoding", encodingAnomaly.ErrorType);
            Assert.Null(applier.GetEncodingAnomaly(lineNumber, lineNumber + 1, chosenEncoding, anomalies));
        }
        else
        {
            // Anomaly is only recorded if the line was actually mutated
            // Types that must always mutate: quotes (always drops a quote), opt-boundary (always flips boundary)
            if (string.Equals(chosenType, "quotes", StringComparison.Ordinal))
            {
                Assert.NotEqual(line, mutated);
                Assert.Equal(line.Length - quoteDelim.Length, mutated.Length);
            }
            else if (string.Equals(chosenType, "opt-boundary", StringComparison.Ordinal))
            {
                Assert.NotEqual(line, mutated);
            }

            if (mutated != line)
            {
                ChaosAnomaly recorded = Assert.Single(anomalies);
                Assert.Equal(lineNumber.ToString(CultureInfo.InvariantCulture), recorded.LineNumber);
                Assert.Equal(recordId, recorded.RecordID);
                Assert.Equal(chosenType, recorded.ErrorType);
                Assert.False(string.IsNullOrEmpty(recorded.Description));

                if (string.Equals(chosenType, "mixed-delimiters", StringComparison.Ordinal) && input.Values.Length > 1)
                {
                    Assert.NotEqual(line, mutated);
                    Assert.StartsWith("Delimiter ", recorded.Column, StringComparison.Ordinal);
                }
                else if (string.Equals(chosenType, "quotes", StringComparison.Ordinal))
                {
                    Assert.Equal(line.Length - quoteDelim.Length, mutated.Length);
                    Assert.StartsWith("Column ", recorded.Column, StringComparison.Ordinal);
                    Assert.Contains($"ascii:{((int)input.QuoteDelimiter).ToString(CultureInfo.InvariantCulture)}", recorded.Description, StringComparison.Ordinal);
                }
                else if (string.Equals(chosenType, "columns", StringComparison.Ordinal))
                {
                    if (input.Values.Length > 1)
                    {
                        Assert.NotEqual(line.Length, mutated.Length);
                    }
                }
                else if (string.Equals(chosenType, "eol", StringComparison.Ordinal))
                {
                    Assert.Contains("\r\n", mutated, StringComparison.Ordinal);
                }
            }
            else
            {
                // No anomaly recorded when line wasn't mutated
                Assert.Empty(anomalies);
            }
        }

        // Exercise single-column / empty-delimiter / multi-char delimiter edge paths
        string singleToken = $"{recordId}_{input.Values[0].Replace(colDelim, string.Empty, StringComparison.Ordinal).Replace(quoteDelim, string.Empty, StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal)}";
        var edgeAnomalies = new List<ChaosAnomaly>();
        var edgeApplier = new DatAnomalyApplier(new Random(seedInput.Seed), string.Empty, "''", "\n");
        _ = edgeApplier.Apply(1, singleToken, recordId, "mixed-delimiters", edgeAnomalies);
        _ = edgeApplier.Apply(2, singleToken, recordId, "quotes", edgeAnomalies);
        _ = edgeApplier.Apply(3, singleToken, recordId, "eol", edgeAnomalies);
        var emptyQuoteApplier = new DatAnomalyApplier(new Random(seedInput.Seed), ",", string.Empty, "\n");
        _ = emptyQuoteApplier.Apply(4, singleToken, recordId, "quotes", edgeAnomalies);
        // After fix: anomalies only recorded when line was modified
        // eol always modifies, others may not with empty delimiters
        Assert.Contains(edgeAnomalies, a => a.ErrorType == "eol");
    }

    [Property]
    public void EmailAnomalyApplier_Apply_ShouldMutateAndAuditAnomaliesDeterministically(
        byte rawTypeIndex,
        ChaosSeedPropertyInput seedInput)
    {
        string[] chaosTypes = [.. ChaosAnomalyTypes.Email];
        string chosenType = chaosTypes[rawTypeIndex % chaosTypes.Length];

        string boundary = "----=_Part_Sample_123";
        string sampleEmail = string.Concat(
            "From: alice@example.com\r\n",
            "To: bob@example.com\r\n",
            "Subject: Important Project Status Update\r\n",
            "Date: Mon, 01 Jan 2024 10:00:00 +0000\r\n",
            "MIME-Version: 1.0\r\n",
            $"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n",
            "\r\n",
            $"--{boundary}\r\n",
            "Content-Type: text/plain; charset=utf-8\r\n",
            "\r\n",
            "Hello Bob,\r\nHere is the weekly update.\r\n",
            $"--{boundary}\r\n",
            "Content-Type: application/pdf; name=\"report.pdf\"\r\n",
            "Content-Disposition: attachment; filename=\"report.pdf\"\r\n",
            "\r\n",
            "JVBERi0xLjQKfakecontent\r\n",
            $"--{boundary}--\r\n");

        byte[] originalBytes = Encoding.UTF8.GetBytes(sampleEmail);
        string recordId = $"DOC{seedInput.RecordOrdinal.ToString("D4", CultureInfo.InvariantCulture)}";
        string fileName = $"{recordId}.eml";
        int workItemIndex = (int)(seedInput.LineNumber % 1000) + 1;

        var first = EmailAnomalyApplier.Apply(originalBytes, fileName, recordId, chosenType, workItemIndex, new Random(seedInput.Seed));
        var second = EmailAnomalyApplier.Apply(originalBytes, fileName, recordId, chosenType, workItemIndex, new Random(seedInput.Seed));

        // Determinism invariant
        Assert.Equal(first.Content, second.Content);
        Assert.Equal(first.Anomaly, second.Anomaly);

        // Mutation invariant
        Assert.False(originalBytes.SequenceEqual(first.Content));

        // Audit contract
        ChaosAnomaly anomaly = first.Anomaly;
        Assert.Equal(fileName, anomaly.File);
        Assert.Equal(recordId, anomaly.RecordID);
        Assert.Equal(chosenType, anomaly.ErrorType);
        Assert.False(string.IsNullOrEmpty(anomaly.Location));
        Assert.False(string.IsNullOrEmpty(anomaly.LineNumber));
        Assert.False(string.IsNullOrEmpty(anomaly.Description));
    }

    [Property]
    public void DatAnomalyApplier_ApplyRecord_ShouldBeDeterministicAndAuditTheColumn(
        byte rawTypeIndex,
        ChaosSeedPropertyInput seedInput)
    {
        string[] chaosTypes = ["path-pointer", "field-length", "date-format", "null-byte", "trailing-empties"];
        string chaosType = chaosTypes[rawTypeIndex % chaosTypes.Length];
        IReadOnlyList<string> columns = ["Control Number", "File Path", "Date Created", "Author"];
        var record = new LoadFileRecord
        {
            Columns = columns,
            Values = ["DOC00000001", "NATIVES\\001\\DOC00000001.pdf", "2024-01-15", "Jane Doe"],
            RecordId = $"DOC{seedInput.RecordOrdinal:D4}",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);
        var firstAnomalies = new List<ChaosAnomaly>();
        var replayAnomalies = new List<ChaosAnomaly>();
        var first = new DatAnomalyApplier(new Random(seedInput.Seed), "|", "\"", "\r\n", ["Date Created"]);
        var replay = new DatAnomalyApplier(new Random(seedInput.Seed), "|", "\"", "\r\n", ["Date Created"]);

        string mutated = first.Apply(2, line, record.RecordId, chaosType, firstAnomalies, record, serializer);
        string replayed = replay.Apply(2, line, record.RecordId, chaosType, replayAnomalies, record, serializer);

        Assert.NotEqual(line, mutated);
        Assert.Equal(mutated, replayed);
        var anomaly = Assert.Single(firstAnomalies);
        Assert.Equal(anomaly, Assert.Single(replayAnomalies));
        Assert.Equal(chaosType, anomaly.ErrorType);
        Assert.Equal(record.RecordId, anomaly.RecordID);
        Assert.False(string.IsNullOrWhiteSpace(anomaly.Column));
        Assert.NotEqual("N/A", anomaly.Column);
    }

    [Property]
    public void DatAnomalyApplier_PathPointer_ShouldAvoidExistingSourcePaths(
        byte rawCollisionDepth,
        bool useBackslashes,
        ChaosSeedPropertyInput seedInput)
    {
        const string originalPath = "folder/doc.pdf";
        int collisionDepth = (rawCollisionDepth % 5) + 1;
        var existingFilePaths = new List<string> { originalPath };
        for (int depth = 1; depth <= collisionDepth; depth++)
        {
            string suffix = depth == 1 ? "_MISSING" : $"_MISSING_{depth - 1}";
            string extension = depth == 1 || depth % 2 == 0 ? ".docx" : ".pdf";
            string existingPath = $"folder/doc{suffix}{extension}";
            if (useBackslashes)
            {
                existingPath = existingPath.Replace('/', '\\');
            }
            else if (depth % 2 == 0)
            {
                existingPath = existingPath.ToUpperInvariant();
            }

            existingFilePaths.Add(existingPath);
        }

        var record = new LoadFileRecord
        {
            Columns = ["File Path"],
            Values = [originalPath],
            RecordId = $"DOC{seedInput.RecordOrdinal:D4}",
        };
        var serializer = new DatSerializer('|', '"', "~");
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(
            new Random(seedInput.Seed),
            "|",
            "\"",
            "\r\n",
            sourceFilePaths: existingFilePaths
                .Select(path => path.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        string line = serializer.RenderRecord(record);

        string result = applier.Apply(2, line, record.RecordId, "path-pointer", anomalies, record, serializer);
        string mutatedPath = DatLineParser.Parse(result, '|', '"')[0].Replace('\\', '/');

        Assert.NotEqual(line, result);
        Assert.Equal(
            $"folder/doc_MISSING_{collisionDepth.ToString(CultureInfo.InvariantCulture)}.pdf",
            mutatedPath);

        var reorderedRecord = new LoadFileRecord
        {
            Columns = ["Author", "File Path"],
            Values = ["Jane Doe", originalPath],
            RecordId = $"DOC{seedInput.RecordOrdinal:D4}_REORDERED",
        };
        string reorderedLine = serializer.RenderRecord(reorderedRecord);
        string reorderedResult = applier.Apply(
            3,
            reorderedLine,
            reorderedRecord.RecordId,
            "path-pointer",
            anomalies,
            reorderedRecord,
            serializer);
        var reorderedValues = DatLineParser.Parse(reorderedResult, '|', '"');

        Assert.Equal("Jane Doe", reorderedValues[0]);
        Assert.Equal(mutatedPath, reorderedValues[1].Replace('\\', '/'));
        Assert.Equal(2, anomalies.Count);
        Assert.All(anomalies, anomaly => Assert.Equal("path-pointer", anomaly.ErrorType));
    }

    [Property]
    public void OptAnomalyApplier_Apply_ShouldMutateOptLineAndRecordAnomalyMetadata(
        BatesPropertyInput batesInput,
        byte rawTypeIndex,
        bool isBoundary,
        ChaosSeedPropertyInput seedInput)
    {
        string bates = BatesRangeAnalyzer.FormatBates(batesInput.Start, batesInput.Prefix, batesInput.Digits);
        string boundaryFlag = isBoundary ? "Y" : string.Empty;
        int pageCount = (batesInput.Count % 20) + 1;
        string optLine = $"{bates},VOL001,IMAGES\\001\\{bates}.tif,{boundaryFlag},,,{pageCount.ToString(CultureInfo.InvariantCulture)}";

        string[] optTypes = ["opt-boundary", "opt-columns", "opt-pagecount", "opt-path", "opt-batesnumber", "opt-encoding", "unknown-opt"];
        string chosenType = optTypes[rawTypeIndex % optTypes.Length];

        var anomalies = new List<ChaosAnomaly>();
        var applier = new OptAnomalyApplier(new Random(seedInput.Seed));
        long lineNumber = seedInput.LineNumber;

        string mutated = applier.Apply(lineNumber, optLine, bates, chosenType, anomalies);

        // Anomaly is only recorded if the line was actually mutated
        if (string.Equals(chosenType, "opt-encoding", StringComparison.Ordinal))
        {
            Assert.Equal(optLine, mutated);
            Assert.Empty(anomalies);

            byte[]? injectedBytes = applier.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.Latin1, anomalies);

            Assert.NotNull(injectedBytes);
            Assert.Equal(Encoding.UTF8.GetPreamble(), injectedBytes);
            ChaosAnomaly encodingAnomaly = Assert.Single(anomalies);
            Assert.Equal($"Boundary {lineNumber.ToString(CultureInfo.InvariantCulture)}-{(lineNumber + 1).ToString(CultureInfo.InvariantCulture)}", encodingAnomaly.LineNumber);
            Assert.Equal("N/A", encodingAnomaly.RecordID);
            Assert.Equal("N/A", encodingAnomaly.Column);
            Assert.Equal(chosenType, encodingAnomaly.ErrorType);
            Assert.False(string.IsNullOrEmpty(encodingAnomaly.Description));
            Assert.Null(applier.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.Latin1, anomalies));
        }
        else if (mutated != optLine)
        {
            ChaosAnomaly anomaly = Assert.Single(anomalies);

            Assert.Equal(lineNumber.ToString(CultureInfo.InvariantCulture), anomaly.LineNumber);
            Assert.Equal(bates, anomaly.RecordID);
            Assert.Equal("N/A", anomaly.Column);
            Assert.Equal(chosenType, anomaly.ErrorType);
            Assert.False(string.IsNullOrEmpty(anomaly.Description));
            Assert.Null(applier.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.UTF8, anomalies));

            int originalCommas = optLine.Count(c => c == ',');
            int mutatedCommas = mutated.Count(c => c == ',');

            switch (chosenType)
            {
                case "opt-boundary":
                    string[] parts = mutated.Split(',');
                    Assert.Equal(isBoundary ? string.Empty : "Y", parts[3]);
                    break;
                case "opt-columns":
                    Assert.True((mutatedCommas == originalCommas + 1) || (mutatedCommas == originalCommas - 1));
                    break;
                case "opt-pagecount":
                    Assert.True(
                        mutated.EndsWith(",ABC", StringComparison.Ordinal) ||
                        mutated.EndsWith(",-1", StringComparison.Ordinal));
                    break;
                case "opt-path":
                    Assert.Contains("IMAGES\\..\\..\\invalid\\path.tif", mutated, StringComparison.Ordinal);
                    break;
                case "opt-batesnumber":
                    Assert.StartsWith(",", mutated, StringComparison.Ordinal);
                    break;
            }
        }
        else
        {
            // No anomaly recorded when line wasn't mutated
            Assert.Empty(anomalies);
        }

        // Exercise degenerate single-token line without commas
        string commaFreeToken = $"RAW_{bates.Replace(",", string.Empty, StringComparison.Ordinal)}_{pageCount.ToString(CultureInfo.InvariantCulture)}";
        var degenerateAnomalies = new List<ChaosAnomaly>();
        foreach (string t in optTypes)
        {
            _ = applier.Apply(1, commaFreeToken, bates, t, degenerateAnomalies);
        }

        // After fix: only types that actually modify the line record anomalies
        // opt-batesnumber always modifies (removes first field), others may not
        Assert.Contains(degenerateAnomalies, a => a.ErrorType == "opt-batesnumber");
    }

    [Property]
    public void CsvAnomalyApplier_Apply_ShouldMutateAndReplayFormatSpecificAnomalies(
        DelimitedRecordPropertyInput input,
        byte rawTypeIndex,
        ChaosSeedPropertyInput seedInput)
    {
        string[] chaosTypes = ["csv-quote-escape", "csv-embedded-newline", "csv-bom"];
        string chaosType = chaosTypes[rawTypeIndex % chaosTypes.Length];
        string[] values = input.Values.Append("chaos-probe").ToArray();
        string[] columns = input.Columns.Append("CHAOS_PROBE").ToArray();
        var record = LoadFileRecordBuilder.Build(columns, values, $"DOC{seedInput.RecordOrdinal:D4}");
        string line = new CsvSerializer().RenderRecord(record);
        var firstAnomalies = new List<ChaosAnomaly>();
        var replayAnomalies = new List<ChaosAnomaly>();
        var first = new CsvAnomalyApplier(new Random(seedInput.Seed));
        var replay = new CsvAnomalyApplier(new Random(seedInput.Seed));

        string mutated = first.Apply(seedInput.LineNumber, line, record.RecordId, chaosType, firstAnomalies);
        string replayed = replay.Apply(seedInput.LineNumber, line, record.RecordId, chaosType, replayAnomalies);

        Assert.Equal(mutated, replayed);

        if (chaosType == "csv-bom")
        {
            Assert.Equal(line, mutated);
            byte[]? bytes = first.GetEncodingAnomaly(seedInput.LineNumber, seedInput.LineNumber + 1, Encoding.UTF8, firstAnomalies);
            byte[]? replayBytes = replay.GetEncodingAnomaly(seedInput.LineNumber, seedInput.LineNumber + 1, Encoding.UTF8, replayAnomalies);
            Assert.Equal(Encoding.UTF8.GetPreamble(), bytes);
            Assert.Equal(bytes, replayBytes);
            ChaosAnomaly anomaly = Assert.Single(firstAnomalies);
            Assert.Equal(anomaly, Assert.Single(replayAnomalies));
            Assert.Equal($"Boundary {seedInput.LineNumber.ToString(CultureInfo.InvariantCulture)}-{(seedInput.LineNumber + 1).ToString(CultureInfo.InvariantCulture)}", anomaly.LineNumber);
            Assert.Equal("N/A", anomaly.RecordID);
        }
        else
        {
            Assert.NotEqual(line, mutated);
            ChaosAnomaly anomaly = Assert.Single(firstAnomalies);
            Assert.Equal(anomaly, Assert.Single(replayAnomalies));
            Assert.Equal(seedInput.LineNumber.ToString(CultureInfo.InvariantCulture), anomaly.LineNumber);
            Assert.Equal(record.RecordId, anomaly.RecordID);
            Assert.Equal(chaosType, anomaly.ErrorType);
            Assert.False(string.IsNullOrWhiteSpace(anomaly.Column));
            Assert.False(string.IsNullOrWhiteSpace(anomaly.Description));
            if (chaosType == "csv-embedded-newline")
            {
                Assert.Equal(columns.Length, DelimitedTestParser.ParseLine(mutated, ',', '"').Count);
            }
        }
    }

    [Property]
    public void ConcordanceAnomalyApplier_Apply_ShouldMutateAndReplayFormatSpecificAnomalies(
        DelimitedRecordPropertyInput input,
        byte rawTypeIndex,
        ChaosSeedPropertyInput seedInput)
    {
        string[] chaosTypes = ["concordance-delimiter", "concordance-quote-escape"];
        string chaosType = chaosTypes[rawTypeIndex % chaosTypes.Length];
        string[] values = input.Values.Append("chaos-probe").ToArray();
        string[] columns = input.Columns.Append("CHAOS_PROBE").ToArray();
        var record = LoadFileRecordBuilder.Build(columns, values, $"DOC{seedInput.RecordOrdinal:D4}");
        var request = new FileGenerationRequest
        {
            Delimiters = new Config.DelimiterConfig
            {
                ColumnDelimiter = input.ColumnDelimiter.ToString(),
                QuoteDelimiter = input.QuoteDelimiter.ToString(),
            },
        };
        var serializer = new ConcordanceSerializer(request);
        string line = serializer.RenderRecord(record);
        var firstAnomalies = new List<ChaosAnomaly>();
        var replayAnomalies = new List<ChaosAnomaly>();
        var first = new ConcordanceAnomalyApplier(new Random(seedInput.Seed), input.ColumnDelimiter, input.QuoteDelimiter);
        var replay = new ConcordanceAnomalyApplier(new Random(seedInput.Seed), input.ColumnDelimiter, input.QuoteDelimiter);

        string mutated = first.Apply(seedInput.LineNumber, line, record.RecordId, chaosType, firstAnomalies, record, serializer);
        string replayed = replay.Apply(seedInput.LineNumber, line, record.RecordId, chaosType, replayAnomalies, record, serializer);

        Assert.NotEqual(line, mutated);
        Assert.Equal(mutated, replayed);
        ChaosAnomaly anomaly = Assert.Single(firstAnomalies);
        Assert.Equal(anomaly, Assert.Single(replayAnomalies));
        Assert.Equal(seedInput.LineNumber.ToString(CultureInfo.InvariantCulture), anomaly.LineNumber);
        Assert.Equal(record.RecordId, anomaly.RecordID);
        Assert.Equal(chaosType, anomaly.ErrorType);
        Assert.False(string.IsNullOrWhiteSpace(anomaly.Column));
        Assert.False(string.IsNullOrWhiteSpace(anomaly.Description));

        if (chaosType == "concordance-delimiter")
        {
            Assert.Equal(
                columns.Length - 1,
                DelimitedTestParser.ParseLine(mutated, input.ColumnDelimiter, input.QuoteDelimiter).Count);
        }
        else
        {
            Assert.Equal($"Column {columns.Length}", anomaly.Column);
            Assert.Throws<FormatException>(
                () => DelimitedTestParser.ParseLine(mutated, input.ColumnDelimiter, input.QuoteDelimiter));
        }
    }

    [Property]
    public void ChaosEngine_ApplyXmlChaos_ShouldReplayAndAuditDocumentOrFileAnomalies(
        byte rawTypeIndex,
        ChaosSeedPropertyInput seedInput)
    {
        string[] chaosTypes = ["xml-unclosed-tag", "xml-bad-entity", "xml-encoding-lie"];
        string chaosType = chaosTypes[rawTypeIndex % chaosTypes.Length];
        int documentCount = Math.Clamp(seedInput.LineNumber, 1, 6);
        string documents = string.Concat(Enumerable.Range(1, documentCount)
            .Select(index => $"<Document DocID=\"DOC{index:D8}\"><Tags><Tag>encoding=\"utf-8\"</Tag></Tags></Document>"));
        string xml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Root><Batch>{documents}</Batch></Root>";
        var first = new ChaosEngine(documentCount, "100%", chaosType, LoadFileFormat.EdrmXml, "|", "\"", "\r\n", seedInput.Seed);
        var replay = new ChaosEngine(documentCount, "100%", chaosType, LoadFileFormat.EdrmXml, "|", "\"", "\r\n", seedInput.Seed);

        string result = first.ApplyXmlChaos(xml);
        string replayed = replay.ApplyXmlChaos(xml);

        Assert.Equal(result, replayed);
        Assert.Equal(first.Anomalies, replay.Anomalies);

        if (chaosType == "xml-encoding-lie")
        {
            Assert.Contains("encoding=\"utf-16\"", result, StringComparison.Ordinal);
            Assert.Equal(documentCount, result.Split("encoding=\"utf-8\"", StringSplitOptions.None).Length - 1);
            ChaosAnomaly anomaly = Assert.Single(first.Anomalies);
            Assert.Equal("1", anomaly.LineNumber);
            Assert.Equal("N/A", anomaly.RecordID);
            Assert.Equal("XML declaration", anomaly.Column);
        }
        else
        {
            Assert.Throws<System.Xml.XmlException>(() => System.Xml.Linq.XDocument.Parse(result));
            Assert.Equal(documentCount, first.Anomalies.Count);
            Assert.All(first.Anomalies, anomaly =>
            {
                Assert.Equal(chaosType, anomaly.ErrorType);
                Assert.NotEqual("N/A", anomaly.RecordID);
            });
        }
    }

    [Property]
    public void CrossFormatAnomalyApplier_Apply_ShouldBeDeterministicAndAuditReciprocalPeers(
        int rawSeed,
        byte rawTypeIndex)
    {
        int seed = Math.Abs((long)rawSeed) > int.MaxValue ? 0 : Math.Abs(rawSeed);
        string[] allTypes =
        [
            ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            ChaosAnomalyTypes.CrossFormat.DesyncOrder,
            ChaosAnomalyTypes.CrossFormat.DesyncPath,
            ChaosAnomalyTypes.CrossFormat.OrphanAttachment,
            ChaosAnomalyTypes.CrossFormat.MissingChild,
            ChaosAnomalyTypes.CrossFormat.BrokenParent,
        ];
        string chosenType = allTypes[rawTypeIndex % allTypes.Length];

        var datLines = new List<string>
        {
            "\u00feDOCID\u00fe\u0014\u00feBATES_NUMBER\u00fe\u0014\u00fePARENTDOCID\u00fe\u0014\u00feIMAGE_PATH\u00fe\u0014\u00fePAGECOUNT\u00fe\u0014\u00feVOLUME\u00fe",
            "\u00feDOC00000001\u00fe\u0014\u00feTEST00000001\u00fe\u0014\u00fe\u00fe\u0014\u00feIMAGES\\VOL001\\DOC00000001.tif\u00fe\u0014\u00fe1\u00fe\u0014\u00feVOL001\u00fe",
            "\u00feDOC00000002\u00fe\u0014\u00feTEST00000002\u00fe\u0014\u00feDOC00000001\u00fe\u0014\u00feIMAGES\\VOL001\\DOC00000002_001.tif\u00fe\u0014\u00fe2\u00fe\u0014\u00feVOL001\u00fe",
            "\u00feDOC00000003\u00fe\u0014\u00feTEST00000003\u00fe\u0014\u00fe\u00fe\u0014\u00feIMAGES\\VOL001\\DOC00000003.tif\u00fe\u0014\u00fe1\u00fe\u0014\u00feVOL001\u00fe",
        };

        var optLines = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002_001.tif,Y,,,2",
            "TEST00000002_002,VOL001,IMAGES\\VOL001\\DOC00000002_002.tif,,,,",
            "TEST00000003,VOL001,IMAGES\\VOL001\\DOC00000003.tif,Y,,,1",
        };

        var result1 = CrossFormatAnomalyApplier.Apply(new List<string>(datLines), new List<string>(optLines), scenarioOrTypes: chosenType, chaosAmount: "1", seed: seed);
        var result2 = CrossFormatAnomalyApplier.Apply(new List<string>(datLines), new List<string>(optLines), scenarioOrTypes: chosenType, chaosAmount: "1", seed: seed);

        static void AssertFinalAuditLine(
            IReadOnlyList<string> lines,
            string file,
            string recordId,
            string lineNumber)
        {
            string marker = file == "loadfile.dat"
                ? $"\u00fe{recordId}\u00fe"
                : $"{recordId},";
            int emittedLineNumber = Array.FindIndex(
                lines.ToArray(),
                line => line.StartsWith(marker, StringComparison.Ordinal)) + 1;
            long reportedLineNumber = long.Parse(lineNumber, CultureInfo.InvariantCulture);

            if (emittedLineNumber > 0)
            {
                Assert.Equal((long)emittedLineNumber, reportedLineNumber);
            }
            else
            {
                Assert.InRange(reportedLineNumber, 1, lines.Count + 1L);
            }
        }

        // Determinism
        Assert.Equal(result1.DatLines, result2.DatLines);
        Assert.Equal(result1.OptLines, result2.OptLines);
        Assert.Equal(result1.Anomalies.Count, result2.Anomalies.Count);

        // Invariants on anomalies and reciprocal peers
        Assert.NotEmpty(result1.Anomalies);
        foreach (var anomaly in result1.Anomalies)
        {
            Assert.True(anomaly.File == "loadfile.dat" || anomaly.File == "loadfile.opt");
            Assert.NotNull(anomaly.Peer);
            Assert.NotEqual(anomaly.File, anomaly.Peer.File);
            Assert.False(string.IsNullOrEmpty(anomaly.RecordID));
            Assert.False(string.IsNullOrEmpty(anomaly.Peer.RecordID));
            Assert.False(string.IsNullOrEmpty(anomaly.LineNumber));
            Assert.False(string.IsNullOrEmpty(anomaly.Peer.LineNumber));

            var anomalyLines = anomaly.File == "loadfile.dat" ? result1.DatLines : result1.OptLines;
            AssertFinalAuditLine(anomalyLines, anomaly.File!, anomaly.RecordID, anomaly.LineNumber);
            var peerLines = anomaly.Peer.File == "loadfile.dat" ? result1.DatLines : result1.OptLines;
            AssertFinalAuditLine(peerLines, anomaly.Peer.File, anomaly.Peer.RecordID, anomaly.Peer.LineNumber);
        }

        if (chosenType == ChaosAnomalyTypes.CrossFormat.DesyncOrder)
        {
            ChaosAnomaly optAnomaly = Assert.Single(result1.OptAnomalies);
            int emittedLineNumber = Array.FindIndex(
                result1.OptLines.ToArray(),
                line => line.StartsWith($"{optAnomaly.RecordID},", StringComparison.Ordinal)) + 1;

            Assert.True(emittedLineNumber > 0);
            Assert.Equal(emittedLineNumber.ToString(CultureInfo.InvariantCulture), optAnomaly.LineNumber);
            Assert.Equal(optAnomaly.LineNumber, Assert.Single(result1.DatAnomalies).Peer!.LineNumber);
        }
    }

    [Property]
    public void ProductionMetadataBudget_SanitizeAndValidate_ShouldEnforceAsciiAndByteBudget(
        PathSegmentPropertyInput segmentInput,
        ushort repeatFactor)
    {
        string raw = $"{segmentInput.RawText}\r\n\t\u0001\u007f\u00e9\u2603";
        var capture = new ManifestCaptureMaterializer();
        var request = new FileGenerationRequest();

        _ = ProductionManifestWriter.WriteAsync(
            productionPath: "/virtual/prod",
            request: request,
            batesStart: "DOC00000001",
            batesEnd: "DOC00000010",
            volumeCount: 1,
            generationTime: TimeSpan.FromMilliseconds(50),
            productionId: raw,
            materializer: capture).GetAwaiter().GetResult();

        Assert.False(string.IsNullOrEmpty(capture.LastJson));
        ProductionManifest? manifest = JsonSerializer.Deserialize<ProductionManifest>(capture.LastJson);
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Metadata);

        string sanitized = manifest.Metadata["production_id"];
        foreach (char c in sanitized)
        {
            bool validChar = (c == '\t') || (c >= '\u0020' && c <= '\u007e');
            Assert.True(validChar);
        }

        Assert.DoesNotContain('\r', sanitized);
        Assert.DoesNotContain('\n', sanitized);
        Assert.Equal(raw.Length, sanitized.Length);
        Assert.Equal(sanitized.Length, Encoding.UTF8.GetByteCount(sanitized));

        string key = "production_id";
        long singlePairBytes = ProductionMetadataBudget.PairBytes(key, sanitized);
        Assert.Equal(Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(sanitized), singlePairBytes);

        int count = (repeatFactor % 300) + 1;
        string repeatedValue = new string('A', count * 30);
        long totalBytes = ProductionMetadataBudget.PairBytes(key, repeatedValue);
        bool withinBudget = totalBytes <= ProductionMetadataBudget.MaxBytes;

        Assert.Equal((key.Length + repeatedValue.Length) <= ProductionMetadataBudget.MaxBytes, withinBudget);

        if (repeatedValue.Length > ProductionMetadataBudget.MaxBytes)
        {
            Assert.Throws<InvalidOperationException>(() =>
                ProductionManifestWriter.WriteAsync(
                    productionPath: "/virtual/prod",
                    request: request,
                    batesStart: "DOC00000001",
                    batesEnd: "DOC00000010",
                    volumeCount: 1,
                    generationTime: TimeSpan.FromMilliseconds(10),
                    productionId: repeatedValue,
                    materializer: capture).GetAwaiter().GetResult());
        }
    }

    [Property]
    public void ComparisonEngine_Compare_IdenticalManifests_ShouldYieldAllUnchanged(
        BatesPropertyInput batesInput,
        bool reproductionMode)
    {
        int count = (batesInput.Count % 60) + 1;
        List<ComparisonRecord> priorRecords = BuildManifestRecords(batesInput.Prefix, batesInput.Start, batesInput.Digits, count, "PROD_001");
        List<ComparisonRecord> newRecords = BuildManifestRecords(batesInput.Prefix, batesInput.Start, batesInput.Digits, count, "PROD_001");

        string mode = reproductionMode ? "reproduction" : "replacement";
        ComparisonResult result = ComparisonEngine.PerformComparison(
            priorRecords,
            newRecords,
            mode,
            priorDuplicates: [],
            newDuplicates: [],
            priorPaths: ["prior/_manifest.json"],
            newPath: "new/_manifest.json");

        Assert.Equal(count, result.Summary.TotalPriorRecords);
        Assert.Equal(count, result.Summary.TotalNewRecords);
        Assert.Equal(count, result.Summary.UnchangedCount);
        Assert.Equal(0, result.Summary.AddedCount);
        Assert.Equal(0, result.Summary.RemovedCount);
        Assert.Equal(0, result.Summary.ChangedCount);
        Assert.Equal(0, result.Summary.ReplacedCount);
        Assert.Equal(0, result.Summary.DuplicateCount);
    }

    [Property]
    public void ComparisonEngine_Compare_ModifiedManifests_ShouldConserveRecordCounts(
        BatesPropertyInput batesInput,
        byte rawUnchanged,
        byte rawChanged,
        byte rawReplaced,
        byte rawRemoved,
        byte rawAdded)
    {
        int unchangedCount = rawUnchanged % 15;
        int changedCount = rawChanged % 15;
        int replacedCount = rawReplaced % 15;
        int removedCount = rawRemoved % 15;
        int addedCount = rawAdded % 15;

        int priorCount = unchangedCount + changedCount + replacedCount + removedCount;
        int newCount = unchangedCount + changedCount + replacedCount + addedCount;

        var priorRecords = new List<ComparisonRecord>(priorCount);
        var newRecords = new List<ComparisonRecord>(newCount);

        long cursor = batesInput.Start;
        int controlCursor = 1;

        for (int i = 0; i < unchangedCount; i++)
        {
            ComparisonRecord rec = CreateRecord(batesInput.Prefix, cursor++, batesInput.Digits, controlCursor++, "VOL001", "HASH_SAME");
            priorRecords.Add(rec);
            newRecords.Add(CloneRecord(rec));
        }

        for (int i = 0; i < changedCount; i++)
        {
            ComparisonRecord rec = CreateRecord(batesInput.Prefix, cursor++, batesInput.Digits, controlCursor++, "VOL001", "HASH_BEFORE");
            priorRecords.Add(rec);
            ComparisonRecord modified = CloneRecord(rec);
            modified.Hash = "HASH_AFTER";
            newRecords.Add(modified);
        }

        long replaceNewBatesBase = batesInput.Start + 1_000_000L;
        for (int i = 0; i < replacedCount; i++)
        {
            int controlIdx = controlCursor++;
            ComparisonRecord priorRec = CreateRecord(batesInput.Prefix, cursor++, batesInput.Digits, controlIdx, "VOL001", "HASH_ORIG");
            ComparisonRecord newRec = CreateRecord(batesInput.Prefix, replaceNewBatesBase + i, batesInput.Digits, controlIdx, "VOL001", "HASH_REPL");
            priorRecords.Add(priorRec);
            newRecords.Add(newRec);
        }

        for (int i = 0; i < removedCount; i++)
        {
            priorRecords.Add(CreateRecord(batesInput.Prefix, cursor++, batesInput.Digits, controlCursor++, "VOL001", "HASH_REM"));
        }

        long addedBatesBase = batesInput.Start + 2_000_000L;
        for (int i = 0; i < addedCount; i++)
        {
            newRecords.Add(CreateRecord(batesInput.Prefix, addedBatesBase + i, batesInput.Digits, controlCursor++, "VOL002", "HASH_ADD"));
        }

        List<DuplicateDetail> priorDups = ComparisonEngine.FindDuplicates(priorRecords, "prior");
        List<DuplicateDetail> newDups = ComparisonEngine.FindDuplicates(newRecords, "new");
        Assert.Empty(priorDups);
        Assert.Empty(newDups);

        ComparisonResult result = ComparisonEngine.PerformComparison(
            priorRecords,
            newRecords,
            "replacement",
            priorDups,
            newDups,
            ["prior/_manifest.json"],
            "new/_manifest.json");

        Assert.Equal(unchangedCount, result.Summary.UnchangedCount);
        Assert.Equal(changedCount, result.Summary.ChangedCount);
        Assert.Equal(replacedCount, result.Summary.ReplacedCount);
        Assert.Equal(removedCount, result.Summary.RemovedCount);
        Assert.Equal(addedCount, result.Summary.AddedCount);

        Assert.Equal(
            result.Summary.TotalNewRecords,
            result.Summary.UnchangedCount + result.Summary.ChangedCount + result.Summary.ReplacedCount + result.Summary.AddedCount);
        Assert.Equal(
            result.Summary.TotalPriorRecords,
            result.Summary.UnchangedCount + result.Summary.ChangedCount + result.Summary.ReplacedCount + result.Summary.RemovedCount);
    }

    [Property]
    public void ComparisonEngine_Compare_SupplementalMode_ShouldNeverReportRemoved(
        BatesPropertyInput batesInput,
        byte rawPriorCount,
        byte rawOverlapCount,
        byte rawFreshCount)
    {
        int priorCount = (rawPriorCount % 25) + 1;
        int overlapCount = rawOverlapCount % (priorCount + 1);
        int freshCount = (rawFreshCount % 25) + 1;

        List<ComparisonRecord> priorRecords = BuildManifestRecords(
            batesInput.Prefix,
            batesInput.Start,
            batesInput.Digits,
            priorCount,
            "PROD_PRIOR");

        var newRecords = new List<ComparisonRecord>(overlapCount + freshCount);
        for (int i = 0; i < overlapCount; i++)
        {
            newRecords.Add(CloneRecord(priorRecords[i]));
        }

        long freshStart = batesInput.Start + priorCount + 10;
        for (int i = 0; i < freshCount; i++)
        {
            newRecords.Add(CreateRecord(
                batesInput.Prefix,
                freshStart + i,
                batesInput.Digits,
                priorCount + i + 1,
                "VOL002",
                $"HASH_FRESH_{i.ToString(CultureInfo.InvariantCulture)}"));
        }

        ComparisonResult result = ComparisonEngine.PerformComparison(
            priorRecords,
            newRecords,
            "supplemental",
            priorDuplicates: [],
            newDuplicates: [],
            priorPaths: ["prior/_manifest.json"],
            newPath: "new/_manifest.json");

        Assert.Equal(0, result.Summary.RemovedCount);
        Assert.Empty(result.Details.Removed);
        Assert.Equal(overlapCount, result.Summary.DuplicateCount);
        Assert.Equal(overlapCount + freshCount, result.Summary.AddedCount);
        Assert.All(result.Details.Duplicates, d => Assert.Contains("Supplemental overlap", d.Message, StringComparison.Ordinal));
    }

    [Property]
    public void BatesRangeAnalyzer_AnalyzeBatesRanges_ShouldPreserveRangeAndVolumeInvariants(
        BatesPropertyInput batesInput,
        byte rawGapSize,
        bool mutateVolume)
    {
        int firstSegmentCount = (batesInput.Count % 20) + 2;
        int gapSize = (rawGapSize % 10) + 1;
        int secondSegmentCount = (batesInput.Count % 15) + 1;

        string formatted = BatesRangeAnalyzer.FormatBates(batesInput.Start, batesInput.Prefix, batesInput.Digits);
        bool parsed = BatesRangeAnalyzer.TryParseBates(formatted, out string parsedPrefix, out long parsedNum, out int parsedDigits);
        Assert.True(parsed);
        Assert.Equal(batesInput.Prefix, parsedPrefix);
        Assert.Equal(batesInput.Start, parsedNum);
        Assert.True(parsedDigits >= batesInput.Digits);

        var priorRecords = new List<ComparisonRecord>();
        var newRecords = new List<ComparisonRecord>();

        for (int i = 0; i < firstSegmentCount; i++)
        {
            ComparisonRecord r = CreateRecord(batesInput.Prefix, batesInput.Start + i, batesInput.Digits, i + 1, "VOL001", "HASH_1");
            priorRecords.Add(r);
            ComparisonRecord nr = CloneRecord(r);
            if (mutateVolume && i == 0)
            {
                nr.Hash = "HASH_CHANGED";
            }

            newRecords.Add(nr);
        }

        // Prior-only volume VOL_REMOVED
        priorRecords.Add(CreateRecord(batesInput.Prefix, batesInput.Start + 500_000L, batesInput.Digits, 90_001, "VOL_REMOVED", "HASH_REM"));

        // Second segment in newRecords separated by gapSize skipped Bates numbers in VOL_ADDED
        long secondStart = batesInput.Start + firstSegmentCount + gapSize;
        for (int i = 0; i < secondSegmentCount; i++)
        {
            newRecords.Add(CreateRecord(
                batesInput.Prefix,
                secondStart + i,
                batesInput.Digits,
                firstSegmentCount + i + 1,
                "VOL_ADDED",
                "HASH_2"));
        }

        var batesAnalysis = new BatesAnalysis();
        var details = new ResultDetails();
        BatesRangeAnalyzer.AnalyzeBatesRanges(priorRecords, newRecords, "replacement", batesAnalysis, details);

        Assert.Single(details.Skipped);
        Assert.Equal(gapSize, batesAnalysis.TotalSkippedBates);
        Assert.Single(batesAnalysis.Overlaps);

        var volumeResults = new List<VolumeResult>();
        VolumeAnalyzer.AnalyzeVolumes(priorRecords, newRecords, volumeResults);

        Assert.Equal(3, volumeResults.Count);
        VolumeResult vol1 = Assert.Single(volumeResults, v => string.Equals(v.VolumeName, "VOL001", StringComparison.Ordinal));
        VolumeResult volRemoved = Assert.Single(volumeResults, v => string.Equals(v.VolumeName, "VOL_REMOVED", StringComparison.Ordinal));
        VolumeResult volAdded = Assert.Single(volumeResults, v => string.Equals(v.VolumeName, "VOL_ADDED", StringComparison.Ordinal));

        Assert.Equal(mutateVolume ? "changed" : "unchanged", vol1.Status);
        Assert.NotEmpty(vol1.PriorBatesRange);
        Assert.Equal(vol1.PriorBatesRange, vol1.NewBatesRange);

        Assert.Equal("removed", volRemoved.Status);
        Assert.NotEmpty(volRemoved.PriorBatesRange);
        Assert.Equal(string.Empty, volRemoved.NewBatesRange);

        Assert.Equal("added", volAdded.Status);
        Assert.Equal(string.Empty, volAdded.PriorBatesRange);
        Assert.NotEmpty(volAdded.NewBatesRange);
    }

    [Property]
    public void ComparisonEngine_FindDuplicates_ShouldDetectDuplicateBatesAndControlNumbers(
        BatesPropertyInput batesInput,
        byte rawInterSetGap)
    {
        int count = (batesInput.Count % 15) + 2;
        int interSetGap = (rawInterSetGap % 10) + 2;

        List<ComparisonRecord> records = BuildManifestRecords(
            batesInput.Prefix,
            batesInput.Start,
            batesInput.Digits,
            count,
            "PROD_DUP");

        // Inject one duplicate Bates Number and one duplicate Control Number
        ComparisonRecord dupBates = CreateRecord(batesInput.Prefix, batesInput.Start, batesInput.Digits, count + 100, "VOL001", "HASH_DUP_B");
        ComparisonRecord dupControl = CreateRecord(batesInput.Prefix, batesInput.Start + count + 100, batesInput.Digits, 1, "VOL001", "HASH_DUP_C");
        records.Add(dupBates);
        records.Add(dupControl);

        List<DuplicateDetail> duplicates = ComparisonEngine.FindDuplicates(records, "prior");
        Assert.Equal(2, duplicates.Count);
        Assert.Contains(duplicates, d => d.Message.Contains("Duplicate Bates Number", StringComparison.Ordinal));
        Assert.Contains(duplicates, d => d.Message.Contains("Duplicate Control Number", StringComparison.Ordinal));

        // Verify supplemental inter-set gap detection in BatesRangeAnalyzer
        List<ComparisonRecord> priorSet = BuildManifestRecords(batesInput.Prefix, batesInput.Start, batesInput.Digits, count, "PROD_1");
        long newStart = batesInput.Start + count - 1 + interSetGap;
        List<ComparisonRecord> newSet = BuildManifestRecords(batesInput.Prefix, newStart, batesInput.Digits, count, "PROD_2");

        var supplementalBates = new BatesAnalysis();
        var supplementalDetails = new ResultDetails();
        BatesRangeAnalyzer.AnalyzeBatesRanges(priorSet, newSet, "supplemental", supplementalBates, supplementalDetails);

        Assert.Empty(supplementalDetails.Skipped);
        Assert.Single(supplementalBates.Gaps);
        Assert.Equal(interSetGap - 1, supplementalBates.TotalSkippedBates);
    }

    [Property]
    public void NumberGenerator_Generate_ShouldRespectRangeAndTextGeneratorInvariants(
        int rawA,
        int rawB,
        byte rawDistIndex,
        byte rawGeneratorIndex,
        ChaosSeedPropertyInput seedInput)
    {
        int min = Math.Clamp(Math.Min(rawA, rawB), -10_000, 10_000);
        int max = Math.Clamp(Math.Max(rawA, rawB), -10_000, 10_000);

        string?[] distributions = [null, "uniform", "exponential", "gaussian", "normal"];
        string? dist = distributions[rawDistIndex % distributions.Length];

        var numCol = new ColumnDefinition
        {
            Name = "CUSTOM_NUM",
            Type = "number",
            Distribution = dist,
            Range = new RangeConfig { Min = min, Max = max },
        };

        var numGen = new NumberGenerator(numCol);
        var rng = new Random(seedInput.Seed);
        int dataLength = seedInput.DataLength;
        int pageCount = seedInput.PageCount;
        int folderNumber = (rawDistIndex % 20) + 1;
        string[] extensions = ["pdf", "docx", "xlsx", "eml", "tiff"];
        string expectedExt = extensions[rawGeneratorIndex % extensions.Length];
        string fileName = $"DOC{seedInput.FileOrdinal.ToString("D8", CultureInfo.InvariantCulture)}.{expectedExt}";
        string filePathInZip = $"VOL001/NATIVES/{folderNumber.ToString("D3", CultureInfo.InvariantCulture)}/{fileName}";
        var workItem = new FileWorkItem
        {
            Index = 1,
            FolderNumber = folderNumber,
            FileName = fileName,
            FilePathInZip = filePathInZip,
        };
        var fileData = new FileData
        {
            WorkItem = workItem,
            DataLength = dataLength,
            PageCount = pageCount,
        };

        for (int docIdx = 1; docIdx <= 10; docIdx++)
        {
            var ctx = new ColumnGenerationContext
            {
                NativeFileIndex = docIdx,
                FolderNumber = folderNumber,
                DocumentIndex = docIdx,
                Seeded = rng,
                Now = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                FileData = fileData,
                ProfileColumn = numCol,
            };

            string rawVal = numGen.Generate(ctx);
            int parsed = int.Parse(rawVal, CultureInfo.InvariantCulture);
            Assert.InRange(parsed, min, max);
        }

        // Verify inverted range (max < min) returns min
        var invertedGen = new NumberGenerator(new ColumnDefinition
        {
            Name = "INVERTED_NUM",
            Type = "number",
            Range = new RangeConfig { Min = min + 10, Max = min },
        });
        var baseCtx = new ColumnGenerationContext
        {
            NativeFileIndex = 1,
            FolderNumber = folderNumber,
            DocumentIndex = 1,
            Seeded = rng,
            Now = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            FileData = fileData,
        };
        Assert.Equal((min + 10).ToString(CultureInfo.InvariantCulture), invertedGen.Generate(baseCtx));

        // Verify special FILESIZE and PAGECOUNT columns
        Assert.Equal(dataLength.ToString(CultureInfo.InvariantCulture), new NumberGenerator(new ColumnDefinition { Name = "FILESIZE", Type = "number" }).Generate(baseCtx));
        Assert.Equal(pageCount.ToString(CultureInfo.InvariantCulture), new NumberGenerator(new ColumnDefinition { Name = "PAGECOUNT", Type = "number" }).Generate(baseCtx));

        // Exercise TextGenerator named generators and special file columns
        string[] namedGenerators = ["emailsubject", "md5hash", "sha1hash", "sha256hash", "reviewnote", "encoding", "timezone", "customgen"];
        string chosenGen = namedGenerators[rawGeneratorIndex % namedGenerators.Length];
        var textCol = new ColumnDefinition
        {
            Name = "TEXT_COL",
            Type = "text",
            Generator = chosenGen,
        };
        var textGen = new TextGenerator(textCol, dataSourceValues: null, distributionIndices: null);
        string generatedText = textGen.Generate(baseCtx);
        Assert.False(string.IsNullOrEmpty(generatedText));

        if (string.Equals(chosenGen, "md5hash", StringComparison.Ordinal))
        {
            Assert.Equal(32, generatedText.Length);
        }
        else if (string.Equals(chosenGen, "sha1hash", StringComparison.Ordinal))
        {
            Assert.Equal(40, generatedText.Length);
        }
        else if (string.Equals(chosenGen, "sha256hash", StringComparison.Ordinal))
        {
            Assert.Equal(64, generatedText.Length);
        }

        Assert.Equal(workItem.FilePathInZip, new TextGenerator(new ColumnDefinition { Name = "FILEPATH" }, null, null).Generate(baseCtx));
        Assert.Equal(fileName, new TextGenerator(new ColumnDefinition { Name = "FILENAME" }, null, null).Generate(baseCtx));
        Assert.Equal(expectedExt, new TextGenerator(new ColumnDefinition { Name = "FILEEXT" }, null, null).Generate(baseCtx));
    }

    [Property]
    public void DataGenerator_GenerateRow_ShouldRespectDistributionsAndEmptyRates(
        byte rawDistIndex,
        bool forceAllEmpty,
        int seed)
    {
        string[] distributions = ["uniform", "pareto", "weighted", "gaussian", "normal", "exponential"];
        string dist = distributions[rawDistIndex % distributions.Length];
        string[] allowedValues = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

        int emptyPct = forceAllEmpty ? 100 : 0;
        var profile = new ColumnProfile
        {
            Name = "prop-test-profile",
            Settings = new ProfileSettings
            {
                EmptyValuePercentage = emptyPct,
            },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["custodians"] = new DataSourceConfig
                {
                    Count = allowedValues.Length,
                    Distribution = dist,
                    Values = [.. allowedValues],
                    Weights = [50, 25, 15, 7, 3],
                },
            },
            Columns =
            [
                new ColumnDefinition
                {
                    Name = "REQ_CUSTODIAN",
                    Type = "text",
                    DataSource = "custodians",
                    Required = true,
                },
                new ColumnDefinition
                {
                    Name = "OPT_CUSTODIAN",
                    Type = "text",
                    DataSource = "custodians",
                    Required = false,
                    EmptyPercentage = emptyPct,
                },
            ],
        };

        var dataGen = new DataGenerator(
            profile,
            seed: seed,
            now: new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            custodianCountOverride: 4,
            emptyPercentageOverride: emptyPct);

        var workItem = new FileWorkItem { Index = 1, FolderNumber = 1, FileName = "DOC1.pdf", FilePathInZip = "VOL001/DOC1.pdf" };
        var fileData = new FileData { WorkItem = workItem };

        for (int i = 0; i < 15; i++)
        {
            Dictionary<string, string> row = dataGen.GenerateRow(workItem, fileData);
            Assert.Contains(row["REQ_CUSTODIAN"], allowedValues.Take(4));

            if (forceAllEmpty)
            {
                Assert.Equal(string.Empty, row["OPT_CUSTODIAN"]);
            }
            else
            {
                Assert.Contains(row["OPT_CUSTODIAN"], allowedValues.Take(4));
            }
        }
    }

    private static List<ComparisonRecord> BuildManifestRecords(
        string prefix,
        long start,
        int digits,
        int count,
        string productionId)
    {
        var list = new List<ComparisonRecord>(count);
        for (int i = 0; i < count; i++)
        {
            ComparisonRecord rec = CreateRecord(prefix, start + i, digits, i + 1, "VOL001", $"HASH_{i.ToString(CultureInfo.InvariantCulture)}");
            rec.ProductionId = productionId;
            list.Add(rec);
        }

        return list;
    }

    private static ComparisonRecord CreateRecord(
        string prefix,
        long batesNum,
        int digits,
        int controlIndex,
        string volume,
        string hash)
    {
        string bates = BatesRangeAnalyzer.FormatBates(batesNum, prefix, digits);
        string control = $"CTRL{controlIndex.ToString("D6", CultureInfo.InvariantCulture)}";
        return new ComparisonRecord
        {
            BatesNumber = bates,
            ControlNumber = control,
            FilePath = $"NATIVES/001/{bates}.pdf",
            Hash = hash,
            Volume = volume,
            ManifestPath = "_manifest.json",
            ProductionId = "PROD_SET",
            SourceLine = controlIndex + 1,
        };
    }

    private static ComparisonRecord CloneRecord(ComparisonRecord source)
    {
        return new ComparisonRecord
        {
            BatesNumber = source.BatesNumber,
            ControlNumber = source.ControlNumber,
            FilePath = source.FilePath,
            Hash = source.Hash,
            Volume = source.Volume,
            ManifestPath = source.ManifestPath,
            ProductionId = source.ProductionId,
            SourceLine = source.SourceLine,
        };
    }

    private sealed class ManifestCaptureMaterializer : IFileMaterializer
    {
        public string LastJson { get; private set; } = string.Empty;

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default)
        {
            this.LastJson = text;
            return Task.CompletedTask;
        }

        public Stream OpenWriteStream(string path) => Stream.Null;

        public void AddFileData(FileData data)
        {
        }

        public Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task CreateZipAsync(string sourceDir, string zipPath, Config.ZipCompressionMethod method, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public bool ShouldValidate => false;
    }
}
