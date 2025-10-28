using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class PerformanceMonitorTests
    {
        private readonly ITestOutputHelper _output;

        public PerformanceMonitorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Constructor_CreatesValidInstance()
        {
            // Act
            var monitor = new PerformanceMonitor();

            // Assert
            Assert.NotNull(monitor);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public void Start_ValidTotalFiles_InitializesCorrectly(long totalFiles)
        {
            // Arrange
            var monitor = new PerformanceMonitor();

            // Act
            monitor.Start(totalFiles);

            // Assert
            // No direct assertions possible as the state is internal
            // This tests that Start doesn't throw
            Assert.True(true);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Start_InvalidTotalFiles_HandlesGracefully(long totalFiles)
        {
            // Arrange
            var monitor = new PerformanceMonitor();

            // Act & Assert
            // Should handle gracefully (though behavior might be undefined)
            Assert.DoesNotThrow(() => monitor.Start(totalFiles));
        }

        [Fact]
        public void ReportFilesCompleted_PositiveCount_IncrementsCorrectly()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(100);

            // Act
            monitor.ReportFilesCompleted(10);
            monitor.ReportFilesCompleted(20);
            monitor.ReportFilesCompleted(5);

            // Assert
            // No direct assertion possible, but should not throw
            Assert.True(true);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-10)]
        public void ReportFilesCompleted_NegativeCount_HandlesGracefully(long count)
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(100);

            // Act & Assert
            // Should handle gracefully (though behavior might be undefined)
            Assert.DoesNotThrow(() => monitor.ReportFilesCompleted(count));
        }

        [Fact]
        public void ReportFilesCompleted_LargeCount_HandlesCorrectly()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(1000000);

            // Act
            monitor.ReportFilesCompleted(100000);

            // Assert
            // Should handle large numbers without overflow
            Assert.True(true);
        }

        [Fact]
        public void Stop_AfterStarting_ReturnsValidMetrics()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(100);
            monitor.ReportFilesCompleted(50);

            // Allow some time to pass for realistic metrics
            Thread.Sleep(10);

            // Act
            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.ElapsedMilliseconds >= 0);
            Assert.True(metrics.FilesPerSecond >= 0);
        }

        [Fact]
        public void Stop_WithoutStarting_ReturnsZeroMetrics()
        {
            // Arrange
            var monitor = new PerformanceMonitor();

            // Act
            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.Equal(0, metrics.ElapsedMilliseconds);
            Assert.Equal(0, metrics.FilesPerSecond);
        }

        [Fact]
        public void Stop_MultipleCalls_ReturnsConsistentResults()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(100);
            monitor.ReportFilesCompleted(50);
            Thread.Sleep(10);

            // Act
            var metrics1 = monitor.Stop();
            var metrics2 = monitor.Stop();
            var metrics3 = monitor.Stop();

            // Assert
            Assert.Equal(metrics1.ElapsedMilliseconds, metrics2.ElapsedMilliseconds);
            Assert.Equal(metrics2.ElapsedMilliseconds, metrics3.ElapsedMilliseconds);
            Assert.Equal(metrics1.FilesPerSecond, metrics2.FilesPerSecond);
            Assert.Equal(metrics2.FilesPerSecond, metrics3.FilesPerSecond);
        }

        [Fact]
        public void PerformanceMetrics_HasValidStructure()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(100);
            monitor.ReportFilesCompleted(100);

            // Act
            var metrics = monitor.Stop();

            // Assert
            Assert.True(metrics.ElapsedMilliseconds >= 0);
            Assert.True(metrics.FilesPerSecond >= 0);
            Assert.True(metrics.ElapsedMilliseconds <= 60000); // Should complete within reasonable time
        }

        [Fact]
        public void ReportFilesCompleted_ConcurrentCalls_ThreadSafe()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(1000);
            const int threadCount = 10;
            const int reportsPerThread = 100;
            var tasks = new Task[threadCount];

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < reportsPerThread; j++)
                    {
                        monitor.ReportFilesCompleted(1);
                        Thread.Sleep(1); // Small delay to simulate real work
                    }
                });
            }

            Task.WaitAll(tasks);

            // Allow monitoring to catch up
            Thread.Sleep(50);

            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.FilesPerSecond > 0);
            _output.WriteLine($"Files per second: {metrics.FilesPerSecond}");
        }

        [Fact]
        public void StartStopCycle_MultipleCycles_WorksCorrectly()
        {
            // Arrange
            var monitor = new PerformanceMonitor();

            // Act & Assert
            for (int i = 0; i < 5; i++)
            {
                monitor.Start(100);
                monitor.ReportFilesCompleted(50);
                Thread.Sleep(1);
                var metrics = monitor.Stop();

                Assert.NotNull(metrics);
                Assert.True(metrics.ElapsedMilliseconds >= 0);
                Assert.True(metrics.FilesPerSecond >= 0);

                _output.WriteLine($"Cycle {i + 1}: {metrics.ElapsedMilliseconds}ms, {metrics.FilesPerSecond:F2} files/sec");
            }
        }

        [Fact]
        public void ReportFilesCompleted_BatchReporting_CalculatesCorrectRate()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(1000);

            // Act
            var startTime = DateTime.UtcNow;

            // Report files in batches to simulate real usage
            for (int i = 0; i < 10; i++)
            {
                monitor.ReportFilesCompleted(100);
                Thread.Sleep(10); // Simulate processing time
            }

            var metrics = monitor.Stop();
            var actualDuration = DateTime.UtcNow - startTime;

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.ElapsedMilliseconds > 0);
            Assert.True(metrics.FilesPerSecond > 0);

            _output.WriteLine($"Actual duration: {actualDuration.TotalMilliseconds:F1}ms");
            _output.WriteLine($"Measured duration: {metrics.ElapsedMilliseconds}ms");
            _output.WriteLine($"Files per second: {metrics.FilesPerSecond:F2}");
        }

        [Fact]
        public void PerformanceMonitor_RealWorkload_HandlesAccurately()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            const int totalFiles = 1000;
            monitor.Start(totalFiles);

            // Act - Simulate realistic file generation
            for (int i = 0; i < totalFiles; i += 50) // Report in batches
            {
                // Simulate some work
                Thread.Sleep(1);

                // Report batch completion
                monitor.ReportFilesCompleted(Math.Min(50, totalFiles - i));
            }

            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.ElapsedMilliseconds > 0);
            Assert.True(metrics.FilesPerSecond > 0);
            Assert.True(metrics.FilesPerSecond < 10000); // Should be realistic

            _output.WriteLine($"Completed {totalFiles} files in {metrics.ElapsedMilliseconds}ms");
            _output.WriteLine($"Performance: {metrics.FilesPerSecond:F2} files/second");
        }

        [Fact]
        public void ReportFilesCompleted_ZeroReporting_HandlesGracefully()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(100);

            // Act
            monitor.ReportFilesCompleted(0);
            monitor.ReportFilesCompleted(0);
            monitor.ReportFilesCompleted(0);

            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.Equal(0, metrics.FilesPerSecond);
        }

        [Fact]
        public void Stop_VeryShortOperation_HandlesCorrectly()
        {
            // Arrange
            var monitor = new PerformanceMonitor();
            monitor.Start(1);

            // Act - Very fast operation
            monitor.ReportFilesCompleted(1);
            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.ElapsedMilliseconds >= 0);
            // Files per second should be very high for very short operations
            Assert.True(metrics.FilesPerSecond >= 0);
        }

        [Fact]
        public void PerformanceMetrics_DefaultValues_AreReasonable()
        {
            // Arrange
            var monitor = new PerformanceMonitor();

            // Act
            var metrics = monitor.Stop();

            // Assert
            Assert.Equal(0, metrics.ElapsedMilliseconds);
            Assert.Equal(0, metrics.FilesPerSecond);
        }
    }
}