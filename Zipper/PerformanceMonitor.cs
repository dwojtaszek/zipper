// <copyright file="PerformanceMonitor.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace Zipper
{
    /// <summary>
    /// Monitors and reports performance metrics during file generation.
    /// Consolidates progress tracking and performance metrics into a single class.
    /// </summary>
    public class PerformanceMonitor
    {
        private readonly Stopwatch stopwatch = new Stopwatch();
        private long filesCompleted;
        private long totalFiles;
        private DateTime lastProgressUpdate = DateTime.UtcNow;
        private double lastDisplayedPercentage;

        /// <summary>
        /// Starts performance monitoring for a new operation.
        /// </summary>
        /// <param name="totalFiles">Total number of files to process.</param>
        public void Start(long totalFiles)
        {
            this.totalFiles = totalFiles;
            this.filesCompleted = 0;
            this.lastDisplayedPercentage = 0;
            this.lastProgressUpdate = DateTime.UtcNow;
            this.stopwatch.Restart();
        }

        /// <summary>
        /// Report completion of a batch of files.
        /// </summary>
        /// <param name="count">Number of files completed in this batch.</param>
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

        /// <summary>
        /// Get current completion count.
        /// </summary>
        /// <returns>Number of files completed so far.</returns>
        public long GetCompletedCount()
        {
            return Interlocked.Read(ref this.filesCompleted);
        }

        /// <summary>
        /// Force immediate progress report.
        /// </summary>
        /// <param name="completed">Number of files completed.</param>
        /// <param name="total">Total number of files.</param>
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

            // Only display progress when there's a meaningful change (at least 1% or completion)
            if (percentage > this.lastDisplayedPercentage + 1 || percentage >= 100)
            {
                this.lastDisplayedPercentage = percentage;
                Console.Write($"\rProgress: {completed:N0} / {total:N0} files ({percentage:F1}%) - {rate:F1} files/sec - ETA: {eta:hh\\:mm\\:ss}          ");
            }
        }

        /// <summary>
        /// Write a final newline to clean up progress display.
        /// </summary>
        public void FinalizeProgress()
        {
            // Disable in CI environments
            if (Environment.GetEnvironmentVariable("CI") == "true")
            {
                return;
            }

            Console.WriteLine(); // New line after progress
        }

        /// <summary>
        /// Stops monitoring and returns performance metrics.
        /// </summary>
        /// <returns>Performance metrics for the completed operation.</returns>
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

    /// <summary>
    /// Performance metrics captured during file generation.
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// Gets or sets the total elapsed time in milliseconds.
        /// </summary>
        public double ElapsedMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the number of files completed.
        /// </summary>
        public long FilesCompleted { get; set; }

        /// <summary>
        /// Gets or sets the files processed per second.
        /// </summary>
        public double FilesPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the average time per file in milliseconds.
        /// </summary>
        public double AverageTimePerFile { get; set; }
    }
}
