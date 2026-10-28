using Xunit;
using Zipper.LoadFiles;
using Zipper.Validation;

namespace Zipper.Tests;

public class DatAnomalyApplierTests
{
    private static readonly IReadOnlyList<string> RecordColumns =
        ["Control Number", "File Path", "Date Created", "Author"];

    private static LoadFileRecord CreateRecord() => new()
    {
        Columns = RecordColumns,
        Values = ["DOC00000001", "NATIVES\\001\\DOC00000001.pdf", "2024-01-15", "Jane Doe"],
        RecordId = "DOC00000001",
    };

    private static readonly DatSerializer Serializer = new('|', '"', "~");

    [Fact]
    public void Apply_MixedDelimiters_NoDelimiterInLine_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        // Arrange: line with no delimiter
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NoDelimiterHere";

        // Act
        string result = applier.Apply(1, line, "DOC001", "mixed-delimiters", anomalies);

        // Assert
        Assert.Equal(line, result); // Line unchanged
        Assert.Empty(anomalies); // No anomaly recorded
    }

    [Fact]
    public void Apply_MixedDelimiters_WithDelimiter_ReplacesOneDelimiter_AndRecordsAnomaly()
    {
        // Arrange: line with delimiters
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe\u0014\u00feValue3\u00fe";

        // Act
        string result = applier.Apply(1, line, "DOC001", "mixed-delimiters", anomalies);

        // Assert
        Assert.NotEqual(line, result); // Line changed
        Assert.Single(anomalies);
        Assert.Equal("mixed-delimiters", anomalies[0].ErrorType);
        Assert.Contains("Delimiter", anomalies[0].Column, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ColumnShift_AddTrue_AlwaysChangesLine_AndRecordsAnomaly()
    {
        // Arrange: line with delimiter
        const string line = "Value1\u0014Value2";

        // Run multiple times to catch both add and remove cases
        bool sawAdd = false;
        bool sawRemove = false;

        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new DatAnomalyApplier(new Random(seed), "\u0014", "\u00fe", "\r\n");
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "DOC001", "columns", testAnomalies);

            if (result != line)
            {
                if (result.Count(c => c == '\u0014') > line.Count(c => c == '\u0014'))
                    sawAdd = true;
                else
                    sawRemove = true;

                Assert.Single(testAnomalies);
                Assert.Equal("columns", testAnomalies[0].ErrorType);
            }
        }

        Assert.True(sawAdd, "Should have seen add case");
        Assert.True(sawRemove, "Should have seen remove case");
    }

    [Fact]
    public void Apply_ColumnShift_RemoveWithNoDelimiter_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        // Arrange: line with NO delimiter
        const string line = "NoDelimiterHere";

        // Act - we need to force remove (add=false). 
        // Since it's random, we run multiple seeds until we hit remove.
        bool foundRemoveCase = false;

        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new DatAnomalyApplier(new Random(seed), "\u0014", "\u00fe", "\r\n");
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "DOC001", "columns", testAnomalies);

            if (result == line) // remove case with no delimiter = no change
            {
                foundRemoveCase = true;
                // This is the bug: anomaly should NOT be recorded when line unchanged
                Assert.Empty(testAnomalies); // Currently fails: anomaly is recorded
                break;
            }
        }

        Assert.True(foundRemoveCase, "Should have found a seed that picks remove");
    }

    [Fact]
    public void Apply_Quotes_NoQuoteDelimiter_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        // Arrange: quote delimiter is empty
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", string.Empty, "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "Value1\u0014Value2";

        // Act
        string result = applier.Apply(1, line, "DOC001", "quotes", anomalies);

        // Assert
        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void Apply_Eol_AlwaysChangesLine_AndRecordsAnomaly()
    {
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe";

        string result = applier.Apply(1, line, "DOC001", "eol", anomalies);

        Assert.Contains("\r\n", result, StringComparison.Ordinal);
        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("eol", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_PathPointer_ReplacesPathWithMissingPath_AndPathReconciliationFindsIt()
    {
        var record = CreateRecord();
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n");

        string result = applier.Apply(2, line, record.RecordId, "path-pointer", anomalies, record, Serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.NotEqual(record.Values[1], fields[1]);
        Assert.EndsWith("_MISSING.pdf", fields[1], StringComparison.Ordinal);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("path-pointer", anomaly.ErrorType);
        Assert.Equal("File Path", anomaly.Column);

        string archiveEntry = record.Values[1].Replace('\\', '/');
        var validator = new PathReconciliationValidator();
        var originalValidation = new ValidationResult();
        validator.Validate([record.Values[1]], [archiveEntry], "test.dat", originalValidation);
        Assert.Empty(originalValidation.Findings);

        var validation = new ValidationResult();
        validator.Validate([fields[1]], [archiveEntry], "test.dat", validation);
        Assert.Contains(validation.Findings, finding => finding.Category == "PathReconciliation");
    }

    [Fact]
    public void Apply_PathPointer_WhenTextPathPrecedesFilePath_ShouldTargetFilePath()
    {
        var record = new LoadFileRecord
        {
            Columns = ["Text Path", "File Path"],
            Values = ["TEXT\\001\\DOC00000001.txt", "NATIVES\\001\\DOC00000001.pdf"],
            RecordId = "DOC00000001",
        };
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n");

        string result = applier.Apply(2, line, record.RecordId, "path-pointer", anomalies, record, Serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Equal(record.Values[0], fields[0]);
        Assert.EndsWith("_MISSING.pdf", fields[1], StringComparison.Ordinal);
        Assert.Equal("File Path", Assert.Single(anomalies).Column);
    }

    [Fact]
    public void Apply_PathPointer_WhenRecordColumnsChange_RecomputesFilePathIndex()
    {
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n");
        var firstRecord = new LoadFileRecord
        {
            Columns = ["File Path", "Author"],
            Values = ["NATIVES\\001\\DOC00000001.pdf", "Jane Doe"],
            RecordId = "DOC00000001",
        };
        var firstAnomalies = new List<ChaosAnomaly>();
        _ = applier.Apply(
            2,
            Serializer.RenderRecord(firstRecord),
            firstRecord.RecordId,
            "path-pointer",
            firstAnomalies,
            firstRecord,
            Serializer);

        var secondRecord = new LoadFileRecord
        {
            Columns = ["Author", "File Path"],
            Values = ["Jane Doe", "NATIVES\\001\\DOC00000002.pdf"],
            RecordId = "DOC00000002",
        };
        var secondAnomalies = new List<ChaosAnomaly>();
        string result = applier.Apply(
            3,
            Serializer.RenderRecord(secondRecord),
            secondRecord.RecordId,
            "path-pointer",
            secondAnomalies,
            secondRecord,
            Serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Equal("Jane Doe", fields[0]);
        Assert.EndsWith("_MISSING.pdf", fields[1], StringComparison.Ordinal);
        Assert.Equal("File Path", Assert.Single(secondAnomalies).Column);
    }

    [Fact]
    public void Apply_PathPointer_WhenMissingPathCandidatesCollide_EmptiesPathAfterBoundedAttempts()
    {
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "folder/doc.pdf",
            "folder/doc_MISSING.pdf",
        };
        for (int collisionIndex = 1; collisionIndex <= 64; collisionIndex++)
        {
            sourcePaths.Add($"folder/doc_MISSING_{collisionIndex}.pdf");
        }

        var record = new LoadFileRecord
        {
            Columns = ["File Path"],
            Values = ["folder/doc.pdf"],
            RecordId = "DOC00000001",
        };
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(
            new Random(42),
            "|",
            "\"",
            "\r\n",
            sourceFilePaths: sourcePaths);

        string result = applier.Apply(
            2,
            Serializer.RenderRecord(record),
            record.RecordId,
            "path-pointer",
            anomalies,
            record,
            Serializer);

        Assert.Equal(string.Empty, Assert.Single(DatLineParser.Parse(result, '|', '"')));
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("File Path", anomaly.Column);
        Assert.Contains("PathReconciliation:", anomaly.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PathPointer_EmptyOriginalPath_ReplacesAndAuditsPath()
    {
        var record = new LoadFileRecord
        {
            Columns = ["File Path"],
            Values = [string.Empty],
            RecordId = "DOC00000001",
        };
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n");

        string result = applier.Apply(
            2,
            Serializer.RenderRecord(record),
            record.RecordId,
            "path-pointer",
            anomalies,
            record,
            Serializer);

        Assert.Equal("NATIVES\\__MISSING__\\DOCUMENT.pdf", Assert.Single(DatLineParser.Parse(result, '|', '"')));
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("File Path", anomaly.Column);
        Assert.Contains("PathReconciliation:", anomaly.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PathPointer_WithoutPathColumn_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        var record = new LoadFileRecord
        {
            Columns = ["Control Number", "Author"],
            Values = ["DOC00000001", "Jane Doe"],
            RecordId = "DOC00000001",
        };
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n");

        string result = applier.Apply(2, line, record.RecordId, "path-pointer", anomalies, record, Serializer);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Theory]
    [InlineData("field-length")]
    [InlineData("null-byte")]
    public void Apply_FieldAnomalies_WithoutEligibleColumn_ReturnUnchangedLine_AndDoNotRecordAnomaly(string type)
    {
        var record = new LoadFileRecord
        {
            Columns = ["Control Number", "File Path", "Date Created"],
            Values = ["DOC00000001", "NATIVES\\001\\DOC00000001.pdf", "2024-01-15"],
            RecordId = "DOC00000001",
        };
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n", ["Date Created"]);

        string result = applier.Apply(2, line, record.RecordId, type, anomalies, record, Serializer);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void Apply_FieldLength_ExceedsCommonLimit_AndRecordsColumn()
    {
        var record = CreateRecord();
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n", ["Date Created"]);

        string result = applier.Apply(2, line, record.RecordId, "field-length", anomalies, record, Serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.True(fields[3].Length > 32_767);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("field-length", anomaly.ErrorType);
        Assert.Equal("Author", anomaly.Column);
    }

    [Fact]
    public void Apply_DateFormat_UsesMalformedDate_AndRecordsColumn()
    {
        var record = CreateRecord();
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n", ["Date Created"]);

        string result = applier.Apply(2, line, record.RecordId, "date-format", anomalies, record, Serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Contains(fields[2], new[] { "00/00/0000", "2018-01-15 a.m.", "2018-044-09" });
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("date-format", anomaly.ErrorType);
        Assert.Equal("Date Created", anomaly.Column);
    }

    [Fact]
    public void Apply_DateFormat_WithoutDateColumn_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        var record = new LoadFileRecord
        {
            Columns = ["Control Number", "File Path", "Author"],
            Values = ["DOC00000001", "NATIVES\\001\\DOC00000001.pdf", "Jane Doe"],
            RecordId = "DOC00000001",
        };
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n", ["Date Created"]);

        string result = applier.Apply(2, line, record.RecordId, "date-format", anomalies, record, Serializer);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void Apply_NullByte_InsertsUnsupportedControls_AndRecordsColumn()
    {
        var record = CreateRecord();
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n", ["Date Created"]);

        string result = applier.Apply(2, line, record.RecordId, "null-byte", anomalies, record, Serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Contains('\0', fields[3]);
        Assert.Contains('\u0001', fields[3]);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("null-byte", anomaly.ErrorType);
        Assert.Equal("Author", anomaly.Column);
    }

    [Fact]
    public void Apply_TrailingEmpties_AppendsLongEmptyTail_AndRecordsAnomaly()
    {
        var record = CreateRecord();
        string line = Serializer.RenderRecord(record);
        var anomalies = new List<ChaosAnomaly>();
        var applier = new DatAnomalyApplier(new Random(42), "|", "\"", "\r\n");

        string result = applier.Apply(2, line, record.RecordId, "trailing-empties", anomalies, record, Serializer);

        var fields = DatLineParser.Parse(result, '|', '"');
        Assert.Equal(record.Columns.Count + 4096, fields.Count);
        Assert.All(fields.Skip(record.Columns.Count), Assert.Empty);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("trailing-empties", anomaly.ErrorType);
        Assert.Equal("Trailing empty columns after Author", anomaly.Column);
    }

    [Fact]
    public void Apply_Encoding_DoesNotChangeLine_RecordsAnomalyInGetEncodingAnomaly()
    {
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "Value1\u0014Value2";

        string result = applier.Apply(1, line, "DOC001", "encoding", anomalies);

        Assert.Equal(line, result); // Line unchanged by Apply
        Assert.Empty(anomalies); // No anomaly in Apply

        // Anomaly is recorded in GetEncodingAnomaly
        var bytes = applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.UTF8, anomalies);
        Assert.NotNull(bytes);
        Assert.Single(anomalies);
        Assert.Equal("encoding", anomalies[0].ErrorType);
    }
}

public class OptAnomalyApplierTests
{
    [Fact]
    public void Apply_OptEncoding_DoesNotChangeLine_RecordsAnomalyInGetEncodingAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-encoding", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
        Assert.Null(applier.GetEncodingAnomaly(2, 3, System.Text.Encoding.Latin1, anomalies));

        var bytes = applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.Latin1, anomalies);

        Assert.NotNull(bytes);
        Assert.Equal(System.Text.Encoding.UTF8.GetPreamble(), bytes);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("Boundary 1-2", anomaly.LineNumber);
        Assert.Equal("N/A", anomaly.RecordID);
        Assert.Equal("N/A", anomaly.Column);
        Assert.Equal("opt-encoding", anomaly.ErrorType);
        Assert.Contains("UTF-8 BOM", anomaly.Description, StringComparison.Ordinal);
        Assert.Null(applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.Latin1, anomalies));
    }

    [Fact]
    public void Apply_OptBoundary_ValidLine_FlipsBoundary_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-boundary", anomalies);

        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("opt-boundary", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptBoundary_InvalidLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        // Line with fewer than 4 fields
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001";

        string result = applier.Apply(1, line, "IMG001", "opt-boundary", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies); // Currently fails: anomaly recorded
    }

    [Fact]
    public void Apply_OptColumns_AddComma_AlwaysChangesLine_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        // We can't control the random choice, but add case always changes
        // Run multiple times to catch add case
        bool sawAdd = false;
        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new OptAnomalyApplier(new Random(seed));
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "IMG001", "opt-columns", testAnomalies);

            if (result.Count(c => c == ',') > line.Count(c => c == ','))
            {
                sawAdd = true;
                Assert.Single(testAnomalies);
                Assert.Equal("opt-columns", testAnomalies[0].ErrorType);
                break;
            }
        }
        Assert.True(sawAdd);
    }

    [Fact]
    public void Apply_OptColumns_RemoveComma_NoCommaInLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NO_COMMAS_HERE";

        // We need to hit the remove case. Run multiple seeds.
        bool foundRemoveCase = false;
        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new OptAnomalyApplier(new Random(seed));
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "IMG001", "opt-columns", testAnomalies);

            if (result == line) // remove with no comma = no change
            {
                foundRemoveCase = true;
                Assert.Empty(testAnomalies); // Currently fails: anomaly recorded
                break;
            }
        }
        Assert.True(foundRemoveCase);
    }

    [Fact]
    public void Apply_OptPagecount_ValidLine_CorruptsLastField_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-pagecount", anomalies);

        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("opt-pagecount", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptPagecount_NoCommaInLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NO_COMMAS_HERE";

        string result = applier.Apply(1, line, "IMG001", "opt-pagecount", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies); // Currently fails: anomaly recorded
    }

    [Fact]
    public void Apply_OptPath_ValidLine_CorruptsPath_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-path", anomalies);

        Assert.NotEqual(line, result);
        Assert.Contains("invalid", result, StringComparison.Ordinal);
        Assert.Single(anomalies);
        Assert.Equal("opt-path", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptPath_InvalidLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001"; // Only 2 fields, need 3

        string result = applier.Apply(1, line, "IMG001", "opt-path", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies); // Currently fails: anomaly recorded
    }

    [Fact]
    public void Apply_OptBatesNumber_ValidLine_RemovesBates_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-batesnumber", anomalies);

        Assert.NotEqual(line, result);
        Assert.StartsWith(",", result, StringComparison.Ordinal);
        Assert.Single(anomalies);
        Assert.Equal("opt-batesnumber", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptBatesNumber_NoCommaInLine_ReturnsEmptyString_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NO_COMMAS";

        string result = applier.Apply(1, line, "IMG001", "opt-batesnumber", anomalies);

        // Current behavior: returns empty string (changed)
        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
    }
}

public class CsvAnomalyApplierTests
{
    [Fact]
    public void Apply_CsvQuoteEscape_WhenFieldHasDoubledQuote_ProducesUnbalancedQuote_AndRecordsAnomaly()
    {
        // RFC 4180: "" (doubled) is the valid CSV escape. Chaos breaks it to a single " mid-field
        var applier = new CsvAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        // A valid CSV line where a field contains a doubled-quote escape: "say ""hi""",value
        const string line = "\"say \"\"hi\"\"\",value";

        string result = applier.Apply(1, line, "DOC001", "csv-quote-escape", anomalies);

        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("csv-quote-escape", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_CsvQuoteEscape_WhenNoDoubledQuotePresent_InjectsBrokenQuotedField_AndRecordsAnomaly()
    {
        var applier = new CsvAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "simple,line,no,quotes";

        string result = applier.Apply(1, line, "DOC001", "csv-quote-escape", anomalies);

        Assert.NotEqual(line, result);
        Assert.Contains("\"", result, StringComparison.Ordinal);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("csv-quote-escape", anomaly.ErrorType);
        Assert.StartsWith("Field ", anomaly.Column, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_CsvEmbeddedNewline_InjectsRawNewlineInsideQuotedField_AndRecordsAnomaly()
    {
        var applier = new CsvAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "\"field one\",\"field two\",value";

        string result = applier.Apply(1, line, "DOC001", "csv-embedded-newline", anomalies);

        Assert.NotEqual(line, result);
        Assert.True(result.Contains('\n') || result.Contains('\r'));
        Assert.Single(anomalies);
        Assert.Equal("csv-embedded-newline", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_CsvEmbeddedNewline_WhenNoQuotedField_QuotesFieldAndInjectsNewline_AndRecordsAnomaly()
    {
        var applier = new CsvAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "no,quoted,fields";

        string result = applier.Apply(1, line, "DOC001", "csv-embedded-newline", anomalies);

        Assert.NotEqual(line, result);
        Assert.True(result.Contains('\n') || result.Contains('\r'));
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("csv-embedded-newline", anomaly.ErrorType);
    }

    [Fact]
    public void Apply_CsvBom_DoesNotChangeLine_RecordsAnomalyInGetEncodingAnomaly()
    {
        var applier = new CsvAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "col1,col2,col3";

        string result = applier.Apply(1, line, "DOC001", "csv-bom", anomalies);

        Assert.Equal(line, result);    // line unchanged by Apply
        Assert.Empty(anomalies);       // anomaly recorded via GetEncodingAnomaly

        var bytes = applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.UTF8, anomalies);
        Assert.NotNull(bytes);
        Assert.Single(anomalies);
        Assert.Equal("csv-bom", anomalies[0].ErrorType);
        // Second call for same line returns null (one-shot)
        Assert.Null(applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.UTF8, anomalies));
    }

    [Fact]
    public void Apply_UnknownType_ReturnLineUnchanged()
    {
        var applier = new CsvAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "a,b,c";

        string result = applier.Apply(1, line, "DOC001", "no-such-type", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }
}

public class ConcordanceAnomalyApplierTests
{
    [Fact]
    public void Apply_ConcordanceDelimiter_ReplacesOneFieldDelimiter_AndRecordsAnomaly()
    {
        // Concordance default column delimiter is ASCII 20 (0x14)
        var applier = new ConcordanceAnomalyApplier(new Random(42), '\u0014', '\u00fe');
        var anomalies = new List<ChaosAnomaly>();
        // A valid Concordance line with ASCII 20 as field delimiters
        const string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe\u0014\u00feValue3\u00fe";

        string result = applier.Apply(1, line, "DOC001", "concordance-delimiter", anomalies);

        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("concordance-delimiter", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_ConcordanceDelimiter_WhenNoDelimiterInLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new ConcordanceAnomalyApplier(new Random(42), '\u0014', '\u00fe');
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NoDelimiterHere";

        string result = applier.Apply(1, line, "DOC001", "concordance-delimiter", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void Apply_ConcordanceQuoteEscape_DropsOneQuoteDelimiter_AndRecordsAnomaly()
    {
        // Concordance quote delimiter is ASCII 254 (þ). Chaos drops one to break quoting.
        var applier = new ConcordanceAnomalyApplier(new Random(42), '\u0014', '\u00fe');
        var anomalies = new List<ChaosAnomaly>();
        const string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe";

        string result = applier.Apply(1, line, "DOC001", "concordance-quote-escape", anomalies);

        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("concordance-quote-escape", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_ConcordanceQuoteEscape_WhenNoQuoteDelimiter_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new ConcordanceAnomalyApplier(new Random(42), '\u0014', '\u00fe');
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NoQuoteDelimiterHere";

        string result = applier.Apply(1, line, "DOC001", "concordance-quote-escape", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void Apply_UnknownType_ReturnsLineUnchanged()
    {
        var applier = new ConcordanceAnomalyApplier(new Random(42), '\u0014', '\u00fe');
        var anomalies = new List<ChaosAnomaly>();
        const string line = "\u00feVal\u00fe\u0014\u00feOther\u00fe";

        string result = applier.Apply(1, line, "DOC001", "no-such-type", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void GetEncodingAnomaly_AlwaysReturnsNull()
    {
        var applier = new ConcordanceAnomalyApplier(new Random(42), '\u0014', '\u00fe');
        var anomalies = new List<ChaosAnomaly>();

        var result = applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.UTF8, anomalies);

        Assert.Null(result);
        Assert.Empty(anomalies);
    }
}

public class XmlChaosAnomalyApplierTests
{
    private const string DocumentXml = "<Document DocID=\"DOC00000001\"><Files/><Tags><Tag TagName=\"Author\" TagValue=\"Jane Doe\"/></Tags></Document>";

    [Fact]
    public void Apply_LinePath_AlwaysReturnsLineUnchanged()
    {
        var applier = new XmlChaosAnomalyApplier();
        var anomalies = new List<ChaosAnomaly>();
        const string line = "<Document />";

        string result = applier.Apply(1, line, "DOC1", "xml-unclosed-tag", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void GetEncodingAnomaly_AlwaysReturnsNull()
    {
        var applier = new XmlChaosAnomalyApplier();
        var anomalies = new List<ChaosAnomaly>();

        Assert.Null(applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.UTF8, anomalies));
        Assert.Empty(anomalies);
    }

    [Fact]
    public void TryApplyToDocument_UnclosedTag_RemovesClosingDocumentTag()
    {
        var applier = new XmlChaosAnomalyApplier();

        bool changed = applier.TryApplyToDocument(DocumentXml, "xml-unclosed-tag", out string result, out string column, out string description);

        Assert.True(changed);
        Assert.DoesNotContain("</Document>", result, StringComparison.Ordinal);
        Assert.Equal("Document", column);
        Assert.Contains("unclosed", description, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApplyToDocument_BadEntity_InsertsUndefinedEntityReference()
    {
        var applier = new XmlChaosAnomalyApplier();

        bool changed = applier.TryApplyToDocument(DocumentXml, "xml-bad-entity", out string result, out string column, out string description);

        Assert.True(changed);
        Assert.Contains("&chaos;", result, StringComparison.Ordinal);
        Assert.Equal("Document", column);
        Assert.Contains("entity", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryApplyToDocument_EncodingLie_IsNotADocumentAnomaly()
    {
        var applier = new XmlChaosAnomalyApplier();

        bool changed = applier.TryApplyToDocument(DocumentXml, "xml-encoding-lie", out string result, out _, out _);

        Assert.False(changed);
        Assert.Equal(DocumentXml, result);
    }

    [Fact]
    public void TryApplyEncodingLie_WithUtf8Declaration_ClaimsUtf16()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?><Root/>";

        bool changed = XmlChaosAnomalyApplier.TryApplyEncodingLie(xml, out string result);

        Assert.True(changed);
        Assert.Contains("encoding=\"utf-16\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApplyEncodingLie_WithoutDeclaration_ReturnsUnchanged()
    {
        const string xml = "<Root/>";

        bool changed = XmlChaosAnomalyApplier.TryApplyEncodingLie(xml, out string result);

        Assert.False(changed);
        Assert.Equal(xml, result);
    }
}
