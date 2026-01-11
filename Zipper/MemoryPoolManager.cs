// <copyright file="MemoryPoolManager.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Buffers;

namespace Zipper
{
    /// <summary>
    /// Manages memory pooling for large byte arrays to reduce GC pressure.
    /// </summary>
    public class MemoryPoolManager : IDisposable
    {
        private readonly MemoryPool<byte> memoryPool;
        private bool disposed = false;

        public MemoryPoolManager()
        {
            this.memoryPool = MemoryPool<byte>.Shared;
        }

        /// <summary>
        /// Rents memory of at least the specified size.
        /// </summary>
        /// <param name="size">Minimum size in bytes.</param>
        /// <returns>IMemoryOwner.<byte> or null if size exceeds maximum</returns>
        public IMemoryOwner<byte>? Rent(int size)
        {
            if (size <= 0 || size > PerformanceConstants.MaxPoolSize)
            {
                return null;
            }

            return this.memoryPool.Rent(size);
        }

        /// <summary>
        /// Returns memory to the pool.
        /// </summary>
        public void Return(IMemoryOwner<byte> memory)
        {
            if (memory != null && !this.disposed)
            {
                memory.Dispose();
            }
        }

        public void Dispose()
        {
            this.disposed = true;
        }
    }
}
