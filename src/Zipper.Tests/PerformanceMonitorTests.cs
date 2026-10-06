using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests;

public class PerformanceMonitorTests
{
    private readonly ITestOutputHelper output;

    public PerformanceMonitorTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Constructor_NewMonitor_StartsWithZeroedTrackingState()
    {
        // Act
        var monitor = new PerformanceMonitor();

        // Assert - a monitor that has never been started tracks nothing.
        Assert.Equal(0, monitor.TotalFiles);
        Assert.Equal(0, monitor.GetCompletedCount());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Start_ValidTotalFiles_ResetsCompletionCountAndRecordsTotal(long totalFiles)
    {
        // Arrange
        var monitor = new PerformanceMonitor();
        monitor.Start(5000);
        monitor.ReportFilesCompleted(4321);

        // Act
        monitor.Start(totalFiles);

        // Assert - Start begins a new operation: the completion count restarts and the total is the new one.
        Assert.Equal(0, monitor.GetCompletedCount());
        Assert.Equal(totalFiles, monitor.TotalFiles);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Start_InvalidTotalFiles_StoresTotalVerbatimAndLeavesCompletionCountAtZero(long totalFiles)
    {
        // Arrange - report first, so the completion count is non-zero before Start runs. Asserting
        // zero against a never-used monitor would pass no matter what Start does.
        var monitor = new PerformanceMonitor();
        monitor.Start(10);
        monitor.ReportFilesCompleted(7);

        // Act
        monitor.Start(totalFiles);

        // Assert - Start must not clamp or throw on non-positive totals, and it must still reset
        // completion tracking for the new (non-positive) total.
        Assert.Equal(totalFiles, monitor.TotalFiles);
        Assert.Equal(0, monitor.GetCompletedCount());
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

        // Assert - every metric must be derived from the reported count and the measured elapsed
        // time. The rate numerator is asserted against FilesCompleted while TotalFiles differs
        // (100 vs 50), so a rate derived from the total instead of the completed count fails here.
        Assert.Equal(100, monitor.TotalFiles);
        Assert.Equal(50, metrics.FilesCompleted);
        Assert.True(metrics.ElapsedMilliseconds > 0, "Elapsed time must advance after a 10ms pause");
        Assert.True(metrics.ElapsedMilliseconds <= 60000, "A 10ms pause must not report a runaway elapsed time");
        Assert.Equal(metrics.FilesCompleted / (metrics.ElapsedMilliseconds / 1000), metrics.FilesPerSecond, 6);
        Assert.Equal(metrics.ElapsedMilliseconds / 50, metrics.AverageTimePerFile, 6);
    }

    [Fact]
    public void Stop_WithoutStarting_ReturnsZeroMetrics()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act
        var metrics = monitor.Stop();

        // Assert
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
    public void Stop_AfterReportingAllFiles_DerivesEveryMetricFromCountAndElapsed()
    {
        // Arrange
        var monitor = new PerformanceMonitor();
        monitor.Start(100);
        monitor.ReportFilesCompleted(100);
        Thread.Sleep(10);

        // Act
        var metrics = monitor.Stop();

        // Assert - a fully-reported run: the count is exact, the total is intact, and each rate
        // divides by the elapsed time.
        Assert.Equal(100, monitor.TotalFiles);
        Assert.Equal(100, metrics.FilesCompleted);
        Assert.True(metrics.ElapsedMilliseconds > 0, "Elapsed time must advance after a 10ms pause");
        Assert.True(metrics.ElapsedMilliseconds <= 60000, "A 10ms pause must not report a runaway elapsed time");
        Assert.Equal(metrics.FilesCompleted / (metrics.ElapsedMilliseconds / 1000), metrics.FilesPerSecond, 6);
        Assert.Equal(metrics.ElapsedMilliseconds / 100, metrics.AverageTimePerFile, 6);
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

        // Assert - every concurrent report must be counted exactly once.
        Assert.Equal(1000, metrics.FilesCompleted);
        Assert.Equal(1000, monitor.GetCompletedCount());
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

            Assert.Equal(100, monitor.TotalFiles);
            Assert.Equal(50, metrics.FilesCompleted);
            Assert.True(metrics.ElapsedMilliseconds > 0, "Elapsed time must advance after a 1ms pause");
            Assert.True(metrics.ElapsedMilliseconds <= 60000, "A 1ms pause must not report a runaway elapsed time");
            Assert.True(metrics.FilesPerSecond > 0, "Throughput must be positive for a run that reported files");

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

    [Fact]
    public void GetCompletedCount_AfterReporting_ReturnsCorrectCount()
    {
        // Arrange
        var monitor = new PerformanceMonitor();
        monitor.Start(1000);

        // Act
        monitor.ReportFilesCompleted(100);
        monitor.ReportFilesCompleted(200);
        var completed = monitor.GetCompletedCount();

        // Assert
        Assert.Equal(300, completed);
    }

    [Fact]
    public void GetCompletedCount_AfterStart_ReturnsZero()
    {
        // Arrange
        var monitor = new PerformanceMonitor();
        monitor.Start(1000);

        // Act
        var completed = monitor.GetCompletedCount();

        // Assert
        Assert.Equal(0, completed);
    }

    [Fact]
    public void FinalizeProgress_AfterFullReport_LeavesCompletionStateUntouched()
    {
        // Arrange
        var monitor = new PerformanceMonitor();
        monitor.Start(100);
        monitor.ReportFilesCompleted(100);

        // Act
        monitor.FinalizeProgress();

        // Assert - FinalizeProgress only closes the progress display; it must not alter tracking
        // state. Both values are pinned independently so that resetting both to zero fails.
        Assert.Equal(100, monitor.TotalFiles);
        Assert.Equal(100, monitor.GetCompletedCount());
    }
}
