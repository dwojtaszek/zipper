using System;
using System.Threading.Tasks;
using Xunit;

namespace Zipper
{
    public class PerformanceMonitorTests
    {
        [Fact]
        public async Task StartAndStop_ShouldMeasureTime()
        {
            var monitor = new PerformanceMonitor();

            monitor.Start(100);
            await Task.Delay(100);
            var metrics = monitor.Stop();

            Assert.True(metrics.ElapsedMilliseconds >= 100);
            Assert.True(metrics.FilesPerSecond >= 0);
        }

        [Fact]
        public void ReportProgress_ShouldNotThrow()
        {
            var monitor = new PerformanceMonitor();

            var exception = Record.Exception(() =>
                monitor.ReportProgress(50, 100));

            Assert.Null(exception);
        }
    }
}