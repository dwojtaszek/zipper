namespace Zipper.Profiles.Generation;

/// <summary>
/// Registry mapping column type strings to their generator class names.
/// Every column kind has exactly one registered generator.
/// </summary>
internal static class ColumnValueGeneratorRegistry
{
    /// <summary>
    /// All registered column type strings. One entry per Column Kind.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Profile-driven kinds
        "identifier", "text", "longtext", "date", "datetime", "number", "boolean", "coded", "email",

        // Legacy metadata kinds (--with-metadata path)
        "foldercustodian", "indexcustodian", "legacydatesent", "legacydatecreated", "legacyauthor",
        "filedatasize", "randomfilesize",

        // EML column kinds
        "emailto", "emailfrom", "emailsubject", "emailsentdate", "emailattachment",

        // Synthetic email column kinds (loadfile-only / no FileData)
        "syntheticemailto", "syntheticemailfrom", "syntheticemailsubject", "syntheticemailsentdate",
    };

    public static bool IsKnownType(string type) => KnownTypes.Contains(type);
}
