using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using FsCheck.Xunit;
using Xunit;
using Zipper.Config;
using Zipper.ManifestComparison;
using Zipper.Validation;

namespace Zipper.Tests.Properties;

/// <summary>
/// Property-based tests for the Post-Generation, Production Set, and Supplemental
/// Validators, plus rolling Bates transitions across Rolling Production Sets
/// (<see cref="ProductionSetPlanner"/> ↔ <see cref="ProductionSetPostValidator"/> agreement)
/// and <see cref="VolumeAnalyzer"/> volume rollup laws.
/// </summary>
[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class ValidationPropertyTests : IDisposable
{
    private const char ColDelim = '\x14';
    private const char QuoteDelim = '\xfe';
    private const string PriorPrefix = "SUPP";
    private const int PriorDigits = 8;
    private const int BatesDigits = 8;

    /// <summary>
    /// Shared temp root: a 100-case property allocates one sub-directory per iteration, so
    /// per-iteration create/delete churn stays out of the measured property body and cleanup
    /// happens once in <see cref="Dispose"/> (docs/property-based-testing.md §3D).
    /// </summary>
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"zipper-validation-pbt-{Guid.NewGuid():N}");

    public ValidationPropertyTests()
    {
        Directory.CreateDirectory(this.tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.tempRoot))
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the shared temp root.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the shared temp root.
        }
    }

    // ------------------------------------------------------------------
    // 1. PostGenerationValidator — soundness & completeness
    // ------------------------------------------------------------------

    /// <summary>
    /// Soundness and completeness of standard-mode post-generation validation: an uncorrupted
    /// Archive + Load File pair produces zero findings, and every single structural fault
    /// (an Archive entry deleted behind the Load File's back, a Load File path that resolves to
    /// no Archive entry, a column-count mismatch, a duplicate DOCID) deterministically produces
    /// an error finding in the category that owns it.
    /// </summary>
    [Property]
    public void PostGenerationValidator_Validate_CleanOutputShouldPassAndInjectedFaultShouldFail(ValidationMutationPropertyInput input)
    {
        int fileCount = Math.Clamp(input.FileCount, 2, 6);
        int mutationType = input.MutationType % ZipperArbitraries.PostGenerationFaultCount;

        var caseDir = CreateCaseDirectory("postgen");
        var archivePath = Path.Combine(caseDir, "loadfile.zip");
        var datPath = Path.Combine(caseDir, "loadfile.dat");
        CreateStandardArchiveAndDat(archivePath, datPath, fileCount);

        var request = CreateStandardModeRequest(caseDir, fileCount);

        // ArchiveEntryPaths is null so the validator extracts entry paths from the Archive
        // itself; the faulted re-validation then re-reads the mutated Archive/DAT.
        var context = new ValidationContext
        {
            ArchiveFilePath = archivePath,
            LoadFiles = new Dictionary<string, string> { ["dat"] = datPath },
            Request = request,
        };

        var validator = new PostGenerationValidator();

        // Soundness: uncorrupted output passes with zero findings.
        var clean = validator.Validate(context);
        Assert.Empty(clean.Findings);

        // Completeness: inject exactly one structural fault.
        switch (mutationType)
        {
            case 0:
                // Delete the Archive entry referenced by the last DAT row.
                DeleteArchiveEntry(archivePath, $"folder_{fileCount - 1:D3}/file_{fileCount - 1:D8}.pdf");
                break;
            case 1:
                // Append a DAT row whose path resolves to no Archive entry.
                AppendDatRow(datPath, $"DOC{fileCount:D8}", "MISSING/NONEXISTENT/FILE.PDF");
                break;
            case 2:
                // Append a DAT row with one field too many.
                AppendDatRow(datPath, $"DOC{fileCount:D8}", "folder_000/file_00000000.pdf", "EXTRA");
                break;
            case 3:
                // Duplicate the first row's DOCID in the second row.
                DuplicateFirstDocId(datPath);
                break;
        }

        var faulted = validator.Validate(context);
        var expectedCategory = mutationType switch
        {
            0 or 1 => "PathReconciliation",
            2 => "ColumnCount",
            _ => "UniqueId",
        };

        Assert.Contains(faulted.Findings, f => f.Category == expectedCategory);
        Assert.True(faulted.HasErrors, $"Mutation category {mutationType} must produce an error finding.");
    }

    // ------------------------------------------------------------------
    // 2. ProductionSetPostValidator — fault-category detection
    // ------------------------------------------------------------------

    /// <summary>
    /// Fault injection across every category the Production Set validator owns: missing
    /// native/text/image/redacted files, an invalid <c>NATIVE_WITHHELD</c> value, duplicate
    /// Bates Numbers, a Bates Number gap, a Load File row dropped behind the Production
    /// Manifest's back, a corrupt manifest, an OPT boundary violation, duplicate DOCIDs, and
    /// Azure metadata constraint violations. The clean fixture passes with exact
    /// checked-count anchors, every single fault fails the report with the owning finding
    /// code, and reference counts are conserved when a referenced file goes missing (REQ-149).
    /// </summary>
    [Property]
    public void ProductionSetPostValidator_Validate_ShouldDetectEverySingleFaultCategory(ValidationMutationPropertyInput input)
    {
        int fileCount = Math.Clamp(input.FileCount, 2, 6);
        int mutationType = input.MutationType % ZipperArbitraries.ProductionSetFaultCount;

        var prodPath = CreateCaseDirectory("prodset");
        var request = CreateProductionSetRequest(fileCount);
        CreateSyntheticProductionSet(prodPath, fileCount);

        var clean = ProductionSetPostValidator.Validate(prodPath, request);
        Assert.Equal("passed", clean.Status);
        Assert.Equal(0, clean.ErrorCount);
        Assert.Equal(1, clean.CheckedFileCounts["dat"]);
        Assert.Equal(1, clean.CheckedFileCounts["opt"]);
        Assert.Equal(fileCount, clean.CheckedFileCounts["native"]);
        Assert.Equal(fileCount, clean.CheckedFileCounts["text"]);
        Assert.Equal(2 * fileCount, clean.CheckedFileCounts["image"]);
        Assert.Equal(fileCount + 1, clean.CheckedLoadFileRowCounts["dat"]);
        Assert.Equal(fileCount, clean.CheckedLoadFileRowCounts["opt"]);

        ApplyProductionSetFault(prodPath, mutationType);

        var report = ProductionSetPostValidator.Validate(prodPath, request);
        Assert.Equal("failed", report.Status);

        switch (mutationType)
        {
            case 0 or 1 or 2 or 3:
                Assert.Contains(report.Findings, f => f.Code == "PathExistence" && f.Severity == "error");

                // REQ-149: checkedFileCounts counts references examined, not files found, so
                // deleting a referenced file must not move any counter.
                Assert.Equal(clean.CheckedFileCounts, report.CheckedFileCounts);
                break;
            case 4:
                Assert.Contains(report.Findings, f => f.Code == "InvalidValue" && f.Message.Contains("NATIVE_WITHHELD", StringComparison.Ordinal));
                break;
            case 5 or 10:
                Assert.Contains(report.Findings, f => f.Code == "UniqueId");
                break;
            case 6 or 7:
                Assert.Contains(report.Findings, f => f.Code == "BatesConsistency");
                break;
            case 8:
                Assert.Contains(report.Findings, f => f.Code == "ManifestSyntax");
                break;
            case 9:
                Assert.Contains(report.Findings, f => f.Code == "OptBoundary");
                break;
            case 11 or 12 or 13:
                Assert.Contains(report.Findings, f => f.Code == "MetadataAzureConstraint");
                break;
        }

        Assert.True(report.ErrorCount > 0, $"Mutation category {mutationType} must be detected as a validation error.");
    }

    // ------------------------------------------------------------------
    // 3. SupplementalValidator — interval disjointness iff success
    // ------------------------------------------------------------------

    /// <summary>
    /// Supplemental validation across arbitrary Bates Number intervals: with two baseline
    /// intervals (sometimes adjacent, sometimes with a small gap) and one arbitrary planned
    /// interval, validation succeeds exactly when the planned interval is strictly disjoint
    /// from every baseline interval, and under the <c>reject</c> gap policy it additionally
    /// fails exactly when the merged coverage leaves a skipped Bates Number (REQ-137,
    /// REQ-138). The report's expected-next Bates Number is the maximum baseline end + 1 on
    /// every success (REQ-140). The planned interval's placement (overlap, adjacency,
    /// far-disjoint) is derived from the seed so every run exercises each branch of the
    /// iff-contract instead of relying on uniform draws that rarely land near a baseline.
    /// </summary>
    [Property]
    public void SupplementalValidator_ValidateAsync_WithArbitraryBatesRanges_ShouldRejectExactlyTheOverlaps(ValidationMutationPropertyInput input)
    {
        long priorStart = Math.Clamp(input.PriorStart, 1, 50_000);
        long priorEnd = priorStart + Math.Clamp(input.PriorLength, 0, 50);

        // A second baseline interval above the first, adjacent or at most two Bates Numbers
        // away, so the merged baseline is sometimes contiguous and sometimes gapped.
        long secondStart = priorEnd + 1 + (Math.Abs((long)input.Seed) % 3);
        long secondEnd = secondStart + Math.Clamp(input.PriorLength, 0, 50);

        // Placement mode guarantees branch coverage: 0 overlaps the first baseline,
        // 1 sits adjacent below it, 2 sits adjacent above the second baseline,
        // 3 is an arbitrary (usually far-disjoint) draw.
        long plannedLength = Math.Clamp(input.PlannedLength, 0, 50);
        int placement = (int)(Math.Abs((long)input.Seed) % 4);
        long plannedStart = placement switch
        {
            0 => priorStart + (Math.Abs((long)input.Seed / 4) % Math.Max(1, priorEnd - priorStart + 1)),
            1 => Math.Max(1, priorStart - plannedLength - 1),
            2 => secondEnd + 1,
            _ => Math.Clamp(input.PlannedStart, 1, 50_000),
        };

        long plannedEnd = plannedStart + plannedLength;

        var caseDir = CreateCaseDirectory("supplemental");
        var priorManifestPath = WritePriorManifest(caseDir, "baseline", PriorPrefix, PriorDigits, priorStart, priorEnd);
        var secondManifestPath = WritePriorManifest(caseDir, "baseline2", PriorPrefix, PriorDigits, secondStart, secondEnd);

        var baselines = new[] { (priorStart, priorEnd), (secondStart, secondEnd) };
        bool overlapsBaseline = baselines.Any(b => Math.Max(b.Item1, plannedStart) <= Math.Min(b.Item2, plannedEnd));
        bool leavesGap = MergedRangeCount([.. baselines, (plannedStart, plannedEnd)]) > 1;

        var allowRequest = CreateSupplementalRequest([priorManifestPath, secondManifestPath], "allow");
        var plannedStartText = FormatPrior(plannedStart);
        var plannedEndText = FormatPrior(plannedEnd);

        if (overlapsBaseline)
        {
            var overlapError = Assert.Throws<ValidationFailedException>(() =>
                SupplementalValidator.ValidateAsync(allowRequest, plannedStartText, plannedEndText).GetAwaiter().GetResult());
            Assert.Contains("Duplicate Bates Numbers", overlapError.Message, StringComparison.Ordinal);
        }
        else
        {
            var report = SupplementalValidator.ValidateAsync(allowRequest, plannedStartText, plannedEndText).GetAwaiter().GetResult();
            Assert.Empty(report.DuplicateRanges);
            Assert.Equal(plannedStartText, report.ActualStartingBates);

            // REQ-140: expected next Bates Number is one past the maximum baseline end.
            Assert.Equal(FormatPrior(secondEnd + 1), report.ExpectedNextBates);
        }

        // REQ-138: the reject gap policy fails exactly when overlap or gap is present.
        var rejectRequest = CreateSupplementalRequest([priorManifestPath, secondManifestPath], "reject");
        if (overlapsBaseline || leavesGap)
        {
            Assert.Throws<ValidationFailedException>(() =>
                SupplementalValidator.ValidateAsync(rejectRequest, plannedStartText, plannedEndText).GetAwaiter().GetResult());
        }
        else
        {
            var contiguousReport = SupplementalValidator.ValidateAsync(rejectRequest, plannedStartText, plannedEndText).GetAwaiter().GetResult();
            Assert.Empty(contiguousReport.DuplicateRanges);
            Assert.Empty(contiguousReport.SkippedRanges);
        }
    }

    // ------------------------------------------------------------------
    // 4. Rolling Bates prefixes and starts across Rolling Production Sets
    // ------------------------------------------------------------------

    /// <summary>
    /// Planner ↔ validator agreement at exact Rolling Production Set boundaries (REQ-171, REQ-172):
    /// for every rolling index, <see cref="ProductionSetPlanner"/> resolves the per-set Bates Numbers
    /// from the per-index <c>Prefixes</c>/<c>Starts</c> arrays (or the continuous/restart
    /// fallback), and <see cref="ProductionSetPostValidator"/> validates a synthetic Rolling
    /// Production Set built from those planned values against the same root request. A set
    /// built from the <em>previous</em> rolling index's values, or shifted by one increment, must fail.
    /// </summary>
    [Property]
    public void BatesSequence_MultiVolumePrefixesAndStarts_ShouldTransitionAtExactVolumeBoundaries(ValidationMutationPropertyInput input)
    {
        int prefixCount = Math.Clamp(input.PrefixCount, 1, 6);
        int fileCount = Math.Clamp(input.FileCount, 2, 6);
        int rollingIndex = Math.Clamp(input.SequenceNumber, 1, prefixCount) - 1;
        bool useExplicitStarts = input.UseExplicitStarts;
        var mode = input.UseRestartMode ? RollingBatesMode.Restart : RollingBatesMode.Continuous;

        var prefixes = Enumerable.Range(0, prefixCount).Select(i => $"VOL{i:D2}").ToArray();
        var starts = input.BatesStarts;

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileCount = fileCount, FileType = "pdf" },
            Production = new ProductionConfig { ProductionSet = true, VolumeSize = 100, RollingBatesMode = mode },
            Bates = new BatesNumberConfig
            {
                Prefix = prefixes[0],
                Start = starts[0],
                Digits = BatesDigits,
                Prefixes = useExplicitStarts ? prefixes : null,
                Starts = useExplicitStarts ? starts.Select(b => (long)b).ToArray() : null,
            },
        };

        // Per-rolling-index planned Bates Numbers for every index, resolved by the real planner.
        var plannedByVolume = new List<IReadOnlyList<ProductionNativeFilePlan>>();
        for (int index = 0; index < prefixCount; index++)
        {
            var plans = ProductionSetPlanner.Plan(request, index);
            Assert.Equal(fileCount, plans.Count);

            string expectedPrefix = useExplicitStarts ? prefixes[index] : prefixes[0];
            long expectedStart = useExplicitStarts
                ? starts[index]
                : mode == RollingBatesMode.Restart
                    ? starts[0]
                    : starts[0] + ((long)index * fileCount);

            Assert.All(plans, p => Assert.StartsWith(expectedPrefix, p.BatesNumber, StringComparison.Ordinal));
            Assert.Equal(FormatBates(expectedPrefix, expectedStart), plans[0].BatesNumber);
            Assert.Equal(FormatBates(expectedPrefix, expectedStart + fileCount - 1), plans[^1].BatesNumber);

            plannedByVolume.Add(plans);
        }

        // A rolling Production Set built from this rolling index's planned Bates Numbers must
        // validate against the same root request: planner and validator agree per rolling index.
        var validPath = CreateCaseDirectory("rolling");
        WriteRollingProductionSet(validPath, plannedByVolume[rollingIndex], rollingIndex + 1);
        var validReport = ProductionSetPostValidator.Validate(validPath, request);
        Assert.Equal("passed", validReport.Status);

        // A set built from the PREVIOUS rolling index's Bates Numbers must fail, except when the
        // previous index resolves to the same values (restart without explicit starts, or
        // the root config itself, which the validator accepts as already-effective).
        if (rollingIndex >= 2)
        {
            var previousPlans = plannedByVolume[rollingIndex - 1];
            bool previousVolumeIsDistinct =
                !string.Equals(previousPlans[0].BatesNumber, plannedByVolume[rollingIndex][0].BatesNumber, StringComparison.Ordinal);
            if (previousVolumeIsDistinct)
            {
                var crossedPath = CreateCaseDirectory("rolling_prev");
                WriteRollingProductionSet(crossedPath, previousPlans, rollingIndex + 1);
                var crossedReport = ProductionSetPostValidator.Validate(crossedPath, request);
                Assert.Equal("failed", crossedReport.Status);
                Assert.Contains(crossedReport.Findings, f => f.Code == "BatesConsistency");
            }
        }

        // An off-by-one shifted start must always fail: the first Bates Number is exact.
        string currentPrefix = useExplicitStarts ? prefixes[rollingIndex] : prefixes[0];
        var shiftedPlans = new List<(string Bates, string DocId)>
        {
            (ShiftOneIncrement(currentPrefix, plannedByVolume[rollingIndex][0].BatesNumber), "DOC00000001"),
        };
        for (int i = 1; i < fileCount; i++)
        {
            shiftedPlans.Add((plannedByVolume[rollingIndex][i].BatesNumber, $"DOC{i + 1:D8}"));
        }

        var shiftedPath = CreateCaseDirectory("rolling_shift");
        WriteRollingProductionSetFrom(shiftedPath, shiftedPlans, rollingIndex + 1);
        var shiftedReport = ProductionSetPostValidator.Validate(shiftedPath, request);
        Assert.Equal("failed", shiftedReport.Status);
        Assert.Contains(shiftedReport.Findings, f => f.Code == "BatesConsistency");
    }

    // ------------------------------------------------------------------
    // 5. VolumeAnalyzer — reflexive identity and symmetric delta counts
    // ------------------------------------------------------------------

    /// <summary>
    /// Volume rollup laws (REQ-162): comparing a record set to itself yields one
    /// <c>unchanged</c> Volume Result per distinct Volume whose Bates Number range equals the
    /// independently computed min–max of that Volume's records; swapping prior and new sides
    /// swaps <c>added</c> ↔ <c>removed</c> counts and swaps the reported Bates ranges while
    /// preserving the <c>changed</c>/<c>unchanged</c> classification of shared Volumes.
    /// </summary>
    [Property]
    public void VolumeAnalyzer_Compare_ShouldSatisfyReflexiveIdentityAndSymmetricDeltaCounts(ValidationMutationPropertyInput input)
    {
        int seed = (int)(Math.Abs((long)input.Seed) % 1_000_000L);
        var priorRecords = BuildComparisonRecords(seed, "PRIOR");
        var newRecords = MutateComparisonRecords(priorRecords, seed + 1);

        // Reflexive identity: v vs v.
        var reflexive = new List<VolumeResult>();
        VolumeAnalyzer.AnalyzeVolumes(priorRecords, priorRecords, reflexive);

        var priorVolumes = priorRecords
            .Select(r => r.Volume)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Equal(priorVolumes.Count, reflexive.Count);
        foreach (var volume in priorVolumes)
        {
            var result = Assert.Single(reflexive, r => string.Equals(r.VolumeName, volume, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("unchanged", result.Status);
            Assert.Equal(result.PriorBatesRange, result.NewBatesRange);

            var batesInVolume = priorRecords
                .Where(r => string.Equals(r.Volume, volume, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.BatesNumber)
                .ToList();
            Assert.Equal($"{batesInVolume.Min()} - {batesInVolume.Max()}", result.PriorBatesRange);
        }

        // Symmetric delta counts: swapping the sides swaps added ↔ removed and the
        // reported Bates ranges, and preserves the changed/unchanged classification.
        var priorThenNew = new List<VolumeResult>();
        VolumeAnalyzer.AnalyzeVolumes(priorRecords, newRecords, priorThenNew);
        var newThenPrior = new List<VolumeResult>();
        VolumeAnalyzer.AnalyzeVolumes(newRecords, priorRecords, newThenPrior);

        Assert.Equal(
            priorThenNew.Count(r => r.Status == "added"),
            newThenPrior.Count(r => r.Status == "removed"));
        Assert.Equal(
            priorThenNew.Count(r => r.Status == "removed"),
            newThenPrior.Count(r => r.Status == "added"));

        foreach (var result in priorThenNew)
        {
            var swapped = newThenPrior.Single(r => string.Equals(r.VolumeName, result.VolumeName, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(result.PriorBatesRange, swapped.NewBatesRange);
            Assert.Equal(result.NewBatesRange, swapped.PriorBatesRange);
            switch (result.Status)
            {
                case "added":
                    Assert.Equal("removed", swapped.Status);
                    break;
                case "removed":
                    Assert.Equal("added", swapped.Status);
                    break;
                default:
                    Assert.Equal(result.Status, swapped.Status);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string CreateCaseDirectory(string label)
    {
        var dir = Path.Combine(this.tempRoot, $"{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FileGenerationRequest CreateStandardModeRequest(string outputPath, int fileCount) => new()
    {
        Output = new OutputConfig { OutputPath = outputPath, FileCount = fileCount, FileType = "pdf" },
        Bates = new BatesNumberConfig { Prefix = "DOC", Start = 1, Digits = BatesDigits },
        Delimiters = new DelimiterConfig { ColumnDelimiter = ColDelim.ToString(), QuoteDelimiter = QuoteDelim.ToString() },
    };

    private static FileGenerationRequest CreateProductionSetRequest(int fileCount) => new()
    {
        Output = new OutputConfig { FileCount = fileCount, FileType = "pdf" },
        Production = new ProductionConfig { ProductionSet = true, VolumeSize = 100 },
        Bates = new BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = BatesDigits },
        Delimiters = new DelimiterConfig { ColumnDelimiter = ColDelim.ToString(), QuoteDelimiter = QuoteDelim.ToString() },
    };

    private static FileGenerationRequest CreateSupplementalRequest(IReadOnlyList<string> priorManifests, string gapPolicy) => new()
    {
        Bates = new BatesNumberConfig { Prefix = PriorPrefix, Start = 1, Digits = PriorDigits },
        Production = new ProductionConfig
        {
            ProductionSet = true,
            SupplementalProduction = true,
            PriorManifests = priorManifests,
            SupplementalGapPolicy = gapPolicy,
        },
    };

    private static void CreateStandardArchiveAndDat(string archivePath, string datPath, int fileCount)
    {
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            for (int i = 0; i < fileCount; i++)
            {
                var entry = archive.CreateEntry($"folder_{i:D3}/file_{i:D8}.pdf");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("%PDF-1.4");
            }
        }

        var rows = new List<string> { JoinDatFields("DOCID", "PATH") };
        for (int i = 0; i < fileCount; i++)
        {
            rows.Add(JoinDatFields($"DOC{i:D8}", $"folder_{i:D3}/file_{i:D8}.pdf"));
        }

        // Written with explicit CRLF so the clean fixture satisfies the request's default
        // CRLF Line Ending contract on every platform.
        File.WriteAllText(datPath, string.Join("\r\n", rows) + "\r\n");
    }

    private static void AppendDatRow(string datPath, params string[] fields)
    {
        File.AppendAllText(datPath, JoinDatFields(fields) + "\r\n");
    }

    private static void DuplicateFirstDocId(string datPath)
    {
        var lines = File.ReadAllLines(datPath);
        var firstRow = ProductionSetPostValidator.ParseDatLine(lines[1], ColDelim, QuoteDelim);
        var secondRow = ProductionSetPostValidator.ParseDatLine(lines[2], ColDelim, QuoteDelim);
        secondRow[0] = firstRow[0];
        lines[2] = JoinDatFields([.. secondRow]);
        File.WriteAllText(datPath, string.Join("\r\n", lines) + "\r\n");
    }

    private static void DeleteArchiveEntry(string archivePath, string entryName)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        archive.GetEntry(entryName)?.Delete();
    }

    /// <summary>
    /// Builds a minimal self-consistent synthetic Production Set on disk: DAT and OPT Load
    /// Files, a Production Manifest whose Bates range matches the DAT parent rows, and the
    /// referenced native, text, image, and redacted image files. Hand-built so each fault
    /// category has exactly the files and columns it needs and 100 iterations stay fast.
    /// </summary>
    private static void CreateSyntheticProductionSet(string prodPath, int fileCount)
    {
        Directory.CreateDirectory(Path.Combine(prodPath, "DATA"));
        Directory.CreateDirectory(Path.Combine(prodPath, "NATIVES"));
        Directory.CreateDirectory(Path.Combine(prodPath, "TEXT"));
        Directory.CreateDirectory(Path.Combine(prodPath, "IMAGES"));
        Directory.CreateDirectory(Path.Combine(prodPath, "REDACTED", "IMAGES"));

        var datRows = new List<string>
        {
            JoinDatFields("DOCID", "BATES_NUMBER", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "PARENTDOCID", "REDACTED_IMAGE_PATH", "REDACTED_TEXT_PATH", "NATIVE_WITHHELD"),
        };

        var optRows = new List<string>();
        for (int i = 1; i <= fileCount; i++)
        {
            var bates = FormatBates("TEST", i);
            var nativeRel = $"NATIVES\\DOC{i:D8}.pdf";
            var textRel = $"TEXT\\DOC{i:D8}.txt";
            var imageRel = $"IMAGES\\DOC{i:D8}_001.tif";
            var redactedImageRel = $"REDACTED\\IMAGES\\RIMG{i:D8}.tif";

            File.WriteAllText(Path.Combine(prodPath, "NATIVES", $"DOC{i:D8}.pdf"), "%PDF-1.4");
            File.WriteAllText(Path.Combine(prodPath, "TEXT", $"DOC{i:D8}.txt"), "extracted text");
            File.WriteAllText(Path.Combine(prodPath, "IMAGES", $"DOC{i:D8}_001.tif"), "II*");
            File.WriteAllText(Path.Combine(prodPath, "REDACTED", "IMAGES", $"RIMG{i:D8}.tif"), "II*");

            datRows.Add(JoinDatFields($"DOC{i:D8}", bates, nativeRel, textRel, imageRel, string.Empty, redactedImageRel, string.Empty, "NO"));

            // The OPT is a fixed 7-column comma-delimited format whose column 3 carries a
            // real image path, so each record's image reference is checked twice (DAT + OPT).
            optRows.Add($"{bates},CTRL{i:D8},IMAGES\\DOC{i:D8}_001.tif,1,0,,");
        }

        File.WriteAllLines(Path.Combine(prodPath, "DATA", "loadfile.dat"), datRows);
        File.WriteAllLines(Path.Combine(prodPath, "DATA", "loadfile.opt"), optRows);

        var manifest = new JsonObject
        {
            ["productionId"] = "PROPTEST",
            ["batesNumberStart"] = FormatBates("TEST", 1),
            ["batesNumberEnd"] = FormatBates("TEST", fileCount),
            ["metadata"] = new JsonObject
            {
                ["production_id"] = "PROPTEST",
                ["bates_number_start"] = FormatBates("TEST", 1),
                ["bates_number_end"] = FormatBates("TEST", fileCount),
                ["volume_count"] = "1",
            },
        };
        File.WriteAllText(Path.Combine(prodPath, "_manifest.json"), manifest.ToJsonString());
    }

    private static void ApplyProductionSetFault(string prodPath, int mutationType)
    {
        var datPath = Path.Combine(prodPath, "DATA", "loadfile.dat");
        var optPath = Path.Combine(prodPath, "DATA", "loadfile.opt");
        var manifestPath = Path.Combine(prodPath, "_manifest.json");

        switch (mutationType)
        {
            case 0:
                File.Delete(Path.Combine(prodPath, "NATIVES", "DOC00000001.pdf"));
                break;
            case 1:
                File.Delete(Path.Combine(prodPath, "TEXT", "DOC00000001.txt"));
                break;
            case 2:
                File.Delete(Path.Combine(prodPath, "IMAGES", "DOC00000001_001.tif"));
                break;
            case 3:
                File.Delete(Path.Combine(prodPath, "REDACTED", "IMAGES", "RIMG00000001.tif"));
                break;
            case 4:
                RewriteDatRow(datPath, 1, fields => fields[8] = "MAYBE");
                break;
            case 5:
                RewriteDatRow(datPath, 2, fields => fields[1] = FormatBates("TEST", 1));
                break;
            case 6:
                RewriteDatRow(datPath, 2, fields => fields[1] = FormatBates("TEST", 3));
                break;
            case 7:
                {
                    var datLines = File.ReadAllLines(datPath).ToList();
                    datLines.RemoveAt(datLines.Count - 1);
                    File.WriteAllLines(datPath, datLines);
                    break;
                }
            case 8:
                File.WriteAllText(manifestPath, "{ not valid json ");
                break;
            case 9:
                {
                    var optLines = File.ReadAllLines(optPath).ToList();
                    optLines[0] += ",EXTRA_COL";
                    File.WriteAllLines(optPath, optLines);
                    break;
                }
            case 10:
                RewriteDatRow(datPath, 2, fields => fields[0] = "DOC00000001");
                break;
            case 11:
                MutateManifestMetadata(manifestPath, metadata => metadata["production_id"] = "caf\u00e9");
                break;
            case 12:
                MutateManifestMetadata(manifestPath, metadata => metadata["bad key!"] = "value");
                break;
            case 13:
                MutateManifestMetadata(manifestPath, metadata => metadata["volume_count"] = 42);
                break;
        }
    }

    private static void RewriteDatRow(string datPath, int dataRowIndex, Action<List<string>> mutate)
    {
        var lines = File.ReadAllLines(datPath).ToList();
        var fields = ProductionSetPostValidator.ParseDatLine(lines[dataRowIndex], ColDelim, QuoteDelim);
        mutate(fields);
        lines[dataRowIndex] = JoinDatFields([.. fields]);
        File.WriteAllLines(datPath, lines);
    }

    private static void MutateManifestMetadata(string manifestPath, Action<JsonObject> mutate)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        mutate(manifest["metadata"]!.AsObject());
        File.WriteAllText(manifestPath, manifest.ToJsonString());
    }

    /// <summary>
    /// Writes a minimal rolling Production Set whose DAT parent Bates Numbers are exactly the
    /// planned values, so validation exercises the validator's rolling effective-Bates
    /// resolution against the manifest's <c>rollingSequenceNumber</c>.
    /// </summary>
    private static void WriteRollingProductionSet(string prodPath, IReadOnlyList<ProductionNativeFilePlan> plans, int rollingSequenceNumber)
    {
        var rows = plans
            .Select((plan, index) => (Bates: plan.BatesNumber, DocId: $"DOC{index + 1:D8}"))
            .ToList();
        WriteRollingProductionSetFrom(prodPath, rows, rollingSequenceNumber);
    }

    private static void WriteRollingProductionSetFrom(string prodPath, IReadOnlyList<(string Bates, string DocId)> rows, int rollingSequenceNumber)
    {
        Directory.CreateDirectory(Path.Combine(prodPath, "DATA"));

        var datRows = new List<string> { JoinDatFields("DOCID", "BATES_NUMBER") };
        var optRows = new List<string>();
        foreach (var (bates, docId) in rows)
        {
            datRows.Add(JoinDatFields(docId, bates));
            optRows.Add($"{bates},{docId},,1,0,,");
        }

        File.WriteAllLines(Path.Combine(prodPath, "DATA", "loadfile.dat"), datRows);
        File.WriteAllLines(Path.Combine(prodPath, "DATA", "loadfile.opt"), optRows);

        var manifest = new JsonObject
        {
            ["productionId"] = "ROLLING",
            ["batesNumberStart"] = rows[0].Bates,
            ["batesNumberEnd"] = rows[^1].Bates,
            ["rollingSequenceNumber"] = rollingSequenceNumber,
        };
        File.WriteAllText(Path.Combine(prodPath, "_manifest.json"), manifest.ToJsonString());
    }

    private static string ShiftOneIncrement(string prefix, string batesNumber)
    {
        var numeric = long.Parse(batesNumber[prefix.Length..], CultureInfo.InvariantCulture);
        return FormatBates(prefix, numeric + 1);
    }

    private static List<ComparisonRecord> BuildComparisonRecords(int seed, string productionId)
    {
        var rng = new Random(seed);
        int volumeCount = 1 + rng.Next(4);
        var records = new List<ComparisonRecord>();
        int sequence = 1;
        for (int volume = 1; volume <= volumeCount; volume++)
        {
            int recordCount = 1 + rng.Next(4);
            for (int i = 0; i < recordCount; i++, sequence++)
            {
                records.Add(new ComparisonRecord
                {
                    ProductionId = productionId,
                    Volume = $"VOL{volume:D3}",
                    BatesNumber = $"DOC{sequence.ToString("D8", CultureInfo.InvariantCulture)}",
                    FilePath = $"VOL{volume:D3}/NATIVES/DOC{sequence.ToString("D8", CultureInfo.InvariantCulture)}.pdf",
                    // Empty hashes are legal and must never flip a Volume to changed (REQ-162).
                    Hash = rng.Next(3) switch
                    {
                        0 => string.Empty,
                        1 => "aaaaaaaa",
                        _ => "bbbbbbbb",
                    },
                });
            }
        }

        return records;
    }

    private static List<ComparisonRecord> MutateComparisonRecords(List<ComparisonRecord> source, int seed)
    {
        var rng = new Random(seed);
        var mutated = source
            .Select(r => new ComparisonRecord
            {
                ProductionId = r.ProductionId,
                Volume = r.Volume,
                BatesNumber = r.BatesNumber,
                FilePath = r.FilePath,
                Hash = r.Hash,
            })
            .ToList();

        if (mutated.Count > 0 && rng.Next(2) == 0)
        {
            mutated[rng.Next(mutated.Count)].Hash = "cccccccc";
        }

        var volumes = mutated.Select(r => r.Volume).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (volumes.Count > 0 && rng.Next(2) == 0)
        {
            var renamedFrom = volumes[rng.Next(volumes.Count)];
            var renamed = $"VOL{900 + rng.Next(9):D3}";
            foreach (var record in mutated.Where(r => string.Equals(r.Volume, renamedFrom, StringComparison.OrdinalIgnoreCase)))
            {
                record.Volume = renamed;
            }
        }

        if (rng.Next(2) == 0)
        {
            mutated.Add(new ComparisonRecord
            {
                ProductionId = "NEW",
                Volume = "VOL950",
                BatesNumber = "DOC99999999",
                FilePath = "VOL950/NATIVES/DOC99999999.pdf",
                Hash = string.Empty,
            });
        }

        if (mutated.Count > 1 && rng.Next(2) == 0)
        {
            mutated.RemoveAt(rng.Next(mutated.Count));
        }

        return mutated;
    }

    private static int MergedRangeCount(IReadOnlyList<(long Start, long End)> ranges)
    {
        // Merge rule per the supplemental validator: adjacent (start <= end + 1)
        // ranges leave no skipped Bates Number between them.
        var mergedRanges = new List<(long Start, long End)>();
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (mergedRanges.Count == 0)
            {
                mergedRanges.Add(range);
            }
            else
            {
                var last = mergedRanges[^1];
                if (range.Start <= last.End + 1)
                {
                    mergedRanges[^1] = (last.Start, Math.Max(last.End, range.End));
                }
                else
                {
                    mergedRanges.Add(range);
                }
            }
        }

        return mergedRanges.Count;
    }

    private static string WritePriorManifest(string dir, string volumeName, string prefix, int digits, long start, long end)
    {
        var manifestDir = Path.Combine(dir, volumeName);
        Directory.CreateDirectory(manifestDir);
        var manifestPath = Path.Combine(manifestDir, "_manifest.json");
        var manifest = new JsonObject
        {
            ["volumeName"] = volumeName,
            ["productionId"] = "P1",
            ["batesRange"] = new JsonObject
            {
                ["start"] = FormatBates(prefix, start, digits),
                ["end"] = FormatBates(prefix, end, digits),
                ["prefix"] = prefix,
                ["digits"] = digits,
            },
        };
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        return manifestPath;
    }

    private static string JoinDatFields(params string[] fields) => string.Join(ColDelim, fields.Select(f => $"{QuoteDelim}{f}{QuoteDelim}"));

    private static string FormatPrior(long value) => FormatBates(PriorPrefix, value, PriorDigits);

    // Deliberately independent of the production FormatBates helpers (SupplementalValidator,
    // BatesRangeAnalyzer): a test oracle must not share formatting code with the code under test.
    private static string FormatBates(string prefix, long value) => FormatBates(prefix, value, BatesDigits);

    private static string FormatBates(string prefix, long value, int digits)
        => $"{prefix}{value.ToString($"D{digits}", CultureInfo.InvariantCulture)}";
}
