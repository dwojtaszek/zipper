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
            Assert.Contains("smoke, compatibility, malformed, security, encoding, all", output, StringComparison.Ordinal);
            Assert.Contains("Production Set Options:", output, StringComparison.Ordinal);
            Assert.Contains("--redacted-production", output, StringComparison.Ordinal);
            Assert.Contains("--withheld-native-policy", output, StringComparison.Ordinal);
            Assert.Contains(
                "  --production-id <string> Configurable production ID (requires --production-set; non-empty; supports lists, defaults to auto-incrementing/timestamp), max 250 UTF-8 bytes per generated element, single safe path segment per element",
                output,
                StringComparison.Ordinal);
            Assert.Contains("Bates Numbering:", output, StringComparison.Ordinal);
            Assert.Contains(
                "  --bates-prefix <string>  Bates Number prefix, max 200 UTF-8 bytes per list element (e.g., CLIENT001)",
                output,
                StringComparison.Ordinal);
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

    [Fact]
    public void Show_MarksDeflate64Bzip2AsNotYetSupported()
    {
        var originalError = Console.Error;
        var errorOutput = new StringWriter();
        Console.SetError(errorOutput);

        try
        {
            HelpTextGenerator.Show();

            var output = errorOutput.ToString();
            var compressionLine = output
                .Split('\n')
                .FirstOrDefault(l => l.IndexOf("--compression", StringComparison.Ordinal) >= 0);

            Assert.NotNull(compressionLine);
            Assert.True(compressionLine.IndexOf("not yet supported", StringComparison.OrdinalIgnoreCase) >= 0,
                $"Expected deflate64/bzip2 marked not yet supported in help compression line: {compressionLine}");
        }
        finally
        {
            Console.SetError(originalError);
            errorOutput.Dispose();
        }
    }
}
