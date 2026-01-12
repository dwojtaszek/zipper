// <copyright file="PerformanceMonitor.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace Zipper
{
    /// <summary>
    /// Monitors and reports performance metrics during file generation.
    /// </summary>
    public class PerformanceMonitor
    {
        private readonly Stopwatch stopwatch = new Stopwatch();
        private long filesCompleted;
        private long totalFiles;
        private DateTime lastProgressUpdate = DateTime.UtcNow;

        public void Start(long totalFiles)
        {
            this.totalFiles = totalFiles;
            this.filesCompleted = 0;
            this.stopwatch.Restart();
        }

        public void ReportFilesCompleted(long count)
        {
            Interlocked.Add(ref this.filesCompleted, count);

            var now = DateTime.UtcNow;
            if ((now - this.lastProgressUpdate).TotalMilliseconds >= 100) // Update every 100ms
            {
                this.ReportProgress(this.filesCompleted, this.totalFiles);
                this.lastProgressUpdate = now;
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
            var elapsed = this.stopwatch.Elapsed;
            var rate = elapsed.TotalSeconds > 0 ? completed / elapsed.TotalSeconds : 0;
            var eta = rate > 0 ? TimeSpan.FromSeconds((total - completed) / rate) : TimeSpan.Zero;

            Console.Write($"\rProgress: {completed:N0} / {total:N0} files ({percentage:F1}%) - {rate:F1} files/sec - ETA: {eta:hh\\:mm\\:ss}");
        }

        public PerformanceMetrics Stop()
        {
            this.stopwatch.Stop();

            var elapsed = this.stopwatch.Elapsed;
            var rate = elapsed.TotalSeconds > 0 ? this.filesCompleted / elapsed.TotalSeconds : 0;

            return new PerformanceMetrics
            {
                ElapsedMilliseconds = elapsed.TotalMilliseconds,
                FilesCompleted = this.filesCompleted,
                FilesPerSecond = rate,
                AverageTimePerFile = elapsed.TotalMilliseconds / Math.Max(this.filesCompleted, 1),
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
