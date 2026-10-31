using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

public sealed class CrossFormatAnomalyApplierTests : IDisposable
{
    private const char ColDelim = '\x14';
    private const char QuoteDelim = '\xfe';
    private readonly string testOutputPath;

    public CrossFormatAnomalyApplierTests()
    {
        this.testOutputPath = Path.Combine(Path.GetTempPath(), $"zipper_cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.testOutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.testOutputPath))
        {
            try
            {
                Directory.Delete(this.testOutputPath, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    private static string JoinDat(params string[] fields)
    {
        var quote = QuoteDelim.ToString();
        var col = ColDelim.ToString();
        return string.Join(col, fields.Select(f => $"{quote}{f}{quote}"));
    }

    private static (List<string> datLines, List<string> optLines) CreateSampleLines()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "PARENTDOCID", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "TEST00000001", "", "IMAGES\\VOL001\\DOC00000001.tif", "1", "VOL001"),
            JoinDat("DOC00000002", "TEST00000002", "DOC00000001", "IMAGES\\VOL001\\DOC00000002_001.tif", "2", "VOL001"),
            JoinDat("DOC00000003", "TEST00000003", "", "IMAGES\\VOL001\\DOC00000003.tif", "1", "VOL001"),
        };

        var optLines = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002_001.tif,Y,,,2",
            "TEST00000002_002,VOL001,IMAGES\\VOL001\\DOC00000002_002.tif,,,,",
            "TEST00000003,VOL001,IMAGES\\VOL001\\DOC00000003.tif,Y,,,1",
        };

        return (datLines, optLines);
    }

    private static (List<string> DatLines, List<string> OptLines) CreateNoFamilyLines(string idColumn = "DOCID")
    {
        var datLines = new List<string>
        {
            JoinDat(idColumn, "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif", "1", "VOL001"),
            JoinDat("DOC00000002", "TEST00000002", "IMAGES\\VOL001\\DOC00000002.tif", "1", "VOL001"),
            JoinDat("DOC00000003", "TEST00000003", "IMAGES\\VOL001\\DOC00000003.tif", "1", "VOL001"),
        };
        var optLines = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
            "TEST00000003,VOL001,IMAGES\\VOL001\\DOC00000003.tif,Y,,,1",
        };

        return (datLines, optLines);
    }

    private sealed class FirstIndexRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    [Fact]
    public void Apply_NullArguments_ThrowsArgumentNullException()
    {
        var (datLines, optLines) = CreateSampleLines();
        Assert.Throws<ArgumentNullException>(() => CrossFormatAnomalyApplier.Apply(null!, optLines, "cross-format-desync", "1"));
        Assert.Throws<ArgumentNullException>(() => CrossFormatAnomalyApplier.Apply(datLines, null!, "cross-format-desync", "1"));
        Assert.Throws<ArgumentNullException>(() => CrossFormatAnomalyApplier.Apply(datLines, optLines, "cross-format-desync", "1", (Random)null!));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(3, 0)]
    public void Apply_EmptyOrHeaderOnlyBuffers_ReturnsUntouchedLinesAndEmptyAnomalies(int datCount, int optCount)
    {
        var datLines = Enumerable.Range(0, datCount).Select(i => $"Line{i}").ToList();
        var optLines = Enumerable.Range(0, optCount).Select(i => $"Opt{i},V,P,Y,,,").ToList();
        var result = CrossFormatAnomalyApplier.Apply(datLines, optLines, scenarioOrTypes: "cross-format-desync", chaosAmount: "1", seed: 42);
        Assert.Equal(datLines, result.DatLines);
        Assert.Equal(optLines, result.OptLines);
        Assert.Empty(result.Anomalies);
    }

    [Fact]
    public void Apply_UnalignedOptKeys_FallsBackToUnprocessedDatRecord()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC001", "BATES001", @"IMAGES\VOL001\DOC001.tif", "1", "VOL001"),
        };
        var optLines = new List<string> { @"MISMATCHED_KEY,VOL001,IMAGES\VOL001\DOC001.tif,Y,,,1" };
        var result = CrossFormatAnomalyApplier.Apply(datLines, optLines, scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount, chaosAmount: "1", seed: 42);
        Assert.NotEmpty(result.Anomalies);
        Assert.Equal("DOC001", result.DatAnomalies[0].RecordID);
        Assert.Equal("MISMATCHED_KEY", result.OptAnomalies[0].RecordID);
    }

    [Fact]
    public void Apply_ZeroAmount_MakesNoChanges()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            chaosAmount: "0",
            seed: 42);

        Assert.Equal(datLines, result.DatLines);
        Assert.Equal(optLines, result.OptLines);
        Assert.Empty(result.Anomalies);
    }

    [Fact]
    public void Apply_DesyncPagecount_CorruptsAndProducesReciprocalPeerAudit()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        Assert.Equal(ChaosAnomalyTypes.CrossFormat.DesyncPagecount, datAnomaly.ErrorType);
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.DesyncPagecount, optAnomaly.ErrorType);

        Assert.NotNull(datAnomaly.Peer);
        Assert.Equal("loadfile.opt", datAnomaly.Peer.File);
        Assert.Equal(optAnomaly.LineNumber, datAnomaly.Peer.LineNumber);
        Assert.Equal(optAnomaly.RecordID, datAnomaly.Peer.RecordID);

        Assert.NotNull(optAnomaly.Peer);
        Assert.Equal("loadfile.dat", optAnomaly.Peer.File);
        Assert.Equal(datAnomaly.LineNumber, optAnomaly.Peer.LineNumber);
        Assert.Equal(datAnomaly.RecordID, optAnomaly.Peer.RecordID);
    }

    [Fact]
    public void Apply_DesyncPagecount_OnePageDocumentEmitsSevenColumnOptRow()
    {
        var (datLines, optLines) = CreateNoFamilyLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            chaosAmount: "1",
            seed: 42);

        Assert.Equal(optLines.Count + 1, result.OptLines.Count);
        Assert.All(result.OptLines, line => Assert.Equal(7, line.Split(',').Length));
    }

    [Fact]
    public void Apply_DesyncPagecount_BatesEnding001_UsesDistinctContinuationIdentity()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "CASE_001", "IMAGES\\VOL001\\CASE_001.tif", "1", "VOL001"),
            JoinDat("DOC00000002", "CASE_002", "IMAGES\\VOL001\\CASE_002.tif", "1", "VOL001"),
        };
        var optLines = new List<string>
        {
            "CASE_001,VOL001,IMAGES\\VOL001\\CASE_001.tif,Y,,,1",
            "CASE_002,VOL001,IMAGES\\VOL001\\CASE_002.tif,Y,,,1",
        };

        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            chaosAmount: "1",
            random: new FirstIndexRandom());

        var recordIds = result.OptLines.Select(line => line.Split(',')[0]).ToArray();

        Assert.Contains("CASE_001_002", recordIds);
        Assert.Equal(recordIds.Length, recordIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(
            result.OptLines,
            line => line.StartsWith("CASE_001_002,VOL001,IMAGES\\VOL001\\CASE_001_002.tif,", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_DesyncPagecount_MultipageOptKeyEnding001_PairsWithBaseDatRecordAndProducesAccurateAudit()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001_001.tif", "2", "VOL001"),
            JoinDat("DOC00000002", "TEST00000002", "IMAGES\\VOL001\\DOC00000002.tif", "1", "VOL001"),
        };
        var optLines = new List<string>
        {
            "TEST00000001_001,VOL001,IMAGES\\VOL001\\DOC00000001_001.tif,Y,,,2",
            "TEST00000001_002,VOL001,IMAGES\\VOL001\\DOC00000001_002.tif,,,,",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
        };

        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            chaosAmount: "1",
            random: new FirstIndexRandom());

        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        // The multipage OPT document break TEST00000001_001 must pair with DOC00000001, not the random second doc
        Assert.Equal("DOC00000001", datAnomaly.RecordID);
        Assert.Equal("TEST00000001_001", optAnomaly.RecordID);
        Assert.NotNull(datAnomaly.Peer);
        Assert.Equal("TEST00000001_001", datAnomaly.Peer.RecordID);
        Assert.NotNull(optAnomaly.Peer);
        Assert.Equal("DOC00000001", optAnomaly.Peer.RecordID);
    }

    [Fact]
    public void Apply_DesyncPath_MultipageOptKeyEnding001_PairsWithMatchingDatRecord()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001_001.tif", "2", "VOL001"),
            JoinDat("DOC00000002", "TEST00000002", "IMAGES\\VOL001\\DOC00000002.tif", "1", "VOL001"),
        };
        var optLines = new List<string>
        {
            "TEST00000001_001,VOL001,IMAGES\\VOL001\\DOC00000001_001.tif,Y,,,2",
            "TEST00000001_002,VOL001,IMAGES\\VOL001\\DOC00000001_002.tif,,,,",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
        };

        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPath,
            chaosAmount: "1",
            random: new FirstIndexRandom());

        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        Assert.Equal("DOC00000001", datAnomaly.RecordID);
        Assert.Equal("TEST00000001_001", optAnomaly.RecordID);
        Assert.NotNull(datAnomaly.Peer);
        Assert.Equal("TEST00000001_001", datAnomaly.Peer.RecordID);
        Assert.NotNull(optAnomaly.Peer);
        Assert.Equal("DOC00000001", optAnomaly.Peer.RecordID);
    }

    [Fact]
    public void Apply_DesyncOrder_SwapsOptDocumentOrderAndProducesReciprocalAudit()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncOrder,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        Assert.Equal(ChaosAnomalyTypes.CrossFormat.DesyncOrder, datAnomaly.ErrorType);
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.DesyncOrder, optAnomaly.ErrorType);

        Assert.NotNull(datAnomaly.Peer);
        Assert.Equal("loadfile.opt", datAnomaly.Peer.File);
        Assert.NotNull(optAnomaly.Peer);
        Assert.Equal("loadfile.dat", optAnomaly.Peer.File);

        // Verify OPT order actually changed while DAT preserves its original bytes
        Assert.NotEqual(optLines, result.OptLines);
        Assert.Equal(datLines, result.DatLines);
    }

    [Fact]
    public void Apply_DesyncOrder_AuditsMovedOptDocumentLineNumber()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncOrder,
            chaosAmount: "1",
            seed: 42);

        var optAnomaly = Assert.Single(result.OptAnomalies);
        int actualLineNumber = Array.FindIndex(
            result.OptLines.ToArray(),
            line => line.StartsWith($"{optAnomaly.RecordID},", StringComparison.Ordinal)) + 1;

        Assert.True(actualLineNumber > 0, $"OPT record '{optAnomaly.RecordID}' must be present in the emitted lines.");
        Assert.Equal(actualLineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), optAnomaly.LineNumber);
    }

    [Fact]
    public void Apply_CombinedDesyncScenario_AuditsFinalPeerLineNumbers()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: "cross-format-desync",
            chaosAmount: "3",
            seed: 42);

        static int FindLine(IReadOnlyList<string> lines, string file, string recordId)
        {
            string marker = file == "loadfile.dat"
                ? $"{QuoteDelim}{recordId}{QuoteDelim}"
                : $"{recordId},";
            return Array.FindIndex(
                lines.ToArray(),
                line => line.StartsWith(marker, StringComparison.Ordinal)) + 1;
        }

        foreach (var anomaly in result.Anomalies)
        {
            Assert.NotNull(anomaly.Peer);
            var anomalyLines = anomaly.File == "loadfile.dat" ? result.DatLines : result.OptLines;
            int anomalyLine = FindLine(anomalyLines, anomaly.File!, anomaly.RecordID);
            Assert.True(anomalyLine > 0, $"Record '{anomaly.RecordID}' must be present in {anomaly.File}.");
            Assert.Equal(anomalyLine.ToString(System.Globalization.CultureInfo.InvariantCulture), anomaly.LineNumber);

            var peerLines = anomaly.Peer.File == "loadfile.dat" ? result.DatLines : result.OptLines;
            int peerLine = FindLine(peerLines, anomaly.Peer.File, anomaly.Peer.RecordID);
            Assert.True(peerLine > 0, $"Peer record '{anomaly.Peer.RecordID}' must be present in {anomaly.Peer.File}.");
            Assert.Equal(peerLine.ToString(System.Globalization.CultureInfo.InvariantCulture), anomaly.Peer.LineNumber);
        }
    }

    [Fact]
    public void Apply_CombinedFamilyScenario_AuditsRemovedDatRowsAtFinalGapLine()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: "broken-families",
            chaosAmount: "2",
            seed: 42);

        Assert.Equal(2, result.DatLines.Count);
        Assert.Equal("2", result.DatAnomalies[0].LineNumber);
        Assert.Equal("3", result.DatAnomalies[1].LineNumber);
    }

    [Fact]
    public void Apply_DesyncPath_MutatesOptVolumeAndProducesReciprocalAudit()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPath,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        Assert.Equal(ChaosAnomalyTypes.CrossFormat.DesyncPath, datAnomaly.ErrorType);
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.DesyncPath, optAnomaly.ErrorType);

        Assert.NotNull(datAnomaly.Peer);
        Assert.Equal("loadfile.opt", datAnomaly.Peer.File);
        Assert.NotNull(optAnomaly.Peer);
        Assert.Equal("loadfile.dat", optAnomaly.Peer.File);

        Assert.Contains(result.OptLines, l => l.Contains("VOL999", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_OrphanAttachment_RemovesParentFromDatAndProducesReciprocalAudit()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.OrphanAttachment,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        Assert.Equal(ChaosAnomalyTypes.CrossFormat.OrphanAttachment, datAnomaly.ErrorType);
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.OrphanAttachment, optAnomaly.ErrorType);

        // Parent DOC00000001 should be dropped from DAT, leaving DOC00000002 orphaned
        Assert.DoesNotContain(result.DatLines, l => l.StartsWith($"\u00feDOC00000001\u00fe", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_MissingChild_RemovesAttachmentFromDatAndProducesReciprocalAudit()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.MissingChild,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        Assert.Equal(ChaosAnomalyTypes.CrossFormat.MissingChild, datAnomaly.ErrorType);
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.MissingChild, optAnomaly.ErrorType);

        // Child DOC00000002 should be dropped from DAT while remaining in OPT
        Assert.DoesNotContain(result.DatLines, l => l.StartsWith($"\u00feDOC00000002\u00fe", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_MissingChild_NoExistingFamilyCreatesMissingDatChildAndRetainsItsOptDocument()
    {
        var (datLines, optLines) = CreateNoFamilyLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.MissingChild,
            chaosAmount: "1",
            seed: 42);

        var header = ProductionSetPostValidator.ParseDatLine(result.DatLines[0], ColDelim, QuoteDelim);
        int docIdIndex = header.FindIndex(h => h == "DOCID");
        int endAttachIndex = header.FindIndex(h => h == "ENDATTACH");
        Assert.Equal(0, header.Count(h => h == "PARENTDOCID"));
        Assert.Equal(1, header.Count(h => h == "ENDATTACH"));
        Assert.Equal(datLines.Count - 1, result.DatLines.Count);
        Assert.Equal(optLines.Count, result.OptLines.Count);

        var records = result.DatLines.Skip(1)
            .Select(line => ProductionSetPostValidator.ParseDatLine(line, ColDelim, QuoteDelim))
            .ToList();
        var datAnomaly = Assert.Single(result.DatAnomalies);
        var optAnomaly = Assert.Single(result.OptAnomalies);
        Assert.DoesNotContain(records, fields => fields[docIdIndex] == datAnomaly.RecordID);
        Assert.Contains(records, fields => fields[endAttachIndex] == optAnomaly.RecordID);
        Assert.Equal(optAnomaly.RecordID, datAnomaly.Peer!.RecordID);
        Assert.Equal(datAnomaly.RecordID, optAnomaly.Peer!.RecordID);
        Assert.Contains(result.OptLines, line => line.StartsWith($"{optAnomaly.RecordID},", StringComparison.Ordinal));
        Assert.Equal(optAnomaly.LineNumber, datAnomaly.Peer.LineNumber);
        Assert.Equal(datAnomaly.LineNumber, optAnomaly.Peer.LineNumber);
    }

    [Fact]
    public void Apply_BrokenParent_FullAmountTargetsDistinctDatRecords()
    {
        var (datLines, optLines) = CreateNoFamilyLines();

        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.BrokenParent,
            chaosAmount: "100%",
            seed: 42);

        var header = ProductionSetPostValidator.ParseDatLine(datLines[0], ColDelim, QuoteDelim);
        int docIdIndex = header.FindIndex(column => column == "DOCID");
        var inputRecordIds = datLines.Skip(1)
            .Select(line => ProductionSetPostValidator.ParseDatLine(line, ColDelim, QuoteDelim)[docIdIndex])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetedRecordIds = result.DatAnomalies
            .Select(anomaly => anomaly.RecordID)
            .Where(inputRecordIds.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, result.DatAnomalies.Count);
        Assert.Equal(3, targetedRecordIds.Count);
    }

    [Fact]
    public void Apply_BrokenFamilies_WithoutParentColumnAddsColumnOnlyOnce()
    {
        var (datLines, optLines) = CreateNoFamilyLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: "broken-families",
            chaosAmount: "3",
            seed: 42);

        var header = ProductionSetPostValidator.ParseDatLine(result.DatLines[0], ColDelim, QuoteDelim);

        Assert.Equal(1, header.Count(h => h == "PARENTDOCID"));
    }

    [Fact]
    public void Apply_OrphanAttachment_ControlNumberColumn_AuditsTheEmittedRecord()
    {
        var (datLines, optLines) = CreateNoFamilyLines("Control Number");
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.OrphanAttachment,
            chaosAmount: "1",
            seed: 42);

        var header = ProductionSetPostValidator.ParseDatLine(result.DatLines[0], ColDelim, QuoteDelim);
        int recordIdIndex = header.FindIndex(h => h == "Control Number");
        int parentIdIndex = header.FindIndex(h => h == "PARENTDOCID");
        var anomaly = Assert.Single(result.DatAnomalies);
        int lineNumber = Array.FindIndex(
            result.DatLines.ToArray(),
            line => line.StartsWith($"{QuoteDelim}{anomaly.RecordID}{QuoteDelim}", StringComparison.Ordinal)) + 1;

        Assert.True(lineNumber > 0);
        Assert.Equal(lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), anomaly.LineNumber);
        var fields = ProductionSetPostValidator.ParseDatLine(result.DatLines[lineNumber - 1], ColDelim, QuoteDelim);
        Assert.Equal("DOC_PARENT_MISSING", fields[parentIdIndex]);
        Assert.False(string.IsNullOrEmpty(fields[recordIdIndex]));
    }

    [Fact]
    public void Apply_BrokenParent_CorruptsParentDocIdAndProducesReciprocalAudit()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.BrokenParent,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        var optAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.opt");

        Assert.Equal(ChaosAnomalyTypes.CrossFormat.BrokenParent, datAnomaly.ErrorType);
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.BrokenParent, optAnomaly.ErrorType);

        Assert.Contains(result.DatLines, l => l.Contains("INVALID_PARENT", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_Determinism_SameSeedProducesIdenticalResults()
    {
        var (dat1, opt1) = CreateSampleLines();
        var (dat2, opt2) = CreateSampleLines();

        var res1 = CrossFormatAnomalyApplier.Apply(dat1, opt1, scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount, chaosAmount: "1", seed: 12345);
        var res2 = CrossFormatAnomalyApplier.Apply(dat2, opt2, scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount, chaosAmount: "1", seed: 12345);

        Assert.Equal(res1.DatLines, res2.DatLines);
        Assert.Equal(res1.OptLines, res2.OptLines);
        Assert.Equal(res1.Anomalies.Count, res2.Anomalies.Count);
        for (int i = 0; i < res1.Anomalies.Count; i++)
        {
            Assert.Equal(res1.Anomalies[i].ErrorType, res2.Anomalies[i].ErrorType);
            Assert.Equal(res1.Anomalies[i].LineNumber, res2.Anomalies[i].LineNumber);
            Assert.Equal(res1.Anomalies[i].RecordID, res2.Anomalies[i].RecordID);
            Assert.Equal(res1.Anomalies[i].File, res2.Anomalies[i].File);
            Assert.Equal(res1.Anomalies[i].Peer?.LineNumber, res2.Anomalies[i].Peer?.LineNumber);
        }
    }

    [Theory]
    [InlineData("cross-format-desync")]
    [InlineData("broken-families")]
    public void Apply_NamedScenarios_ProduceExpectedAnomalyCategories(string scenario)
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: scenario,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
        var allTypes = result.Anomalies.Select(a => a.ErrorType).Distinct().ToList();

        if (scenario == "cross-format-desync")
        {
            Assert.All(allTypes, t => Assert.Contains(t, ChaosAnomalyTypes.CrossFormat.DesyncTypes));
        }
        else
        {
            Assert.All(allTypes, t => Assert.Contains(t, ChaosAnomalyTypes.CrossFormat.FamilyTypes));
        }
    }

    [Fact]
    public void ChaosEngine_ApplyCrossFormat_DelegatesCorrectly()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = ChaosEngine.ApplyCrossFormat(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            chaosAmount: "1",
            seed: 42);

        Assert.NotEmpty(result.Anomalies);
    }

    [Fact]
    public void Validator_Dogfooding_CatchesDesyncPagecount()
    {
        var prodDir = Path.Combine(this.testOutputPath, "dogfood_pagecount");
        var (datLines, optLines) = CreateSampleLines();
        var res = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPagecount,
            chaosAmount: "1",
            seed: 42);

        SetupProductionSet(prodDir, res.DatLines, res.OptLines);

        var request = CreateRequest();
        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
    }

    [Fact]
    public void Validator_Dogfooding_CatchesDesyncOrder()
    {
        var prodDir = Path.Combine(this.testOutputPath, "dogfood_order");
        var (datLines, optLines) = CreateSampleLines();
        var res = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncOrder,
            chaosAmount: "1",
            seed: 42);

        SetupProductionSet(prodDir, res.DatLines, res.OptLines);

        var request = CreateRequest();
        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "BatesConsistency");
    }

    [Fact]
    public void Validator_Dogfooding_CatchesDesyncPath()
    {
        var prodDir = Path.Combine(this.testOutputPath, "dogfood_path");
        var (datLines, optLines) = CreateSampleLines();
        var res = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.DesyncPath,
            chaosAmount: "1",
            seed: 42);

        SetupProductionSet(prodDir, res.DatLines, res.OptLines);

        var request = CreateRequest();
        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "PathReconciliation");
    }

    [Fact]
    public void Apply_NamedScenario_WithoutAmount_UsesScenarioDefaultAmount()
    {
        var datLines = new List<string> { JoinDat("DOCID", "BATES_NUMBER", "PARENTDOCID", "IMAGE_PATH", "PAGECOUNT", "VOLUME") };
        var optLines = new List<string>();
        for (int i = 1; i <= 20; i++)
        {
            string key = $"DOC{i:D8}";
            datLines.Add(JoinDat(key, $"TEST{i:D8}", string.Empty, $"IMAGES\\VOL001\\{key}.tif", "1", "VOL001"));
            optLines.Add($"TEST{i:D8},VOL001,IMAGES\\VOL001\\{key}.tif,Y,,,1");
        }

        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: "cross-format-desync",
            chaosAmount: null,
            seed: 42);

        // Scenario default is 5% of 20 DAT documents; each coordinated anomaly records one DAT and one OPT entry.
        Assert.Equal(2, result.Anomalies.Count);
    }

    [Fact]
    public void Validator_Dogfooding_CatchesOrphanAttachment()
    {
        var prodDir = Path.Combine(this.testOutputPath, "dogfood_orphan");
        var (datLines, optLines) = CreateSampleLines();
        var res = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.OrphanAttachment,
            chaosAmount: "1",
            seed: 42);

        SetupProductionSet(prodDir, res.DatLines, res.OptLines);

        var report = ProductionSetPostValidator.Validate(prodDir, CreateRequest());

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "BatesConsistency");
    }

    [Fact]
    public void Validator_Dogfooding_CatchesMissingChild()
    {
        var prodDir = Path.Combine(this.testOutputPath, "dogfood_missing_child");
        var (datLines, optLines) = CreateSampleLines();
        var res = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.MissingChild,
            chaosAmount: "1",
            seed: 42);

        SetupProductionSet(prodDir, res.DatLines, res.OptLines);

        var report = ProductionSetPostValidator.Validate(prodDir, CreateRequest());

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "OptBoundary");
    }

    [Fact]
    public void Validator_Dogfooding_CatchesBrokenParent()
    {
        var prodDir = Path.Combine(this.testOutputPath, "dogfood_broken_parent");
        var (datLines, optLines) = CreateSampleLines();
        var res = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.BrokenParent,
            chaosAmount: "1",
            seed: 42);

        SetupProductionSet(prodDir, res.DatLines, res.OptLines);

        var report = ProductionSetPostValidator.Validate(prodDir, CreateRequest());

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "BatesConsistency");
    }

    [Fact]
    public void Apply_MissingChild_OptChildNull_DoesNotSkipNextCandidate()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "PARENTDOCID", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "TEST00000001", "", "IMAGES\\VOL001\\DOC00000001.tif", "1", "VOL001"),
            JoinDat("DOC00000002", "TEST00000002", "DOC00000001", "IMAGES\\VOL001\\DOC00000002.tif", "1", "VOL001"),
            JoinDat("DOC00000003", "TEST00000003", "DOC00000001", "IMAGES\\VOL001\\DOC00000003.tif", "1", "VOL001"),
        };
        // OPT only has parent DOC1 and child DOC3 (DOC2 has no matching OPT row)
        var optLines = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000003,VOL001,IMAGES\\VOL001\\DOC00000003.tif,Y,,,1",
        };

        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.MissingChild,
            chaosAmount: "1",
            random: new FirstIndexRandom());

        // Because DOC2 has no OPT row, it shouldn't cause DOC3 to be skipped; DOC3 should be selected as the missing child
        var datAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat");
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.MissingChild, datAnomaly.ErrorType);
        Assert.Equal("DOC00000003", datAnomaly.RecordID);
    }

    [Fact]
    public void Apply_OrphanAttachment_DoesNotDropParentAlreadyTargeted()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "PARENTDOCID", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "TEST00000001", "", "IMAGES\\VOL001\\DOC00000001.tif", "1", "VOL001"),
            JoinDat("DOC00000002", "TEST00000002", "DOC00000001", "IMAGES\\VOL001\\DOC00000002.tif", "1", "VOL001"),
            JoinDat("DOC00000003", "TEST00000003", "", "IMAGES\\VOL001\\DOC00000003.tif", "1", "VOL001"),
            JoinDat("DOC00000004", "TEST00000004", "DOC00000003", "IMAGES\\VOL001\\DOC00000004.tif", "1", "VOL001"),
        };
        var optLines = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
            "TEST00000003,VOL001,IMAGES\\VOL001\\DOC00000003.tif,Y,,,1",
            "TEST00000004,VOL001,IMAGES\\VOL001\\DOC00000004.tif,Y,,,1",
        };

        // First apply desync-path targeting DOC00000001, then orphan-attachment
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: $"{ChaosAnomalyTypes.CrossFormat.DesyncPath},{ChaosAnomalyTypes.CrossFormat.OrphanAttachment}",
            chaosAmount: "2",
            random: new FirstIndexRandom());

        Assert.Equal(4, result.Anomalies.Count); // 2 DAT, 2 OPT
        var pathAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat" && a.ErrorType == ChaosAnomalyTypes.CrossFormat.DesyncPath);
        Assert.Equal("DOC00000001", pathAnomaly.RecordID);

        // DOC00000001 was targeted by desync-path, so orphan-attachment must NOT drop DOC00000001; it must target DOC00000004 with parent DOC00000003
        var orphanAnomaly = Assert.Single(result.Anomalies, a => a.File == "loadfile.dat" && a.ErrorType == ChaosAnomalyTypes.CrossFormat.OrphanAttachment);
        Assert.Equal("DOC00000004", orphanAnomaly.RecordID);
    }

    [Fact]
    public void Apply_ScenarioOrTypes_TrimsAndNormalizesWhitespaceAndCasing()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: "  DESYNC-PATH  ,  DESYNC-ORDER  ",
            chaosAmount: "1",
            random: new FirstIndexRandom());

        Assert.NotEmpty(result.Anomalies);
        Assert.Equal(ChaosAnomalyTypes.CrossFormat.DesyncPath, result.Anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_ShortDatRows_SafelyPadsFieldsWithoutThrowing()
    {
        var datLines = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "PARENTDOCID", "IMAGE_PATH", "PAGECOUNT", "VOLUME"),
            JoinDat("DOC00000001", "TEST00000001"), // Only 2 fields, while header has 6 fields
        };
        var optLines = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        // Applying broken-parent requires writing to PARENTDOCID (column index 2)
        var exception = Record.Exception(() => CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: ChaosAnomalyTypes.CrossFormat.BrokenParent,
            chaosAmount: "1",
            random: new FirstIndexRandom()));

        Assert.Null(exception);
    }

    [Fact]
    public void Apply_NonCrossFormatScenario_InjectsNoAnomaliesAndPreservesLines()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: "mixed-delimiters",
            chaosAmount: "1",
            random: new FirstIndexRandom());

        Assert.Empty(result.Anomalies);
        Assert.Equal(datLines, result.DatLines);
        Assert.Equal(optLines, result.OptLines);
    }

    [Fact]
    public void Apply_UnknownOrNonCrossFormatTypes_InjectsNoAnomalies()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: "unknown-type,another-non-cross-type",
            chaosAmount: "1",
            random: new FirstIndexRandom());

        Assert.Empty(result.Anomalies);
        Assert.Equal(datLines, result.DatLines);
        Assert.Equal(optLines, result.OptLines);
    }

    [Fact]
    public void Apply_NullOrEmptyScenarioOrTypes_StillDefaultsToAllCrossFormatTypes()
    {
        var (datLines, optLines) = CreateSampleLines();
        var result = CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes: null,
            chaosAmount: "1",
            random: new FirstIndexRandom());

        Assert.NotEmpty(result.Anomalies);
    }

    private static FileGenerationRequest CreateRequest()
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = Path.GetTempPath(),
                FileCount = 3,
                FileType = "pdf",
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 10,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Bates = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 8,
            },
        };
    }

    private static void SetupProductionSet(string prodDir, IReadOnlyList<string> datRows, IReadOnlyList<string> optRows)
    {
        var dataDir = Path.Combine(prodDir, "DATA");
        var volDir = Path.Combine(prodDir, "IMAGES", "VOL001");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(volDir);

        File.WriteAllLines(Path.Combine(dataDir, "loadfile.dat"), datRows, Encoding.UTF8);
        File.WriteAllLines(Path.Combine(dataDir, "loadfile.opt"), optRows, Encoding.UTF8);

        // Create dummy TIFF files matching all paths in optRows
        foreach (var line in optRows)
        {
            var parts = line.Split(',');
            if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                var relPath = parts[2].Trim().Replace('\\', '/');
                var fullPath = Path.Combine(prodDir, relPath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (!File.Exists(fullPath))
                {
                    File.WriteAllBytes(fullPath, [0x49, 0x49, 0x2A, 0x00]);
                }
            }
        }
    }
}
