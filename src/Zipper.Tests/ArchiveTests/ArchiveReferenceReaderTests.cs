using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #845: the .NET reference reader's normalized operation results for every
/// catalog case. This required reader runs beside Python zipfile in
/// tests/archive-tests/verify-fixtures.py and the optional readers in
/// verify-reader-matrix.py: list / read-entry / integrity-check / extract outcomes are
/// normalized (never exception wording) and checked against the Expectation File's
/// allowed outcomes. Valid controls are the required positive controls and are
/// extracted into a fresh owned directory. Other malformed and policy-sensitive
/// fixtures are not extracted here; trailing-data-undeclared is safe because it reuses
/// the valid Stored control's paths. Platform-marked expectations do not apply on this
/// host and are recorded as not-applicable, never as passing verifications (REQ-212).
/// </summary>
public class ArchiveReferenceReaderTests : WorkingDirectoryTestBase
{
    public static readonly IReadOnlyList<string> AllCaseKeyList =
        [.. ArchiveTestCatalog.ListSuite("all").Select(definition => definition.CaseKey)];

    public static TheoryData<string> AllCaseKeys => new(AllCaseKeyList);

    private sealed record ReaderOutcome(string Operation, string NormalizedOutcome, bool Applicable, bool Allowed);

    private const string RuntimeCrcBehaviorChangedMessage =
        "Runtime CRC behavior changed — bump Expectation Revisions for all CRC-declaring cases and update NormalizeIntegrityCheck per docs/archive-test-suites.md before updating this canary.";

    private static readonly HashSet<string> ApplicablePlatforms = GetApplicablePlatforms();

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
            .Where(e => e.Operation == operation
                        && (e.Platform is null || ApplicablePlatforms.Contains(e.Platform)))
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
                    when ((ex.Message.Contains("unsupported compression method", StringComparison.OrdinalIgnoreCase)
                           || ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase))
                          && entry.LocalHeaderMethod is not (0 or 8))
                {
                    // A codec the reader cannot implement is rejected outright.
                    // Gated on the entry's wire method so a corrupt stored/deflate
                    // stream can never be misreported as an unsupported codec.
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
        catch (System.IO.InvalidDataException ex)
            when (ex.Message.Contains("unsupported compression method", StringComparison.OrdinalIgnoreCase)
                  || ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase))
        {
            // A codec the reader cannot implement is rejected outright, never
            // reported as a generic extraction failure.
            return "unsupported-method-rejected";
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
        var expectationFileBytes = await File.ReadAllBytesAsync(jsonPath);
        var testCase = ArchiveTestJson.Parse(expectationFileBytes);
        var archiveBytes = await File.ReadAllBytesAsync(zipPath);

        Assert.Equal(caseKey, testCase.CaseKey);

        var extractDir = Path.Combine(TempDir, caseKey + "-extract");
        var outcomes = ExecuteReaderOperations(testCase, archiveBytes, extractDir);
        WriteReaderProfileReport(testCase, outcomes, expectationFileBytes, archiveBytes);
    }

    [Fact]
    public void ReadOperations_PlatformInapplicableExtractAdapterCheck_StillRuns()
    {
        var definition = ArchiveTestCatalog.GetCase("valid-stored");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);
        var platformScopedCase = testCase with
        {
            Expectations = [.. testCase.Expectations.Select(
                expectation => expectation with { Platform = "adls-gen2" })],
            AdapterChecks =
            [
                new ArchiveTestAdapterCheck(
                    "dotnet-reference", "extract", "extract-succeeds"),
            ],
        };

        var outcomes = ExecuteReaderOperations(
            platformScopedCase, artifact.ArchiveBytes, Path.Combine(TempDir, "adapter-extract"));

        var extract = Assert.Single(outcomes, outcome => outcome.Operation == "extract");
        Assert.False(extract.Applicable);
        Assert.Equal("extract-succeeds", extract.NormalizedOutcome);

        var reportDirectory = Path.Combine(TempDir, "dotnet-report");
        Directory.CreateDirectory(reportDirectory);
        var reportPath = Path.Combine(
            reportDirectory, "dotnet-reference-" + platformScopedCase.FixtureId + ".json");
        File.WriteAllText(reportPath, "stale report");
        var oldReportDirectory = Environment.GetEnvironmentVariable("ZIPPER_ATC_DOTNET_REPORT_DIR");
        try
        {
            Environment.SetEnvironmentVariable("ZIPPER_ATC_DOTNET_REPORT_DIR", reportDirectory);
            WriteReaderProfileReport(
                platformScopedCase,
                outcomes,
                ArchiveTestJson.SerializeToUtf8Bytes(platformScopedCase),
                artifact.ArchiveBytes);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZIPPER_ATC_DOTNET_REPORT_DIR", oldReportDirectory);
        }

        using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        var extractOperation = Assert.Single(
            report.RootElement.GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("operation").GetString() == "extract");
        Assert.Equal("not-run", extractOperation.GetProperty("normalizedOutcome").GetString());
        Assert.Equal(
            "extract-succeeds",
            report.RootElement.GetProperty("adapterCheckOutcomes")
                .GetProperty("extract").GetString());
    }

    [Fact]
    public void ReadOperations_CrcBothMismatch_PinsRuntimeCrcValidationBehavior()
    {
        var definition = ArchiveTestCatalog.GetCase("crc-both-mismatch");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        var exception = Record.Exception(() =>
        {
            using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
            using var stream = archive.Entries[0].Open();
            stream.CopyTo(Stream.Null);
        });

        if (Environment.Version.Major >= 11)
        {
            Assert.True(
                exception is InvalidDataException crcException
                    && crcException.Message.Contains("crc", StringComparison.OrdinalIgnoreCase),
                RuntimeCrcBehaviorChangedMessage);
            Assert.Fail(RuntimeCrcBehaviorChangedMessage);
        }

        Assert.True(exception is null, RuntimeCrcBehaviorChangedMessage);
        Assert.True(
            NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase) == "crc-mismatch-unchecked",
            RuntimeCrcBehaviorChangedMessage);
    }

    [Fact]
    public void ReadOperations_Zip64ExtraWithoutSentinels_UsesHeaderSizes()
    {
        var definition = ArchiveTestCatalog.GetCase("zip64-extra-without-sentinels");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        // Observed .NET behavior for #1279: it uses the 32-bit sizes and ignores
        // the still-present Zip64 size values when no 32-bit field is a sentinel.
        Assert.Equal("list-succeeds", NormalizeList(artifact.ArchiveBytes, testCase));
        Assert.Equal("read-entry-content-matches", NormalizeReadEntry(artifact.ArchiveBytes, testCase));
        Assert.Equal("integrity-passes", NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase));
    }

    [Fact]
    public void ReadOperations_DeclaredSizeUndersized_ReturnsUnverifiedBytesAtDeclaredCompressedSizeBoundary()
    {
        var definition = ArchiveTestCatalog.GetCase("declared-size-undersized");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        using (var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read))
        using (var stream = Assert.Single(archive.Entries).Open())
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            var returnedContent = copy.ToArray();
            Assert.InRange(returnedContent.Length, 1, artifact.Entries[0].Content.Length - 1);
            Assert.Equal(
                artifact.Entries[0].Content.AsSpan(0, returnedContent.Length).ToArray(),
                returnedContent);
        }

        Assert.Equal("list-succeeds", NormalizeList(artifact.ArchiveBytes, testCase));
        Assert.Equal("read-entry-returns-unverified-bytes", NormalizeReadEntry(artifact.ArchiveBytes, testCase));
        Assert.Equal("integrity-fails", NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase));
    }

    [Fact]
    public void ReadOperations_Zip64LocatorAmbiguity_UsesLengthAnchoredDirectory()
    {
        var definition = ArchiveTestCatalog.GetCase("zip64-locator-ambiguity");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        Assert.Equal("list-succeeds", NormalizeList(artifact.ArchiveBytes, testCase));
        Assert.Equal("read-entry-content-matches", NormalizeReadEntry(artifact.ArchiveBytes, testCase));
        Assert.Equal("integrity-passes", NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase));
    }

    [Fact]
    public void ReadOperations_Zip64EocdOffsetDisagreement_PinsDotNetReaderOutcomes()
    {
        var definition = ArchiveTestCatalog.GetCase("zip64-eocd-offset-disagreement");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        Assert.Equal("list-succeeds", NormalizeList(artifact.ArchiveBytes, testCase));
        Assert.Equal("read-entry-content-matches", NormalizeReadEntry(artifact.ArchiveBytes, testCase));
        Assert.Equal("integrity-passes", NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase));
    }

    [Fact]
    public void ReadOperations_UndeclaredTrailingData_PinsDotNetReaderOutcomes()
    {
        var definition = ArchiveTestCatalog.GetCase("trailing-data-undeclared");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        Assert.Equal("list-succeeds", NormalizeList(artifact.ArchiveBytes, testCase));
        Assert.Equal("read-entry-content-matches", NormalizeReadEntry(artifact.ArchiveBytes, testCase));
        Assert.Equal("integrity-passes", NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase));

        var extractDirectory = Path.Combine(TempDir, "trailing-data-extract");
        Directory.CreateDirectory(extractDirectory);
        Assert.Equal("extract-succeeds", NormalizeExtract(artifact.ArchiveBytes, testCase, extractDirectory));
    }

    [Fact]
    public void ReadOperations_NameSiblingPrefixTraversal_PreservesNamesAndPayloads()
    {
        var definition = ArchiveTestCatalog.GetCase("name-sibling-prefix-traversal");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        using (var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read))
        {
            Assert.Equal(
                testCase.Entries.Select(entry => entry.ReadableName),
                archive.Entries.Select(entry => entry.FullName));
        }

        Assert.Equal("list-succeeds", NormalizeList(artifact.ArchiveBytes, testCase));
        Assert.Equal("read-entry-content-matches", NormalizeReadEntry(artifact.ArchiveBytes, testCase));
        Assert.Equal("integrity-passes", NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase));
    }

    [Fact]
    public void ReadOperations_ExtraFieldTruncated_PinsDotNetReaderOutcomes()
    {
        var definition = ArchiveTestCatalog.GetCase("extra-field-truncated");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var testCase = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);

        Assert.Equal("list-fails", NormalizeList(artifact.ArchiveBytes, testCase));
        Assert.Equal("read-entry-fails", NormalizeReadEntry(artifact.ArchiveBytes, testCase));
        Assert.Equal("integrity-fails", NormalizeIntegrityCheck(artifact.ArchiveBytes, testCase));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("read-entry")]
    [InlineData("integrity-check")]
    [InlineData("extract")]
    public async Task ReadOperations_MissingRequiredOperation_FailsContractBeforeReaderExecution(string omittedOperation)
    {
        var destination = Path.Combine(TempDir, "omitted-" + omittedOperation);
        await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(["valid-stored"], seed: 42, outputPath: destination),
            CancellationToken.None);

        var jsonPath = Directory.GetFiles(destination, "*.json").Single();
        var zipPath = Path.ChangeExtension(jsonPath, ".zip");
        var testCase = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));
        var archiveBytes = await File.ReadAllBytesAsync(zipPath);

        var mutilatedCase = testCase with
        {
            Expectations = [.. testCase.Expectations.Where(e => e.Operation != omittedOperation)],
        };

        var extractDir = Path.Combine(TempDir, "extract-" + omittedOperation);
        Assert.Throws<Xunit.Sdk.EqualException>(() =>
            ExecuteReaderOperations(mutilatedCase, archiveBytes, extractDir));

        Assert.False(Directory.Exists(extractDir), "Reader execution must not proceed after contract failure");
    }

    private static readonly string[] RequiredOperations =
    [
        "extract",
        "integrity-check",
        "list",
        "read-entry",
    ];

    private static void AssertDeclaredOperations(ArchiveTestCase testCase)
    {
        var declaredOperations = testCase.Expectations
            .Select(e => e.Operation)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            RequiredOperations.Order(StringComparer.Ordinal),
            declaredOperations.Order(StringComparer.Ordinal));
    }

    private IReadOnlyList<ReaderOutcome> ExecuteReaderOperations(
        ArchiveTestCase testCase, byte[] archiveBytes, string extractDir)
    {
        AssertDeclaredOperations(testCase);

        var outcomes = new List<ReaderOutcome>();
        var expectedOperations = testCase.Expectations
            .Where(e => e.Platform is null || ApplicablePlatforms.Contains(e.Platform))
            .Select(e => e.Operation)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);
        var adapterCheckActions = (testCase.AdapterChecks ?? [])
            .Where(check => check.Adapter == "dotnet-reference")
            .Select(check => check.Action)
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

        var extractRequired = testCase.Classification == "valid"
            && (expectedOperations.Contains("extract") || adapterCheckActions.Contains("extract"));
        if (extractRequired)
        {
            // Required positive control: real extraction into a fresh owned directory.
            Directory.CreateDirectory(extractDir);
            var extract = NormalizeExtract(archiveBytes, testCase, extractDir);
            var allowed = OutcomeAllowed(extract, "extract", testCase);
            outcomes.Add(new ReaderOutcome("extract", extract,
                Applicable: allowed is not null, Allowed: allowed is true));
        }

        // Every expected-and-applicable operation must be allowed for this reader.
        foreach (var outcome in outcomes)
        {
            Assert.True(!outcome.Applicable || outcome.Allowed,
                $"case '{testCase.CaseKey}': .NET reader outcome '{outcome.NormalizedOutcome}' for " +
                $"'{outcome.Operation}' is outside the allowed outcomes");
        }

        // Not-run is explicit: apart from extraction (deliberately not exercised for
        // malformed and policy-sensitive fixtures), every unmarked expectation must
        // have a recorded outcome — a silent skip cannot satisfy a required gate.
        var mustRun = expectedOperations
            .Where(op => op != "extract" || testCase.Classification == "valid")
            .ToHashSet(StringComparer.Ordinal);
        var ran = outcomes.Select(o => o.Operation).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(ran, mustRun);
        return outcomes;
    }

    private static HashSet<string> GetApplicablePlatforms()
    {
        var platforms = new HashSet<string>(StringComparer.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            platforms.Add("windows");
        }

        if (OperatingSystem.IsLinux())
        {
            platforms.Add("linux");
        }

        foreach (var platform in (Environment.GetEnvironmentVariable("ZIPPER_ATC_PLATFORMS") ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            platforms.Add(platform);
        }

        return platforms;
    }

    private static void WriteReaderProfileReport(
        ArchiveTestCase testCase,
        IReadOnlyList<ReaderOutcome> outcomes,
        byte[] expectationFileBytes,
        byte[] archiveBytes)
    {
        var reportDirectory = Environment.GetEnvironmentVariable("ZIPPER_ATC_DOTNET_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(reportDirectory))
        {
            return;
        }

        Directory.CreateDirectory(reportDirectory);
        var operations = outcomes
            .Select(outcome => new
            {
                operation = outcome.Operation,
                normalizedOutcome = outcome.Applicable ? outcome.NormalizedOutcome : "not-run",
                status = !outcome.Applicable ? "not-run" : outcome.Allowed ? "pass" : "fail",
            })
            .ToList();
        foreach (var operation in RequiredOperations.Except(
                     operations.Select(result => result.operation), StringComparer.Ordinal))
        {
            operations.Add(new
            {
                operation,
                normalizedOutcome = "not-run",
                status = "not-run",
            });
        }

        var adapterCheckOutcomes = (testCase.AdapterChecks ?? [])
            .Where(check => check.Adapter == "dotnet-reference")
            .Select(check => check.Action)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                action => action,
                action => outcomes.Single(outcome => outcome.Operation == action).NormalizedOutcome,
                StringComparer.Ordinal);
        var skipped = !testCase.Expectations.Any(expectation =>
            expectation.Platform is null || ApplicablePlatforms.Contains(expectation.Platform));
        var report = new
        {
            fixtureId = testCase.FixtureId,
            caseKey = testCase.CaseKey,
            expectationFileSha256 = Convert.ToHexStringLower(SHA256.HashData(expectationFileBytes)),
            archiveSha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes)),
            adapter = "dotnet-reference",
            adapterVersion = Environment.Version.ToString(),
            status = skipped ? "skipped" : "pass",
            skipReason = skipped
                ? "no operation expectations apply on this host"
                : null,
            adapterCheckOutcomes,
            operations,
        };
        var reportPath = Path.Combine(
            reportDirectory, "dotnet-reference-" + testCase.FixtureId + ".json");
        using var reportStream = new FileStream(
            reportPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(reportStream);
        writer.Write(JsonSerializer.Serialize(report));
        writer.Write('\n');
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
