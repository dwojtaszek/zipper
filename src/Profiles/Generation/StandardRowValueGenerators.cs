namespace Zipper.Profiles.Generation;

/// <summary>
/// Row-level resolution data for the profile-path standard row values in the DAT Standard
/// composer. Carries the file-context identities, overrides, and family values resolved by the
/// standard-row value generators; the Column Profile value for the current column travels on
/// <see cref="ColumnGenerationContext.ProfileValue"/> and serves as the fallback for columns
/// whose file-context value is not applicable.
/// </summary>
internal sealed record StandardRowResolution
{
    /// <summary>Control Number identity (child-aware override, else Bates identity).</summary>
    public required string ControlIdentity { get; init; }

    /// <summary>Bates identity (IdOverride, else formatted sequence / DOC-index fallback).</summary>
    public required string BatesIdentity { get; init; }

    /// <summary>Native path (FilePathOverride, else the work item's in-ZIP path).</summary>
    public required string NativePath { get; init; }

    /// <summary>File size (FileSizeOverride, else DataLength; null for child records).</summary>
    public string? FileSize { get; init; }

    /// <summary>Resolved text path for the record (empty when text output is off).</summary>
    public required string TextPath { get; init; }

    /// <summary>Page count of the parent record.</summary>
    public required int PageCount { get; init; }

    public required bool IsChild { get; init; }

    public required bool WithFamilies { get; init; }

    public required bool WithText { get; init; }

    public required string BegAttach { get; init; }

    public required string EndAttach { get; init; }

    public required string ParentDocId { get; init; }
}

/// <summary>Resolves the DOCID / CONTROLNUMBER column to the record's Control Number identity.</summary>
internal sealed class ControlNumberGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => context.StandardRow!.ControlIdentity;
}

/// <summary>Resolves the BEGBATES / ENDBATES columns to the record's Bates identity.</summary>
internal sealed class BatesNumberGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => context.StandardRow!.BatesIdentity;
}

/// <summary>Resolves the FILEPATH / NATIVEPATH columns to the record's native path.</summary>
internal sealed class NativePathGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => context.StandardRow!.NativePath;
}

/// <summary>Resolves the FILESIZE column to the record's file size, falling back to the profile value.</summary>
internal sealed class FileSizeGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => context.StandardRow!.FileSize ?? context.ProfileValue ?? string.Empty;
}

/// <summary>
/// Resolves a family column (BEGATTACH / ENDATTACH / PARENTDOCID) to its family value when
/// family columns are enabled, else the profile value.
/// </summary>
internal sealed class FamilyValueGenerator : IColumnValueGenerator
{
    private readonly Func<StandardRowResolution, string> selector;

    public FamilyValueGenerator(Func<StandardRowResolution, string> selector)
    {
        this.selector = selector;
    }

    public string Generate(ColumnGenerationContext context) =>
        context.StandardRow!.WithFamilies ? this.selector(context.StandardRow) : context.ProfileValue ?? string.Empty;
}

/// <summary>
/// Resolves the email metadata and Date Sent / Author columns to the profile value, blank on
/// child (attachment) records.
/// </summary>
internal sealed class ProfileValueUnlessChildGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.StandardRow!.IsChild ? string.Empty : context.ProfileValue ?? string.Empty;
}

/// <summary>Resolves the PAGECOUNT column to 1 on child records, else the parent's page count.</summary>
internal sealed class PageCountGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.StandardRow!.IsChild ? "1" : context.StandardRow.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Resolves the TEXTPATH column to the resolved text path when text is on, else the profile value.</summary>
internal sealed class TextPathGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.StandardRow!.WithText ? context.StandardRow.TextPath : context.ProfileValue ?? string.Empty;
}

/// <summary>
/// Registry mapping profile-path column names (looked up case-insensitively by the caller) to
/// the standard-row value generators, per ADR-0004. Column names not present here fall through
/// to hash resolution then the profile value.
/// </summary>
internal static class StandardRowValueGenerators
{
    public static readonly IReadOnlyDictionary<string, IColumnValueGenerator> ByName = Create();

    private static Dictionary<string, IColumnValueGenerator> Create()
    {
        var map = new Dictionary<string, IColumnValueGenerator>(StringComparer.OrdinalIgnoreCase);
        Add(map, new ControlNumberGenerator(), "DOCID", "CONTROLNUMBER", "CONTROL_NUMBER", "CONTROL NUMBER");
        Add(map, new BatesNumberGenerator(), "BEGBATES", "ENDBATES");
        Add(map, new NativePathGenerator(), "FILEPATH", "FILE_PATH", "FILE PATH", "NATIVEPATH", "NATIVE_PATH", "NATIVE PATH");
        Add(map, new FileSizeGenerator(), "FILESIZE", "FILE_SIZE", "FILE SIZE");
        Add(map, new FamilyValueGenerator(s => s.BegAttach), "BEGATTACH", "BEG_ATTACH", "BEG ATTACH");
        Add(map, new FamilyValueGenerator(s => s.EndAttach), "ENDATTACH", "END_ATTACH", "END ATTACH");
        Add(map, new FamilyValueGenerator(s => s.ParentDocId), "PARENTDOCID", "PARENT_DOC_ID", "PARENT DOC ID");
        Add(map, new ProfileValueUnlessChildGenerator(),
            "DATESENT", "DATE_SENT", "DATE SENT", "AUTHOR",
            "EMAILTO", "EMAIL_TO", "EMAIL TO",
            "EMAILFROM", "EMAIL_FROM", "EMAIL FROM",
            "EMAILCC", "EMAIL_CC", "EMAIL CC",
            "EMAILSUBJECT", "EMAIL_SUBJECT", "EMAIL SUBJECT",
            "EMAILSENTDATE", "EMAIL_SENT_DATE", "EMAIL SENT DATE",
            "EMAILATTACHMENT", "EMAIL_ATTACHMENT", "EMAIL ATTACHMENT");
        Add(map, new PageCountGenerator(), "PAGECOUNT", "PAGE_COUNT", "PAGE COUNT");
        Add(map, new TextPathGenerator(), "TEXTPATH", "TEXT_PATH", "TEXT PATH");
        return map;
    }

    private static void Add(Dictionary<string, IColumnValueGenerator> map, IColumnValueGenerator generator, params string[] names)
    {
        foreach (var name in names)
        {
            map[name] = generator;
        }
    }
}
