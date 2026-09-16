using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #842 policy-sensitive cases: member names and metadata that exercise
/// extractor safety — path escapes, absolute/UNC/reserved names, name collisions,
/// file/directory conflicts, and Unix symlink metadata. Fixtures are inspected only
/// through read-only APIs and raw header bytes; <see cref="ZipFileExtensions.ExtractToDirectory"/>
/// is never called on them (that requires an isolated VM/container, out of scope here).
/// </summary>
public class ArchivePathPolicyCaseTests : TempDirectoryTestBase
{
    private static readonly string[] PolicyCaseKeys =
    [
        "path-parent-traversal",
        "path-posix-absolute",
        "path-windows-drive",
        "path-unc",
        "path-reserved-device",
        "path-trailing-dot-space",
        "duplicate-name",
        "case-collision",
        "unicode-normalization-collision",
        "file-directory-conflict",
        "symlink-then-descendant",
        "azure-directory-marker-collision",
        "path-windows-illegal-chars",
        "path-azure-disallowed-unicode",
        "directory-slash-with-payload",
        "directory-attribute-with-payload",
    ];

    /// <summary>The ten cases whose extract expectation is a containment policy.</summary>
    private static readonly string[] ContainmentCaseKeys =
    [
        "path-parent-traversal",
        "path-posix-absolute",
        "path-windows-drive",
        "path-unc",
        "path-reserved-device",
        "path-trailing-dot-space",
        "symlink-then-descendant",
        "path-azure-disallowed-unicode",
        "directory-slash-with-payload",
        "directory-attribute-with-payload",
    ];

    /// <summary>The five collision cases that name a no-silent-overwrite policy.</summary>
    private static readonly string[] CollisionCaseKeys =
    [
        "duplicate-name",
        "case-collision",
        "unicode-normalization-collision",
        "file-directory-conflict",
        "azure-directory-marker-collision",
    ];

    public static TheoryData<string> AllPolicyCaseKeys => new(PolicyCaseKeys);
    public static TheoryData<string> ContainmentCaseKeyData => new(ContainmentCaseKeys);
    public static TheoryData<string> CollisionCaseKeyData => new(CollisionCaseKeys);

    private static ArchiveFixtureArtifact BuildControl(string caseKey) =>
        ArchiveFixtureBuilder.BuildControl(caseKey, 42, CancellationToken.None);

    private static string Utf8Hex(string text) => Convert.ToHexStringLower(Encoding.UTF8.GetBytes(text));

    private static uint ReadUInt32(byte[] bytes, long offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)offset, 4));

    [Fact]
    public void Build_WindowsIllegalChars_KeepsExactRawNames()
    {
        var artifact = BuildControl("path-windows-illegal-chars");

        Assert.Equal(
            ["test.txt:hidden", "a<b.txt", "a>b.txt", "a\"b.txt", "a|b.txt", "a?b.txt", "a*b.txt", "folder./doc.txt", "a\\b.txt"],
            artifact.Layout.Entries.Select(e => e.Name).ToList());
    }

    [Fact]
    public void Build_AzureDisallowedUnicode_KeepsExactCodepoints()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("path-azure-disallowed-unicode", 42, CancellationToken.None);

        Assert.Equal(3, artifact.Layout.EntryCount);
        Assert.Equal(Utf8Hex("data" + (char)0xFDD0 + "file.txt"), artifact.Layout.Entries[0].NameHex);
        Assert.Equal(Utf8Hex("data" + (char)0x85 + "file.txt"), artifact.Layout.Entries[1].NameHex);
        Assert.Equal(Utf8Hex("data" + (char)0xFFFE + "file.txt"), artifact.Layout.Entries[2].NameHex);

        // Distinct payloads keep every member observable by content hash.
        Assert.Equal(3, artifact.Entries.Select(e => e.ContentSha256).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("atc-azure-nonchar", Encoding.ASCII.GetString(artifact.Entries[0].Content));
        Assert.Equal("atc-azure-c1ctrl", Encoding.ASCII.GetString(artifact.Entries[1].Content));
        Assert.Equal("atc-azure-nonchar-fffe", Encoding.ASCII.GetString(artifact.Entries[2].Content));
    }

    [Fact]
    public void Build_AzureDirectoryMarkerCollision_KeepsAllThreeMembersDistinct()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("azure-directory-marker-collision", 42, CancellationToken.None);

        Assert.Equal(3, artifact.Layout.EntryCount);
        Assert.Equal(
            ["folder/", "folder_$folder$", "folder/child.txt"],
            artifact.Layout.Entries.Select(e => e.Name).ToList());

        // Distinct payloads keep every member observable by content hash: no silent
        // collapse between the slash marker, the $folder$ marker, and the child.
        Assert.Equal(3, artifact.Entries.Select(e => e.ContentSha256).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("atc-azure-dir-marker", Encoding.ASCII.GetString(artifact.Entries[0].Content));
        Assert.Equal("atc-azure-folder", Encoding.ASCII.GetString(artifact.Entries[1].Content));
        Assert.Equal("atc-azure-child", Encoding.ASCII.GetString(artifact.Entries[2].Content));
    }

    [Fact]
    public void Build_FilenameNullByte_EmbedsNulInBothHeaders()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("filename-null-byte", 42, CancellationToken.None);
        var repeat = ArchiveFixtureBuilder.BuildControl("filename-null-byte", 42, CancellationToken.None);

        Assert.Equal(repeat.ArchiveBytes, artifact.ArchiveBytes);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal("report.pdfX.exe".Length, entry.LocalNameLength);
        Assert.Equal("7265706f72742e706466002e657865", entry.NameHex);

        // The NUL sits at index 10 in both headers; neighbors are untouched.
        var localNameAt = entry.LocalHeaderOffset + 30;
        Assert.Equal(0x00, artifact.ArchiveBytes[(int)localNameAt + 10]);
        Assert.Equal((byte)'f', artifact.ArchiveBytes[(int)localNameAt + 9]);
        Assert.Equal((byte)'.', artifact.ArchiveBytes[(int)localNameAt + 11]);
        var centralNameAt = entry.CentralDirectoryOffset + 46;
        Assert.Equal(
            artifact.ArchiveBytes.AsSpan((int)localNameAt, entry.LocalNameLength).ToArray(),
            artifact.ArchiveBytes.AsSpan((int)centralNameAt, entry.CentralNameLength).ToArray());

        Assert.Equal(
            artifact.Entries[0].Content,
            ReadEntryContent(artifact.ArchiveBytes, artifact.Layout.Entries[0].Name));
    }

    [Fact]
    public void Build_FilenameC0Control_EmbedsControlRangeInBothHeaders()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("filename-c0-control", 42, CancellationToken.None);
        var repeat = ArchiveFixtureBuilder.BuildControl("filename-c0-control", 42, CancellationToken.None);

        Assert.Equal(repeat.ArchiveBytes, artifact.ArchiveBytes);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal("report.pdfX.exe".Length, entry.LocalNameLength);
        Assert.Equal("0102036f72742e706466582e657865", entry.NameHex);

        // The C0 bytes sit at indexes 0-2 in both headers; neighbors are untouched.
        var localNameAt = entry.LocalHeaderOffset + 30;
        Assert.Equal(0x01, artifact.ArchiveBytes[(int)localNameAt]);
        Assert.Equal(0x02, artifact.ArchiveBytes[(int)localNameAt + 1]);
        Assert.Equal(0x03, artifact.ArchiveBytes[(int)localNameAt + 2]);
        Assert.Equal((byte)'o', artifact.ArchiveBytes[(int)localNameAt + 3]);
        Assert.Equal((byte)'X', artifact.ArchiveBytes[(int)localNameAt + 10]);
        var centralNameAt = entry.CentralDirectoryOffset + 46;
        Assert.Equal(
            artifact.ArchiveBytes.AsSpan((int)localNameAt, entry.LocalNameLength).ToArray(),
            artifact.ArchiveBytes.AsSpan((int)centralNameAt, entry.CentralNameLength).ToArray());
        Assert.Equal(
            artifact.Entries[0].Content,
            ReadEntryContent(artifact.ArchiveBytes, artifact.Layout.Entries[0].Name));
    }

    private static byte[] ReadEntryContent(byte[] archiveBytes, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"entry '{entryName}' not found");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void Build_UnicodePathExtraMismatch_Injects7075FieldInBothHeaders()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("unicode-path-extra-mismatch", 42, CancellationToken.None);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal("safe.txt", entry.Name);
        Assert.Equal("2e2e2f2e2e2f657363617065642e747874", entry.UnicodePathNameHex);
        Assert.NotEqual(entry.NameHex, entry.UnicodePathNameHex);

        // Both headers carry the 25-byte subfield: tag 0x7075, version 1, CRC-32 of
        // the standard name, discrepant Unicode name. Lengths come from the headers.
        var unicodeName = Encoding.UTF8.GetBytes("../../escaped.txt");
        var subfieldLength = 4 + 1 + 4 + unicodeName.Length;
        Assert.Equal(subfieldLength, entry.LocalExtraLength);
        Assert.Equal(subfieldLength, entry.CentralExtraLength);

        var localExtraAt = entry.LocalHeaderOffset + 30 + entry.LocalNameLength;
        Assert.Equal(0x7075, ReadUInt16(artifact.ArchiveBytes, localExtraAt));
        Assert.Equal(1 + 4 + unicodeName.Length, ReadUInt16(artifact.ArchiveBytes, localExtraAt + 2));
        Assert.Equal(1, artifact.ArchiveBytes[(int)localExtraAt + 4]);
        Assert.Equal(0x46d8446bu, ReadUInt32(artifact.ArchiveBytes, localExtraAt + 5));

        var centralExtraAt = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength;
        Assert.Equal(0x7075, ReadUInt16(artifact.ArchiveBytes, centralExtraAt));
        Assert.Equal(
            artifact.ArchiveBytes.AsSpan((int)localExtraAt, subfieldLength).ToArray(),
            artifact.ArchiveBytes.AsSpan((int)centralExtraAt, subfieldLength).ToArray());
    }

    [Fact]
    public void Build_UnicodePathExtraMismatch_DotNetIgnoresExtraField()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("unicode-path-extra-mismatch", 42, CancellationToken.None);

        // The verified differential: .NET resolves the standard header name and
        // ignores the discrepant Unicode Path field, while 0x7075-honoring readers
        // (Python zipfile, asserted by the independent verifier) resolve the
        // traversal name. Both behaviors are recorded, neither assumed.
        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        var sole = Assert.Single(archive.Entries);
        Assert.Equal("safe.txt", sole.FullName);
        using var stream = sole.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        Assert.Equal(artifact.Entries[0].Content, copy.ToArray());
    }

    private static ushort ReadUInt16(byte[] bytes, long offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)offset, 2));

    /// <summary>Reads one entry's content by ordinal through the reference reader —
    /// read-only, never extraction.</summary>
    private static byte[] ReadOrdinalContent(ArchiveFixtureArtifact artifact, int ordinal)
    {
        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        using var stream = archive.Entries[ordinal].Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Publishes the policy suite into a TempDir-relative directory.</summary>
    private async Task<ArchiveTestSuiteResult> PublishPolicySuiteAsync(string directoryName) =>
        await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create([.. PolicyCaseKeys], 42, Path.Combine(TempDir, directoryName)),
            CancellationToken.None);

    /// <summary>
    /// Publishes the policy suite once per test and returns the parsed Expectation Files
    /// by Case Key — expectations are verified from the real generator output on disk.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ArchiveTestCase>> PublishPolicyCasesAsync()
    {
        var result = await PublishPolicySuiteAsync("expectations");

        return Directory.GetFiles(result.PublishedDirectory, "*.json")
            .Select(p => ArchiveTestJson.Parse(File.ReadAllBytes(p)))
            .ToDictionary(t => t.CaseKey);
    }

    private static ArchiveTestExpectation ExtractExpectationOf(ArchiveTestCase testCase) =>
        testCase.Expectations.Single(e => e.Operation == "extract");

    // ---- Catalog wiring ----

    // The security-suite membership contract (policy, collision, unsupported-feature,
    // bounded resource per #834) is pinned by ArchiveTestSuiteContractTests; these
    // policy-case tests target the sixteen policy-sensitive recipes directly.

    // ---- Raw name bytes (the hazard is the bytes themselves) ----

    [Fact]
    public void Build_ParentTraversal_NameHoldsLiteralDotDotComponent()
    {
        var artifact = BuildControl("path-parent-traversal");

        Assert.Equal(Utf8Hex("../escape.txt"), artifact.Layout.Entries[0].NameHex);
        Assert.Equal("../escape.txt", artifact.Layout.Entries[0].Name);
        Assert.Empty(artifact.Mutations);
    }

    [Fact]
    public void Build_PosixAbsolute_NameIsSlashRooted()
    {
        var artifact = BuildControl("path-posix-absolute");

        Assert.Equal(Utf8Hex("/etc/target.txt"), artifact.Layout.Entries[0].NameHex);
    }

    [Fact]
    public void Build_WindowsDrive_NameHoldsDriveLetterAndBackslashes()
    {
        var artifact = BuildControl("path-windows-drive");

        Assert.Equal(Utf8Hex(@"C:\boot.txt"), artifact.Layout.Entries[0].NameHex);
    }

    [Fact]
    public void Build_Unc_NameHoldsDoubleBackslashPrefix()
    {
        var artifact = BuildControl("path-unc");

        Assert.Equal(Utf8Hex(@"\\server\share\doc.txt"), artifact.Layout.Entries[0].NameHex);
    }

    [Fact]
    public void Build_ReservedDevice_NamesAreWindowsDeviceNames()
    {
        var artifact = BuildControl("path-reserved-device");

        Assert.Equal(Utf8Hex("NUL.txt"), artifact.Layout.Entries[0].NameHex);
        Assert.Equal(Utf8Hex("CON.txt"), artifact.Layout.Entries[1].NameHex);
    }

    [Fact]
    public void Build_TrailingDotSpace_NamesEndInDotOrSpace()
    {
        var artifact = BuildControl("path-trailing-dot-space");

        Assert.Equal(Utf8Hex("trailing.dot."), artifact.Layout.Entries[0].NameHex);
        Assert.Equal(Utf8Hex("trailing.txt "), artifact.Layout.Entries[1].NameHex);
    }

    // ---- Collisions: ordinals preserved, distinct hashes observable ----

    [Fact]
    public void Build_DuplicateName_TwoOrdinalsSameRawNameDistinctContentHashes()
    {
        var artifact = BuildControl("duplicate-name");

        Assert.Equal(2, artifact.Layout.EntryCount);
        Assert.Equal(artifact.Layout.Entries[0].NameHex, artifact.Layout.Entries[1].NameHex);
        Assert.Equal(0, artifact.Layout.Entries[0].Ordinal);
        Assert.Equal(1, artifact.Layout.Entries[1].Ordinal);
        Assert.NotEqual(artifact.Entries[0].ContentSha256, artifact.Entries[1].ContentSha256);

        // The reference reader lists both ordinals; read-only access, no extraction.
        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
    }

    [Fact]
    public void Build_CaseCollision_OnlyCaseDiffersAndHashesDiffer()
    {
        var artifact = BuildControl("case-collision");

        Assert.Equal(Utf8Hex("node.txt"), artifact.Layout.Entries[0].NameHex);
        Assert.Equal(Utf8Hex("Node.txt"), artifact.Layout.Entries[1].NameHex);
        Assert.NotEqual(artifact.Entries[0].ContentSha256, artifact.Entries[1].ContentSha256);
        Assert.Equal([0, 1], artifact.Layout.Entries.Select(e => e.Ordinal));
    }

    [Fact]
    public void Build_UnicodeNormalization_BytesDifferDecodedNamesNormalizeEqual()
    {
        var artifact = BuildControl("unicode-normalization-collision");

        var nfc = artifact.Layout.Entries[0];
        var nfd = artifact.Layout.Entries[1];

        Assert.Equal(Utf8Hex("café.txt"), nfc.NameHex);
        Assert.Equal(Utf8Hex("cafe\u0301.txt"), nfd.NameHex);
        Assert.NotEqual(nfc.NameHex, nfd.NameHex);
        Assert.NotEqual(artifact.Entries[0].ContentSha256, artifact.Entries[1].ContentSha256);

        // Decoded names stay exactly their written forms: precomposed U+00E9 vs
        // "e" + combining U+0301. (Their Unicode-normalization equality is a property
        // of the standard, not of this fixture; asserting it here would also tie the
        // test to host ICU availability.)
        Assert.Equal("café.txt", nfc.Name);
        Assert.Equal("cafe\u0301.txt", nfd.Name);
    }

    [Fact]
    public void Build_FileDirectoryConflict_FileAndNestedChildCoexist()
    {
        var artifact = BuildControl("file-directory-conflict");

        Assert.Equal(Utf8Hex("node"), artifact.Layout.Entries[0].NameHex);
        Assert.Equal(Utf8Hex("node/child.txt"), artifact.Layout.Entries[1].NameHex);
        Assert.All(artifact.Entries, e => Assert.False(e.IsDirectory));
        Assert.Equal(2, artifact.Layout.EntryCount);
    }

    // ---- Symlink metadata: bytes only, never an OS object ----

    [Fact]
    public void Build_Symlink_ExternalAttributesHoldUnixLinkTypeBits()
    {
        var artifact = BuildControl("symlink-then-descendant");
        var central = artifact.Layout.Entries[0].CentralDirectoryOffset;

        // S_IFLNK | 0777 mode bits in the central external attributes (central + 38).
        Assert.Equal(0xA1FF_0000u, ReadUInt32(artifact.ArchiveBytes, central + 38));

        // Version-made-by high byte declares the Unix host so the bits read as mode
        // bits; the low version byte stays exactly the standard writer's value.
        var versionMadeBy = ReadUInt16(artifact.ArchiveBytes, central + 4);
        Assert.Equal(ArchiveTestCatalog.UnixHostSystem, versionMadeBy >> 8);
        var plainControl = BuildControl("valid-stored");
        Assert.Equal(
            ReadUInt16(plainControl.ArchiveBytes, plainControl.Layout.Entries[0].CentralDirectoryOffset + 4) & 0xFF,
            versionMadeBy & 0xFF);
    }

    [Fact]
    public void Build_Symlink_ContentIsInertEscapeTargetTextAndDescendantFollows()
    {
        var artifact = BuildControl("symlink-then-descendant");

        // The link's payload is the escape target as inert text; nothing follows it.
        Assert.Equal("../outside.txt", Encoding.ASCII.GetString(ReadOrdinalContent(artifact, 0)));
        Assert.Equal("atc-symlink-descendant", Encoding.ASCII.GetString(ReadOrdinalContent(artifact, 1)));
    }

    [Fact]
    public void Build_Symlink_CreatesNoFilesystemEntryUnderTheTestRoot()
    {
        var before = Directory.GetFileSystemEntries(TempDir, "*", SearchOption.AllDirectories);

        BuildControl("symlink-then-descendant");

        // Building Archive bytes materializes no file, directory, or symlink anywhere
        // under the test root: the builder's only outputs are in-memory bytes.
        Assert.Equal(before, Directory.GetFileSystemEntries(TempDir, "*", SearchOption.AllDirectories));
    }

    // ---- Read-only verification across all policy cases ----

    [Theory]
    [MemberData(nameof(AllPolicyCaseKeys))]
    public void Build_PolicyCase_BytesAreDeterministicAndMutationFree(string caseKey)
    {
        var first = BuildControl(caseKey);
        var second = BuildControl(caseKey);

        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
        Assert.Empty(first.Mutations);
    }

    [Theory]
    [MemberData(nameof(AllPolicyCaseKeys))]
    public void Read_ThroughReferenceReader_ListsEntriesAndMatchesContentHashes(string caseKey)
    {
        var artifact = BuildControl(caseKey);

        // Read-only listing and per-ordinal content reads: no extraction on this host.
        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        Assert.Equal(artifact.Layout.EntryCount, archive.Entries.Count);

        for (var ordinal = 0; ordinal < archive.Entries.Count; ordinal++)
        {
            using var stream = archive.Entries[ordinal].Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.Equal(artifact.Entries[ordinal].ContentSha256, Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())));
        }
    }

    // ---- Expectations: containment policy, not universal exception strings ----

    [Theory]
    [MemberData(nameof(ContainmentCaseKeyData))]
    public async Task Expectations_Extract_RequiresContainmentWithPermittedRejectionStage(string caseKey)
    {
        var cases = await PublishPolicyCasesAsync();
        var extract = ExtractExpectationOf(cases[caseKey]);

        Assert.Equal(["entry-rejected", "entry-renamed-by-policy", "extract-fails"], extract.AllowedOutcomes);
        Assert.Contains("no-writes-outside-root", extract.Invariants);
        Assert.Contains("containment-required", extract.Invariants);
        Assert.Equal(["extract"], extract.FailureStages);
        Assert.Null(extract.Capability);
    }

    [Theory]
    [MemberData(nameof(CollisionCaseKeyData))]
    public async Task Expectations_Collisions_NameNoSilentOverwritePolicy(string caseKey)
    {
        var cases = await PublishPolicyCasesAsync();
        var extract = ExtractExpectationOf(cases[caseKey]);

        Assert.Contains("no-silent-overwrite", extract.Invariants);
        Assert.Contains("distinct-content-hashes-observable", extract.Invariants);
        Assert.Contains("entry-renamed-by-policy", extract.AllowedOutcomes);
        Assert.Contains("no-writes-outside-root", extract.Invariants);
    }

    [Fact]
    public async Task Expectations_Symlink_MustNotMaterializeLinksOrFollowEscapeTargets()
    {
        var cases = await PublishPolicyCasesAsync();
        var extract = ExtractExpectationOf(cases["symlink-then-descendant"]);

        Assert.Contains("links-never-materialized-as-os-links", extract.Invariants);
        Assert.Contains("escape-targets-never-followed", extract.Invariants);
    }

    [Fact]
    public async Task Expectations_PlatformSensitivity_MarkedPerCaseKey()
    {
        var cases = await PublishPolicyCasesAsync();

        Assert.Null(ExtractExpectationOf(cases["path-parent-traversal"]).Platform);
        Assert.Null(ExtractExpectationOf(cases["path-posix-absolute"]).Platform);
        Assert.Null(ExtractExpectationOf(cases["duplicate-name"]).Platform);
        Assert.Null(ExtractExpectationOf(cases["file-directory-conflict"]).Platform);
        Assert.Null(ExtractExpectationOf(cases["azure-directory-marker-collision"]).Platform);
        Assert.Null(ExtractExpectationOf(cases["symlink-then-descendant"]).Platform);
        Assert.Null(ExtractExpectationOf(cases["directory-slash-with-payload"]).Platform);
        Assert.Null(ExtractExpectationOf(cases["directory-attribute-with-payload"]).Platform);

        Assert.Equal("windows", ExtractExpectationOf(cases["path-windows-drive"]).Platform);
        Assert.Equal("windows", ExtractExpectationOf(cases["path-unc"]).Platform);
        Assert.Equal("windows", ExtractExpectationOf(cases["path-reserved-device"]).Platform);
        Assert.Equal("windows", ExtractExpectationOf(cases["path-trailing-dot-space"]).Platform);
        Assert.Equal("windows", ExtractExpectationOf(cases["path-windows-illegal-chars"]).Platform);
        Assert.Equal("case-insensitive-filesystems", ExtractExpectationOf(cases["case-collision"]).Platform);
        Assert.Equal("normalizing-filesystems", ExtractExpectationOf(cases["unicode-normalization-collision"]).Platform);
    }

    [Fact]
    public async Task Expectations_PolicyCases_ListReadAndIntegrityStayUnconditional()
    {
        var cases = await PublishPolicyCasesAsync();

        foreach (var caseKey in PolicyCaseKeys)
        {
            var expectations = cases[caseKey].Expectations;

            Assert.Equal("listed-count-matches-entries", expectations.Single(e => e.Operation == "list").AllowedOutcomes.Single());
            Assert.Equal("read-entry-content-matches", expectations.Single(e => e.Operation == "read-entry").AllowedOutcomes.Single());
            Assert.Equal("integrity-passes", expectations.Single(e => e.Operation == "integrity-check").AllowedOutcomes.Single());
        }
    }

    // ---- Publication: safe basenames only, nothing materialized outside staging ----

    [Fact]
    public async Task GenerateAsync_SecuritySuite_PublishesAllTwentyNineMembersWithSafeBasenames()
    {
        // The full security suite per #834 (policy recipes plus the
        // unsupported-feature and bounded resource cases) plus the #869 parser
        // differential, the #871 Unicode Path policy case, the #872 shared-range
        // resource case, the #876 hostile-name cases, the #880 Azure marker
        // case, the #882 Azure-disallowed-Unicode case, the #877 Deflate64
        // unsupported-method twin, the consolidated #885/#886/#879 Windows-illegal-character
        // matrix, the two #875 entry-type cases, and the two #899 method/data disagreement cases: every member
        // publishes a safe Fixture ID pair.
        var securityKeys = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.SecuritySuite)
            .Select(c => c.CaseKey)
            .ToList();
        Assert.Equal(29, securityKeys.Count);

        var result = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(securityKeys, 42, Path.Combine(TempDir, "security")),
            CancellationToken.None);

        Assert.Equal(securityKeys.Count, result.FixtureIds.Count);
        Assert.Equal(securityKeys.Count * 2, Directory.GetFiles(result.PublishedDirectory).Length);

        // Every published basename is the safe Fixture ID pair — hostile member
        // names never reach output destinations.
        var pattern = new Regex("^atc-[0-9a-f]{64}\\.(zip|json)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.All(Directory.GetFiles(result.PublishedDirectory), file =>
            Assert.Matches(pattern, Path.GetFileName(file)));
    }

    [Fact]
    public async Task GenerateAsync_PolicyCases_PublishesPairsWithSafeBasenamesOnly()
    {
        // Publishes the sixteen policy-sensitive recipes (the path/collision/entry-type
        // subset of the security suite; the full suite membership is pinned by
        // ArchiveTestSuiteContractTests).
        var result = await PublishPolicySuiteAsync("out");

        Assert.Equal(PolicyCaseKeys.Length, result.FixtureIds.Count);
        Assert.Equal(PolicyCaseKeys.Length * 2, Directory.GetFiles(result.PublishedDirectory).Length);

        // Every published basename is the safe Fixture ID pair — malicious member names
        // never reach output destinations.
        var pattern = new Regex("^atc-[0-9a-f]{64}\\.(zip|json)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        foreach (var file in Directory.GetFiles(result.PublishedDirectory))
        {
            Assert.Matches(pattern, Path.GetFileName(file));
        }

        // The duplicate-name Expectation File preserves both conflicting entries with
        // their ordinals and distinct content hashes.
        ArchiveTestCase? duplicate = null;
        foreach (var jsonPath in Directory.GetFiles(result.PublishedDirectory, "*.json"))
        {
            var parsed = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));
            if (parsed.CaseKey == "duplicate-name")
            {
                duplicate = parsed;
                break;
            }
        }

        var testCase = Assert.IsType<ArchiveTestCase>(duplicate);
        Assert.Equal(2, testCase.Entries.Count);
        Assert.Equal([0, 1], testCase.Entries.Select(e => e.Ordinal));
        Assert.Equal(testCase.Entries[0].ReadableName, testCase.Entries[1].ReadableName);
        Assert.NotEqual(testCase.Entries[0].ContentSha256, testCase.Entries[1].ContentSha256);
    }

    [Fact]
    public async Task GenerateAsync_PolicyCases_MaterializeNothingOutsideTheStagingDirectory()
    {
        var destination = Path.Combine(TempDir, "staged");

        await PublishPolicySuiteAsync("staged");

        // The destination directory itself is the only thing the run created; no file,
        // directory, or symlink appeared anywhere else under the test root.
        var entries = Directory.GetFileSystemEntries(TempDir);
        var entry = Assert.Single(entries);
        Assert.Equal(destination, entry);
    }
}
