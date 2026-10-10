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
        var timeProvider = new ManualTimeProvider();
        var monitor = new PerformanceMonitor(timeProvider);
        monitor.Start(5000);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        monitor.ReportFilesCompleted(4321);

        // Act
        monitor.Start(totalFiles);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var metrics = monitor.Stop();

        // Assert - Start begins a new operation: the completion count restarts and the total is the new one.
        Assert.Equal(0, monitor.GetCompletedCount());
        Assert.Equal(totalFiles, monitor.TotalFiles);
        Assert.Equal(1000, metrics.ElapsedMilliseconds);
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
        var timeProvider = new ManualTimeProvider();
        var monitor = new PerformanceMonitor(timeProvider);
        monitor.Start(100);
        monitor.ReportFilesCompleted(50);
        timeProvider.Advance(TimeSpan.FromSeconds(4));

        // Act
        var metrics = monitor.Stop();

        // Assert - every metric must be derived from the reported count and the measured elapsed
        // time. The rate numerator is asserted against FilesCompleted while TotalFiles differs
        // (100 vs 50), so a rate derived from the total instead of the completed count fails here.
        Assert.Equal(100, monitor.TotalFiles);
        Assert.Equal(50, metrics.FilesCompleted);
        Assert.Equal(4000, metrics.ElapsedMilliseconds);
        Assert.Equal(12.5, metrics.FilesPerSecond, 6);
        Assert.Equal(80, metrics.AverageTimePerFile, 6);
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
        var timeProvider = new ManualTimeProvider();
        var monitor = new PerformanceMonitor(timeProvider);
        monitor.Start(100);
        monitor.ReportFilesCompleted(50);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // Act
        var metrics1 = monitor.Stop();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var metrics2 = monitor.Stop();
        var metrics3 = monitor.Stop();

        // Assert
        Assert.Equal(1000, metrics1.ElapsedMilliseconds);
        Assert.Equal(metrics1.ElapsedMilliseconds, metrics2.ElapsedMilliseconds);
        Assert.Equal(metrics2.ElapsedMilliseconds, metrics3.ElapsedMilliseconds);
        Assert.Equal(metrics1.FilesPerSecond, metrics2.FilesPerSecond);
        Assert.Equal(metrics2.FilesPerSecond, metrics3.FilesPerSecond);
    }

    [Fact]
    public void Stop_AfterReportingAllFiles_DerivesEveryMetricFromCountAndElapsed()
    {
        var timeProvider = new ManualTimeProvider();
        var monitor = new PerformanceMonitor(timeProvider);
        monitor.Start(100);
        monitor.ReportFilesCompleted(100);
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        // Act
        var metrics = monitor.Stop();

        // Assert - a fully-reported run: the count is exact, the total is intact, and each rate
        // divides by the elapsed time.
        Assert.Equal(100, monitor.TotalFiles);
        Assert.Equal(100, metrics.FilesCompleted);
        Assert.Equal(2000, metrics.ElapsedMilliseconds);
        Assert.Equal(50, metrics.FilesPerSecond, 6);
        Assert.Equal(20, metrics.AverageTimePerFile, 6);
    }

    [Fact]
    public async Task ReportFilesCompleted_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var timeProvider = new ManualTimeProvider();
        var monitor = new PerformanceMonitor(timeProvider);
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
                }
            });
        }

        await Task.WhenAll(tasks);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var metrics = monitor.Stop();

        // Assert - every concurrent report must be counted exactly once.
        Assert.Equal(1000, metrics.FilesCompleted);
        Assert.Equal(1000, monitor.GetCompletedCount());
        Assert.Equal(1000, metrics.ElapsedMilliseconds);
        Assert.Equal(1000, metrics.FilesPerSecond, 6);
        Assert.Equal(1, metrics.AverageTimePerFile, 6);
        this.output.WriteLine($"Files per second: {metrics.FilesPerSecond}");
    }

    [Fact]
    public void StartStopCycle_MultipleCycles_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new ManualTimeProvider();
        var monitor = new PerformanceMonitor(timeProvider);

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            monitor.Start(100);
            monitor.ReportFilesCompleted(50);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            var metrics = monitor.Stop();

            Assert.Equal(100, monitor.TotalFiles);
            Assert.Equal(50, metrics.FilesCompleted);
            Assert.Equal(1000, metrics.ElapsedMilliseconds);
            Assert.Equal(50, metrics.FilesPerSecond, 6);
            Assert.Equal(20, metrics.AverageTimePerFile, 6);

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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref this.timestamp);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref this.timestamp, duration.Ticks);
    }
}
