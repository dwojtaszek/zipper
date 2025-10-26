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

        /// <summary>
        /// Force immediate progress report
        /// </summary>
        /// <param name="completed">Number of files completed</param>
        /// <param name="total">Total number of files</param>
        public static void ReportProgress(long completed, long total)
        {
            var percentage = total > 0 ? (double)completed / total * 100 : 0;
            var elapsed = DateTime.UtcNow - s_lastProgressUpdate;
            var rate = elapsed.TotalSeconds > 0 ? completed / elapsed.TotalSeconds : 0;
            var eta = rate > 0 ? TimeSpan.FromSeconds((total - completed) / rate) : TimeSpan.Zero;

            Console.Write($"\rProgress: {completed:N0} / {total:N0} files ({percentage:F1}%) - {rate:F1} files/sec - ETA: {eta:hh\\:mm\\:ss}");
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