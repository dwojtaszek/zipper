namespace Zipper;

/// <summary>
/// Canonical catalog of Chaos anomaly type names.
/// Single source of truth consumed by ChaosModule and ChaosEngine.
/// </summary>
internal static class ChaosAnomalyTypes
{
    public static readonly IReadOnlyList<string> DatDefault = new[]
    {
        "mixed-delimiters", "quotes", "columns", "eol", "encoding",
    };

    public static readonly IReadOnlyList<string> DatRecordOnly = new[]
    {
        "path-pointer", "field-length", "date-format", "null-byte", "trailing-empties",
    };

    public static readonly IReadOnlyList<string> Dat = DatDefault.Concat(DatRecordOnly).ToArray();

    public static readonly IReadOnlyList<string> Opt = new[]
    {
        "opt-boundary", "opt-columns", "opt-pagecount", "opt-path", "opt-batesnumber", "opt-encoding",
    };

    /// <summary>
    /// RFC 4180-specific CSV anomalies: quote-escaping divergence, embedded newlines, and BOM placement.
    /// These are deliberately different from the DAT arms — CSV uses comma+double-quote, not ASCII 20/254.
    /// </summary>
    public static readonly IReadOnlyList<string> Csv = new[]
    {
        "csv-quote-escape", "csv-embedded-newline", "csv-bom",
    };

    /// <summary>
    /// Concordance DAT anomalies: targets the Concordance-specific delimiter set (ASCII 20/254)
    /// rather than inheriting the generic DAT arms silently.
    /// </summary>
    public static readonly IReadOnlyList<string> Concordance = new[]
    {
        "concordance-delimiter", "concordance-quote-escape",
    };

    /// <summary>
    /// EDRM-XML structural anomalies injected within the <c>XmlLoadFileWriter</c> carve-out:
    /// unclosed tags, bad entity references, and encoding-declaration lies.
    /// These are applied inside <c>XmlLoadFileWriter.WriteAsync</c> and do not flow through
    /// the composer → serializer → emitter seam (architecture carve-out, ADR-0006/0007).
    /// </summary>
    public static readonly IReadOnlyList<string> EdrmXml = new[]
    {
        "xml-unclosed-tag", "xml-bad-entity", "xml-encoding-lie",
    };

    /// <summary>
    /// Coordinated cross-format anomalies spanning DAT and OPT load files in a Production Set:
    /// page-count lies, shifted Bates order, cross-file volume path mismatches, orphaned attachments,
    /// missing children, and broken parent-document pointers.
    /// </summary>
    public static class CrossFormat
    {
        public const string DesyncPagecount = "desync-pagecount";
        public const string DesyncOrder = "desync-order";
        public const string DesyncPath = "desync-path";
        public const string OrphanAttachment = "orphan-attachment";
        public const string MissingChild = "missing-child";
        public const string BrokenParent = "broken-parent";

        public static readonly IReadOnlyList<string> DesyncTypes = new[]
        {
            DesyncPagecount, DesyncOrder, DesyncPath,
        };

        public static readonly IReadOnlyList<string> FamilyTypes = new[]
        {
            OrphanAttachment, MissingChild, BrokenParent,
        };

        public static readonly IReadOnlyList<string> All = new[]
        {
            DesyncPagecount, DesyncOrder, DesyncPath,
            OrphanAttachment, MissingChild, BrokenParent,
        };
    }

    /// <summary>
    /// Returns the valid anomaly types for the given format.
    /// </summary>
    public static IReadOnlyList<string> ForFormat(LoadFileFormat format) =>
        format switch
        {
            LoadFileFormat.Opt => Opt,
            LoadFileFormat.Csv => Csv,
            LoadFileFormat.Concordance => Concordance,
            LoadFileFormat.EdrmXml => EdrmXml,
            _ => Dat,
        };
}
