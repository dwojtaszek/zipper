using Xunit;

namespace Zipper
{
    public class PerformanceBenchmarkRunnerTests
    {
        [Fact]
        public async Task RunBenchmarks_WritesAllFourSections()
        {
            var output = await CaptureOutputAsync();

            Assert.Contains("Parallel vs Sequential", output);
            Assert.Contains("Memory Pool Performance", output);
            Assert.Contains("Scalability", output);
            Assert.Contains("Allocation Impact", output);
            Assert.Contains("Benchmark Suite Complete", output);
        }

        [Fact]
        public async Task RunBenchmarks_PrintsTimingMetrics()
        {
            var output = await CaptureOutputAsync();

            Assert.Contains("ms", output);
            Assert.Contains("files/sec", output);
            Assert.Contains("Status", output);
        }

        [Fact]
        public async Task RunBenchmarks_CompletesWithoutThrowing()
        {
            var exception = await Record.ExceptionAsync(() => PerformanceBenchmarkRunner.RunBenchmarks());
            Assert.Null(exception);
        }

        private static async Task<string> CaptureOutputAsync()
        {
            var originalOut = Console.Out;
            using var stringWriter = new StringWriter();
            Console.SetOut(stringWriter);

            try
            {
                await PerformanceBenchmarkRunner.RunBenchmarks();
            }
            finally
            {
                await stringWriter.FlushAsync();
                Console.SetOut(originalOut);
            }

            return stringWriter.ToString();
        }
    }
}
