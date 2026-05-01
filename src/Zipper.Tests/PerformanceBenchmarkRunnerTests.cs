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
            var outputPath = Path.Combine(Path.GetTempPath(), $"bench_out_{Guid.NewGuid()}.txt");
            try
            {
                var originalOut = Console.Out;

                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var streamWriter = new StreamWriter(fileStream))
                {
                    Console.SetOut(streamWriter);
                    try
                    {
                        await PerformanceBenchmarkRunner.RunBenchmarks();
                    }
                    finally
                    {
                        await streamWriter.FlushAsync();
                        Console.SetOut(originalOut);
                    }
                }

                return await File.ReadAllTextAsync(outputPath);
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    try
                    {
                        File.Delete(outputPath);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
