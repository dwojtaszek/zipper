namespace Zipper.ArchiveTests;

internal sealed record ArchiveTestSuiteResult(
    IReadOnlyList<string> FixtureIds,
    string PublishedDirectory);

/// <summary>
/// The only Archive Test module that writes fixture pairs to disk (REQ-209, REQ-214):
/// stage every pair in a newly owned GUID-named sibling directory with exclusive-create
/// files, verify semantic validation before publication, detect duplicate Fixture IDs,
/// enforce suite/sidecar budgets, then rename the staging directory into place. On
/// failure or cancellation only the owned staging directory is removed; the requested
/// destination is never created partially, overwritten, or cleaned up.
/// </summary>
internal static class ArchiveTestSuiteGenerator
{
    private const string GeneratorContractVersion = "1";

    internal static async Task<ArchiveTestSuiteResult> GenerateAsync(
        ArchiveTestRequest request,
        CancellationToken cancellationToken,
        Action<string>? afterStaging = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var parent = request.OutputDirectory.Parent
            ?? throw new ArgumentException($"Output directory '{request.OutputDirectory.FullName}' has no parent directory.");
        Directory.CreateDirectory(parent.FullName);

        var staging = Path.Combine(parent.FullName, $"atc-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            afterStaging?.Invoke(staging);

            var publishedIds = new HashSet<string>(StringComparer.Ordinal);
            var orderedIds = new List<string>();
            long suiteBytes = 0;
            foreach (var caseKey in request.CaseKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = ArchiveTestCatalog.GetCase(caseKey);
                var artifact = ArchiveFixtureBuilder.Build(definition, request.Seed, cancellationToken);
                var fixtureId = ArchiveTestIdentity.ComputeFixtureId(
                    GeneratorContractVersion, definition.CaseKey, definition.CaseRevision,
                    definition.ExpectationRevision, request.Seed, artifact.ArchiveSha256);

                if (!publishedIds.Add(fixtureId))
                {
                    throw new InvalidOperationException($"Duplicate Archive Test Fixture ID '{fixtureId}' for Case Key '{caseKey}'; publication stopped before any overwrite.");
                }

                var testCase = BuildExpectationFile(fixtureId, definition, request.Seed, artifact);
                var jsonBytes = ArchiveTestJson.SerializeToUtf8Bytes(testCase);
                if (jsonBytes.Length > ArchiveTestRequest.MaxSidecarBytes)
                {
                    throw new InvalidOperationException(
                        $"Expectation File for Case Key '{caseKey}' is {jsonBytes.Length} bytes, over the {ArchiveTestRequest.MaxSidecarBytes}-byte sidecar budget.");
                }

                suiteBytes = checked(suiteBytes + artifact.ArchiveBytes.Length + jsonBytes.Length);
                if (suiteBytes > ArchiveTestRequest.MaxSuiteBytes)
                {
                    throw new InvalidOperationException(
                        $"Archive Test suite exceeds the {ArchiveTestRequest.MaxSuiteBytes}-byte folder budget at Case Key '{caseKey}'.");
                }

                await WritePairAsync(staging, fixtureId, artifact.ArchiveBytes, jsonBytes, cancellationToken)
                    .ConfigureAwait(false);
                orderedIds.Add(fixtureId);
            }

            // POSIX rename(2) silently replaces an existing empty destination directory, so an
            // explicit guard is required before the rename: a destination that appeared during
            // publication is a failure, never a merge. A residual create-between-check-and-rename
            // race is inherent to rename semantics; the guard removes the deterministic case.
            var destination = request.OutputDirectory.FullName;
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new IOException(
                    $"Archive Test destination '{destination}' appeared during publication; nothing was overwritten and the staged pairs were discarded.");
            }

            Directory.Move(staging, destination);

            return new ArchiveTestSuiteResult(orderedIds, request.OutputDirectory.FullName);
        }
        catch
        {
            // Cleanup only the owned staging directory, with its own failure handling and a
            // fresh token so a cancelled caller still gets the directory removed.
            TryDeleteDirectory(staging);
            throw;
        }
    }

    private static ArchiveTestCase BuildExpectationFile(
        string fixtureId, ArchiveTestCaseDefinition definition, int seed, ArchiveFixtureArtifact artifact)
    {
        // Layout ordinals and recipe expectations walk in the same recipe order;
        // a misalignment is a generator bug and must fail loudly, not silently drop hashes.
        if (artifact.Layout.Entries.Count != artifact.Entries.Count)
        {
            throw new InvalidOperationException(
                $"Case Key '{definition.CaseKey}': layout has {artifact.Layout.Entries.Count} entries but the recipe produced {artifact.Entries.Count}.");
        }

        var entries = artifact.Layout.Entries
            .Select(layout =>
            {
                var recipe = artifact.Entries[layout.Ordinal];
                var isDirectory = layout.Name.EndsWith('/');
                return new ArchiveTestEntry(
                    Ordinal: layout.Ordinal,
                    Kind: isDirectory ? "directory" : "file",
                    LocalNameRaw: layout.NameHex,
                    CentralNameRaw: layout.NameHex,
                    ReadableName: layout.Name,
                    ContentSha256: !isDirectory ? recipe.ContentSha256 : null,
                    ContentSize: !isDirectory ? recipe.Content.Length : null,
                    LocalHeaderOffset: layout.LocalHeaderOffset,
                    DataOffset: layout.DataOffset,
                    CentralDirectoryOffset: layout.CentralDirectoryOffset);
            })
            .ToList();

        var expandedBytes = artifact.Entries.Sum(entry => (long)entry.Content.Length);
        var testCase = new ArchiveTestCase(
            SchemaVersion: 1,
            GeneratorContractVersion: GeneratorContractVersion,
            GeneratorVersion: typeof(ArchiveTestSuiteGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            FixtureId: fixtureId,
            CaseKey: definition.CaseKey,
            CaseRevision: definition.CaseRevision,
            ExpectationRevision: definition.ExpectationRevision,
            Seed: seed,
            Classification: definition.Classification,
            Archive: new ArchiveTestArchive(fixtureId + ".zip", artifact.ArchiveBytes.Length, artifact.ArchiveSha256),
            Entries: entries,
            Mutations: artifact.Mutations,
            Expectations: BuildExpectations(definition),
            Limits: new ArchiveTestLimits(
                EntryCount: artifact.Layout.EntryCount,
                ExpandedBytesBudget: expandedBytes,
                JsonBytesBudget: (int)Math.Min(ArchiveTestRequest.MaxSidecarBytes, int.MaxValue),
                DeadlineSeconds: ArchiveTestCaseSemantics.MaxDeadlineSeconds));

        var errors = ArchiveTestCaseSemantics.Validate(testCase);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expectation File for Case Key '{definition.CaseKey}' failed semantic validation: {string.Join("; ", errors)}");
        }

        return testCase;
    }

    /// <summary>
    /// Capability-specific expectations (REQ-212): valid controls must fully pass;
    /// CRC-defect fixtures allow both detected and undetected outcomes under the strict
    /// integrity profile (some readers do not check CRC) while the payload bytes must
    /// stay unchanged; truncation fixtures may fail at any defined stage.
    /// </summary>
    private const string StrictProfile = "strict";
    private const string ListOperation = "list";
    private const string ReadEntryOperation = "read-entry";
    private const string IntegrityCheckOperation = "integrity-check";
    private const string ExtractOperation = "extract";
    private const string NoPartialWrites = "no-partial-writes";
    private const string PayloadBytesUnchanged = "payload-bytes-unchanged";
    private const string OperationFails = "operation-fails";
    private const string ListSucceeds = "list-succeeds";
    private const string ListFails = "list-fails";

    private static IReadOnlyList<ArchiveTestExpectation> BuildExpectations(ArchiveTestCaseDefinition definition)
    {
        if (!definition.IsMutation)
        {
            return
            [
                Expectation(ListOperation, StrictProfile, ["listed-count-matches-entries"], [NoPartialWrites, "listed-count == entry-count"]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches"], [NoPartialWrites]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes"], [NoPartialWrites]),
                Expectation(ExtractOperation, StrictProfile, ["extract-completes"], [NoPartialWrites, "extracted-bytes-match-content-hashes"]),
            ];
        }

        return definition.Mutation switch
        {
            ArchiveTestMutationKind.CrcLocalMismatch
                or ArchiveTestMutationKind.CrcCentralMismatch
                or ArchiveTestMutationKind.CrcBothMismatch =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, "strict-integrity-v1", ["crc-mismatch-rejected", "crc-mismatch-unchecked"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation], capability: "crc32"),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Local-header lies with an intact central directory: lenient central-based
            // readers succeed; strict local-header validators reject (ticket #840).
            ArchiveTestMutationKind.NameLocalCentralMismatch
                or ArchiveTestMutationKind.MethodLocalCentralMismatch
                or ArchiveTestMutationKind.SizeLocalCentralMismatch
                or ArchiveTestMutationKind.ExtraFieldLengthOverrun =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes", "integrity-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Central-declared offset lies: the physical bytes are unchanged; a lenient
            // reader may still list entries, but reads at the declared offsets fail or
            // return unverified bytes.
            ArchiveTestMutationKind.OffsetOutsideArchive
                or ArchiveTestMutationKind.OffsetIntoPayload
                or ArchiveTestMutationKind.OverlappingEntryRanges =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [NoPartialWrites, "declared-offsets-are-lies"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes"], [NoPartialWrites], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "integrity-unchecked"], [NoPartialWrites], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails"], [NoPartialWrites], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Policy-sensitive: the method code is consistent but unimplemented; the
            // payload bytes stay the stored control bytes (ticket #840).
            ArchiveTestMutationKind.UnsupportedMethod =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds], [PayloadBytesUnchanged], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["unsupported-method-rejected", "unsupported-method-unchecked"], [PayloadBytesUnchanged], ["open", ReadEntryOperation], capability: "method-98-ppmd"),
                Expectation(IntegrityCheckOperation, StrictProfile, ["unsupported-method-rejected", "integrity-unchecked"], [PayloadBytesUnchanged], ["open", IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails", "extract-succeeds"], [PayloadBytesUnchanged], ["open", ExtractOperation]),
            ],
            ArchiveTestMutationKind.EncryptionFlagWithPlaintext =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged], ["open", ListOperation]),
                // Read-entry allows only failure: every mainstream reader honors the
                // encrypted flag and cannot decrypt the plaintext without a password.
                // Unlike the CRC and offset lies, no lenient middle ground exists.
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "integrity-unchecked"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails"], [PayloadBytesUnchanged], ["open", ExtractOperation]),
            ],
            ArchiveTestMutationKind.InvalidUtf8Name =>
            [
                // Both headers declare UTF-8 the raw name bytes do not satisfy: readers
                // may list (raw or lossy-decoded names) or reject; returned content is
                // unverified because the name contract is broken (ticket #841 step 4).
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-unchecked", "integrity-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails"], [PayloadBytesUnchanged], ["open", ExtractOperation]),
            ],
            // Zip64 resolution lies: the sentinels stay but the extended field is gone
            // or claims more than it holds. Claims stay tiny; no allocation may follow
            // them (ticket #841 test plan).
            ArchiveTestMutationKind.Zip64MissingExtra
                or ArchiveTestMutationKind.Zip64TruncatedExtra =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [NoPartialWrites, "declared-sizes-are-lies", "no-allocation-from-declared-sizes"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes"], [NoPartialWrites, "no-allocation-from-declared-sizes"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Tail truncations (ticket #839): every operation fails at a defined stage.
            ArchiveTestMutationKind.TruncatePayloadTail
                or ArchiveTestMutationKind.TruncateCentralTail
                or ArchiveTestMutationKind.TruncateEocd
                or ArchiveTestMutationKind.MissingEocd =>
            [
                Expectation(ListOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            _ => throw new InvalidOperationException(
                $"Archive Test case '{definition.CaseKey}': no expectation set defined for mutation '{definition.Mutation}'."),
        };
    }

    private static ArchiveTestExpectation Expectation(
        string operation,
        string profile,
        string[] allowedOutcomes,
        string[] invariants,
        string[]? failureStages = null,
        string? capability = null) =>
        new(
            Operation: operation,
            Profile: profile,
            AllowedOutcomes: allowedOutcomes,
            Invariants: invariants,
            Platform: null,
            Capability: capability,
            FailureStages: failureStages);

    private static async Task WritePairAsync(
        string staging, string fixtureId, byte[] archiveBytes, byte[] jsonBytes, CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(staging, fixtureId + ".zip");
        var jsonPath = Path.Combine(staging, fixtureId + ".json");

        // Exclusive-create file modes: an unexpected collision fails the run instead of overwriting.
        using (var zip = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await zip.WriteAsync(archiveBytes, cancellationToken).ConfigureAwait(false);
        }

        using (var json = new FileStream(jsonPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await json.WriteAsync(jsonBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort; never mask the original failure.
        }
    }
}
