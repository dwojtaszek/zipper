using System.Buffers;

namespace Zipper
{
    /// <summary>
    /// Manages memory pooling for large byte arrays to reduce GC pressure.
    /// </summary>
    public class MemoryPoolManager : IDisposable
    {
        /// <summary>
        /// Rents memory of at least the specified size.
        /// </summary>
        /// <param name="size">Minimum size in bytes.</param>
        /// <returns>IMemoryOwner&lt;byte&gt; or null if size exceeds maximum.</returns>
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
