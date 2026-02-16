using Xunit;

namespace Zipper
{
    public class MemoryPoolManagerTests
    {
        [Fact]
        public void RentAndReturn_ShouldReuseMemory()
        {
            var manager = new MemoryPoolManager();
            var memory = manager.Rent(1024);

            Assert.NotNull(memory);
            Assert.True(memory.Memory.Length >= 1024);

            manager.Return(memory);

            // Verify we can rent again (pool should work)
            var memory2 = manager.Rent(1024);
            Assert.NotNull(memory2);
            Assert.True(memory2.Memory.Length >= 1024);

            manager.Return(memory2);
        }

        [Fact]
        public void Rent_ExceedingMaxSize_ShouldReturnNull()
        {
            var manager = new MemoryPoolManager();
            var memory = manager.Rent((int)PerformanceConstants.MaxPoolSize + 1);

            Assert.Null(memory);
        }
    }
}
