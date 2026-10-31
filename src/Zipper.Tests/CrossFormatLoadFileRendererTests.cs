using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public sealed class CrossFormatLoadFileRendererTests
{
    [Fact]
    public async Task RenderAndApply_SplitPreamblesAndLineEndings_PreservesRenderedLines()
    {
        var datEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var optEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        const string datText = "DOCID\u0014BATES\r\nDOC1\u0014B1\nDÓC2\u0014B2\rlast";
        const string optText = "B1,VOL001,IMAGES\\VOL001\\DOC1.tif,Y,,,1\r\nB2,VOL001,IMAGES\\VOL001\\DÓC2.tif,Y,,,1\r\n";
        var request = new FileGenerationRequest
        {
            Chaos = new ChaosConfig { ChaosAmount = "0" },
        };

        var result = await CrossFormatLoadFileRenderer.RenderAndApplyAsync(
            request,
            request,
            Array.Empty<FileData>(),
            (format, _) => format == LoadFileFormat.Dat
                ? new FixedBytesLoadFileWriter(datText, datEncoding, ".dat")
                : new FixedBytesLoadFileWriter(optText, optEncoding, ".opt"),
            WriterMode.LoadfileOnly,
            "loadfile.dat",
            "loadfile.opt",
            datEncoding,
            optEncoding,
            CancellationToken.None);

        Assert.Equal(
            new[] { "DOCID\u0014BATES", "DOC1\u0014B1", "DÓC2\u0014B2", "last" },
            result.DatLines);
        Assert.Equal(
            new[]
            {
                "B1,VOL001,IMAGES\\VOL001\\DOC1.tif,Y,,,1",
                "B2,VOL001,IMAGES\\VOL001\\DÓC2.tif,Y,,,1",
            },
            result.OptLines);
    }

    private sealed class FixedBytesLoadFileWriter(string text, Encoding encoding, string extension) : ILoadFileWriter
    {
        public string FormatName => "Test";

        public string FileExtension => extension;

        public async Task WriteAsync(
            Stream stream,
            FileGenerationRequest request,
            IReadOnlyList<FileData> processedFiles,
            ChaosEngine? chaosEngine = null,
            CancellationToken cancellationToken = default)
        {
            var oneByte = new byte[1];
            foreach (byte value in encoding.GetPreamble().Concat(encoding.GetBytes(text)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                oneByte[0] = value;
                await stream.WriteAsync(oneByte.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
