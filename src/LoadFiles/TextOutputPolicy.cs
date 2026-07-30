using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Consolidates text output policy (Encoding and EOL) into a single byte-identical module.
/// </summary>
internal sealed class TextOutputPolicy
{
    public Encoding Encoding { get; }
    public string EndOfLine { get; }

    public TextOutputPolicy(FileGenerationRequest request, LoadFileFormat format, WriterMode mode, bool hasChaos)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.Encoding = ResolveEncoding(request, format);
        this.EndOfLine = ResolveEndOfLine(request, format, mode, hasChaos);
    }

    private static Encoding ResolveEncoding(FileGenerationRequest request, LoadFileFormat format)
    {
        var resolvedEncoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile?.Encoding);

        if (format == LoadFileFormat.Opt)
        {
            return (request.LoadFile?.IsEncodingExplicit == true) || resolvedEncoding.CodePage != Encoding.UTF8.CodePage
                ? resolvedEncoding
                : EncodingHelper.GetEncoding("ANSI") ?? Encoding.UTF8;
        }

        return resolvedEncoding;
    }

    private static string ResolveEndOfLine(FileGenerationRequest request, LoadFileFormat format, WriterMode mode, bool hasChaos)
    {
        if (format == LoadFileFormat.Csv || format == LoadFileFormat.Concordance)
        {
            return Environment.NewLine;
        }

        // EOL quirk preserved: standard (in-archive) generation used the platform newline,
        // every other path (loadfile-only, production, and all chaos) uses the configured EOL.
        // Source-Driven Loadfile-Only reuses the Standard composers but writes on-disk Load
        // Files, so it follows the loadfile-only rule (configured EOL).
        var inArchive = mode == WriterMode.Standard && !request.LoadfileOnly;
        return (inArchive && !hasChaos)
            ? Environment.NewLine
            : LoadFileEmitter.GetEolString(request.Delimiters?.EndOfLine!);
    }
}
