using System;
using System.Buffers;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class MemoryPoolManagerTests
    {
        private readonly ITestOutputHelper _output;

        public MemoryPoolManagerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Constructor_CreatesValidInstance()
        {
            // Act
            using var manager = new MemoryPoolManager();

            // Assert
            Assert.NotNull(manager);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1024)]
        [InlineData(4096)]
        [InlineData(65536)]
        [InlineData(1048576)] // 1MB
        public void Rent_ValidSize_ReturnsMemoryOwner(int size)
        {
            // Arrange
            using var manager = new MemoryPoolManager();

            // Act
            var memoryOwner = manager.Rent(size);

            // Assert
            Assert.NotNull(memoryOwner);
            Assert.True(memoryOwner.Memory.Length >= size);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-1024)]
        [InlineData(-1048576)]
        public void Rent_InvalidSize_ReturnsNull(int size)
        {
            // Arrange
            using var manager = new MemoryPoolManager();

            // Act
            var memoryOwner = manager.Rent(size);

            // Assert
            Assert.Null(memoryOwner);
        }

        [Fact]
        public void Rent_ExceedsMaxPoolSize_ReturnsNull()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            var largeSize = PerformanceConstants.MaxPoolSize + 1;

            // Act
            var memoryOwner = manager.Rent(largeSize);

            // Assert
            Assert.Null(memoryOwner);
        }

        [Fact]
        public void Rent_MaxPoolSize_ReturnsMemoryOwner()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            var maxSize = PerformanceConstants.MaxPoolSize;

            // Act
            var memoryOwner = manager.Rent(maxSize);

            // Assert
            Assert.NotNull(memoryOwner);
            Assert.True(memoryOwner.Memory.Length >= maxSize);
        }

        [Fact]
        public void Rent_MultipleCalls_ReturnsDistinctMemoryOwners()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            const int size = 4096;

            // Act
            var owner1 = manager.Rent(size);
            var owner2 = manager.Rent(size);
            var owner3 = manager.Rent(size);

            // Assert
            Assert.NotNull(owner1);
            Assert.NotNull(owner2);
            Assert.NotNull(owner3);
            Assert.NotSame(owner1, owner2);
            Assert.NotSame(owner2, owner3);
            Assert.NotSame(owner1, owner3);

            // Cleanup
            owner1?.Dispose();
            owner2?.Dispose();
            owner3?.Dispose();
        }

        [Fact]
        public void Rent_MemoryOwner_CanWriteAndReadData()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            const int size = 1024;
            var testData = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            using var memoryOwner = manager.Rent(size);
            var memory = memoryOwner.Memory;

            // Write test data
            testData.CopyTo(memory.Span);

            // Read test data back
            var readData = new byte[testData.Length];
            memory.Span.Slice(0, testData.Length).CopyTo(readData);

            // Assert
            Assert.Equal(testData, readData);
        }

        [Fact]
        public void Rent_LargeMemoryOwner_HandlesCorrectly()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            const int size = 1024 * 1024; // 1MB
            var testPattern = new byte[256];

            // Fill pattern
            for (int i = 0; i < testPattern.Length; i++)
            {
                testPattern[i] = (byte)(i % 256);
            }

            // Act
            using var memoryOwner = manager.Rent(size);
            var memory = memoryOwner.Memory;

            // Fill memory with pattern
            var span = memory.Span;
            for (int i = 0; i < memory.Length; i += testPattern.Length)
            {
                var copyLength = Math.Min(testPattern.Length, memory.Length - i);
                testPattern.AsSpan(0, copyLength).CopyTo(span.Slice(i, copyLength));
            }

            // Assert
            // Verify first pattern
            for (int i = 0; i < testPattern.Length; i++)
            {
                Assert.Equal(testPattern[i], span[i]);
            }

            // Verify memory size
            Assert.True(memory.Length >= size);
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            var manager = new MemoryPoolManager();

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                manager.Dispose();
                manager.Dispose();
                manager.Dispose();
            });
        }

        [Fact]
        public void Rent_AfterDispose_ReturnsNull()
        {
            // Arrange
            var manager = new MemoryPoolManager();
            manager.Dispose();

            // Act
            var memoryOwner = manager.Rent(1024);

            // Assert
            Assert.Null(memoryOwner);
        }

        [Fact]
        public void MemoryPoolManager_ConcurrentAccess_ThreadSafe()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            const int threadCount = 10;
            const int operationsPerThread = 100;
            var tasks = new Task[threadCount];
            var successfulRentals = 0;

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        var memoryOwner = manager.Rent(1024);
                        if (memoryOwner != null)
                        {
                            Interlocked.Increment(ref successfulRentals);
                            memoryOwner.Dispose();
                        }
                    }
                });
            }

            Task.WaitAll(tasks);

            // Assert
            Assert.Equal(threadCount * operationsPerThread, successfulRentals);
        }

        [Fact]
        public void Rent_VariableSizes_HandlesEfficiently()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            var sizes = new[] { 64, 256, 1024, 4096, 16384, 65536 };
            var memoryOwners = new IMemoryOwner<byte>?[sizes.Length];

            // Act
            for (int i = 0; i < sizes.Length; i++)
            {
                memoryOwners[i] = manager.Rent(sizes[i]);
            }

            // Assert
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.NotNull(memoryOwners[i]);
                Assert.True(memoryOwners[i]!.Memory.Length >= sizes[i]);
                _output.WriteLine($"Requested: {sizes[i]}, Got: {memoryOwners[i]!.Memory.Length}");
            }

            // Cleanup
            foreach (var owner in memoryOwners)
            {
                owner?.Dispose();
            }
        }

        [Fact]
        public void Rent_RoundTripDataIntegrity_MaintainsCorrectData()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            const int size = 8192;
            var originalData = new byte[size];
            var random = new Random(42); // Fixed seed for reproducible tests

            // Fill with random data
            random.NextBytes(originalData);

            // Act
            using var memoryOwner = manager.Rent(size);
            var memory = memoryOwner.Memory;

            // Copy data to pooled memory
            originalData.CopyTo(memory.Span);

            // Copy data back
            var recoveredData = new byte[size];
            memory.Span.CopyTo(recoveredData);

            // Assert
            Assert.Equal(originalData, recoveredData);
        }

        [Fact]
        public void Rent_MemoryOwnerLifetime_HandlesGracefully()
        {
            // Arrange
            using var manager = new MemoryPoolManager();
            const int size = 4096;
            IMemoryOwner<byte>? memoryOwner;

            // Act
            memoryOwner = manager.Rent(size);
            Assert.NotNull(memoryOwner);

            // Write data before disposal
            var memory = memoryOwner.Memory;
            memory.Span.Fill(0xAA);

            // Dispose
            memoryOwner.Dispose();

            // Assert - After disposal, the memory owner should still be accessible
            // but the underlying memory might be returned to the pool
            // This tests that disposal doesn't crash
            Assert.True(true); // If we reach here, disposal worked correctly
        }
    }
}