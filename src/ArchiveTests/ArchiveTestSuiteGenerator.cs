using System.Buffers.Binary;

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

    internal static ArchiveTestCase BuildExpectationFile(
        ArchiveFixtureArtifact artifact, ArchiveTestCaseDefinition definition, int seed = 42)
    {
        var fixtureId = ArchiveTestIdentity.ComputeFixtureId(
            GeneratorContractVersion, definition.CaseKey, definition.CaseRevision,
            definition.ExpectationRevision, seed, artifact.ArchiveSha256);
        return BuildExpectationFile(fixtureId, definition, seed, artifact);
    }

    internal static ArchiveTestCase BuildExpectationFile(
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
                var isDirectory = recipe.IsDirectory;
                var (localMethod, centralMethod) = ResolveMethodCodes(artifact.ArchiveBytes, layout, recipe);
                var payloadCodec = !isDirectory ? recipe.PayloadCodec : null;
                return new ArchiveTestEntry(
                    Ordinal: layout.Ordinal,
                    Kind: isDirectory ? "directory" : "file",
                    LocalNameRaw: layout.NameHex,
                    CentralNameRaw: layout.NameHex,
                    ReadableName: layout.Name,
                    LocalHeaderMethod: localMethod,
                    CentralDirectoryMethod: centralMethod,
                    PayloadCodec: payloadCodec,
                    ContentSha256: !isDirectory ? recipe.ContentSha256 : null,
                    ContentSize: !isDirectory ? recipe.Content.Length : null,
                    NameLengthUtf8Bytes: layout.NameHex.Length / 2,
                    NameLengthUtf16Units: layout.Name.Length,
                    NameLengthScalars: layout.Name.EnumerateRunes().Count(),
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

    private static (int LocalMethod, int CentralMethod) ResolveMethodCodes(
        ReadOnlySpan<byte> archiveBytes, ArchiveFixtureEntryLayout layout, ArchiveFixtureEntryExpectation recipe)
    {
        var localMethod = (int)recipe.Method;
        var centralMethod = (int)recipe.Method;

        if (layout.LocalHeaderOffset >= 0
            && checked(layout.LocalHeaderOffset + 10) <= archiveBytes.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(archiveBytes.Slice((int)layout.LocalHeaderOffset, 4)) == ArchiveFixtureLayout.LocalHeaderSignature)
        {
            localMethod = BinaryPrimitives.ReadUInt16LittleEndian(archiveBytes.Slice((int)layout.LocalHeaderOffset + 8, 2));
        }

        if (layout.CentralDirectoryOffset >= 0
            && checked(layout.CentralDirectoryOffset + 12) <= archiveBytes.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(archiveBytes.Slice((int)layout.CentralDirectoryOffset, 4)) == ArchiveFixtureLayout.CentralHeaderSignature)
        {
            centralMethod = BinaryPrimitives.ReadUInt16LittleEndian(archiveBytes.Slice((int)layout.CentralDirectoryOffset + 10, 2));
        }

        return (localMethod, centralMethod);
    }

    /// <summary>
    /// Capability-specific expectations (REQ-212): valid controls must fully pass;
    /// CRC-defect fixtures allow both detected and undetected outcomes under the strict
    /// integrity profile (some readers do not check CRC) while the payload bytes must
    /// stay unchanged; truncation fixtures may fail at any defined stage.
    /// </summary>
    private const string StrictProfile = "strict";
    private const string FullCodecProfile = "full-codec";
    private const string UnsupportedReaderProfile = "unsupported-reader";
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
        // Valid archives whose entries require capability-specific verification profiles
        // (tickets #897, #898, #930, #931): full-codec readers (e.g. 7-Zip) must list, decode,
        // hash-check, integrity-check, and extract successfully; readers without the codec
        // must cleanly reject the unsupported method, never producing corrupt unverified bytes.
        if (definition.CaseKey is "valid-bzip2" or "valid-deflate64" or "valid-deflate64-long-match" or "valid-mixed-methods" or "bzip2-high-ratio-bounded")
        {
            return
            [
                Expectation(ListOperation, FullCodecProfile, ["listed-count-matches-entries"], [NoPartialWrites, "listed-count == entry-count"]),
                Expectation(ReadEntryOperation, FullCodecProfile, ["read-entry-content-matches"], [NoPartialWrites]),
                Expectation(IntegrityCheckOperation, FullCodecProfile, ["integrity-passes"], [NoPartialWrites]),
                Expectation(ExtractOperation, FullCodecProfile, ["extract-completes"], [NoPartialWrites, "extracted-bytes-match-content-hashes"]),

                Expectation(ListOperation, UnsupportedReaderProfile, ["listed-count-matches-entries"], [NoPartialWrites, "listed-count == entry-count"]),
                Expectation(ReadEntryOperation, UnsupportedReaderProfile, ["unsupported-method-rejected"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, UnsupportedReaderProfile, ["unsupported-method-rejected"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, UnsupportedReaderProfile, ["unsupported-method-rejected"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, ExtractOperation]),
            ];
        }

        // The shared-range resource case (ticket #872): structurally honest (valid),
        // but all 64 entries share one name and one physical compressed range, so
        // both extraction and overlap policy vary by reader: overwriting readers
        // succeed, duplicate-refusing readers fail extracts, and strict overlap
        // checkers (Python 3.12 raises where 3.14 warns-and-continues) fail reads.
        // Every observed variance is recorded; the payload bytes never change.
        if (definition.CaseKey == "zip-bomb-overlapping-deflate")
        {
            return
            [
                Expectation(ListOperation, StrictProfile, ["listed-count-matches-entries"], [NoPartialWrites, "listed-count == entry-count"]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes", "integrity-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-completes", "extract-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, ExtractOperation]),
            ];
        }

        if (definition.CaseKey == "eocdr-ambiguity-comment")
        {
            return
            [
                Expectation(ListOperation, StrictProfile, [ListFails], [PayloadBytesUnchanged, "two-structurally-plausible-eocd-records"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes"], [PayloadBytesUnchanged, "shadow-eocd-selects-b-bin"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails"], [PayloadBytesUnchanged, "shadow-eocd-selects-b-bin"], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails"], [PayloadBytesUnchanged, "shadow-eocd-selects-b-bin"], ["open", ReadEntryOperation, ExtractOperation]),
            ];
        }

        if (!definition.IsMutation)
        {
            // Policy-sensitive direct recipes (ticket #842): valid ZIP syntax whose
            // names/metadata exercise extractor policy. unsupported-method is also
            // policy-sensitive but is a mutation case, handled by the switch below.
            if (definition.Classification == ArchiveTestCatalog.PolicySensitiveClassification)
            {
                return PolicyExpectations(definition);
            }

            return
            [
                Expectation(ListOperation, StrictProfile, ["listed-count-matches-entries"], [NoPartialWrites, "listed-count == entry-count"]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches"], [NoPartialWrites]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes"], [NoPartialWrites]),
                Expectation(ExtractOperation, StrictProfile, ["extract-completes"], [NoPartialWrites, "extracted-bytes-match-content-hashes"]),
            ];
        }

        var chain = definition.Mutations!;
        return chain switch
        {
            // The one-mutation form: expectations per named mutation kind.
            [var single] => SingleMutationExpectations(single, definition),
            // Combined chains (ticket #843): a finite, explicit compatibility list —
            // never a cross-product of every mutation. The chain order is the recipe.
            [ArchiveTestMutationKind.CrcLocalMismatch, ArchiveTestMutationKind.NameLocalCentralMismatch] =>
            [
                // Two independent field-level lies: a lenient reader may still list and
                // read (with unverified content), a strict one may reject either.
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails", "read-entry-returns-unverified-bytes"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, "strict-integrity-v1", ["crc-mismatch-rejected", "crc-mismatch-unchecked", OperationFails], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation], capability: "crc32"),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            [ArchiveTestMutationKind.CrcLocalMismatch, ArchiveTestMutationKind.TruncatePayloadTail] =>
            [
                // The CRC lie is consumed first; the tail truncation then removes the
                // rest of the payload, the central directory, and the EOCD: every
                // operation fails at a defined stage.
                Expectation(ListOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, [OperationFails], [NoPartialWrites], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            _ => throw new InvalidOperationException(
                $"Archive Test case '{definition.CaseKey}': no expectation set defined for the mutation chain [{string.Join(", ", chain.Select(kind => kind.ToCaseKey()))}]."),
        };
    }

    private static IReadOnlyList<ArchiveTestExpectation> SingleMutationExpectations(
        ArchiveTestMutationKind kind,
        ArchiveTestCaseDefinition definition) =>
        kind switch
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
            // The orphan hidden entry (ticket #869) keeps the visible central directory
            // intact: CD readers list and read visibles while streaming readers see more.
            // Cross-method disagreements (ticket #899) are the same shape with
            // non-Stored/Deflate codes.
            ArchiveTestMutationKind.NameLocalCentralMismatch
                or ArchiveTestMutationKind.MethodLocalCentralMismatch
                or ArchiveTestMutationKind.SizeLocalCentralMismatch
                or ArchiveTestMutationKind.ExtraFieldLengthOverrun
                or ArchiveTestMutationKind.OrphanLocalHeader
                or ArchiveTestMutationKind.MethodCrossDeflate64Deflate
                or ArchiveTestMutationKind.MethodCrossBzip2Stored =>
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
            // Split-archive declarations (ticket #933): spanning is unsupported —
            // readers that honor spanning refuse, readers that ignore spanning
            // read on. Policy-sensitive like unsupported-method, distinguished
            // from malformed count disagreement by classification and invariant.
            ArchiveTestMutationKind.MultidiskEocdDeclared
                or ArchiveTestMutationKind.MultidiskCentralEntryDeclared =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged, "spanning-declared-single-file"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails"], [PayloadBytesUnchanged, "spanning-declared-single-file"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes", "integrity-fails", "integrity-unchecked"], [PayloadBytesUnchanged, "spanning-declared-single-file"], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged, "spanning-declared-single-file"], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Directory-count disagreements (ticket #933): malformed declarations
            // over intact bytes. One field disagrees per case; every observed
            // reader variance is recorded.
            ArchiveTestMutationKind.EocdEntryCountMismatch
                or ArchiveTestMutationKind.Zip64EocdEntryCountMismatch
                or ArchiveTestMutationKind.Zip64LocatorDiskMismatch =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged, "declared-counts-disagree"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails"], [PayloadBytesUnchanged, "declared-counts-disagree"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes", "integrity-fails", "integrity-unchecked"], [PayloadBytesUnchanged, "declared-counts-disagree"], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged, "declared-counts-disagree"], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Descriptor, extra-field, and encoding conflicts (ticket #934): one
            // metadata source disagrees while each structure looks plausible and
            // the payload bytes stay intact. CRC lies report through the CRC
            // vocabulary; the rest admit no CRC outcome they cannot produce.
            ArchiveTestMutationKind.DescriptorCrcDisagreement
                or ArchiveTestMutationKind.UnicodePathCrcMismatch =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails", "read-entry-returns-unverified-bytes"], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "integrity-unchecked", "crc-mismatch-rejected", "crc-mismatch-unchecked"], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            ArchiveTestMutationKind.DescriptorSizeDisagreement
                or ArchiveTestMutationKind.DuplicateZip64Extra
                or ArchiveTestMutationKind.DuplicateUnicodePath
                or ArchiveTestMutationKind.Utf8FlagCp437Name
                or ArchiveTestMutationKind.UnicodePathNameDivergence =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails", "read-entry-returns-unverified-bytes"], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes", "integrity-fails", "integrity-unchecked"], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged, "metadata-sources-disagree"], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Mixed-method one-bad-member cases (ticket #935): the Store and
            // Deflate siblings stay healthy while one member fails. A corrupt
            // BZip2 member fails reads everywhere it decodes; readers without
            // the codec report unsupported instead. A reserved-method member is
            // cleanly unsupported under every profile. Aggregate integrity can
            // never report success while a member fails.
            ArchiveTestMutationKind.MixedMethodsOneCorruptMember =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged, "one-member-fails"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "unsupported-method-rejected"], [PayloadBytesUnchanged, "one-member-fails"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "unsupported-method-rejected"], [PayloadBytesUnchanged, "one-member-fails"], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails"], [PayloadBytesUnchanged, "one-member-fails"], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            ArchiveTestMutationKind.MixedMethodsOneUnsupportedMember =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged, "one-member-unsupported"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["unsupported-method-rejected", "unsupported-method-unchecked"], [PayloadBytesUnchanged, "one-member-unsupported"], ["open", ReadEntryOperation], capability: "method-98-mixed"),
                Expectation(IntegrityCheckOperation, StrictProfile, ["unsupported-method-rejected", "integrity-unchecked"], [PayloadBytesUnchanged, "one-member-unsupported"], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails", "extract-succeeds"], [PayloadBytesUnchanged, "one-member-unsupported"], ["open", ReadEntryOperation, ExtractOperation]),
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
            // Deflate64 lie (ticket #877) and BZip2-labeled stored data (ticket #899):
            // same consistent-code shape, but readers report it differently — .NET
            // fails the entry read (not the unsupported-method rejection method 98
            // gets), zlib readers fail too.
            // ADF/Synapse-bound readers meet a method code with no usable codec.
            ArchiveTestMutationKind.UnsupportedMethodDeflate64
                or ArchiveTestMutationKind.MethodDataBzip2AsStored =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes", "unsupported-method-rejected"], [PayloadBytesUnchanged], ["open", ReadEntryOperation], capability: "method-9-deflate64"),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "integrity-unchecked", "unsupported-method-rejected"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails", "extract-succeeds"], [PayloadBytesUnchanged], ["open", ExtractOperation]),
            ],
            ArchiveTestMutationKind.EncryptionFlagWithPlaintext =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [PayloadBytesUnchanged], ["open", ListOperation]),
                // Read-entry primarily expects failure: mainstream readers honor the
                // encrypted flag and refuse without a password. Cross-implementation
                // note (ticket #845): at least one real reader (.NET ZipArchive)
                // streams the plaintext without honoring the flag, serving bytes it
                // never authenticated — that unverified middle ground is allowed too.
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
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
            // Declared uncompressed size beyond the 32 MiB expanded budget over tiny
            // physical bytes: readers must never allocate or read to the untrusted
            // declared size (ticket #843). Cross-implementation note (ticket #845):
            // a streaming reader that ignores the declared size and stops at the
            // compressed-data end returns the true payload — the content hash still
            // matches and the CRC verifies, so those lenient outcomes are allowed.
            ArchiveTestMutationKind.DeclaredSizeOversized =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [NoPartialWrites, "declared-sizes-are-lies", "no-allocation-from-declared-sizes"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes", "read-entry-content-matches"], [NoPartialWrites, "no-allocation-from-declared-sizes"], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "integrity-unchecked", "integrity-passes"], [NoPartialWrites], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, [OperationFails], [NoPartialWrites, "no-allocation-from-declared-sizes"], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // DEFLATE-family bitstream corruption (tickets #874 and #900): framing
            // and directory stay intact, so listing succeeds; no decoder reproduces
            // verified content from the broken stream. BZip2 block-magic, truncation,
            // and CRC corruptions behave the same way through their own codecs.
            ArchiveTestMutationKind.DeflateInvalidBtype
                or ArchiveTestMutationKind.DeflateCorruptHuffman
                or ArchiveTestMutationKind.MethodDataDeflateAsBzip2 =>
            // Readers report the failure differently: zlib-based readers fail the
            // entry read, while .NET rejects the entry at Open() as an unsupported
            // compression method. The exact exception type is never contracted.
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds], [NoPartialWrites], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes", "unsupported-method-rejected"], [NoPartialWrites], ["open", ReadEntryOperation], capability: "deflate-bitstream"),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "integrity-unchecked", "unsupported-method-rejected"], [NoPartialWrites], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails"], [NoPartialWrites], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            // Coded bitstream corruption (ticket #900): same contract shape as the
            // deflate arm for the BZip2 and Deflate64 payload defects, labeled with
            // the codec-neutral capability.
            ArchiveTestMutationKind.Bzip2CorruptBlockMagic
                or ArchiveTestMutationKind.Bzip2TruncatedStream
                or ArchiveTestMutationKind.Bzip2WrongCrc
                or ArchiveTestMutationKind.Deflate64CorruptStream
                or ArchiveTestMutationKind.Deflate64TruncatedStream =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds], [NoPartialWrites], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-fails", "read-entry-returns-unverified-bytes", "unsupported-method-rejected"], [NoPartialWrites], ["open", ReadEntryOperation], capability: "coded-bitstream"),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-fails", "integrity-unchecked", "unsupported-method-rejected"], [NoPartialWrites], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-fails"], [NoPartialWrites], ["open", ReadEntryOperation, ExtractOperation]),
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
            // Unrebased prefix (ticket #873): the stub shifts every true position while
            // declared offsets stay stale. Prefix-tolerant readers (Python 3.12+, via
            // prepended-data correction) list and read as if rebased; strict readers
            // (.NET) cannot even walk the directory. Both sides are recorded.
            ArchiveTestMutationKind.PrefixUnrebased =>
            [
                Expectation(ListOperation, StrictProfile, [ListSucceeds, ListFails], [NoPartialWrites, "declared-offsets-are-lies"], ["open", ListOperation]),
                Expectation(ReadEntryOperation, StrictProfile, ["read-entry-content-matches", "read-entry-fails", "read-entry-returns-unverified-bytes"], [PayloadBytesUnchanged], ["open", ReadEntryOperation]),
                Expectation(IntegrityCheckOperation, StrictProfile, ["integrity-passes", "integrity-fails", "integrity-unchecked"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, IntegrityCheckOperation]),
                Expectation(ExtractOperation, StrictProfile, ["extract-succeeds", "extract-fails"], [PayloadBytesUnchanged], ["open", ReadEntryOperation, ExtractOperation]),
            ],
            _ => throw new InvalidOperationException(
                $"Archive Test case '{definition.CaseKey}': no expectation set defined for mutation '{kind}'."),
        };

    // Policy-sensitive expectation vocabulary (ticket #842): required behavior is
    // containment, a named no-silent-overwrite policy, and never following escape
    // targets — recorded as invariants plus a permitted rejection stage, never a
    // universal exact exception string. Renaming or rejecting by a documented adapter
    // policy is acceptable; silently writing outside the root is not.
    private const string NoSilentOverwrite = "no-silent-overwrite";
    private const string NoWritesOutsideRoot = "no-writes-outside-root";
    private const string ContainmentRequired = "containment-required";
    private const string DistinctContentHashes = "distinct-content-hashes-observable";
    private const string LinksNeverMaterialized = "links-never-materialized-as-os-links";
    private const string EscapeTargetsNeverFollowed = "escape-targets-never-followed";
    private const string ListedCountMatchesEntries = "listed-count-matches-entries";
    private const string ReadEntryContentMatches = "read-entry-content-matches";
    private const string IntegrityPasses = "integrity-passes";

    /// <summary>Per-Case Key platform sensitivity and collision invariants.</summary>
    private sealed record PolicyExpectationProfile(string? Platform, string[] CollisionInvariants);

    private static IReadOnlyList<ArchiveTestExpectation> PolicyExpectations(ArchiveTestCaseDefinition definition)
    {
        var profile = definition.CaseKey switch
        {
            // Containment hazards that exist on every platform.
            "path-parent-traversal" or "path-posix-absolute" or "unicode-path-extra-mismatch" =>
                new PolicyExpectationProfile(null, []),
            // Hostile filename bytes (ticket #876): NUL and C0 bytes in member
            // names. Extraction stays contained and never materializes the raw
            // bytes as ambient filenames on ordinary hosts.
            "filename-null-byte" or "filename-c0-control" =>
                new PolicyExpectationProfile(null, []),
            // Entry-type confusion (ticket #875): a directory marker or attribute
            // carrying a payload. Extraction must stay contained; the directory/file
            // verdict itself varies, so no payload-preservation invariant is named here.
            "directory-slash-with-payload" or "directory-attribute-with-payload" =>
                new PolicyExpectationProfile(null, []),
            // Windows-only hazards: drive letters, UNC paths, reserved device names,
            // trailing dot/space (Win32 strips them; POSIX keeps them as literal bytes),
            // and the consolidated illegal-character matrix (tickets #885, #886, #879).
            "path-windows-drive" or "path-unc" or "path-reserved-device" or "path-trailing-dot-space" or "path-windows-illegal-chars" =>
                new PolicyExpectationProfile("windows", []),
            // Collisions: the entries are individually valid; the policy is that no
            // variant may silently overwrite another (reject or rename instead).
            "duplicate-name" or "file-directory-conflict" =>
                new PolicyExpectationProfile(null, [NoSilentOverwrite, DistinctContentHashes]),
            // Zero-width collision (ticket #889): the names are distinct bytes on
            // disk but collapse in stripping indexers and review UIs, so the
            // no-silent-overwrite policy applies only on those collapsing
            // consumers (ordinary extraction keeps both members; the verifier
            // skips this platform unless the consumer opts in).
            "zero-width-collision" =>
                new PolicyExpectationProfile("zero-width-collapsing-consumers", [NoSilentOverwrite, DistinctContentHashes]),
            "case-collision" =>
                new PolicyExpectationProfile("case-insensitive-filesystems", [NoSilentOverwrite, DistinctContentHashes]),
            // Emoji ZWJ NAME_MAX boundary (ticket #888): the exceeded member
            // cannot materialize on Linux (ENAMETOOLONG) while the boundary
            // member extracts everywhere; evaluated only by named Linux jobs.
            "path-emoji-zwj-namemax" =>
                new PolicyExpectationProfile("linux", []),
            "unicode-normalization-collision" =>
                new PolicyExpectationProfile("normalizing-filesystems", [NoSilentOverwrite, DistinctContentHashes]),
            // Symlink metadata: the type bits and escape target are data; extraction
            // must neither materialize OS links nor follow the target.
            "symlink-then-descendant" =>
                new PolicyExpectationProfile(null, [LinksNeverMaterialized, EscapeTargetsNeverFollowed]),
            // Bidi override (ticket #884): the control character (U+202E, three
            // UTF-8 bytes E2 80 AE) is data; the name must round-trip verbatim
            // and extraction stays contained via the shared invariants below.
            "filename-bidi-override" =>
                new PolicyExpectationProfile(null, []),
            // Azure directory markers (ticket #880): slash marker, $folder$ marker,
            // and genuine child coexist; migration must not collapse them.
            "azure-directory-marker-collision" =>
                new PolicyExpectationProfile(null, [NoSilentOverwrite, DistinctContentHashes]),
            // ADLS Gen2 segment depth (ticket #878): container-relative 63/64
            // segment members. The boundary member is legal everywhere; the
            // exceeded member only violates the hierarchical-namespace limit,
            // so only named ADLS jobs evaluate its extraction verdict.
            "path-adls-segments-boundary" =>
                new PolicyExpectationProfile(null, []),
            "path-adls-segments-exceeded" =>
                new PolicyExpectationProfile("adls-gen2", []),
            // Azure-disallowed Unicode (ticket #882): non-characters and C1 controls
            // in names. Valid Archive entries; only Azure upload rejects them.
            "path-azure-disallowed-unicode" =>
                new PolicyExpectationProfile(null, []),
            _ => throw new InvalidOperationException(
                $"Archive Test case '{definition.CaseKey}': no policy expectation set defined for a policy-sensitive direct recipe."),
        };

        return
        [
            Expectation(ListOperation, StrictProfile, [ListedCountMatchesEntries], [NoPartialWrites], platform: profile.Platform),
            Expectation(ReadEntryOperation, StrictProfile, [ReadEntryContentMatches], [NoPartialWrites], platform: profile.Platform),
            Expectation(IntegrityCheckOperation, StrictProfile, [IntegrityPasses], [NoPartialWrites], platform: profile.Platform),
            // Extract deliberately does NOT carry no-partial-writes: a documented
            // per-entry policy may reject or rename one member and still extract the
            // rest of a multi-entry Archive. The required invariants are containment
            // (never outside the root) and, for collisions, no silent overwrites.
            Expectation(
                ExtractOperation,
                StrictProfile,
                ["entry-rejected", "entry-renamed-by-policy", "extract-fails"],
                [NoWritesOutsideRoot, ContainmentRequired, .. profile.CollisionInvariants],
                [ExtractOperation],
                platform: profile.Platform),
        ];
    }

    private static ArchiveTestExpectation Expectation(
        string operation,
        string profile,
        string[] allowedOutcomes,
        string[] invariants,
        string[]? failureStages = null,
        string? capability = null,
        string? platform = null) =>
        new(
            Operation: operation,
            Profile: profile,
            AllowedOutcomes: allowedOutcomes,
            Invariants: invariants,
            Platform: platform,
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
