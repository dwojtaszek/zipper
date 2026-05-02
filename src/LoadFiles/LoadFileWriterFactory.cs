namespace Zipper.LoadFiles;

/// <summary>
/// Factory for creating load file writers based on format type.
/// </summary>
internal static class LoadFileWriterFactory
{
    /// <summary>
    /// Creates a load file writer for the specified format.
    /// </summary>
    /// <param name="format">The desired load file format.</param>
    /// <returns>An instance of ILoadFileWriter for the specified format.</returns>
    internal static ILoadFileWriter CreateWriter(LoadFileFormat format)
    {
        return format switch
        {
            LoadFileFormat.Dat => new DatWriter(),
            LoadFileFormat.Opt => new OptWriter(),
            LoadFileFormat.Csv => new CsvWriter(),
            LoadFileFormat.Xml => new XmlLoadFileWriter(),
            LoadFileFormat.EdrmXml => new XmlLoadFileWriter(), // EDRM XML uses same writer
            LoadFileFormat.Concordance => new ConcordanceWriter(),
            _ => new DatWriter(),
        };
    }
}
