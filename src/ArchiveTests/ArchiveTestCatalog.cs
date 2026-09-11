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
    ArchiveTestMutationKind? Mutation = null,
    ArchiveControlEnrichment? Enrichment = null)
{
    public bool IsMutation => Mutation is not null;
}

/// <summary>
/// Baseline construction steps applied to a valid control after the standard writer
/// finalized it (ticket #840 step 1). Enrichment is valid construction, never a defect:
/// the Archive stays fully readable with matching content hashes.
/// </summary>
internal enum ArchiveControlEnrichment
{
    /// <summary>Inserts an 8-byte extra subfield into entry 0's local header.</summary>
    LocalExtraField,
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

    /// <summary>
    /// Local-header-signature-like prefix (PK\x03\x04 plus a length-like word) placed at
    /// the start of the signature-payload control's stored content. Declared before
    /// <see cref="Cases"/>: the signature-payload recipe reads it during static
    /// initialization, and C# static field initializers run in textual order.
    /// </summary>
    private static readonly byte[] SignatureLikePrefix = [0x50, 0x4b, 0x03, 0x04, 0x14, 0x00];

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
        // A valid control whose stored payload begins with local-header-signature-like
        // bytes (PK\x03\x04): readers must treat them as data, never as a header.
        ["valid-signature-payload"] = new(
            "valid-signature-payload", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: SignaturePayloadControlRecipe),
        // A valid control carrying a legal local-header extra subfield on entry 0; the
        // extra-field-length-overrun case mutates its subfield header (ticket #840 step 1).
        ["valid-extra-field"] = new(
            "valid-extra-field", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: StoredControlRecipe,
            Enrichment: ArchiveControlEnrichment.LocalExtraField),
        // Malformed or policy-sensitive cases: built from the named valid control at the
        // same Seed by applying the recorded mutation. The control recipe is repeated so
        // a fixture is reconstructible from controlCaseKey + mutations alone.
        ["crc-local-mismatch"] = MutatedDefinition(ArchiveTestMutationKind.CrcLocalMismatch),
        ["crc-central-mismatch"] = MutatedDefinition(ArchiveTestMutationKind.CrcCentralMismatch),
        ["crc-both-mismatch"] = MutatedDefinition(ArchiveTestMutationKind.CrcBothMismatch),
        ["truncate-payload-tail"] = MutatedDefinition(ArchiveTestMutationKind.TruncatePayloadTail),
        ["truncate-central-tail"] = MutatedDefinition(ArchiveTestMutationKind.TruncateCentralTail),
        ["truncate-eocd"] = MutatedDefinition(ArchiveTestMutationKind.TruncateEocd),
        ["missing-eocd"] = MutatedDefinition(ArchiveTestMutationKind.MissingEocd),
        ["name-local-central-mismatch"] = MutatedDefinition(ArchiveTestMutationKind.NameLocalCentralMismatch),
        ["method-local-central-mismatch"] = MutatedDefinition(ArchiveTestMutationKind.MethodLocalCentralMismatch),
        ["size-local-central-mismatch"] = MutatedDefinition(ArchiveTestMutationKind.SizeLocalCentralMismatch),
        ["offset-outside-archive"] = MutatedDefinition(ArchiveTestMutationKind.OffsetOutsideArchive),
        ["offset-into-payload"] = MutatedDefinition(
            ArchiveTestMutationKind.OffsetIntoPayload, controlCaseKey: SignaturePayloadControlCaseKey),
        ["extra-field-length-overrun"] = MutatedDefinition(
            ArchiveTestMutationKind.ExtraFieldLengthOverrun, controlCaseKey: ExtraFieldControlCaseKey),
        ["unsupported-method"] = MutatedDefinition(
            ArchiveTestMutationKind.UnsupportedMethod, classification: PolicySensitiveClassification),
        ["encryption-flag-with-plaintext"] = MutatedDefinition(ArchiveTestMutationKind.EncryptionFlagWithPlaintext),
        ["overlapping-entry-ranges"] = MutatedDefinition(ArchiveTestMutationKind.OverlappingEntryRanges),
    };

    private const string MalformedClassification = "malformed";
    private const string PolicySensitiveClassification = "policy-sensitive";
    private const string StoredControlCaseKey = "valid-stored";
    private const string SignaturePayloadControlCaseKey = "valid-signature-payload";
    private const string ExtraFieldControlCaseKey = "valid-extra-field";

    private static ArchiveTestCaseDefinition MutatedDefinition(
        ArchiveTestMutationKind mutation,
        string controlCaseKey = StoredControlCaseKey,
        string classification = MalformedClassification) => new(
        mutation.ToCaseKey(), CaseRevision: 1, ExpectationRevision: 1, Classification: classification,
        Suites: [MalformedSuite],
        Recipe: ControlRecipe(controlCaseKey),
        ControlCaseKey: controlCaseKey,
        Mutation: mutation);

    private static ArchiveTestRecipe ControlRecipe(string controlCaseKey) => controlCaseKey switch
    {
        StoredControlCaseKey or ExtraFieldControlCaseKey => StoredControlRecipe,
        SignaturePayloadControlCaseKey => SignaturePayloadControlRecipe,
        _ => throw new InvalidOperationException($"Unknown Archive Test control Case Key '{controlCaseKey}'."),
    };

    private static ArchiveTestRecipe StoredControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("a.txt", 100, "stored"),
        ArchiveTestRecipeEntry.File("b.bin", 40, "stored"),
    ]);

    private static ArchiveTestRecipe SignaturePayloadControlRecipe => new(
    [
        new ArchiveTestRecipeEntry(
            "sig.txt", 40, "stored", IsDirectory: false, PayloadPrefix: SignatureLikePrefix),
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
