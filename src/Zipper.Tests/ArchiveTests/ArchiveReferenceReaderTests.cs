using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #845: the .NET reference reader's normalized operation results for every
/// catalog case. The .NET ZipArchive is the second reader beside the Python adapter in
/// tests/archive-tests/verify-fixtures.py: list / read-entry / integrity-check /
/// extract outcomes are normalized (never exception wording) and checked against the
/// Expectation File's allowed outcomes, mirroring the independent verifier. Valid
/// controls are the required positive controls and are extracted into a fresh owned
/// directory; policy-sensitive and malformed fixtures are never extracted here.
/// Platform-marked expectations do not apply on this host and are recorded as
/// not-applicable, never as passing verifications (REQ-212).
/// </summary>
public class ArchiveReferenceReaderTests : TempDirectoryTestBase
{
    public static readonly IReadOnlyList<string> AllCaseKeyList =
        [.. ArchiveTestCatalog.ListSuite("all").Select(definition => definition.CaseKey)];

    public static TheoryData<string> AllCaseKeys => new(AllCaseKeyList);

    private sealed record ReaderOutcome(string Operation, string NormalizedOutcome, bool Applicable, bool Allowed);

    /// <summary>
    /// Catalog success vocabulary aliases of the reader's canonical success tokens
    /// (e.g. the verifier emits 'list-succeeds' where the catalog records
    /// 'listed-count-matches-entries' or 'empty-list').
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> OutcomeAliases =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["list-succeeds"] = ["list-succeeds", "listed-count-matches-entries", "empty-list"],
            ["extract-succeeds"] = ["extract-succeeds", "extract-completes", "extract-completes-empty"],
            ["list-fails"] = ["list-fails", "operation-fails"],
            ["read-entry-fails"] = ["read-entry-fails", "operation-fails"],
            ["integrity-fails"] = ["integrity-fails", "operation-fails"],
            ["extract-fails"] = ["extract-fails", "operation-fails"],
        };

    /// <summary>
    /// True when the observed outcome is within the allowed outcomes of the
    /// operation's unmarked expectations; null when no unmarked expectation exists
    /// for the operation (platform-specific: not applicable on this host).
    /// </summary>
    private static bool? OutcomeAllowed(string observed, string operation, ArchiveTestCase testCase)
    {
        var expectations = testCase.Expectations
            .Where(e => e.Operation == operation && e.Platform is null)
            .ToList();
        if (expectations.Count == 0)
        {
            return null;
        }

        var allowed = expectations.SelectMany(e => e.AllowedOutcomes).ToHashSet(StringComparer.Ordinal);
        var candidates = OutcomeAliases.TryGetValue(observed, out var aliases)
            ? aliases
            : [observed];
        return candidates.Any(candidate => allowed.Contains(candidate));
    }

    /// <summary>
    /// Normalizes the .NET reader's behavior for one operation. .NET's ZipArchive does
    /// not verify CRC-32 while streaming, so CRC lies stay undetected on this reader
    /// ('crc-mismatch-unchecked' against a declared CRC mutation); entries are read
    /// by ordinal, never through name-keyed lookups, so duplicate names stay
    /// distinguishable.
    /// </summary>
    private static string NormalizeList(byte[] archiveBytes, ArchiveTestCase testCase)
    {
        // 'listed-count-matches-entries' is a claim, so the count is verified:
        // listing succeeds only when the reader's count equals the declared one.
        try
        {
            using var archive = new ZipArchive(
                new MemoryStream(archiveBytes), ZipArchiveMode.Read);
            return archive.Entries.Count == testCase.Limits.EntryCount
                ? "list-succeeds"
                : "list-fails";
        }
        catch (Exception)
        {
            return "list-fails";
        }
    }

    private static string NormalizeReadEntry(byte[] archiveBytes, ArchiveTestCase testCase)
    {
        try
        {
            // The entry objects are only usable while the archive is open, so the
            // whole read pass lives inside the using. Reads are positional: the
            // reader invariant relied on here is that Entries enumerates in central
            // directory order, which is the recipe order the ordinals encode.
            using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
            var entries = archive.Entries;

            foreach (var entry in testCase.Entries)
            {
                if (entry.Kind != "file" || entry.ContentSha256 is null)
                {
                    continue;
                }

                if (entry.Ordinal >= entries.Count)
                {
                    return "read-entry-fails";
                }

                try
                {
                    // Open() itself rejects codecs the reader cannot implement
                    // (InvalidDataException with a specific unsupported-method
                    // wording), so it must sit inside this try, not just the copy.
                    using var stream = entries[entry.Ordinal].Open();
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    var sha = Convert.ToHexStringLower(SHA256.HashData(copy.ToArray()));
                    if (sha != entry.ContentSha256)
                    {
                        return "read-entry-returns-unverified-bytes";
                    }
                }
                catch (System.IO.InvalidDataException ex)
                    when (ex.Message.Contains("unsupported compression method", StringComparison.Ordinal))
                {
                    // A codec the reader cannot implement is rejected outright.
                    return "unsupported-method-rejected";
                }
            }
        }
        catch (Exception)
        {
            return "read-entry-fails";
        }

        // A broken name contract (invalid UTF-8 name bytes under a UTF-8 flag) means
        // the reader serves the payload under a lossy-decoded name, and a plaintext
        // entry wearing the encrypted flag is served without any decryption: in both
        // cases the (name, bytes) binding the reader offers is unverified.
        if (DeclaresNameContractMutation(testCase) || DeclaresEncryptionFlagMutation(testCase))
        {
            return "read-entry-returns-unverified-bytes";
        }

        return "read-entry-content-matches";
    }

    private static string NormalizeIntegrityCheck(byte[] archiveBytes, ArchiveTestCase testCase)
    {
        // .NET streams without CRC verification: against a declared CRC mutation the
        // honest normalized outcome is that the lie went undetected, not a clean pass.
        var read = NormalizeReadEntry(archiveBytes, testCase);
        return read switch
        {
            "read-entry-content-matches" when DeclaresCrcMutation(testCase) => "crc-mismatch-unchecked",
            "read-entry-content-matches" => "integrity-passes",
            "read-entry-returns-unverified-bytes" when DeclaresEncryptionFlagMutation(testCase) => "integrity-unchecked",
            "read-entry-returns-unverified-bytes" => "integrity-fails",
            "unsupported-method-rejected" => "unsupported-method-rejected",
            _ => "integrity-fails",
        };
    }

    private static bool DeclaresEncryptionFlagMutation(ArchiveTestCase testCase) =>
        testCase.Mutations.Any(m => m.Code.Contains("encryption", StringComparison.Ordinal));

    private static bool DeclaresNameContractMutation(ArchiveTestCase testCase) =>
        testCase.Mutations.Any(m => m.Code.Contains("invalid-utf8-name", StringComparison.Ordinal));

    private static bool DeclaresCrcMutation(ArchiveTestCase testCase) =>
        testCase.Mutations.Any(m => m.Code.Contains("crc", StringComparison.Ordinal));

    private static string NormalizeExtract(byte[] archiveBytes, ArchiveTestCase testCase, string destination)
    {
        try
        {
            using var archive = new ZipArchive(
                new MemoryStream(archiveBytes), ZipArchiveMode.Read);
            archive.ExtractToDirectory(destination);
        }
        catch (Exception)
        {
            return "extract-fails";
        }

        var fileEntries = testCase.Entries.Where(e => e.Kind == "file").ToList();
        var found = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
        if (found.Length != fileEntries.Count)
        {
            return "extract-fails";
        }

        // Containment invariant: every extracted path stays under the owned root
        // (with a trailing separator so a sibling like 'root-evil' cannot match).
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        if (found.Any(path => !Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal)))
        {
            return "extract-fails";
        }

        // Content fidelity as a multiset of payload hashes: readers decode legacy
        // (cp437) name bytes by their own policy, so names may render differently
        // per reader while the extracted byte set must stay exactly the payloads.
        // Entries without a known content hash contribute a placeholder so the two
        // multisets stay aligned for fixtures with unknowable content.
        string HashFor(ArchiveTestEntry entry) =>
            entry.ContentSha256 ?? "unknown-" + entry.Ordinal;
        var expectedHashes = fileEntries.Select(HashFor).Order(StringComparer.Ordinal).ToList();
        var actualHashes = found
            .Select(File.ReadAllBytes)
            .Select(bytes => Convert.ToHexStringLower(SHA256.HashData(bytes)))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (!expectedHashes.SequenceEqual(actualHashes))
        {
            return "extract-fails";
        }

        return "extract-succeeds";
    }

    [Theory]
    [MemberData(nameof(AllCaseKeys))]
    public async Task ReadOperations_AllCases_OutcomesMatchExpectationFile(string caseKey)
    {
        // Publish the real pair through the generator, then read the bytes back from
        // disk — the same contract the independent Python verifier consumes. Unlike
        // the verifier, this test's input is its own in-process generator output, so
        // the classification field is trusted here; the Python verifier derives
        // extraction safety independently instead.
        var destination = Path.Combine(TempDir, caseKey);
        await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create([caseKey], seed: 42, outputPath: destination),
            CancellationToken.None);

        var jsonPath = Directory.GetFiles(destination, "*.json").Single();
        var zipPath = Path.ChangeExtension(jsonPath, ".zip");
        var testCase = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));
        var archiveBytes = await File.ReadAllBytesAsync(zipPath);

        Assert.Equal(caseKey, testCase.CaseKey);

        var outcomes = new List<ReaderOutcome>();
        var expectedOperations = testCase.Expectations
            .Where(e => e.Platform is null)
            .Select(e => e.Operation)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var operation in new[] { "list", "read-entry", "integrity-check" })
        {
            var observed = operation switch
            {
                "list" => NormalizeList(archiveBytes, testCase),
                "read-entry" => NormalizeReadEntry(archiveBytes, testCase),
                _ => NormalizeIntegrityCheck(archiveBytes, testCase),
            };
            var allowed = OutcomeAllowed(observed, operation, testCase);
            outcomes.Add(new ReaderOutcome(operation, observed,
                Applicable: allowed is not null, Allowed: allowed is true));
        }

        var extractExpected = testCase.Classification == "valid" && expectedOperations.Contains("extract");
        if (extractExpected)
        {
            // Required positive control: real extraction into a fresh owned directory.
            var extractDir = Path.Combine(TempDir, caseKey + "-extract");
            Directory.CreateDirectory(extractDir);
            var extract = NormalizeExtract(archiveBytes, testCase, extractDir);
            outcomes.Add(new ReaderOutcome("extract", extract,
                Applicable: true, Allowed: OutcomeAllowed(extract, "extract", testCase) is true));
        }

        // Every expected-and-applicable operation must be allowed for this reader.
        foreach (var outcome in outcomes)
        {
            Assert.True(!outcome.Applicable || outcome.Allowed,
                $"case '{caseKey}': .NET reader outcome '{outcome.NormalizedOutcome}' for " +
                $"'{outcome.Operation}' is outside the allowed outcomes");
        }

        // Not-run is explicit: apart from extraction (deliberately not exercised for
        // malformed and policy-sensitive fixtures), every unmarked expectation must
        // have a recorded outcome — a silent skip cannot satisfy a required gate.
        var mustRun = expectedOperations
            .Where(op => op != "extract" || testCase.Classification == "valid")
            .ToList();
        var ran = outcomes.Select(o => o.Operation).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(ran, mustRun.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadOperations_ValidControls_ExtractMatchesEntireTree()
    {
        var destination = Path.Combine(TempDir, "tree");
        await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(["valid-empty", "valid-stored", "valid-deflate"],
                seed: 42, outputPath: destination),
            CancellationToken.None);

        foreach (var jsonPath in Directory.GetFiles(destination, "*.json"))
        {
            var testCase = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));
            if (testCase.CaseKey is not ("valid-stored" or "valid-deflate"))
            {
                continue;
            }

            var extractDir = Path.Combine(TempDir, testCase.CaseKey + "-tree");
            Directory.CreateDirectory(extractDir);
            var outcome = NormalizeExtract(
                await File.ReadAllBytesAsync(Path.ChangeExtension(jsonPath, ".zip")),
                testCase, extractDir);

            Assert.Equal("extract-succeeds", outcome);
            // The extracted tree is exactly the file entries — no extras, no misses.
            Assert.Equal(testCase.Entries.Count(e => e.Kind == "file"),
                Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).Length);
        }
    }
}
