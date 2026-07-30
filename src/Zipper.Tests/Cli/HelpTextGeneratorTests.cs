using Xunit;
using Zipper.Cli;

namespace Zipper.Tests;

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
            Assert.Contains("Error: Missing required arguments.", output, StringComparison.Ordinal);
            Assert.Contains("Usage:", output, StringComparison.Ordinal);
            Assert.Contains("--type <pdf|jpg|tiff|eml|docx|xlsx>", output, StringComparison.Ordinal);
            Assert.Contains("--count <number>", output, StringComparison.Ordinal);
            Assert.Contains("--output-path <path>", output, StringComparison.Ordinal);
            Assert.Contains("Optional Arguments:", output, StringComparison.Ordinal);
            Assert.Contains("Required Arguments:", output, StringComparison.Ordinal);
            Assert.Contains("Load File Options:", output, StringComparison.Ordinal);
            Assert.Contains("Loadfile-Only Options:", output, StringComparison.Ordinal);
            Assert.Contains("Chaos Engine Options:", output, StringComparison.Ordinal);
            Assert.Contains("Production Set Options:", output, StringComparison.Ordinal);
            Assert.Contains("Bates Numbering:", output, StringComparison.Ordinal);
            Assert.Contains("TIFF Options:", output, StringComparison.Ordinal);
            Assert.Contains("Column Profile Options:", output, StringComparison.Ordinal);
            Assert.Contains("Utility Options:", output, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
            errorOutput.Dispose();
        }
    }
}
