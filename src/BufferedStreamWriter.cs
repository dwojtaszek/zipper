// <copyright file="BufferedStreamWriter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Buffers;

namespace Zipper
{
    /// <summary>
    /// Provides buffered writing to streams to reduce I/O overhead.
    /// </summary>
    public class BufferedStreamWriter : IAsyncDisposable
    {
        private readonly Stream stream;
        private readonly int bufferSize;
        private readonly MemoryPool<byte> memoryPool;
        private IMemoryOwner<byte>? buffer;
        private int bufferPosition;
        private bool disposed = false;

        public BufferedStreamWriter(Stream stream, int? bufferSize = null)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.bufferSize = bufferSize ?? PerformanceConstants.DefaultBufferSize;
            this.memoryPool = MemoryPool<byte>.Shared;
            this.buffer = this.memoryPool.Rent(this.bufferSize);
        }

        /// <summary>
        /// Writes data asynchronously using buffering.
        /// </summary>
        /// <returns></returns>
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(BufferedStreamWriter));
            }

            var remaining = data.Length;
            var offset = 0;

            while (remaining > 0)
            {
                var availableSpace = this.bufferSize - this.bufferPosition;

                if (remaining >= availableSpace)
                {
                    // Fill buffer and flush
                    data.Slice(offset, availableSpace).CopyTo(this.buffer!.Memory[this.bufferPosition..]);
                    this.bufferPosition += availableSpace;
                    await this.FlushInternalAsync(cancellationToken);

                    offset += availableSpace;
                    remaining -= availableSpace;
                }
                else
                {
                    // Copy to buffer
                    data.Slice(offset, remaining).CopyTo(this.buffer!.Memory[this.bufferPosition..]);
                    this.bufferPosition += remaining;
                    break;
                }
            }
        }

        /// <summary>
        /// Flushes any buffered data to the underlying stream.
        /// </summary>
        /// <returns></returns>
        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(BufferedStreamWriter));
            }

            await this.FlushInternalAsync(cancellationToken);
            await this.stream.FlushAsync(cancellationToken);
        }

        private async ValueTask FlushInternalAsync(CancellationToken cancellationToken)
        {
            if (this.bufferPosition > 0)
            {
                await this.stream.WriteAsync(this.buffer!.Memory[..this.bufferPosition], cancellationToken);
                this.bufferPosition = 0;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!this.disposed)
            {
                await this.FlushAsync();
                this.buffer?.Dispose();
                this.disposed = true;
            }
        }
    }
}
