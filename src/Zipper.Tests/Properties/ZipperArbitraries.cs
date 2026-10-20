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
/// <item><see cref="Seed"/> plus <see cref="PriorStart"/>/<see cref="PriorLength"/> and
/// <see cref="PlannedStart"/>/<see cref="PlannedLength"/> drive supplemental interval
/// generation (<see cref="PlannedStart"/> is generated as a small offset from
/// <see cref="PriorStart"/> so overlap, adjacency, and disjoint cases all appear).</item>
/// <item><see cref="PrefixCount"/>, <see cref="BatesStarts"/>, <see cref="SequenceNumber"/>, <see cref="UseRestartMode"/>, and <see cref="UseExplicitStarts"/> drive rolling Bates resolution.</item>
/// <item><see cref="Seed"/> also seeds Volume Analyzer record generation.</item>
/// </list>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ValidationMutationPropertyInput(
    int FileCount,
    int Seed,
    int MutationType,
    int PriorStart,
    int PriorLength,
    int PlannedStart,
    int PlannedLength,
    int PrefixCount,
    int[] BatesStarts,
    int SequenceNumber,
    bool UseRestartMode,
    bool UseExplicitStarts);

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
    int MutationSelector,
    ArchiveEntrySpec[] Entries);

/// <summary>
/// Shared FsCheck Arbitrary registrations for Zipper domain property-based tests.
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
            from seed in Gen.Choose(1, 100_000)
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
            from mutationSelector in Gen.Choose(0, 9_999)
            from entries in entriesGen
            select new ArchiveFixturePropertyInput(
                suiteInfo.Suite,
                suiteInfo.Raw,
                suiteInfo.IsCanonical,
                seed,
                mutationSelector,
                entries);

        return Arb.From(gen, ShrinkArchiveFixturePropertyInput);
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

        if (input.MutationSelector > 0)
        {
            yield return input with { MutationSelector = input.MutationSelector / 2 };
            yield return input with { MutationSelector = 0 };
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
            from seed in Gen.Choose(1, 100_000)
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
            from seed in Gen.Choose(1, 100_000)
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
            from seed in Gen.Choose(1, 100_000)
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
            from seed in Gen.Choose(1, 100_000)
            select new AuditWriterPropertyInput(count, rate, withFamilies, isLoadfileOnly, fmt, seed);

        return Arb.From(gen, ShrinkAuditWriterInput);
    }

    /// <summary>
    /// Number of single-fault categories <c>ValidationPropertyTests</c> can inject into a
    /// standard-mode output (PostGenerationValidator property). Shared with the generator so
    /// the modulus in the property and this range cannot drift apart.
    /// </summary>
    public const int PostGenerationFaultCount = 4;

    /// <summary>
    /// Number of single-fault categories <c>ValidationPropertyTests</c> can inject into a
    /// synthetic Production Set (ProductionSetPostValidator property). Shared with the
    /// generator so the modulus in the property and this range cannot drift apart.
    /// </summary>
    public const int ProductionSetFaultCount = 14;

    public static Arbitrary<ValidationMutationPropertyInput> ValidationMutationPropertyInputArb()
    {
        var gen =
            from fileCount in Gen.Choose(2, 6)
            from seed in Gen.Choose(0, 100_000)
            from mutationType in Gen.Choose(0, ProductionSetFaultCount - 1)
            from priorStart in Gen.Choose(1, 50_000)
            from priorLength in Gen.Choose(0, 50)
            from plannedOffset in Gen.Choose(-60, 60)
            from plannedLength in Gen.Choose(0, 50)
            from prefixCount in Gen.Choose(1, 6)
            from rawStarts in Gen.ArrayOf(Gen.Choose(1, 50_000), prefixCount)
            from sequenceNumber in Gen.Choose(1, prefixCount)
            from useRestartMode in Gen.Elements(true, false)
            from useExplicitStarts in Gen.Elements(true, false)
            select new ValidationMutationPropertyInput(
                fileCount,
                seed,
                mutationType,
                priorStart,
                priorLength,
                Math.Max(1, priorStart + plannedOffset),
                plannedLength,
                prefixCount,
                ToStrictlyIncreasing(rawStarts),
                sequenceNumber,
                useRestartMode,
                useExplicitStarts);

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
            from seed in Gen.Choose(1, 100_000)
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

        if (input.Seed > 0)
        {
            yield return input with { Seed = input.Seed / 2 };
            yield return input with { Seed = 0 };
        }

        if (input.UseExplicitStarts)
        {
            yield return input with { UseExplicitStarts = false };
        }

        if (input.UseRestartMode)
        {
            yield return input with { UseRestartMode = false };
        }
    }
}
