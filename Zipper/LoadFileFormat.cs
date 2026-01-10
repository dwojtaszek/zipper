namespace Zipper;

/// <summary>
/// Defines the supported load file output formats
/// </summary>
public enum LoadFileFormat
{
    /// <summary>
    /// Default DAT format with ASCII 20/254 delimiters
    /// </summary>
    Dat,

    /// <summary>
    /// OPT (Opticon) format - tab-separated, Relativity standard
    /// </summary>
    Opt,

    /// <summary>
    /// CSV format - comma-separated values with proper escaping
    /// </summary>
    Csv,

    /// <summary>
    /// XML format - structured markup
    /// </summary>
    Xml,

    /// <summary>
    /// CONCORDANCE database format with specific delimiters
    /// </summary>
    Concordance
}
