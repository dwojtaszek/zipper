using System;
using System.Threading;

namespace Zipper
{
    /// <summary>
    /// Tracks and reports progress during file generation operations
    /// </summary>
    public static class ProgressTracker
    {
        private static long s_filesCompleted;
        private static long s_totalFiles;
        private static DateTime s_lastProgressUpdate = DateTime.UtcNow;
        private static readonly object s_lock = new object();

        /// <summary>
        /// Initialize progress tracking for a new operation
        /// </summary>
        /// <param name="totalFiles">Total number of files to process</param>
        public static void Initialize(long totalFiles)
        {
            lock (s_lock)
            {
                s_totalFiles = totalFiles;
                s_filesCompleted = 0;
                s_lastProgressUpdate = DateTime.UtcNow;
                s_lastDisplayedPercentage = 0;
            }
        }

        /// <summary>
        /// Report completion of a batch of files
        /// </summary>
        /// <param name="count">Number of files completed in this batch</param>
        public static void ReportFilesCompleted(long count)
        {
            Interlocked.Add(ref s_filesCompleted, count);

            var now = DateTime.UtcNow;
            if ((now - s_lastProgressUpdate).TotalMilliseconds >= 100) // Update every 100ms
            {
                ReportProgress(s_filesCompleted, s_totalFiles);
                s_lastProgressUpdate = now;
            }
        }

        /// <summary>
        /// Report completion of a single file
        /// </summary>
        /// <param name="fileName">Name of the completed file (for logging)</param>
        public static void ReportFileGenerated(string fileName)
        {
            Interlocked.Increment(ref s_filesCompleted);

            var now = DateTime.UtcNow;
            if ((now - s_lastProgressUpdate).TotalMilliseconds >= 100)
            {
                ReportProgress(s_filesCompleted, s_totalFiles);
                s_lastProgressUpdate = now;
            }
        }

        private static readonly Random s_random = new Random();
        private static double s_lastDisplayedPercentage = 0;

        /// <summary>
        /// Force immediate progress report
        /// </summary>
        /// <param name="completed">Number of files completed</param>
        /// <param name="total">Total number of files</param>
        public static void ReportProgress(long completed, long total)
        {
            // Disable progress bar in CI environments to prevent hangs
            if (Environment.GetEnvironmentVariable("CI") == "true")
            {
                return;
            }

            var actualPercentage = total > 0 ? (double)completed / total * 100 : 0;
            var elapsed = DateTime.UtcNow - s_lastProgressUpdate;
            var rate = elapsed.TotalSeconds > 0 ? completed / elapsed.TotalSeconds : 0;
            var eta = rate > 0 ? TimeSpan.FromSeconds((total - completed) / rate) : TimeSpan.Zero;

            // Only display progress when there's a meaningful change (up to 5% increments)
            var displayPercentage = CalculateDisplayPercentage(actualPercentage);

            if (displayPercentage > s_lastDisplayedPercentage || actualPercentage >= 100)
            {
                s_lastDisplayedPercentage = displayPercentage;
                var percentageToDisplay = Math.Min(displayPercentage, 100);
                Console.Write($"\rProgress: {percentageToDisplay:F1}% - {rate:F1} files/sec - ETA: {eta:hh\\:mm\\:ss}                     ");
            }
        }

        /// <summary>
        /// Calculate display percentage with random increments up to 5%
        /// </summary>
        /// <param name="actualPercentage">The actual completion percentage</param>
        /// <returns>Display percentage with randomized increments</returns>
        private static double CalculateDisplayPercentage(double actualPercentage)
        {
            if (actualPercentage >= 100)
                return 100;

            // Calculate the next display threshold based on last displayed percentage
            var nextThreshold = Math.Ceiling(s_lastDisplayedPercentage / 5.0) * 5.0;

            // Add random variation within the 5% range (1-5% increments)
            if (actualPercentage >= nextThreshold)
            {
                var maxIncrement = Math.Min(5.0, 100 - nextThreshold);
                var randomIncrement = s_random.NextDouble() * maxIncrement + 0.5; // 0.5% to 5.5%
                return Math.Min(nextThreshold + randomIncrement, actualPercentage);
            }

            return s_lastDisplayedPercentage;
        }

        /// <summary>
        /// Get current completion count
        /// </summary>
        /// <returns>Number of files completed so far</returns>
        public static long GetCompletedCount()
        {
            return Interlocked.Read(ref s_filesCompleted);
        }

        /// <summary>
        /// Reset progress tracking (typically called at operation completion)
        /// </summary>
        public static void Reset()
        {
            lock (s_lock)
            {
                s_filesCompleted = 0;
                s_totalFiles = 0;
                s_lastProgressUpdate = DateTime.UtcNow;
                s_lastDisplayedPercentage = 0;
            }
        }

        /// <summary>
        /// Write a final newline to clean up progress display
        /// </summary>
        public static void FinalizeProgress()
        {
            Console.WriteLine(); // New line after progress
        }
    }
}