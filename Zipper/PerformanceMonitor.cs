using System;
using System.Diagnostics;
using System.Threading;

namespace Zipper
{
    /// <summary>
    /// Monitors and reports performance metrics during file generation
    /// </summary>
    public class PerformanceMonitor
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private long _filesCompleted;
        private long _totalFiles;
        private DateTime _lastProgressUpdate = DateTime.UtcNow;

        public void Start(long totalFiles)
        {
            _totalFiles = totalFiles;
            _filesCompleted = 0;
            _stopwatch.Restart();
        }

        public void ReportFilesCompleted(long count)
        {
            Interlocked.Add(ref _filesCompleted, count);

            var now = DateTime.UtcNow;
            if ((now - _lastProgressUpdate).TotalMilliseconds >= 100) // Update every 100ms
            {
                ReportProgress(_filesCompleted, _totalFiles);
                _lastProgressUpdate = now;
            }
        }

        public void ReportProgress(long completed, long total)
        {
            // Disable progress bar in CI environments to prevent hangs
            if (Environment.GetEnvironmentVariable("CI") == "true")
            {
                return;
            }

            var percentage = total > 0 ? (double)completed / total * 100 : 0;
            var elapsed = _stopwatch.Elapsed;
            var rate = elapsed.TotalSeconds > 0 ? completed / elapsed.TotalSeconds : 0;
            var eta = rate > 0 ? TimeSpan.FromSeconds((total - completed) / rate) : TimeSpan.Zero;

            Console.Write($"\rProgress: {completed:N0} / {total:N0} files ({percentage:F1}%) - {rate:F1} files/sec - ETA: {eta:hh\\:mm\\:ss}");
        }

        public PerformanceMetrics Stop()
        {
            _stopwatch.Stop();

            var elapsed = _stopwatch.Elapsed;
            var rate = elapsed.TotalSeconds > 0 ? _filesCompleted / elapsed.TotalSeconds : 0;

            return new PerformanceMetrics
            {
                ElapsedMilliseconds = elapsed.TotalMilliseconds,
                FilesCompleted = _filesCompleted,
                FilesPerSecond = rate,
                AverageTimePerFile = elapsed.TotalMilliseconds / Math.Max(_filesCompleted, 1)
            };
        }
    }

    public class PerformanceMetrics
    {
        public double ElapsedMilliseconds { get; set; }
        public long FilesCompleted { get; set; }
        public double FilesPerSecond { get; set; }
        public double AverageTimePerFile { get; set; }
    }
}
