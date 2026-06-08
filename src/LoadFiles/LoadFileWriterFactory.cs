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
    /// <param name="mode">The writer mode (Standard, Loadfile-Only, or Production Set).</param>
    /// <returns>An instance of ILoadFileWriter for the specified format.</returns>
    internal static ILoadFileWriter CreateWriter(LoadFileFormat format, WriterMode mode = WriterMode.Standard)
    {
        return format switch
        {
            LoadFileFormat.Dat => new DatComposingWriter(mode),
            LoadFileFormat.Opt => new OptComposingWriter(mode),
            LoadFileFormat.Csv => new CsvComposingWriter(),
            LoadFileFormat.EdrmXml => new XmlLoadFileWriter(), // EDRM XML uses same writer
            LoadFileFormat.Concordance => new ConcordanceComposingWriter(),
            _ => new DatComposingWriter(mode),
        };
    }
}
