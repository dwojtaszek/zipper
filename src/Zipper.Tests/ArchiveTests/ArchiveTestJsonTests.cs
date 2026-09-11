using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveTestJsonTests
{
    internal static ArchiveTestCase ValidEmptyCase() => new(
        SchemaVersion: 1,
        GeneratorContractVersion: "1",
        GeneratorVersion: "0.0.0",
        FixtureId: "atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950",
        CaseKey: "valid-empty",
        CaseRevision: 1,
        ExpectationRevision: 1,
        Seed: 42,
        Classification: "valid",
        Archive: new ArchiveTestArchive(
            FileName: "atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950.zip",
            PhysicalSize: 22,
            Sha256: "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85"),
        Entries: [],
        Mutations: [],
        Expectations:
        [
            new ArchiveTestExpectation(
                Operation: "list",
                Profile: "strict",
                AllowedOutcomes: ["empty-list"],
                Invariants: ["listed-count == 0"],
                Platform: null,
                Capability: null,
                FailureStages: null),
            new ArchiveTestExpectation(
                Operation: "extract",
                Profile: "strict",
                AllowedOutcomes: ["extract-completes-empty"],
                Invariants: ["no-files-created", "no-partial-writes"],
                Platform: null,
                Capability: null,
                FailureStages: null),
        ],
        Limits: new ArchiveTestLimits(EntryCount: 0, ExpandedBytesBudget: 0, JsonBytesBudget: 1048576, DeadlineSeconds: 10));

    [Fact]
    public void Serialize_ValidCase_RoundTripsThroughBytes()
    {
        var testCase = ValidEmptyCase();

        var parsed = ArchiveTestJson.Parse(ArchiveTestJson.SerializeToUtf8Bytes(testCase));

        // Records hold reference-typed lists, so byte equality is the round-trip proof:
        // re-serializing the parsed record must reproduce the exact original bytes.
        Assert.Equal(ArchiveTestJson.SerializeToUtf8Bytes(testCase), ArchiveTestJson.SerializeToUtf8Bytes(parsed));
    }

    [Fact]
    public void Serialize_RepeatedCalls_ProduceIdenticalBytes()
    {
        var first = ArchiveTestJson.SerializeToUtf8Bytes(ValidEmptyCase());
        var second = ArchiveTestJson.SerializeToUtf8Bytes(ValidEmptyCase());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialize_OptionalNullMembers_AreOmittedNotEmitted()
    {
        // The authoritative draft-07 schema declares optional members (platform,
        // capability, ordinal, declaredValue, ...) as string/integer: a JSON null
        // would fail schema validation. Found by the ticket #845 Ajv verifier;
        // this guards the WhenWritingNull policy against regression.
        var bytes = ArchiveTestJson.SerializeToUtf8Bytes(ValidEmptyCase());
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("null", text, StringComparison.Ordinal);
        Assert.DoesNotContain("platform", text, StringComparison.Ordinal);
        Assert.DoesNotContain("capability", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_AcrossCultures_ProduceIdenticalBytes()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        }
        catch (CultureNotFoundException)
        {
            // Globalization-invariant runtime: make the skipped culture switch visible.
            Assert.Equal(string.Empty, CultureInfo.CurrentCulture.Name);
        }

        try
        {
            var bytes = ArchiveTestJson.SerializeToUtf8Bytes(ValidEmptyCase());

            Assert.Equal(ArchiveTestJson.SerializeToUtf8Bytes(ValidEmptyCase()), bytes);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Serialize_DocumentEndsWithSingleLineFeedWithoutBom()
    {
        var bytes = ArchiveTestJson.SerializeToUtf8Bytes(ValidEmptyCase());

        Assert.NotEqual(0xEF, bytes[0]); // no UTF-8 BOM
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\n', bytes[^2]);
    }

    [Fact]
    public void Parse_PublishedValidEmptyFixture_MatchesRecord()
    {
        // Real bytes from the frozen fixture pair (ticket 01).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var fixturePath = Path.Combine(dir!.FullName, "tests", "fixtures", "archive-tests", "valid-empty.json");

        var parsed = ArchiveTestJson.Parse(File.ReadAllBytes(fixturePath));

        Assert.Equal("atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950", parsed.FixtureId);
        Assert.Equal(parsed.FixtureId + ".zip", parsed.Archive.FileName);

        // The production identity function must reproduce the published pair's Fixture ID
        // from the parsed fields, proving record and identity wiring agree.
        Assert.Equal(
            parsed.FixtureId,
            ArchiveTestIdentity.ComputeFixtureId(
                parsed.GeneratorContractVersion, parsed.CaseKey, parsed.CaseRevision,
                parsed.ExpectationRevision, parsed.Seed, parsed.Archive.Sha256));
    }

    [Fact]
    public void Serialize_DuplicateEntryNamesWithDistinctOrdinals_SurviveRoundTrip()
    {
        var entry = new ArchiveTestEntry(
            Ordinal: 0, Kind: "file", LocalNameRaw: "612e747874", CentralNameRaw: "612e747874",
            ReadableName: "a.txt", ContentSha256: null, ContentSize: null,
            LocalHeaderOffset: null, DataOffset: null, CentralDirectoryOffset: null);
        var duplicateName = entry with { Ordinal = 1 };
        var testCase = ValidEmptyCase() with
        {
            Entries = [entry, duplicateName],
            Limits = new ArchiveTestLimits(2, 0, 1048576, 10),
        };

        var parsed = ArchiveTestJson.Parse(ArchiveTestJson.SerializeToUtf8Bytes(testCase));

        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal("a.txt", parsed.Entries[0].ReadableName);
        Assert.Equal("a.txt", parsed.Entries[1].ReadableName);
        Assert.Equal(new[] { 0, 1 }, parsed.Entries.Select(e => e.Ordinal).ToArray());
    }

    [Fact]
    public void Parse_TerminalNewlinePolicy_IsEnforced()
    {
        var bytes = ArchiveTestJson.SerializeToUtf8Bytes(ValidEmptyCase());

        // Missing terminal LF, doubled terminal LF, and a leading LF are all rejected.
        Assert.ThrowsAny<JsonException>(() => ArchiveTestJson.Parse(bytes[..^1]));
        Assert.ThrowsAny<JsonException>(() => ArchiveTestJson.Parse([.. bytes, (byte)'\n']));
        Assert.ThrowsAny<JsonException>(() => ArchiveTestJson.Parse([(byte)'\n', .. bytes]));

        // Exactly one terminal LF parses.
        Assert.NotNull(ArchiveTestJson.Parse(bytes));
    }

    [Fact]
    public void Parse_UnknownEnumValue_IsRejectedBySemantics()
    {
        // Raw string literals strip the final newline, so append the terminal LF explicitly.
        var json = Encoding.UTF8.GetBytes(
            """{"schemaVersion":1,"generatorContractVersion":"1","generatorVersion":"0","fixtureId":"atc-0000000000000000000000000000000000000000000000000000000000000000","caseKey":"valid-empty","caseRevision":1,"expectationRevision":1,"seed":42,"classification":"mystery","archive":{"fileName":"x.zip","physicalSize":0,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"},"entries":[],"mutations":[],"expectations":[{"operation":"scan","profile":"strict","allowedOutcomes":["ok"],"invariants":[]}],"limits":{"entryCount":0,"expandedBytesBudget":0,"jsonBytesBudget":1,"deadlineSeconds":1}}"""
            + "\n");

        var parsed = ArchiveTestJson.Parse(json);
        var errors = ArchiveTestCaseSemantics.Validate(parsed);

        Assert.Contains(errors, error => error.Contains("unknown classification 'mystery'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unknown operation 'scan'", StringComparison.Ordinal));
    }
}
