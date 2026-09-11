using System.IO.Compression;
using System.Text;

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
    byte[]? PayloadPrefix,
    bool IsPolicyName = false,
    uint ExternalAttributes = 0,
    byte HostSystem = 0)
{
    internal static ArchiveTestRecipeEntry File(string name, int length, string method = "deflate") =>
        new(name, length, method, IsDirectory: false, PayloadPrefix: null);

    internal static ArchiveTestRecipeEntry Directory(string name) =>
        new(name, 0, "stored", IsDirectory: true, PayloadPrefix: null);

    /// <summary>
    /// A policy-sensitive entry (ticket #842): the name is written verbatim — the name
    /// bytes are themselves the hazard under test, so generation-time path validation
    /// is bypassed on purpose (the extractor-facing guards in SourcePathSanitizer and
    /// PathValidator stay untouched). The payload is tiny inert ASCII text; duplicate
    /// and collision variants must carry different texts so overwrite choices stay
    /// observable by content hash. Optional Unix external attributes and host system
    /// mark symlink entries (S_IFLNK type bits; building the Archive bytes never
    /// creates an OS symlink).
    /// </summary>
    internal static ArchiveTestRecipeEntry PolicyFile(string name, string text, uint externalAttributes = 0, byte hostSystem = 0) =>
        new(
            name, text.Length, "stored", IsDirectory: false, PayloadPrefix: Encoding.ASCII.GetBytes(text),
            IsPolicyName: true, ExternalAttributes: externalAttributes, HostSystem: hostSystem);
}

internal sealed record ArchiveTestRecipe(IReadOnlyList<ArchiveTestRecipeEntry> Entries);

/// <summary>
/// A Case Key in the finite Archive Test catalog: revisions, classification, suite
/// membership, and the baseline recipe (REQ-208). Malformed cases name their
/// <see cref="ControlCaseKey"/> and an ordered <see cref="Mutations"/> chain: the
/// control's bytes plus the recorded chain reconstruct the fixture (a single-element
/// chain is the one-mutation form; ticket #843 adds finite, explicit multi-mutation
/// chains — never a cross-product of every mutation). Suite selection never changes a
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
    IReadOnlyList<ArchiveTestMutationKind>? Mutations = null,
    ArchiveControlConstruction? Construction = null)
{
    public bool IsMutation => Mutations is { Count: > 0 };
}

/// <summary>
/// Baseline construction steps for valid controls (tickets #840 and #841). Construction
/// is valid building, never a defect: the Archive stays fully readable with matching
/// content hashes. <see cref="LocalExtraField"/> through <see cref="ArchiveComment"/>
/// are applied after the standard writer finalized the Archive;
/// <see cref="NonSeekableDescriptor"/> and <see cref="Zip64HandBuilt"/> change how the
/// baseline bytes are produced in the first place.
/// </summary>
internal enum ArchiveControlConstruction
{
    /// <summary>Inserts an 8-byte extra subfield into entry 0's local header.</summary>
    LocalExtraField,

    /// <summary>Writes through a non-seekable stream so the standard writer emits
    /// data descriptors with the signature (APPNOTE §4.3.9).</summary>
    NonSeekableDescriptor,

    /// <summary>Removes the descriptor's optional signature and re-links subsequent
    /// offsets; the unsigned descriptor form is equally valid.</summary>
    StripDescriptorSignature,

    /// <summary>Swaps one ASCII name byte for its legacy CP437 byte with bit 11 clear.</summary>
    Cp437Name,

    /// <summary>Appends an archive comment containing signature-like bytes; the comment
    /// length field, not a signature search, determines the layout.</summary>
    ArchiveComment,

    /// <summary>Serializes the tiny Zip64 records directly (sentinels, extended fields in
    /// specification order, Zip64 EOCD and locator); the standard writer cannot select
    /// Zip64 for small Archives.</summary>
    Zip64HandBuilt,
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

    /// <summary>The archive comment for valid-signatures-in-comment: signature-like
    /// PK bytes (local, central, and descriptor signatures — the EOCD signature is
    /// excluded because a reference reader's backward EOCD scan misreads it) plus
    /// padding; pure data, located by the EOCD comment length field.</summary>
    internal static readonly byte[] SignatureComment =
        [0x50, 0x4b, 0x03, 0x04, 0x50, 0x4b, 0x01, 0x02, 0x50, 0x4b, 0x07, 0x08, .. Encoding.ASCII.GetBytes("atc-comment!")];

    /// <summary>The length of <see cref="SignatureComment"/>; also declared by the
    /// builder so the layout reader locates the EOCD by length, never by search.</summary>
    internal const int SignatureCommentLength = 24;

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
            Recipe: DeflateControlRecipe),
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
            Construction: ArchiveControlConstruction.LocalExtraField),
        // Ticket #841 compatibility controls: descriptors (APPNOTE §4.3.9), name encodings
        // (bit 11 / CP437), signature-like comment bytes, and a tiny genuine Zip64 Archive.
        ["valid-descriptor-signature"] = new(
            "valid-descriptor-signature", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: DescriptorControlRecipe,
            Construction: ArchiveControlConstruction.NonSeekableDescriptor),
        ["valid-descriptor-no-signature"] = new(
            "valid-descriptor-no-signature", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: DescriptorControlRecipe,
            Construction: ArchiveControlConstruction.StripDescriptorSignature),
        ["valid-utf8-name"] = new(
            "valid-utf8-name", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: Utf8NameControlRecipe),
        ["valid-cp437-name"] = new(
            "valid-cp437-name", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: Cp437NameControlRecipe,
            Construction: ArchiveControlConstruction.Cp437Name),
        ["valid-signatures-in-comment"] = new(
            "valid-signatures-in-comment", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: SignatureCommentControlRecipe,
            Construction: ArchiveControlConstruction.ArchiveComment),
        ["valid-zip64-small"] = new(
            "valid-zip64-small", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: Zip64ControlRecipe,
            Construction: ArchiveControlConstruction.Zip64HandBuilt),
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
        ["invalid-utf8-name"] = MutatedDefinition(ArchiveTestMutationKind.InvalidUtf8Name),
        ["zip64-missing-extra"] = MutatedDefinition(
            ArchiveTestMutationKind.Zip64MissingExtra, controlCaseKey: Zip64ControlCaseKey),
        ["zip64-truncated-extra"] = MutatedDefinition(
            ArchiveTestMutationKind.Zip64TruncatedExtra, controlCaseKey: Zip64ControlCaseKey),
        // Ticket #842 policy-sensitive cases: member names and metadata that exercise
        // extractor safety. All are direct recipes (no mutation, no construction): the
        // bytes are exactly what the standard writer emits for these names. They are
        // valid ZIP syntax — classification policy-sensitive, suite security — so only
        // extraction policy, never ZIP validity, is in question.
        ["path-parent-traversal"] = PolicyDefinition("path-parent-traversal", ParentTraversalRecipe),
        ["path-posix-absolute"] = PolicyDefinition("path-posix-absolute", PosixAbsoluteRecipe),
        ["path-windows-drive"] = PolicyDefinition("path-windows-drive", WindowsDriveRecipe),
        ["path-unc"] = PolicyDefinition("path-unc", UncRecipe),
        ["path-reserved-device"] = PolicyDefinition("path-reserved-device", ReservedDeviceRecipe),
        ["path-trailing-dot-space"] = PolicyDefinition("path-trailing-dot-space", TrailingDotSpaceRecipe),
        ["duplicate-name"] = PolicyDefinition("duplicate-name", DuplicateNameRecipe),
        ["case-collision"] = PolicyDefinition("case-collision", CaseCollisionRecipe),
        ["unicode-normalization-collision"] = PolicyDefinition("unicode-normalization-collision", NormalizationCollisionRecipe),
        ["file-directory-conflict"] = PolicyDefinition("file-directory-conflict", FileDirectoryConflictRecipe),
        ["symlink-then-descendant"] = PolicyDefinition("symlink-then-descendant", SymlinkThenDescendantRecipe),
        // Ticket #843 bounded resource cases: honest high compression, genuine
        // depth-two nesting, and the exact entry cap — all valid Archives.
        ["high-ratio-bounded"] = new(
            "high-ratio-bounded", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: HighRatioControlRecipe),
        ["nested-archives-depth-two"] = new(
            "nested-archives-depth-two", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: NestedArchiveControlRecipe),
        ["many-small-entries"] = new(
            "many-small-entries", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [CompatibilitySuite],
            Recipe: ManySmallEntriesRecipe),
        // A declared uncompressed size beyond the expanded budget over tiny physical
        // bytes: a declaration lie, never a real allocation (ticket #843).
        ["declared-size-oversized"] = MutatedDefinition(
            ArchiveTestMutationKind.DeclaredSizeOversized, controlCaseKey: DeflateControlCaseKey),
        // Ticket #843 combined chains: the audited field-level mutations composed in a
        // finite, explicit list; the chain order is the recipe, each mutation consumes
        // the previous result.
        ["combined-crc-and-name"] = CombinedDefinition(
            "combined-crc-and-name",
            ArchiveTestMutationKind.CrcLocalMismatch, ArchiveTestMutationKind.NameLocalCentralMismatch),
        ["combined-crc-and-truncate"] = CombinedDefinition(
            "combined-crc-and-truncate",
            ArchiveTestMutationKind.CrcLocalMismatch, ArchiveTestMutationKind.TruncatePayloadTail),
    };

    private const string MalformedClassification = "malformed";
    internal const string PolicySensitiveClassification = "policy-sensitive";

    /// <summary>Unix host system code (version-made-by high byte) for symlink entries.</summary>
    internal const byte UnixHostSystem = 3;
    private const string StoredControlCaseKey = "valid-stored";
    private const string DeflateControlCaseKey = "valid-deflate";
    private const string SignaturePayloadControlCaseKey = "valid-signature-payload";
    private const string ExtraFieldControlCaseKey = "valid-extra-field";
    private const string Zip64ControlCaseKey = "valid-zip64-small";

    private static ArchiveTestCaseDefinition MutatedDefinition(
        ArchiveTestMutationKind mutation,
        string controlCaseKey = StoredControlCaseKey,
        string classification = MalformedClassification) => new(
        mutation.ToCaseKey(), CaseRevision: 1, ExpectationRevision: 1, Classification: classification,
        Suites: [MalformedSuite],
        Recipe: ControlRecipe(controlCaseKey),
        ControlCaseKey: controlCaseKey,
        Mutations: [mutation]);

    /// <summary>An ordered mutation chain (ticket #843): a finite, explicit combination
    /// over the stored control; each mutation consumes the previous result.</summary>
    private static ArchiveTestCaseDefinition CombinedDefinition(string caseKey, params ArchiveTestMutationKind[] chain) => new(
        caseKey, CaseRevision: 1, ExpectationRevision: 1, Classification: MalformedClassification,
        Suites: [MalformedSuite],
        Recipe: ControlRecipe(StoredControlCaseKey),
        ControlCaseKey: StoredControlCaseKey,
        Mutations: chain);

    /// <summary>A policy-sensitive direct recipe (ticket #842): suite security, no
    /// mutation, no construction — the standard writer's bytes for these names.</summary>
    private static ArchiveTestCaseDefinition PolicyDefinition(string caseKey, ArchiveTestRecipe recipe) => new(
        caseKey, CaseRevision: 1, ExpectationRevision: 1, Classification: PolicySensitiveClassification,
        Suites: [SecuritySuite],
        Recipe: recipe);

    private static ArchiveTestRecipe ControlRecipe(string controlCaseKey) => controlCaseKey switch
    {
        StoredControlCaseKey or ExtraFieldControlCaseKey => StoredControlRecipe,
        DeflateControlCaseKey => DeflateControlRecipe,
        SignaturePayloadControlCaseKey => SignaturePayloadControlRecipe,
        Zip64ControlCaseKey => Zip64ControlRecipe,
        _ => throw new InvalidOperationException($"Unknown Archive Test control Case Key '{controlCaseKey}'."),
    };

    private static ArchiveTestRecipe StoredControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("a.txt", 100, "stored"),
        ArchiveTestRecipeEntry.File("b.bin", 40, "stored"),
    ]);

    // The control for the declared-size-oversized mutation: one deflated entry whose
    // compressed bytes are far smaller than its honest uncompressed size.
    private static ArchiveTestRecipe DeflateControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("doc.txt", 400, "deflate"),
    ]);

    private static ArchiveTestRecipe SignaturePayloadControlRecipe => new(
    [
        new ArchiveTestRecipeEntry(
            "sig.txt", 40, "stored", IsDirectory: false, PayloadPrefix: SignatureLikePrefix),
    ]);

    // A single stored entry so the descriptor controls carry exactly one data descriptor
    // whose form is the only variable between the signed and unsigned variants.
    private static ArchiveTestRecipe DescriptorControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("d.txt", 100, "stored"),
    ]);

    // A non-ASCII name: the standard writer encodes it as UTF-8 with bit 11 set.
    private static ArchiveTestRecipe Utf8NameControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("café.txt", 60, "stored"),
    ]);

    // An ASCII placeholder name of the same byte length as its CP437 target
    // ("caf\x82.txt"): the Cp437Name construction swaps one byte, no offsets shift.
    private static ArchiveTestRecipe Cp437NameControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("cafx.txt", 60, "stored"),
    ]);

    // The entry payload itself begins with signature-like bytes (pure data), and the
    // archive comment adds more signature-like bytes after the EOCD.
    private static ArchiveTestRecipe SignatureCommentControlRecipe => new(
    [
        new ArchiveTestRecipeEntry(
            "note.txt", 40, "stored", IsDirectory: false, PayloadPrefix: SignatureLikePrefix),
    ]);

    private static ArchiveTestRecipe Zip64ControlRecipe => new(
    [
        ArchiveTestRecipeEntry.File("z64.txt", 30, "stored"),
    ]);

    // Ticket #843 resource recipes: honest high compression, genuine depth-two
    // nesting, and the exact entry cap. All are valid Archives with true sizes.

    // One MiB of a single repeated byte under deflate: an intentionally high, honest
    // compression ratio — the bounded counterpart of a decompression bomb.
    private static ArchiveTestRecipe HighRatioControlRecipe
    {
        get
        {
            const int ExpandedLength = 1024 * 1024;
            var repeated = new byte[ExpandedLength];
            Array.Fill(repeated, (byte)0x41);
            return new ArchiveTestRecipe(
            [
                new ArchiveTestRecipeEntry("hi-ratio.bin", ExpandedLength, "deflate", IsDirectory: false, PayloadPrefix: repeated),
            ]);
        }
    }

    // A genuine depth-two nesting: the outer Archive carries a real inner Archive as
    // one member's exact content plus a plain sibling file; the inner Archive holds
    // only a regular file — no recursive self-reference, no third level.
    private static ArchiveTestRecipe NestedArchiveControlRecipe
    {
        get
        {
            var inner = BuildInnerArchiveBytes();
            return new ArchiveTestRecipe(
            [
                new ArchiveTestRecipeEntry("inner.zip", inner.Length, "stored", IsDirectory: false, PayloadPrefix: inner),
                ArchiveTestRecipeEntry.File("outer.txt", 30, "stored"),
            ]);
        }
    }

    // Built with the standard writer and a fixed timestamp so the inner Archive's
    // bytes are deterministic recipe content, like every other payload.
    private static byte[] BuildInnerArchiveBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("inner.txt", CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var entryStream = entry.Open();
            entryStream.Write("atc-nested-inner"u8);
        }

        return stream.ToArray();
    }

    // Exactly the 1,000-entry cap: small, distinct, honest members; no hidden extras.
    private static ArchiveTestRecipe ManySmallEntriesRecipe => new(
    [
        .. Enumerable.Range(0, ArchiveTestCaseSemantics.MaxEntries)
            .Select(index => ArchiveTestRecipeEntry.File($"small-{index:0000}.txt", 8, "stored")),
    ]);

    // Ticket #842 policy-sensitive recipes: tiny inert ASCII payloads; the member
    // name bytes are the hazard under test. Collision variants carry different texts
    // so overwrite choices stay observable by content hash.

    private static ArchiveTestRecipe ParentTraversalRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("../escape.txt", "atc-parent-traversal"),
    ]);

    private static ArchiveTestRecipe PosixAbsoluteRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("/etc/target.txt", "atc-posix-absolute"),
    ]);

    private static ArchiveTestRecipe WindowsDriveRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile(@"C:\boot.txt", "atc-windows-drive"),
    ]);

    private static ArchiveTestRecipe UncRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile(@"\\server\share\doc.txt", "atc-unc-escape"),
    ]);

    // Windows reserved device names with innocuous extensions.
    private static ArchiveTestRecipe ReservedDeviceRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("NUL.txt", "atc-reserved-nul"),
        ArchiveTestRecipeEntry.PolicyFile("CON.txt", "atc-reserved-con"),
    ]);

    // Win32 strips trailing dots and spaces from filenames; POSIX keeps them.
    private static ArchiveTestRecipe TrailingDotSpaceRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("trailing.dot.", "atc-trailing-dot"),
        ArchiveTestRecipeEntry.PolicyFile("trailing.txt ", "atc-trailing-space"),
    ]);

    private static ArchiveTestRecipe DuplicateNameRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("dup.txt", "atc-duplicate-first"),
        ArchiveTestRecipeEntry.PolicyFile("dup.txt", "atc-duplicate-second"),
    ]);

    private static ArchiveTestRecipe CaseCollisionRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("node.txt", "atc-case-lower"),
        ArchiveTestRecipeEntry.PolicyFile("Node.txt", "atc-case-upper"),
    ]);

    // NFC ("café.txt", U+00E9) and NFD ("cafe" + U+0301) byte sequences: distinct
    // raw bytes that decode to equal names under Unicode normalization.
    private static ArchiveTestRecipe NormalizationCollisionRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("café.txt", "atc-nfc-text"),
        ArchiveTestRecipeEntry.PolicyFile("cafe\u0301.txt", "atc-nfd-text"),
    ]);

    // A file and a same-named directory path: the conflict an extractor must resolve
    // without silently dropping either member.
    private static ArchiveTestRecipe FileDirectoryConflictRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("node", "atc-conflict-file"),
        ArchiveTestRecipeEntry.PolicyFile("node/child.txt", "atc-conflict-child"),
    ]);

    // A Unix symlink entry (S_IFLNK | 0777 mode bits, Unix host) whose inert text
    // content holds a relative escape target, followed by a descendant entry. Only
    // Archive bytes are created; no OS symlink ever exists.
    private static ArchiveTestRecipe SymlinkThenDescendantRecipe => new(
    [
        ArchiveTestRecipeEntry.PolicyFile("link", "../outside.txt", externalAttributes: 0xA1FF_0000, hostSystem: UnixHostSystem),
        ArchiveTestRecipeEntry.PolicyFile("link/child.txt", "atc-symlink-descendant"),
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
        SecuritySuite => [.. Cases.Values.Where(c => c.Suites.Contains(SecuritySuite)).OrderBy(c => c.CaseKey, StringComparer.Ordinal)],
        _ => throw new ArgumentException($"Unknown Archive Test suite '{suite}'."),
    };
}
