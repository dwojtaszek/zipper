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
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0)
        {
            await stream.WriteAsync(preamble);
        }

        var buffer = new StringBuilder();
        if (hasHeader)
        {
            buffer.Append(serializer.RenderHeader(headerColumns));
            buffer.Append(eol);
        }

        foreach (var record in records)
        {
            buffer.Append(serializer.RenderRecord(record));
            buffer.Append(eol);

            if (buffer.Length >= 200_000)
            {
                await stream.WriteAsync(encoding.GetBytes(buffer.ToString()));
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            await stream.WriteAsync(encoding.GetBytes(buffer.ToString()));
        }
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
        var rows = new List<(long LineNumber, string RecordId, string Line)>();
        long lineNumber = 1;
        if (hasHeader)
        {
            rows.Add((lineNumber++, "HEADER", serializer.RenderHeader(headerColumns)));
        }

        foreach (var record in records)
        {
            rows.Add((lineNumber++, record.RecordId, serializer.RenderRecord(record)));
        }

        // A MemoryStream guarantees correct byte ordering when raw encoding-anomaly bytes
        // must be inserted between text lines.
        using var memStream = new MemoryStream();
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0)
        {
            await memStream.WriteAsync(preamble);
        }

        var sb = new StringBuilder();
        foreach (var (line, recordId, originalLine) in rows)
        {
            var text = chaosEngine.ShouldIntercept(line)
                ? chaosEngine.Intercept(line, originalLine, recordId)
                : originalLine;

            sb.Append(text);
            sb.Append(eol);
            await memStream.WriteAsync(encoding.GetBytes(sb.ToString()));
            sb.Clear();

            var anomaly = chaosEngine.GetEncodingAnomaly(line, line + 1, encoding);
            if (anomaly != null)
            {
                await memStream.WriteAsync(anomaly);
            }
        }

        memStream.Position = 0;
        await memStream.CopyToAsync(stream);
    }
}
