using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Base class for load file writers providing common column building functionality.
/// </summary>
internal abstract class LoadFileWriterBase : ILoadFileWriter
{
    public abstract string FormatName { get; }

    public abstract string FileExtension { get; }

    public abstract System.Threading.Tasks.Task WriteAsync(
        System.IO.Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null);

    /// <summary>
    /// Gets the file type in lowercase for comparisons.
    /// </summary>
    /// <returns></returns>
    protected static string GetFileTypeLower(FileGenerationRequest request) =>
        request.Output.FileTypeLower;

    /// <summary>
    /// Determines if metadata columns should be included.
    /// </summary>
    /// <returns></returns>
    protected static bool ShouldIncludeMetadata(FileGenerationRequest request) =>
        request.Metadata.ShouldIncludeMetadataColumns(request.Output);

    /// <summary>
    /// Determines if EML-specific columns should be included.
    /// </summary>
    /// <returns></returns>
    protected static bool ShouldIncludeEmlColumns(FileGenerationRequest request) =>
        request.Metadata.ShouldIncludeEmlColumns(request.Output);

    /// <summary>
    /// Determines if page count column should be included.
    /// </summary>
    /// <returns></returns>
    protected static bool ShouldIncludePageCount(FileGenerationRequest request) =>
        request.Tiff.ShouldIncludePageCount(request.Output);

    /// <summary>
    /// Generates metadata column values for a file.
    /// </summary>
    /// <returns></returns>
    protected static MetadataColumns GenerateMetadataValues(FileWorkItem workItem, FileData fileData, Random random, DateTime now, FileGenerationRequest request)
    {
        var builder = new MetadataRowBuilder(request, random, now);
        return new MetadataColumns
        {
            Custodian = builder.GetCustodian(workItem.FolderNumber),
            DateSent = builder.GetDateSent(),
            Author = builder.GetAuthor(),
            FileSize = fileData.DataLength,
        };
    }

    /// <summary>
    /// Generates EML-specific column values for a file.
    /// Uses actual EmailTemplate metadata when available for consistency with EML content.
    /// </summary>
    protected static EmlColumns GenerateEmlValues(FileWorkItem workItem, FileData fileData, Random random, DateTime now, FileGenerationRequest request)
    {
        var builder = new MetadataRowBuilder(request, random, now);
        return new EmlColumns
        {
            To = builder.GetEmailTo(workItem, fileData),
            From = builder.GetEmailFrom(workItem, fileData),
            Subject = builder.GetEmailSubject(workItem, fileData),
            SentDate = builder.GetEmailSentDate(workItem, fileData),
            Attachment = builder.GetEmailAttachment(fileData),
        };
    }

    /// <summary>
    /// Generates the Bates number for a file.
    /// </summary>
    /// <returns></returns>
    protected static string GenerateBatesNumber(FileGenerationRequest request, FileWorkItem workItem)
    {
        return request.Bates != null
            ? BatesNumberGenerator.Generate(request.Bates, workItem.Index - 1)
            : string.Empty;
    }

    /// <summary>
    /// Generates the extracted text file path for a file.
    /// </summary>
    /// <returns></returns>
    protected static string GenerateTextPath(FileGenerationRequest request, FileWorkItem workItem)
    {
        return workItem.FilePathInZip.Replace($".{request.Output.FileType}", ".txt");
    }

    /// <summary>
    /// Generates the document ID for a file.
    /// </summary>
    /// <returns></returns>
    protected static string GenerateDocumentId(FileWorkItem workItem) => $"DOC{workItem.Index:D8}";

    /// <summary>
    /// Escapes a field value for CSV/Concordance formats per RFC 4180.
    /// Wraps in quotes if the value contains comma, quote, CR, or LF characters.
    /// </summary>
    protected static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    /// <summary>
    /// Escapes a field value for Concordance DAT format using the configured quote delimiter.
    /// Doubles the quote character within the field value (e.g., þ → þþ).
    /// </summary>
    /// <param name="field">Field value to escape.</param>
    /// <param name="quoteDelimiter">The quote delimiter character (e.g., ASCII 254 þ).</param>
    /// <returns>Escaped field value.</returns>
    protected static string EscapeDatField(string field, char quoteDelimiter)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        if (field.Contains(quoteDelimiter))
        {
            return field.Replace(quoteDelimiter.ToString(), new string(quoteDelimiter, 2));
        }

        return field;
    }

    /// <summary>
    /// Gets the end-of-line string from the configured EOL specifier.
    /// </summary>
    internal static string GetEolString(string eol) => eol?.ToUpperInvariant() switch
    {
        "LF" => "\n",
        "CR" => "\r",
        _ => "\r\n",
    };

    /// <summary>
    /// Creates a StreamWriter with the configured encoding, leaving the underlying stream open.
    /// </summary>
    protected static StreamWriter CreateWriter(Stream stream, FileGenerationRequest request)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        return new StreamWriter(stream, encoding, leaveOpen: true);
    }

    /// <summary>
    /// Flushes the StringBuilder buffer to the stream in batches.
    /// </summary>
    protected static async Task FlushBufferAsync(StreamWriter writer, StringBuilder buffer)
    {
        if (buffer.Length > 0)
        {
            await writer.WriteAsync(buffer.ToString());
            buffer.Clear();
        }
    }

    /// <summary>
    /// Applies chaos interception to a line if the Chaos Engine targets this line number.
    /// </summary>
    protected static string ApplyChaosInterception(ChaosEngine? chaos, long lineNumber, string line, string recordId)
    {
        if (chaos != null && chaos.ShouldIntercept(lineNumber))
        {
            return chaos.Intercept(lineNumber, line, recordId);
        }

        return line;
    }

    /// <summary>
    /// Writes encoding anomaly bytes between lines if the Chaos Engine has an anomaly for this boundary.
    /// Returns true if bytes were written.
    /// </summary>
    protected static async Task<bool> WriteEncodingAnomalyBytesAsync(Stream stream, ChaosEngine? chaos, long lineNumber, long nextLineNumber, Encoding encoding)
    {
        if (chaos != null)
        {
            var anomaly = chaos.GetEncodingAnomaly(lineNumber, nextLineNumber, encoding);
            if (anomaly != null)
            {
                await stream.WriteAsync(anomaly);
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Holds metadata column values.
/// </summary>
internal record MetadataColumns
{
    public string Custodian { get; init; } = string.Empty;

    public string DateSent { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public long FileSize { get; init; }
}

/// <summary>
/// Holds EML-specific column values.
/// </summary>
internal record EmlColumns
{
    public string To { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string SentDate { get; init; } = string.Empty;

    public string Attachment { get; init; } = string.Empty;
}
