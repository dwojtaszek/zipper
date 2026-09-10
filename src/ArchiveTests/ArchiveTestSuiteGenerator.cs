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
            Mutations: [],
            Expectations:
            [
                new ArchiveTestExpectation(
                    Operation: "list",
                    Profile: "strict",
                    AllowedOutcomes: ["listed-count-matches-entries"],
                    Invariants: ["no-partial-writes", "listed-count == entry-count"],
                    Platform: null,
                    Capability: null,
                    FailureStages: null),
                new ArchiveTestExpectation(
                    Operation: "read-entry",
                    Profile: "strict",
                    AllowedOutcomes: ["read-entry-content-matches"],
                    Invariants: ["no-partial-writes"],
                    Platform: null,
                    Capability: null,
                    FailureStages: null),
                new ArchiveTestExpectation(
                    Operation: "integrity-check",
                    Profile: "strict",
                    AllowedOutcomes: ["integrity-passes"],
                    Invariants: ["no-partial-writes"],
                    Platform: null,
                    Capability: null,
                    FailureStages: null),
                new ArchiveTestExpectation(
                    Operation: "extract",
                    Profile: "strict",
                    AllowedOutcomes: ["extract-completes"],
                    Invariants: ["no-partial-writes", "extracted-bytes-match-content-hashes"],
                    Platform: null,
                    Capability: null,
                    FailureStages: null),
            ],
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
