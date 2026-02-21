namespace Zipper
{
    public static class PerformanceConstants
    {
        /// <summary>
        /// Default buffer size for I/O operations (64KB).
        /// </summary>
        public const int DefaultBufferSize = 65536;

        /// <summary>
        /// Maximum memory pool size (100MB).
        /// </summary>
        public const long MaxPoolSize = 100 * 1024 * 1024;

        /// <summary>
        /// Batch size for progress reporting.
        /// </summary>
        public const int ProgressBatchSize = 1000;

        /// <summary>
        /// Maximum padding per file to prevent memory exhaustion.
        /// </summary>
        public const long MaxPaddingPerFile = 100 * 1024 * 1024; // 100MB

        /// <summary>
        /// Default degree of parallelism for file generation.
        /// </summary>
        public static readonly int DefaultConcurrency = Environment.ProcessorCount / 2;
    }
}
