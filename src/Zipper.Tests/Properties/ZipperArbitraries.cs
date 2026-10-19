using System.Runtime.InteropServices;
using FsCheck;
using FsCheck.Fluent;
using Zipper.ArchiveTests;

namespace Zipper.Tests.Properties;

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
}
