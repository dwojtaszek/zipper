using System.Runtime.InteropServices;
using FsCheck;
using FsCheck.Fluent;

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
/// Shared FsCheck Arbitrary registrations for Zipper domain property-based tests.
/// </summary>
public static class ZipperArbitraries
{
    private static readonly string[] SamplePrefixes =
    [
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
            from start in Gen.Choose(1, 500_000).Select(x => (long)x)
            from digits in Gen.Choose(4, 12)
            from count in Gen.Choose(1, 250)
            select new BatesPropertyInput(prefix, start, digits, count);

        return gen.ToArbitrary();
    }

    public static Arbitrary<DistributionPropertyInput> DistributionPropertyInputArb()
    {
        var gen =
            from totalFiles in Gen.Choose(1, 500)
            from folderCount in Gen.Choose(1, 50)
            from seed in Gen.Choose(1, 100_000)
            select new DistributionPropertyInput(totalFiles, folderCount, seed);

        return gen.ToArbitrary();
    }

    public static Arbitrary<DelimitedRecordPropertyInput> DelimitedRecordPropertyInputArb()
    {
        var cellChars = new[]
        {
            'a', 'b', 'c', '0', '1', ' ', '_', '-', '/', '\\', '.', ':',
            '\x14', '\xfe', '\xae', '|', '^', '~', ',', '"', ';', '\t', '\'',
            '\r', '\n'
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

        return gen.ToArbitrary();
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

        return gen.ToArbitrary();
    }
}
