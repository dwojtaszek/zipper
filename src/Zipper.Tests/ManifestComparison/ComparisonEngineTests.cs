using Xunit;
using Zipper.ManifestComparison;

namespace Zipper.Tests;

public class ComparisonEngineTests
{
    private static ComparisonRecord Rec(string bates, string control, string path, string hash, string volume, int line = 0) =>
        new() { BatesNumber = bates, ControlNumber = control, FilePath = path, Hash = hash, Volume = volume, SourceLine = line };

    [Fact]
    public void PerformComparison_MultiplePriors_BuildsManifestListInInputOrder()
    {
        // REQ-157: the Comparison Report preserves the supplied manifest order —
        // every prior path in order, then the new manifest last.
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000010", "DOC010", "n10.pdf", "h10", "VOL001") };

        var result = ComparisonEngine.PerformComparison(
            prior, newRecs, "replacement",
            new List<DuplicateDetail>(), new List<DuplicateDetail>(),
            new List<string> { "/prior1/_manifest.json", "/prior2/_manifest.json" }, "/new/_manifest.json");

        Assert.Equal(new[] { "/prior1/_manifest.json", "/prior2/_manifest.json", "/new/_manifest.json" }, result.Manifests);
        Assert.Equal(1, result.Summary.TotalPriorRecords);
        Assert.Equal(1, result.Summary.TotalNewRecords);
    }

    // REQ-158/REQ-160: replacement and reproduction share identical matching and
    // classification; only the reported Comparison Mode differs. The same
    // fixture matrix must produce identical detail/count results in both modes.
    public static TheoryData<string> ReplacementEquivalenceModes => new() { "replacement", "reproduction" };

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_IdenticalClassification(string mode)
    {
        // Same Bates + everything equal -> unchanged.
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Equal(mode, result.ComparisonMode);
        Assert.Single(result.Details.Unchanged);
        Assert.Empty(result.Details.Changed);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_SameBatesChangedHash_ClassifiesChanged(string mode)
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1-modified", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Changed);
        Assert.Equal("h1", result.Details.Changed[0].PriorHash);
        Assert.Equal("h1-modified", result.Details.Changed[0].NewHash);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_SameControlDifferentBates_ClassifiesReplaced(string mode)
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000099", "DOC001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Replaced);
        Assert.Equal("PR000001", result.Details.Replaced[0].PriorBatesNumber);
        Assert.Equal("PR000099", result.Details.Replaced[0].NewBatesNumber);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_OnlyInPriorOrNew_ClassifiesRemovedAndAdded(string mode)
    {
        var prior = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001"),
            Rec("PR000002", "DOC002", "n2.pdf", "h2", "VOL001")
        };
        var newRecs = new List<ComparisonRecord> { Rec("PR000010", "DOC010", "n10.pdf", "h10", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Added);
        Assert.Equal(2, result.Details.Removed.Count);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_MissingHashOnEitherSide_StillUnchanged(string mode)
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        // Empty hash values never block an unchanged classification (REQ-158).
        Assert.Single(result.Details.Unchanged);
        Assert.Empty(result.Details.Changed);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_DuplicateBatesInPrior_ClassifiesDuplicated(string mode)
    {
        var prior = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001", line: 2),
            Rec("PR000001", "DOC002", "n2.pdf", "h2", "VOL001", line: 5)
        };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var priorDuplicates = ComparisonEngine.FindDuplicates(prior, "prior");

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, priorDuplicates, new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Duplicates);
        Assert.Contains("Duplicate Bates Number 'PR000001'", result.Details.Duplicates[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_CaseInsensitiveMatches_ClassifiesUnchanged(string mode)
    {
        // Case-insensitive Volume and Bates Number matching is pinned behavior.
        var prior = new List<ComparisonRecord> { Rec("pr000001", "doc001", "NATIVES/vol001/n1.pdf", "H1", "vol001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "natives/VOL001/n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Unchanged);
        Assert.Empty(result.Details.Changed);
        Assert.Empty(result.Details.Replaced);
    }

    [Fact]
    public void PerformComparison_ReplacementMode_ClassifiesAddedAndRemoved()
    {
        var prior = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "h1", "VOL001"),
            Rec("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "h2", "VOL001")
        };
        var newRecs = new List<ComparisonRecord>
        {
            Rec("PR000010", "DOC010", "NATIVES/VOL001/doc10.pdf", "h10", "VOL001")
        };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "replacement", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/prior/_manifest.json" }, "/new/_manifest.json");

        Assert.Equal("replacement", result.ComparisonMode);
        Assert.Single(result.Details.Added);
        Assert.Equal(2, result.Details.Removed.Count);
        Assert.Empty(result.Details.Unchanged);
        Assert.Equal(2, result.Summary.TotalPriorRecords);
        Assert.Equal(1, result.Summary.TotalNewRecords);
    }

    [Fact]
    public void PerformComparison_MatchingBates_SameHashPathControl_ClassifiesUnchanged()
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "replacement", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Unchanged);
        Assert.Empty(result.Details.Changed);
        Assert.Empty(result.Details.Added);
        Assert.Empty(result.Details.Removed);
    }

    [Fact]
    public void PerformComparison_MatchingBates_DifferentHash_ClassifiesChanged()
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1-modified", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "replacement", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Changed);
        Assert.Equal("h1", result.Details.Changed[0].PriorHash);
        Assert.Equal("h1-modified", result.Details.Changed[0].NewHash);
    }

    [Fact]
    public void PerformComparison_SupplementalMode_OverlapRecordedAsDuplicateAndAdded_NoRemovals()
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "supplemental", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        // Supplemental mode: overlapping Bates/Control is a duplicate + added; no removals.
        Assert.True(result.Summary.DuplicateCount > 0);
        Assert.Single(result.Details.Added);
        Assert.Empty(result.Details.Removed);
    }

    [Fact]
    public void FindDuplicates_WithRepeatedBates_ReportsDuplicates()
    {
        var records = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001", line: 2),
            Rec("PR000001", "DOC002", "n2.pdf", "h2", "VOL001", line: 5)
        };

        var dups = ComparisonEngine.FindDuplicates(records, "prior");

        Assert.Single(dups);
        Assert.Equal("prior", dups[0].Set);
        Assert.Contains("Duplicate Bates Number 'PR000001'", dups[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_SameBatesSameHash_PathOnlyChange_ClassifiesChanged(string mode)
    {
        // REQ-158/REQ-160: A Bates match with an equal non-empty hash and Control Number but differing path is changed.
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "NATIVES/VOL001/doc1_moved.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 1, totalNew: 1, added: 0, removed: 0, unchanged: 0, changed: 1, replaced: 0, duplicates: 0);
        var detail = Assert.Single(result.Details.Changed);
        Assert.Equal("PR000001", detail.BatesNumber);
        Assert.Equal("DOC001", detail.ControlNumber);
        Assert.Equal("NATIVES/VOL001/doc1.pdf", detail.PriorPath);
        Assert.Equal("NATIVES/VOL001/doc1_moved.pdf", detail.NewPath);
        Assert.Equal("h1", detail.PriorHash);
        Assert.Equal("h1", detail.NewHash);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_SameBatesPathHash_ControlOnlyChange_ClassifiesChanged(string mode)
    {
        // REQ-158/REQ-160: A Bates match with an equal non-empty hash and path but differing Control Number is changed.
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC002", "NATIVES/VOL001/doc1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 1, totalNew: 1, added: 0, removed: 0, unchanged: 0, changed: 1, replaced: 0, duplicates: 0);
        var detail = Assert.Single(result.Details.Changed);
        Assert.Equal("PR000001", detail.BatesNumber);
        Assert.Equal("DOC002", detail.ControlNumber);
        Assert.Equal("NATIVES/VOL001/doc1.pdf", detail.PriorPath);
        Assert.Equal("NATIVES/VOL001/doc1.pdf", detail.NewPath);
        Assert.Equal("h1", detail.PriorHash);
        Assert.Equal("h1", detail.NewHash);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_BatesMatchesPriorA_ControlMatchesPriorB_PreservesBatesFirstPrecedence(string mode)
    {
        // REQ-158/REQ-160: New-side records match prior-side records by Bates Number first, then Control Number.
        // When a new record's Bates matches prior A and Control matches prior B, Bates match wins -> changed (prior A),
        // and prior B is removed. Swapping precedence would incorrectly match prior B as replaced and remove prior A.
        var prior = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "h1", "VOL001"),
            Rec("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "h2", "VOL001")
        };
        var newRecs = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC002", "NATIVES/VOL001/doc1.pdf", "h1", "VOL001")
        };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 2, totalNew: 1, added: 0, removed: 1, unchanged: 0, changed: 1, replaced: 0, duplicates: 0);

        var changed = Assert.Single(result.Details.Changed);
        Assert.Equal("PR000001", changed.BatesNumber);
        Assert.Equal("DOC002", changed.ControlNumber);
        Assert.Equal("NATIVES/VOL001/doc1.pdf", changed.PriorPath);
        Assert.Equal("NATIVES/VOL001/doc1.pdf", changed.NewPath);
        Assert.Equal("h1", changed.PriorHash);
        Assert.Equal("h1", changed.NewHash);

        var removed = Assert.Single(result.Details.Removed);
        Assert.Equal("PR000002", removed.BatesNumber);
        Assert.Equal("DOC002", removed.ControlNumber);
        Assert.Equal("NATIVES/VOL001/doc2.pdf", removed.FilePath);
        Assert.Equal("h2", removed.Hash);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_SameControlDifferentBates_CaseInsensitiveKeys_ClassifiesReplaced(string mode)
    {
        // REQ-158/REQ-160: A Control-Number-only match (case-insensitive) with differing Bates is replaced.
        var prior = new List<ComparisonRecord> { Rec("PR000001", "ctrl_doc_001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000099", "CTRL_DOC_001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 1, totalNew: 1, added: 0, removed: 0, unchanged: 0, changed: 0, replaced: 1, duplicates: 0);
        var detail = Assert.Single(result.Details.Replaced);
        Assert.Equal("PR000001", detail.PriorBatesNumber);
        Assert.Equal("PR000099", detail.NewBatesNumber);
        Assert.Equal("CTRL_DOC_001", detail.ControlNumber);
    }

    public static TheoryData<string, string, string> MissingHashCombinations => new()
    {
        { "replacement", "h1", "" },     // Missing new hash
        { "replacement", "", "h1" },     // Missing prior hash
        { "replacement", "", "" },       // Both missing
        { "reproduction", "h1", "" },    // Missing new hash
        { "reproduction", "", "h1" },    // Missing prior hash
        { "reproduction", "", "" }       // Both missing
    };

    [Theory]
    [MemberData(nameof(MissingHashCombinations))]
    public void PerformComparison_ReplacementAndReproduction_MissingHashVariants_MetadataUnchanged_ClassifiesUnchanged(string mode, string priorHash, string newHash)
    {
        // REQ-158/REQ-160: Empty hash values on either or both sides never block an unchanged classification when other metadata matches.
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", priorHash, "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", newHash, "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 1, totalNew: 1, added: 0, removed: 0, unchanged: 1, changed: 0, replaced: 0, duplicates: 0);
        var detail = Assert.Single(result.Details.Unchanged);
        Assert.Equal("PR000001", detail.BatesNumber);
        Assert.Equal("DOC001", detail.ControlNumber);
        Assert.Equal("n1.pdf", detail.FilePath);
        Assert.Equal(newHash, detail.Hash);
        Assert.Equal("VOL001", detail.Volume);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_MissingHashWithChangedPathOrControl_ClassifiesChanged(string mode)
    {
        // REQ-158/REQ-160: Missing hash on either side does not mask genuine path or control changes.
        var prior = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001"),
            Rec("PR000002", "DOC002", "n2.pdf", "", "VOL001")
        };
        var newRecs = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1_renamed.pdf", "", "VOL001"), // Path changed, new hash missing
            Rec("PR000002", "DOC002_MOD", "n2.pdf", "h2", "VOL001")   // Control changed, prior hash missing
        };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, mode, new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 2, totalNew: 2, added: 0, removed: 0, unchanged: 0, changed: 2, replaced: 0, duplicates: 0);

        var pathChanged = result.Details.Changed[0];
        Assert.Equal("PR000001", pathChanged.BatesNumber);
        Assert.Equal("DOC001", pathChanged.ControlNumber);
        Assert.Equal("n1.pdf", pathChanged.PriorPath);
        Assert.Equal("n1_renamed.pdf", pathChanged.NewPath);
        Assert.Equal("h1", pathChanged.PriorHash);
        Assert.Equal(string.Empty, pathChanged.NewHash);

        var controlChanged = result.Details.Changed[1];
        Assert.Equal("PR000002", controlChanged.BatesNumber);
        Assert.Equal("DOC002_MOD", controlChanged.ControlNumber);
        Assert.Equal("n2.pdf", controlChanged.PriorPath);
        Assert.Equal("n2.pdf", controlChanged.NewPath);
        Assert.Equal(string.Empty, controlChanged.PriorHash);
        Assert.Equal("h2", controlChanged.NewHash);
    }

    [Fact]
    public void FindDuplicates_WithRepeatedControlNumberAndDistinctBates_ReportsDuplicatesWithSourceLines()
    {
        // REQ-158/REQ-161: Duplicate Control Numbers within one side are independently detected with source lines.
        var priorRecords = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001", line: 4),
            Rec("PR000002", "DOC001", "n2.pdf", "h2", "VOL001", line: 9)
        };
        var newRecords = new List<ComparisonRecord>
        {
            Rec("PR000010", "DOC010", "n10.pdf", "h10", "VOL001", line: 15),
            Rec("PR000011", "DOC010", "n11.pdf", "h11", "VOL001", line: 28)
        };

        var priorDups = ComparisonEngine.FindDuplicates(priorRecords, "prior");
        var newDups = ComparisonEngine.FindDuplicates(newRecords, "new");

        var priorDup = Assert.Single(priorDups);
        Assert.Equal("prior", priorDup.Set);
        Assert.Equal("PR000002", priorDup.BatesNumber);
        Assert.Equal("DOC001", priorDup.ControlNumber);
        Assert.Equal("Duplicate Control Number 'DOC001' on line 9 (first seen on line 4)", priorDup.Message);

        var newDup = Assert.Single(newDups);
        Assert.Equal("new", newDup.Set);
        Assert.Equal("PR000011", newDup.BatesNumber);
        Assert.Equal("DOC010", newDup.ControlNumber);
        Assert.Equal("Duplicate Control Number 'DOC010' on line 28 (first seen on line 15)", newDup.Message);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_DuplicateControlNumbersOnPriorSide_PopulatesDuplicatesWithoutBreakingClassification(string mode)
    {
        // REQ-158/REQ-161: Duplicate Control Numbers on the prior side are independently detected with source lines
        // and reported in duplicates without affecting per-record classification.
        var priorRecords = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001", line: 2),
            Rec("PR000002", "DOC001", "n2.pdf", "h2", "VOL001", line: 6), // Duplicate Control Number on line 6
            Rec("PR000003", "DOC003", "n3.pdf", "h3", "VOL001", line: 9)
        };
        var newRecords = new List<ComparisonRecord>
        {
            Rec("PR000003", "DOC003", "n3.pdf", "h3", "VOL001", line: 3), // Matches PR000003 -> unchanged
            Rec("PR000004", "DOC004", "n4.pdf", "h4", "VOL001", line: 7)  // Added
            // PR000001 and PR000002 omitted -> removed
        };

        var priorDups = ComparisonEngine.FindDuplicates(priorRecords, "prior");
        var newDups = ComparisonEngine.FindDuplicates(newRecords, "new");

        var result = ComparisonEngine.PerformComparison(priorRecords, newRecords, mode, priorDups, newDups, new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 3, totalNew: 2, added: 1, removed: 2, unchanged: 1, changed: 0, replaced: 0, duplicates: 1);

        var dup = Assert.Single(result.Details.Duplicates);
        Assert.Equal("prior", dup.Set);
        Assert.Equal("PR000002", dup.BatesNumber);
        Assert.Equal("DOC001", dup.ControlNumber);
        Assert.Equal("Duplicate Control Number 'DOC001' on line 6 (first seen on line 2)", dup.Message);

        var unc = Assert.Single(result.Details.Unchanged);
        Assert.Equal("PR000003", unc.BatesNumber);

        Assert.Equal(2, result.Details.Removed.Count);
        Assert.Equal("PR000001", result.Details.Removed[0].BatesNumber);
        Assert.Equal("DOC001", result.Details.Removed[0].ControlNumber);
        Assert.Equal("PR000002", result.Details.Removed[1].BatesNumber);
        Assert.Equal("DOC001", result.Details.Removed[1].ControlNumber);

        var add = Assert.Single(result.Details.Added);
        Assert.Equal("PR000004", add.BatesNumber);
    }

    [Theory]
    [MemberData(nameof(ReplacementEquivalenceModes))]
    public void PerformComparison_ReplacementAndReproduction_DuplicateControlNumbersOnNewSide_PopulatesDuplicatesWithoutBreakingClassification(string mode)
    {
        // REQ-158/REQ-161: Duplicate Control Numbers on the new side are independently detected with source lines
        // and reported in duplicates without affecting per-record classification.
        var priorRecords = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001", line: 2)
        };
        var newRecords = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001", line: 3),  // Matches PR000001 -> unchanged
            Rec("PR000010", "DOC010", "n10.pdf", "h10", "VOL001", line: 7), // Added
            Rec("PR000011", "DOC010", "n11.pdf", "h11", "VOL001", line: 12) // Added, duplicate Control Number on line 12
        };

        var priorDups = ComparisonEngine.FindDuplicates(priorRecords, "prior");
        var newDups = ComparisonEngine.FindDuplicates(newRecords, "new");

        var result = ComparisonEngine.PerformComparison(priorRecords, newRecords, mode, priorDups, newDups, new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 1, totalNew: 3, added: 2, removed: 0, unchanged: 1, changed: 0, replaced: 0, duplicates: 1);

        var dup = Assert.Single(result.Details.Duplicates);
        Assert.Equal("new", dup.Set);
        Assert.Equal("PR000011", dup.BatesNumber);
        Assert.Equal("DOC010", dup.ControlNumber);
        Assert.Equal("Duplicate Control Number 'DOC010' on line 12 (first seen on line 7)", dup.Message);

        var unc = Assert.Single(result.Details.Unchanged);
        Assert.Equal("PR000001", unc.BatesNumber);

        Assert.Equal(2, result.Details.Added.Count);
        Assert.Equal("PR000010", result.Details.Added[0].BatesNumber);
        Assert.Equal("PR000011", result.Details.Added[1].BatesNumber);
    }

    [Fact]
    public void PerformComparison_SupplementalMode_ControlOnlyOverlap_RecordedAsDuplicateAndAdded_NeverRemoved()
    {
        // REQ-159/UBIQUITOUS_LANGUAGE: In supplemental mode, overlap on Control Number alone
        // records a duplicate warning and classifies the record as added; supplemental records never establish removals.
        var prior = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001"),
            Rec("PR000002", "DOC002", "n2.pdf", "h2", "VOL001")
        };
        var newRecs = new List<ComparisonRecord>
        {
            Rec("PR000099", "DOC001", "n99.pdf", "h99", "VOL001"),  // Distinct Bates, overlapping Control Number
            Rec("PR000100", "DOC100", "n100.pdf", "h100", "VOL001") // Distinct Bates and Control Number
        };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "supplemental", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        AssertExactSummaryCounts(result, totalPrior: 2, totalNew: 2, added: 2, removed: 0, unchanged: 0, changed: 0, replaced: 0, duplicates: 1);

        var dup = Assert.Single(result.Details.Duplicates);
        Assert.Equal("new", dup.Set);
        Assert.Equal("PR000099", dup.BatesNumber);
        Assert.Equal("DOC001", dup.ControlNumber);
        Assert.Equal("Supplemental overlap: Bates 'PR000099' or Control 'DOC001' matches a prior production set.", dup.Message);

        Assert.Equal(2, result.Details.Added.Count);
        Assert.Equal("PR000099", result.Details.Added[0].BatesNumber);
        Assert.Equal("DOC001", result.Details.Added[0].ControlNumber);
        Assert.Equal("PR000100", result.Details.Added[1].BatesNumber);
        Assert.Equal("DOC100", result.Details.Added[1].ControlNumber);

        Assert.Empty(result.Details.Removed);
        Assert.Empty(result.Details.Unchanged);
        Assert.Empty(result.Details.Changed);
        Assert.Empty(result.Details.Replaced);
    }

    private static void AssertExactSummaryCounts(
        ComparisonResult result,
        int totalPrior,
        int totalNew,
        int added,
        int removed,
        int unchanged,
        int changed,
        int replaced,
        int duplicates)
    {
        Assert.Equal(totalPrior, result.Summary.TotalPriorRecords);
        Assert.Equal(totalNew, result.Summary.TotalNewRecords);
        Assert.Equal(added, result.Summary.AddedCount);
        Assert.Equal(removed, result.Summary.RemovedCount);
        Assert.Equal(unchanged, result.Summary.UnchangedCount);
        Assert.Equal(changed, result.Summary.ChangedCount);
        Assert.Equal(replaced, result.Summary.ReplacedCount);
        Assert.Equal(duplicates, result.Summary.DuplicateCount);

        Assert.Equal(added, result.Details.Added.Count);
        Assert.Equal(removed, result.Details.Removed.Count);
        Assert.Equal(unchanged, result.Details.Unchanged.Count);
        Assert.Equal(changed, result.Details.Changed.Count);
        Assert.Equal(replaced, result.Details.Replaced.Count);
        Assert.Equal(duplicates, result.Details.Duplicates.Count);
    }
}

