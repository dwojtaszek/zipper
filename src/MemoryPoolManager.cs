using System.Buffers;

namespace Zipper
{
    /// Manages memory pooling for large byte arrays to reduce GC pressure.
    /// Note: This class serves as a thin pass-through wrapper around <see cref="MemoryPool{T}.Shared"/>.
    public class MemoryPoolManager : IDisposable
    {
        /// <summary>
        /// Rents memory of at least the specified size.
        /// </summary>
        /// <param name="size">Minimum size in bytes.</param>
        /// <returns>IMemoryOwner&lt;byte&gt;, or null if size is zero, negative, or exceeds maximum pool size.</returns>
        public IMemoryOwner<byte>? Rent(int size)
        {
            if (size <= 0 || size > PerformanceConstants.MaxPoolSize)
            {
                return null;
            }

            return MemoryPool<byte>.Shared.Rent(size);
        }

        public void Dispose()
        {
            // No-op: MemoryPool<byte>.Shared is a singleton and should not be disposed.
            // Individual IMemoryOwner<byte> instances are disposed by their callers.
        }
    }
}
