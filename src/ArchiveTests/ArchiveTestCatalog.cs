namespace Zipper.ArchiveTests;

/// <summary>
/// One recipe entry: a fixed name, a seed-varying payload of the given length (or a
/// verbatim prefix for scenarios that need specific bytes), and the compression method.
/// Directory entries end with '/' and carry no payload.
/// </summary>
internal sealed record ArchiveTestRecipeEntry(
    string Name,
    int Length,
    string Method,
    bool IsDirectory,
    byte[]? PayloadPrefix)
{
    internal static ArchiveTestRecipeEntry File(string name, int length, string method = "deflate") =>
        new(name, length, method, IsDirectory: false, PayloadPrefix: null);

    internal static ArchiveTestRecipeEntry Directory(string name) =>
        new(name, 0, "stored", IsDirectory: true, PayloadPrefix: null);
}

internal sealed record ArchiveTestRecipe(IReadOnlyList<ArchiveTestRecipeEntry> Entries);

/// <summary>
/// A Case Key in the finite Archive Test catalog: revisions, classification, suite
/// membership, and the baseline recipe (REQ-208). Malformed cases name their
/// <see cref="ControlCaseKey"/> and <see cref="Mutation"/>: the control's bytes plus the
/// recorded mutation chain reconstruct the fixture. Suite selection never changes a
/// case's bytes; it only selects which cases run.
/// </summary>
internal sealed record ArchiveTestCaseDefinition(
    string CaseKey,
    int CaseRevision,
    int ExpectationRevision,
    string Classification,
    IReadOnlyList<string> Suites,
    ArchiveTestRecipe Recipe,
    string? ControlCaseKey = null,
    ArchiveTestMutationKind? Mutation = null)
{
    public bool IsMutation => Mutation is not null;
}

/// <summary>
/// The explicit, finite list of Archive Test case definitions. Case Keys and revisions
/// are stable: changing a recipe means bumping CaseRevision (or ExpectationRevision for
/// expectations), which changes the Fixture ID (REQ-210).
/// </summary>
internal static class ArchiveTestCatalog
{
    internal const string SmokeSuite = "smoke";
    internal const string CompatibilitySuite = "compatibility";
    internal const string MalformedSuite = "malformed";
    internal const string SecuritySuite = "security";
    internal const string AllSuites = "all";

    internal static readonly IReadOnlyList<string> ValidControlSuites = [SmokeSuite, CompatibilitySuite];

    private static readonly IReadOnlyDictionary<string, ArchiveTestCaseDefinition> Cases = new Dictionary<string, ArchiveTestCaseDefinition>(StringComparer.Ordinal)
    {
        ["valid-empty"] = new(
            "valid-empty", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: ValidControlSuites,
            Recipe: new ArchiveTestRecipe([])),
        ["valid-zero-entry"] = new(
            "valid-zero-entry", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: new ArchiveTestRecipe([])),
        ["valid-stored"] = new(
            "valid-stored", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: ValidControlSuites,
            Recipe: StoredControlRecipe),
        ["valid-deflate"] = new(
            "valid-deflate", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: ValidControlSuites,
            Recipe: new ArchiveTestRecipe(
            [
                ArchiveTestRecipeEntry.File("doc.txt", 400, "deflate"),
            ])),
        ["valid-directories"] = new(
            "valid-directories", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: new ArchiveTestRecipe(
            [
                ArchiveTestRecipeEntry.Directory("dir/"),
                ArchiveTestRecipeEntry.File("dir/file.txt", 60, "stored"),
                ArchiveTestRecipeEntry.File("top.txt", 30, "stored"),
            ])),
        // Malformed cases: built from the named valid control at the same Seed by applying
        // the recorded mutation. The control recipe is repeated so a fixture is reconstructible
        // from controlCaseKey + mutations alone.
        ["crc-local-mismatch"] = MalformedDefinition(ArchiveTestMutationKind.CrcLocalMismatch),
        ["crc-central-mismatch"] = MalformedDefinition(ArchiveTestMutationKind.CrcCentralMismatch),
        ["crc-both-mismatch"] = MalformedDefinition(ArchiveTestMutationKind.CrcBothMismatch),
        ["truncate-payload-tail"] = MalformedDefinition(ArchiveTestMutationKind.TruncatePayloadTail),
        ["truncate-central-tail"] = MalformedDefinition(ArchiveTestMutationKind.TruncateCentralTail),
        ["truncate-eocd"] = MalformedDefinition(ArchiveTestMutationKind.TruncateEocd),
        ["missing-eocd"] = MalformedDefinition(ArchiveTestMutationKind.MissingEocd),
    };

    private const string MalformedClassification = "malformed";
    private const string StoredControlCaseKey = "valid-stored";

    private static ArchiveTestCaseDefinition MalformedDefinition(ArchiveTestMutationKind mutation) => new(
        mutation.ToCaseKey(), CaseRevision: 1, ExpectationRevision: 1, Classification: MalformedClassification,
        Suites: [MalformedSuite],
        Recipe: StoredControlRecipe,
        ControlCaseKey: StoredControlCaseKey,
        Mutation: mutation);

    private static ArchiveTestRecipe StoredControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("a.txt", 100, "stored"),
        ArchiveTestRecipeEntry.File("b.bin", 40, "stored"),
    ]);

    internal static ArchiveTestCaseDefinition GetCase(string caseKey) =>
        Cases.TryGetValue(caseKey, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown Archive Test Case Key '{caseKey}'. Known keys: {string.Join(", ", Cases.Keys.Order())}.");

    internal static IReadOnlyList<ArchiveTestCaseDefinition> ListSuite(string suite) => suite switch
    {
        AllSuites => [.. Cases.Values.OrderBy(c => c.CaseKey, StringComparer.Ordinal)],
        SmokeSuite => [.. Cases.Values.Where(c => c.Suites.Contains(SmokeSuite)).OrderBy(c => c.CaseKey, StringComparer.Ordinal)],
        CompatibilitySuite => [.. Cases.Values.Where(c => c.Suites.Contains(CompatibilitySuite)).OrderBy(c => c.CaseKey, StringComparer.Ordinal)],
        MalformedSuite => [.. Cases.Values.Where(c => c.Suites.Contains(MalformedSuite)).OrderBy(c => c.CaseKey, StringComparer.Ordinal)],
        SecuritySuite => [],
        _ => throw new ArgumentException($"Unknown Archive Test suite '{suite}'."),
    };
}
