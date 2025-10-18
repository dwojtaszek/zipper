using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Zipper
{
    /// <summary>
    /// Provides buffered writing to streams to reduce I/O overhead
    /// </summary>
    public class BufferedStreamWriter : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly int _bufferSize;
        private readonly MemoryPool<byte> _memoryPool;
        private IMemoryOwner<byte>? _buffer;
        private int _bufferPosition;
        private bool _disposed = false;

        public BufferedStreamWriter(Stream stream, int? bufferSize = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _bufferSize = bufferSize ?? PerformanceConstants.DefaultBufferSize;
            _memoryPool = MemoryPool<byte>.Shared;
            _buffer = _memoryPool.Rent(_bufferSize);
        }

        /// <summary>
        /// Writes data asynchronously using buffering
        /// </summary>
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BufferedStreamWriter));

            var remaining = data.Length;
            var offset = 0;

            while (remaining > 0)
            {
                var availableSpace = _bufferSize - _bufferPosition;

                if (remaining >= availableSpace)
                {
                    // Fill buffer and flush
                    data.Slice(offset, availableSpace).CopyTo(_buffer!.Memory[_bufferPosition..]);
                    _bufferPosition += availableSpace;
                    await FlushInternalAsync(cancellationToken);

                    offset += availableSpace;
                    remaining -= availableSpace;
                }
                else
                {
                    // Copy to buffer
                    data.Slice(offset, remaining).CopyTo(_buffer!.Memory[_bufferPosition..]);
                    _bufferPosition += remaining;
                    break;
                }
            }
        }

        /// <summary>
        /// Flushes any buffered data to the underlying stream
        /// </summary>
        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BufferedStreamWriter));

            await FlushInternalAsync(cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        private async ValueTask FlushInternalAsync(CancellationToken cancellationToken)
        {
            if (_bufferPosition > 0)
            {
                await _stream.WriteAsync(_buffer!.Memory[.._bufferPosition], cancellationToken);
                _bufferPosition = 0;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await FlushAsync();
                _buffer?.Dispose();
                _disposed = true;
            }
        }
    }
}
