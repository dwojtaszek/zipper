// <copyright file="PerformanceMonitorTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Xunit;
using Xunit.Abstractions;

namespace Zipper
{
    public class PerformanceMonitorTests
    {
        private readonly ITestOutputHelper output;

        public PerformanceMonitorTests(ITestOutputHelper output)
        {
            this.output = output;
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
            monitor.Start(totalFiles); // Should not throw
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
        public async Task ReportFilesCompleted_ConcurrentCalls_ThreadSafe()
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

            await Task.WhenAll(tasks);

            // Allow monitoring to catch up
            Thread.Sleep(50);

            var metrics = monitor.Stop();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.FilesPerSecond > 0);
            this.output.WriteLine($"Files per second: {metrics.FilesPerSecond}");
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

                this.output.WriteLine($"Cycle {i + 1}: {metrics.ElapsedMilliseconds}ms, {metrics.FilesPerSecond:F2} files/sec");
            }
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
