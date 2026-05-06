using Xunit;
using Zipper.Cli;

namespace Zipper.Tests
{
    [Collection("ConsoleTests")]
    public class HelpTextGeneratorTests
    {
        [Fact]
        public void Show_DisplaysUsageInformation()
        {
            var originalError = Console.Error;
            var errorOutput = new StringWriter();
            Console.SetError(errorOutput);

            try
            {
                HelpTextGenerator.Show();

                var output = errorOutput.ToString();
                Assert.Contains("Error: Missing required arguments.", output);
                Assert.Contains("Usage:", output);
                Assert.Contains("--type <pdf|jpg|tiff|eml|docx|xlsx>", output);
                Assert.Contains("--count <number>", output);
                Assert.Contains("--output-path <path>", output);
                Assert.Contains("Optional Arguments:", output);
                Assert.Contains("Required Arguments:", output);
                Assert.Contains("Load File Options:", output);
                Assert.Contains("Loadfile-Only Options:", output);
                Assert.Contains("Chaos Engine Options:", output);
                Assert.Contains("Production Set Options:", output);
                Assert.Contains("Bates Numbering:", output);
                Assert.Contains("TIFF Options:", output);
                Assert.Contains("Column Profile Options:", output);
                Assert.Contains("Utility Options:", output);
            }
            finally
            {
                Console.SetError(originalError);
                errorOutput.Dispose();
            }
        }
    }
}
