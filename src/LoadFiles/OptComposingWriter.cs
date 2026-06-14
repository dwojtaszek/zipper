using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Drives the OPT format through the record seam (<see cref="OptComposer"/> +
/// <see cref="OptSerializer"/> + <see cref="LoadFileEmitter"/>), replacing the fat
/// <c>OptWriter</c>. OPT defaults to ANSI (Windows-1252) encoding and, in standard mode,
/// neither applies chaos nor supports metadata columns — both preserved here.
/// </summary>
internal sealed class OptComposingWriter : ILoadFileWriter
{
    private readonly WriterMode mode;

    internal OptComposingWriter(WriterMode mode = WriterMode.Standard)
    {
        this.mode = mode;
    }

    public string FormatName => this.mode switch
    {
        WriterMode.LoadfileOnly => "OPT (Image)",
        WriterMode.ProductionSet => "Production Set OPT",
        _ => "OPT",
    };

    public string FileExtension => ".opt";

    public async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null,
        CancellationToken cancellationToken = default)
    {
        if (this.mode == WriterMode.Standard)
        {
            WarnUnsupportedStandardColumns(request);
        }

        var composer = new OptComposer(request, this.mode);
        var serializer = new OptSerializer();
        var encoding = GetOptEncoding(request);

        // Standard (in-archive) OPT used the platform newline and ignored chaos entirely;
        // loadfile-only and production used the configured EOL and applied chaos.
        var effectiveChaos = this.mode == WriterMode.Standard ? null : chaosEngine;
        var eol = this.mode == WriterMode.Standard
            ? Environment.NewLine
            : LoadFileEmitter.GetEolString(request.Delimiters.EndOfLine);

        var records = composer.Compose(processedFiles);
        await LoadFileEmitter.EmitAsync(stream, serializer, composer.HeaderColumns, records, encoding, eol, effectiveChaos, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the OPT encoding, defaulting to Windows-1252 (ANSI) when no explicit encoding is set.
    /// </summary>
    private static Encoding GetOptEncoding(FileGenerationRequest request)
    {
        var resolvedEncoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        return request.LoadFile.IsEncodingExplicit || !object.Equals(resolvedEncoding, Encoding.UTF8)
            ? resolvedEncoding
            : EncodingHelper.GetEncoding("ANSI") ?? Encoding.UTF8;
    }

    private static void WarnUnsupportedStandardColumns(FileGenerationRequest request)
    {
        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            Console.Error.WriteLine("Warning: --with-metadata columns are not supported in Opticon format. The OPT file uses the standard 7-column layout.");
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            Console.Error.WriteLine("Warning: Email metadata columns are not supported in Opticon format. The OPT file uses the standard 7-column layout.");
        }

        if (request.Output.WithText)
        {
            Console.Error.WriteLine("Warning: --with-text is not supported in Opticon format. The OPT file uses the standard 7-column layout.");
        }

        if (request.Bates != null)
        {
            Console.Error.WriteLine("Warning: The Bates number column is part of the standard Opticon 7-column format (column 1). Other Bates configuration is ignored.");
        }
    }
}
