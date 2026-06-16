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

        return format switch
        {
            LoadFileFormat.Opt when request.LoadFile?.IsEncodingExplicit == true || resolvedEncoding.CodePage != Encoding.UTF8.CodePage => resolvedEncoding,
            LoadFileFormat.Opt => EncodingHelper.GetEncoding("ANSI") ?? Encoding.UTF8,
            _ => resolvedEncoding
        };
    }

    private static string ResolveEndOfLine(FileGenerationRequest request, LoadFileFormat format, WriterMode mode, bool hasChaos)
    {
        return (format, mode, hasChaos) switch
        {
            (LoadFileFormat.Csv or LoadFileFormat.Concordance, _, _) => Environment.NewLine,
            (_, WriterMode.Standard, false) => Environment.NewLine,
            _ => LoadFileEmitter.GetEolString(request.Delimiters?.EndOfLine!)
        };
    }
}
