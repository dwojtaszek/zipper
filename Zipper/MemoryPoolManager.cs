using System;
using System.Buffers;

namespace Zipper
{
    /// <summary>
    /// Manages memory pooling for large byte arrays to reduce GC pressure
    /// </summary>
    public class MemoryPoolManager : IDisposable
    {
        private readonly MemoryPool<byte> _memoryPool;
        private bool _disposed = false;

        public MemoryPoolManager()
        {
            _memoryPool = MemoryPool<byte>.Shared;
        }

        /// <summary>
        /// Rents memory of at least the specified size
        /// </summary>
        /// <param name="size">Minimum size in bytes</param>
        /// <returns>IMemoryOwner<byte> or null if size exceeds maximum</returns>
        public IMemoryOwner<byte>? Rent(int size)
        {
            if (size <= 0 || size > PerformanceConstants.MaxPoolSize)
                return null;

            return _memoryPool.Rent(size);
        }

        /// <summary>
        /// Returns memory to the pool
        /// </summary>
        public void Return(IMemoryOwner<byte> memory)
        {
            if (memory != null && !_disposed)
            {
                memory.Dispose();
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
