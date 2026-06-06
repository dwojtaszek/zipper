using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes rendered load file lines to a stream. The emitter is the single I/O and chaos
/// authority for delimited formats: it owns the encoding preamble (BOM), end-of-line
/// sequence, output batching, and the chaos pipeline. Composers decide the columns and
/// values; serializers render a record to a line; the emitter puts those lines on the wire.
/// </summary>
/// <remarks>
/// The <paramref name="eol"/> is supplied by the caller rather than derived here because
/// the historical writers are inconsistent: standard (in-archive) generation used the
/// platform newline while loadfile-only, production-set, and chaos paths used the
/// configured end-of-line. That quirk is preserved for byte-for-byte output parity.
/// </remarks>
internal static class LoadFileEmitter
{
    /// <summary>
    /// Resolves the end-of-line sequence from the configured EOL specifier (defaulting to CRLF).
    /// </summary>
    public static string GetEolString(string eol) => eol?.ToUpperInvariant() switch
    {
        "LF" => "\n",
        "CR" => "\r",
        _ => "\r\n",
    };

    /// <summary>
    /// Emits an optional header followed by the records. Without a chaos engine the records
    /// are streamed lazily with a bounded buffer (no full materialization). With a chaos
    /// engine the rendered lines are materialized so line-targeted interception and
    /// cross-line encoding-anomaly bytes can be injected deterministically.
    /// </summary>
    public static async Task EmitAsync(
        Stream stream,
        ILoadFileSerializer serializer,
        IReadOnlyList<string> headerColumns,
        IEnumerable<LoadFileRecord> records,
        Encoding encoding,
        string eol,
        ChaosEngine? chaosEngine)
    {
        bool hasHeader = headerColumns is { Count: > 0 };

        if (chaosEngine == null)
        {
            await EmitStreamingAsync(stream, serializer, hasHeader, headerColumns, records, encoding, eol);
        }
        else
        {
            await EmitWithChaosAsync(stream, serializer, hasHeader, headerColumns, records, encoding, eol, chaosEngine);
        }
    }

    private static async Task EmitStreamingAsync(
        Stream stream,
        ILoadFileSerializer serializer,
        bool hasHeader,
        IReadOnlyList<string> headerColumns,
        IEnumerable<LoadFileRecord> records,
        Encoding encoding,
        string eol)
    {
        // A StreamWriter owns the encoding preamble (written once) and chunks output to the
        // underlying stream by its internal buffer, so records stream out without the whole
        // file ever being materialized in memory.
        await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);

        if (hasHeader)
        {
            await writer.WriteAsync(serializer.RenderHeader(headerColumns));
            await writer.WriteAsync(eol);
        }

        foreach (var record in records)
        {
            await writer.WriteAsync(serializer.RenderRecord(record));
            await writer.WriteAsync(eol);
        }

        await writer.FlushAsync();
    }

    private static async Task EmitWithChaosAsync(
        Stream stream,
        ILoadFileSerializer serializer,
        bool hasHeader,
        IReadOnlyList<string> headerColumns,
        IEnumerable<LoadFileRecord> records,
        Encoding encoding,
        string eol,
        ChaosEngine chaosEngine)
    {
        // Records stream lazily: raw encoding-anomaly bytes are written straight to the stream
        // after each line's encoded text, so byte ordering is correct without materializing the
        // rows or buffering the whole file (keeps chaos emit at O(1) auxiliary memory).
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0)
        {
            await stream.WriteAsync(preamble);
        }

        long lineNumber = 1;
        if (hasHeader)
        {
            await EmitChaosLineAsync(stream, chaosEngine, lineNumber, serializer.RenderHeader(headerColumns), "HEADER", encoding, eol);
            lineNumber++;
        }

        foreach (var record in records)
        {
            await EmitChaosLineAsync(stream, chaosEngine, lineNumber, serializer.RenderRecord(record), record.RecordId, encoding, eol);
            lineNumber++;
        }
    }

    private static async Task EmitChaosLineAsync(
        Stream stream,
        ChaosEngine chaosEngine,
        long lineNumber,
        string originalLine,
        string recordId,
        Encoding encoding,
        string eol)
    {
        var text = chaosEngine.ShouldIntercept(lineNumber)
            ? chaosEngine.Intercept(lineNumber, originalLine, recordId)
            : originalLine;

        await stream.WriteAsync(encoding.GetBytes(text + eol));

        var anomaly = chaosEngine.GetEncodingAnomaly(lineNumber, lineNumber + 1, encoding);
        if (anomaly != null)
        {
            await stream.WriteAsync(anomaly);
        }
    }
}
