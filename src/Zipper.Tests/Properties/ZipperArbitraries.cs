using System.Runtime.InteropServices;
using FsCheck;
using FsCheck.Fluent;
using Zipper.ArchiveTests;
using Zipper.Config;

namespace Zipper.Tests.Properties;

/// <summary>
/// Bounded input for Load File composer flag combinations, column counts, and record alignment property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MetadataFlagCombinationInput(
    bool IncludeMetadata,
    bool IncludeCustodian,
    bool IncludeDateRange,
    bool IncludeFileSize,
    bool IncludePageCount,
    bool IncludeHash,
    bool IncludeNativePath,
    bool IncludeTextPath,
    bool IncludeEmailFields,
    bool IncludeCompressionMethod,
    string? ColumnProfile,
    BatesNumberConfig? BatesConfig,
    ProductionConfig? ProductionConfig,
    int AttachmentRate,
    int FileCount,
    int Seed);

/// <summary>
/// Bounded input for Load File emitter byte count, preamble, and line-ending contract property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct EmitterPropertyInput(
    string EncodingName,
    bool UseByteOrderMark,
    string EndOfLine,
    bool HasHeader,
    int RecordCount,
    int ColumnCount,
    int Seed);

/// <summary>
/// Bounded input for EDRM-XML load file writer structure and character escaping property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct XmlLoadFilePropertyInput(
    int FileCount,
    bool WithFamilies,
    bool WithText,
    bool WithHash,
    int AttachmentRate,
    int Seed,
    string MetadataValue);

/// <summary>
/// Bounded input for family plan attachment calculation and child order property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct FamilyPlanPropertyInput(
    int FileCount,
    double AttachmentRate,
    int Seed);

/// <summary>
/// Bounded input for companion load file audit writer JSON serialization and total record conservation property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AuditWriterPropertyInput(
    int FileCount,
    int AttachmentRate,
    bool WithFamilies,
    bool IsLoadfileOnly,
    LoadFileFormat Format,
    int Seed);

/// <summary>
/// Bounded input for coded column generator determinism and category membership property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct CodedGeneratorPropertyInput(
    string[] Categories,
    int[]? DistributionIndices,
    bool MultiValue,
    int MultiValueMin,
    int MultiValueMax,
    int DocumentIndex,
    int Seed);

/// <summary>
/// Bounded input for validation fault-injection, supplemental Bates Number range,
/// rolling Bates, and Volume Analyzer property tests.
/// <para>
/// One shared input struct serves the <c>ValidationPropertyTests</c> suite; each property
/// reads only the fields that vary its own domain:
/// <list type="bullet">
/// <item><see cref="FileCount"/> and <see cref="MutationType"/> drive single-fault injection.</item>
/// <item><see cref="PriorStart"/>/<see cref="PriorLength"/> and
/// <see cref="PlannedStart"/>/<see cref="PlannedLength"/> drive supplemental interval
/// generation (<see cref="PlannedStart"/> is generated as a small offset from
/// <see cref="PriorStart"/> so overlap, adjacency, and disjoint cases all appear).</item>
/// <item><see cref="SecondBaselineGap"/>, <see cref="Placement"/>, and
/// <see cref="PlacementOffset"/> drive the supplemental interval placement so every
/// branch of the iff-contract is exercised instead of relying on uniform draws.</item>
/// <item><see cref="PrefixCount"/>, <see cref="BatesStarts"/>, <see cref="SequenceNumber"/>, <see cref="UseRestartMode"/>, and <see cref="UseExplicitStarts"/> drive rolling Bates resolution.</item>
/// <item><see cref="VolumeSeed"/> seeds Volume Analyzer record generation.</item>
/// </list>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ValidationMutationPropertyInput(
    int FileCount,
    int MutationType,
    int PriorStart,
    int PriorLength,
    int PlannedStart,
    int PlannedLength,
    int SecondBaselineGap,
    int Placement,
    int PlacementOffset,
    int PrefixCount,
    int[] BatesStarts,
    int SequenceNumber,
    bool UseRestartMode,
    bool UseExplicitStarts,
    int VolumeSeed);

/// <summary>
/// Bounded input for Bates Number sequence and range property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BatesPropertyInput(
    string Prefix,
    long Start,
    int Digits,
    int Count);

/// <summary>
/// Bounded input for distribution and folder-allocation property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct DistributionPropertyInput(
    int TotalFiles,
    int FolderCount,
    int Seed);

/// <summary>
/// Bounded input for Load File record serialization and parsing round-trip property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct DelimitedRecordPropertyInput(
    char ColumnDelimiter,
    char QuoteDelimiter,
    char NewlineDelimiter,
    string[] Columns,
    string[] Values);

/// <summary>
/// Bounded input for path validation and sanitization property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PathSegmentPropertyInput(
    string RawText,
    char ReplacementChar);

/// <summary>
/// Bounded entry specification for Archive Test Fixture generation property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ArchiveEntrySpec(
    string Name,
    string CompressionMethod,
    int LocalHeaderMethod,
    int CentralDirectoryMethod,
    string PayloadCodec,
    int PayloadLength);

/// <summary>
/// Bounded input for Archive Test Fixture layout, mutation, semantics, and module property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ArchiveFixturePropertyInput(
    string SuiteName,
    string RawSuiteInput,
    bool IsCanonicalSuite,
    int Seed,
    int SingleMutationCaseIndex,
    int SuiteCaseIndex,
    ArchiveEntrySpec[] Entries);

/// <summary>
/// Bounded input for Ordering Window admission, blocking, and cancellation property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct OrderingWindowPropertyInput(
    int WindowSize,
    int ItemCount,
    int[] CompletionOrder,
    int CommitsBeforeProbe);

/// <summary>
/// Bounded CLI flag combination for cross-cutting rule exclusivity property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct CliFlagCombinationPropertyInput(
    bool HasFileType,
    bool HasFileTypes,
    bool HasCount,
    bool LoadfileOnly,
    bool ProductionSet,
    bool RedactedProduction,
    bool HasBatesPrefix,
    bool HasColumnProfile,
    bool HasSourceInput,
    bool HasSourcePathMode);

/// <summary>
/// Bounded File Data projection for the disk-backed spool serializer round-trip property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct FileDataPropertyInput(
    long Index,
    int FolderNumber,
    string FolderName,
    string FileName,
    string FilePathInZip,
    string FileType,
    int DataLength,
    int PageCount,
    string Hash,
    bool HasHashes,
    HashAlgorithm HashAlgorithm,
    string HashValue,
    bool HasAttachment,
    string AttachmentFilename,
    int AttachmentLength,
    bool HasEmail,
    bool HasCc,
    string EmailRecipient,
    string EmailSender,
    string EmailSubject,
    long SentDateTicks,
    string CcAddress,
    string ControlNumberOverride,
    string BatesNumberOverride,
    bool HasSourceMetadata,
    string[] MetadataKeys,
    string[] MetadataValues,
    int ItemCount);

/// <summary>
/// Bounded hash algorithm, payload, and seed for the HashUtility property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HashPropertyInput(
    HashAlgorithm Algorithm,
    byte[] Data,
    int Seed);

/// <summary>
/// Bounded strict-prefix delimiter flag and value for the DelimiterModule build property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct StrictDelimiterPropertyInput(
    int RoleIndex,
    string Value);

/// <summary>
/// Bounded legacy (non-prefixed) delimiter flag and value for the DelimiterModule build
/// property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LegacyDelimiterPropertyInput(
    int RoleIndex,
    string Value);

/// <summary>
/// Bounded End-of-Line flag value plus the mode flags that gate it, for the DelimiterModule
/// build property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct EolDelimiterPropertyInput(
    string Eol,
    bool LoadfileOnly,
    bool ProductionSet);

/// <summary>
/// Bounded seed-derived values for the chaos applier and column generator property tests.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ChaosSeedPropertyInput(
    int Seed,
    int RecordOrdinal,
    int LineNumber,
    int DataLength,
    int PageCount,
    int FileOrdinal);

/// <summary>
/// Shared FsCheck Arbitrary registrations for Zipper domain property-based tests.
/// <para>
/// Seed ranges are drawn from <c>0</c> (inclusive) so they match the CLI's documented
/// non-negative <c>--seed</c> contract and stay inside the domain the seed shrinkers
/// explore: a shrinker that halves a seed also yields <c>0</c>, and a counterexample that
/// lands on <c>0</c> is a value the generator can produce. No production path treats
/// <c>0</c> differently from any other seed — every consumer branches on
/// <c>Metadata.Seed.HasValue</c>, never on the seed being positive.
/// </para>
/// </summary>
public static class ZipperArbitraries
{
    private static readonly string[] SamplePrefixes =
    [
        string.Empty,
        "DOC",
        "BATES",
        "PROD_",
        "CLIENT-01-",
        "A",
        "Z99_"
    ];

    private static readonly (char Col, char Quote, char Newline)[] ValidDelimiterTriples =
    [
        ('\x14', '\xfe', '\xae'),
        ('|', '^', '~'),
        (',', '"', ';'),
        ('\t', '\'', '`')
    ];

    public static Arbitrary<BatesPropertyInput> BatesPropertyInputArb()
    {
        var gen =
            from prefix in Gen.Elements(SamplePrefixes)
            from start in Gen.Choose(0, 500_000).Select(x => (long)x)
            from digits in Gen.Choose(1, 12)
            from count in Gen.Choose(1, 250)
            select new BatesPropertyInput(prefix, start, digits, count);

        return Arb.From(gen, ShrinkBatesInput);
    }

    public static Arbitrary<DistributionPropertyInput> DistributionPropertyInputArb()
    {
        var gen =
            from totalFiles in Gen.Choose(1, 500)
            from folderCount in Gen.Choose(1, 50)
            from seed in Gen.Choose(0, 100_000)
            select new DistributionPropertyInput(totalFiles, folderCount, seed);

        return Arb.From(gen, ShrinkDistributionInput);
    }

    public static Arbitrary<DelimitedRecordPropertyInput> DelimitedRecordPropertyInputArb()
    {
        var cellChars = new[]
        {
            'a', 'b', 'c', '0', '1', ' ', '_', '-', '/', '\\', '.', ':',
            '\x14', '\xfe', '\xae', '|', '^', '~', ',', '"', ';', '\t', '\'',
            '\r', '\n', 'é', 'Ω'
        };

        var cellGen =
            from len in Gen.Choose(0, 24)
            from chars in Gen.ArrayOf(Gen.Elements(cellChars), len)
            select new string(chars);

        var gen =
            from delims in Gen.Elements(ValidDelimiterTriples)
            from colCount in Gen.Choose(1, 8)
            from rawValues in Gen.ArrayOf(cellGen, colCount)
            let columns = Enumerable.Range(1, colCount)
                .Select(i => $"COL_{i}")
                .ToArray()
            select new DelimitedRecordPropertyInput(
                delims.Col,
                delims.Quote,
                delims.Newline,
                columns,
                rawValues);

        return Arb.From(gen, ShrinkDelimitedRecordInput);
    }

    public static Arbitrary<PathSegmentPropertyInput> PathSegmentPropertyInputArb()
    {
        var alphabet = new[]
        {
            'a', 'Z', '0', '9', '_', '-', ' ', '.', '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0', '\t', '\n'
        };

        var rawGen = Gen.OneOf(
            from len in Gen.Choose(0, 32)
            from chars in Gen.ArrayOf(Gen.Elements(alphabet), len)
            select new string(chars),
            Gen.Elements(
                "CON", "PRN", "AUX", "NUL", "COM1", "COM9", "LPT1", "LPT9",
                "con.txt", "aux.pdf", "..", ".", "../secret", "trailing.", "trailing ",
                "valid_name", "Folder-01", "COM\u00b9", "LPT\u00b2", "LPT\u00b3"));

        var gen =
            from raw in rawGen
            from replacement in Gen.Elements('_', '-')
            select new PathSegmentPropertyInput(raw, replacement);

        return Arb.From(gen, ShrinkPathSegmentInput);
    }

    private static readonly (string Codec, int WireCode)[] SupportedArchiveCodecs =
    [
        ("stored", 0),
        ("deflate", 8),
        ("deflate64", 9),
        ("bzip2", 12)
    ];

    private static readonly string[] CanonicalSuites = [.. ArchiveTestCatalog.SupportedSuites];

    private static readonly string[] NonCanonicalSuites =
    [
        "unknown",
        "invalid_suite",
        "fuzz",
        "smoke_v2",
        "all-suites",
        "none",
        "   "
    ];

    /// <summary>
    /// Malformed-suite Archive Test cases that carry exactly one mutation and a control case.
    /// Shared with <c>ArchiveFixturePropertyTests</c> so the generator can draw a direct case
    /// index instead of folding a wide seed through a modulo reduction.
    /// </summary>
    internal static readonly IReadOnlyList<ArchiveTestCaseDefinition> SingleMutationMalformedCases =
        ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.MalformedSuite)
            .Where(definition => definition.Mutations is { Count: 1 } && definition.ControlCaseKey is not null)
            .ToList();

    /// <summary>
    /// Number of Archive Test cases in each supported suite. Used to draw a case index that is
    /// already in range for the selected suite.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> SuiteCaseCounts =
        ArchiveTestCatalog.SupportedSuites.ToDictionary(
            suite => suite,
            suite => ArchiveTestCatalog.ListSuite(suite).Count,
            StringComparer.Ordinal);

    /// <summary>
    /// Strict-prefix delimiter values exercised by the delimiter property tests. The pool mixes
    /// valid <c>ascii:</c>/<c>char:</c> prefixes, out-of-range and malformed prefixes, the
    /// quote-only <c>none</c> keyword, and the empty value that means "flag not supplied".
    /// </summary>
    private static readonly string[] StrictDelimiterValues =
    [
        "ascii:0", "ascii:20", "ascii:65", "ascii:254", "ascii:255",
        "ascii:256", "ascii:-1", "ascii:abc", "ascii:",
        "char:;", "char:z", "char:1", "char:", "char",
        "none", "NONE", "None",
        "20", ";", string.Empty,
    ];

    /// <summary>
    /// Legacy (non-prefixed) delimiter values exercised by the delimiter property tests. The pool
    /// mixes escape sequences, numeric codes, out-of-range codes, and multi-character values.
    /// </summary>
    private static readonly string[] LegacyDelimiterValues =
    [
        "\\t", "\\n", "\\r", "\\r\\n",
        "\t", "\n", "\r",
        "0", "20", "65", "255", "256", "-1",
        "abc", ";", "|", "NONE", string.Empty,
    ];

    public static Arbitrary<ArchiveEntrySpec> ArchiveEntrySpecArb()
    {
        var gen =
            from codecInfo in Gen.Elements(SupportedArchiveCodecs)
            from payloadLen in Gen.Choose(8, 64)
            select new ArchiveEntrySpec(
                $"file_{codecInfo.Codec}.bin",
                codecInfo.Codec,
                codecInfo.WireCode,
                codecInfo.WireCode,
                codecInfo.Codec,
                payloadLen);

        return Arb.From(gen, ShrinkArchiveEntrySpec);
    }

    public static Arbitrary<ArchiveFixturePropertyInput> ArchiveFixturePropertyInputArb()
    {
        var entrySpecGen =
            from codecInfo in Gen.Elements(SupportedArchiveCodecs)
            from payloadLen in Gen.Choose(8, 64)
            select (codecInfo.Codec, codecInfo.WireCode, payloadLen);

        var entriesGen =
            from count in Gen.Choose(1, 4)
            from specs in Gen.ArrayOf(entrySpecGen, count)
            select specs.Select((s, index) => new ArchiveEntrySpec(
                $"entry_{index}_{s.Codec}.dat",
                s.Codec,
                s.WireCode,
                s.WireCode,
                s.Codec,
                s.payloadLen)).ToArray();

        var canonicalSuiteGen =
            from suite in Gen.Elements(CanonicalSuites)
            from casing in Gen.Elements(0, 1, 2, 3)
            let cased = casing switch
            {
                1 => suite.ToUpperInvariant(),
                2 => char.ToUpperInvariant(suite[0]) + suite[1..],
                3 => suite.Length > 2 ? char.ToUpperInvariant(suite[0]) + suite[1..^1] + char.ToUpperInvariant(suite[^1]) : suite.ToUpperInvariant(),
                _ => suite.ToLowerInvariant(),
            }
            select (Suite: suite, Raw: cased, IsCanonical: true);

        var nonCanonicalSuiteGen =
            from suite in Gen.Elements(CanonicalSuites)
            from ws in Gen.Elements(1, 2, 3)
            from invalidName in Gen.Elements(NonCanonicalSuites)
            from raw in Gen.Elements(
                invalidName,
                ws switch
                {
                    1 => $"  {suite}  ",
                    2 => $"\t{suite}\n",
                    _ => $" {suite}",
                })
            select (Suite: suite, Raw: raw, IsCanonical: false);

        var suiteGen = Gen.OneOf(
            canonicalSuiteGen,
            canonicalSuiteGen,
            canonicalSuiteGen,
            canonicalSuiteGen,
            nonCanonicalSuiteGen);

        var gen =
            from suiteInfo in suiteGen
            from seed in Gen.Choose(0, 100_000)
            from singleMutationCaseIndex in Gen.Choose(0, SingleMutationMalformedCases.Count - 1)
            from suiteCaseIndex in Gen.Choose(0, SuiteCaseCounts[suiteInfo.Suite] - 1)
            from entries in entriesGen
            select new ArchiveFixturePropertyInput(
                suiteInfo.Suite,
                suiteInfo.Raw,
                suiteInfo.IsCanonical,
                seed,
                singleMutationCaseIndex,
                suiteCaseIndex,
                entries);

        return Arb.From(gen, ShrinkArchiveFixturePropertyInput);
    }

    /// <summary>
    /// Strict-prefix delimiter flags exercised by the delimiter property tests. The order is a
    /// stable draw order for the properties; it does not mirror the order
    /// <c>DelimiterModule.TryBuild</c> validates the flags in, because each property case
    /// supplies exactly one flag.
    /// </summary>
    public static readonly IReadOnlyList<string> StrictDelimiterFlags =
        Array.AsReadOnly(
            new[]
            {
                "--col-delim",
                "--quote-delim",
                "--newline-delim",
                "--multi-delim",
                "--nested-delim",
            });

    /// <summary>
    /// Legacy (non-prefixed) delimiter flags exercised by the delimiter property tests.
    /// </summary>
    public static readonly IReadOnlyList<string> LegacyDelimiterFlags =
        Array.AsReadOnly(
            new[]
            {
                "--delimiter-column",
                "--delimiter-quote",
                "--delimiter-newline",
            });

    public static Arbitrary<OrderingWindowPropertyInput> OrderingWindowPropertyInputArb()
    {
        var gen =
            from windowSize in Gen.Choose(1, 6)
            from itemCount in Gen.Choose(1, 12)
            from keys in Gen.ArrayOf(Gen.Choose(0, 1_000_000), itemCount)
            from commitsBeforeProbe in Gen.Choose(0, itemCount)
            let completionOrder = Enumerable.Range(1, itemCount)
                .OrderBy(index => keys[index - 1])
                .ToArray()
            select new OrderingWindowPropertyInput(windowSize, itemCount, completionOrder, commitsBeforeProbe);

        return Arb.From(gen, ShrinkOrderingWindowInput);
    }

    public static Arbitrary<CliFlagCombinationPropertyInput> CliFlagCombinationPropertyInputArb()
    {
        var gen =
            from hasFileType in Gen.Elements(true, false)
            from hasFileTypes in Gen.Elements(true, false)
            from hasCount in Gen.Elements(true, false)
            from loadfileOnly in Gen.Elements(true, false)
            from productionSet in Gen.Elements(true, false)
            from redactedProduction in Gen.Elements(true, false)
            from hasBatesPrefix in Gen.Elements(true, false)
            from hasColumnProfile in Gen.Elements(true, false)
            from hasSourceInput in Gen.Elements(true, false)
            from hasSourcePathMode in Gen.Elements(true, false)
            select new CliFlagCombinationPropertyInput(
                hasFileType,
                hasFileTypes,
                hasCount,
                loadfileOnly,
                productionSet,
                redactedProduction,
                hasBatesPrefix,
                hasColumnProfile,
                hasSourceInput,
                hasSourcePathMode);

        return Arb.From(gen, ShrinkCliFlagCombinationInput);
    }

    public static Arbitrary<FileDataPropertyInput> FileDataPropertyInputArb()
    {
        var textGen = Gen.Elements(
            string.Empty,
            "a",
            "DOC00000001.pdf",
            "folder name",
            "Ünïcødé",
            "  padded  ");

        var gen =
            from index in Gen.Choose(1, 100_000).Select(x => (long)x)
            from folderNumber in Gen.Choose(0, 999)
            from folderName in textGen
            from fileName in textGen
            from filePathInZip in textGen
            from fileType in Gen.Elements(string.Empty, "pdf", "jpg", "eml")
            from dataLength in Gen.Choose(0, 100_000)
            from pageCount in Gen.Choose(0, 5_000)
            from hash in Gen.Elements(string.Empty, "d41d8cd98f00b204e9800998ecf8427e")
            from hasHashes in Gen.Elements(true, false)
            from hashAlgorithm in Gen.Elements(HashAlgorithm.MD5, HashAlgorithm.SHA1, HashAlgorithm.SHA256)
            from hashValue in Gen.Elements(string.Empty, "abc123")
            from hasAttachment in Gen.Elements(true, false)
            from attachmentFilename in textGen
            from attachmentLength in Gen.Choose(0, 4_096)
            from hasEmail in Gen.Elements(true, false)
            from hasCc in Gen.Elements(true, false)
            from emailRecipient in textGen
            from emailSender in textGen
            from emailSubject in textGen
            from sentDateTicks in Gen.Choose(0, 2_000_000_000).Select(x => (long)x)
            from ccAddress in textGen
            from controlNumberOverride in Gen.Elements(string.Empty, "DOC00000001")
            from batesNumberOverride in Gen.Elements(string.Empty, "BATES00000001")
            from hasSourceMetadata in Gen.Elements(true, false)
            from metadataKeyDraw in Gen.ArrayOf(Gen.Elements("custodian", "author", "date-sent"))
            let metadataKeys = metadataKeyDraw.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray()
            from metadataValues in Gen.ArrayOf(textGen, metadataKeys.Length)
            from itemCount in Gen.Choose(1, 4)
            select new FileDataPropertyInput(
                index,
                folderNumber,
                folderName,
                fileName,
                filePathInZip,
                fileType,
                dataLength,
                pageCount,
                hash,
                hasHashes,
                hashAlgorithm,
                hashValue,
                hasAttachment,
                attachmentFilename,
                attachmentLength,
                hasEmail,
                hasCc,
                emailRecipient,
                emailSender,
                emailSubject,
                sentDateTicks,
                ccAddress,
                controlNumberOverride,
                batesNumberOverride,
                hasSourceMetadata,
                metadataKeys,
                metadataValues,
                itemCount);

        return Arb.From(gen, ShrinkFileDataInput);
    }

    public static Arbitrary<HashPropertyInput> HashPropertyInputArb()
    {
        var gen =
            from algorithm in Gen.Elements(HashAlgorithm.MD5, HashAlgorithm.SHA1, HashAlgorithm.SHA256)
            from length in Gen.Choose(0, 512)
            from bytes in Gen.ArrayOf(Gen.Choose(0, 255).Select(x => (byte)x), length)
            from seed in Gen.Choose(0, 1_000_000)
            select new HashPropertyInput(algorithm, bytes, seed);

        return Arb.From(gen, ShrinkHashInput);
    }

    public static Arbitrary<StrictDelimiterPropertyInput> StrictDelimiterPropertyInputArb()
    {
        var gen =
            from roleIndex in Gen.Choose(0, StrictDelimiterFlags.Count - 1)
            from value in Gen.Elements(StrictDelimiterValues)
            select new StrictDelimiterPropertyInput(roleIndex, value);

        return Arb.From(gen, ShrinkStrictDelimiterInput);
    }

    public static Arbitrary<LegacyDelimiterPropertyInput> LegacyDelimiterPropertyInputArb()
    {
        var gen =
            from roleIndex in Gen.Choose(0, LegacyDelimiterFlags.Count - 1)
            from value in Gen.Elements(LegacyDelimiterValues)
            select new LegacyDelimiterPropertyInput(roleIndex, value);

        return Arb.From(gen, ShrinkLegacyDelimiterInput);
    }

    public static Arbitrary<EolDelimiterPropertyInput> EolDelimiterPropertyInputArb()
    {
        var gen =
            from eol in Gen.Elements("CRLF", "LF", "CR", "crlf", "lf", "cr", "bogus", "CRLF ", string.Empty)
            from loadfileOnly in Gen.Elements(true, false)
            from productionSet in Gen.Elements(true, false)
            select new EolDelimiterPropertyInput(eol, loadfileOnly, productionSet);

        return Arb.From(gen, ShrinkEolDelimiterInput);
    }

    public static Arbitrary<ChaosSeedPropertyInput> ChaosSeedPropertyInputArb()
    {
        var gen =
            from seed in Gen.Choose(0, 1_000_000)
            from recordOrdinal in Gen.Choose(0, 9_999)
            from lineNumber in Gen.Choose(1, 500)
            from dataLength in Gen.Choose(128, 100_127)
            from pageCount in Gen.Choose(1, 50)
            from fileOrdinal in Gen.Choose(1, 99_999)
            select new ChaosSeedPropertyInput(seed, recordOrdinal, lineNumber, dataLength, pageCount, fileOrdinal);

        return Arb.From(gen, ShrinkChaosSeedInput);
    }

    private static IEnumerable<BatesPropertyInput> ShrinkBatesInput(BatesPropertyInput input)
    {
        if (input.Count > 1)
        {
            int halfCount = input.Count / 2;
            if (halfCount > 1)
            {
                yield return input with { Count = halfCount };
            }

            yield return input with { Count = 1 };
        }

        if (input.Start > 0)
        {
            long halfStart = input.Start / 2;
            if (halfStart > 0)
            {
                yield return input with { Start = halfStart };
            }

            yield return input with { Start = 0 };
        }

        if (input.Digits > 1)
        {
            yield return input with { Digits = Math.Max(1, input.Digits - 1) };
        }

        if (input.Prefix.Length > 0)
        {
            yield return input with { Prefix = string.Empty };
        }
    }

    private static IEnumerable<DistributionPropertyInput> ShrinkDistributionInput(DistributionPropertyInput input)
    {
        if (input.TotalFiles > 1)
        {
            int halfTotal = input.TotalFiles / 2;
            if (halfTotal > 1)
            {
                yield return input with { TotalFiles = halfTotal };
            }

            yield return input with { TotalFiles = 1 };
        }

        if (input.FolderCount > 1)
        {
            int halfFolders = input.FolderCount / 2;
            if (halfFolders > 1)
            {
                yield return input with { FolderCount = halfFolders };
            }

            yield return input with { FolderCount = 1 };
        }
    }

    private static IEnumerable<DelimitedRecordPropertyInput> ShrinkDelimitedRecordInput(DelimitedRecordPropertyInput input)
    {
        if (input.Columns.Length > 1)
        {
            int half = Math.Max(1, input.Columns.Length / 2);
            yield return input with
            {
                Columns = input.Columns[..half],
                Values = input.Values[..half],
            };
            yield return input with
            {
                Columns = input.Columns[1..],
                Values = input.Values[1..],
            };
        }

        for (int i = 0; i < input.Values.Length; i++)
        {
            if (input.Values[i].Length > 0)
            {
                var shorterValues = (string[])input.Values.Clone();
                shorterValues[i] = input.Values[i][..(input.Values[i].Length / 2)];
                yield return input with { Values = shorterValues };
            }
        }
    }

    private static IEnumerable<PathSegmentPropertyInput> ShrinkPathSegmentInput(PathSegmentPropertyInput input)
    {
        if (!string.IsNullOrEmpty(input.RawText))
        {
            if (input.RawText.Length > 2)
            {
                yield return input with { RawText = input.RawText[..(input.RawText.Length / 2)] };
            }

            if (input.RawText.Length > 1)
            {
                yield return input with { RawText = input.RawText[1..] };
                yield return input with { RawText = input.RawText[..^1] };
            }
            else
            {
                yield return input with { RawText = string.Empty };
            }
        }
    }

    private static IEnumerable<ArchiveEntrySpec> ShrinkArchiveEntrySpec(ArchiveEntrySpec spec)
    {
        if (spec.PayloadLength > 8)
        {
            yield return spec with { PayloadLength = Math.Max(8, spec.PayloadLength / 2) };
        }
    }

    private static IEnumerable<ArchiveFixturePropertyInput> ShrinkArchiveFixturePropertyInput(ArchiveFixturePropertyInput input)
    {
        if (input.Entries.Length > 1)
        {
            int half = input.Entries.Length / 2;
            if (half > 1)
            {
                yield return input with { Entries = input.Entries[..half] };
            }

            yield return input with { Entries = [input.Entries[0]] };
        }

        if (input.Entries.Length == 1 && input.Entries[0].PayloadLength > 8)
        {
            var shorter = (ArchiveEntrySpec[])input.Entries.Clone();
            shorter[0] = shorter[0] with { PayloadLength = Math.Max(8, shorter[0].PayloadLength / 2) };
            yield return input with { Entries = shorter };
        }

        if (input.Seed > 0)
        {
            int halfSeed = input.Seed / 2;
            if (halfSeed > 0)
            {
                yield return input with { Seed = halfSeed };
            }

            yield return input with { Seed = 0 };
        }

        if (input.IsCanonicalSuite && input.RawSuiteInput != input.SuiteName)
        {
            yield return input with { RawSuiteInput = input.SuiteName };
        }

        if (input.SingleMutationCaseIndex > 0)
        {
            yield return input with { SingleMutationCaseIndex = input.SingleMutationCaseIndex / 2 };
            yield return input with { SingleMutationCaseIndex = 0 };
        }

        if (input.SuiteCaseIndex > 0)
        {
            yield return input with { SuiteCaseIndex = input.SuiteCaseIndex / 2 };
            yield return input with { SuiteCaseIndex = 0 };
        }
    }

    private static IEnumerable<OrderingWindowPropertyInput> ShrinkOrderingWindowInput(OrderingWindowPropertyInput input)
    {
        if (input.WindowSize > 1)
        {
            yield return input with { WindowSize = Math.Max(1, input.WindowSize / 2) };
        }

        if (input.ItemCount > 1)
        {
            int half = input.ItemCount / 2;
            if (half > 1)
            {
                yield return input with
                {
                    ItemCount = half,
                    CompletionOrder = Enumerable.Range(1, half).ToArray(),
                    CommitsBeforeProbe = Math.Min(input.CommitsBeforeProbe, half),
                };
            }

            yield return input with
            {
                ItemCount = 1,
                CompletionOrder = [1],
                CommitsBeforeProbe = Math.Min(input.CommitsBeforeProbe, 1),
            };
        }

        if (input.CommitsBeforeProbe > 0)
        {
            yield return input with { CommitsBeforeProbe = 0 };
        }
    }

    private static IEnumerable<CliFlagCombinationPropertyInput> ShrinkCliFlagCombinationInput(CliFlagCombinationPropertyInput input)
    {
        if (input.HasFileType)
        {
            yield return input with { HasFileType = false };
        }

        if (input.HasFileTypes)
        {
            yield return input with { HasFileTypes = false };
        }

        if (input.HasCount)
        {
            yield return input with { HasCount = false };
        }

        if (input.LoadfileOnly)
        {
            yield return input with { LoadfileOnly = false };
        }

        if (input.ProductionSet)
        {
            yield return input with { ProductionSet = false };
        }

        if (input.RedactedProduction)
        {
            yield return input with { RedactedProduction = false };
        }

        if (input.HasBatesPrefix)
        {
            yield return input with { HasBatesPrefix = false };
        }

        if (input.HasColumnProfile)
        {
            yield return input with { HasColumnProfile = false };
        }

        if (input.HasSourceInput)
        {
            yield return input with { HasSourceInput = false };
        }

        if (input.HasSourcePathMode)
        {
            yield return input with { HasSourcePathMode = false };
        }
    }

    private static IEnumerable<FileDataPropertyInput> ShrinkFileDataInput(FileDataPropertyInput input)
    {
        if (input.DataLength > 0)
        {
            yield return input with { DataLength = input.DataLength / 2 };
            yield return input with { DataLength = 0 };
        }

        if (input.PageCount > 0)
        {
            yield return input with { PageCount = input.PageCount / 2 };
            yield return input with { PageCount = 0 };
        }

        if (input.AttachmentLength > 0)
        {
            yield return input with { AttachmentLength = input.AttachmentLength / 2 };
            yield return input with { AttachmentLength = 0 };
        }

        if (input.FolderNumber > 0)
        {
            yield return input with { FolderNumber = input.FolderNumber / 2 };
            yield return input with { FolderNumber = 0 };
        }

        if (input.HasAttachment)
        {
            yield return input with { HasAttachment = false };
        }

        if (input.HasEmail)
        {
            yield return input with { HasEmail = false };
        }

        if (input.HasHashes)
        {
            yield return input with { HasHashes = false };
        }

        if (input.HasSourceMetadata)
        {
            yield return input with { HasSourceMetadata = false };
        }

        if (input.ItemCount > 1)
        {
            yield return input with { ItemCount = input.ItemCount - 1 };
            yield return input with { ItemCount = 1 };
        }
    }

    private static IEnumerable<HashPropertyInput> ShrinkHashInput(HashPropertyInput input)
    {
        if (input.Data.Length > 0)
        {
            yield return input with { Data = input.Data[..(input.Data.Length / 2)] };
            yield return input with { Data = [] };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }
    }

    private static IEnumerable<StrictDelimiterPropertyInput> ShrinkStrictDelimiterInput(StrictDelimiterPropertyInput input)
    {
        if (input.RoleIndex > 0)
        {
            yield return input with { RoleIndex = input.RoleIndex / 2 };
            yield return input with { RoleIndex = 0 };
        }

        if (input.Value.Length > 0)
        {
            yield return input with { Value = input.Value[..(input.Value.Length / 2)] };
            yield return input with { Value = string.Empty };
        }
    }

    private static IEnumerable<LegacyDelimiterPropertyInput> ShrinkLegacyDelimiterInput(LegacyDelimiterPropertyInput input)
    {
        if (input.RoleIndex > 0)
        {
            yield return input with { RoleIndex = input.RoleIndex / 2 };
            yield return input with { RoleIndex = 0 };
        }

        if (input.Value.Length > 0)
        {
            yield return input with { Value = input.Value[..(input.Value.Length / 2)] };
            yield return input with { Value = string.Empty };
        }
    }

    private static IEnumerable<EolDelimiterPropertyInput> ShrinkEolDelimiterInput(EolDelimiterPropertyInput input)
    {
        if (input.Eol.Length > 0)
        {
            yield return input with { Eol = input.Eol[..(input.Eol.Length / 2)] };
            yield return input with { Eol = string.Empty };
        }

        if (input.LoadfileOnly)
        {
            yield return input with { LoadfileOnly = false };
        }

        if (input.ProductionSet)
        {
            yield return input with { ProductionSet = false };
        }
    }

    private static IEnumerable<ChaosSeedPropertyInput> ShrinkChaosSeedInput(ChaosSeedPropertyInput input)
    {
        if (input.RecordOrdinal > 0)
        {
            yield return input with { RecordOrdinal = input.RecordOrdinal / 2 };
            yield return input with { RecordOrdinal = 0 };
        }

        if (input.LineNumber > 1)
        {
            yield return input with { LineNumber = input.LineNumber / 2 };
            yield return input with { LineNumber = 1 };
        }

        if (input.DataLength > 128)
        {
            yield return input with { DataLength = Math.Max(128, input.DataLength / 2) };
        }

        if (input.PageCount > 1)
        {
            yield return input with { PageCount = input.PageCount / 2 };
            yield return input with { PageCount = 1 };
        }

        if (input.FileOrdinal > 1)
        {
            yield return input with { FileOrdinal = input.FileOrdinal / 2 };
            yield return input with { FileOrdinal = 1 };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }
    }

    public static Arbitrary<MetadataFlagCombinationInput> MetadataFlagCombinationInputArb()
    {
        var profiles = new[]
        {
            null,
            "minimal",
            "standard",
            "litigation",
            "full",
            "legacywithmetadata",
            "legacyeml",
            "legacywithcollectionmetadata",
        };

        var prefixes = new[] { "DOC", "BATES", "PROD_", string.Empty };

        var gen =
            from meta in Gen.Elements(true, false)
            from custodian in Gen.Elements(true, false)
            from dateRange in Gen.Elements(true, false)
            from fileSize in Gen.Elements(true, false)
            from pageCount in Gen.Elements(true, false)
            from hash in Gen.Elements(true, false)
            from nativePath in Gen.Elements(true, false)
            from textPath in Gen.Elements(true, false)
            from emailFields in Gen.Elements(true, false)
            from compression in Gen.Elements(true, false)
            from profile in Gen.Elements(profiles)
            from hasBates in Gen.Elements(true, false)
            from batesPrefix in Gen.Elements(prefixes)
            from batesStart in Gen.Choose(0, 50_000)
            from batesDigits in Gen.Choose(1, 10)
            from isProd in Gen.Elements(true, false)
            from volSize in Gen.Choose(100, 5000)
            from rollingMode in Gen.Elements(RollingBatesMode.Continuous, RollingBatesMode.Restart)
            from attachmentRate in Gen.Choose(0, 100)
            from fileCount in Gen.Choose(1, 10)
            from seed in Gen.Choose(0, 100_000)
            let bates = hasBates
                ? new BatesNumberConfig { Prefix = batesPrefix, Start = batesStart, Digits = batesDigits }
                : null
            let prod = isProd
                ? new ProductionConfig { ProductionSet = true, VolumeSize = volSize, RollingBatesMode = rollingMode }
                : null
            select new MetadataFlagCombinationInput(
                meta, custodian, dateRange, fileSize, pageCount, hash,
                nativePath, textPath, emailFields, compression, profile,
                bates, prod, attachmentRate, fileCount, seed);

        return Arb.From(gen, ShrinkMetadataFlagCombinationInput);
    }

    public static Arbitrary<EmitterPropertyInput> EmitterPropertyInputArb()
    {
        var encodings = new[] { "UTF-8", "UTF-16LE", "ANSI", "ASCII" };
        var eols = new[] { "CRLF", "LF", "CR" };

        var gen =
            from enc in Gen.Elements(encodings)
            from bom in Gen.Elements(true, false)
            from eol in Gen.Elements(eols)
            from hasHeader in Gen.Elements(true, false)
            from recordCount in Gen.Choose(0, 10)
            from colCount in Gen.Choose(1, 5)
            from seed in Gen.Choose(0, 100_000)
            select new EmitterPropertyInput(enc, bom, eol, hasHeader, recordCount, colCount, seed);

        return Arb.From(gen, ShrinkEmitterPropertyInput);
    }

    public static Arbitrary<XmlLoadFilePropertyInput> XmlLoadFilePropertyInputArb()
    {
        var sampleStrings = new[]
        {
            "Normal Text",
            "Special & < > \" ' Characters",
            "Unicode: 日本語, Español (ñ), Greek (Ω)",
            "Whitespace: \t spaces and \r\n newlines",
            string.Empty,
            "Control Chars: \t \n \r",
            "A &amp; B &lt; C &gt; D",
        };

        var gen =
            from count in Gen.Choose(1, 8)
            from withFamilies in Gen.Elements(true, false)
            from withText in Gen.Elements(true, false)
            from withHash in Gen.Elements(true, false)
            from rate in Gen.Choose(0, 100)
            from seed in Gen.Choose(0, 100_000)
            from meta in Gen.Elements(sampleStrings)
            select new XmlLoadFilePropertyInput(count, withFamilies, withText, withHash, rate, seed, meta);

        return Arb.From(gen, ShrinkXmlLoadFileInput);
    }

    public static Arbitrary<FamilyPlanPropertyInput> FamilyPlanPropertyInputArb()
    {
        var gen =
            from count in Gen.Choose(1, 20)
            from rate in Gen.Choose(0, 100).Select(x => (double)x)
            from seed in Gen.Choose(0, 100_000)
            select new FamilyPlanPropertyInput(count, rate, seed);

        return Arb.From(gen, ShrinkFamilyPlanInput);
    }

    public static Arbitrary<AuditWriterPropertyInput> AuditWriterPropertyInputArb()
    {
        var gen =
            from count in Gen.Choose(1, 15)
            from rate in Gen.Choose(0, 100)
            from withFamilies in Gen.Elements(true, false)
            from isLoadfileOnly in Gen.Elements(true, false)
            from fmt in Gen.Elements(Enum.GetValues<LoadFileFormat>())
            from seed in Gen.Choose(0, 100_000)
            select new AuditWriterPropertyInput(count, rate, withFamilies, isLoadfileOnly, fmt, seed);

        return Arb.From(gen, ShrinkAuditWriterInput);
    }

    /// <summary>
    /// Number of single-fault categories <c>ValidationPropertyTests</c> can inject into a
    /// standard-mode output (PostGenerationValidator property). The property folds the
    /// generator's wider <c>MutationType</c> draw onto this count with a modulus, so it must
    /// stay equal to the property's fault-injection switch arity.
    /// </summary>
    public const int PostGenerationFaultCount = 4;

    /// <summary>
    /// Number of single-fault categories <c>ValidationPropertyTests</c> can inject into a
    /// synthetic Production Set (ProductionSetPostValidator property). Shared with the
    /// generator so the modulus in the property and this range cannot drift apart.
    /// </summary>
    public const int ProductionSetFaultCount = 15;

    public static Arbitrary<ValidationMutationPropertyInput> ValidationMutationPropertyInputArb()
    {
        var gen =
            from fileCount in Gen.Choose(2, 6)
            from mutationType in Gen.Choose(0, ProductionSetFaultCount - 1)
            from priorStart in Gen.Choose(1, 50_000)
            from priorLength in Gen.Choose(0, 50)
            from plannedOffset in Gen.Choose(-60, 60)
            from plannedLength in Gen.Choose(0, 50)
            from secondBaselineGap in Gen.Choose(0, 2)
            from placement in Gen.Choose(0, 3)
            from placementOffset in Gen.Choose(0, priorLength)
            from prefixCount in Gen.Choose(1, 6)
            from rawStarts in Gen.ArrayOf(Gen.Choose(1, 50_000), prefixCount)
            from sequenceNumber in Gen.Choose(1, prefixCount)
            from useRestartMode in Gen.Elements(true, false)
            from useExplicitStarts in Gen.Elements(true, false)
            from volumeSeed in Gen.Choose(0, 999_999)
            select new ValidationMutationPropertyInput(
                fileCount,
                mutationType,
                priorStart,
                priorLength,
                Math.Max(1, priorStart + plannedOffset),
                plannedLength,
                secondBaselineGap,
                placement,
                placementOffset,
                prefixCount,
                ToStrictlyIncreasing(rawStarts),
                sequenceNumber,
                useRestartMode,
                useExplicitStarts,
                volumeSeed);

        return Arb.From(gen, ShrinkValidationMutationInput);
    }

    /// <summary>
    /// Sorts the raw per-volume starts and adds a fixed spacing so every element is
    /// strictly greater than its predecessor. Rolling volume Bates transitions are only
    /// observable when consecutive volumes cannot collide, so the generator enforces the
    /// precondition instead of leaving the test to discard non-increasing draws.
    /// </summary>
    private static int[] ToStrictlyIncreasing(int[] rawStarts)
    {
        var sorted = (int[])rawStarts.Clone();
        Array.Sort(sorted);
        for (var i = 0; i < sorted.Length; i++)
        {
            sorted[i] += i * 1_000;
        }

        return sorted;
    }

    public static Arbitrary<CodedGeneratorPropertyInput> CodedGeneratorPropertyInputArb()
    {
        var samplePools = new[]
        {
            new[] { "Alpha", "Beta", "Gamma", "Delta" },
            new[] { "Red", "Green", "Blue", "Yellow", "Orange" },
            new[] { "CatA", "CatB" },
            new[] { "SoleCategory" },
            new[] { "Dup1", "Dup1", "Dup2", "Dup2", "Unique" },
        };

        var gen =
            from pool in Gen.Elements(samplePools)
            from hasIndices in Gen.Elements(true, false)
            from multiValue in Gen.Elements(true, false)
            from minCount in Gen.Choose(1, 3)
            from maxCount in Gen.Choose(3, 5)
            from docIndex in Gen.Choose(0, 50)
            from seed in Gen.Choose(0, 100_000)
            let indices = hasIndices ? Enumerable.Range(0, 10).Select(i => (i + seed) % pool.Length).ToArray() : null
            select new CodedGeneratorPropertyInput(pool, indices, multiValue, minCount, Math.Max(minCount, maxCount), docIndex, seed);

        return Arb.From(gen, ShrinkCodedGeneratorInput);
    }

    private static IEnumerable<MetadataFlagCombinationInput> ShrinkMetadataFlagCombinationInput(MetadataFlagCombinationInput input)
    {
        if (input.FileCount > 1)
        {
            int half = input.FileCount / 2;
            if (half > 1)
            {
                yield return input with { FileCount = half };
            }

            yield return input with { FileCount = 1 };
        }

        if (input.AttachmentRate > 0)
        {
            yield return input with { AttachmentRate = input.AttachmentRate / 2 };
            yield return input with { AttachmentRate = 0 };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }

        if (input.ColumnProfile is not null)
        {
            yield return input with { ColumnProfile = null };
        }

        if (input.BatesConfig is not null)
        {
            yield return input with { BatesConfig = null };
        }

        if (input.ProductionConfig is not null)
        {
            yield return input with { ProductionConfig = null };
        }

        if (input.IncludeEmailFields)
        {
            yield return input with { IncludeEmailFields = false };
        }

        if (input.IncludePageCount)
        {
            yield return input with { IncludePageCount = false };
        }

        if (input.IncludeTextPath)
        {
            yield return input with { IncludeTextPath = false };
        }

        if (input.IncludeMetadata)
        {
            yield return input with { IncludeMetadata = false };
        }

        if (input.IncludeHash)
        {
            yield return input with { IncludeHash = false };
        }

        if (input.IncludeCompressionMethod)
        {
            yield return input with { IncludeCompressionMethod = false };
        }

        if (input.IncludeDateRange)
        {
            yield return input with { IncludeDateRange = false };
        }

        if (input.IncludeCustodian)
        {
            yield return input with { IncludeCustodian = false };
        }

        if (input.IncludeFileSize)
        {
            yield return input with { IncludeFileSize = false };
        }

        if (input.IncludeNativePath)
        {
            yield return input with { IncludeNativePath = false };
        }
    }

    private static IEnumerable<EmitterPropertyInput> ShrinkEmitterPropertyInput(EmitterPropertyInput input)
    {
        if (input.RecordCount > 0)
        {
            yield return input with { RecordCount = input.RecordCount / 2 };
            yield return input with { RecordCount = 0 };
        }

        if (input.ColumnCount > 1)
        {
            yield return input with { ColumnCount = Math.Max(1, input.ColumnCount / 2) };
            yield return input with { ColumnCount = 1 };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }

        if (input.HasHeader)
        {
            yield return input with { HasHeader = false };
        }
    }

    private static IEnumerable<XmlLoadFilePropertyInput> ShrinkXmlLoadFileInput(XmlLoadFilePropertyInput input)
    {
        if (input.FileCount > 1)
        {
            yield return input with { FileCount = input.FileCount / 2 };
            yield return input with { FileCount = 1 };
        }

        if (input.AttachmentRate > 0)
        {
            yield return input with { AttachmentRate = input.AttachmentRate / 2 };
            yield return input with { AttachmentRate = 0 };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }

        if (input.WithFamilies)
        {
            yield return input with { WithFamilies = false };
        }

        if (input.WithText)
        {
            yield return input with { WithText = false };
        }

        if (input.WithHash)
        {
            yield return input with { WithHash = false };
        }
    }

    private static IEnumerable<FamilyPlanPropertyInput> ShrinkFamilyPlanInput(FamilyPlanPropertyInput input)
    {
        if (input.FileCount > 1)
        {
            int half = input.FileCount / 2;
            if (half > 1)
            {
                yield return input with { FileCount = half };
            }

            yield return input with { FileCount = 1 };
        }

        if (input.AttachmentRate > 0.0)
        {
            yield return input with { AttachmentRate = input.AttachmentRate / 2.0 };
            yield return input with { AttachmentRate = 0.0 };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }
    }

    private static IEnumerable<AuditWriterPropertyInput> ShrinkAuditWriterInput(AuditWriterPropertyInput input)
    {
        if (input.FileCount > 1)
        {
            int half = input.FileCount / 2;
            if (half > 1)
            {
                yield return input with { FileCount = half };
            }

            yield return input with { FileCount = 1 };
        }

        if (input.AttachmentRate > 0)
        {
            yield return input with { AttachmentRate = input.AttachmentRate / 2 };
            yield return input with { AttachmentRate = 0 };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }

        if (input.WithFamilies)
        {
            yield return input with { WithFamilies = false };
        }

        if (input.IsLoadfileOnly)
        {
            yield return input with { IsLoadfileOnly = false };
        }
    }

    private static IEnumerable<CodedGeneratorPropertyInput> ShrinkCodedGeneratorInput(CodedGeneratorPropertyInput input)
    {
        if (input.Categories.Length > 2)
        {
            var shorter = input.Categories[..(input.Categories.Length / 2)];
            yield return input with
            {
                Categories = shorter,
                DistributionIndices = input.DistributionIndices?
                    .Select(i => i % shorter.Length)
                    .ToArray(),
            };
        }

        if (input.DocumentIndex > 0)
        {
            yield return input with { DocumentIndex = input.DocumentIndex / 2 };
            yield return input with { DocumentIndex = 0 };
        }

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }
    }

    private static IEnumerable<ValidationMutationPropertyInput> ShrinkValidationMutationInput(ValidationMutationPropertyInput input)
    {
        if (input.FileCount > 2)
        {
            int half = input.FileCount / 2;
            if (half >= 2)
            {
                yield return input with { FileCount = half };
            }

            yield return input with { FileCount = 2 };
        }

        if (input.MutationType > 0)
        {
            yield return input with { MutationType = input.MutationType / 2 };
            yield return input with { MutationType = 0 };
        }

        if (input.PrefixCount > 1)
        {
            int half = input.PrefixCount / 2;
            if (half > 1)
            {
                yield return input with
                {
                    PrefixCount = half,
                    BatesStarts = input.BatesStarts[..half],
                    SequenceNumber = Math.Min(input.SequenceNumber, half),
                };
            }

            yield return input with
            {
                PrefixCount = 1,
                BatesStarts = input.BatesStarts[..1],
                SequenceNumber = 1,
            };
        }

        if (input.SequenceNumber > 1)
        {
            yield return input with { SequenceNumber = input.SequenceNumber - 1 };
            yield return input with { SequenceNumber = 1 };
        }

        if (input.PriorLength > 0)
        {
            yield return input with { PriorLength = input.PriorLength - 1 };
            yield return input with { PriorLength = 0 };
        }

        if (input.PlannedLength > 0)
        {
            yield return input with { PlannedLength = input.PlannedLength - 1 };
            yield return input with { PlannedLength = 0 };
        }

        if (input.UseExplicitStarts)
        {
            yield return input with { UseExplicitStarts = false };
        }

        if (input.UseRestartMode)
        {
            yield return input with { UseRestartMode = false };
        }

        if (input.SecondBaselineGap > 0)
        {
            yield return input with { SecondBaselineGap = 0 };
        }

        if (input.Placement > 0)
        {
            yield return input with { Placement = 0 };
        }

        if (input.PlacementOffset > 0)
        {
            yield return input with { PlacementOffset = 0 };
        }

        if (input.VolumeSeed > 0)
        {
            yield return input with { VolumeSeed = input.VolumeSeed / 2 };
            yield return input with { VolumeSeed = 0 };
        }
    }
}
