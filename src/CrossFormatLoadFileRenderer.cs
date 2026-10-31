using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Renders the DAT and OPT Load Files to line buffers and applies a coordinated
/// cross-format Chaos Scenario across both before they are written to disk.
/// </summary>
internal static class CrossFormatLoadFileRenderer
{
    public static async Task<CrossFormatAnomalyResult> RenderAndApplyAsync(
        FileGenerationRequest datRequest,
        FileGenerationRequest optRequest,
        IReadOnlyList<FileData> records,
        Func<LoadFileFormat, WriterMode, ILoadFileWriter> createWriter,
        WriterMode writerMode,
        string datFileName,
        string optFileName,
        Encoding datEncoding,
        Encoding optEncoding,
        CancellationToken cancellationToken)
    {
        var datLines = await RenderLinesAsync(datRequest, LoadFileFormat.Dat, records, createWriter, writerMode, datEncoding, cancellationToken).ConfigureAwait(false);
        var optLines = await RenderLinesAsync(optRequest, LoadFileFormat.Opt, records, createWriter, writerMode, optEncoding, cancellationToken).ConfigureAwait(false);

        return ChaosEngine.ApplyCrossFormat(
            datLines,
            optLines,
            datRequest.Chaos.ChaosScenario,
            datRequest.Chaos.ChaosAmount,
            datRequest.Metadata.Seed,
            datRequest.Delimiters.ColumnDelimiter ?? "\u0014",
            datRequest.Delimiters.QuoteDelimiter ?? "\u00fe",
            datFileName,
            optFileName,
            datRequest.Delimiters.NewlineDelimiter);
    }

    private static async Task<List<string>> RenderLinesAsync(
        FileGenerationRequest request,
        LoadFileFormat format,
        IReadOnlyList<FileData> records,
        Func<LoadFileFormat, WriterMode, ILoadFileWriter> createWriter,
        WriterMode writerMode,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var eol = LoadFileEmitter.GetEolString(request.Delimiters?.EndOfLine!);
        using var stream = new LineBufferingStream(encoding, eol);
        var writer = createWriter(format, writerMode);
        await writer.WriteAsync(stream, request, records, null, cancellationToken).ConfigureAwait(false);
        return stream.Complete();
    }

    private sealed class LineBufferingStream : Stream
    {
        private readonly Decoder decoder;
        private readonly byte[] preamble;
        private readonly byte[] pendingPreamble;
        private readonly char[] decodedCharacters;
        private readonly List<string> lines = [];
        private readonly StringBuilder currentLine = new();
        private readonly string eol;
        private int pendingPreambleLength;
        private bool preambleResolved;
        private bool pendingCarriageReturn;
        private bool completed;

        public LineBufferingStream(Encoding encoding, string? eol = null)
        {
            this.decoder = encoding.GetDecoder();
            this.preamble = encoding.GetPreamble();
            this.pendingPreamble = new byte[this.preamble.Length];
            this.preambleResolved = this.preamble.Length == 0;
            this.decodedCharacters = new char[encoding.GetMaxCharCount(4096)];
            this.eol = string.IsNullOrEmpty(eol) ? "\r\n" : eol;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public List<string> Complete()
        {
            if (this.completed)
            {
                return this.lines;
            }

            if (!this.preambleResolved && this.pendingPreambleLength > 0)
            {
                this.preambleResolved = true;
                this.Decode(this.pendingPreamble.AsSpan(0, this.pendingPreambleLength), flush: false);
            }

            this.Decode(ReadOnlySpan<byte>.Empty, flush: true);
            if (this.pendingCarriageReturn)
            {
                this.currentLine.Append('\r');
                this.pendingCarriageReturn = false;
            }

            if (this.currentLine.Length > 0)
            {
                this.lines.Add(this.currentLine.ToString());
            }

            this.completed = true;
            return this.lines;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            this.ProcessBytes(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer) => this.ProcessBytes(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ProcessBytes(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void ProcessBytes(ReadOnlySpan<byte> bytes)
        {
            if (!this.preambleResolved)
            {
                while (!bytes.IsEmpty && !this.preambleResolved)
                {
                    byte nextByte = bytes[0];
                    bytes = bytes[1..];
                    int preambleIndex = this.pendingPreambleLength++;
                    this.pendingPreamble[preambleIndex] = nextByte;

                    if (nextByte != this.preamble[preambleIndex])
                    {
                        this.preambleResolved = true;
                        this.Decode(this.pendingPreamble.AsSpan(0, this.pendingPreambleLength), flush: false);
                    }
                    else if (this.pendingPreambleLength == this.preamble.Length)
                    {
                        this.preambleResolved = true;
                    }
                }
            }

            if (this.preambleResolved && !bytes.IsEmpty)
            {
                this.Decode(bytes, flush: false);
            }
        }

        private void Decode(ReadOnlySpan<byte> bytes, bool flush)
        {
            while (true)
            {
                this.decoder.Convert(
                    bytes,
                    this.decodedCharacters,
                    flush,
                    out int bytesUsed,
                    out int charactersUsed,
                    out bool completed);
                this.AppendCharacters(this.decodedCharacters.AsSpan(0, charactersUsed));
                bytes = bytes[bytesUsed..];

                if (completed)
                {
                    return;
                }

                if (bytesUsed == 0 && charactersUsed == 0)
                {
                    throw new InvalidOperationException("The Load File encoding decoder could not make progress.");
                }
            }
        }

        private void AppendCharacters(ReadOnlySpan<char> characters)
        {
            foreach (char character in characters)
            {
                if (this.eol == "\n")
                {
                    if (character == '\n')
                    {
                        this.lines.Add(this.currentLine.ToString());
                        this.currentLine.Clear();
                    }
                    else
                    {
                        this.currentLine.Append(character);
                    }
                }
                else if (this.eol == "\r")
                {
                    if (character == '\r')
                    {
                        this.lines.Add(this.currentLine.ToString());
                        this.currentLine.Clear();
                    }
                    else
                    {
                        this.currentLine.Append(character);
                    }
                }
                else
                {
                    if (this.pendingCarriageReturn)
                    {
                        this.pendingCarriageReturn = false;
                        if (character == '\n')
                        {
                            this.lines.Add(this.currentLine.ToString());
                            this.currentLine.Clear();
                            continue;
                        }

                        this.currentLine.Append('\r');
                    }

                    if (character == '\r')
                    {
                        this.pendingCarriageReturn = true;
                    }
                    else
                    {
                        this.currentLine.Append(character);
                    }
                }
            }
        }
    }
}
