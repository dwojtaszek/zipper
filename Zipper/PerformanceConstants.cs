namespace Zipper
{
    public static class PerformanceConstants
    {
        /// <summary>
        /// Default degree of parallelism for file generation
        /// </summary>
        public static readonly int DefaultConcurrency = Environment.ProcessorCount;

        /// <summary>
        /// Default buffer size for I/O operations (64KB)
        /// </summary>
        public static readonly int DefaultBufferSize = 65536;

        /// <summary>
        /// Maximum memory pool size (100MB)
        /// </summary>
        public static readonly long MaxPoolSize = 100 * 1024 * 1024;

        /// <summary>
        /// Batch size for progress reporting
        /// </summary>
        public static readonly int ProgressBatchSize = 1000;

        /// <summary>
        /// Maximum padding per file to prevent memory exhaustion
        /// </summary>
        public static readonly long MaxPaddingPerFile = 100 * 1024 * 1024; // 100MB
    }
}
