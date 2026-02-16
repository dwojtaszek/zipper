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
            LoadFileFormat.Xml => new XmlWriter(),
            LoadFileFormat.EdrmXml => new XmlWriter(), // EDRM XML uses same writer
            LoadFileFormat.Concordance => new ConcordanceWriter(),
            _ => new DatWriter(),
        };
    }
}

/// <summary>
/// Wrapper for the existing LoadFileGenerator to implement ILoadFileWriter.
/// </summary>
internal class DatWriter : ILoadFileWriter
{
    public string FormatName => "DAT";

    public string FileExtension => ".dat";

    public async System.Threading.Tasks.Task WriteAsync(
        System.IO.Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles)
    {
        var encoding = Zipper.EncodingHelper.GetEncodingOrDefault(request.Encoding);

        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new System.IO.StreamWriter(stream, encoding, leaveOpen: true);
        await LoadFileGenerator.WriteLoadFileContent(writer, request, processedFiles);

        // Flush to ensure data is written
        await writer.FlushAsync();
    }
}
