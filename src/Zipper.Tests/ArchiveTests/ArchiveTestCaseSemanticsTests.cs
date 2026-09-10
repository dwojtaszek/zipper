using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveTestCaseSemanticsTests
{
    [Fact]
    public void Validate_ValidEmptyCase_ReturnsNoErrors()
    {
        Assert.Empty(ArchiveTestCaseSemantics.Validate(ArchiveTestJsonTests.ValidEmptyCase()));
    }

    [Fact]
    public void Validate_MismatchedFileName_IsRejected()
    {
        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Archive = new ArchiveTestArchive(
                "atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950.zip", 22,
                "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85"),
            FixtureId = "atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e951",
        };

        var errors = ArchiveTestCaseSemantics.Validate(testCase);

        Assert.Contains(errors, error => error.Contains("must equal fixtureId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MutatedFixtureId_IsRejected()
    {
        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with { FixtureId = "not-an-id" };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(testCase),
            error => error.Contains("is not atc-", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UnsafeBasename_IsRejected()
    {
        var fileName = "nested/atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950.zip";
        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Archive = new ArchiveTestArchive(
                fileName, 22, "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85"),
        };

        var errors = ArchiveTestCaseSemantics.Validate(testCase);

        Assert.Contains(errors, error => error.Contains("bare basename", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must equal fixtureId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateEntryOrdinal_IsRejected()
    {
        var entry = new ArchiveTestEntry(
            0, "file", "612e747874", "612e747874", "a.txt", null, null, null, null, null);
        var duplicate = entry with { ReadableName = "b.txt" };
        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Entries = [entry, duplicate],
            Limits = new ArchiveTestLimits(2, 0, 1048576, 10),
        };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(testCase),
            error => error.Contains("ordinal 0 is negative or duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EntryCountMismatch_IsRejected()
    {
        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Limits = new ArchiveTestLimits(EntryCount: 3, ExpandedBytesBudget: 0, JsonBytesBudget: 1048576, DeadlineSeconds: 10),
        };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(testCase),
            error => error.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_OverBudgetLimits_AreRejected()
    {
        var overExpanded = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Limits = new ArchiveTestLimits(0, ArchiveTestCaseSemantics.MaxExpandedBytesBudget + 1, 1048576, 10),
        };
        var overJson = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Limits = new ArchiveTestLimits(0, 0, ArchiveTestCaseSemantics.MaxJsonBytes + 1, 10),
        };
        var overDeadline = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Limits = new ArchiveTestLimits(0, 0, 1048576, ArchiveTestCaseSemantics.MaxDeadlineSeconds + 1),
        };
        var overPhysical = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Archive = new ArchiveTestArchive(
                ArchiveTestJsonTests.ValidEmptyCase().FixtureId + ".zip",
                ArchiveTestCaseSemantics.MaxArchivePhysicalBytes + 1,
                "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85"),
        };

        Assert.Contains(ArchiveTestCaseSemantics.Validate(overExpanded), e => e.Contains("expandedBytesBudget", StringComparison.Ordinal));
        Assert.Contains(ArchiveTestCaseSemantics.Validate(overJson), e => e.Contains("jsonBytesBudget", StringComparison.Ordinal));
        Assert.Contains(ArchiveTestCaseSemantics.Validate(overDeadline), e => e.Contains("deadlineSeconds", StringComparison.Ordinal));
        Assert.Contains(ArchiveTestCaseSemantics.Validate(overPhysical), e => e.Contains("physicalSize", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingMutationLength_IsRejected()
    {
        var mutation = new ArchiveTestMutation(
            Code: "crc-local-mismatch", Structure: "local-header", OffsetBasis: "before-mutation",
            Offset: 14, Explanation: "flips CRC-32", Ordinal: null,
            DeletedLength: null, InsertedLength: null,
            BeforeSize: null, AfterSize: null, BeforeSha256: null, AfterSha256: null,
            BeforeHex: null, AfterHex: null, DeclaredValue: null);
        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with { Mutations = [mutation] };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(testCase),
            error => error.Contains("must declare deletedLength or insertedLength", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeMutationLength_IsRejected()
    {
        var mutation = new ArchiveTestMutation(
            Code: "truncate-payload", Structure: "file-data", OffsetBasis: "before-mutation",
            Offset: 40, Explanation: "truncates payload", Ordinal: 0,
            DeletedLength: -1, InsertedLength: 0,
            BeforeSize: null, AfterSize: null, BeforeSha256: null, AfterSha256: null,
            BeforeHex: null, AfterHex: null, DeclaredValue: null);
        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with { Mutations = [mutation] };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(testCase),
            error => error.Contains("lengths must not be negative", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MalformedSchemaVersionOrHash_IsRejected()
    {
        var badSchemaVersion = ArchiveTestJsonTests.ValidEmptyCase() with { SchemaVersion = 2 };
        var badHash = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Archive = new ArchiveTestArchive(
                ArchiveTestJsonTests.ValidEmptyCase().FixtureId + ".zip", 22, "not-a-hash"),
        };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(badSchemaVersion),
            error => error.Contains("schemaVersion must be 1", StringComparison.Ordinal));
        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(badHash),
            error => error.Contains("archive.sha256 must be 64 lowercase hex", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptyExpectations_IsRejected()
    {
        var emptyOutcomes = ArchiveTestJsonTests.ValidEmptyCase() with
        {
            Expectations =
            [
                new ArchiveTestExpectation("list", "strict", [], [], null, null, null),
            ],
        };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(emptyOutcomes),
            error => error.Contains("allowedOutcomes must not be empty", StringComparison.Ordinal));

        var none = ArchiveTestJsonTests.ValidEmptyCase() with { Expectations = [] };

        Assert.Contains(
            ArchiveTestCaseSemantics.Validate(none),
            error => error.Contains("at least one operation record", StringComparison.Ordinal));
    }
}
